using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace NetworkTools
{
    /// <summary>
    /// MAC address → vendor lookup.
    /// A small built-in table covers common network / industrial / virtual vendors.
    /// For full coverage put the IEEE "oui.txt" (or Wireshark "manuf") file next to NetworkTools.exe.
    /// </summary>
    public static class Oui
    {
        static Dictionary<string, string> table;
        static readonly object sync = new object();
        public static int ExternalCount { get; private set; }

        static readonly string[] BuiltIn =
        {
            // virtual machines
            "005056,000C29,000569,001C14=VMware",
            "00155D=Microsoft Hyper-V",
            "080027=VirtualBox",
            "525400=QEMU / KVM",
            "001C42=Parallels",
            // single-board computers
            "B827EB,DCA632,E45F01,D83ADD,2CCF67,28CDC1=Raspberry Pi",
            // network vendors
            "00000C,000142,000143,0001C7,0001C9,000216,000217,00024A,00024B=Cisco",
            "00180A,881544,E0553D,AC17C8=Cisco Meraki",
            "00090F,906CAC,704CA5,E81CBA,08 5B0E=Fortinet",
            "000C42,4C5E0C,6C3B6B,744D28,B869F4,CC2DE0,D4CA6D,E48D8C,2CC81B,488F5A,64D154,DC2C6E,18FD74,C4AD34=MikroTik",
            "00156D,002722,0418D6,24A43C,44D9E7,687251,788A20,802AA8,B4FBE4,DC9FDB,F09FC2,FCECDA,18E829,7483C2,E063DA,245A4C,70A741=Ubiquiti",
            "000585,0010DB,00121E,2C6BF5,3C6104,54E032,84B59C,F4CC55=Juniper",
            "000B86,001A1E,94B40F,204C03=Aruba",
            "00E0FC,001882,286ED4,48 46FB=Huawei",
            "00A0C5=Zyxel",
            "00095B,00146C,A040A0=Netgear",
            "00055D,000D88,1C7EE5=D-Link",
            "50C7BF,14CC20,C04A00,F4F26D,98DAC4,60E327,EC086B=TP-Link",
            "001FC6=ASUS",
            "001132,9009D0=Synology",
            "245EBE,00089B=QNAP",
            // cameras
            "4419B6,2857BE,C056E3,4CBD8F,BCAD28,A41437,54C415=Hikvision",
            "3CEF8C,9002A9,E0508B,4C11BF,14A78B,A0BD1D=Dahua",
            "00408C,ACCC8E,B8A44F=Axis",
            // industrial
            "0090E8=Moxa",
            "000E8C,001B1B,080006=Siemens",
            "0080F4=Schneider Electric",
            "0000BC,001D9C=Rockwell / Allen-Bradley",
            "00A045=Phoenix Contact",
            "000105=Beckhoff",
            "0030DE=WAGO",
            "00D0C9=Advantech",
            // PCs / NICs / servers
            "00E04C=Realtek",
            "001B21,3CFDFE,A0369F,001517,6805CA=Intel",
            "001422,F8BC12,18 66DA,B8CA3A=Dell",
            "002590,AC1F6B=Supermicro",
            // printers
            "008077=Brother",
            "000085=Canon",
            "000048=Epson"
        };

        static void EnsureLoaded()
        {
            lock (sync)
            {
                if (table != null) return;
                var t = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in BuiltIn)
                {
                    int eq = line.IndexOf('=');
                    string vendor = line.Substring(eq + 1);
                    foreach (string p in line.Substring(0, eq).Split(','))
                    {
                        string k = p.Replace(" ", "");
                        if (k.Length == 6) t[k] = vendor;
                    }
                }
                ExternalCount = 0;
                foreach (string dir in DataDirs())
                    foreach (string name in new[] { "oui.txt", "manuf", "manuf.txt" })
                    {
                        string path = Path.Combine(dir, name);
                        if (File.Exists(path)) ExternalCount += LoadFile(path, t);
                    }
                table = t;
            }
        }

        public const string DownloadUrl = "https://www.wireshark.org/download/automated/data/manuf";

        static string[] DataDirs()
        {
            return new[]
            {
                Path.GetDirectoryName(Application.ExecutablePath),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetworkTools")
            };
        }

        /// <summary>Downloads the Wireshark vendor list next to the exe (or to %APPDATA%). Returns the saved path.</summary>
        public static string Download()
        {
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            byte[] data;
            using (var wc = new System.Net.WebClient())
            {
                wc.Headers[System.Net.HttpRequestHeader.UserAgent] = "NetworkTools";
                data = wc.DownloadData(DownloadUrl);
            }
            if (data.Length < 100000 || Encoding.UTF8.GetString(data, 0, Math.Min(data.Length, 65536)).IndexOf("\t", StringComparison.Ordinal) < 0)
                throw new InvalidDataException("下載的內容不是廠牌資料檔。");
            Exception last = null;
            foreach (string dir in DataDirs())
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    string path = Path.Combine(dir, "manuf");
                    File.WriteAllBytes(path, data);
                    lock (sync) table = null;   // reload on next lookup
                    return path;
                }
                catch (Exception ex) { last = ex; }
            }
            throw last ?? new IOException("無法寫入檔案。");
        }

        public static bool HasExternal
        {
            get
            {
                foreach (string dir in DataDirs())
                    foreach (string name in new[] { "oui.txt", "manuf", "manuf.txt" })
                        if (File.Exists(Path.Combine(dir, name))) return true;
                return false;
            }
        }

        // IEEE oui.txt:  "00-00-0C   (hex)\t\tCisco Systems, Inc"
        // Wireshark manuf: "00:00:0C\tCisco\tCisco Systems, Inc"
        static int LoadFile(string path, Dictionary<string, string> t)
        {
            int n = 0;
            try
            {
                foreach (string raw in File.ReadLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length < 9 || line[0] == '#') continue;
                    int hex = line.IndexOf("(hex)", StringComparison.OrdinalIgnoreCase);
                    string key, vendor;
                    if (hex > 0)
                    {
                        key = line.Substring(0, hex).Trim().Replace("-", "");
                        vendor = line.Substring(hex + 5).Trim();
                    }
                    else
                    {
                        string[] f = line.Split('\t');
                        if (f.Length < 2 || f[0].Length != 8) continue;   // skip /28, /36 blocks
                        key = f[0].Replace(":", "").Replace("-", "");
                        vendor = (f.Length > 2 && f[2].Trim().Length > 0 ? f[2] : f[1]).Trim();
                    }
                    if (key.Length != 6 || vendor.Length == 0) continue;
                    t[key] = vendor;
                    n++;
                }
            }
            catch { }
            return n;
        }

        /// <summary>Warm the table on a background thread.</summary>
        public static void Preload()
        {
            if (table == null) System.Threading.ThreadPool.QueueUserWorkItem(delegate { EnsureLoaded(); });
        }

        public static string Lookup(string mac)
        {
            if (string.IsNullOrEmpty(mac)) return "";
            string hex = mac.Replace("-", "").Replace(":", "").Replace(".", "");
            if (hex.Length < 6) return "";
            EnsureLoaded();
            string v;
            if (table.TryGetValue(hex.Substring(0, 6), out v)) return v;
            int b0;
            if (int.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out b0) && (b0 & 0x02) != 0)
                return "(隨機 / 虛擬 MAC)";
            return "";
        }
    }
}
