using System.Globalization;
using GsxIflySync.Core;
using GsxIflySync.SimConnect;

namespace GsxIflySync.App;

/// <summary>Interface : diagnostic en lecture, puis synchronisation confirmée écriture par écriture.</summary>
internal sealed class MainForm : Form
{
    private const int MaxMessages = 500;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly SyncConfig _config;
    private readonly FileLogger _log;
    private readonly SimConnectClient _client;
    private readonly SyncService _service;

    private readonly System.Windows.Forms.Timer _dispatchTimer = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 1000 };
    private DateTimeOffset _lastConnectAttempt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastSnapshotLog = DateTimeOffset.MinValue;
    private bool _syncBusy;

    private readonly Label _msfs = Value();
    private readonly Label _aircraft = Value();
    private readonly Label _gsx = Value();
    private readonly Label _boarding = Value();
    private readonly Label _planned = Value();
    private readonly Label _boarded = Value();
    private readonly Label _iflyPax = Value();
    private readonly Label _gap = Value();
    private readonly Label _sync = Value();
    private readonly CheckBox _readOnly = new() { Text = "Lecture seule", AutoSize = true };
    private readonly Button _testButton = new() { Text = "Tester la connexion", AutoSize = true };
    private readonly Button _syncButton = new() { Text = "Synchroniser", AutoSize = true };
    private readonly TextBox _freeVar = new() { Width = 360, PlaceholderText = "L:NOM_VARIABLE  ou  A:PAYLOAD STATION WEIGHT:3, kilograms" };
    private readonly Button _readButton = new() { Text = "Lire", AutoSize = true };
    private readonly ListView _vars = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, GridLines = true };
    private readonly ListBox _messages = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true, IntegralHeight = false };

    public MainForm(SyncConfig config, FileLogger log)
    {
        _config = config;
        _log = log;
        _client = new SimConnectClient(log);

        var writer = new GuardedWriter(_client, new WriteGuard(config), log);
        _service = new SyncService(_client, writer, config, log);

        Text = "GSX → iFly 737 MAX — passagers";
        Width = 1000;
        Height = 820;
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();

        _readOnly.Checked = config.ReadOnly;
        _readOnly.CheckedChanged += (_, _) => OnReadOnlyChanged();
        _testButton.Click += (_, _) => TestConnection();
        _syncButton.Click += async (_, _) => await SynchronizeAsync();
        _readButton.Click += (_, _) => AddFreeVariable();
        _freeVar.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                AddFreeVariable();
                e.SuppressKeyPress = true;
            }
        };

        _log.LineWritten += line =>
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(() => AppendMessage(line));
            }
        };

        _service.RegisterAll();
        foreach (var name in config.WatchVariables)
        {
            TryRegister(name);
        }

        _dispatchTimer.Interval = Math.Max(20, config.DispatchIntervalMs);
        _dispatchTimer.Tick += (_, _) => _client.Pump();
        _refreshTimer.Tick += (_, _) => Refresh(false);
        _client.ConnectionChanged += () => Refresh(false);

        Shown += (_, _) =>
        {
            _client.TryConnect();
            _lastConnectAttempt = DateTimeOffset.Now;
            _dispatchTimer.Start();
            _refreshTimer.Start();
            Refresh(true);
        };

        // Fermeture : on ferme seulement la connexion. L'outil n'a posé aucun état temporaire à défaire.
        FormClosing += (_, _) =>
        {
            _dispatchTimer.Stop();
            _refreshTimer.Stop();
            _client.Dispose();
        };
    }

    private static Label Value() => new() { AutoSize = true, Text = "—", Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };

    private void BuildLayout()
    {
        var status = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(8) };
        status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(string caption, Label value)
        {
            status.Controls.Add(new Label { Text = caption, AutoSize = true });
            status.Controls.Add(value);
        }

        Row("Connexion MSFS", _msfs);
        Row("Avion (A:TITLE)", _aircraft);
        Row("GSX", _gsx);
        Row("Embarquement GSX", _boarding);
        Row("Passagers prévus (GSX)", _planned);
        Row("Passagers embarqués (GSX)", _boarded);
        Row("Passagers iFly", _iflyPax);
        Row("Écart", _gap);
        Row("Synchronisation", _sync);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 0, 8, 4) };
        buttons.Controls.AddRange([_testButton, _syncButton, _readOnly]);

        var free = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 0, 8, 4) };
        free.Controls.AddRange([new Label { Text = "Variable libre :", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _freeVar, _readButton]);

        _vars.Columns.Add("Variable", 380);
        _vars.Columns.Add("Valeur", 300);
        _vars.Columns.Add("Statut", 100);
        _vars.Columns.Add("Reçue à", 120);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 300 };
        split.Panel1.Controls.Add(_vars);
        split.Panel2.Controls.Add(_messages);

        Controls.Add(split);
        Controls.Add(free);
        Controls.Add(buttons);
        Controls.Add(status);
    }

    private void Refresh(bool force)
    {
        var now = DateTimeOffset.Now;
        if (!_client.IsConnected && _client.State == ConnectionState.Disconnected
            && now - _lastConnectAttempt >= TimeSpan.FromSeconds(_config.ReconnectIntervalSeconds))
        {
            _lastConnectAttempt = now;
            _client.TryConnect();
        }

        var inputs = _service.Observe();
        var gsx = inputs.Gsx;
        var ifly = inputs.Ifly;

        _msfs.Text = $"{_client.State} — {_client.StatusText}";
        _aircraft.Text = ifly.Title.Display(IflyVariables.Title) + (ifly.TitleMatches ? "" : $"  (filtre « {_config.Ifly.AircraftTitleFilter} » non trouvé)");
        _gsx.Text = gsx.Available ? "disponible" : $"indisponible — {gsx.AvailabilityDetail}";
        _boarding.Text = gsx.Available ? GsxSnapshot.Describe(gsx.BoardingState) : "absent";
        _planned.Text = !gsx.Available
            ? "absent"
            : gsx.PlannedPassengers is { } p
                ? p.ToString(Inv)
                : "non publié (L:FSDT_GSX_NUMPASSENGERS à 0 : non fixé)" + (gsx.MaxPassengers is { } m ? $" — max Simbrief {m}" : "");
        _boarded.Text = gsx.BoardedTotal is { } b
            ? $"{b} (cumul) — bus courant {Count(gsx.BoardingCurrent)} — stable : {(inputs.GsxStable ? "oui" : "non")}"
            : "absent";
        _iflyPax.Text = ifly.PassengerCount is { } n ? $"{n} — {ifly.PassengerCountSource}" : $"absent — {ifly.PassengerCountSource}";
        _gap.Text = gsx.BoardedTotal is { } t && ifly.PassengerCount is { } i
            ? $"{t - i:+0;-0;0} passagers"
            : "non calculable (une des deux valeurs est absente)";
        _sync.Text = DescribeSync();
        _syncButton.Enabled = !_syncBusy;

        RefreshVariables();

        if (force || now - _lastSnapshotLog >= TimeSpan.FromSeconds(30))
        {
            _lastSnapshotLog = now;
            _log.Info($"instantané : MSFS={_client.State} | GSX={_gsx.Text} | embarquement={_boarding.Text} | prévus={_planned.Text} | embarqués={_boarded.Text} | iFly={_iflyPax.Text} | écart={_gap.Text}");
        }
    }

    private string DescribeSync()
    {
        var label = _service.State switch
        {
            SyncState.Running => "en cours",
            SyncState.Succeeded => "réussie",
            SyncState.Impossible => "impossible",
            _ => "inactive",
        };
        return _config.ReadOnly ? $"{label} — LECTURE SEULE" : label;
    }

    private void RefreshVariables()
    {
        var specs = _client.RegisteredVariables.ToList();
        _vars.BeginUpdate();
        try
        {
            while (_vars.Items.Count > specs.Count)
            {
                _vars.Items.RemoveAt(_vars.Items.Count - 1);
            }

            for (var i = 0; i < specs.Count; i++)
            {
                var spec = specs[i];
                var r = _client.Get(spec);
                string[] cells =
                [
                    spec.ToString(),
                    r.Display(spec),
                    r.Status.ToString(),
                    r.ReceivedAt?.ToString("HH:mm:ss", Inv) ?? "—",
                ];

                if (i < _vars.Items.Count)
                {
                    var item = _vars.Items[i];
                    for (var c = 0; c < cells.Length; c++)
                    {
                        if (item.SubItems[c].Text != cells[c])
                        {
                            item.SubItems[c].Text = cells[c];
                        }
                    }

                    item.ForeColor = StatusColor(r.Status);
                }
                else
                {
                    _vars.Items.Add(new ListViewItem(cells) { ForeColor = StatusColor(r.Status) });
                }
            }
        }
        finally
        {
            _vars.EndUpdate();
        }
    }

    private static System.Drawing.Color StatusColor(VarStatus status) => status switch
    {
        VarStatus.Available => SystemColors.WindowText,
        VarStatus.Error => System.Drawing.Color.Firebrick,
        _ => SystemColors.GrayText,
    };

    private static string Count(int? n) => n?.ToString(Inv) ?? "absent";

    private void TestConnection()
    {
        _log.Info("test de connexion demandé");
        if (!_client.IsConnected)
        {
            _client.Disconnect("test de connexion : réouverture");
            _client.TryConnect();
            _lastConnectAttempt = DateTimeOffset.Now;
        }

        Refresh(true);
        var available = _client.RegisteredVariables.Count(v => _client.Get(v).Status == VarStatus.Available);
        var errors = _client.RegisteredVariables.Count(v => _client.Get(v).Status == VarStatus.Error);
        MessageBox.Show(
            this,
            $"MSFS : {_client.State} — {_client.StatusText}\nGSX : {_gsx.Text}\nAvion : {_aircraft.Text}\n"
            + $"Variables reçues : {available}, en erreur : {errors}, total : {_client.RegisteredVariables.Count()}\n\n"
            + "Juste après la connexion, les valeurs arrivent en 1 à 2 secondes.",
            "Test de connexion",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void OnReadOnlyChanged()
    {
        _config.ReadOnly = _readOnly.Checked;
        if (_readOnly.Checked)
        {
            _log.Warn("lecture seule ACTIVÉE : toute écriture est bloquée");
        }
        else
        {
            _log.Warn("lecture seule levée (session uniquement, config.json inchangé)");
        }

        Refresh(false);
    }

    private async Task SynchronizeAsync()
    {
        if (_syncBusy)
        {
            return;
        }

        _syncBusy = true;
        _syncButton.Enabled = false;
        try
        {
            var plan = _service.Plan();
            _log.Info($"plan de synchronisation : {plan.Outcome} — {plan.Reason}");

            if (plan.Outcome != SyncOutcome.Ready)
            {
                var result = await _service.ExecuteAsync(plan, _ => false);
                MessageBox.Show(this, $"{plan.Outcome} : {plan.Reason}", "Synchronisation", MessageBoxButtons.OK,
                    result.State == SyncState.Succeeded ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                return;
            }

            var done = await _service.ExecuteAsync(plan, ConfirmWrite);
            var lines = string.Join("\n", done.Checks.Select(c => $"{(c.Ok ? "OK " : "KO ")} {c.Write.Var.Name} : {c.Detail}"));
            MessageBox.Show(this, $"{done.State} : {done.Message}\n\n{lines}", "Synchronisation", MessageBoxButtons.OK,
                done.State == SyncState.Succeeded ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        finally
        {
            _syncBusy = false;
            Refresh(false);
        }
    }

    private bool ConfirmWrite(PlannedWrite write)
    {
        var current = _client.Get(write.Var).Display(write.Var);
        var answer = MessageBox.Show(
            this,
            $"Écrire dans le simulateur ?\n\n{write.Description}\n\nVariable : {write.Var.Name}\nValeur actuelle : {current}\n"
            + $"Nouvelle valeur : {write.Value.ToString("0.###", Inv)}",
            "Confirmer l'écriture",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        return answer == DialogResult.Yes;
    }

    private void AddFreeVariable()
    {
        if (TryRegister(_freeVar.Text) is { } spec)
        {
            _log.Info($"variable libre ajoutée : {spec}");
            _freeVar.Clear();
            Refresh(false);
        }
    }

    private VarSpec? TryRegister(string text)
    {
        try
        {
            var spec = VarSpec.Parse(text);
            _client.Register(spec);
            return spec;
        }
        catch (ArgumentException ex)
        {
            _log.Warn($"variable ignorée « {text} » : {ex.Message}");
            return null;
        }
    }

    private void AppendMessage(string line)
    {
        _messages.Items.Add(line);
        while (_messages.Items.Count > MaxMessages)
        {
            _messages.Items.RemoveAt(0);
        }

        _messages.TopIndex = _messages.Items.Count - 1;
    }
}
