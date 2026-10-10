using System.Collections.Concurrent;
using MouseBatteryTray;

internal static class MonitorTests
{
    public static async Task RunAsync()
    {
        using var stop = new CancellationTokenSource();
        var readings = new ConcurrentQueue<BatteryReading>();
        var calls = 0;
        var active = 0;
        var maximumActive = 0;
        var monitor = new BatteryMonitor(async token =>
        {
            maximumActive = Math.Max(maximumActive, Interlocked.Increment(ref active));
            try
            {
                await Task.Delay(5, token);
                return Interlocked.Increment(ref calls) switch
                {
                    1 => new("Test mouse", 12, false, "On battery"),
                    2 => new("Test mouse", null, false, "Mouse disconnected"),
                    3 => throw new OperationCanceledException("Battery read timed out"),
                    4 => throw new IOException("G HUB restarted"),
                    _ => new("Test mouse", 13, true, "Charging")
                };
            }
            finally { Interlocked.Decrement(ref active); }
        }, TimeSpan.FromMilliseconds(20));
        await monitor.RunAsync(value =>
        {
            readings.Enqueue(value);
            if (readings.Count == 5) stop.Cancel();
        }, stop.Token).WaitAsync(TimeSpan.FromSeconds(3));

        var values = readings.ToArray();
        Check(values.Length == 5, "Polls continue without manual refresh");
        Check(values[1].Percentage == 12 && values[1].IsStale && !values[1].Charging,
            "Disconnect keeps clearly marked last reading");
        Check(values[2].Percentage == 12 && values[2].IsStale, "Timed out read does not stop automatic retries");
        Check(values[3].Percentage == 12 && values[3].IsStale, "Failed read does not stop automatic retries");
        Check(values[4].Percentage == 13 && !values[4].IsStale && values[4].Charging,
            "Automatic recovery restores live percentage and charging");
        Check(maximumActive == 1, "Reads never overlap");

        using var manualStop = new CancellationTokenSource();
        var manualCalls = 0;
        var manual = new BatteryMonitor(_ => Task.FromResult(new BatteryReading("Test mouse", 50, false, "On battery")),
            TimeSpan.FromHours(1));
        for (var i = 0; i < 100; i++) manual.RequestRefresh();
        await manual.RunAsync(_ => { if (++manualCalls == 2) manualStop.Cancel(); }, manualStop.Token)
            .WaitAsync(TimeSpan.FromSeconds(3));
        Check(manualCalls == 2, "Refresh requests wake the worker and coalesce");

        using var idleStop = new CancellationTokenSource();
        var idle = new BatteryMonitor(_ => Task.FromResult(new BatteryReading("Test mouse", 50, false, "On battery")),
            TimeSpan.FromHours(1));
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var idleTask = idle.RunAsync(_ => first.TrySetResult(), idleStop.Token);
        await first.Task.WaitAsync(TimeSpan.FromSeconds(3));
        idleStop.Cancel();
        await idleTask.WaitAsync(TimeSpan.FromSeconds(3));
        Check(idleTask.IsCompletedSuccessfully, "Quit cancels a sleeping worker promptly");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }
}
