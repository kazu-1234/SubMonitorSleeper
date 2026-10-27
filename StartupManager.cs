using WinUiShared;

namespace SubMonitorSleeper;

/// <summary>ログオンタスク専用。実体は WinUiShared.AutostartService（SPM 正本）。</summary>
public static class StartupManager
{
    private static readonly AutostartIdentity Identity = new()
    {
        AppName = "SubMonitorSleeper",
        LogonTaskName = "SubMonitorSleeper Logon",
        RegistryName = "SubMonitorSleeper",
        AllowRegistryRun = false,
        StartupShortcutNames = ["SubMonitorSleeper.lnk", "SubMonitorSleeper - Shortcut.lnk"]
    };

    public static bool IsAutoStartEnabled() => AutostartService.IsEnabled(Identity);

    public static bool SyncAutostartWithSettings(bool enable) =>
        AutostartService.Sync(Identity, enable, useLogonTask: true);

    public static bool ApplyAutoStart(bool enable) => SyncAutostartWithSettings(enable);

    public static void ValidateAutoStart(bool autoStartEnabled) =>
        SyncAutostartWithSettings(autoStartEnabled);

    public static string? GetRegisteredCommand() =>
        AutostartService.GetRegisteredCommand(Identity, preferLogonTask: true);

    public static void MigrateFromLegacyIfNeeded()
    {
        bool hadRegistry = AutostartService.IsRegistryPresent(Identity);
        if (hadRegistry && !IsAutoStartEnabled())
        {
            SyncAutostartWithSettings(true);
            return;
        }

        AutostartService.RemoveRegistryAndShortcuts(Identity);
    }

    public static void CleanupAutostartOnly() => SyncAutostartWithSettings(false);
}
