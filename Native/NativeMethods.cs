using System;
using System.Runtime.InteropServices;

namespace SubMonitorSleeper.Native
{
    /// <summary>
    /// Win32 API の P/Invoke 定義。
    /// - user32: モニター（HMONITOR）の列挙と情報取得
    /// - dxva2: DDC/CI（モニターへ I2C 経由で VCP コマンドを送る）
    /// </summary>
    internal static class NativeMethods
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

        // ───────── 構造体 ─────────

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
