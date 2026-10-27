using GsxIflySync.Core;
using GsxIflySync.SimConnect;

namespace GsxIflySync.Tests;

public class SyncPlannerTests
{
    private static SyncPlan Plan(SyncConfig cfg, FakeSim sim, bool stable = true, bool external = false)
    {
        var inputs = new SyncInputs(new GsxReader(sim).Read(), new IflyAdapter(sim, cfg.Ifly).Read(), stable, external);
        return new SyncPlanner(cfg).Plan(inputs);
    }

    [Fact]
    public void Read_only_blocks()
    {
        var cfg = Scenario.WritableConfig();
        cfg.ReadOnly = true;
        Assert.Equal(SyncOutcome.Blocked, Plan(cfg, Scenario.BoardingCompleted(150)).Outcome);
    }

    [Fact]
    public void Disabled_sync_blocks()
    {
        var cfg = Scenario.WritableConfig();
        cfg.SyncEnabled = false;
        Assert.Equal(SyncOutcome.Blocked, Plan(cfg, Scenario.BoardingCompleted(150)).Outcome);
    }

    [Fact]
    public void Other_aircraft_blocks()
    {
        var sim = Scenario.BoardingCompleted(150).SetText(IflyVariables.Title, "PMDG 737-800");
        var plan = Plan(Scenario.WritableConfig(), sim);
        Assert.Equal(SyncOutcome.Blocked, plan.Outcome);
        Assert.Contains("avion non reconnu", plan.Reason);
    }

    [Fact]
    public void Gsx_unavailable_blocks()
    {
        var sim = Scenario.BoardingCompleted(150).Set(GsxVariables.BoardingState, 0).Set(GsxVariables.DeboardingState, 0);
        var plan = Plan(Scenario.WritableConfig(), sim);
        Assert.Equal(SyncOutcome.Blocked, plan.Outcome);
        Assert.Contains("GSX indisponible", plan.Reason);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    public void Boarding_not_completed_blocks(int state)
    {
        var sim = Scenario.BoardingCompleted(80).Set(GsxVariables.BoardingState, state);
        var plan = Plan(Scenario.WritableConfig(), sim);
        Assert.Equal(SyncOutcome.Blocked, plan.Outcome);
        Assert.Contains("non terminé", plan.Reason);
    }

    [Fact]
    public void Unstable_data_blocks() =>
        Assert.Equal(SyncOutcome.Blocked, Plan(Scenario.WritableConfig(), Scenario.BoardingCompleted(150), stable: false).Outcome);

    [Fact]
    public void Same_count_already_in_ifly_is_nothing_to_do()
    {
        var sim = Scenario.BoardingCompleted(150).Set(new VarSpec(Scenario.PaxVar), 150);
        Assert.Equal(SyncOutcome.NothingToDo, Plan(Scenario.WritableConfig(), sim).Outcome);
    }

    [Fact]
    public void Mass_already_loaded_by_someone_else_is_nothing_to_do()
    {
        var sim = Scenario.BoardingCompleted(150)
            .Set(IflyVariables.StationWeight(3), 7500)
            .Set(IflyVariables.StationWeight(4), 7502);
        var plan = Plan(Scenario.WritableConfig(), sim);
        Assert.Equal(SyncOutcome.NothingToDo, plan.Outcome);
        Assert.Contains("déjà chargée", plan.Reason);
    }

    [Fact]
    public void External_payload_change_refuses_to_double_load()
    {
        var plan = Plan(Scenario.WritableConfig(), Scenario.BoardingCompleted(150), external: true);
        Assert.Equal(SyncOutcome.Impossible, plan.Outcome);
        Assert.Contains("double chargement", plan.Reason);
    }

    [Fact]
    public void Without_confirmed_ifly_variable_sync_is_impossible_and_shows_manual_target()
    {
        var cfg = new SyncConfig { ReadOnly = false, SyncEnabled = true };
        var plan = Plan(cfg, Scenario.BoardingCompleted(150));
        Assert.Equal(SyncOutcome.Impossible, plan.Outcome);
        Assert.Contains("à la main", plan.Reason);
        Assert.Contains("150", plan.Reason);
        Assert.Empty(plan.Writes);
    }

    [Fact]
    public void One_unlisted_variable_refuses_the_whole_plan()
    {
        var cfg = Scenario.WritableConfig();
        cfg.ConfirmedWritableVariables.Remove("A:PAYLOAD STATION WEIGHT:4");
        var plan = Plan(cfg, Scenario.BoardingCompleted(150));
        Assert.Equal(SyncOutcome.Impossible, plan.Outcome);
        Assert.Empty(plan.Writes);
        Assert.Contains("PAYLOAD STATION WEIGHT:4", plan.Reason);
    }

    [Fact]
    public void Ready_plan_splits_mass_by_share()
    {
        var cfg = Scenario.WritableConfig();
        cfg.Ifly.PassengerStations[0].Share = 3;
        cfg.Ifly.PassengerStations[1].Share = 1;
        var plan = Plan(cfg, Scenario.BoardingCompleted(100));

        Assert.Equal(SyncOutcome.Ready, plan.Outcome);
        Assert.Equal(100, plan.TargetPassengers);
        Assert.Equal(10_000, plan.ExpectedPassengerKg);
        Assert.Collection(
            plan.Writes,
            w => Assert.Equal((Scenario.PaxVar, 100.0), (w.Var.Name, w.Value)),
            w => Assert.Equal(7500, w.Value),
            w => Assert.Equal(2500, w.Value));
    }
}

public class SyncServiceTests
{
    private static (SyncService Service, FakeSim Sim, NullLog Log) Build(SyncConfig cfg, FakeSim sim, Func<DateTimeOffset>? clock = null)
    {
        var log = new NullLog();
        var service = new SyncService(sim, new GuardedWriter(sim, new WriteGuard(cfg), log), cfg, log, clock);
        return (service, sim, log);
    }

