using System;
using System.Threading;
using System.Windows.Forms;

namespace RdpPanel
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // unexpected errors: write them to errors.log (it goes into the diagnostic file) instead of crashing silently
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                ErrorLog.Write("ui", e.Exception);
                try
                {
                    MessageBox.Show("Something unexpected went wrong:\r\n\r\n" + e.Exception.Message
                        + "\r\n\r\nThe panel keeps running. Details were saved in errors.log; the 'Create diagnostic file' button includes them.",
                        AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ErrorLog.Write("fatal", e.ExceptionObject as Exception);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var cfg = AppConfig.Load();
            Theme.Apply(cfg.LightTheme);
            bool auto = Array.IndexOf(args, "--autostart") >= 0;
            Application.Run(new MainForm(cfg, auto));
        }
    }
}
