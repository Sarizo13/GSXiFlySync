using GsxIflySync.Core;

namespace GsxIflySync.Tests;

public class VarSpecTests
{
    [Theory]
    [InlineData("L:FSDT_GSX_BOARDING_STATE", "L:FSDT_GSX_BOARDING_STATE", "number", VarKind.Number)]
    [InlineData("  L:MY_VAR , bool ", "L:MY_VAR", "bool", VarKind.Number)]
    [InlineData("PAYLOAD STATION WEIGHT:3, kilograms", "A:PAYLOAD STATION WEIGHT:3", "kilograms", VarKind.Number)]
    [InlineData("TITLE, string", "A:TITLE", "", VarKind.Text)]
    [InlineData("L:1:LOCAL_VAR", "L:1:LOCAL_VAR", "number", VarKind.Number)]
    public void Parse_reads_prefix_unit_and_kind(string input, string name, string unit, VarKind kind)
    {
        var spec = VarSpec.Parse(input);
        Assert.Equal(name, spec.Name);
        Assert.Equal(unit, spec.Unit);
        Assert.Equal(kind, spec.Kind);
    }

    [Fact]
    public void Parse_rejects_empty() => Assert.Throws<ArgumentException>(() => VarSpec.Parse("  "));

    [Fact]
    public void SimConnect_name_drops_A_prefix_only()
    {
        Assert.Equal("PAYLOAD STATION WEIGHT:3", IflyVariables.StationWeight(3).SimConnectName);
        Assert.Equal("L:FSDT_GSX_BOARDING_STATE", GsxVariables.BoardingState.SimConnectName);
    }

    [Fact]
    public void Unavailable_value_is_never_displayed_as_zero()
    {
        var text = VarReading.Unavailable("simulateur déconnecté").Display(GsxVariables.BoardingState);
        Assert.DoesNotContain("0", text.Replace("simulateur", ""));
        Assert.StartsWith("indisponible", text);
    }

    [Fact]
    public void Zero_L_var_is_flagged_as_ambiguous()
    {
        var text = VarReading.Number(0, DateTimeOffset.UnixEpoch).Display(GsxVariables.NumPassengers);
        Assert.Contains("indiscernable", text);
    }

    [Fact]
    public void Zero_A_var_is_a_plain_zero()
    {
        Assert.Equal("0", VarReading.Number(0, DateTimeOffset.UnixEpoch).Display(IflyVariables.StationWeight(1)));
    }
}

public class GsxReaderTests
{
    [Fact]
    public void Disconnected_sim_means_gsx_unavailable()
    {
        var sim = Scenario.BoardingCompleted(150);
        sim.IsConnected = false;
        var snap = new GsxReader(sim).Read();
        Assert.False(snap.Available);
        Assert.Null(snap.BoardedTotal);
    }

    [Fact]
    public void All_states_at_zero_mean_gsx_absent_not_zero_passengers()
    {
        var sim = new FakeSim()
            .Set(GsxVariables.BoardingState, 0)
            .Set(GsxVariables.DeboardingState, 0)
            .Set(GsxVariables.PassengersBoardingTotal, 0);
        var snap = new GsxReader(sim).Read();
        Assert.False(snap.Available);
        Assert.Null(snap.BoardedTotal);
    }

    [Fact]
    public void Never_received_state_means_unavailable()
    {
        var snap = new GsxReader(new FakeSim()).Read();
        Assert.False(snap.Available);
    }

    [Fact]
    public void Completed_boarding_is_read()
    {
        var snap = new GsxReader(Scenario.BoardingCompleted(150)).Read();
        Assert.True(snap.Available);
        Assert.Equal(GsxServiceState.Completed, snap.BoardingState);
        Assert.Equal(150, snap.BoardedTotal);
        Assert.Null(snap.PlannedPassengers); // NUMPASSENGERS = 0 : non fixé
    }

