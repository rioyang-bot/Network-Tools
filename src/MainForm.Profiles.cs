using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace NetworkTools
{
    // IP profiles (saved IP / gateway / DNS sets) and the one-click "restore" snapshot.
    public partial class MainForm
    {
        ModernButton btnRestore;

        class IpProfile
        {
            public string Name, Ip, Gw, Dns1, Dns2;

            public string Encode()
            {
                return string.Join(";", new[] { Clean(Name), Clean(Ip), Clean(Gw), Clean(Dns1), Clean(Dns2) });
            }

            public static IpProfile Decode(string s)
            {
                string[] f = s.Split(';');
                if (f.Length < 2 || f[0].Trim().Length == 0) return null;
                return new IpProfile
                {
                    Name = f[0].Trim(),
                    Ip = f[1].Trim(),
                    Gw = f.Length > 2 ? f[2].Trim() : "",
                    Dns1 = f.Length > 3 ? f[3].Trim() : "",
                    Dns2 = f.Length > 4 ? f[4].Trim() : ""
                };
            }

            public static string Clean(string s)
            {
                return (s ?? "").Replace(";", " ").Replace("|", " ").Replace("=", " ").Trim();
            }

            public string Summary
            {
                get { return Ip + (Gw.Length > 0 ? "   GW " + Gw : ""); }
            }
        }

        // =====================================================================
        //  Profiles
        // =====================================================================

        List<IpProfile> LoadProfiles()
        {
            return cfg.GetList("Profiles", '|').Select(IpProfile.Decode).Where(p => p != null).ToList();
        }

        void SaveProfiles(List<IpProfile> list)
        {
            cfg.Set("Profiles", string.Join("|", list.Select(p => p.Encode())));
            cfg.Save();
        }

        void ShowProfileMenu(Control anchor)
        {
            List<IpProfile> list = LoadProfiles();
            var menu = new ContextMenuStrip { Font = Theme.UI, ShowImageMargin = false };
            if (list.Count == 0)
                menu.Items.Add("(尚無設定檔)").Enabled = false;
            foreach (IpProfile p in list)
            {
                IpProfile prof = p;
                var it = new ToolStripMenuItem("  " + p.Name + "      " + p.Summary + "  ");
                it.Click += delegate { LoadProfile(prof); };
                menu.Items.Add(it);
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("＋  將目前欄位存成設定檔…", null, delegate { SaveProfileFromEditor(); });
            if (list.Count > 0)
            {
                var del = new ToolStripMenuItem("刪除設定檔");
                foreach (IpProfile p in list)
                {
                    IpProfile prof = p;
                    del.DropDownItems.Add("  " + p.Name + "  ", null, delegate { DeleteProfile(prof.Name); });
                }
                menu.Items.Add(del);
            }
            menu.Show(anchor, new Point(anchor.Width, anchor.Height), ToolStripDropDownDirection.BelowLeft);
        }

        void LoadProfile(IpProfile p)
        {
            fldIp.Box.Text = p.Ip;          // recalculates the suggested gateway...
            fldGw.Box.Text = p.Gw;          // ...which the profile's own gateway then replaces
            fldDns1.Box.Text = p.Dns1;
            fldDns2.Box.Text = p.Dns2;
            Log("已帶入設定檔「" + p.Name + "」：" + p.Summary);
            AdapterInfo a = SelectedAdapter();
            if (a == null) return;
            if (Ask("已帶入設定檔「" + p.Name + "」\n\n    IP：" + p.Ip +
                    "\n    Gateway：" + (p.Gw.Length > 0 ? p.Gw : "(不設定)") +
                    "\n    DNS：" + (p.Dns1.Length > 0 ? p.Dns1 + (p.Dns2.Length > 0 ? ", " + p.Dns2 : "") : "(不設定)") +
                    "\n\n要立即套用到「" + a.Name + "」嗎？\n(選「否」只帶入欄位，可修改後再按「套用」)"))
                ApplyPrimary();
        }

        void SaveProfileFromEditor()
        {
            uint ip; int prefix; string gw, dns1, dns2;
            if (!ReadEditor(out ip, out prefix, out gw, out dns1, out dns2)) return;
            string ipText = IpCalc.ToStr(ip) + "/" + prefix;
            List<IpProfile> list = LoadProfiles();
            IpProfile same = list.FirstOrDefault(p => p.Ip == ipText);
            string name = PromptText("儲存設定檔", "設定檔名稱 (例如：客戶 A 交換器)", same != null ? same.Name : ipText);
            if (name == null) return;
            name = IpProfile.Clean(name);
            if (name.Length == 0) { Warn("請輸入設定檔名稱。"); return; }
            IpProfile existing = list.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (!Ask("已經有名為「" + name + "」的設定檔，要覆蓋嗎？")) return;
                list.Remove(existing);
            }
            list.Add(new IpProfile { Name = name, Ip = ipText, Gw = gw, Dns1 = dns1, Dns2 = dns2 });
            list = list.OrderBy(p => p.Name, StringComparer.CurrentCulture).ToList();
            SaveProfiles(list);
            Log("已儲存設定檔「" + name + "」：" + ipText + (gw.Length > 0 ? "  GW " + gw : ""));
        }

        void DeleteProfile(string name)
        {
            if (!Ask("確定要刪除設定檔「" + name + "」嗎？")) return;
            List<IpProfile> list = LoadProfiles();
            list.RemoveAll(p => p.Name == name);
            SaveProfiles(list);
            Log("已刪除設定檔「" + name + "」。");
        }

        // =====================================================================
        //  Restore snapshot: the adapter's settings before this tool first changed it
        // =====================================================================

        static string SnapKey(AdapterInfo a) { return "Snap." + a.Id; }

        void TakeSnapshot(AdapterInfo a)
        {
            if (cfg.Has(SnapKey(a))) return;   // keep the ORIGINAL settings until they are restored
            List<string> dns = AdapterInfo.StaticDns(a.Id);
            string v = DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "|" + (a.Dhcp ? "1" : "0") + "|" +
                       string.Join(",", a.Addrs.Select(x => x.Ip + "/" + x.Prefix)) + "|" +
                       string.Join(",", a.Gateways) + "|" + string.Join(",", dns);
            cfg.Set(SnapKey(a), v);
            cfg.Save();
            Log("已記錄「" + a.Name + "」變更前的設定，可按「↶ 還原」改回。");
        }

        class Snapshot
        {
            public string Time;
            public bool Dhcp;
            public List<AddrInfo> Addrs = new List<AddrInfo>();
            public List<string> Gateways = new List<string>(), Dns = new List<string>();
        }

        Snapshot ReadSnapshot(AdapterInfo a)
        {
            if (a == null || !cfg.Has(SnapKey(a))) return null;
            string[] f = cfg.Get(SnapKey(a), "").Split('|');
            if (f.Length < 5) return null;
            var s = new Snapshot { Time = f[0], Dhcp = f[1] == "1" };
            foreach (string x in f[2].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] ip = x.Split('/');
                int pfx;
                if (ip.Length == 2 && int.TryParse(ip[1], out pfx)) s.Addrs.Add(new AddrInfo { Ip = ip[0], Prefix = pfx });
            }
            s.Gateways.AddRange(f[3].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            s.Dns.AddRange(f[4].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            return s;
        }

        void UpdateRestoreButton()
        {
            if (btnRestore == null) return;
            Snapshot s = ReadSnapshot(SelectedAdapter());
            btnRestore.Enabled = s != null && !busy;
            btnRestore.Invalidate();
        }

        void RestoreSnapshot()
        {
            AdapterInfo a = SelectedAdapter();
            Snapshot s = ReadSnapshot(a);
            if (s == null) { Warn("這張網卡沒有可還原的紀錄。"); return; }
            if (!s.Dhcp && s.Addrs.Count == 0) { Warn("紀錄中沒有 IP 資料，無法還原。"); return; }

            string desc = s.Dhcp ? "DHCP 自動取得" : string.Join(", ", s.Addrs.Select(x => x.Ip + "/" + x.Prefix));
            string dnsDesc = s.Dns.Count > 0 ? string.Join(", ", s.Dns) : (s.Dhcp ? "自動取得" : "(無)");
            if (!Ask("要將「" + a.Name + "」還原到 " + s.Time + " 變更前的設定嗎？\n\n    IP：" + desc +
                     (s.Dhcp ? "" : "\n    Gateway：" + (s.Gateways.Count > 0 ? s.Gateways[0] : "(無)")) +
                     "\n    DNS：" + dnsDesc))
                return;
            if (!EnsureAdmin()) return;

            string idx = a.Index.ToString();
            var cmds = new List<string>();
            if (s.Dhcp)
                cmds.Add("interface ipv4 set address name=" + idx + " source=dhcp");
            else
            {
                AddrInfo first = s.Addrs[0];
                cmds.Add("interface ipv4 set address name=" + idx + " source=static address=" + first.Ip +
                         " mask=" + first.Mask + " gateway=" + (s.Gateways.Count > 0 ? s.Gateways[0] : "none"));
                foreach (AddrInfo x in s.Addrs.Skip(1))
                    cmds.Add("interface ipv4 add address name=" + idx + " address=" + x.Ip + " mask=" + x.Mask);
            }
            if (s.Dns.Count > 0)
            {
                cmds.Add("interface ipv4 set dnsservers name=" + idx + " source=static address=" + s.Dns[0] + " register=primary validate=no");
                for (int i = 1; i < s.Dns.Count; i++)
                    cmds.Add("interface ipv4 add dnsservers name=" + idx + " address=" + s.Dns[i] + " index=" + (i + 1) + " validate=no");
            }
            else if (s.Dhcp)
                cmds.Add("interface ipv4 set dnsservers name=" + idx + " source=dhcp");
            else
                cmds.Add("interface ipv4 set dnsservers name=" + idx + " source=static address=none register=primary validate=no");

            string key = SnapKey(a);
            RunNetshBatch("還原設定：" + a.Name + " → " + desc, cmds, delegate
            {
                cfg.Remove(key);
                cfg.Save();
                Log("已還原，並清除這張網卡的還原紀錄。");
            });
        }

        // =====================================================================
        //  Small text prompt dialog
        // =====================================================================

        string PromptText(string title, string label, string initial)
        {
            using (var f = new Form())
            {
                f.Text = title;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = f.MinimizeBox = false;
                f.ShowInTaskbar = false;
                f.StartPosition = FormStartPosition.CenterParent;
                f.AutoScaleMode = AutoScaleMode.None;
                f.BackColor = Theme.Card;
                f.Font = Theme.UI;
                f.ClientSize = new Size(S(420), S(150));

                var lbl = new Label { Text = label, AutoSize = true, ForeColor = Theme.Muted, Font = Theme.UIBold, Location = new Point(S(18), S(16)) };
                var fld = new InputField(false, 11f) { Location = new Point(S(16), S(42)), Size = new Size(S(388), S(36)) };
                fld.Box.Text = initial ?? "";
                var ok = new ModernButton("確定", BtnKind.Primary, null) { DialogResult = DialogResult.OK };
                var cancel = new ModernButton("取消", BtnKind.Secondary, null) { DialogResult = DialogResult.Cancel };
                var row = new FlowLayoutPanel
                {
                    FlowDirection = FlowDirection.RightToLeft,
                    Location = new Point(S(16), S(94)),
                    Size = new Size(S(392), S(48)),
                    BackColor = Theme.Card
                };
                row.Controls.Add(cancel);
                row.Controls.Add(ok);
                f.Controls.Add(lbl);
                f.Controls.Add(fld);
                f.Controls.Add(row);
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                f.Shown += delegate { fld.Box.Focus(); fld.Box.SelectAll(); };
                return f.ShowDialog(this) == DialogResult.OK ? fld.Box.Text.Trim() : null;
            }
        }
    }
}
