using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace MouseBatteryTray;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
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
        Application.Run(new TrayApp());
        return 0;
    }
}

internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 30_000 };
    private readonly CancellationTokenSource shutdown = new();
    private readonly ToolStripMenuItem status = new("Connecting to G HUB…") { Enabled = false };
    private readonly ToolStripMenuItem startup = new("Start with Windows");
    private Icon? icon;
    private BatteryReading reading = new("Logitech mouse", null, false, "Connecting to G HUB");
    private bool refreshing;
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "MouseBatteryTray";

    public TrayApp()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(status);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Refresh now", null, async (_, _) => await RefreshAsync());
        menu.Items.Add(startup);
        startup.Click += (_, _) => ToggleStartup();
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => ExitThread());
        menu.Opening += (_, _) =>
        {
            try { using var key = Registry.CurrentUser.OpenSubKey(RunKey); startup.Checked = key?.GetValue(RunName) is string; }
            catch { startup.Checked = false; }
        };
        tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += async (_, _) => await RefreshAsync();
        UpdateIcon();
        timer.Tick += async (_, _) => await RefreshAsync();
        timer.Start();
        // Start after the Windows Forms message loop is ready, so continuations stay on the UI thread.
        var initial = new System.Windows.Forms.Timer { Interval = 100 };
        initial.Tick += async (_, _) => { initial.Stop(); initial.Dispose(); await RefreshAsync(); };
        initial.Start();
    }

    private async Task RefreshAsync()
    {
        if (refreshing || shutdown.IsCancellationRequested) return;
        refreshing = true;
        try { reading = await GHub.ReadAsync(shutdown.Token); }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { return; }
        catch (OperationCanceledException) { reading = new("Logitech mouse", null, false, "G HUB did not respond"); }
        catch (Exception ex) when (ex is System.Net.WebSockets.WebSocketException or IOException or JsonException or InvalidOperationException)
        { reading = new("Logitech mouse", null, false, "Battery unavailable — check G HUB"); }
        finally { refreshing = false; }
        if (!shutdown.IsCancellationRequested) UpdateIcon();
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
        using var image = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(image);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var text = reading.Percentage?.ToString() ?? "?";
        var color = reading.Charging ? (light ? Color.DarkGreen : Color.LimeGreen) :
            reading.Percentage <= 10 ? (light ? Color.Firebrick : Color.Tomato) :
            reading.Percentage <= 20 ? (light ? Color.DarkOrange : Color.Gold) : light ? Color.Black : Color.White;
        using var brush = new SolidBrush(color);
        using var font = new Font("Segoe UI", text.Length == 3 ? 17 : 22, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(text, font, brush, new RectangleF(-1, -1, 34, 31), format);
        if (reading.Charging) graphics.FillRectangle(brush, 10, 29, 12, 2);
        var handle = image.GetHicon();
        try { using var native = Icon.FromHandle(handle); return (Icon)native.Clone(); }
        finally { DestroyIcon(handle); }
    }

    private void ToggleStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key.GetValue(RunName) is not null) key.DeleteValue(RunName, false);
            else key.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        { MessageBox.Show("Could not change startup setting: " + ex.Message, "Mouse Battery Tray"); }
    }

    protected override void ExitThreadCore()
    {
        shutdown.Cancel();
        timer.Stop();
        timer.Dispose();
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