    [Theory]
    [InlineData(5.0, GsxServiceState.Performing)]
    [InlineData(6.0, GsxServiceState.Completed)]
    [InlineData(5.5, GsxServiceState.Unknown)]
    [InlineData(9.0, GsxServiceState.Unknown)]
    [InlineData(-1.0, GsxServiceState.Unknown)]
    public void State_mapping(double raw, GsxServiceState expected) => Assert.Equal(expected, GsxReader.ToState(raw));

    [Fact]
    public void Register_all_subscribes_every_gsx_variable()
    {
        var sim = new FakeSim();
        new GsxReader(sim).RegisterAll();
        Assert.All(GsxVariables.All, v => Assert.Contains(v.Key, sim.Registered));
    }
}

public class IflyAdapterTests
{
    [Fact]
    public void Without_configuration_passenger_count_is_absent()
    {
        var cfg = new SyncConfig();
        var snap = new IflyAdapter(Scenario.BoardingCompleted(10), cfg.Ifly).Read();
        Assert.Null(snap.PassengerCount);
        Assert.Contains("inconnue", snap.PassengerCountSource);
    }

    [Fact]
    public void Estimate_from_stations_is_labelled_as_estimate()
    {
        var cfg = Scenario.WritableConfig();
        cfg.Ifly.PassengerCountVariable = null;
        var sim = Scenario.BoardingCompleted(10)
            .Set(IflyVariables.StationWeight(3), 500)
            .Set(IflyVariables.StationWeight(4), 500);
        var snap = new IflyAdapter(sim, cfg.Ifly).Read();
        Assert.Equal(10, snap.PassengerCount);
        Assert.Contains("ESTIMATION", snap.PassengerCountSource);
    }

    [Fact]
    public void Missing_station_makes_mass_absent_not_partial()
    {
        var cfg = Scenario.WritableConfig();
        var sim = new FakeSim().Set(IflyVariables.StationWeight(3), 500);
        Assert.Null(new IflyAdapter(sim, cfg.Ifly).Read().PassengerStationsKg);
    }

    [Fact]
    public void Title_filter_is_case_insensitive()
    {
        var cfg = new SyncConfig();
        var sim = new FakeSim().SetText(IflyVariables.Title, "IFLY 737-MAX8");
        Assert.True(new IflyAdapter(sim, cfg.Ifly).Read().TitleMatches);
    }
}

public class WriteGuardTests
{
    private static SyncConfig Config(params string[] confirmed) =>
        new() { ReadOnly = false, ConfirmedWritableVariables = [.. confirmed] };

    [Fact]
    public void Read_only_blocks_everything()
    {
        var cfg = Config("L:X");
        cfg.ReadOnly = true;
        Assert.NotNull(new WriteGuard(cfg).Check(new VarSpec("L:X")));
    }

    [Fact]
    public void Unlisted_variable_is_refused() =>
        Assert.Contains("absente", new WriteGuard(Config("L:X")).Check(new VarSpec("L:Y")));

    [Fact]
    public void Listed_variable_is_allowed_case_insensitively() =>
        Assert.Null(new WriteGuard(Config("a:payload station weight:3")).Check(IflyVariables.StationWeight(3)));

    [Theory]
    [InlineData("A:FUEL TANK CENTER QUANTITY")]
    [InlineData("L:IFLY_FUEL_TOTAL")]
    [InlineData("A:CG PERCENT")]
    [InlineData("L:FSDT_GSX_NUMPASSENGERS")]
    [InlineData("A:EMPTY WEIGHT")]
    public void Fuel_cg_and_gsx_are_refused_even_if_listed(string name) =>
        Assert.Contains("interdite", new WriteGuard(Config(name)).Check(new VarSpec(name)));

    [Fact]
    public void Text_variables_are_not_writable() =>
        Assert.NotNull(new WriteGuard(Config("A:TITLE")).Check(IflyVariables.Title));

