namespace SubMonitorSleeper
{
    /// <summary>
    /// 後から調整しそうな値をここに集約する（処理中に直書きしない）。
    /// </summary>
    internal static class AppConstants
    {
        // ───────── DDC/CI（VCP コード 0xD6 = Power Mode） ─────────

        /// <summary>VCP コード 0xD6（Power Mode）。MCCS 規格で電源状態を表す。</summary>
        public const byte VcpPowerMode = 0xD6;

        /// <summary>D6 の「点灯（オン）」の値。</summary>
        public const uint PowerModeOn = 1;

        /// <summary>D6 の「スタンバイ」の値（消灯値の既定。DDC 回路が生きたまま残りやすく、1 で戻しやすい）。</summary>
        public const uint PowerModeStandby = 4;

        /// <summary>D6 の「電源オフ（DPM オフ）」の値。機種によっては DDC も止まり、D6=1 で戻せないことがある。</summary>
        public const uint PowerModeOff = 5;

        /// <summary>消灯値の初期値（設定ファイルに無いとき）。</summary>
        public const uint DefaultPowerOffValue = PowerModeStandby;

        /// <summary>D6 の送信（と点灯時の確認）を失敗とみなすまでの試行回数。DDC は I2C なので一発で通らないことがある。</summary>
        public const int DdcRetryCount = 3;

        /// <summary>試行と試行の間の待ち時間（ms）。モニター側の処理が終わるのを待つ。</summary>
        public const int DdcRetryDelayMs = 300;

        /// <summary>
        /// 書き込みから読み戻しまでの待ち時間（ms）。DDC/CI 規格では書き込み後 50 ms 以上空けることになっている。
        /// スタンバイからの復帰直後は応答が遅い機種があるため少し長めにする。
        /// </summary>
        public const int DdcVerifyDelayMs = 150;

        // ───────── テスト消灯 ─────────

        /// <summary>設定画面の「テスト消灯」で消してから自動で点けるまでの秒数。</summary>
        public const int TestOffSeconds = 5;

        // ───────── スリープ復帰・ロック解除時の点灯 ─────────

        /// <summary>
        /// 復帰の再適用コールバック（即時 + 800〜90000 ms の遅延）の間隔がこれ以上空いたら「新しい復帰」とみなす（秒）。
        /// 遅延再適用の間隔は最大 30 秒なので、それより長くする。
        /// </summary>
        public const int ResumeBurstGapSeconds = 45;

        /// <summary>
        /// 消灯コマンドの直後これ以内に始まった「復帰」は無視する（秒）。
        /// 消灯したことでモニター構成の変更通知（WM_DISPLAYCHANGE など）が来たとき、
        /// それを復帰と取り違えてすぐ点け直してしまうのを防ぐ。
        /// </summary>
        public const int ResumeIgnoreAfterOffSeconds = 10;
    }
}
