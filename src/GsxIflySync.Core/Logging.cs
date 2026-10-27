using System.Globalization;

namespace GsxIflySync.Core;

public interface ILog
{
    void Debug(string message);

    void Info(string message);

    void Warn(string message);

    void Error(string message);
}

/// <summary>Journal horodaté (heure locale + décalage UTC, en millisecondes), un fichier par jour.</summary>
public sealed class FileLogger : ILog, IDisposable
{
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly Func<DateTimeOffset> _clock;
    private StreamWriter? _writer;
    private DateOnly _day;

    public FileLogger(string directory, Func<DateTimeOffset>? clock = null)
    {
        _directory = directory;
        _clock = clock ?? (() => DateTimeOffset.Now);
        Directory.CreateDirectory(directory);
    }

    /// <summary>Appelé pour chaque ligne écrite (affichage dans l'interface).</summary>
    public event Action<string>? LineWritten;

    public string CurrentPath => Path.Combine(_directory, $"GsxIflySync_{_day:yyyyMMdd}.log");

    public void Debug(string message) => Write("DEBUG", message);

    public void Info(string message) => Write("INFO ", message);

    public void Warn(string message) => Write("WARN ", message);

    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        string line;
        lock (_gate)
        {
            var now = _clock();
            var day = DateOnly.FromDateTime(now.DateTime);
            if (_writer is null || day != _day)
            {
                _writer?.Dispose();
                _day = day;
                _writer = new StreamWriter(new FileStream(CurrentPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                {
                    AutoFlush = true,
                };
            }

            line = $"{now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture)} [{level}] {message}";
            _writer.WriteLine(line);
        }

        LineWritten?.Invoke(line);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
