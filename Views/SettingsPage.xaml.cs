using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SubMonitorSleeper.Services;
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
            RefreshTargetMonitorText();

            // ページを開き直したときにテスト中なら、ボタンを押せない状態に合わせる
            TestOffButton.IsEnabled = _state.IsTestOffRunning?.Invoke() != true;

            _isInitializing = false;
        }

        /// <summary>対象モニター（メイン以外の 1 枚）のデバイス名と解像度を表示する。</summary>
        private void RefreshTargetMonitorText()
        {
            MonitorDescription? target = DdcController.FindTargetMonitor();

            // 「\\.\DISPLAY2」の先頭の「\\.\」は読み手に意味がないので外して出す
            TargetMonitorText.Text = target == null
                ? Strings.Get("Settings_TargetMonitor_NotFound")
                : Strings.Format(
                    "Settings_TargetMonitor_Format",
                    target.DeviceName.Replace(@"\\.\", string.Empty),
                    target.Width,
                    target.Height);
        }

        private async void TestOffButton_Click(object sender, RoutedEventArgs e)
        {
            if (_state?.RunTestOffAsync == null)
                return;

            // 消灯 → 待機 → 点灯が終わるまでボタンを止める（連打で消灯が重なるのを防ぐ）
            TestOffButton.IsEnabled = false;
            try
            {
                await _state.RunTestOffAsync();
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
