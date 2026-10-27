// v0.1.0

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using SubMonitorSleeper.Services;
using WinUiShared;

namespace SubMonitorSleeper
{
    /// <summary>
    /// プロセス寿命を管理する常駐オブジェクト。共通処理（二重起動・トレイ・復帰監視・終了の一本化）は
    /// WinUiShared.AppRuntimeBase にあり、ここにはこのアプリ固有の部分だけを書く。
    /// 固有部分: サブモニターの D6 消灯・点灯、トレイの「今すぐ消す / 今すぐ点ける」、
    /// 終了・クラッシュ・スリープ復帰・ロック解除時の点灯への復旧、テスト消灯。
    /// </summary>
    public sealed class AppRuntime : AppRuntimeBase
    {
        private readonly Settings _settings;
        private readonly AppState _appState;
        private readonly DdcController _ddc = new();

        // 消灯・点灯の状態は UI スレッドと DDC 用のバックグラウンドスレッドの両方から触るので、このロックで守る
        private readonly object _stateGate = new();

        // DDC 命令を「頼まれた順」に 1 本ずつ流すための鎖。Task.Run を個別に投げると実行順が入れ替わり、
        // 「消す → すぐ点ける」が「点ける → 消す」になってしまうことがあるため、前の命令の後ろにつなぐ
        private Task _ddcChain = Task.CompletedTask;

        /// <summary>最後に消灯命令を出した時刻（UTC）。復帰時の点灯をするかどうかの判断に使う。</summary>
        private DateTime _lastOffCommandUtc = DateTime.MinValue;

        /// <summary>今の「復帰ひとまとまり」が始まった時刻と、最後に復帰コールバックが来た時刻（UTC）。</summary>
        private DateTime _resumeBurstStartUtc = DateTime.MinValue;
        private DateTime _lastResumeApplyUtc = DateTime.MinValue;

        /// <summary>復帰時の点灯失敗を通知した「復帰ひとまとまり」の開始時刻（同じ復帰で何度も通知しないため）。</summary>
        private DateTime _resumeFailureNotifiedBurstUtc = DateTime.MinValue;

        /// <summary>テスト消灯の実行中か（0 = いいえ / 1 = はい）。連打で二重に走らないようにする。</summary>
        private int _testOffRunning;

        public AppRuntime(Application app, Settings settings)
            : base(app, settings)
        {
            _settings = settings;
            _appState = new AppState(_settings)
            {
                // 設定画面の「トレイアイコンを隠す」から呼ばれる
                ApplyTrayIconVisibility = ApplyTrayIconVisibility,
                RunTestOffAsync = RunTestOffAsync,
                IsTestOffRunning = () => Volatile.Read(ref _testOffRunning) != 0
            };
        }

        public AppState AppState => _appState;
        public Settings Settings => _settings;

        protected override Window CreateMainWindow() => new MainWindow(this);

        // ───────── 起動・復帰 ─────────

        protected override void OnStartCore()
        {
            // 起動直後も「復帰」と同じ経路（OnResumeApplyCore）を通す。
            // 前回が強制終了でサブが消えたまま（SubTurnedOffByApp = true）なら、ここで点灯に戻る
            ResumeCoordinator?.BeginStartupPeriod();
        }

        /// <summary>
        /// ウォッチドッグ（30 秒ごと）では何もしない。
        /// false のままだとウォッチドッグが OnResumeApplyCore を呼び、ユーザーが消したサブを 30 秒ごとに点け直してしまう。
        /// </summary>
        protected override bool UseResumeWatchdog => true;

        protected override void OnResumeWatchdogCore()
        {
        }

