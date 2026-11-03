using System;
using WinUiShared;

namespace SubMonitorSleeper
{
    public static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // --exit（インストーラからの終了依頼）、--sync-autostart / --cleanup-autostart、WinUI の起動は共通処理。
            // アプリ固有の引数があれば、3 つ目の引数に「処理したら true を返す関数」を渡す
            AppBootstrap.RunMain(args, AppSetup.Profile, handlePreLaunchArgs: null, () => new App());
        }
    }
}
