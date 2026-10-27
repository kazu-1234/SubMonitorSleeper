using System;
using System.Collections.Generic;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using SubMonitorSleeper.Services;
using Windows.System;
using Windows.UI.Core;
using WinUiShared;

namespace SubMonitorSleeper.Views
{
    public sealed partial class SettingsPage : Page
    {
        private AppState? _state;
        private bool _isInitializing;

        public SettingsPage()
        {
            InitializeComponent();
            CompactComboBoxHelper.AttachFitToSelectedText(ThemeComboBox);
            ToggleSwitchClickHelper.ProtectFromParentCapture(AutoStartToggle);
            AutostartExpandHelper.AttachSkipInitialAnimation(AutoStartExpander);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _state = e.Parameter as AppState;
            if (_state == null)
                return;

            _isInitializing = true;

            ThemeComboBox.Items.Clear();
            ThemeComboBox.Items.Add(Strings.Get("Settings_Theme_System"));
            ThemeComboBox.Items.Add(Strings.Get("Settings_Theme_Light"));
            ThemeComboBox.Items.Add(Strings.Get("Settings_Theme_Dark"));
            // 項目の並び（システム / ライト / ダーク）と設定値を明示的に対応付ける。
            // enum の数値をそのまま添字に使うと、並び替え時に別の項目が選ばれてしまうため。
            ThemeComboBox.SelectedIndex = _state.Settings.ThemePreference switch
            {
                AppThemePreference.Light => 1,
                AppThemePreference.Dark => 2,
                _ => 0
            };

            AutoStartToggle.IsOn = _state.Settings.AutoStart;
            AutostartTaskOnlyCheckBox.IsChecked = _state.Settings.UseLogonTask;
            HideTrayIconCheckBox.IsChecked = _state.Settings.HideTrayIcon;
            AutoStartExpander.IsExpanded = true;
            RefreshAutostartInfo();

            LoadTargetMonitorList();
            LoadPowerOffValueList();
            ShowCurrentHotkey();
            RefreshWaitTimeText();
            RefreshTargetMonitorText();

            // ページを開き直したときにテスト中なら、ボタンを押せない状態に合わせる
            TestOffButton.IsEnabled = _state.SubMonitor?.IsTestOffRunning != true;

            _isInitializing = false;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            // キー入力の受付中にページを離れたら、外していたホットキーを登録し直す
            CancelHotkeyCapture();
            base.OnNavigatedFrom(e);
        }

        // ───────── 対象モニター ─────────

        // ComboBox の各項目に対応する固定 ID（先頭の null は「自動」）
        private readonly List<string?> _targetMonitorPaths = new();

        private void LoadTargetMonitorList()
        {
            if (_state?.SubMonitor == null)
                return;

            TargetMonitorComboBox.Items.Clear();
            _targetMonitorPaths.Clear();

            TargetMonitorComboBox.Items.Add(Strings.Get("Settings_TargetMonitor_Auto"));
            _targetMonitorPaths.Add(null);

            foreach (MonitorInfo monitor in _state.SubMonitor.GetMonitors())
            {
                // 製品名が取れないモニターは DISPLAYn だけを出す。メインには印を付けて、誤ってメインを消さないようにする
                string name = string.IsNullOrEmpty(monitor.FriendlyName)
                    ? monitor.ShortDeviceName
                    : Strings.Format("Settings_TargetMonitor_ItemFormat", monitor.FriendlyName, monitor.ShortDeviceName);
                if (monitor.IsPrimary)
                    name += Strings.Get("Settings_TargetMonitor_PrimarySuffix");

                TargetMonitorComboBox.Items.Add(name);
                _targetMonitorPaths.Add(monitor.DevicePath);
            }

            string? saved = _state.Settings.TargetMonitorDevicePath;
            int index = _targetMonitorPaths.FindIndex(p => string.Equals(p, saved, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                // 保存してあるモニターが今つながっていない。勝手に選び直さず、「未接続」として選んだままにする
                TargetMonitorComboBox.Items.Add(Strings.Get("Settings_TargetMonitor_Disconnected"));
                _targetMonitorPaths.Add(saved);
                index = _targetMonitorPaths.Count - 1;
            }

            TargetMonitorComboBox.SelectedIndex = index;
        }

        private void TargetMonitorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || _state?.SubMonitor == null)
                return;

            int index = TargetMonitorComboBox.SelectedIndex;
            if (index < 0 || index >= _targetMonitorPaths.Count)
                return;

            _state.SubMonitor.SetTargetMonitor(_targetMonitorPaths[index]);
            RefreshTargetMonitorText();
        }

        /// <summary>テスト消灯カードの説明欄に、今の対象（「自動」なら実際に選ばれる 1 枚）を出す。</summary>
        private void RefreshTargetMonitorText()
        {
            MonitorInfo? target = MonitorEnumerator.Resolve(_state?.Settings.TargetMonitorDevicePath);

            TargetMonitorText.Text = target == null
                ? Strings.Get("Settings_TargetMonitor_NotFound")
                : Strings.Format(
                    "Settings_TargetMonitor_Format",
                    target.ShortDeviceName,
                    target.Bounds.Width,
                    target.Bounds.Height);
        }