    private static Func<DateTimeOffset> Clock(Func<DateTimeOffset> get) => get;

    [Fact]
    public async Task Full_sync_writes_then_reads_back()
    {
        var now = DateTimeOffset.UnixEpoch;
        var (service, sim, _) = Build(Scenario.WritableConfig(), Scenario.BoardingCompleted(120), Clock(() => now));
        service.Observe();
        now = now.AddSeconds(11);
        var plan = service.Plan();
        Assert.Equal(SyncOutcome.Ready, plan.Outcome);

        var asked = 0;
        var result = await service.ExecuteAsync(plan, _ => { asked++; return true; });

        Assert.Equal(SyncState.Succeeded, result.State);
        Assert.Equal(3, asked); // une confirmation par écriture
        Assert.Equal(3, sim.Writes.Count);
        Assert.All(result.Checks, c => Assert.True(c.Ok));
        Assert.Contains("12000.0 kg", result.Message);
    }

    [Fact]
    public async Task Refusing_first_confirmation_writes_nothing()
    {
        var now = DateTimeOffset.UnixEpoch;
        var (service, sim, _) = Build(Scenario.WritableConfig(), Scenario.BoardingCompleted(120), Clock(() => now));
        service.Observe();
        now = now.AddSeconds(11);
        var result = await service.ExecuteAsync(service.Plan(), _ => false);
        Assert.Equal(SyncState.Impossible, result.State);
        Assert.Empty(sim.Writes);
    }

    [Fact]
    public async Task Value_not_retained_by_aircraft_is_reported()
    {
        var now = DateTimeOffset.UnixEpoch;
        var sim = Scenario.BoardingCompleted(120);
        sim.RetainWrites = false;
        var (service, _, _) = Build(Scenario.WritableConfig(), sim, Clock(() => now));
        service.Observe();
        now = now.AddSeconds(11);
        var result = await service.ExecuteAsync(service.Plan(), _ => true);
        Assert.Equal(SyncState.Impossible, result.State);
        Assert.Contains("n'a pas retenu", result.Message);
        Assert.All(result.Checks, c => Assert.False(c.Ok));
    }

    [Fact]
    public async Task Read_only_switched_on_mid_plan_stops_writes()
    {
        var now = DateTimeOffset.UnixEpoch;
        var cfg = Scenario.WritableConfig();
        var (service, sim, _) = Build(cfg, Scenario.BoardingCompleted(120), Clock(() => now));
        service.Observe();
        now = now.AddSeconds(11);
        var plan = service.Plan();
        cfg.ReadOnly = true;
        var result = await service.ExecuteAsync(plan, _ => true);
        Assert.Equal(SyncState.Impossible, result.State);
        Assert.Empty(sim.Writes);
    }

    [Fact]
    public void Payload_change_during_boarding_is_detected()
    {
        var now = DateTimeOffset.UnixEpoch;
        var sim = Scenario.BoardingCompleted(0).Set(GsxVariables.BoardingState, 5);
        var (service, _, log) = Build(Scenario.WritableConfig(), sim, Clock(() => now));

        Assert.False(service.Observe().ExternalPayloadChange);
        sim.Set(IflyVariables.StationWeight(3), 2000);
        Assert.True(service.Observe().ExternalPayloadChange);

        sim.Set(GsxVariables.BoardingState, 6).Set(GsxVariables.PassengersBoardingTotal, 150);
        service.Observe();
        now = now.AddSeconds(11);
        Assert.Equal(SyncOutcome.Impossible, service.Plan().Outcome);
        Assert.Contains(log.Lines, l => l.Contains("hors outil"));
    }

