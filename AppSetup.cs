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

        // 型の選び方:
        //   常駐型（BlueShift / SPM 系）  … CloseBehavior = KeepRunning, UseTray = true（自動起動を使うなら SyncAutostart も渡す）
        //   ウィンドウ型（LHD / Music 系）… CloseBehavior = ExitProcess, UseTray = false, SyncAutostart = null
        //   UseResumeMonitor は、スリープ復帰時に何かを再適用するアプリだけ true にする
        public static readonly AppProfile Profile = new()
        {
            AppName = AppName,
            InstanceNameVersion = "v1",

            // リリース後に GitHub Releases API URL を設定する（未設定なら更新確認は「未設定」表示）
            // 例: https://api.github.com/repos/owner/repo/releases/latest
            LatestReleaseApiUrl = null,

            CloseBehavior = WindowCloseBehavior.KeepRunning,
            UseTray = true,
            UseResumeMonitor = true,
            SyncAutostart = StartupManager.SyncAutostartWithSettings,

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