    [Fact]
    public void Guarded_writer_never_reaches_sim_when_refused()
    {
        var sim = new FakeSim();
        var writer = new GuardedWriter(sim, new WriteGuard(Config()), new NullLog());
        Assert.NotNull(writer.Write(new VarSpec("L:ANYTHING"), 1));
        Assert.Empty(sim.Writes);
    }
}

public class StabilityTrackerTests
{
    [Fact]
    public void Becomes_stable_after_duration_and_resets_on_change()
    {
        var t0 = DateTimeOffset.UnixEpoch;
        var s = new StabilityTracker(TimeSpan.FromSeconds(10));
        s.Observe(100, t0);
        Assert.False(s.IsStable(t0.AddSeconds(9)));
        s.Observe(100, t0.AddSeconds(9));
        Assert.True(s.IsStable(t0.AddSeconds(10)));
        s.Observe(101, t0.AddSeconds(11));
        Assert.False(s.IsStable(t0.AddSeconds(15)));
    }

    [Fact]
    public void Absent_value_is_never_stable()
    {
        var s = new StabilityTracker(TimeSpan.Zero);
        s.Observe(null, DateTimeOffset.UnixEpoch);
        Assert.False(s.IsStable(DateTimeOffset.UnixEpoch.AddHours(1)));
    }
}

public class SyncConfigTests
{
    [Fact]
    public void Defaults_cannot_write_anything()
    {
        var c = new SyncConfig();
        Assert.True(c.ReadOnly);
        Assert.False(c.SyncEnabled);
        Assert.Empty(c.ConfirmedWritableVariables);
        Assert.Null(c.Ifly.PassengerCountVariable);
        Assert.Empty(c.Ifly.PassengerStations);
    }

    [Fact]
    public void Json_round_trip_with_comments()
    {
        const string json = """
            {
              // commentaire autorisé
              "readOnly": false,
              "syncEnabled": true,
              "ifly": { "passengerStations": [ { "index": 3, "share": 0.5 } ] },
              "confirmedWritableVariables": [ "A:PAYLOAD STATION WEIGHT:3" ],
            }
            """;
        var c = SyncConfig.Parse(json);
        Assert.False(c.ReadOnly);
        Assert.Equal(3, c.Ifly.PassengerStations[0].Index);
        var again = SyncConfig.Parse(c.ToJson());
        Assert.Equal(c.ConfirmedWritableVariables, again.ConfirmedWritableVariables);
        Assert.DoesNotContain("weightVar", c.ToJson());
    }

    [Fact]
    public void Missing_config_is_created_from_example()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gsxifly-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "config.example.json"), Path.Combine(dir, "config.example.json"));
        var c = SyncConfig.LoadOrCreate(Path.Combine(dir, "config.json"));
        Assert.True(c.ReadOnly);
        Assert.NotEmpty(c.WatchVariables);
        Assert.True(File.Exists(Path.Combine(dir, "config.json")));
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Example_config_file_is_safe()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "config.example.json");
        var c = SyncConfig.Parse(File.ReadAllText(path));
        Assert.True(c.ReadOnly);
        Assert.False(c.SyncEnabled);
        Assert.Empty(c.ConfirmedWritableVariables);
    }
}

public class FileLoggerTests
{
    [Fact]
    public void Writes_timestamped_lines_to_daily_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gsxifly-tests-" + Guid.NewGuid().ToString("N"));
        var at = new DateTimeOffset(2026, 10, 27, 14, 5, 9, 123, TimeSpan.FromHours(1));
        using (var log = new FileLogger(dir, () => at))
        {
            log.Info("bonjour");
        }

        var text = File.ReadAllText(Path.Combine(dir, "GsxIflySync_20261027.log"));
        Assert.Contains("2026-10-27 14:05:09.123 +01:00 [INFO ] bonjour", text);
        Directory.Delete(dir, true);
    }
}
