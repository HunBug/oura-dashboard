using Microsoft.Extensions.Options;

namespace OuraDashboard.Web.Services.Llm;

public sealed class LlmConcurrencyLimiter
{
    private readonly SemaphoreSlim _semaphore;

    public LlmConcurrencyLimiter(IOptions<LlmOptions> options)
    {
        _semaphore = new SemaphoreSlim(Math.Max(1, options.Value.MaxConcurrentRequests));
    }

    public async Task<IDisposable> WaitAsync(CancellationToken ct)
    {
        await _semaphore.WaitAsync(ct);
        return new Releaser(_semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}
