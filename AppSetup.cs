using WinUiShared;

namespace SubMonitorSleeper
{
    /// <summary>
    /// このアプリの「型」と識別名（共通基盤 WinUiShared に渡す値はすべてここに集約する）。
    /// 新しいアプリを複製したら、まずこのファイルの名前・URL・機能の ON/OFF を書き換える。
    /// </summary>
    internal static class AppSetup
    {
        /// <summary>%AppData% のフォルダ名・Mutex 名・User-Agent などに使う識別名。公開後は変えない。</summary>
        public const string AppName = "SubMonitorSleeper";

        /// <summary>
        /// 自動起動の登録名（タスク名・Run の名前・ショートカット名。公開後は変えない）。
        /// ログオンタスク（既定）とスタートアップ（レジストリ Run）を Settings.UseLogonTask で切り替える。
        /// Debug ビルドの exe を登録しない対策は共通部（AutostartService）が行う。
        /// </summary>
        public static readonly AutostartController Autostart = new(new AutostartIdentity
        {
            AppName = AppName,
            LogonTaskName = AppName + " Logon",
            RegistryName = AppName,
            // true にしないと useLogonTask = false でもタスク登録になる（スタートアップ方式を選べるようにするため）
            AllowRegistryRun = true,
            StartupShortcutNames = [AppName + ".lnk", AppName + " - Shortcut.lnk"]
        });

        // 型の選び方:
        //   常駐型（BlueShift / SPM 系）  … CloseBehavior = KeepRunning, UseTray = true（自動起動を使うなら SyncAutostart も渡す）
        //   ウィンドウ型（LHD / Music 系）… CloseBehavior = ExitProcess, UseTray = false, SyncAutostart = null
        //   UseResumeMonitor は、スリープ復帰時に何かを再適用するアプリだけ true にする
        public static readonly AppProfile Profile = new()
        {
            AppName = AppName,
            InstanceNameVersion = "v1",

            // GitHub Releases の最新版を確認し、添付の *-setup.exe で更新する（SPM と同じ方式）
            LatestReleaseApiUrl = AppConstants.GitHubApiUrl,

            CloseBehavior = WindowCloseBehavior.KeepRunning,
            UseTray = true,
            UseResumeMonitor = true,
            // 自動起動の窓口。--sync-autostart / --cleanup-autostart、起動時の移行と同期、終了時の掃除を共通部が行う
            Autostart = Autostart,
            LoadSettings = Settings.Load,

            // 他アプリと衝突しないウィンドウクラス名（AppName を含める）
            SystemEventWindowClassName = "SubMonitorSleeperSystemEventWindow_v2",
            TrayWindowClassName = "SubMonitorSleeper_TrayMessageWindow_v1",
            TrayWindowTitle = "SubMonitorSleeperTray",
            TrayIconFileName = "AppIcon.ico",

            GetString = Strings.Get,
            FormatString = Strings.Format
        };
    }
}
