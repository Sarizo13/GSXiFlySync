using System.Globalization;
using System.Runtime.InteropServices;
using GsxIflySync.Core;

namespace GsxIflySync.SimConnect;

public enum ConnectionState
{
    Disconnected,
    Opening,
    Connected,
}

/// <summary>
/// Connexion SimConnect en scrutation (GetNextDispatch), sans fenêtre ni thread :
/// <see cref="Pump"/> est appelée par un minuteur de l'interface.
/// Une définition + une requête par variable, pour qu'une exception désigne une variable précise.
/// </summary>
public sealed class SimConnectClient : IVariableSource, IRawVariableWriter, IDisposable
{
    private const string AppName = "GSX iFly Sync";

    /// <summary>Les données arrivent chaque seconde : au-delà de ce silence, la connexion est tenue pour perdue.</summary>
    private static readonly TimeSpan SilenceLimit = TimeSpan.FromSeconds(10);

    private readonly ILog _log;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<string, Entry> _byKey = [];
    private readonly Dictionary<uint, Entry> _byId = [];
    private readonly Dictionary<uint, (Entry Entry, string Operation)> _bySendId = [];
    private IntPtr _handle;
    private uint _nextId = 1;
    private DateTimeOffset _lastMessage;
    private bool _dataSeen;

    public SimConnectClient(ILog log, Func<DateTimeOffset>? clock = null)
    {
        _log = log;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    public string StatusText { get; private set; } = "non connecté";

    public bool IsConnected => State == ConnectionState.Connected;

    public event Action? ConnectionChanged;

    /// <summary>Tente d'ouvrir la connexion. Sans effet si elle est déjà ouverte.</summary>
    public void TryConnect()
    {
        if (_handle != IntPtr.Zero)
        {
            return;
        }

        try
        {
            var hr = NativeMethods.SimConnect_Open(out _handle, AppName, IntPtr.Zero, 0, IntPtr.Zero, 0);
            if (hr < 0 || _handle == IntPtr.Zero)
            {
                _handle = IntPtr.Zero;
                SetState(ConnectionState.Disconnected, $"MSFS injoignable (HRESULT 0x{hr:X8})");
                return;
            }
        }
        catch (DllNotFoundException)
        {
            SetState(ConnectionState.Disconnected, "SimConnect.dll introuvable : la copier à côté de l'exécutable (voir README)");
            return;
        }
        catch (BadImageFormatException)
        {
            SetState(ConnectionState.Disconnected, "SimConnect.dll incompatible : le programme et la DLL doivent être en x64");
            return;
        }

        _lastMessage = _clock();
        SetState(ConnectionState.Opening, "connexion ouverte, attente de MSFS");
        foreach (var entry in _byKey.Values)
        {
            Define(entry);
        }
    }

    /// <summary>Lit tous les messages en attente. À appeler régulièrement (≈100 ms).</summary>
    public void Pump()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        for (var i = 0; i < 500; i++)
        {
            var hr = NativeMethods.SimConnect_GetNextDispatch(_handle, out var ptr, out var size);
            if (hr < 0 || ptr == IntPtr.Zero || size == 0)
            {
                break;
            }

            var raw = new byte[size];
            Marshal.Copy(ptr, raw, 0, (int)size);
            _lastMessage = _clock();

            try
            {
                Handle(SimConnectMessageParser.Parse(raw));
            }
            catch (InvalidDataException ex)
            {
                _log.Error($"message SimConnect illisible : {ex.Message}");
            }

            if (_handle == IntPtr.Zero)
            {
                return;
            }
        }

        // Au menu principal aucun avion n'envoie de données : on ne juge le silence qu'après un premier flux.
        if (_dataSeen && _clock() - _lastMessage > SilenceLimit)
        {
            Disconnect($"aucune donnée depuis {SilenceLimit.TotalSeconds:0} s : connexion perdue");
        }
    }

    public void Register(VarSpec spec)
    {
        if (_byKey.ContainsKey(spec.Key))
        {
            return;
        }

        var entry = new Entry(spec, _nextId++);
        _byKey[spec.Key] = entry;
        _byId[entry.Id] = entry;
        _log.Debug($"variable enregistrée #{entry.Id} {spec}");

        if (_handle != IntPtr.Zero)
        {
            Define(entry);
        }
    }

    public VarReading Get(VarSpec spec)
    {
        if (!IsConnected)
        {
            return VarReading.Unavailable("simulateur déconnecté");
        }

        return _byKey.TryGetValue(spec.Key, out var entry) ? entry.Reading : VarReading.Unavailable("variable non enregistrée");
    }

    public IEnumerable<VarSpec> RegisteredVariables => _byKey.Values.Select(e => e.Spec);

