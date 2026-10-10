namespace MouseBatteryTray;

// One background worker owns all reads. Manual refreshes wake the same worker,
// so neither UI timers nor overlapping requests can interrupt automatic polling.
internal sealed class BatteryMonitor
{
    private readonly Func<CancellationToken, Task<BatteryReading>> read;
    private readonly TimeSpan interval;
    private readonly SemaphoreSlim refresh = new(0, 1);
    private BatteryReading? lastKnown;

    public BatteryMonitor(Func<CancellationToken, Task<BatteryReading>>? read = null, TimeSpan? interval = null)
    {
        this.read = read ?? GHub.ReadAsync;
        this.interval = interval ?? TimeSpan.FromSeconds(5);
    }

    public void RequestRefresh()
    {
        try { refresh.Release(); }
        catch (SemaphoreFullException) { } // Coalesce clicks/resume events into one pending refresh.
    }

    public async Task RunAsync(Action<BatteryReading> update, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                BatteryReading current;
                try { current = await read(token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch (OperationCanceledException) { current = new("Logitech mouse", null, false, "G HUB did not respond"); }
                catch (Exception) { current = new("Logitech mouse", null, false, "Reconnecting to G HUB"); }

                if (current.Percentage is not null)
                    lastKnown = current with { IsStale = false };
                else if (lastKnown is not null)
                    current = lastKnown with { Charging = false, IsStale = true, Status = "Last known — " + current.Status };

                if (token.IsCancellationRequested) return;
                update(current);
                await refresh.WaitAsync(interval, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
