using System;
using System.Collections.Generic;
using SubMonitorSleeper.Native;

namespace SubMonitorSleeper.Services
{
    /// <summary>
    /// 指定したモニター上に「開いているアプリのウィンドウ」が 1 つでもあるかを調べる（あり / なしの 0/1 判定）。
    /// 動画再生中のプレーヤーもウィンドウとして数えるので、メディア再生の検知はしない。
    /// </summary>
    public static class WindowPresenceChecker
    {
        /// <summary>
        /// デスクトップやタスクバーなどシェルの部品。画面に常にあるので数えない。
        /// Progman / WorkerW = デスクトップ（壁紙・アイコン）、Shell_(Secondary)TrayWnd = タスクバー。
        /// </summary>
        private static readonly HashSet<string> ShellClassNames = new(StringComparer.Ordinal)
        {
            "Progman",
            "WorkerW",
            "Shell_TrayWnd",
            "Shell_SecondaryTrayWnd"
        };

        // 自アプリのウィンドウ（設定画面・トレイ用の隠し窓）は判定に使わないので、自分のプロセス ID で除外する
        private static readonly uint OwnProcessId = (uint)Environment.ProcessId;

        /// <summary>monitorBounds と重なるアプリのウィンドウがあれば true。</summary>
        public static bool HasAppWindowOn(NativeMethods.RECT monitorBounds)
        {
            bool found = false;
            var classBuffer = new char[256];

            NativeMethods.EnumWindows((hWnd, _) =>
            {
                if (IsCountableWindow(hWnd, classBuffer)
                    && TryGetVisibleBounds(hWnd, out NativeMethods.RECT bounds)
                    && Overlaps(bounds, monitorBounds))
                {
                    found = true;
                    return false;   // 1 つ見つかれば十分なので列挙を打ち切る
                }

                return true;
            }, IntPtr.Zero);

            return found;
        }

        /// <summary>「アプリのウィンドウ」として数えるか（仕様の除外 + ツールウィンドウの除外）。</summary>
        private static bool IsCountableWindow(IntPtr hWnd, char[] classBuffer)
        {
            // 非表示・最小化は画面に出ていないので数えない
            if (!NativeMethods.IsWindowVisible(hWnd) || NativeMethods.IsIconic(hWnd))
                return false;

            // クローク中（別の仮想デスクトップにある・UWP が裏で待機している等）は見えていないので数えない
            if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
                && cloaked != 0)
                return false;

            // ツールウィンドウは画面全体を覆う透明オーバーレイ（GPU ドライバ等）に多く、常に「使用中」になってしまうので数えない
            if ((NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOOLWINDOW) != 0)
                return false;

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint processId);
            if (processId == OwnProcessId)
                return false;

            int length = NativeMethods.GetClassName(hWnd, classBuffer, classBuffer.Length);
            if (length > 0 && ShellClassNames.Contains(new string(classBuffer, 0, length)))
                return false;

            return true;
        }

        /// <summary>
        /// 見えている枠の矩形を取る。GetWindowRect は見えないリサイズ枠（約 8px）を含み、
        /// メインで最大化した窓がサブ側へはみ出して「サブ上にある」と誤判定されるため、DWM の値を優先する。
        /// </summary>
        private static bool TryGetVisibleBounds(IntPtr hWnd, out NativeMethods.RECT bounds)
        {
            if (NativeMethods.DwmGetWindowAttribute(
                    hWnd,
                    NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                    out bounds,
                    System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RECT>()) != 0
                && !NativeMethods.GetWindowRect(hWnd, out bounds))
            {
                return false;
            }

            // 大きさ 0 の窓（メッセージ受信用の隠し窓など）は見えないので数えない
            return bounds.Width > 0 && bounds.Height > 0;
        }

        /// <summary>2 つの矩形が縦横とも WindowOverlapMinPixels 以上重なっているか。</summary>
        private static bool Overlaps(NativeMethods.RECT a, NativeMethods.RECT b)
        {
            int overlapWidth = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
            int overlapHeight = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
            return overlapWidth >= AppConstants.WindowOverlapMinPixels
                && overlapHeight >= AppConstants.WindowOverlapMinPixels;
        }
    }
}
