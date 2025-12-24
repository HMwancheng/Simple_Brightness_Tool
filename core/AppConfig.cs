using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace SimpleBrightness.Core
{
    public class AppConfig
    {
        public int ScrollStep { get; set; } = 5;
        public int DebounceTime { get; set; } = 200;
        public bool UseSoftwarePower { get; set; } = false;
        public List<string> HiddenMonitors { get; set; } = new List<string>();
        public Dictionary<string, string> CustomNames { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, Dictionary<int, int>> Curves { get; set; } = new Dictionary<string, Dictionary<int, int>>();
        public Dictionary<string, int> SavedBrightness { get; set; } = new Dictionary<string, int>();

        private static string ConfigPath = Path.Combine(Application.StartupPath, "HMSimpleBrightness_Config.json");

        public static AppConfig Load()
        {
            if (File.Exists(ConfigPath))
            {
                try { return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath)) ?? new AppConfig(); }
                catch { }
            }
            return new AppConfig();
        }

        public void Save()
        {
            try { File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this)); }
            catch { }
        }

        public Dictionary<int, int> GetCurveForMonitor(string id)
        {
            if (Curves.ContainsKey(id)) return Curves[id];
            return new Dictionary<int, int> { { 0, 0 }, { 100, 100 } };
        }
    }
}
