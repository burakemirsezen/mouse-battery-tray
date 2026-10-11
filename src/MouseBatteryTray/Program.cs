using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace MouseBatteryTray;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--startup" && args[1] is "on" or "off")
        {
            try { StartupRegistration.SetEnabled(args[1] == "on", Application.ExecutablePath); return 0; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or COMException or InvalidOperationException)
            { Console.Error.WriteLine("Could not change startup setting: " + ex.Message); return 1; }
        }
        if (args.Length == 2 && args[0] == "--check")
        {
            try
            {
                var reading = GHub.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
                File.WriteAllText(args[1], JsonSerializer.Serialize(reading));
                return reading.Percentage is null ? 2 : 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(args[1], JsonSerializer.Serialize(new { Error = ex.Message }));
                return 1;
            }
        }
        using var instance = new Mutex(true, "Local\\MouseBatteryTray", out var first);
        if (!first) return 0;
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp(args.Length == 2 && args[0] == "--observe" ? args[1] : null));
        return 0;
    }
}

internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon tray;
    private readonly Control dispatcher = new();
    private readonly BatteryMonitor monitor = new();
    private readonly CancellationTokenSource shutdown = new();
    private readonly ToolStripMenuItem status = new("Connecting to G HUB…") { Enabled = false };
    private readonly ToolStripMenuItem lastCheck = new("Waiting for first reading") { Enabled = false };
    private readonly ToolStripMenuItem startup = new("Start with Windows");
    private Icon? icon;
    private BatteryReading reading = new("Logitech mouse", null, false, "Connecting to G HUB");

    public TrayApp(string? observationPath = null)
    {
        try { StartupRegistration.MigrateLegacy(Application.ExecutablePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or COMException or InvalidOperationException) { }
        _ = dispatcher.Handle;
        var menu = new ContextMenuStrip();
        menu.Items.Add(status);
        menu.Items.Add(lastCheck);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Refresh now", null, (_, _) => monitor.RequestRefresh());
        menu.Items.Add(startup);
        startup.Click += (_, _) => ToggleStartup();
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => ExitThread());
        menu.Opening += (_, _) =>
        {
            try { startup.Checked = StartupRegistration.IsEnabled; }
            catch { startup.Checked = false; }
        };
        tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => monitor.RequestRefresh();
        UpdateIcon();
        SystemEvents.PowerModeChanged += PowerModeChanged;
        _ = Task.Run(() => monitor.RunAsync(current => PostReading(current, observationPath), shutdown.Token));
    }

    private void PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) monitor.RequestRefresh();
    }

    private void PostReading(BatteryReading current, string? observationPath)
    {
        if (shutdown.IsCancellationRequested) return;
        try
        {
            dispatcher.BeginInvoke(() =>
            {
                if (shutdown.IsCancellationRequested) return;
                reading = current;
                lastCheck.Text = "Last checked: " + DateTime.Now.ToString("HH:mm:ss");
                UpdateIcon();
                // Explicit diagnostic mode only. Records updates applied on the UI thread.
                if (observationPath is not null)
                {
                    try { File.AppendAllText(observationPath, JsonSerializer.Serialize(new { CheckedAt = DateTimeOffset.UtcNow, Reading = current }) + Environment.NewLine); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            });
        }
        catch (InvalidOperationException) when (shutdown.IsCancellationRequested) { }
    }

    private void UpdateIcon()
    {
        var description = reading.Percentage is int pct
            ? $"{reading.Name}: {pct}% — {reading.Status}" : reading.Status;
        status.Text = description;
        tray.Text = description.Length > 63 ? description[..63] : description;
        var next = DrawIcon(reading);
        tray.Icon = next;
        icon?.Dispose();
        icon = next;
    }

    private static Icon DrawIcon(BatteryReading reading)
    {
        bool light = false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            light = key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
        }
        catch { }
        using var image = TrayIcon.Draw(reading, light);
        var handle = image.GetHicon();
        try { using var native = Icon.FromHandle(handle); return (Icon)native.Clone(); }
        finally { DestroyIcon(handle); }
    }

    private void ToggleStartup()
    {
        try
        {
            StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled, Application.ExecutablePath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or COMException or InvalidOperationException)
        { MessageBox.Show("Could not change startup setting: " + ex.Message, "Mouse Battery Tray"); }
    }

    protected override void ExitThreadCore()
    {
        shutdown.Cancel();
        SystemEvents.PowerModeChanged -= PowerModeChanged;
        dispatcher.Dispose();
        tray.Visible = false;
        tray.ContextMenuStrip?.Dispose();
        tray.Dispose();
        icon?.Dispose();
        base.ExitThreadCore();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
