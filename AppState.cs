using System;
using System.Threading.Tasks;

namespace SubMonitorSleeper
{
    /// <summary>ページ間で共有するアプリ状態。</summary>
    public sealed class AppState
    {
        public AppState(Settings settings)
        {
            Settings = settings;
        }

        public Settings Settings { get; }

        /// <summary>タスクトレイアイコンの表示状態を設定値に合わせて反映する（AppRuntime が注入）。</summary>
        public Action? ApplyTrayIconVisibility { get; set; }

        /// <summary>テスト消灯（消灯 → 数秒後に点灯）を実行する（AppRuntime が注入）。</summary>
        public Func<Task>? RunTestOffAsync { get; set; }

        /// <summary>テスト消灯の実行中か（ページを開き直したときにボタンの有効・無効を合わせるため）。</summary>
        public Func<bool>? IsTestOffRunning { get; set; }
    }
}
