namespace GsxIflySync.Core;

/// <summary>Type de donnée demandé à SimConnect pour une variable.</summary>
public enum VarKind
{
    Number,
    Text,
}

/// <summary>
/// Description d'une variable simulateur : nom complet avec préfixe
/// (« L:FSDT_GSX_BOARDING_STATE », « A:PAYLOAD STATION WEIGHT:3 »…) et unité SimConnect.
/// </summary>
public sealed record VarSpec(string Name, string Unit = "number", VarKind Kind = VarKind.Number)
{
    /// <summary>Nom tel qu'il est passé à SimConnect (sans le préfixe « A: », qui n'est pas accepté).</summary>
    public string SimConnectName => Name.StartsWith("A:", StringComparison.OrdinalIgnoreCase) ? Name[2..] : Name;

    /// <summary>Vrai pour une variable L: globale. SimConnect ne signale pas d'erreur pour une L: inexistante.</summary>
    public bool IsLocalVar => Name.StartsWith("L:", StringComparison.OrdinalIgnoreCase);

    /// <summary>Clé de comparaison insensible à la casse et aux espaces de bord.</summary>
    public string Key => Normalize(Name);

    public static string Normalize(string name) => name.Trim().ToUpperInvariant();

    /// <summary>
    /// Analyse une saisie libre : « L:VAR », « L:VAR, number », « A:TITLE, string ».
    /// Sans préfixe, la variable est considérée comme une SimVar standard (A:).
    /// </summary>
    public static VarSpec Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("Nom de variable vide.", nameof(input));
        }

        var parts = input.Split(',', 2, StringSplitOptions.TrimEntries);
        var name = parts[0];
        var unit = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : "number";

        if (!name.Contains(':') || name.IndexOf(':') > 2)
        {
            name = "A:" + name;
        }

        var kind = unit.Equals("string", StringComparison.OrdinalIgnoreCase) ? VarKind.Text : VarKind.Number;
        return new VarSpec(name, kind == VarKind.Text ? "" : unit, kind);
    }

    public override string ToString() => Kind == VarKind.Text ? $"{Name} (texte)" : $"{Name} [{Unit}]";
}

public enum VarStatus
{
    /// <summary>Valeur reçue du simulateur.</summary>
    Available,

    /// <summary>Pas de valeur : simulateur déconnecté, pas encore reçue, ou source absente.</summary>
    Unavailable,

    /// <summary>SimConnect a renvoyé une exception pour cette variable (nom inconnu, type invalide…).</summary>
    Error,
}

/// <summary>Dernière lecture connue d'une variable. Une donnée absente n'a jamais de valeur numérique.</summary>
public sealed record VarReading(
    VarStatus Status,
    double? Value = null,
    string? Text = null,
    string? Detail = null,
    DateTimeOffset? ReceivedAt = null)
{
    public static VarReading Unavailable(string detail) => new(VarStatus.Unavailable, Detail: detail);

    public static VarReading Failed(string detail) => new(VarStatus.Error, Detail: detail);

    public static VarReading Number(double value, DateTimeOffset at) => new(VarStatus.Available, Value: value, ReceivedAt: at);

    public static VarReading Of(string text, DateTimeOffset at) => new(VarStatus.Available, Text: text, ReceivedAt: at);

    public bool HasValue => Status == VarStatus.Available && (Value.HasValue || Text is not null);

    /// <summary>Texte d'affichage : jamais « 0 » pour une donnée absente.</summary>
    public string Display(VarSpec spec)
    {
        return Status switch
        {
            VarStatus.Available when Text is not null => $"\"{Text}\"",
            VarStatus.Available when Value.HasValue && spec.IsLocalVar && Value.Value == 0
                => "0 (L: à 0 ou inexistante — indiscernable)",
            VarStatus.Available when Value.HasValue => Value.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            VarStatus.Error => $"ERREUR : {Detail}",
            _ => $"indisponible ({Detail})",
        };
    }
}

/// <summary>Source de lectures (SimConnect en production, faux en test).</summary>
public interface IVariableSource
{
    bool IsConnected { get; }

    /// <summary>Demande l'abonnement à une variable. Idempotent.</summary>
    void Register(VarSpec spec);

    VarReading Get(VarSpec spec);
}

/// <summary>
/// Écriture brute vers le simulateur. Ne jamais l'appeler directement :
/// passer par <see cref="GuardedWriter"/>, seul chemin autorisé.
/// </summary>
public interface IRawVariableWriter
{
    /// <summary>Envoie la valeur. Retourne un message d'erreur, ou null si l'envoi a été accepté par SimConnect.</summary>
    string? WriteNumber(VarSpec spec, double value);
}
