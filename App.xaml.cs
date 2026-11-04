// v1.1.1

using Microsoft.UI.Xaml;
using System;
using WinUiShared;

namespace SubMonitorSleeper
{
    public partial class App : Application
    {
        private AppRuntime? _runtime;

        internal static AppRuntime Runtime =>
            (Current as App)?._runtime
            ?? throw new InvalidOperationException("App runtime is not initialized.");

        public App()
        {
            InitializeComponent();
            // × で MainWindow を破棄してもプロセスは継続する（トレイ常駐 / 明示 Exit まで終了しない）。
            // ウィンドウ型でも同じ設定にし、終了は AppRuntimeBase.ExitApplication の 1 経路に揃える
            DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
            UnhandledException += (_, e) =>
            {
                // UI スレッドの未処理例外: まずサブを点灯に戻してから、共通処理（ログ記録・落とさない）へ
                _runtime?.RestoreSubMonitorAfterCrash("unhandled");
                AppBootstrap.HandleUnhandledException(e);
            };
            // UI スレッド以外の例外と、待たれずに失敗した Task の例外も lifetime.log に残す。
            // UI 以外のスレッドの未処理例外はプロセスが必ず落ちるので、落ちる前に消えたままのサブだけは点灯に戻す
            AppBootstrap.InstallGlobalExceptionLogging(() => _runtime?.RestoreSubMonitorAfterCrash("domain-unhandled"));
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            var settings = Settings.Load();

            // 単一インスタンス・自動起動の移行と同期は共通処理。二重起動なら null（既存の窓に表示を頼んで終了済み）
            PrimaryLaunch? launch = AppBootstrap.BeginPrimaryLaunch(this, settings);
            if (launch == null)
                return;

            _runtime = new AppRuntime(this, settings);
            _runtime.Start(launch.LaunchInBackground, launch.RequestInteractiveShow);
        }
    }
}
