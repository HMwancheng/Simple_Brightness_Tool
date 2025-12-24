using System;
using System.Threading;
using System.Windows.Forms;
using SimpleBrightness.Core;

namespace SimpleBrightness
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (Mutex mutex = new Mutex(false, "Global\\" + "HMSimpleBrightness_v38_Split"))
            {
                if (!mutex.WaitOne(0, false)) return;
                ApplicationConfiguration.Initialize();
                Application.Run(new MyCustomApplicationContext());
            }
        }
    }
}
