using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MouseBatteryTray;

internal static class StartupRegistration
{
    private const string LegacyRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string LegacyRunName = "MouseBatteryTray";
    public static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Mouse Battery Tray.lnk");
    public static bool IsEnabled => File.Exists(ShortcutPath);

    public static void SetEnabled(bool enabled, string executable)
    {
        if (enabled)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath)!);
            object? shellObject = null;
            object? linkObject = null;
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell")
                    ?? throw new InvalidOperationException("Windows shortcut support is unavailable.");
                shellObject = Activator.CreateInstance(shellType)!;
                dynamic shell = shellObject;
                linkObject = shell.CreateShortcut(ShortcutPath);
                dynamic link = linkObject;
                link.TargetPath = executable;
                link.WorkingDirectory = Path.GetDirectoryName(executable);
                link.Description = "Show Logitech mouse battery in the Windows tray";
                link.Save();
            }
            finally
            {
                if (linkObject is not null) Marshal.FinalReleaseComObject(linkObject);
                if (shellObject is not null) Marshal.FinalReleaseComObject(shellObject);
            }
        }
        else File.Delete(ShortcutPath);

        // Keep one startup mechanism and let the menu turn it off completely.
        using var legacy = Registry.CurrentUser.OpenSubKey(LegacyRunKey, writable: true);
        legacy?.DeleteValue(LegacyRunName, throwOnMissingValue: false);
    }

    public static void MigrateLegacy(string executable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(LegacyRunKey);
        if (key?.GetValue(LegacyRunName) is string command &&
            string.Equals(command.Trim().Trim('"'), executable, StringComparison.OrdinalIgnoreCase))
            SetEnabled(true, executable);
    }
}
