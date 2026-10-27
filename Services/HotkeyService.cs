using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SubMonitorSleeper.Native;

namespace SubMonitorSleeper.Services
{
    /// <summary>
    /// グローバルホットキー（どのアプリが前面でも効くキー）を RegisterHotKey で登録する。
    /// WM_HOTKEY を受けるために、画面に出ない「メッセージ専用ウィンドウ」を UI スレッドに 1 つ作る。
    /// MainWindow の WndProc は差し替えない（WinUI の再描画が壊れるため。トレイと同じ方針）。
    /// </summary>
    public sealed class HotkeyService : IDisposable
    {
        private const string WindowClassName = "SubMonitorSleeper_HotkeyWindow_v1";

        /// <summary>このアプリ内でのホットキー番号（1 つしか登録しないので固定）。</summary>
        private const int HotkeyId = 1;

        // ウィンドウ手続きの delegate は Win32 側から呼ばれ続けるので、GC に回収されないよう static で持ち続ける
        private static readonly NativeMethods.WndProc StaticWndProc = WindowProc;
        private static bool _classRegistered;

        // WindowProc は static なので、どのインスタンス宛てかを HWND から引く表を持つ
        private static readonly Dictionary<IntPtr, HotkeyService> Instances = new();

        private IntPtr _hwnd;
        private bool _isRegistered;

        /// <summary>ホットキーが押された（UI スレッドで呼ばれる）。</summary>
        public event Action? Pressed;

        /// <summary>UI スレッドで作ること（ウィンドウは作ったスレッドのメッセージループで動くため）。</summary>
        public HotkeyService()
        {
            IntPtr hInstance = NativeMethods.GetModuleHandle(IntPtr.Zero);
            EnsureClassRegistered(hInstance);

            _hwnd = NativeMethods.CreateWindowExW(
                0, WindowClassName, string.Empty, 0,
                0, 0, 0, 0,
                NativeMethods.HWND_MESSAGE, IntPtr.Zero, hInstance, IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
                throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");

            Instances[_hwnd] = this;
        }

        /// <summary>
        /// ホットキーを登録する（既に登録していれば外してから）。他のアプリが同じ組み合わせを使っていると失敗する。
        /// </summary>
        /// <param name="modifiers">AppConstants.ModControl など（MOD_NOREPEAT はここで足す）。</param>
        /// <param name="virtualKey">仮想キーコード（例: M = 0x4D）。</param>
        public bool TryRegister(uint modifiers, uint virtualKey)
        {
            Unregister();
            if (_hwnd == IntPtr.Zero || modifiers == 0 || virtualKey == 0)
                return false;

            // MOD_NOREPEAT: 押しっぱなしのキーリピートで消灯・点灯が連続で切り替わらないようにする
            _isRegistered = NativeMethods.RegisterHotKey(_hwnd, HotkeyId, modifiers | NativeMethods.MOD_NOREPEAT, virtualKey);
            return _isRegistered;
        }

        /// <summary>登録を外す（設定画面でキーを入力している間など）。</summary>
        public void Unregister()
        {
            if (!_isRegistered)
                return;

            NativeMethods.UnregisterHotKey(_hwnd, HotkeyId);
            _isRegistered = false;
        }

        /// <summary>表示用の文字列（例: Ctrl+Alt+M）。</summary>
        public static string Format(uint modifiers, uint virtualKey)
        {
            var parts = new List<string>();
            if ((modifiers & AppConstants.ModControl) != 0) parts.Add("Ctrl");
            if ((modifiers & AppConstants.ModAlt) != 0) parts.Add("Alt");
            if ((modifiers & AppConstants.ModShift) != 0) parts.Add("Shift");
            if ((modifiers & AppConstants.ModWin) != 0) parts.Add("Win");
            parts.Add(FormatKey(virtualKey));
            return string.Join("+", parts);
        }

        /// <summary>仮想キーコードを読める名前にする（英数字・F キーはそのまま、それ以外は enum 名）。</summary>
        private static string FormatKey(uint virtualKey)
        {
            // 0–9 と A–Z は仮想キーコードが ASCII と同じ
            if (virtualKey is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A)
                return ((char)virtualKey).ToString();

            // F1–F24（0x70–0x87）
            if (virtualKey is >= 0x70 and <= 0x87)
                return $"F{virtualKey - 0x6F}";

            return ((Windows.System.VirtualKey)virtualKey).ToString();
        }

        private static void EnsureClassRegistered(IntPtr hInstance)
        {
            if (_classRegistered)
                return;

            var wc = new NativeMethods.WNDCLASSW
            {
                lpfnWndProc = StaticWndProc,
                hInstance = hInstance,
                lpszClassName = WindowClassName
            };

            if (NativeMethods.RegisterClassW(ref wc) == 0)
            {
                int error = Marshal.GetLastWin32Error();
                if (error != NativeMethods.ERROR_CLASS_ALREADY_EXISTS)
                    throw new InvalidOperationException($"RegisterClass failed: {error}");
            }

            _classRegistered = true;
        }

        private static IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == NativeMethods.WM_HOTKEY
                && wParam.ToInt32() == HotkeyId
                && Instances.TryGetValue(hWnd, out HotkeyService? service))
            {
                service.Pressed?.Invoke();
                return IntPtr.Zero;
            }

            return NativeMethods.DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        public void Dispose()
        {
            Unregister();
            if (_hwnd != IntPtr.Zero)
            {
                Instances.Remove(_hwnd);
                NativeMethods.DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
        }
    }
}
