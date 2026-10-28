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
    public static bool SyncAutostartWithSettings(bool enable, bool useLogonTask = true)
    {
#if DEBUG
        // Debug ビルドでは bin\Debug の exe を自動起動に登録しない。
        // 登録すると、ログオン時に Debug 版が常駐し、その横で VS が再ビルドして .pri / XAML を差し替えるため、
        // 次に画面を開いた瞬間に XAML の読み込みに失敗して落ちる（実際に起きたクラッシュの原因）。
        // タスク名はインストール版と共通なので、インストール版の登録も書き換えない・消さない。
        // 例外として、OFF にしたときに「今の Debug exe を指す登録」が残っていれば、その掃除だけは行う。
        if (enable || !IsRegisteredToCurrentExe())
        {
            AppHost.AppendLifetimeLog($"autostart-sync-skipped-debug enable={enable}");
            return true;
        }
#endif
        return AutostartService.Sync(Identity, enable, useLogonTask);
    }

#if DEBUG
    /// <summary>今動いている exe（bin\Debug）を指す登録が、タスクか Run のどちらかに残っているか。</summary>
    private static bool IsRegisteredToCurrentExe()
    {
        string exePath = Environment.ProcessPath ?? string.Empty;
        if (exePath.Length == 0)
            return false;

        // 登録コマンドは「"パス" --background」の形なので、パスを含むかで判定する（大文字小文字は無視）
        foreach (bool preferLogonTask in new[] { true, false })
        {
            string? command = GetRegisteredCommand(preferLogonTask);
            if (command != null && command.Contains(exePath, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
#endif

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
