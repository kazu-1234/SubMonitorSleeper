using System;
using System.IO;
using WinUiShared;

namespace SubMonitorSleeper
{
    /// <summary>
    /// %AppData%\SubMonitorSleeper\settings.json に保存する設定。
    /// 共通基盤が使う項目は IAppHostSettings（テーマ・トレイ・自動起動・更新確認・窓位置）。固有の項目は自由に追加してよい。
    /// </summary>
    public class Settings : IAppHostSettings
    {
        /// <summary>デフォルトはシステム連動。</summary>
        public AppThemePreference ThemePreference { get; set; } = AppThemePreference.System;

        /// <summary>true のときタスクトレイアイコンを表示しない。</summary>
        public bool HideTrayIcon { get; set; }

        public int WindowWidth { get; set; } = 960;
        public int WindowHeight { get; set; } = 680;

        /// <summary>未保存時は -1。次回起動で位置を復元する。</summary>
        public int WindowX { get; set; } = -1;

        /// <summary>未保存時は -1。次回起動で位置を復元する。</summary>
        public int WindowY { get; set; } = -1;

        /// <summary>前回終了時に最大化されていたか。</summary>
        public bool WindowMaximized { get; set; }

        /// <summary>起動時（初回 MainWindow 表示時）に更新を自動確認する。</summary>
        public bool AutoCheckUpdateOnStartup { get; set; } = true;

        /// <summary>最後に更新確認した UTC 時刻。未確認は null。</summary>
        public DateTime? LastUpdateCheckUtc { get; set; }

        /// <summary>ログオンタスクによる自動起動。</summary>
        public bool AutoStart { get; set; }

        /// <summary>
        /// true: ログオンタスクで起動（既定）。
        /// false: スタートアップ（レジストリ Run）に登録。
        /// </summary>
        public bool UseLogonTask { get; set; } = true;

        /// <summary>消灯時に D6 へ書く値（4 = スタンバイ / 5 = 電源オフ）。それ以外の値は 4 として扱う。</summary>
        public uint PowerOffValue { get; set; } = AppConstants.DefaultPowerOffValue;

        /// <summary>
        /// このアプリがサブを消灯したまま、まだ点灯に戻していないか。
        /// 強制終了（タスクマネージャー・インストーラーの taskkill など）で戻せなかった場合に、
        /// 次回起動時にこれを見て点灯へ戻すため、消灯の命令を出す前に true で保存しておく。
        /// </summary>
        public bool SubTurnedOffByApp { get; set; }

        private static string SettingsFilePath =>
            Path.Combine(AppSetup.Profile.AppDataDirectory, "settings.json");

        /// <summary>読み込み。ファイルが無い・壊れているときは既定値（壊れていれば 1 つ前の保存版で救済）。</summary>
        public static Settings Load() =>
            JsonSettingsFile.Load<Settings>(SettingsFilePath) ?? new Settings();

        /// <summary>保存（一時ファイル → 差し替えで、途中で落ちても壊れない）。失敗は無視する。</summary>
        public void Save() => JsonSettingsFile.Save(SettingsFilePath, this);
    }
}
