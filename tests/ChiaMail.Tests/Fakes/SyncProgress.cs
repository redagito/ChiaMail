namespace ChiaMail.Tests.Fakes;

/// <summary>
/// Synchronous IProgress&lt;T&gt; — Progress&lt;T&gt; uses SynchronizationContext.Post
/// which in console/test environments runs the callback on ThreadPool (async),
/// making test assertions race-prone.
/// </summary>
internal sealed class SyncProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;
    public SyncProgress(Action<T> handler) => _handler = handler;
    public void Report(T value) => _handler(value);
}
