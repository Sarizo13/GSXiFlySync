using System.Globalization;

namespace GsxIflySync.Core;

public enum SyncOutcome
{
    /// <summary>Conditions non réunies pour l'instant (lecture seule, embarquement en cours…).</summary>
    Blocked,

    /// <summary>L'iFly porte déjà la bonne valeur : rien à écrire.</summary>
    NothingToDo,

    /// <summary>L'écriture n'est pas possible de façon sûre : écart à corriger à la main.</summary>
    Impossible,

    /// <summary>Écritures prêtes, chacune soumise à confirmation.</summary>
    Ready,
}

public sealed record PlannedWrite(VarSpec Var, double Value, double Tolerance, string Description);

public sealed record SyncPlan(
    SyncOutcome Outcome,
    string Reason,
    IReadOnlyList<PlannedWrite> Writes,
    int? TargetPassengers = null,
    double? ExpectedPassengerKg = null);

public sealed record SyncInputs(
    GsxSnapshot Gsx,
    IflySnapshot Ifly,
    bool GsxStable,
    bool ExternalPayloadChange);

/// <summary>
/// Décide s'il faut écrire, et quoi. Logique pure, sans accès au simulateur :
/// c'est elle que couvrent les tests unitaires.
/// </summary>
public sealed class SyncPlanner(SyncConfig config)
{
    public SyncPlan Plan(SyncInputs input)
    {
        var gsx = input.Gsx;
        var ifly = input.Ifly;

        if (config.ReadOnly)
        {
            return Blocked("mode lecture seule actif");
        }

        if (!config.SyncEnabled)
        {
            return Blocked("synchronisation désactivée dans config.json (syncEnabled = false)");
        }

        if (!ifly.Connected)
        {
            return Blocked("simulateur déconnecté");
        }

        if (!ifly.TitleMatches)
        {
            return Blocked($"avion non reconnu : TITLE = {ifly.Title.Display(IflyVariables.Title)}, filtre « {config.Ifly.AircraftTitleFilter} »");
        }

        if (!gsx.Available)
        {
            return Blocked($"GSX indisponible : {gsx.AvailabilityDetail}");
        }

        if (gsx.BoardingState != GsxServiceState.Completed)
        {
            return Blocked($"embarquement GSX non terminé : {GsxSnapshot.Describe(gsx.BoardingState)}");
        }

        if (gsx.BoardedTotal is not { } target)
        {
            return Blocked("nombre de passagers embarqués GSX indisponible");
        }

        if (!input.GsxStable)
        {
            return Blocked($"données GSX instables : attendre {config.StabilitySeconds} s sans changement");
        }

        var expectedKg = target * config.Ifly.KgPerPassenger;

        // Protection double chargement 1 : l'iFly affiche déjà le bon nombre.
        if (ifly.PassengerCount == target && config.Ifly.PassengerCountVariable is not null)
        {
            return new SyncPlan(SyncOutcome.NothingToDo, $"l'iFly affiche déjà {target} passagers", [], target, expectedKg);
        }

        // Protection double chargement 2 : la masse passagers est déjà là (GSX, EFB ou autre outil).
        if (ifly.PassengerStationsKg is { } currentKg && Math.Abs(currentKg - expectedKg) <= config.MassToleranceKg)
        {
            return new SyncPlan(
                SyncOutcome.NothingToDo,
                Format($"masse passagers déjà chargée : {currentKg:0} kg pour {expectedKg:0} kg attendus"),
                [],
                target,
                expectedKg);
        }

        // Protection double chargement 3 : la charge a bougé sans nous pendant l'embarquement.
        if (input.ExternalPayloadChange)
        {
            return new SyncPlan(
                SyncOutcome.Impossible,
                "la masse des stations passagers a changé pendant l'embarquement sans action de l'outil : "
                + "un autre système charge l'avion, synchronisation refusée pour éviter un double chargement",
                [],
                target,
                expectedKg);
        }

        var writes = BuildWrites(target, expectedKg);
        if (writes.Count == 0)
        {
            return new SyncPlan(
                SyncOutcome.Impossible,
                Format($"aucune variable iFly confirmée en écriture. À corriger à la main dans l'EFB : {target} passagers ({expectedKg:0} kg)")
                + Gap(ifly, target),
                [],
                target,
                expectedKg);
        }

        var guard = new WriteGuard(config);
        var refusals = writes
            .Select(w => (w, reason: guard.Check(w.Var)))
            .Where(x => x.reason is not null)
            .Select(x => $"{x.w.Var.Name} : {x.reason}")
            .ToList();

        if (refusals.Count > 0)
        {
            // Tout ou rien : une écriture partielle laisserait passagers et masse incohérents.
            return new SyncPlan(
                SyncOutcome.Impossible,
                "écriture refusée par la garde — " + string.Join(" ; ", refusals) + Gap(ifly, target),
                [],
                target,
                expectedKg);
        }

        return new SyncPlan(SyncOutcome.Ready, Format($"{target} passagers GSX à reporter dans l'iFly"), writes, target, expectedKg);
    }

    private List<PlannedWrite> BuildWrites(int target, double expectedKg)
    {
        var writes = new List<PlannedWrite>();

        if (!string.IsNullOrWhiteSpace(config.Ifly.PassengerCountVariable))
        {
            writes.Add(new PlannedWrite(VarSpec.Parse(config.Ifly.PassengerCountVariable), target, 0.5, $"nombre de passagers = {target}"));
        }

        var stations = config.Ifly.PassengerStations;
        var totalShare = stations.Sum(s => Math.Max(0, s.Share));
        if (stations.Count > 0 && totalShare > 0)
        {
            foreach (var station in stations)
            {
                var kg = Math.Round(expectedKg * Math.Max(0, station.Share) / totalShare, 1);
                writes.Add(new PlannedWrite(station.WeightVar, kg, config.MassToleranceKg, Format($"station {station.Index} = {kg:0.0} kg")));
            }
        }

        return writes;
    }

    private static string Gap(IflySnapshot ifly, int target) =>
        ifly.PassengerCount is { } n ? Format($" (écart actuel : {target - n:+0;-0;0} passagers)") : "";

    private static SyncPlan Blocked(string reason) => new(SyncOutcome.Blocked, reason, []);

    private static string Format(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
