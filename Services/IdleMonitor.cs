using System;
using System.Threading;
using SubMonitorSleeper.Native;

namespace SubMonitorSleeper.Services
{
    /// <summary>サブが消えている理由。</summary>
    public enum SubOffKind
    {
        /// <summary>点灯中（このアプリは消していない）。</summary>
        None,

        /// <summary>「使っていない」判定で自動的に消した。</summary>
        Auto,

        /// <summary>トレイ・ホットキー・テスト消灯など、ユーザーの操作で消した。</summary>
        Manual
    }

    /// <summary>点灯を頼む理由。</summary>
    public enum WakeReason
    {
        /// <summary>消灯中にカーソルがサブへ入った。</summary>
        CursorEntered,

        /// <summary>自動で消した後に、サブ上へウィンドウが現れた。</summary>
        WindowAppeared
    }

    /// <summary>判定に使うアプリ側の状態（ポーリングのたびに AppRuntime から受け取るスナップショット）。</summary>
    /// <param name="TargetDevicePath">対象モニターの固定 ID（null なら自動）。</param>
    /// <param name="OffKind">今サブが消えている理由（None なら点灯中）。</param>
    /// <param name="IsAutoPaused">トレイの「一時停止」中、または D6 失敗で止めている。</param>
    /// <param name="SuppressedUntilTick">この時刻（Environment.TickCount64）までは自動消灯しない（手動点灯後の猶予）。</param>
    public readonly record struct IdleContext(
        string? TargetDevicePath,
        SubOffKind OffKind,
        bool IsAutoPaused,
        long SuppressedUntilTick);

    /// <summary>
    /// 一定間隔でサブの「使っていない」状態を調べ、自動消灯・点灯を AppRuntime に頼む。
    /// 使っていない = ①サブ上にアプリのウィンドウが 1 つもない ②カーソルがサブ上にない、の両方が
    /// Windows の「画面をオフにする」時間だけ続いた状態。
    /// 実際の DDC 送信はしない（判断だけ）。コールバックはバックグラウンドスレッドから呼ばれる。
    /// </summary>
    public sealed class IdleMonitor : IDisposable
    {
        private readonly Func<IdleContext> _getContext;
        private readonly Action _requestAutoOff;
        private readonly Action<WakeReason> _requestWake;
        private readonly Timer _timer;

        // 前回の判定がまだ終わっていなければ次の回を飛ばす（EnumWindows が遅いときに処理が積み重ならないように）
        private int _busy;

        /// <summary>両条件を満たし始めた時刻（TickCount64）。0 は「満たしていない」。</summary>
        private long _conditionsMetSinceTick;

        /// <summary>前回カーソルがサブ上にあったか（null は未確定）。「外 → 中」の変化だけで点灯するため。</summary>
        private bool? _previousCursorOnSub;

        private PowerTimeout _timeout;
        private long _timeoutReadTick;
        private volatile bool _disposed;

        public IdleMonitor(Func<IdleContext> getContext, Action requestAutoOff, Action<WakeReason> requestWake)
        {
            _getContext = getContext;
            _requestAutoOff = requestAutoOff;
            _requestWake = requestWake;

            _timeout = PowerTimeoutReader.Read();
            _timeoutReadTick = Environment.TickCount64;

            _timer = new Timer(_ => Tick(), null, AppConstants.PollIntervalMs, AppConstants.PollIntervalMs);
        }

        /// <summary>
        /// 「使っていない」の経過時間を最初から数え直す（スリープ復帰・一時停止の解除・点灯の後など）。
        /// どのスレッドから呼んでもよい。
        /// </summary>
        public void ResetIdleCount() => Interlocked.Exchange(ref _conditionsMetSinceTick, 0);

        private void Tick()
        {
            if (_disposed || Interlocked.Exchange(ref _busy, 1) != 0)
                return;

            try
            {
                TickCore();
            }
            catch (Exception ex)
            {
                // 1 回の失敗で監視が止まらないよう、ここで受け止めて次の回に任せる
                System.Diagnostics.Debug.WriteLine($"IdleMonitor tick failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        private void TickCore()
        {
            long now = Environment.TickCount64;

            // 電源設定の変更・AC/バッテリーの切替に追従するため、一定間隔で読み直す
            if (now - _timeoutReadTick >= AppConstants.PowerTimeoutRefreshSeconds * 1000L)
            {
                _timeout = PowerTimeoutReader.Read();
                _timeoutReadTick = now;
            }

            IdleContext context = _getContext();

            // 対象モニターが無い（未接続・指定したモニターが外れている）ときは何も判断しない
            MonitorInfo? target = MonitorEnumerator.Resolve(context.TargetDevicePath);
            if (target == null)
            {
                ResetIdleCount();
                _previousCursorOnSub = null;
                return;
            }

            // 条件②: カーソルが今どのモニター上にあるか。消灯中も Windows の画面構成は「拡張」のままなので判定できる
            bool cursorOnSub = NativeMethods.GetCursorPos(out NativeMethods.POINT point)
                && NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONULL) == target.HMonitor;

            // 条件①: サブ上にアプリのウィンドウがあるか
            bool windowOnSub = WindowPresenceChecker.HasAppWindowOn(target.Bounds);

            bool? previousCursorOnSub = _previousCursorOnSub;
            _previousCursorOnSub = cursorOnSub;

            if (context.OffKind != SubOffKind.None)
            {
                // 消灯中は「使っていない時間」を数えない
                ResetIdleCount();

                // カーソル進入: 前回は外・今回は中のときだけ。消した瞬間にカーソルがサブ上にあっても、すぐ点き直さないため
                if (cursorOnSub && previousCursorOnSub == false)
                {
                    _requestWake(WakeReason.CursorEntered);
                    return;
                }

                // 自動で消した後にウィンドウが現れた（アプリがサブで開いた・移動してきた）。手動で消したときは点けない
                if (context.OffKind == SubOffKind.Auto && windowOnSub)
                    _requestWake(WakeReason.WindowAppeared);

                return;
            }

            // 自動消灯しない状況では、経過時間を数え直すだけにする
            bool blocked = context.IsAutoPaused
                || now < context.SuppressedUntilTick
                || _timeout.Seconds == 0      // Windows 側が「なし」なら自動消灯しない
                || cursorOnSub
                || windowOnSub;

            if (blocked)
            {
                ResetIdleCount();
                return;
            }

            // 両条件を満たし始めた時刻を記録し、待ち時間だけ続いたら自動消灯を頼む
            long since = Interlocked.CompareExchange(ref _conditionsMetSinceTick, now, 0);
            if (since == 0)
                return;

            if (now - since >= _timeout.Seconds * 1000L)
            {
                ResetIdleCount();
                _requestAutoOff();
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _timer.Dispose();
        }
    }
}
