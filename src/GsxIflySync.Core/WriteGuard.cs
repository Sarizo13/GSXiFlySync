namespace GsxIflySync.Core;

/// <summary>
/// Seule protection contre l'écriture dans une variable non identifiée :
/// une écriture n'est permise que si la variable figure dans la liste confirmée
/// de config.json, n'est pas une variable de carburant/centrage, et que la lecture seule est levée.
/// </summary>
public sealed class WriteGuard(SyncConfig config)
{
    /// <summary>Fragments interdits même s'ils sont listés : carburant, centrage, masses à vide, GSX.</summary>
    private static readonly string[] Forbidden =
    [
        "FUEL", "CG", "CENTER OF GRAVITY", "EMPTY WEIGHT", "TOTAL WEIGHT", "FSDT_",
    ];

    /// <summary>Retourne null si l'écriture est permise, sinon la raison du refus.</summary>
    public string? Check(VarSpec spec)
    {
        if (config.ReadOnly)
        {
            return "mode lecture seule actif";
        }

        if (spec.Kind != VarKind.Number)
        {
            return "seules les variables numériques sont inscriptibles";
        }

        var key = spec.Key;
        foreach (var fragment in Forbidden)
        {
            if (ContainsWord(key, fragment))
            {
                return $"variable interdite par conception (« {fragment} ») : carburant, centrage et GSX ne sont jamais modifiés";
            }
        }

        var listed = config.ConfirmedWritableVariables.Any(v => VarSpec.Normalize(v) == key);
        return listed ? null : $"{spec.Name} absente de confirmedWritableVariables";
    }

    /// <summary>« CG » ne doit pas bloquer un nom qui le contient par hasard : on compare par mots.</summary>
    private static bool ContainsWord(string key, string fragment)
    {
        if (fragment.EndsWith('_'))
        {
            return key.Contains(fragment, StringComparison.Ordinal);
        }

        var tokens = key.Split([' ', ':', '_'], StringSplitOptions.RemoveEmptyEntries);
        var words = fragment.Split(' ');
        for (var i = 0; i + words.Length <= tokens.Length; i++)
        {
            if (words.Select((w, j) => tokens[i + j] == w).All(x => x))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Unique chemin d'écriture vers le simulateur : vérifie la garde à chaque appel.</summary>
public sealed class GuardedWriter(IRawVariableWriter inner, WriteGuard guard, ILog log)
{
    public string? Write(VarSpec spec, double value)
    {
        var refusal = guard.Check(spec);
        if (refusal is not null)
        {
            log.Warn($"ÉCRITURE REFUSÉE {spec.Name} = {value} : {refusal}");
            return refusal;
        }

        log.Info($"ÉCRITURE {spec.Name} = {value}");
        var error = inner.WriteNumber(spec, value);
        if (error is not null)
        {
            log.Error($"échec d'envoi {spec.Name} : {error}");
        }

        return error;
    }
}
