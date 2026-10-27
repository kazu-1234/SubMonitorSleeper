using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SubMonitorSleeper.Services;

namespace SubMonitorSleeper
{
    /// <summary>
    /// 設定画面からサブモニター制御を操作するための窓口（AppRuntime が実装する）。
    /// ページは AppRuntime を直接知らず、この口だけを使う。すべて UI スレッドから呼ぶ。
    /// </summary>
    public interface ISubMonitorControl
    {
        /// <summary>テスト消灯（消灯 → 数秒後に点灯）を実行する。</summary>
        Task RunTestOffAsync();

        /// <summary>テスト消灯の実行中か（ページを開き直したときにボタンの有効・無効を合わせるため）。</summary>
        bool IsTestOffRunning { get; }

        /// <summary>接続中のモニター一覧。</summary>
        IReadOnlyList<MonitorInfo> GetMonitors();

        /// <summary>対象モニターを変える（null = 自動）。消灯中なら先に旧対象を点灯に戻す。</summary>
        void SetTargetMonitor(string? devicePath);

        /// <summary>消灯時の D6 値（4 / 5）を変える。</summary>
        void SetPowerOffValue(uint value);

        /// <summary>今の Windows の「画面をオフにする」時間。</summary>
        PowerTimeout GetCurrentTimeout();

        /// <summary>キー入力の受付を始める（今のホットキーを一時的に外す。押すと切り替えが動いてしまうため）。</summary>
        void BeginHotkeyCapture();

        /// <summary>新しいホットキーを登録して保存する。登録できなければ元のキーに戻して false。</summary>
        bool ApplyHotkey(uint modifiers, uint virtualKey);

        /// <summary>キー入力の受付を取り消す（元のホットキーを登録し直す）。</summary>
        void CancelHotkeyCapture();
    }

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

        /// <summary>サブモニター制御の窓口（AppRuntime が注入）。</summary>
        public ISubMonitorControl? SubMonitor { get; set; }
    }
}
