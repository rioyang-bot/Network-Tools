using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace NetworkTools
{
    /// <summary>
    /// Simple key=value settings file. Stored next to the exe (portable) when writable,
    /// otherwise in %APPDATA%\NetworkTools.
    /// </summary>
    public class Settings
    {
        readonly Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string filePath;

        static string ExeDirPath
        {
            get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "NetworkTools.ini"); }
        }

        static string AppDataPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                    "NetworkTools", "NetworkTools.ini");
            }
        }

        public static Settings Load()
        {
            var s = new Settings();
            string p = null;
            if (File.Exists(ExeDirPath)) p = ExeDirPath;
            else if (File.Exists(AppDataPath)) p = AppDataPath;
            s.filePath = p;
            if (p != null)
            {
                try
                {
                    foreach (string line in File.ReadAllLines(p, Encoding.UTF8))
                    {
                        int i = line.IndexOf('=');
                        if (i > 0) s.d[line.Substring(0, i).Trim()] = line.Substring(i + 1);
                    }
                }
                catch { }
            }
            return s;
        }

        public void Save()
        {
            string[] lines = d.OrderBy(kv => kv.Key)
                              .Select(kv => kv.Key + "=" + (kv.Value ?? "").Replace("\r", " ").Replace("\n", " "))
                              .ToArray();
            var targets = new List<string>();
            if (filePath != null) targets.Add(filePath);
            targets.Add(ExeDirPath);
            targets.Add(AppDataPath);
            foreach (string p in targets.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(p));
                    File.WriteAllLines(p, lines, Encoding.UTF8);
                    filePath = p;
                    return;
                }
                catch { }
            }
        }

        public string Get(string key, string def)
        {
            string v;
            return d.TryGetValue(key, out v) ? v : def;
        }

        public int GetInt(string key, int def)
        {
            int v;
            return int.TryParse(Get(key, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : def;
        }

        public bool GetBool(string key, bool def)
        {
            string v = Get(key, "");
            if (v == "1") return true;
            if (v == "0") return false;
            return def;
        }

        public List<string> GetList(string key, char sep)
        {
            return Get(key, "").Split(sep).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        }

        public void Remove(string key)
        {
            d.Remove(key);
        }

        public bool Has(string key)
        {
            return d.ContainsKey(key);
        }

        public void Set(string key, object value)
        {
            if (value is bool) d[key] = (bool)value ? "1" : "0";
            else d[key] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }
    }
}
