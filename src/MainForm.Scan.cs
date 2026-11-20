using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace NetworkTools
{
    // Page 2: subnet scan (ping sweep + ARP, MAC, vendor, host name)
    public partial class MainForm
    {
        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        static extern int SendARP(uint destIp, uint srcIp, byte[] macAddr, ref uint physAddrLen);

        const int ScanMaxHosts = 4096;

        InputField fldScan;
        ModernButton btnScan;
        ToggleSwitch chkScanArp, chkScanNames;
        StatusBanner scanBanner;
        Card cardScanRes;
        ListView lvScan;
        ModernButton btnOui;
        System.Windows.Forms.Timer scanTimer;

        class ScanHit
        {
            public uint Ip;
            public long Rtt = -1;     // -1 = no ping reply (found by ARP only)
            public int Ttl;
            public string Mac = "", Vendor = "", Name = "";
            public bool Self;
        }

        volatile bool scanRunning;
        volatile int scanGen;
        class ScanJob
        {
            public int Total, Done, Next, ThreadsLeft;
            public volatile bool WorkersDone;
            public readonly ConcurrentQueue<ScanHit> Queue = new ConcurrentQueue<ScanHit>();
        }
        ScanJob job = new ScanJob();
        readonly Dictionary<uint, ListViewItem> scanItems = new Dictionary<uint, ListViewItem>();
        Stopwatch scanWatch = new Stopwatch();
        string scanRange = "";

        // =====================================================================
        //  UI
        // =====================================================================

        Panel BuildScanPage()
        {
            Oui.Preload();
            var page = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(S(10), S(10), S(10), S(8)) };
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, S(132)));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, S(66)));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            page.Controls.Add(root);

            // --- range card ---
            var card = new Card("掃描範圍", Theme.Card) { Subtitle = "例：192.168.1.0/24、192.168.1.10-50" };
            card.Padding = new Padding(S(18), S(44), S(20), S(6));
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var r0 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0), WrapContents = false };
            fldScan = new InputField(true, 12f) { Width = S(270), Height = S(38), Placeholder = "例：192.168.1.0/24" };
            fldScan.Margin = new Padding(0, S(3), S(8), S(3));
            foreach (string h in cfg.GetList("ScanHistory", '|')) fldScan.History.Add(h);
            fldScan.Box.Text = cfg.Get("LastScan", "");
            fldScan.Choices = ScanChoices;
            fldScan.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ToggleScan(); } };
            r0.Controls.Add(fldScan);
            btnScan = new ModernButton("▶  開始掃描", BtnKind.Primary, delegate { ToggleScan(); });
            btnScan.Font = new Font(Theme.UI.FontFamily, 10.5f, FontStyle.Bold);
            btnScan.MinimumSize = new Size(S(136), S(40));
            btnScan.Margin = new Padding(S(4), S(2), S(4), S(2));
            r0.Controls.Add(btnScan);
            chkScanArp = new ToggleSwitch("ARP 偵測") { Margin = new Padding(S(14), S(9), S(4), S(2)), Checked = cfg.GetBool("ScanArp", true) };
            r0.Controls.Add(chkScanArp);
            chkScanNames = new ToggleSwitch("解析主機名稱") { Margin = new Padding(S(10), S(9), S(4), S(2)), Checked = cfg.GetBool("ScanNames", true) };
            r0.Controls.Add(chkScanNames);
            t.Controls.Add(r0, 0, 0);

            var hint = new Label
            {
                AutoSize = true,
                ForeColor = Theme.Muted,
                Font = Theme.UISmall,
                Margin = new Padding(S(2), S(6), 0, 0),
                Text = "ARP 偵測可找到擋 Ping 的設備 (限同網段)   ·   雙擊結果直接 Ping，按右鍵可 PuTTY 連線、開啟網頁、複製"
            };
            t.Controls.Add(hint, 0, 1);
            card.Controls.Add(t);
            root.Controls.Add(card, 0, 0);

            var tip = new ToolTip { AutoPopDelay = 15000 };
            tip.SetToolTip(fldScan.Box, "支援：192.168.1.0/24、192.168.1.10-50、192.168.1.10-192.168.2.20\n最多 " + ScanMaxHosts + " 個位址\n右側 ▾ 可選擇網卡所在網段或歷史紀錄");
            tip.SetToolTip(chkScanArp, "對沒有回應 Ping 的位址再送 ARP 詢問，可找到關閉 Ping 的設備\n(只對電腦所在的網段有效，會稍微變慢)");

            // --- banner ---
            scanBanner = new StatusBanner { Dock = DockStyle.Fill };
            scanBanner.SetState("•", "尚未掃描", "輸入網段後按「開始掃描」或按 Enter", Theme.Slate, false);
            root.Controls.Add(scanBanner, 0, 1);

            // --- results ---
            cardScanRes = new Card("掃描結果", Theme.Card) { Subtitle = "" };
            cardScanRes.Padding = new Padding(S(18), S(50), S(20), S(14));
            btnOui = new ModernButton(Oui.HasExternal ? "更新廠牌資料庫" : "下載廠牌資料庫", BtnKind.Ghost, delegate { DownloadOui(); }) { MinimumSize = new Size(0, S(30)) };
            cardScanRes.AddHeaderControl(btnOui);
            cardScanRes.AddHeaderControl(new ModernButton("複製全部", BtnKind.Ghost, delegate { CopyScan(null); }) { MinimumSize = new Size(0, S(30)) });
            cardScanRes.AddHeaderControl(new ModernButton("清除", BtnKind.Ghost, delegate { if (!scanRunning) ClearScan(); }) { MinimumSize = new Size(0, S(30)) });
            lvScan = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                BorderStyle = BorderStyle.None,
                OwnerDraw = true,
                Font = Theme.UI,
                Margin = new Padding(0)
            };
            lvScan.SmallImageList = new ImageList { ImageSize = new Size(1, S(32)) };
            lvScan.ShowItemToolTips = true;
            lvScan.Columns.Add("IP 位址", S(186));
            lvScan.Columns.Add("延遲 · TTL", S(148));
            lvScan.Columns.Add("MAC", S(154));
            lvScan.Columns.Add("廠牌", S(126));
            lvScan.Columns.Add("主機名稱", S(120));
            lvScan.DrawColumnHeader += DrawAdapterHeader;
            lvScan.DrawItem += delegate { };
            lvScan.DrawSubItem += DrawScanCell;
            lvScan.MouseUp += delegate { lvScan.Invalidate(); };
            lvScan.Resize += delegate { FitScanColumns(); };
            lvScan.DoubleClick += delegate { ScanAction("ping"); };

            var menu = new ContextMenuStrip { Font = Theme.UI };
            menu.Items.Add("Ping 這台設備", null, delegate { ScanAction("ping"); });
            menu.Items.Add("PuTTY 連線 (SSH / Telnet)", null, delegate { ScanAction("putty"); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("開啟網頁  http://", null, delegate { ScanAction("http"); });
            menu.Items.Add("開啟網頁  https://", null, delegate { ScanAction("https"); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("複製 IP", null, delegate { ScanAction("copyip"); });
            menu.Items.Add("複製 MAC", null, delegate { ScanAction("copymac"); });
            menu.Items.Add("複製這一列", null, delegate { ScanAction("copyrow"); });
            menu.Opening += (s, e) => { if (lvScan.SelectedItems.Count == 0) e.Cancel = true; };
            lvScan.ContextMenuStrip = menu;
            cardScanRes.Controls.Add(lvScan);
            root.Controls.Add(cardScanRes, 0, 2);

            scanTimer = new System.Windows.Forms.Timer { Interval = 150 };
            scanTimer.Tick += delegate { DrainScan(); };
            return page;
        }

        void FitScanColumns()
        {
            int others = 0;
            for (int i = 0; i < lvScan.Columns.Count - 1; i++) others += lvScan.Columns[i].Width;
            int w = lvScan.ClientSize.Width - others - SystemInformation.VerticalScrollBarWidth - 2;
            lvScan.Columns[lvScan.Columns.Count - 1].Width = Math.Max(S(120), w);
        }

        void DrawScanCell(object sender, DrawListViewSubItemEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var h = e.Item.Tag as ScanHit;
            if (h == null) return;
            bool sel = e.Item.Selected;
            Rectangle cell = e.Bounds;
            Color bg = sel ? Theme.IndigoSoft : (e.ItemIndex % 2 == 0 ? Color.White : Theme.Subtle);
            using (var b = new SolidBrush(bg)) g.FillRectangle(b, cell);
            if (sel && e.ColumnIndex == 0)
                Theme.FillRound(g, new RectangleF(cell.X, cell.Y + S(5), S(4), cell.Height - S(10)), S(2), Theme.Indigo, Theme.Violet);
            Rectangle r = cell;
            r.Inflate(-S(8), 0);
            const TextFormatFlags L = TextFormatFlags.Left | TextFormatFlags.EndEllipsis;
            switch (e.ColumnIndex)
            {
                case 0:
                {
                    Color dc = h.Rtt >= 0 ? Theme.Green : Theme.Amber;
                    float d = S(8), dy = r.Y + (r.Height - d) / 2f;
                    using (var hb = new SolidBrush(Color.FromArgb(60, dc))) g.FillEllipse(hb, r.X + S(4) - 3, dy - 3, d + 6, d + 6);
                    using (var db = new SolidBrush(dc)) g.FillEllipse(db, r.X + S(4), dy, d, d);
                    string ipText = IpCalc.ToStr(h.Ip);
                    Theme.Draw(g, ipText, Theme.UIBold, new Rectangle(r.X + S(20), r.Y, r.Width - S(20), r.Height), sel ? Theme.Indigo : Theme.Text, L);
                    if (h.Self)
                    {
                        int tx = r.X + S(22) + TextRenderer.MeasureText(ipText, Theme.UIBold).Width;
                        Size ts = TextRenderer.MeasureText("本機", Theme.UISmall);
                        var tr = new RectangleF(tx, r.Y + (r.Height - S(20)) / 2f, ts.Width + S(8), S(20));
                        if (tr.Right <= r.Right)
                        {
                            Theme.FillRound(g, tr, S(10), Color.FromArgb(204, 251, 241), Color.FromArgb(204, 251, 241));
                            Theme.Draw(g, "本機", Theme.UISmall, Rectangle.Round(tr), Theme.Teal, TextFormatFlags.HorizontalCenter);
                        }
                    }
                    break;
                }
                case 1:
                    if (h.Rtt < 0) Theme.Draw(g, "僅 ARP 回應", Theme.UI, r, Theme.Amber, L);
                    else Theme.Draw(g, (h.Rtt < 1 ? "<1" : h.Rtt.ToString()) + " ms" + (h.Ttl > 0 ? "  ·  TTL " + h.Ttl : ""), Theme.UI, r, Theme.Text, L);
                    break;
                case 2:
                    Theme.Draw(g, h.Mac.Length > 0 ? h.Mac : "—", Theme.UI, r, Theme.Muted, L);
                    break;
                case 3:
                    Theme.Draw(g, h.Vendor, Theme.UI, r, Theme.Text, L);
                    break;
                case 4:
                    Theme.Draw(g, h.Name, Theme.UI, r, Theme.Text, L);
                    break;
            }
        }

        // ▾ menu: subnets of the connected adapters, then history
        List<string[]> ScanChoices()
        {
            var list = new List<string[]>();
            var seen = new HashSet<string>();
            foreach (AdapterInfo a in adapters.Where(x => x.Status == OperationalStatus.Up && !x.FromRegistry))
            {
                foreach (AddrInfo x in a.Addrs)
                {
                    string net = SubnetOf(x);
                    if (net == null || !seen.Add(net)) continue;
                    list.Add(new[] { a.Name + "   " + net, net });
                }
            }
            foreach (string h in fldScan.History)
                if (seen.Add(h)) list.Add(new[] { h, h });
            return list;
        }

        static string SubnetOf(AddrInfo x)
        {
            uint ip;
            if (!IpCalc.TryParseIPv4(x.Ip, out ip) || x.Prefix >= 31) return null;
            if ((ip >> 16) == 0xA9FE) return null;   // 169.254.x.x
            int prefix = Math.Max(x.Prefix, 20);      // keep the scan to at most 4096 hosts
            return IpCalc.ToStr(IpCalc.Network(ip, prefix)) + "/" + prefix;
        }

        void OnScanTabShown()
        {
            if (fldScan.Box.Text.Trim().Length > 0) return;
            AdapterInfo a = SelectedAdapter();
            var order = new List<AdapterInfo>();
            if (a != null) order.Add(a);
            order.AddRange(adapters.Where(x => x != a && x.Status == OperationalStatus.Up && !x.FromRegistry));
            foreach (AdapterInfo ad in order)
                foreach (AddrInfo x in ad.Addrs)
                {
                    string net = SubnetOf(x);
                    if (net != null) { fldScan.Box.Text = net; return; }
                }
        }

        // =====================================================================
        //  Range parsing
        // =====================================================================

        static bool ParseScanRange(string text, out List<uint> ips, out string err)
        {
            ips = new List<uint>();
            err = null;
            text = (text ?? "").Trim();
            if (text.Length == 0) { err = "請輸入要掃描的網段，例如 192.168.1.0/24"; return false; }
            uint a, b;
            int dash = text.IndexOf('-');
            if (dash > 0)
            {
                string left = text.Substring(0, dash).Trim(), right = text.Substring(dash + 1).Trim();
                if (!IpCalc.TryParseIPv4(left, out a)) { err = "起始 IP 格式錯誤：" + left; return false; }
                int last;
                if (int.TryParse(right, out last))
                {
                    if (last < 0 || last > 255) { err = "結束數字需為 0 ~ 255"; return false; }
                    b = (a & 0xFFFFFF00u) | (uint)last;
                }
                else if (!IpCalc.TryParseIPv4(right, out b)) { err = "結束 IP 格式錯誤：" + right; return false; }
                if (b < a) { uint tmp = a; a = b; b = tmp; }
                if ((ulong)b - a + 1 > ScanMaxHosts) { err = "範圍太大，一次最多掃描 " + ScanMaxHosts + " 個位址。"; return false; }
                for (ulong v = a; v <= b; v++) ips.Add((uint)v);
                return true;
            }
            uint ip; int prefix;
            if (!IpCalc.TryParseCidr(text, out ip, out prefix, out err)) return false;
            if (prefix >= 31) { ips.Add(ip); return true; }
            uint net = IpCalc.Network(ip, prefix), bc = IpCalc.Broadcast(ip, prefix);
            if ((ulong)bc - net - 1 > ScanMaxHosts) { err = "/" + prefix + " 範圍太大，一次最多掃描 " + ScanMaxHosts + " 個位址 (請用 /20 以上)。"; return false; }
            for (uint v = net + 1; v < bc; v++) ips.Add(v);
            return true;
        }

        // =====================================================================
        //  Scanning
        // =====================================================================

        void ToggleScan()
        {
            if (scanRunning) StopScan(true);
            else StartScan();
        }

        void StartScan()
        {
            List<uint> ips; string err;
            string text = fldScan.Box.Text.Trim();
            if (!ParseScanRange(text, out ips, out err)) { Warn(err); fldScan.Box.Focus(); return; }
            AddHistory(fldScan, text);
            scanRange = text;
            ClearScan();

            // own addresses (shown as 本機) and the on-link subnets where ARP works
            var selfMac = new Dictionary<uint, string>();
            var local = new List<KeyValuePair<uint, int>>();
            foreach (AdapterInfo ad in adapters.Where(x => x.Status == OperationalStatus.Up && !x.FromRegistry))
                foreach (AddrInfo x in ad.Addrs)
                {
                    uint v;
                    if (!IpCalc.TryParseIPv4(x.Ip, out v)) continue;
                    selfMac[v] = ad.Mac;
                    if (x.Prefix < 32) local.Add(new KeyValuePair<uint, int>(IpCalc.Network(v, x.Prefix), x.Prefix));
                }

            int gen = ++scanGen;
            scanRunning = true;
            job = new ScanJob();
            ScanJob thisJob = job;
            job.Total = ips.Count;


            bool arp = chkScanArp.Checked;
            int threads = Math.Min(64, ips.Count);
            job.ThreadsLeft = threads;
            scanWatch = Stopwatch.StartNew();
            btnScan.Text = "■  停止";
            btnScan.Kind = BtnKind.Danger;
            btnScan.Invalidate();
            scanBanner.Progress = 0;
            UpdateScanBanner();
            scanTimer.Start();

            for (int i = 0; i < threads; i++)
            {
                var th = new Thread(() => ScanWorker(thisJob, gen, ips, arp, selfMac, local)) { IsBackground = true };
                th.Start();
            }
        }

        void ScanWorker(ScanJob job, int gen, List<uint> ips, bool arp, Dictionary<uint, string> selfMac, List<KeyValuePair<uint, int>> local)
        {
            byte[] buf = Encoding.ASCII.GetBytes("abcdefghijklmnopqrstuvwabcdefghi");
            try
            {
                using (var p = new Ping())
                {
                    while (gen == scanGen)
                    {
                        int i = Interlocked.Increment(ref job.Next) - 1;
                        if (i >= ips.Count) break;
                        uint ip = ips[i];
                        var hit = new ScanHit { Ip = ip };
                        try
                        {
                            PingReply r = p.Send(new IPAddress(ToNetworkOrder(ip)), 800, buf, new PingOptions(128, true));
                            if (r.Status == IPStatus.Success)
                            {
                                hit.Rtt = r.RoundtripTime;
                                hit.Ttl = r.Options != null ? r.Options.Ttl : 0;
                            }
                        }
                        catch { }
                        if (gen != scanGen) break;

                        bool onLink = local.Any(n => IpCalc.Network(ip, n.Value) == n.Key);
                        string mac;
                        hit.Self = selfMac.TryGetValue(ip, out mac);
                        if (hit.Self) hit.Mac = mac ?? "";
                        else if (onLink && (hit.Rtt >= 0 || arp)) hit.Mac = ArpLookup(ip);

                        bool alive = hit.Rtt >= 0 || hit.Self || hit.Mac.Length > 0;
                        if (alive)
                        {
                            if (hit.Self && hit.Rtt < 0) hit.Rtt = 0;
                            hit.Vendor = Oui.Lookup(hit.Mac);
                            job.Queue.Enqueue(hit);
                        }
                        Interlocked.Increment(ref job.Done);
                    }
                }
            }
            catch { }
            if (Interlocked.Decrement(ref job.ThreadsLeft) == 0 && gen == scanGen) job.WorkersDone = true;
        }

        static uint ToNetworkOrder(uint ip)
        {
            return BitConverter.ToUInt32(new[] { (byte)(ip >> 24), (byte)(ip >> 16), (byte)(ip >> 8), (byte)ip }, 0);
        }

        static string ArpLookup(uint ip)
        {
            try
            {
                byte[] mac = new byte[6];
                uint len = (uint)mac.Length;
                if (SendARP(ToNetworkOrder(ip), 0, mac, ref len) != 0 || len < 6) return "";
                if (mac.All(x => x == 0)) return "";
                return string.Join("-", mac.Take(6).Select(x => x.ToString("X2")));
            }
            catch { return ""; }
        }

        void DrainScan()
        {
            ScanHit h;
            bool any = false;
            lvScan.BeginUpdate();
            while (job.Queue.TryDequeue(out h))
            {
                any = true;
                AddScanHit(h);
            }
            lvScan.EndUpdate();
            if (any) UpdateScanSubtitle();
            if (scanRunning)
            {
                UpdateScanBanner();
                if (job.WorkersDone) FinishScan(false);
            }
        }

        void AddScanHit(ScanHit h)
        {
            if (scanItems.ContainsKey(h.Ip)) return;
            var it = new ListViewItem(IpCalc.ToStr(h.Ip));
            it.SubItems.Add("");
            it.SubItems.Add(h.Mac);
            it.SubItems.Add(h.Vendor);
            it.SubItems.Add("");
            it.Tag = h;
            it.ToolTipText = ScanTip(h);
            // keep the list sorted by IP
            int pos = 0;
            while (pos < lvScan.Items.Count && ((ScanHit)lvScan.Items[pos].Tag).Ip < h.Ip) pos++;
            lvScan.Items.Insert(pos, it);
            scanItems[h.Ip] = it;
            if (chkScanNames.Checked) ResolveName(scanGen, h);
        }

        void ResolveName(int gen, ScanHit h)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string name = "";
                try
                {
                    string ipText = IpCalc.ToStr(h.Ip);
                    IPHostEntry e = Dns.GetHostEntry(IPAddress.Parse(ipText));
                    if (e != null && !string.IsNullOrEmpty(e.HostName) && e.HostName != ipText) name = e.HostName;
                }
                catch { }
                if (name.Length == 0) return;
                Ui(() =>
                {
                    if (gen != scanGen && scanRunning) return;
                    ListViewItem it;
                    if (!scanItems.TryGetValue(h.Ip, out it) || it.Tag != h) return;
                    h.Name = name;
                    it.SubItems[4].Text = name;
                    it.ToolTipText = ScanTip(h);
                    lvScan.Invalidate(it.Bounds);
                });
            });
        }

        static string ScanTip(ScanHit h)
        {
            return IpCalc.ToStr(h.Ip) + (h.Self ? "  (本機)" : "") +
                   (h.Mac.Length > 0 ? "\nMAC：" + h.Mac : "") +
                   (h.Vendor.Length > 0 ? "\n廠牌：" + h.Vendor : "") +
                   (h.Name.Length > 0 ? "\n主機名稱：" + h.Name : "");
        }

        void DownloadOui()
        {
            if (!Ask("要從 Wireshark 官方網站下載 MAC 廠牌資料 (約 3 MB) 嗎？\n\n" + Oui.DownloadUrl +
                     "\n\n檔案會存在程式資料夾 (manuf)，之後掃描就能顯示大部分設備的廠牌。"))
                return;
            btnOui.Enabled = false;
            btnOui.Text = "下載中…";
            var th = new Thread(() =>
            {
                string path = null, err = null;
                try { path = Oui.Download(); }
                catch (Exception ex) { err = ex.InnerException != null ? ex.InnerException.Message : ex.Message; }
                Ui(() =>
                {
                    btnOui.Enabled = true;
                    btnOui.Text = Oui.HasExternal ? "更新廠牌資料庫" : "下載廠牌資料庫";
                    cardScanRes.PerformLayout();
                    cardScanRes.Invalidate();
                    if (err != null) { Warn("下載失敗：" + err); return; }
                    Oui.Lookup("00-00-00-00-00-00");   // load the new file now
                    foreach (ListViewItem it in lvScan.Items)
                    {
                        var h = (ScanHit)it.Tag;
                        h.Vendor = Oui.Lookup(h.Mac);
                        it.SubItems[3].Text = h.Vendor;
                        it.ToolTipText = ScanTip(h);
                    }
                    lvScan.Invalidate();
                    MessageBox.Show(this, "已下載廠牌資料庫 (" + Oui.ExternalCount.ToString("N0") + " 筆)：\n" + path,
                                    "網路工具", MessageBoxButtons.OK, MessageBoxIcon.Information);
                });
            }) { IsBackground = true };
            th.Start();
        }

        void UpdateScanBanner()
        {
            int done = Math.Min(job.Done, job.Total);
            scanBanner.Progress = job.Total > 0 ? done / (float)job.Total : 0;
            scanBanner.SetState("…", "掃描 " + scanRange + " 中",
                done + " / " + job.Total + "   ·   已找到 " + scanItems.Count + " 台   ·   " + (scanWatch.ElapsedMilliseconds / 1000.0).ToString("0.0") + " 秒",
                Theme.Indigo, true);
        }

        void UpdateScanSubtitle()
        {
            int arpOnly = scanItems.Values.Count(i => ((ScanHit)i.Tag).Rtt < 0);
            cardScanRes.Subtitle = scanItems.Count == 0 ? "" :
                "共 " + scanItems.Count + " 台" + (arpOnly > 0 ? "  ·  " + arpOnly + " 台僅 ARP 回應" : "");
            cardScanRes.Invalidate();
        }

        void StopScan(bool byUser)
        {
            if (!scanRunning) return;
            scanGen++;
            FinishScan(byUser);
        }

        void FinishScan(bool stopped)
        {
            scanRunning = false;
            DrainScan();
            scanTimer.Stop();
            scanWatch.Stop();
            btnScan.Text = "▶  開始掃描";
            btnScan.Kind = BtnKind.Primary;
            btnScan.Invalidate();
            scanBanner.Progress = -1;
            string sec = (scanWatch.ElapsedMilliseconds / 1000.0).ToString("0.0");
            if (stopped)
                scanBanner.SetState("■", "已停止   " + scanRange,
                    "已掃描 " + Math.Min(job.Done, job.Total) + " / " + job.Total + "   ·   找到 " + scanItems.Count + " 台", Theme.Slate, false);
            else if (scanItems.Count > 0)
                scanBanner.SetState("✔", "掃描完成   找到 " + scanItems.Count + " 台",
                    scanRange + "   ·   " + job.Total + " 個位址   ·   " + sec + " 秒", OkColor, false);
            else
                scanBanner.SetState("✖", "沒有找到設備", scanRange + "   ·   " + job.Total + " 個位址   ·   " + sec + " 秒", FailColor, false);
            UpdateScanSubtitle();
            SaveAll();
        }

        void ClearScan()
        {
            ScanHit dummy;
            while (job.Queue.TryDequeue(out dummy)) { }
            scanItems.Clear();
            lvScan.Items.Clear();
            UpdateScanSubtitle();
        }

        // =====================================================================
        //  Actions on a result
        // =====================================================================

        void ScanAction(string what)
        {
            if (lvScan.SelectedItems.Count == 0) return;
            var h = (ScanHit)lvScan.SelectedItems[0].Tag;
            string ip = IpCalc.ToStr(h.Ip);
            switch (what)
            {
                case "ping":
                    StopPing(false);
                    fldTarget.Box.Text = ip;
                    ShowTab(TabPing);
                    StartPing();
                    break;
                case "putty":
                    if (segProto.SelectedText == "Serial") segProto.SelectedIndex = 0;
                    fldTarget.Box.Text = ip;
                    ShowTab(TabPing);
                    LaunchPutty();
                    break;
                case "http":
                case "https":
                    try { Process.Start(new ProcessStartInfo(what + "://" + ip) { UseShellExecute = true }); }
                    catch (Exception ex) { Warn("無法開啟瀏覽器：" + ex.Message); }
                    break;
                case "copyip":
                    SetClipboard(ip);
                    break;
                case "copymac":
                    SetClipboard(h.Mac);
                    break;
                case "copyrow":
                    CopyScan(h);
                    break;
            }
        }

        void CopyScan(ScanHit only)
        {
            var sb = new StringBuilder();
            if (only == null) sb.AppendLine("IP\t延遲(ms)\tTTL\tMAC\t廠牌\t主機名稱");
            foreach (ListViewItem it in lvScan.Items)
            {
                var h = (ScanHit)it.Tag;
                if (only != null && h != only) continue;
                sb.AppendLine(string.Join("\t", new[]
                {
                    IpCalc.ToStr(h.Ip), h.Rtt < 0 ? "(ARP)" : h.Rtt.ToString(), h.Ttl > 0 ? h.Ttl.ToString() : "",
                    h.Mac, h.Vendor, h.Self ? "本機 " + h.Name : h.Name
                }));
            }
            if (only == null && lvScan.Items.Count == 0) { Warn("目前沒有掃描結果。"); return; }
            SetClipboard(sb.ToString().TrimEnd());
        }

        void SetClipboard(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            try { Clipboard.SetText(s); }
            catch (Exception ex) { Warn("無法複製：" + ex.Message); }
        }

        void SaveScanSettings()
        {
            if (fldScan == null) return;
            cfg.Set("ScanHistory", string.Join("|", fldScan.History));
            cfg.Set("LastScan", fldScan.Box.Text.Trim());
            cfg.Set("ScanArp", chkScanArp.Checked);
            cfg.Set("ScanNames", chkScanNames.Checked);
        }
    }
}