    [Fact]
    public void Interrupted_then_resumed_boarding_waits_for_stable_completion()
    {
        var now = DateTimeOffset.UnixEpoch;
        var sim = Scenario.BoardingCompleted(40).Set(GsxVariables.BoardingState, 5);
        var (service, _, _) = Build(Scenario.WritableConfig(), sim, Clock(() => now));
        service.Observe();

        // Interruption : GSX repasse « peut être demandé », compte figé.
        sim.Set(GsxVariables.BoardingState, 1);
        now = now.AddSeconds(30);
        Assert.Equal(SyncOutcome.Blocked, service.Plan().Outcome);

        // Reprise puis fin : le compte bouge encore, puis se fige.
        sim.Set(GsxVariables.BoardingState, 5).Set(GsxVariables.PassengersBoardingTotal, 90);
        service.Observe();
        sim.Set(GsxVariables.BoardingState, 6).Set(GsxVariables.PassengersBoardingTotal, 150);
        now = now.AddSeconds(1);
        Assert.Equal(SyncOutcome.Blocked, service.Plan().Outcome); // pas encore stable
        now = now.AddSeconds(11);
        var plan = service.Plan();
        Assert.Equal(SyncOutcome.Ready, plan.Outcome);
        Assert.Equal(150, plan.TargetPassengers);
    }
}

public class SimConnectMessageParserTests
{
    private static byte[] Header(uint id, int size)
    {
        var b = new byte[size];
        BitConverter.GetBytes((uint)size).CopyTo(b, 0);
        BitConverter.GetBytes(6u).CopyTo(b, 4);
        BitConverter.GetBytes(id).CopyTo(b, 8);
        return b;
    }

    [Fact]
    public void Parses_float64_data()
    {
        var b = Header(8, 48);
        BitConverter.GetBytes(42u).CopyTo(b, 12); // dwRequestID
        BitConverter.GetBytes(42u).CopyTo(b, 20); // dwDefineID
        BitConverter.GetBytes(6.0).CopyTo(b, 40);
        var msg = Assert.IsType<DataMessage>(SimConnectMessageParser.Parse(b));
        Assert.Equal(42u, msg.RequestId);
        Assert.Equal(6.0, msg.ReadFloat64());
    }

    [Fact]
    public void Parses_string_data()
    {
        var b = Header(8, 40 + 256);
        System.Text.Encoding.UTF8.GetBytes("iFly 737-MAX8").CopyTo(b, 40);
        Assert.Equal("iFly 737-MAX8", Assert.IsType<DataMessage>(SimConnectMessageParser.Parse(b)).ReadString());
    }

    [Fact]
    public void Parses_exception()
    {
        var b = Header(1, 24);
        BitConverter.GetBytes(7u).CopyTo(b, 12);
        BitConverter.GetBytes(99u).CopyTo(b, 16);
        var ex = Assert.IsType<ExceptionMessage>(SimConnectMessageParser.Parse(b));
        Assert.Equal((7u, 99u), (ex.Exception, ex.SendId));
        Assert.Equal("NAME_UNRECOGNIZED", SimConnectMessageParser.ExceptionName(ex.Exception));
    }

    [Fact]
    public void Parses_open_and_quit()
    {
        Assert.IsType<OpenMessage>(SimConnectMessageParser.Parse(Header(2, 12)));
        Assert.IsType<QuitMessage>(SimConnectMessageParser.Parse(Header(3, 12)));
        Assert.IsType<OtherMessage>(SimConnectMessageParser.Parse(Header(4, 12)));
    }

    [Fact]
    public void Truncated_message_throws() =>
        Assert.Throws<InvalidDataException>(() => SimConnectMessageParser.Parse(new byte[8]));

    [Fact]
    public void Client_without_simulator_reports_unavailable_not_zero()
    {
        using var client = new SimConnectClient(new NullLog());
        client.Register(GsxVariables.BoardingState);
        var r = client.Get(GsxVariables.BoardingState);
        Assert.Equal(VarStatus.Unavailable, r.Status);
        Assert.Null(r.Value);
        Assert.NotNull(client.WriteNumber(GsxVariables.BoardingState, 1));
    }
}
