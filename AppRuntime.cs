// v0.5.2

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
    /// 固有部分: サブモニターの D6 消灯・点灯、自動判定（IdleMonitor）、ホットキー、
    /// トレイの「今すぐ消す / 今すぐ点ける / 一時停止」、終了・クラッシュ・スリープ復帰・ロック解除時の点灯への復旧。
    /// </summary>
    public sealed class AppRuntime : AppRuntimeBase, ISubMonitorControl
    {
        /// <summary>起動時のホットキー登録失敗を通知するまでの待ち時間（トレイと「常駐準備完了」通知が出た後にする）。</summary>
        private const int StartupHotkeyFailureNoticeDelayMs = 7000;

        private readonly Settings _settings;
        private readonly AppState _appState;
        private readonly DdcController _ddc = new();

        // 消灯・点灯の状態は UI スレッド・DDC 用スレッド・判定ループの 3 か所から触るので、このロックで守る
        private readonly object _stateGate = new();

        // DDC 命令を「頼まれた順」に 1 本ずつ流すための鎖。Task.Run を個別に投げると実行順が入れ替わり、
        // 「消す → すぐ点ける」が「点ける → 消す」になってしまうことがあるため、前の命令の後ろにつなぐ
        private Task _ddcChain = Task.CompletedTask;

        private IdleMonitor? _idleMonitor;
        private HotkeyService? _hotkey;

        /// <summary>今サブが消えている理由（None = 点灯中）。保存はしない（起動時の復旧は SubTurnedOffByApp で行う）。</summary>
        private SubOffKind _offKind = SubOffKind.None;

        /// <summary>自動消灯の一時停止（トレイ、または D6 失敗）。保存しないので再起動で解除される。</summary>
        private bool _autoPaused;

        /// <summary>この時刻（Environment.TickCount64）までは自動消灯しない（手動点灯後の猶予）。</summary>
        private long _autoOffSuppressedUntilTick;

        /// <summary>点灯の命令を送っている最中か（判定ループが 0.5 秒ごとに同じ点灯を頼んでくるのを 1 回にまとめる）。</summary>
        private bool _wakeInFlight;

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
                SubMonitor = this
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

            StartHotkey();

            // 判定ループのコールバックはバックグラウンドスレッドから来るので、UI スレッドに積んでから処理する
            _idleMonitor = new IdleMonitor(
                GetIdleContext,
                () => UiDispatcher.TryEnqueue(OnAutoOffRequested),
                reason => UiDispatcher.TryEnqueue(() => OnWakeRequested(reason)));
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
            // 復帰したら「使っていない時間」は最初から数え直す（スリープ中の時間を数えて、復帰直後に消さないため）
            _idleMonitor?.ResetIdleCount();

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
            _ = TurnOnCoreAsync("resume", notifyIfNotFound: false, isManual: false);
        }

        // ───────── 自動判定（IdleMonitor からの依頼。UI スレッド） ─────────

        /// <summary>判定ループに渡す今の状態（バックグラウンドスレッドから呼ばれる）。</summary>
        private IdleContext GetIdleContext()
        {
            lock (_stateGate)
            {
                // 強制終了後の起動直後など、理由が分からないまま消えている場合は「手動」扱い（ウィンドウ出現で点けない）
                SubOffKind kind = _settings.SubTurnedOffByApp && _offKind == SubOffKind.None
                    ? SubOffKind.Manual
                    : _settings.SubTurnedOffByApp ? _offKind : SubOffKind.None;

                return new IdleContext(_settings.TargetMonitorDevicePath, kind, _autoPaused, _autoOffSuppressedUntilTick);
            }
        }

        private void OnAutoOffRequested()
        {
            lock (_stateGate)
            {
                // 判定から実行までの間に状況が変わっていたら（手動で消した・一時停止した）やめる
                if (_settings.SubTurnedOffByApp || _autoPaused)
                    return;
            }

            AppendLifetimeLog("auto-off");
            _ = TurnOffCoreAsync("auto", SubOffKind.Auto);
        }

        private void OnWakeRequested(WakeReason reason)
        {
            lock (_stateGate)
            {
                if (!_settings.SubTurnedOffByApp || _wakeInFlight)
                    return;
                _wakeInFlight = true;
            }

            AppendLifetimeLog($"wake reason={reason}");
            _ = WakeAsync();

            async Task WakeAsync()
            {
                try
                {
                    DdcResult result = await TurnOnCoreAsync(reason.ToString(), notifyIfNotFound: true, isManual: true);
                    if (result != DdcResult.Success)
                    {
                        // 点けられなかったときに、ウィンドウがある限り 0.5 秒ごとに再送し続けないよう「手動で消した」扱いに変える
                        lock (_stateGate)
                        {
                            if (_settings.SubTurnedOffByApp)
                                _offKind = SubOffKind.Manual;
                        }
                    }
                }
                finally
                {
                    lock (_stateGate)
                        _wakeInFlight = false;
                }
            }
        }

        // ───────── ホットキー ─────────

        private void StartHotkey()
        {
            try
            {
                _hotkey = new HotkeyService();
                _hotkey.Pressed += OnHotkeyPressed;
            }
            catch (Exception ex)
            {
                AppendLifetimeLog($"hotkey-window-failed {ex.Message}");
                return;
            }

            if (_hotkey.TryRegister(_settings.HotkeyModifiers, _settings.HotkeyVirtualKey))
                return;

            // 他のアプリが同じ組み合わせを使っていると登録できない。トレイが出てから知らせる
            string keys = HotkeyService.Format(_settings.HotkeyModifiers, _settings.HotkeyVirtualKey);
            AppendLifetimeLog($"hotkey-register-failed {keys}");
            _ = Task.Run(async () =>
            {
                await Task.Delay(StartupHotkeyFailureNoticeDelayMs).ConfigureAwait(false);
                UiDispatcher.TryEnqueue(() => TrayIcon?.ShowBalloon(
                    Strings.Get("Tray_HotkeyFailedTitle"),
                    Strings.Format("Tray_HotkeyFailedBody", keys),
                    isWarning: true));
            });
        }

        /// <summary>ホットキー: 押すたびに切り替える（消灯中なら点灯、点灯中なら消灯）。UI スレッドで呼ばれる。</summary>
        private void OnHotkeyPressed()
        {
            bool isOff;
            lock (_stateGate)
                isOff = _settings.SubTurnedOffByApp;

            if (isOff)
                _ = TurnSubOnAsync();
            else
                _ = TurnSubOffAsync();
        }

        // ───────── 消灯・点灯（トレイ・ホットキー・設定画面から呼ぶ。UI スレッド） ─────────

        /// <summary>サブを今すぐ消灯する（手動）。</summary>
        public Task<DdcResult> TurnSubOffAsync() => TurnOffCoreAsync("manual", SubOffKind.Manual);

        /// <summary>サブを今すぐ点灯する（手動。点灯後しばらくは自動消灯しない）。</summary>
        public Task<DdcResult> TurnSubOnAsync() => TurnOnCoreAsync("manual", notifyIfNotFound: true, isManual: true);

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
                DdcResult off = await TurnOffCoreAsync("test", SubOffKind.Manual);
                if (off != DdcResult.Success)
                    return;

                await Task.Delay(TimeSpan.FromSeconds(AppConstants.TestOffSeconds));

                // 待っている間に終了した場合は、終了処理（OnExitCore）が点灯に戻すのでここでは触らない
                if (IsExitingProcess)
                    return;

                DdcResult on = await TurnOnCoreAsync("test", notifyIfNotFound: true, isManual: true);
                AppendLifetimeLog($"test-off done on={on}");
            }
            finally
            {
                Interlocked.Exchange(ref _testOffRunning, 0);
            }
        }

        public bool IsTestOffRunning => Volatile.Read(ref _testOffRunning) != 0;

        private Task<DdcResult> TurnOffCoreAsync(string reason, SubOffKind kind)
        {
            uint offValue = GetPowerOffValue();
            string? targetPath;
            lock (_stateGate)
            {
                // 命令を出す「前」に記録して保存する。送信中に強制終了されても、次回起動時に点灯へ戻せるようにするため
                _lastOffCommandUtc = DateTime.UtcNow;
                _settings.SubTurnedOffByApp = true;
                _offKind = kind;
                targetPath = _settings.TargetMonitorDevicePath;
            }
            SaveSettingsOnUiThread();
            _idleMonitor?.ResetIdleCount();

            return EnqueueDdc(() =>
            {
                DdcResult result = _ddc.SetPowerMode(offValue, verify: false, targetPath);
                if (result is DdcResult.MonitorNotFound or DdcResult.NoPhysicalMonitor)
                {
                    // 命令そのものを送れていない＝消えていないので、「消したまま」の記録を取り消す
                    lock (_stateGate)
                    {
                        _settings.SubTurnedOffByApp = false;
                        _offKind = SubOffKind.None;
                    }
                    SaveSettingsOnUiThread();
                }

                if (result != DdcResult.Success)
                    ReportFailure(isOff: true, result, reason);

                return result;
            });
        }

        /// <param name="isManual">
        /// true なら手動点灯（トレイ・ホットキー・カーソル進入・ウィンドウ出現・テスト）。
        /// 点灯後 <see cref="AppConstants.ManualOnGraceSeconds"/> 秒は自動消灯しない。
        /// </param>
        /// <param name="targetPathOverride">対象を変えるときに旧対象へ送る場合だけ指定する。</param>
        private Task<DdcResult> TurnOnCoreAsync(string reason, bool notifyIfNotFound, bool isManual, string? targetPathOverride = null)
        {
            string? targetPath;
            lock (_stateGate)
            {
                // 猶予は「頼んだ時点」から数える（DDC の応答待ちの間に判定ループが消してしまわないように）
                if (isManual)
                    _autoOffSuppressedUntilTick = Environment.TickCount64 + AppConstants.ManualOnGraceSeconds * 1000L;
                targetPath = targetPathOverride ?? _settings.TargetMonitorDevicePath;
            }

            return EnqueueDdc(() =>
            {
                // 点灯は読み戻して確認する（点いたつもりで消えたまま、が一番困るため）
                DdcResult result = _ddc.SetPowerMode(AppConstants.PowerModeOn, verify: true, targetPath);
                if (result == DdcResult.Success)
                {
                    lock (_stateGate)
                    {
                        _settings.SubTurnedOffByApp = false;
                        _offKind = SubOffKind.None;
                    }
                    SaveSettingsOnUiThread();
                    _idleMonitor?.ResetIdleCount();
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

        // ───────── 設定画面からの操作（ISubMonitorControl。UI スレッド） ─────────

        public IReadOnlyList<MonitorInfo> GetMonitors() => MonitorEnumerator.GetMonitors();

        public void SetTargetMonitor(string? devicePath)
        {
            string? oldPath;
            bool wasOff;
            lock (_stateGate)
            {
                oldPath = _settings.TargetMonitorDevicePath;
                if (string.Equals(oldPath, devicePath, StringComparison.OrdinalIgnoreCase))
                    return;

                wasOff = _settings.SubTurnedOffByApp;
                _settings.TargetMonitorDevicePath = devicePath;
            }
            SaveSettingsOnUiThread();
            _idleMonitor?.ResetIdleCount();

            // 消灯中に対象を変えたら、旧対象を点灯に戻す（新しい対象からは旧対象を操作できなくなり、消えたまま残るため）
            if (wasOff)
                _ = TurnOnCoreAsync("retarget", notifyIfNotFound: true, isManual: true, targetPathOverride: oldPath);
        }

        public void SetPowerOffValue(uint value)
        {
            if (value is not (AppConstants.PowerModeStandby or AppConstants.PowerModeOff))
                return;

            _settings.PowerOffValue = value;
            _settings.Save();
        }

        public PowerTimeout GetCurrentTimeout() => PowerTimeoutReader.Read();

        public void BeginHotkeyCapture() => _hotkey?.Unregister();

        public bool ApplyHotkey(uint modifiers, uint virtualKey)
        {
            if (_hotkey == null)
                return false;

            if (!_hotkey.TryRegister(modifiers, virtualKey))
            {
                // 他のアプリが使っている組み合わせなど。今までのキーに戻しておく
                _hotkey.TryRegister(_settings.HotkeyModifiers, _settings.HotkeyVirtualKey);
                return false;
            }

            _settings.HotkeyModifiers = modifiers;
            _settings.HotkeyVirtualKey = virtualKey;
            _settings.Save();
            return true;
        }

        public void CancelHotkeyCapture() =>
            _hotkey?.TryRegister(_settings.HotkeyModifiers, _settings.HotkeyVirtualKey);

        // ───────── 通知 ─────────

        private void ReportFailure(bool isOff, DdcResult result, string reason)
        {
            AppendLifetimeLog($"ddc-{(isOff ? "off" : "on")}-failed reason={reason} result={result}");

            // D6 が通らない（応答なし・確認できない）なら、勝手に消し続けないよう自動消灯を止める。
            // サブ未接続（MonitorNotFound）は D6 の問題ではないので止めない
            bool pausedNow = false;
            if (result is DdcResult.SetFailed or DdcResult.NoPhysicalMonitor or DdcResult.VerifyFailed)
            {
                lock (_stateGate)
                {
                    pausedNow = !_autoPaused;
                    _autoPaused = true;
                }
            }

            if (reason == "resume")
            {
                // 復帰時は同じ復帰の中で何度も再試行するので、通知はその復帰で 1 回だけにする
                lock (_stateGate)
                {
                    if (_resumeFailureNotifiedBurstUtc == _resumeBurstStartUtc && !pausedNow)
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
            if (pausedNow)
                body += "\n" + Strings.Get("Ddc_AutoPaused");

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

        protected override IReadOnlyList<TrayMenuItem>? GetTrayMenuItems()
        {
            bool paused;
            lock (_stateGate)
                paused = _autoPaused;

            // 右クリックのたびに呼ばれるので、「一時停止」のチェックは今の状態で付ける
            return
            [
                new TrayMenuItem(Strings.Get("Tray_TurnOffNow"), () => _ = TurnSubOffAsync()),
                new TrayMenuItem(Strings.Get("Tray_TurnOnNow"), () => _ = TurnSubOnAsync()),
                new TrayMenuItem(Strings.Get("Tray_Pause"), TogglePause, IsChecked: paused)
            ];
        }

        /// <summary>トレイの「一時停止」: 自動消灯の ON / OFF を切り替える（点灯の判定は止めない）。</summary>
        private void TogglePause()
        {
            bool paused;
            lock (_stateGate)
            {
                _autoPaused = !_autoPaused;
                paused = _autoPaused;
            }

            // 再開したら、止めていた間の時間で即消灯しないよう最初から数え直す
            _idleMonitor?.ResetIdleCount();
            AppendLifetimeLog($"auto-paused={paused}");
        }

        // ───────── 終了・クラッシュ時の復旧 ─────────

        protected override void OnExitCore()
        {
            // 判定ループとホットキーを先に止める（終了処理の途中で自動消灯が走らないように）
            _idleMonitor?.Dispose();
            _idleMonitor = null;
            _hotkey?.Dispose();
            _hotkey = null;

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
            string? targetPath;
            lock (_stateGate)
            {
                if (!_settings.SubTurnedOffByApp)
                    return;
                targetPath = _settings.TargetMonitorDevicePath;
            }

            // DdcController 内のロックで、走行中の命令が終わってから送られる（I2C の命令は混ざらない）
            DdcResult result = _ddc.SetPowerMode(AppConstants.PowerModeOn, verify: true, targetPath);
            AppendLifetimeLog($"restore reason={reason} result={result}");
            if (result != DdcResult.Success)
                return;

            lock (_stateGate)
            {
                _settings.SubTurnedOffByApp = false;
                _offKind = SubOffKind.None;
            }

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
