using System.Globalization;

namespace GsxIflySync.Core;

public enum SyncState
{
    Inactive,
    Running,
    Succeeded,
    Impossible,
}

public sealed record WriteCheck(PlannedWrite Write, VarReading Readback, bool Ok, string Detail);

public sealed record SyncResult(SyncState State, string Message, IReadOnlyList<WriteCheck> Checks);

/// <summary>
/// Orchestration : lectures GSX + iFly, stabilité, détection de chargement externe,
/// exécution confirmée écriture par écriture, puis relecture.
/// </summary>
public sealed class SyncService
{
    private readonly IVariableSource _source;
    private readonly GuardedWriter _writer;
    private readonly SyncConfig _config;
    private readonly ILog _log;
    private readonly StabilityTracker _boardedStability;
    private readonly Func<DateTimeOffset> _clock;
    private GsxServiceState _lastBoardingState = GsxServiceState.Unknown;
    private double? _paxKgAtBoardingStart;
    private bool _externalPayloadChange;
    private bool _writing;

    public SyncService(IVariableSource source, GuardedWriter writer, SyncConfig config, ILog log, Func<DateTimeOffset>? clock = null)
    {
        _source = source;
        _writer = writer;
        _config = config;
        _log = log;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _boardedStability = new StabilityTracker(TimeSpan.FromSeconds(config.StabilitySeconds));
        Gsx = new GsxReader(source);
        Ifly = new IflyAdapter(source, config.Ifly);
        Planner = new SyncPlanner(config);
    }

    public GsxReader Gsx { get; }

    public IflyAdapter Ifly { get; }

    public SyncPlanner Planner { get; }

    public SyncState State { get; private set; } = SyncState.Inactive;

    public void RegisterAll()
    {
        Gsx.RegisterAll();
        Ifly.RegisterAll();
    }

    /// <summary>À appeler à chaque rafraîchissement : met à jour stabilité et détection externe.</summary>
    public SyncInputs Observe()
    {
        var now = _clock();
        var gsx = Gsx.Read();
        var ifly = Ifly.Read();

        _boardedStability.Observe(gsx.Available ? gsx.BoardedTotal : null, now);

        if (gsx.BoardingState != _lastBoardingState)
        {
            _log.Info($"GSX embarquement : {GsxSnapshot.Describe(_lastBoardingState)} -> {GsxSnapshot.Describe(gsx.BoardingState)}");

            // Point de référence de la masse passagers au début de l'embarquement.
            if (gsx.BoardingState is GsxServiceState.Requested or GsxServiceState.Performing
                && _lastBoardingState is not (GsxServiceState.Requested or GsxServiceState.Performing))
            {
                _paxKgAtBoardingStart = ifly.PassengerStationsKg;
                _externalPayloadChange = false;
                _log.Info($"référence masse passagers au début d'embarquement : {Kg(_paxKgAtBoardingStart)}");
            }

            _lastBoardingState = gsx.BoardingState;
        }

        if (!_writing && _paxKgAtBoardingStart is { } start && ifly.PassengerStationsKg is { } current
            && Math.Abs(current - start) > _config.MassToleranceKg && !_externalPayloadChange)
        {
            _externalPayloadChange = true;
            _log.Warn($"masse passagers modifiée hors outil : {start:0} kg -> {current:0} kg (double chargement possible)");
        }

        return new SyncInputs(gsx, ifly, _boardedStability.IsStable(now), _externalPayloadChange);
    }

    public SyncPlan Plan() => Planner.Plan(Observe());

    /// <summary>
    /// Exécute un plan prêt. <paramref name="confirm"/> est appelé avant CHAQUE écriture ;
    /// un refus arrête tout. Les valeurs sont relues après <see cref="SyncConfig.ReadbackDelayMs"/>.
    /// </summary>
    public async Task<SyncResult> ExecuteAsync(SyncPlan plan, Func<PlannedWrite, bool> confirm, CancellationToken token = default)
    {
        if (plan.Outcome != SyncOutcome.Ready)
        {
            var state = plan.Outcome switch
            {
                SyncOutcome.NothingToDo => SyncState.Succeeded,
                SyncOutcome.Impossible => SyncState.Impossible,
                _ => SyncState.Inactive,
            };
            return Finish(state, plan.Reason, []);
        }

        State = SyncState.Running;
        _writing = true;
        try
        {
            var sent = new List<PlannedWrite>();
            foreach (var write in plan.Writes)
            {
                if (!confirm(write))
                {
                    _log.Warn($"écriture annulée par l'utilisateur : {write.Description}");
                    return Finish(
                        SyncState.Impossible,
                        sent.Count == 0 ? "annulé avant toute écriture" : $"annulé après {sent.Count} écriture(s) : vérifier l'iFly",
                        []);
                }

                var error = _writer.Write(write.Var, write.Value);
                if (error is not null)
                {
                    return Finish(SyncState.Impossible, $"écriture refusée ou en échec ({write.Var.Name}) : {error}", []);
                }

                sent.Add(write);
            }

            await Task.Delay(_config.ReadbackDelayMs, token).ConfigureAwait(true);
            var checks = Verify(sent);
            var ok = checks.All(c => c.Ok);

            var paxKg = Ifly.Read().PassengerStationsKg;
            var massLine = plan.ExpectedPassengerKg is { } expected && paxKg is { } got
                ? string.Create(CultureInfo.InvariantCulture, $" ; masse passagers relue {got:0.0} kg / attendue {expected:0.0} kg (écart {got - expected:+0.0;-0.0;0.0} kg)")
                : "";

            // Une fois nos écritures relues, elles deviennent la nouvelle référence.
            _paxKgAtBoardingStart = paxKg;

            return ok
                ? Finish(SyncState.Succeeded, "valeurs relues conformes" + massLine, checks)
                : Finish(SyncState.Impossible, "relecture non conforme : l'iFly n'a pas retenu la valeur" + massLine, checks);
        }
        finally
        {
            _writing = false;
        }
    }

    internal List<WriteCheck> Verify(IEnumerable<PlannedWrite> writes)
    {
        var checks = new List<WriteCheck>();
        foreach (var w in writes)
        {
            var r = _source.Get(w.Var);
            WriteCheck check;
            if (r.Status != VarStatus.Available || r.Value is null)
            {
                check = new WriteCheck(w, r, false, $"relecture impossible : {r.Status} ({r.Detail})");
            }
            else
            {
                var delta = r.Value.Value - w.Value;
                var ok = Math.Abs(delta) <= w.Tolerance;
                check = new WriteCheck(w, r, ok, string.Create(CultureInfo.InvariantCulture, $"attendu {w.Value:0.###}, relu {r.Value.Value:0.###}, écart {delta:+0.###;-0.###;0}"));
            }

            (check.Ok ? (Action<string>)_log.Info : _log.Warn)($"relecture {w.Var.Name} : {check.Detail}");
            checks.Add(check);
        }

        return checks;
    }

    private SyncResult Finish(SyncState state, string message, IReadOnlyList<WriteCheck> checks)
    {
        State = state;
        _log.Info($"synchronisation {state} : {message}");
        return new SyncResult(state, message, checks);
    }

    private static string Kg(double? kg) => kg is { } v ? v.ToString("0.0 'kg'", CultureInfo.InvariantCulture) : "indisponible";
}
