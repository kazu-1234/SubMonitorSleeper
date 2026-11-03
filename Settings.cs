using System;
using System.IO;
using WinUiShared;

namespace SubMonitorSleeper
{
    /// <summary>
    /// %AppData%\SubMonitorSleeper\settings.json に保存する設定。
    /// 共通の項目（テーマ・トレイ・起動時の通知・窓位置・更新確認・自動起動）は AppHostSettingsBase にある。ここにはこのアプリ固有の項目だけを書く。
    /// </summary>
    public class Settings : AppHostSettingsBase
    {
        /// <summary>
        /// 対象モニターの固定 ID（EDID 由来のデバイスパス）。null なら自動（メイン以外の先頭）。
        /// \\.\DISPLAYn は再起動で番号が変わることがあるので、変わらない ID を保存する。
        /// </summary>
        public string? TargetMonitorDevicePath { get; set; }

        /// <summary>点灯・消灯を切り替えるホットキーの修飾キー（AppConstants.ModControl などの組み合わせ）。</summary>
        public uint HotkeyModifiers { get; set; } = AppConstants.DefaultHotkeyModifiers;

        /// <summary>ホットキーの仮想キーコード（既定は M = 0x4D）。0 は「なし」（登録しない）。</summary>
        public uint HotkeyVirtualKey { get; set; } = AppConstants.DefaultHotkeyVirtualKey;

        /// <summary>消灯時に D6 へ書く値（4 = スタンバイ / 5 = 電源オフ）。それ以外の値は 4 として扱う。</summary>
        public uint PowerOffValue { get; set; } = AppConstants.DefaultPowerOffValue;

        /// <summary>
        /// このアプリがサブを消灯したまま、まだ点灯に戻していないか。
        /// 強制終了（タスクマネージャー・インストーラーの taskkill など）で戻せなかった場合に、
        /// 次回起動時にこれを見て点灯へ戻すため、消灯の命令を出す前に true で保存しておく。
        /// </summary>
        public bool SubTurnedOffByApp { get; set; }

        /// <summary>読み込み。ファイルが無い・壊れているときは既定値（壊れていれば 1 つ前の保存版で救済）。</summary>
        public static Settings Load() =>
            LoadFrom<Settings>();

    }
}
