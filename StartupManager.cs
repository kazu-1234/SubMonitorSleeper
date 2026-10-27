using WinUiShared;

namespace SubMonitorSleeper;

/// <summary>
/// 自動起動のアプリ側入口。実体は WinUiShared.AutostartService（SPM 正本）。
/// SPM と同じく、ログオンタスク（既定）とスタートアップ（レジストリ Run）を Settings.UseLogonTask で切り替える。
/// </summary>
public static class StartupManager
{
    private static readonly AutostartIdentity Identity = new()
    {
        AppName = "SubMonitorSleeper",
        LogonTaskName = "SubMonitorSleeper Logon",
        RegistryName = "SubMonitorSleeper",
        // true にしないと useLogonTask = false でもタスク登録になる（スタートアップ方式を選べるようにするため）
        AllowRegistryRun = true,
        StartupShortcutNames = ["SubMonitorSleeper.lnk", "SubMonitorSleeper - Shortcut.lnk"]
    };

    public static bool IsAutoStartEnabled() => AutostartService.IsEnabled(Identity);

    /// <param name="enable">自動起動するか。false ならタスク・Run の両方を消す。</param>
    /// <param name="useLogonTask">true: ログオンタスク / false: スタートアップ（レジストリ Run）。</param>
    public static bool SyncAutostartWithSettings(bool enable, bool useLogonTask = true) =>
        AutostartService.Sync(Identity, enable, useLogonTask);

    public static bool ApplyAutoStart(bool enable, bool useLogonTask = true) =>
        SyncAutostartWithSettings(enable, useLogonTask);

    public static void ValidateAutoStart(bool autoStartEnabled, bool useLogonTask = true) =>
        SyncAutostartWithSettings(autoStartEnabled, useLogonTask);

    /// <summary>登録されているコマンド。preferLogonTask は今の方式（タスクか Run か）を先に探すため。</summary>
    public static string? GetRegisteredCommand(bool preferLogonTask = true) =>
        AutostartService.GetRegisteredCommand(Identity, preferLogonTask);

    /// <summary>
    /// 旧方式（Run / スタートアップのショートカット）の残骸をログオンタスクへ移す。
    /// スタートアップ方式を選んでいるときに呼ぶと、選んだ Run まで消してしまうので、呼び出し側で UseLogonTask のときだけ呼ぶ。
    /// </summary>
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
