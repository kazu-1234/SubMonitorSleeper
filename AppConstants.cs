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

        // ───────── 自動判定（使っていないかの監視） ─────────

        /// <summary>サブ上のウィンドウ・カーソル位置を調べる間隔（ms）。短いほど反応が速いが CPU を使う。</summary>
        public const int PollIntervalMs = 500;

        /// <summary>手動点灯（トレイ・ホットキー・カーソル進入・ウィンドウ出現）の後、自動消灯しない秒数。</summary>
        public const int ManualOnGraceSeconds = 60;

        /// <summary>Windows の「画面をオフにする」時間を読み直す間隔（秒）。設定変更・AC/バッテリー切替への追従用。</summary>
        public const int PowerTimeoutRefreshSeconds = 30;

        /// <summary>ウィンドウとサブの重なりがこのピクセル数以上（縦横とも）あれば「サブ上にある」とみなす。</summary>
        public const int WindowOverlapMinPixels = 1;

        // ───────── ホットキー ─────────

        /// <summary>RegisterHotKey の修飾キー（MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8）。</summary>
        public const uint ModAlt = 0x0001;
        public const uint ModControl = 0x0002;
        public const uint ModShift = 0x0004;
        public const uint ModWin = 0x0008;

        /// <summary>既定のホットキー Ctrl+Alt+M（M の仮想キーコードは 0x4D）。</summary>
        public const uint DefaultHotkeyModifiers = ModControl | ModAlt;
        public const uint DefaultHotkeyVirtualKey = 0x4D;
    }
}
