using Microsoft.Extensions.Options;

namespace LunchOrganizer.Tests.TestSupport;

/// <summary>
/// Minimal <see cref="IOptionsMonitor{T}"/> backed by a fixed value handed in at construction time.
/// Same shape as the Email suite's internal `TestOptionsMonitor&lt;T&gt;`, but public so it can be
/// shared across the Unit and Concurrency test namespaces in this assembly.
/// </summary>
public sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    public StaticOptionsMonitor(T currentValue) => CurrentValue = currentValue;

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable OnChange(Action<T, string> listener) => NullDisposable.Instance;

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose()
        {
        }
    }
}
