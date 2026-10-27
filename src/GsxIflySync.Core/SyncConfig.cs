using System.Text.Json;
using System.Text.Json.Serialization;

namespace GsxIflySync.Core;

/// <summary>Une station de charge iFly qui reçoit une part de la masse passagers.</summary>
public sealed class PassengerStation
{
    /// <summary>Index SimConnect de la station (PAYLOAD STATION WEIGHT:index, à partir de 1).</summary>
    public int Index { get; set; }

    /// <summary>Part de la masse passagers (normalisée sur l'ensemble des stations).</summary>
    public double Share { get; set; } = 1.0;

    [JsonIgnore]
    public VarSpec WeightVar => IflyVariables.StationWeight(Index);
}

public sealed class IflySettings
{
    /// <summary>Filtre sur A:TITLE. La synchronisation est refusée si le titre ne le contient pas.</summary>
    public string AircraftTitleFilter { get; set; } = "iFly";

    /// <summary>Variable portant le nombre de passagers iFly. Inconnue à ce jour : null.</summary>
    public string? PassengerCountVariable { get; set; }

    /// <summary>Stations de charge des zones passagers. Inconnues à ce jour : vide.</summary>
    public List<PassengerStation> PassengerStations { get; set; } = [];

    /// <summary>Masse forfaitaire par passager (bagage cabine inclus). Hypothèse à aligner sur l'EFB iFly.</summary>
    public double KgPerPassenger { get; set; } = 84.0;

    /// <summary>Nombre de stations balayées par le diagnostic (noms + masses).</summary>
    public int PayloadStationScanCount { get; set; } = 12;
}

/// <summary>Configuration lue depuis config.json. Les valeurs par défaut n'écrivent rien.</summary>
public sealed class SyncConfig
{
    public bool ReadOnly { get; set; } = true;

    public bool SyncEnabled { get; set; }

    public int DispatchIntervalMs { get; set; } = 100;

    public int ReconnectIntervalSeconds { get; set; } = 5;

    /// <summary>Durée pendant laquelle le compte GSX ne doit plus bouger avant synchronisation.</summary>
    public int StabilitySeconds { get; set; } = 10;

    public double MassToleranceKg { get; set; } = 5.0;

    /// <summary>Délai avant relecture après écriture.</summary>
    public int ReadbackDelayMs { get; set; } = 2500;

    public IflySettings Ifly { get; set; } = new();

    /// <summary>
    /// SEULES variables où l'outil peut écrire. Ne mettre ici qu'un nom « fait sourcé »
    /// ou confirmé en vol par le diagnostic.
    /// </summary>
    public List<string> ConfirmedWritableVariables { get; set; } = [];

    /// <summary>Variables supplémentaires affichées par le diagnostic au démarrage.</summary>
    public List<string> WatchVariables { get; set; } = [];

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static SyncConfig Parse(string json) =>
        JsonSerializer.Deserialize<SyncConfig>(json, Options) ?? throw new InvalidDataException("config.json vide.");

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>
    /// Charge le fichier. S'il manque, copie config.example.json voisin (commenté),
    /// ou à défaut écrit les valeurs sûres par défaut.
    /// </summary>
    public static SyncConfig LoadOrCreate(string path)
    {
        if (!File.Exists(path))
        {
            var example = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "config.example.json");
            if (File.Exists(example))
            {
                File.Copy(example, path);
            }
            else
            {
                File.WriteAllText(path, new SyncConfig().ToJson());
            }
        }

        return Parse(File.ReadAllText(path));
    }
}
