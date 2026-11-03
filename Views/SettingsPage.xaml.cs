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
        private readonly ThemeComboBoxBinder _theme;
        private readonly AutostartSettingsController _autostart;

        public SettingsPage()
        {
            InitializeComponent();
            // 設定画面の ComboBox は 3 つとも同じ見た目（右寄せ・選択中の文字に合わせた幅・同じ余白）にそろえる
            CompactComboBoxHelper.AttachFitToSelectedText(TargetMonitorComboBox);
            CompactComboBoxHelper.AttachFitToSelectedText(PowerOffValueComboBox);
            _theme = new ThemeComboBoxBinder(
                ThemeComboBox,
                get: () => _state?.Settings.ThemePreference ?? AppThemePreference.System,
                set: value => { if (_state != null) _state.Settings.ThemePreference = value; },
                save: () => _state?.Settings.Save());

            _autostart = new AutostartSettingsController(new AutostartSettingsControls
            {
                AutoStartToggle = AutoStartToggle,
                Expander = AutoStartExpander,
                UseLogonTaskCheckBox = AutostartTaskOnlyCheckBox,
                TypeLine = AutostartTypeLine,
                PathLine = AutostartPathLine,
                RefreshButton = RefreshAutostartButton,
                HideTrayIconCheckBox = HideTrayIconCheckBox,
                ShowStartupNotificationCheckBox = ShowStartupNotificationCheckBox
            }, AppSetup.Autostart);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _state = e.Parameter as AppState;
            if (_state == null)
                return;

            _isInitializing = true;

            _theme.Load();
            _autostart.Bind(AutostartSettingsAccess.For(_state.Settings, () => _state.ApplyTrayIconVisibility?.Invoke()));

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

            // キー 0 は「なし」（ホットキーを使わない）
            bool isNone = _state.Settings.HotkeyVirtualKey == 0;
            HotkeyButton.Content = isNone
                ? Strings.Get("Settings_Hotkey_None")
                : HotkeyService.Format(_state.Settings.HotkeyModifiers, _state.Settings.HotkeyVirtualKey);

            // 設定上はキーがあるのに登録できていない（他のアプリが先に使っている）なら、押しても効かないことを出しておく。
            // 表示だけ Ctrl+Alt+M のままだと、効かない理由が分からないため
            bool notWorking = !isNone && _state.SubMonitor?.IsHotkeyRegistered == false;
            SetHotkeyStatus(notWorking ? Strings.Get("Settings_Hotkey_NotRegistered") : string.Empty);
        }

        /// <summary>
        /// ホットキー行の説明欄に文言を出す。空なら欄ごと畳む
        /// （空の TextBlock が残ると見出しが上に寄り、他の行と縦位置がずれるため）。
        /// </summary>
        private void SetHotkeyStatus(string text)
        {
            HotkeyStatusText.Text = text;
            HotkeyStatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void HotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_state?.SubMonitor == null || _isCapturingHotkey)
                return;

            // 受付の間は今のホットキーを外す（押したときにサブの切り替えが動かないように）
            _isCapturingHotkey = true;
            _state.SubMonitor.BeginHotkeyCapture();
            HotkeyButton.Content = Strings.Get("Settings_Hotkey_Capturing");
            // 受付中は操作の案内（Esc で取消 / Delete・Backspace で「なし」）を出す
            SetHotkeyStatus(Strings.Get("Settings_Hotkey_CaptureHint"));
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

            // 修飾キー無しの Delete / Backspace は「なし」にする操作（ホットキーを使わない）
            if (modifiers == 0 && e.Key is VirtualKey.Delete or VirtualKey.Back)
            {
                _swallowNextKeyUp = true;
                _isCapturingHotkey = false;
                _state.SubMonitor.ClearHotkey();
                ShowCurrentHotkey();
                return;
            }

            if (modifiers == 0)
            {
                // 修飾キー無しの 1 文字はふだんの入力とぶつかるので受け付けない
                SetHotkeyStatus(Strings.Get("Settings_Hotkey_NeedModifier"));
                return;
            }

            _swallowNextKeyUp = true;
            _isCapturingHotkey = false;
            bool ok = _state.SubMonitor.ApplyHotkey(modifiers, (uint)e.Key);
            ShowCurrentHotkey();
            // 新しいキーが取れなかったときは、今の状態表示より「その組み合わせは使えない」を優先して出す
            if (!ok)
                SetHotkeyStatus(Strings.Get("Settings_Hotkey_RegisterFailed"));
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
    }
}
