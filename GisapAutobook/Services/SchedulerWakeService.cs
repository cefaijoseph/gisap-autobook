namespace GisapAutobook.Services;

/// <summary>
/// Singleton signal that lets any caller (e.g. /runnow) immediately wake the
/// BookingSchedulerService out of its sleep loop.
/// </summary>
public sealed class SchedulerWakeService
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    /// <summary>Wake the scheduler immediately (no-op if already awake).</summary>
    public void Wake()
    {
        // Release only if the semaphore count is 0 to avoid queuing redundant signals.
        if (_signal.CurrentCount == 0)
            _signal.Release();
    }

    /// <summary>Wait until woken or the delay expires, whichever comes first.</summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            await _signal.WaitAsync(timeout, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }
}
