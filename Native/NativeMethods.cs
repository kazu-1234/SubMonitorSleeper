using System;
using System.Runtime.InteropServices;

namespace SubMonitorSleeper.Native
{
    /// <summary>
    /// Win32 API の P/Invoke 定義。
    /// - user32: モニター（HMONITOR）の列挙と情報取得
    /// - dxva2: DDC/CI（モニターへ I2C 経由で VCP コマンドを送る）
    /// </summary>
    // public: MonitorInfo（設定画面へ渡す public な型）が RECT を持つため
    public static class NativeMethods
    {
        /// <summary>MONITORINFO.dwFlags のビット。立っていれば Windows の「メインディスプレイ」。</summary>
        public const uint MONITORINFOF_PRIMARY = 0x00000001;

        // ───────── user32: モニター列挙 ─────────

        /// <summary>EnumDisplayMonitors のコールバック。true を返すと列挙を続ける。</summary>
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        /// <summary>デスクトップ上のモニターを 1 枚ずつ列挙する（hdc / lprcClip が null なら全モニター）。</summary>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        /// <summary>モニターの矩形・作業領域・メインかどうか・デバイス名（\\.\DISPLAY2 など）を取る。</summary>
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        // ───────── dxva2: DDC/CI ─────────

        /// <summary>1 つの HMONITOR にぶら下がる物理モニター数（通常 1。クローン表示だと複数になる）。</summary>
        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint pdwNumberOfPhysicalMonitors);