    /// <summary>Écriture brute. Appelée uniquement via <see cref="GuardedWriter"/>.</summary>
    public string? WriteNumber(VarSpec spec, double value)
    {
        if (!IsConnected)
        {
            return "simulateur déconnecté";
        }

        if (!_byKey.TryGetValue(spec.Key, out var entry))
        {
            return "variable non enregistrée (jamais lue) : écriture refusée";
        }

        if (entry.Reading.Status == VarStatus.Error)
        {
            return $"variable en erreur SimConnect : {entry.Reading.Detail}";
        }

        var buffer = Marshal.AllocHGlobal(sizeof(double));
        try
        {
            Marshal.Copy(new[] { value }, 0, buffer, 1);
            var hr = NativeMethods.SimConnect_SetDataOnSimObject(_handle, entry.Id, NativeMethods.ObjectIdUser, 0, 0, sizeof(double), buffer);
            if (hr < 0)
            {
                return $"SetDataOnSimObject HRESULT 0x{hr:X8}";
            }

            Track(entry, "écriture");
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Disconnect(string reason)
    {
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.SimConnect_Close(_handle);
            _handle = IntPtr.Zero;
        }

        _bySendId.Clear();
        _dataSeen = false;
        foreach (var entry in _byKey.Values)
        {
            entry.Reading = VarReading.Unavailable("pas encore reçue");
        }

        SetState(ConnectionState.Disconnected, reason);
    }

    public void Dispose() => Disconnect("fermeture de l'outil");

    private void Define(Entry entry)
    {
        var spec = entry.Spec;
        var isText = spec.Kind == VarKind.Text;
        var hr = NativeMethods.SimConnect_AddToDataDefinition(
            _handle,
            entry.Id,
            spec.SimConnectName,
            isText ? null : spec.Unit,
            isText ? NativeMethods.DataTypeString256 : NativeMethods.DataTypeFloat64,
            0f,
            NativeMethods.UnusedDatumId);
        if (hr < 0)
        {
            entry.Reading = VarReading.Failed($"AddToDataDefinition HRESULT 0x{hr:X8}");
            return;
        }

        Track(entry, "définition");

        hr = NativeMethods.SimConnect_RequestDataOnSimObject(
            _handle, entry.Id, entry.Id, NativeMethods.ObjectIdUser, NativeMethods.PeriodSecond, 0, 0, 0, 0);
        if (hr < 0)
        {
            entry.Reading = VarReading.Failed($"RequestDataOnSimObject HRESULT 0x{hr:X8}");
            return;
        }

        Track(entry, "requête");
        entry.Reading = VarReading.Unavailable("pas encore reçue");
    }

    private void Track(Entry entry, string operation)
    {
        if (NativeMethods.SimConnect_GetLastSentPacketID(_handle, out var sendId) >= 0)
        {
            _bySendId[sendId] = (entry, operation);
        }
    }

    private void Handle(SimConnectMessage message)
    {
        switch (message)
        {
            case OpenMessage:
                SetState(ConnectionState.Connected, "connecté à MSFS");
                break;

            case QuitMessage:
                Disconnect("MSFS a fermé la connexion");
                break;

            case ExceptionMessage ex:
                OnException(ex);
                break;

            case DataMessage data when _byId.TryGetValue(data.RequestId, out var entry):
                _dataSeen = true;
                var now = _clock();
                var previous = entry.Reading;
                entry.Reading = entry.Spec.Kind == VarKind.Text
                    ? VarReading.Of(data.ReadString(), now)
                    : VarReading.Number(data.ReadFloat64(), now);
                if (previous.Value != entry.Reading.Value || previous.Text != entry.Reading.Text || previous.Status != entry.Reading.Status)
                {
                    _log.Debug($"{entry.Spec.Name} = {entry.Reading.Display(entry.Spec)}");
                }

                break;
        }
    }

    private void OnException(ExceptionMessage ex)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"exception SimConnect {ex.Exception} {SimConnectMessageParser.ExceptionName(ex.Exception)}");
        if (_bySendId.TryGetValue(ex.SendId, out var origin))
        {
            text += $" ({origin.Operation} de {origin.Entry.Spec.Name})";

            // Une écriture refusée ne rend pas la lecture fausse : on ne marque que les erreurs de définition/requête.
            if (origin.Operation != "écriture")
            {
                origin.Entry.Reading = VarReading.Failed(text);
            }
        }

        _log.Error(text);
    }

    private void SetState(ConnectionState state, string text)
    {
        var changed = state != State || text != StatusText;
        State = state;
        StatusText = text;
        if (changed)
        {
            _log.Info($"SimConnect : {state} — {text}");
            ConnectionChanged?.Invoke();
        }
    }

    private sealed class Entry(VarSpec spec, uint id)
    {
        public VarSpec Spec { get; } = spec;

        public uint Id { get; } = id;

        public VarReading Reading { get; set; } = VarReading.Unavailable("pas encore reçue");
    }
}
