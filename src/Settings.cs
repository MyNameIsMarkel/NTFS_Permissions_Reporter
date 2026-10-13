using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NtfsReporter
{
    // Preferencias guardadas en %APPDATA%\NtfsPermissionsReporter\settings.ini
    static class Settings
    {
        static readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NtfsPermissionsReporter", "settings.ini"); }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) values[line.Substring(0, i)] = line.Substring(i + 1);
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                List<string> lines = new List<string>();
                foreach (KeyValuePair<string, string> kv in values) lines.Add(kv.Key + "=" + kv.Value);
                File.WriteAllLines(FilePath, lines.ToArray(), Encoding.UTF8);
            }
            catch { }
        }

        public static string Get(string key, string def)
        {
            string v;
            return values.TryGetValue(key, out v) ? v : def;
        }

        public static int GetInt(string key, int def)
        {
            int v;
            return int.TryParse(Get(key, ""), out v) ? v : def;
        }

        public static bool GetBool(string key, bool def) { return Get(key, def ? "1" : "0") == "1"; }

        public static void Set(string key, string value) { values[key] = (value ?? "").Replace("\r", " ").Replace("\n", " "); }
        public static void Set(string key, int value) { Set(key, value.ToString()); }
        public static void Set(string key, bool value) { Set(key, value ? "1" : "0"); }
    }
}