        /// <summary>HMONITOR から DDC/CI 用の物理モニターハンドルを取り出す。使い終わったら DestroyPhysicalMonitors で解放する。</summary>
        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetPhysicalMonitorsFromHMONITOR(
            IntPtr hMonitor,
            uint dwPhysicalMonitorArraySize,
            [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

        /// <summary>GetPhysicalMonitorsFromHMONITOR で得たハンドルをまとめて解放する。</summary>
        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyPhysicalMonitors(uint dwPhysicalMonitorArraySize, [In] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

        /// <summary>VCP コードに値を書き込む（D6 なら電源状態）。モニターが ACK を返さないと false。</summary>
        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetVCPFeature(IntPtr hMonitor, byte bVCPCode, uint dwNewValue);

        /// <summary>VCP コードの現在値と最大値を読む。pvct（値の種類）は使わないので IntPtr.Zero を渡す。</summary>
        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetVCPFeatureAndVCPFeatureReply(
            IntPtr hMonitor,
            byte bVCPCode,
            IntPtr pvct,
            out uint pdwCurrentValue,
            out uint pdwMaximumValue);

        // ───────── user32: ウィンドウ列挙（サブ上のウィンドウの有無） ─────────

        /// <summary>GetWindowLongPtr で拡張スタイルを読むときの番号。</summary>
        public const int GWL_EXSTYLE = -20;

        /// <summary>ツールウィンドウ（タスクバー・Alt+Tab に出ない補助窓。透明オーバーレイ等もこれ）。</summary>
        public const long WS_EX_TOOLWINDOW = 0x00000080;

        /// <summary>EnumWindows のコールバック。true を返すと列挙を続ける。</summary>
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        /// <summary>最上位ウィンドウを Z 順（手前から）に列挙する。</summary>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        /// <summary>最小化されているか。</summary>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        /// <summary>ウィンドウクラス名（Progman など）を取る。戻り値は文字数（0 なら失敗）。</summary>
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, char[] lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        /// <summary>ウィンドウ矩形（見えないリサイズ枠を含む）。DWM で取れなかったときの予備。</summary>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        /// <summary>64bit / 32bit で関数名が違うので吸収する。</summary>
        public static long GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
            IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex).ToInt64() : GetWindowLong32(hWnd, nIndex);

        // ───────── dwmapi: 見えている枠・クローク状態 ─────────

        /// <summary>影やリサイズ枠を除いた「見えている」矩形。最大化窓のはみ出し（約 8px）を含まない。</summary>
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        /// <summary>0 以外なら「クローク」中（別の仮想デスクトップ・UWP の待機中など、見えていない）。</summary>
        public const int DWMWA_CLOAKED = 14;

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        // ───────── user32: カーソル ─────────

        /// <summary>MonitorFromPoint: どのモニターにも入っていなければ null（IntPtr.Zero）を返す。</summary>
        public const uint MONITOR_DEFAULTTONULL = 0x00000000;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        // ───────── powrprof / kernel32: 電源プランの「画面をオフにする」 ─────────

        /// <summary>電源設定の「ディスプレイ」グループ（GUID_VIDEO_SUBGROUP）。</summary>
        public static readonly Guid GUID_VIDEO_SUBGROUP = new("7516b95f-f776-4464-8c53-06167f40cc99");

        /// <summary>「次の時間が経過後ディスプレイの電源を切る」（GUID_VIDEO_POWERDOWN_TIMEOUT。値は秒、0 = なし）。</summary>
        public static readonly Guid GUID_VIDEO_POWERDOWN_TIMEOUT = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");

        /// <summary>有効な電源プランの GUID を取る。受け取ったポインタは LocalFree で解放する。戻り値 0 = 成功。</summary>
        [DllImport("powrprof.dll")]
        public static extern uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuid);

        /// <summary>AC 電源時の設定値を読む。戻り値 0 = 成功。</summary>
        [DllImport("powrprof.dll")]
        public static extern uint PowerReadACValueIndex(IntPtr RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroupOfPowerSettingsGuid, ref Guid PowerSettingGuid, out uint AcValueIndex);

        /// <summary>バッテリー時の設定値を読む。戻り値 0 = 成功。</summary>
        [DllImport("powrprof.dll")]
        public static extern uint PowerReadDCValueIndex(IntPtr RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroupOfPowerSettingsGuid, ref Guid PowerSettingGuid, out uint DcValueIndex);

        [DllImport("kernel32.dll")]
        public static extern IntPtr LocalFree(IntPtr hMem);

        /// <summary>AC 電源かバッテリーかを取る（ACLineStatus: 0 = バッテリー、1 = AC、255 = 不明）。</summary>
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

        // ───────── user32: ホットキー（メッセージ専用ウィンドウ） ─────────

        public const uint WM_HOTKEY = 0x0312;

        /// <summary>押しっぱなしのキーリピートで WM_HOTKEY が連続しないようにする修飾フラグ。</summary>
        public const uint MOD_NOREPEAT = 0x4000;

        /// <summary>CreateWindowEx の親に渡すと「メッセージ専用ウィンドウ」（画面に出ない・列挙されない）になる。</summary>
        public static readonly IntPtr HWND_MESSAGE = new(-3);

        public const int ERROR_CLASS_ALREADY_EXISTS = 1410;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern ushort RegisterClassW(ref WNDCLASSW lpWndClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowExW(
            uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
            int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(IntPtr lpModuleName);

        // ───────── user32: モニターの名前と固定 ID（QueryDisplayConfig） ─────────

        /// <summary>今つながって表示に使われている経路だけを取る。</summary>
        public const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;

        public const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
        public const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;

        [DllImport("user32.dll")]
        public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        public static extern int QueryDisplayConfig(
            uint flags,
            ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
            ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
            IntPtr currentTopologyId);

        /// <summary>経路の「出力元」（\\.\DISPLAYn）の名前を取る。</summary>
        [DllImport("user32.dll")]
        public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

        /// <summary>経路の「出力先」（モニター本体）の名前と固定パスを取る。</summary>
        [DllImport("user32.dll")]
        public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

        // ───────── 構造体 ─────────

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASSW
        {
            public uint style;
            public WndProc lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_SOURCE_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint outputTechnology;
            public uint rotation;
            public uint scaling;
            public uint refreshRateNumerator;
            public uint refreshRateDenominator;
            public uint scanLineOrdering;
            public int targetAvailable;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_INFO
        {
            public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
            public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
            public uint flags;
        }

        /// <summary>モード情報（中身は使わないので、大きさ 64 バイトだけ合わせる）。</summary>
        [StructLayout(LayoutKind.Explicit, Size = 64)]
        public struct DISPLAYCONFIG_MODE_INFO
        {
            [FieldOffset(0)] public uint infoType;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_DEVICE_INFO_HEADER
        {
            public uint type;
            public uint size;
            public LUID adapterId;
            public uint id;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string viewGdiDeviceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint flags;
            public uint outputTechnology;
            public ushort edidManufactureId;
            public ushort edidProductCodeId;
            public uint connectorInstance;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string monitorFriendlyDeviceName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string monitorDevicePath;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public readonly int Width => Right - Left;
            public readonly int Height => Bottom - Top;
        }

        /// <summary>MONITORINFO にデバイス名（szDevice）を足した拡張版。cbSize に自分のサイズを入れてから渡す。</summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public uint cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        /// <summary>DDC/CI 用の物理モニター（ハンドル + 説明文字列）。</summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PHYSICAL_MONITOR
        {
            public IntPtr hPhysicalMonitor;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szPhysicalMonitorDescription;
        }
    }
}
