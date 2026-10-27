using System;
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
        /// 対象モニターの D6 に値を書き込む。失敗したら <see cref="AppConstants.DdcRetryCount"/> 回まで試し直す。
        /// </summary>
        /// <param name="value">D6 に書く値（1 = 点灯、4 = スタンバイ、5 = 電源オフ）。</param>
        /// <param name="verify">
        /// true なら書き込み後に読み戻して値が一致するか確かめる（点灯用）。
        /// 消灯後はモニターが応答しなくなる機種もあるため、消灯では false にする。
        /// </param>
        /// <param name="targetDevicePath">対象モニターの固定 ID（null なら自動＝メイン以外の先頭）。</param>
        public DdcResult SetPowerMode(uint value, bool verify, string? targetDevicePath)
        {
            lock (_gate)
            {
                DdcResult result = DdcResult.MonitorNotFound;
                for (int attempt = 1; attempt <= AppConstants.DdcRetryCount; attempt++)
                {
                    result = TrySetPowerModeOnce(value, verify, targetDevicePath);

                    // サブが接続されていないなら、何度やっても同じなのですぐ返す
                    if (result is DdcResult.Success or DdcResult.MonitorNotFound)
                        return result;

                    if (attempt < AppConstants.DdcRetryCount)
                        Thread.Sleep(AppConstants.DdcRetryDelayMs);
                }

                return result;
            }
        }

        private static DdcResult TrySetPowerModeOnce(uint value, bool verify, string? targetDevicePath)
        {
            // HMONITOR はモニターの抜き差しや解像度変更で変わるので、毎回探し直す（覚えておかない）
            MonitorInfo? target = MonitorEnumerator.Resolve(targetDevicePath);
            if (target == null)
                return DdcResult.MonitorNotFound;
            IntPtr hMonitor = target.HMonitor;

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
    }
}
