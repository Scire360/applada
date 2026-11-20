using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Applada.Api.IntegrationTests.Infrastructure;

/// <summary>Guarda em memória tudo o que a API loga, para os testes conferirem categoria, nível e mensagem.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();

    public IReadOnlyList<CapturedLog> Entries => [.. _entries];

    public void Clear() => _entries.Clear();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            entries.Enqueue(new CapturedLog(category, logLevel, formatter(state, exception), exception));
        }
    }
}

public sealed record CapturedLog(string Category, LogLevel Level, string Message, Exception? Exception);
