using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SubMonitorSleeper.Native;

namespace SubMonitorSleeper.Services
{
    /// <summary>接続中のモニター 1 枚分の情報。</summary>
    /// <param name="HMonitor">Windows のモニターハンドル（抜き差し・構成変更で変わるので保存しない）。</param>
    /// <param name="DeviceName">GDI のデバイス名（例: \\.\DISPLAY2）。再起動で番号が変わることがある。</param>
    /// <param name="Bounds">仮想デスクトップ上の矩形（物理ピクセル）。</param>
    /// <param name="IsPrimary">Windows の「メインディスプレイ」か。</param>
    /// <param name="FriendlyName">モニターの製品名（EDID 由来。例: Acer XV272U）。取れなければ空。</param>
    /// <param name="DevicePath">モニターの固定 ID（EDID 由来のデバイスパス）。設定に保存する対象の識別子。</param>
    public sealed record MonitorInfo(
        IntPtr HMonitor,
        string DeviceName,
        NativeMethods.RECT Bounds,
        bool IsPrimary,
        string FriendlyName,
        string DevicePath)
    {
        /// <summary>「\\.\DISPLAY2」の先頭の「\\.\」は読み手に意味がないので外した表示名。</summary>
        public string ShortDeviceName => DeviceName.Replace(@"\\.\", string.Empty);
    }

    /// <summary>
    /// 接続中のモニターを列挙し、設定に保存した固定 ID（DevicePath）から対象モニターを探す。
    /// HMONITOR や \\.\DISPLAYn は構成変更で変わるため、毎回列挙し直して使う（覚えておかない）。
    /// </summary>
    public static class MonitorEnumerator
    {
        /// <summary>接続中のモニターをすべて返す（デバイス名順）。</summary>
        public static List<MonitorInfo> GetMonitors()
        {
            // \\.\DISPLAYn → (製品名, 固定パス) の対応表。QueryDisplayConfig が失敗しても列挙自体は続ける
            Dictionary<string, (string Name, string Path)> targets = ReadTargetNames();

            var result = new List<MonitorInfo>();

            // コールバックはこの呼び出しの中だけで同期的に呼ばれる（列挙が終わるまで戻らない）
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr _, ref NativeMethods.RECT _, IntPtr _) =>
            {
                var info = new NativeMethods.MONITORINFOEX
                {
                    cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFOEX>()
                };

                if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
                {
                    targets.TryGetValue(info.szDevice, out var target);
                    result.Add(new MonitorInfo(
                        hMonitor,
                        info.szDevice,
                        info.rcMonitor,
                        (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0,
                        target.Name ?? string.Empty,
                        // 固定パスが取れない環境では、せめてデバイス名を ID 代わりにする
                        string.IsNullOrEmpty(target.Path) ? info.szDevice : target.Path));
                }

                return true;
            }, IntPtr.Zero);

            // 列挙順は環境で変わりうるので、デバイス名順にして毎回同じ並びにする
            result.Sort((a, b) => string.CompareOrdinal(a.DeviceName, b.DeviceName));
            return result;
        }

        /// <summary>
        /// 対象モニターを決める。devicePath が null / 空なら「自動」＝メイン以外の先頭（3 枚以上でも毎回同じ 1 枚）。
        /// 指定したモニターが今つながっていなければ null（勝手に別のモニターを消さないため）。
        /// </summary>
        public static MonitorInfo? Resolve(string? devicePath)
        {
            List<MonitorInfo> monitors = GetMonitors();

            if (string.IsNullOrEmpty(devicePath))
                return monitors.Find(m => !m.IsPrimary);

            return monitors.Find(m => string.Equals(m.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>QueryDisplayConfig で、出力元（\\.\DISPLAYn）ごとのモニター製品名と固定パスを読む。</summary>
        private static Dictionary<string, (string Name, string Path)> ReadTargetNames()
        {
            var map = new Dictionary<string, (string Name, string Path)>(StringComparer.OrdinalIgnoreCase);

            if (NativeMethods.GetDisplayConfigBufferSizes(NativeMethods.QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != 0)
                return map;

            var paths = new NativeMethods.DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new NativeMethods.DISPLAYCONFIG_MODE_INFO[modeCount];
            if (NativeMethods.QueryDisplayConfig(NativeMethods.QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
                return map;

            for (int i = 0; i < pathCount; i++)
            {
                NativeMethods.DISPLAYCONFIG_PATH_INFO path = paths[i];

                // 1 本の経路 = 「出力元（GPU 側の \\.\DISPLAYn）→ 出力先（モニター）」。両側の名前を聞いて対応付ける
                var source = new NativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = new NativeMethods.DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                        size = (uint)Marshal.SizeOf<NativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                        adapterId = path.sourceInfo.adapterId,
                        id = path.sourceInfo.id
                    }
                };
                if (NativeMethods.DisplayConfigGetDeviceInfo(ref source) != 0)
                    continue;

                var target = new NativeMethods.DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header = new NativeMethods.DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                        size = (uint)Marshal.SizeOf<NativeMethods.DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                        adapterId = path.targetInfo.adapterId,
                        id = path.targetInfo.id
                    }
                };
                if (NativeMethods.DisplayConfigGetDeviceInfo(ref target) != 0)
                    continue;

                // 複製表示では 1 つの出力元に複数のモニターがつながる。最初の 1 枚を代表にする
                map.TryAdd(source.viewGdiDeviceName, (target.monitorFriendlyDeviceName, target.monitorDevicePath));
            }

            return map;
        }
    }
}
