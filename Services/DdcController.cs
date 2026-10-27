using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using SubMonitorSleeper.Native;

namespace SubMonitorSleeper.Services
{
    /// <summary>D6 送信の結果。</summary>
    public enum DdcResult
    {
        /// <summary>送信できた（点灯時は読み戻しで値も確認できた）。</summary>
        Success,

        /// <summary>メイン以外のモニターが見つからない（サブが接続されていない）。</summary>
        MonitorNotFound,

        /// <summary>HMONITOR から DDC/CI 用の物理モニターを取り出せなかった。</summary>
        NoPhysicalMonitor,

        /// <summary>SetVCPFeature が失敗した（モニターが応答しない・DDC/CI が無効など）。</summary>
        SetFailed,

        /// <summary>送信は通ったが、読み戻した値が期待どおりにならなかった（点灯の確認のみ）。</summary>
        VerifyFailed,

        /// <summary>アプリの終了処理中だったので送らなかった（失敗ではないので通知しない）。</summary>
        Cancelled
    }

    /// <summary>対象モニターの情報（設定画面の表示用）。</summary>
    /// <param name="DeviceName">Windows のデバイス名（例: \\.\DISPLAY2）。</param>
    /// <param name="Width">解像度の幅（物理ピクセル）。</param>
    /// <param name="Height">解像度の高さ（物理ピクセル）。</param>
    public sealed record MonitorDescription(string DeviceName, int Width, int Height);

    /// <summary>
    /// DDC/CI の VCP コード 0xD6（Power Mode）で、サブモニターを消灯・点灯する。
    /// Windows の画面構成（拡張表示）は一切変えず、モニター本体にだけ「電源状態」を命令する。
    /// DDC は I2C 通信で 1 回に数十〜数百 ms かかるため、呼び出し側は UI スレッド以外から呼ぶこと。
    /// </summary>
    public sealed class DdcController
    {
        // 消灯と点灯が別スレッドから同時に来ても、I2C に命令が混ざらないよう 1 本ずつ通す
        private readonly object _gate = new();

        /// <summary>
        /// 対象モニター（メインディスプレイ以外の最初の 1 枚）を探す。見つからなければ null。
        /// v0.1.0 は「Windows でメインに設定されていないモニター＝サブ」と判断する。
        /// </summary>
        public static MonitorDescription? FindTargetMonitor()
        {
            IntPtr hMonitor = FindSecondaryHMonitor(out NativeMethods.MONITORINFOEX info);
            if (hMonitor == IntPtr.Zero)
                return null;

            return new MonitorDescription(info.szDevice, info.rcMonitor.Width, info.rcMonitor.Height);
        }

        /// <summary>
        /// 対象モニターの D6 に値を書き込む。失敗したら <see cref="AppConstants.DdcRetryCount"/> 回まで試し直す。
        /// </summary>
        /// <param name="value">D6 に書く値（1 = 点灯、4 = スタンバイ、5 = 電源オフ）。</param>
        /// <param name="verify">
        /// true なら書き込み後に読み戻して値が一致するか確かめる（点灯用）。
        /// 消灯後はモニターが応答しなくなる機種もあるため、消灯では false にする。
        /// </param>
        public DdcResult SetPowerMode(uint value, bool verify)
        {
            lock (_gate)
            {
                DdcResult result = DdcResult.MonitorNotFound;
                for (int attempt = 1; attempt <= AppConstants.DdcRetryCount; attempt++)
                {
                    result = TrySetPowerModeOnce(value, verify);

                    // サブが接続されていないなら、何度やっても同じなのですぐ返す
                    if (result is DdcResult.Success or DdcResult.MonitorNotFound)
                        return result;

                    if (attempt < AppConstants.DdcRetryCount)
                        Thread.Sleep(AppConstants.DdcRetryDelayMs);
                }

                return result;
            }
        }

        private static DdcResult TrySetPowerModeOnce(uint value, bool verify)
        {
            // HMONITOR はモニターの抜き差しや解像度変更で変わるので、毎回探し直す（覚えておかない）
            IntPtr hMonitor = FindSecondaryHMonitor(out _);
            if (hMonitor == IntPtr.Zero)
                return DdcResult.MonitorNotFound;

            if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) || count == 0)
                return DdcResult.NoPhysicalMonitor;

            var monitors = new NativeMethods.PHYSICAL_MONITOR[count];
            if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, monitors))
                return DdcResult.NoPhysicalMonitor;

            try
            {
                // 通常 1 枚。複数（クローン表示）なら全部に同じ命令を送り、1 枚でも失敗したら失敗扱いにする
                foreach (NativeMethods.PHYSICAL_MONITOR monitor in monitors)
                {
                    if (!NativeMethods.SetVCPFeature(monitor.hPhysicalMonitor, AppConstants.VcpPowerMode, value))
                        return DdcResult.SetFailed;
                }

                if (!verify)
                    return DdcResult.Success;

                // DDC/CI は書き込み直後の読み出しを受け付けないことがあるので、少し待ってから読み戻す
                Thread.Sleep(AppConstants.DdcVerifyDelayMs);
                foreach (NativeMethods.PHYSICAL_MONITOR monitor in monitors)
                {
                    if (!NativeMethods.GetVCPFeatureAndVCPFeatureReply(
                            monitor.hPhysicalMonitor,
                            AppConstants.VcpPowerMode,
                            IntPtr.Zero,
                            out uint current,
                            out _)
                        || current != value)
                    {
                        return DdcResult.VerifyFailed;
                    }
                }

                return DdcResult.Success;
            }
            finally
            {
                // 物理モニターハンドルはカーネル側の資源なので、成否にかかわらず必ず解放する
                NativeMethods.DestroyPhysicalMonitors(count, monitors);
            }
        }

        /// <summary>メインディスプレイ以外の最初のモニターの HMONITOR を返す（無ければ IntPtr.Zero）。</summary>
        private static IntPtr FindSecondaryHMonitor(out NativeMethods.MONITORINFOEX foundInfo)
        {
            var candidates = new List<(IntPtr Handle, NativeMethods.MONITORINFOEX Info)>();

            // コールバックはこの呼び出しの中だけで同期的に呼ばれる（列挙が終わるまで戻らない）
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr _, ref NativeMethods.RECT _, IntPtr _) =>
            {
                var info = new NativeMethods.MONITORINFOEX
                {
                    cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFOEX>()
                };

                // MONITORINFOF_PRIMARY が立っていない＝Windows の「メインディスプレイ」ではない
                if (NativeMethods.GetMonitorInfo(hMonitor, ref info)
                    && (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) == 0)
                {
                    candidates.Add((hMonitor, info));
                }

                return true;
            }, IntPtr.Zero);

            if (candidates.Count == 0)
            {
                foundInfo = default;
                return IntPtr.Zero;
            }

            // 列挙順は環境で変わりうるので、デバイス名順にして毎回同じモニターを選ぶ（3 枚以上のとき用）
            candidates.Sort((a, b) => string.CompareOrdinal(a.Info.szDevice, b.Info.szDevice));
            foundInfo = candidates[0].Info;
            return candidates[0].Handle;
        }
    }
}