        /// <summary>
        /// スリープ復帰・画面復帰・ロック解除・起動直後に呼ばれる（即時 + 800〜90000 ms の遅延で何度も来る）。
        /// アプリが消灯したままのサブがあれば点灯に戻す。
        /// </summary>
        protected override void OnResumeApplyCore()
        {
            DateTime now = DateTime.UtcNow;
            lock (_stateGate)
            {
                // 間隔が空いたら新しい復帰とみなし、ひとまとまりの開始時刻を更新する
                if (now - _lastResumeApplyUtc > TimeSpan.FromSeconds(AppConstants.ResumeBurstGapSeconds))
                    _resumeBurstStartUtc = now;
                _lastResumeApplyUtc = now;

                if (!_settings.SubTurnedOffByApp)
                    return;

                // 復帰したあとにユーザーが改めて消した → その意思を優先し、遅延再適用で点け直さない
                if (_lastOffCommandUtc >= _resumeBurstStartUtc)
                    return;

                // 消灯の直後に始まった「復帰」は、消灯で起きた画面構成の変更通知の可能性が高いので無視する
                if (_resumeBurstStartUtc - _lastOffCommandUtc < TimeSpan.FromSeconds(AppConstants.ResumeIgnoreAfterOffSeconds))
                    return;
            }

            // サブ未接続（ノート PC を外で使っているなど）は復帰のたびに通知すると煩わしいので黙る
            _ = TurnOnCoreAsync("resume", notifyIfNotFound: false);
        }

        // ───────── 消灯・点灯（トレイ・設定画面から呼ぶ。UI スレッド） ─────────

        /// <summary>サブを今すぐ消灯する。</summary>
        public Task<DdcResult> TurnSubOffAsync() => TurnOffCoreAsync("manual");

        /// <summary>サブを今すぐ点灯する。</summary>
        public Task<DdcResult> TurnSubOnAsync() => TurnOnCoreAsync("manual", notifyIfNotFound: true);

        /// <summary>
        /// テスト消灯: 消灯して <see cref="AppConstants.TestOffSeconds"/> 秒後に自動で点灯する（DDC が効くかの確認用）。
        /// 実行中にもう一度呼ばれても何もしない。
        /// </summary>
        public async Task RunTestOffAsync()
        {
            if (Interlocked.Exchange(ref _testOffRunning, 1) != 0)
                return;

            try
            {
                AppendLifetimeLog("test-off start");
                DdcResult off = await TurnOffCoreAsync("test");
                if (off != DdcResult.Success)
                    return;

                await Task.Delay(TimeSpan.FromSeconds(AppConstants.TestOffSeconds));

                // 待っている間に終了した場合は、終了処理（OnExitCore）が点灯に戻すのでここでは触らない
                if (IsExitingProcess)
                    return;

                DdcResult on = await TurnOnCoreAsync("test", notifyIfNotFound: true);
                AppendLifetimeLog($"test-off done on={on}");
            }
            finally
            {
                Interlocked.Exchange(ref _testOffRunning, 0);
            }
        }

        private Task<DdcResult> TurnOffCoreAsync(string reason)
        {
            uint offValue = GetPowerOffValue();
            lock (_stateGate)
            {
                // 命令を出す「前」に記録して保存する。送信中に強制終了されても、次回起動時に点灯へ戻せるようにするため
                _lastOffCommandUtc = DateTime.UtcNow;
                _settings.SubTurnedOffByApp = true;
            }
            SaveSettingsOnUiThread();

            return EnqueueDdc(() =>
            {
                DdcResult result = _ddc.SetPowerMode(offValue, verify: false);
                if (result is DdcResult.MonitorNotFound or DdcResult.NoPhysicalMonitor)
                {
                    // 命令そのものを送れていない＝消えていないので、「消したまま」の記録を取り消す
                    lock (_stateGate)
                        _settings.SubTurnedOffByApp = false;
                    SaveSettingsOnUiThread();
                }

                if (result != DdcResult.Success)
                    ReportFailure(isOff: true, result, reason);

                return result;
            });
        }

        private Task<DdcResult> TurnOnCoreAsync(string reason, bool notifyIfNotFound)
        {
            return EnqueueDdc(() =>
            {
                // 点灯は読み戻して確認する（点いたつもりで消えたまま、が一番困るため）
                DdcResult result = _ddc.SetPowerMode(AppConstants.PowerModeOn, verify: true);
                if (result == DdcResult.Success)
                {
                    lock (_stateGate)
                        _settings.SubTurnedOffByApp = false;
                    SaveSettingsOnUiThread();
                    return result;
                }

                if (result != DdcResult.MonitorNotFound || notifyIfNotFound)
                    ReportFailure(isOff: false, result, reason);

                return result;
            });
        }

