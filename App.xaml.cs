// v0.5.1

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

            // UI 以外のスレッドの未処理例外はプロセスが必ず落ちる。落ちる前に、消えたままのサブだけは点灯に戻す
            AppDomain.CurrentDomain.UnhandledException += (_, _) =>
                _runtime?.RestoreSubMonitorAfterCrash("domain-unhandled");
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            var settings = Settings.Load();
            bool launchInBackground = AppBootstrap.HasCommandLineArg(AppBootstrap.BackgroundArg);
            bool requestInteractiveShow = !launchInBackground;

            if (!SingleInstance.TryBecomePrimaryInstance(requestInteractiveShow))
            {
                // 既に起動中のインスタンスへ表示依頼を送ったので、このプロセスは何もせず終了する
                Exit();
                return;
            }

            // スタートアップ（レジストリ）方式を選んでいるときは移行で Run を消さない
            if (settings.UseLogonTask)
                StartupManager.MigrateFromLegacyIfNeeded();
#if !DEBUG
            // 起動時にログオンタスク（またはスタートアップ）を「今動いている exe」へ合わせ直す（primary だけが行う）。
            // Debug ビルドで行うと、インストール版の登録が bin\Debug の exe に書き換わってしまうため Release のみ。
            // （設定画面で自動起動を切り替えたときや --sync-autostart の明示同期は Debug でも行う）
            StartupManager.SyncAutostartWithSettings(settings.AutoStart, settings.UseLogonTask);
#endif

            _runtime = new AppRuntime(this, settings);
            _runtime.Start(launchInBackground, requestInteractiveShow);
        }
    }
}
