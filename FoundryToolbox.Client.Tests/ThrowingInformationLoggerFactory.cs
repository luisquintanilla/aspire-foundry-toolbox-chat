using Microsoft.Extensions.Logging;

namespace FoundryToolbox.Client.Tests;

internal sealed class ThrowingInformationLoggerFactory : ILoggerFactory
{
    public bool ThrowOnInformation { get; set; }

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class Logger(ThrowingInformationLoggerFactory owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Information &&
                owner.ThrowOnInformation)
            {
                throw new InvalidOperationException(
                    "The test logger rejected the information event.");
            }
        }
    }
}