        /// <summary>DDC 命令を前の命令の後ろにつないで、バックグラウンドスレッドで実行する。</summary>
        private Task<DdcResult> EnqueueDdc(Func<DdcResult> work)
        {
            lock (_stateGate)
            {
                // 終了処理が始まっていたら送らない（終了時の点灯より後に、待ち行列の消灯が走って消えたまま残るのを防ぐ）
                Task<DdcResult> next = _ddcChain.ContinueWith(
                    _ => IsExitingProcess ? DdcResult.Cancelled : work(),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);

                // ContinueWith（None）は前の命令が例外で終わっても実行されるので、1 回の失敗で鎖が止まらない
                _ddcChain = next;
                return next;
            }
        }

        /// <summary>設定の消灯値（4 か 5）。手で settings.json を書き換えて変な値が入っていたら 4 にする。</summary>
        private uint GetPowerOffValue() =>
            _settings.PowerOffValue is AppConstants.PowerModeStandby or AppConstants.PowerModeOff
                ? _settings.PowerOffValue
                : AppConstants.DefaultPowerOffValue;

        // ───────── 通知 ─────────

        private void ReportFailure(bool isOff, DdcResult result, string reason)
        {
            AppendLifetimeLog($"ddc-{(isOff ? "off" : "on")}-failed reason={reason} result={result}");

            if (reason == "resume")
            {
                // 復帰時は同じ復帰の中で何度も再試行するので、通知はその復帰で 1 回だけにする
                lock (_stateGate)
                {
                    if (_resumeFailureNotifiedBurstUtc == _resumeBurstStartUtc)
                        return;
                    _resumeFailureNotifiedBurstUtc = _resumeBurstStartUtc;
                }
            }

            string title = Strings.Get(isOff ? "Tray_DdcOffFailedTitle" : "Tray_DdcOnFailedTitle");
            string body = Strings.Get(result switch
            {
                DdcResult.MonitorNotFound => "Ddc_MonitorNotFound",
                DdcResult.NoPhysicalMonitor => "Ddc_NoPhysicalMonitor",
                DdcResult.VerifyFailed => "Ddc_VerifyFailed",
                _ => "Ddc_SetFailed"
            });

            // トレイは UI スレッドの持ち物なので、バックグラウンドからは UI スレッドに積んで出す
            UiDispatcher.TryEnqueue(() => TrayIcon?.ShowBalloon(title, body, isWarning: true));
        }

        private void SaveSettingsOnUiThread()
        {
            // 設定画面（UI スレッド）の保存と同時に書いてファイルを取り合わないよう、保存は UI スレッドに集める
            if (UiDispatcher.HasThreadAccess)
                _settings.Save();
            else
                UiDispatcher.TryEnqueue(_settings.Save);
        }

        // ───────── トレイ ─────────

        protected override IReadOnlyList<TrayMenuItem>? GetTrayMenuItems() =>
        [
            new TrayMenuItem(Strings.Get("Tray_TurnOffNow"), () => _ = TurnSubOffAsync()),
            new TrayMenuItem(Strings.Get("Tray_TurnOnNow"), () => _ = TurnSubOnAsync())
        ];

        // ───────── 終了・クラッシュ時の復旧 ─────────

        protected override void OnExitCore()
        {
            // 終了時は必ず点灯に戻す（消えたまま常駐が居なくなると、手で電源ボタンを押すしかなくなるため）
            RestoreSubMonitorSync("exit");
        }

        /// <summary>
        /// 未処理例外の発生時に呼ぶ（App から。任意のスレッド）。消灯したままなら点灯へ戻す。
        /// ここ自体が例外を投げると元の例外処理を邪魔するので、すべて握りつぶす。
        /// </summary>
        public void RestoreSubMonitorAfterCrash(string reason)
        {
            try
            {
                RestoreSubMonitorSync(reason);
            }
            catch
            {
            }
        }

        /// <summary>消灯したままなら、その場で（同期的に）点灯へ戻す。終了・クラッシュの経路では非同期を待てないため。</summary>
        private void RestoreSubMonitorSync(string reason)
        {
            lock (_stateGate)
            {
                if (!_settings.SubTurnedOffByApp)
                    return;
            }

            // DdcController 内のロックで、走行中の命令が終わってから送られる（I2C の命令は混ざらない）
            DdcResult result = _ddc.SetPowerMode(AppConstants.PowerModeOn, verify: true);
            AppendLifetimeLog($"restore reason={reason} result={result}");
            if (result != DdcResult.Success)
                return;

            lock (_stateGate)
                _settings.SubTurnedOffByApp = false;

            try
            {
                _settings.Save();
            }
            catch
            {
            }
        }
    }
}
