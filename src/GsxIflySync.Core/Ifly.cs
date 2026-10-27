using System.Globalization;

namespace GsxIflySync.Core;

/// <summary>
/// SimVars standard MSFS utilisées côté avion. Elles sont documentées par le SDK MSFS ;
/// ce qui reste INCONNU, c'est si l'iFly s'en sert et si son EFB les reflète.
/// </summary>
public static class IflyVariables
{
    public static readonly VarSpec Title = new("A:TITLE", "", VarKind.Text);
    public static readonly VarSpec StationCount = new("A:PAYLOAD STATION COUNT");
    public static readonly VarSpec TotalWeight = new("A:TOTAL WEIGHT", "kilograms");
    public static readonly VarSpec EmptyWeight = new("A:EMPTY WEIGHT", "kilograms");

    public static VarSpec StationWeight(int index) =>
        new(string.Create(CultureInfo.InvariantCulture, $"A:PAYLOAD STATION WEIGHT:{index}"), "kilograms");

    public static VarSpec StationName(int index) =>
        new(string.Create(CultureInfo.InvariantCulture, $"A:PAYLOAD STATION NAME:{index}"), "", VarKind.Text);
}

public sealed record StationReading(int Index, string? Name, VarReading Weight);

public sealed record IflySnapshot(
    bool Connected,
    VarReading Title,
    bool TitleMatches,
    int? PassengerCount,
    string PassengerCountSource,
    double? PassengerStationsKg,
    IReadOnlyList<StationReading> Stations);

/// <summary>
/// Adaptateur iFly. Tout ce qui est propre à l'avion passe par ici : si l'accès
/// s'avère impossible ou différent, seul ce fichier change.
/// </summary>
public sealed class IflyAdapter(IVariableSource source, IflySettings settings)
{
    public void RegisterAll()
    {
        source.Register(IflyVariables.Title);
        source.Register(IflyVariables.StationCount);
        source.Register(IflyVariables.TotalWeight);
        source.Register(IflyVariables.EmptyWeight);

        for (var i = 1; i <= settings.PayloadStationScanCount; i++)
        {
            source.Register(IflyVariables.StationName(i));
            source.Register(IflyVariables.StationWeight(i));
        }

        foreach (var station in settings.PassengerStations)
        {
            source.Register(station.WeightVar);
        }

        if (PassengerCountSpec is { } count)
        {
            source.Register(count);
        }
    }

    public VarSpec? PassengerCountSpec =>
        string.IsNullOrWhiteSpace(settings.PassengerCountVariable) ? null : VarSpec.Parse(settings.PassengerCountVariable);

    public IflySnapshot Read()
    {
        var title = source.Get(IflyVariables.Title);
        var matches = title.Text is { } t && settings.AircraftTitleFilter.Length > 0
            && t.Contains(settings.AircraftTitleFilter, StringComparison.OrdinalIgnoreCase);

        var stations = new List<StationReading>();
        for (var i = 1; i <= settings.PayloadStationScanCount; i++)
        {
            var name = source.Get(IflyVariables.StationName(i));
            stations.Add(new StationReading(i, name.Text, source.Get(IflyVariables.StationWeight(i))));
        }

        var paxKg = PassengerStationsKg();
        var (count, origin) = ReadPassengerCount(paxKg);

        return new IflySnapshot(source.IsConnected, title, matches, count, origin, paxKg, stations);
    }

    /// <summary>Somme des stations passagers configurées ; null si une seule manque.</summary>
    private double? PassengerStationsKg()
    {
        if (settings.PassengerStations.Count == 0)
        {
            return null;
        }

        double sum = 0;
        foreach (var station in settings.PassengerStations)
        {
            var r = source.Get(station.WeightVar);
            if (r.Status != VarStatus.Available || r.Value is null)
            {
                return null;
            }

            sum += r.Value.Value;
        }

        return sum;
    }

    private (int? Count, string Origin) ReadPassengerCount(double? paxKg)
    {
        if (PassengerCountSpec is { } spec)
        {
            var r = source.Get(spec);
            return r.Status == VarStatus.Available && r.Value is { } v
                ? ((int)Math.Round(v), $"variable {spec.Name}")
                : (null, $"variable {spec.Name} : {r.Status} ({r.Detail})");
        }

        if (paxKg is { } kg && settings.KgPerPassenger > 0)
        {
            return ((int)Math.Round(kg / settings.KgPerPassenger), $"ESTIMATION {kg:0} kg / {settings.KgPerPassenger:0} kg");
        }

        return (null, "aucune source configurée (variable passagers iFly inconnue)");
    }
}
