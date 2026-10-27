using GsxIflySync.Core;

namespace GsxIflySync.Tests;

/// <summary>Simulateur factice : on y pose des valeurs, il les rend comme SimConnect.</summary>
internal sealed class FakeSim : IVariableSource, IRawVariableWriter
{
    private readonly Dictionary<string, VarReading> _values = [];

    public bool IsConnected { get; set; } = true;

    public List<(VarSpec Spec, double Value)> Writes { get; } = [];

    /// <summary>Si faux, l'écriture est « acceptée » mais la valeur n'est pas retenue (EFB qui écrase).</summary>
    public bool RetainWrites { get; set; } = true;

    public HashSet<string> Registered { get; } = [];

    public void Register(VarSpec spec) => Registered.Add(spec.Key);

    public VarReading Get(VarSpec spec)
    {
        if (!IsConnected)
        {
            return VarReading.Unavailable("simulateur déconnecté");
        }

        return _values.TryGetValue(spec.Key, out var r) ? r : VarReading.Unavailable("pas encore reçue");
    }

    public FakeSim Set(VarSpec spec, double value)
    {
        _values[spec.Key] = VarReading.Number(value, DateTimeOffset.UnixEpoch);
        return this;
    }

    public FakeSim SetText(VarSpec spec, string text)
    {
        _values[spec.Key] = VarReading.Of(text, DateTimeOffset.UnixEpoch);
        return this;
    }

    public FakeSim Fail(VarSpec spec, string detail)
    {
        _values[spec.Key] = VarReading.Failed(detail);
        return this;
    }

    public string? WriteNumber(VarSpec spec, double value)
    {
        Writes.Add((spec, value));
        if (RetainWrites)
        {
            Set(spec, value);
        }

        return null;
    }
}

internal sealed class NullLog : ILog
{
    public List<string> Lines { get; } = [];

    public void Debug(string message) => Lines.Add("D " + message);

    public void Info(string message) => Lines.Add("I " + message);

    public void Warn(string message) => Lines.Add("W " + message);

    public void Error(string message) => Lines.Add("E " + message);
}

internal static class Scenario
{
    public const string PaxVar = "L:TEST_PAX_COUNT";

    /// <summary>Config où la synchronisation est possible : 2 stations + une variable de compte confirmées.</summary>
    public static SyncConfig WritableConfig()
    {
        var c = new SyncConfig
        {
            ReadOnly = false,
            SyncEnabled = true,
            StabilitySeconds = 10,
            ReadbackDelayMs = 0,
        };
        c.Ifly.PassengerCountVariable = PaxVar;
        c.Ifly.PassengerStations = [new PassengerStation { Index = 3, Share = 1 }, new PassengerStation { Index = 4, Share = 1 }];
        c.Ifly.KgPerPassenger = 100;
        c.Ifly.PayloadStationScanCount = 2;
        c.ConfirmedWritableVariables = [PaxVar, "A:PAYLOAD STATION WEIGHT:3", "A:PAYLOAD STATION WEIGHT:4"];
        return c;
    }

    /// <summary>Embarquement GSX terminé avec <paramref name="boarded"/> passagers ; iFly vide.</summary>
    public static FakeSim BoardingCompleted(int boarded) =>
        new FakeSim()
            .SetText(IflyVariables.Title, "iFly 737-MAX8 Test Livery")
            .Set(GsxVariables.BoardingState, 6)
            .Set(GsxVariables.DeboardingState, 1)
            .Set(GsxVariables.NumPassengers, 0)
            .Set(GsxVariables.PassengersBoardingTotal, boarded)
            .Set(GsxVariables.PassengersBoarding, 0)
            .Set(new VarSpec(PaxVar), 0)
            .Set(IflyVariables.StationWeight(3), 0)
            .Set(IflyVariables.StationWeight(4), 0);
}
