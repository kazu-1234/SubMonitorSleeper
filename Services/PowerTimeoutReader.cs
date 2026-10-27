using System;
using System.Runtime.InteropServices;
using SubMonitorSleeper.Native;

namespace SubMonitorSleeper.Services
{
    /// <summary>Windows の「画面をオフにする」時間。</summary>
    /// <param name="Seconds">秒数。0 は「なし」（自動消灯しない）。</param>
    /// <param name="IsAcPower">AC 電源の値か（false ならバッテリーの値）。</param>
    public readonly record struct PowerTimeout(uint Seconds, bool IsAcPower);

    /// <summary>
    /// 有効な電源プランから「次の時間が経過後ディスプレイの電源を切る」を読む。
    /// AC 電源とバッテリーで別の値を持つので、今どちらで動いているかを見て使い分ける。
    /// </summary>
    public static class PowerTimeoutReader
    {
        /// <summary>今の電源状態に対応する値を読む。読めなければ「なし（0）」として扱う（勝手に消さない側に倒す）。</summary>
        public static PowerTimeout Read()
        {
            bool isAc = IsOnAcPower();

            if (NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out IntPtr schemePtr) != 0 || schemePtr == IntPtr.Zero)
                return new PowerTimeout(0, isAc);

            try
            {
                // PowerGetActiveScheme は GUID のポインタを返すので、中身を取り出してから使う
                Guid scheme = Marshal.PtrToStructure<Guid>(schemePtr);
                Guid subGroup = NativeMethods.GUID_VIDEO_SUBGROUP;
                Guid setting = NativeMethods.GUID_VIDEO_POWERDOWN_TIMEOUT;

                uint error = isAc
                    ? NativeMethods.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subGroup, ref setting, out uint seconds)
                    : NativeMethods.PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subGroup, ref setting, out seconds);

                return new PowerTimeout(error == 0 ? seconds : 0, isAc);
            }
            finally
            {
                // ポインタの中身は Windows が確保したメモリなので、LocalFree で返す
                NativeMethods.LocalFree(schemePtr);
            }
        }

        /// <summary>AC 電源で動いているか。デスクトップ PC や取得できないとき（255 = 不明）は AC とみなす。</summary>
        private static bool IsOnAcPower()
        {
            if (!NativeMethods.GetSystemPowerStatus(out NativeMethods.SYSTEM_POWER_STATUS status))
                return true;

            return status.ACLineStatus != 0;
        }
    }
}
