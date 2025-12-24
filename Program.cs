using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

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

            using (Mutex mutex = new Mutex(false, "Global\\" + "HMSimpleBrightness_v38_Modular"))
            {
                if (!mutex.WaitOne(0, false)) return;
                ApplicationConfiguration.Initialize();
                Application.Run(new MyCustomApplicationContext());
            }
        }
    }
}