        // ───────── 消灯時の D6 値 ─────────

        // ComboBox の並び（0 = 4 スタンバイ、1 = 5 電源オフ）と D6 の値の対応
        private static readonly uint[] PowerOffValues = { AppConstants.PowerModeStandby, AppConstants.PowerModeOff };

        private void LoadPowerOffValueList()
        {
            if (_state == null)
                return;

            PowerOffValueComboBox.Items.Clear();
            PowerOffValueComboBox.Items.Add(Strings.Get("Settings_PowerOffValue_Standby"));
            PowerOffValueComboBox.Items.Add(Strings.Get("Settings_PowerOffValue_Off"));

            int index = Array.IndexOf(PowerOffValues, _state.Settings.PowerOffValue);
            PowerOffValueComboBox.SelectedIndex = index < 0 ? 0 : index;
        }

        private void PowerOffValueComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || _state?.SubMonitor == null)
                return;

            int index = PowerOffValueComboBox.SelectedIndex;
            if (index >= 0 && index < PowerOffValues.Length)
                _state.SubMonitor.SetPowerOffValue(PowerOffValues[index]);
        }

        // ───────── ホットキー ─────────

        /// <summary>キー入力の受付中か。</summary>
        private bool _isCapturingHotkey;

        /// <summary>
        /// 受付を終えたキーの「離した」イベントを捨てるか。Space / Enter はボタンを離したときに Click が起きるので、
        /// これを捨てないと確定した直後にまた受付が始まってしまう。
        /// </summary>
        private bool _swallowNextKeyUp;

        private void ShowCurrentHotkey()
        {
            if (_state == null)
                return;

            HotkeyButton.Content = HotkeyService.Format(_state.Settings.HotkeyModifiers, _state.Settings.HotkeyVirtualKey);
        }

        private void HotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_state?.SubMonitor == null || _isCapturingHotkey)
                return;

            // 受付の間は今のホットキーを外す（押したときにサブの切り替えが動かないように）
            _isCapturingHotkey = true;
            _state.SubMonitor.BeginHotkeyCapture();
            HotkeyButton.Content = Strings.Get("Settings_Hotkey_Capturing");
            HotkeyStatusText.Text = string.Empty;
        }

        private void HotkeyButton_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (!_isCapturingHotkey || _state?.SubMonitor == null)
                return;

            // 受付中のキーはボタン操作や Alt のメニュー移動に使わせない
            e.Handled = true;

            if (e.Key == VirtualKey.Escape)
            {
                _swallowNextKeyUp = true;
                CancelHotkeyCapture();
                return;
            }

            // 修飾キーだけを押した段階では、まだ確定しない（次の通常キーを待つ）
            if (IsModifierKey(e.Key))
                return;

            uint modifiers = ReadCurrentModifiers();
            if (modifiers == 0)
            {
                // 修飾キー無しの 1 文字はふだんの入力とぶつかるので受け付けない
                HotkeyStatusText.Text = Strings.Get("Settings_Hotkey_NeedModifier");
                return;
            }

            _swallowNextKeyUp = true;
            _isCapturingHotkey = false;
            bool ok = _state.SubMonitor.ApplyHotkey(modifiers, (uint)e.Key);
            HotkeyStatusText.Text = ok ? string.Empty : Strings.Get("Settings_Hotkey_RegisterFailed");
            ShowCurrentHotkey();
        }

        private void HotkeyButton_PreviewKeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (_swallowNextKeyUp)
            {
                _swallowNextKeyUp = false;
                e.Handled = true;
            }
        }

        private void HotkeyButton_LostFocus(object sender, RoutedEventArgs e) => CancelHotkeyCapture();

        /// <summary>受付を取り消して、元のホットキーを登録し直す（受付中でなければ何もしない）。</summary>
        private void CancelHotkeyCapture()
        {
            if (!_isCapturingHotkey)
                return;

            _isCapturingHotkey = false;
            _state?.SubMonitor?.CancelHotkeyCapture();
            HotkeyStatusText.Text = string.Empty;
            ShowCurrentHotkey();
        }

        private static bool IsModifierKey(VirtualKey key) => key is
            VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl or
            VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu or
            VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift or
            VirtualKey.LeftWindows or VirtualKey.RightWindows;

        /// <summary>今押されている修飾キーを RegisterHotKey の形（MOD_CONTROL など）で返す。</summary>
        private static uint ReadCurrentModifiers()
        {
            uint modifiers = 0;
            if (IsDown(VirtualKey.Control)) modifiers |= AppConstants.ModControl;
            if (IsDown(VirtualKey.Menu)) modifiers |= AppConstants.ModAlt;
            if (IsDown(VirtualKey.Shift)) modifiers |= AppConstants.ModShift;
            if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows)) modifiers |= AppConstants.ModWin;
            return modifiers;

            static bool IsDown(VirtualKey key) =>
                InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
        }

        // ───────── 待ち時間 ─────────

        /// <summary>Windows の「画面をオフにする」時間を表示する（例: 10 分（AC 電源）/ なし（自動消灯しない））。</summary>
        private void RefreshWaitTimeText()
        {
            if (_state?.SubMonitor == null)
                return;

            PowerTimeout timeout = _state.SubMonitor.GetCurrentTimeout();
            if (timeout.Seconds == 0)
            {
                WaitTimeText.Text = Strings.Get("Settings_WaitTime_None");
                return;
            }

            // 割り切れる最大の単位で出す（Windows の設定画面と同じ見え方にするため）
            uint s = timeout.Seconds;
            string amount = s % 3600 == 0 ? Strings.Format("Settings_WaitTime_Hours", s / 3600)
                : s % 60 == 0 ? Strings.Format("Settings_WaitTime_Minutes", s / 60)
                : Strings.Format("Settings_WaitTime_Seconds", s);

            WaitTimeText.Text = Strings.Format(timeout.IsAcPower ? "Settings_WaitTime_Ac" : "Settings_WaitTime_Dc", amount);
        }

        // ───────── テスト消灯 ─────────

        private async void TestOffButton_Click(object sender, RoutedEventArgs e)
        {
            if (_state?.SubMonitor == null)
                return;

            // 消灯 → 待機 → 点灯が終わるまでボタンを止める（連打で消灯が重なるのを防ぐ）
            TestOffButton.IsEnabled = false;
            try
            {
                await _state.SubMonitor.RunTestOffAsync();
            }
            finally
            {
                TestOffButton.IsEnabled = true;
                RefreshTargetMonitorText();
            }
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || _state == null)
                return;

            if (ThemeComboBox.SelectedIndex < 0)
                return;

            var preference = ThemeComboBox.SelectedIndex switch
            {
                1 => AppThemePreference.Light,
                2 => AppThemePreference.Dark,
                _ => AppThemePreference.System
            };
            if (preference == _state.Settings.ThemePreference)
                return;

            ThemeService.SetPreference(preference);
            _state.Settings.ThemePreference = preference;
            _state.Settings.Save();
        }

        private void RefreshAutostartInfo()
        {
            bool enabled = _state?.Settings.AutoStart ?? false;
            bool useLogonTask = _state?.Settings.UseLogonTask ?? true;

            // 種類（タスク / スタートアップ）とパスは、今選んでいる方式で表示する
            AutostartTypeLine.Text = AutostartInfoFormatter.FormatTypeLine(
                enabled,
                useLogonTask,
                Strings.Get,
                (key, args) => Strings.Format(key, args));

            AutostartPathLine.Text = AutostartInfoFormatter.FormatPathLine(
                enabled,
                StartupManager.GetRegisteredCommand(preferLogonTask: useLogonTask),
                Strings.Get,
                (key, args) => Strings.Format(key, args));
        }

        private void RefreshAutostartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_state == null)
                return;

            if (_state.Settings.AutoStart)
                StartupManager.SyncAutostartWithSettings(true, _state.Settings.UseLogonTask);

            RefreshAutostartInfo();
        }

        private void AutoStartToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _state == null)
                return;

            bool requested = AutoStartToggle.IsOn;
            if (!StartupManager.SyncAutostartWithSettings(requested, _state.Settings.UseLogonTask) && requested)
            {
                _isInitializing = true;
                AutoStartToggle.IsOn = false;
                _isInitializing = false;
                return;
            }

            _state.Settings.AutoStart = requested;
            _state.Settings.Save();
            RefreshAutostartInfo();
        }

        private void AutostartTaskOnlyCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _state == null)
                return;

            bool useLogonTask = AutostartTaskOnlyCheckBox.IsChecked == true;
            if (useLogonTask == _state.Settings.UseLogonTask)
                return;

            _state.Settings.UseLogonTask = useLogonTask;

            // 自動起動が ON のときだけ、登録を新しい方式へ付け替える（OFF なら次に ON にしたとき反映される）
            if (_state.Settings.AutoStart)
            {
                bool ok = StartupManager.SyncAutostartWithSettings(true, useLogonTask);
                if (!ok)
                {
                    // 切替失敗時は元の方式に戻す（設定と実際の登録がずれないように）
                    _isInitializing = true;
                    _state.Settings.UseLogonTask = !useLogonTask;
                    AutostartTaskOnlyCheckBox.IsChecked = !useLogonTask;
                    _isInitializing = false;
                    return;
                }
            }

            _state.Settings.Save();
            RefreshAutostartInfo();
        }

        private void HideTrayIconCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _state == null)
                return;

            bool hide = HideTrayIconCheckBox.IsChecked == true;
            if (hide == _state.Settings.HideTrayIcon)
                return;

            _state.Settings.HideTrayIcon = hide;
            _state.Settings.Save();
            _state.ApplyTrayIconVisibility?.Invoke();
        }
    }
}
