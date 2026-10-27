namespace GsxIflySync.Core;

/// <summary>
/// Variables L: publiées par GSX Pro. Source : manuel GSX Pro pour MSFS,
/// chapitre « DEVELOPERS - Interfacing with GSX », p. 86-87 (GSX_manual_MSFS.pdf).
/// Toutes sont en LECTURE pour cet outil : il n'écrit jamais dans GSX.
/// </summary>
public static class GsxVariables
{
    public static readonly VarSpec BoardingState = new("L:FSDT_GSX_BOARDING_STATE");
    public static readonly VarSpec DeboardingState = new("L:FSDT_GSX_DEBOARDING_STATE");

    /// <summary>Nombre de passagers fixé par le code avion pour GSX (0 = GSX calcule lui-même).</summary>
    public static readonly VarSpec NumPassengers = new("L:FSDT_GSX_NUMPASSENGERS");

    /// <summary>Maximum de passagers rapporté par Simbrief.</summary>
    public static readonly VarSpec MaxPassengers = new("L:FSDT_GSX_MAX_NUMPASSENGERS");

    public static readonly VarSpec PassengersBoarding = new("L:FSDT_GSX_NUMPASSENGERS_BOARDING");

    /// <summary>Compte cumulé sur plusieurs bus (« Multiple Trips »).</summary>
    public static readonly VarSpec PassengersBoardingTotal = new("L:FSDT_GSX_NUMPASSENGERS_BOARDING_TOTAL");

    public static readonly VarSpec PassengersDeboarding = new("L:FSDT_GSX_NUMPASSENGERS_DEBOARDING");
    public static readonly VarSpec PassengersDeboardingTotal = new("L:FSDT_GSX_NUMPASSENGERS_DEBOARDING_TOTAL");
    public static readonly VarSpec BoardingCargoPercent = new("L:FSDT_GSX_BOARDING_CARGO_PERCENT");

    /// <summary>Automatisation de la charge utile par GSX (manuel p. 117 : seuls les PMDG 737/777 l'ont).</summary>
    public static readonly VarSpec AutomationPayload = new("L:FSDT_GSX_AUTOMATION_PAYLOAD");

    public static IReadOnlyList<VarSpec> All { get; } =
    [
        BoardingState, DeboardingState, NumPassengers, MaxPassengers, PassengersBoarding,
        PassengersBoardingTotal, PassengersDeboarding, PassengersDeboardingTotal,
        BoardingCargoPercent, AutomationPayload,
    ];
}

/// <summary>
/// États de service GSX. 1 à 6 : manuel p. 86. 0 : valeur avant que GSX ne tourne
/// (documenté par un outil tiers, non par FSDT — voir docs/recherche.md).
/// </summary>
public enum GsxServiceState
{
    NotRunning = 0,
    Callable = 1,
    NotAvailable = 2,
    Bypassed = 3,
    Requested = 4,
    Performing = 5,
    Completed = 6,
    Unknown = -1,
}

public sealed record GsxSnapshot(
    bool Available,
    string AvailabilityDetail,
    GsxServiceState BoardingState,
    int? PlannedPassengers,
    int? MaxPassengers,
    int? BoardedTotal,
    int? BoardingCurrent)
{
    public static string Describe(GsxServiceState state) => state switch
    {
        GsxServiceState.NotRunning => "GSX non démarré (0)",
        GsxServiceState.Callable => "peut être demandé (1)",
        GsxServiceState.NotAvailable => "non disponible (2)",
        GsxServiceState.Bypassed => "ignoré (3)",
        GsxServiceState.Requested => "demandé (4)",
        GsxServiceState.Performing => "en cours (5)",
        GsxServiceState.Completed => "terminé (6)",
        _ => "inconnu",
    };
}

/// <summary>Traduit les lectures brutes GSX en un instantané interprété.</summary>
public sealed class GsxReader(IVariableSource source)
{
    public void RegisterAll()
    {
        foreach (var spec in GsxVariables.All)
        {
            source.Register(spec);
        }
    }

    public GsxSnapshot Read()
    {
        var state = source.Get(GsxVariables.BoardingState);
        var deboard = source.Get(GsxVariables.DeboardingState);

        if (!source.IsConnected)
        {
            return Unavailable("simulateur déconnecté");
        }

        if (state.Status != VarStatus.Available || state.Value is null)
        {
            return Unavailable($"état d'embarquement {state.Status} ({state.Detail})");
        }

        var boardingState = ToState(state.Value.Value);
        var deboardingState = deboard.Value is { } d ? ToState(d) : GsxServiceState.Unknown;

        // Une L: inexistante se lit 0 : GSX absent et « GSX non démarré » ne se distinguent pas.
        // Les états documentés vont de 1 à 6 ; si aucun service n'y est, on considère GSX indisponible.
        if (boardingState is GsxServiceState.NotRunning or GsxServiceState.Unknown
            && deboardingState is GsxServiceState.NotRunning or GsxServiceState.Unknown)
        {
            return Unavailable("variables d'état GSX à 0 : GSX absent ou pas encore démarré");
        }

        return new GsxSnapshot(
            Available: true,
            AvailabilityDetail: "GSX actif",
            BoardingState: boardingState,
            PlannedPassengers: PositiveCount(GsxVariables.NumPassengers),
            MaxPassengers: PositiveCount(GsxVariables.MaxPassengers),
            BoardedTotal: Count(GsxVariables.PassengersBoardingTotal),
            BoardingCurrent: Count(GsxVariables.PassengersBoarding));
    }

    private static GsxSnapshot Unavailable(string detail) =>
        new(false, detail, GsxServiceState.Unknown, null, null, null, null);

    internal static GsxServiceState ToState(double raw)
    {
        var rounded = Math.Round(raw);
        return rounded is >= 0 and <= 6 && Math.Abs(raw - rounded) < 0.001
            ? (GsxServiceState)(int)rounded
            : GsxServiceState.Unknown;
    }

    private int? Count(VarSpec spec)
    {
        var r = source.Get(spec);
        return r.Status == VarStatus.Available && r.Value is { } v && v >= 0 ? (int)Math.Round(v) : null;
    }

    /// <summary>0 signifie « non fixé » pour ces variables (manuel p. 87) : traité comme absent.</summary>
    private int? PositiveCount(VarSpec spec) => Count(spec) is > 0 and var n ? n : null;
}
