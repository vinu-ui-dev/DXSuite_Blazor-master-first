using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DXSuite.Host.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);
    private readonly object _writeLock = new();
    private readonly string _logsDirectory;
    private readonly LogLevel _minLevel;

    public FileLoggerProvider(string logsDirectory, LogLevel minLevel)
    {
        _logsDirectory = logsDirectory;
        _minLevel = minLevel;
    }

    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _minLevel, WriteLine));

    private void WriteLine(string line)
    {
        lock (_writeLock)
        {
            var filePath = Path.Combine(_logsDirectory, $"dxsuite-{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(filePath, line + Environment.NewLine);
        }
    }

    public void Dispose()
    {
        _loggers.Clear();
    }

    private sealed class FileLogger(string categoryName, LogLevel minLevel, Action<string> writeLine) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel)
            => logLevel != LogLevel.None
               && (logLevel >= minLevel
                   || ((categoryName == "ApiTrace" || categoryName.EndsWith("AuthController")) && logLevel >= LogLevel.Information));

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
                return;

            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {categoryName}: {message}";
            if (exception is not null)
                line += $"{Environment.NewLine}{exception}";

            writeLine(line);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
