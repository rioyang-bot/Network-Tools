using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace NetworkTools
{
    public partial class MainForm : Form
    {
        readonly Settings cfg;
        readonly bool isAdmin;

        static int S(int v) { return Theme.S(v); }

        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        HeaderPanel header;
        Panel pageIp, pageScan, pagePing;
        Panel[] pages;
        int currentTab;
        const int TabIp = 0, TabScan = 1, TabPing = 2;

        // ---------- Page 1: adapter IP ----------
        ListView lvAdapters;
        ToggleSwitch chkShowHidden;
        SplitContainer splitIp;
        Label lblCalc;
        Card curCard;
        InfoGrid info;
        ListBox lstIps;
        InputField fldIp, fldGw, fldDns1, fldDns2;
        TextBox txtLog;
        readonly List<Button> netshButtons = new List<Button>();
        List<AdapterInfo> adapters = new List<AdapterInfo>();
        string defaultId;
        readonly HashSet<string> hiddenIds;
        bool busy;
        System.Windows.Forms.Timer netChangeTimer;

        // ---------- Page 2: ping / PuTTY ----------
        InputField fldTarget, fldPort, fldUser;
        Segmented segProto;
        ModernButton btnPing;
        ToggleSwitch chkContinuous;
        StatusBanner banner;
        StatTile tileSent, tileRecv, tileLoss, tileAvg;
        RichTextBox rtbPing;
        volatile bool pingRunning;
        volatile int pingGen;
        string pingTarget = "";
        int pSent, pRecv;
        long rMin, rMax, rSum;

        static readonly Color OkColor = Color.FromArgb(5, 150, 105);
        static readonly Color FailColor = Color.FromArgb(225, 29, 72);
        static readonly Color TermText = Color.FromArgb(203, 213, 225);
        static readonly Color TermOk = Color.FromArgb(110, 231, 183);
        static readonly Color TermFail = Color.FromArgb(253, 164, 175);
        static readonly Color TermInfo = Color.FromArgb(125, 211, 252);

        public MainForm()
        {
            cfg = Settings.Load();
            isAdmin = IsAdministrator();
            float scale;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
            Theme.Init(scale);

            defaultId = cfg.Get("DefaultAdapter", "");
            hiddenIds = new HashSet<string>(cfg.GetList("HiddenAdapters", ','), StringComparer.OrdinalIgnoreCase);

            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.UI;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Text = "網路工具 Network Tools" + (isAdmin ? "   [系統管理員]" : "");
            Icon = MakeIcon();
            // Fixed window size (no resizing / maximizing)
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Size = new Size(1080, 960);   // actual screen pixels
            StartPosition = FormStartPosition.Manual;

            var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            pageIp = BuildIpPage();
            pageScan = BuildScanPage();
            pagePing = BuildPingPage();
            pages = new[] { pageIp, pageScan, pagePing };
            foreach (Panel p in pages) host.Controls.Add(p);

            header = new HeaderPanel(new[] { "網卡 IP 設定", "網段掃描", "Ping / PuTTY" }, isAdmin);
            header.TabChanged += delegate { ShowTab(header.Active); };
            header.AdminClicked += delegate { RestartAsAdmin(); };

            Controls.Add(host);
            Controls.Add(header);

            RestoreWindow();

            netChangeTimer = new System.Windows.Forms.Timer { Interval = 1200 };
            netChangeTimer.Tick += delegate { netChangeTimer.Stop(); if (!busy) RefreshAdapters(); };
            NetworkChange.NetworkAddressChanged += OnNetworkChanged;

            ResizeEnd += delegate { SaveAll(); };   // fires after the window is moved
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RefreshAdapters();
            UpdateCalc(false);
            ShowTab(Math.Max(0, Math.Min(pages.Length - 1, cfg.GetInt("Tab", 0))));
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                int h = splitIp.Height;
                int dist = cfg.GetInt("SplitIpV2", h - S(290));
                dist = Math.Max(S(150), Math.Min(dist, h - S(286)));
                if (dist > 0) splitIp.SplitterDistance = dist;
                splitIp.Panel1MinSize = S(140);
                splitIp.Panel2MinSize = S(286);
            }
            catch { }
            splitIp.SplitterMoved += delegate { SaveAll(); };
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            pingRunning = false;
            scanGen++;
            NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
            SaveAll();
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Tab)) { ShowTab((currentTab + 1) % pages.Length); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.Tab)) { ShowTab((currentTab + pages.Length - 1) % pages.Length); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void ShowTab(int i)
        {
            currentTab = i;
            for (int k = 0; k < pages.Length; k++) pages[k].Visible = k == i;
            header.Active = i;
            header.Invalidate();
            if (i == TabPing) fldTarget.Box.Focus();
            if (i == TabScan) OnScanTabShown();
        }

        // =====================================================================
        //  Window size persistence
        // =====================================================================

        void RestoreWindow()
        {
            int w = Width, h = Height;   // size is fixed; only the position is remembered
            int x = cfg.GetInt("Win.X", int.MinValue);
            int y = cfg.GetInt("Win.Y", int.MinValue);
            var r = new Rectangle(x, y, w, h);
            bool onScreen = x != int.MinValue && y != int.MinValue &&
                Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(new Rectangle(r.X + 20, r.Y, Math.Max(40, r.Width - 40), S(30))));
            if (!onScreen)
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                r = new Rectangle(wa.X + Math.Max(0, (wa.Width - w) / 2), wa.Y + Math.Max(0, (wa.Height - h) / 2), w, h);
            }
            Location = r.Location;
        }

        void SaveAll()
        {
            Rectangle r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            if (r.Width > 0 && r.Height > 0)
            {
                cfg.Set("Win.X", r.X);
                cfg.Set("Win.Y", r.Y);
            }
            if (splitIp != null && splitIp.Height > 0) cfg.Set("SplitIpV2", splitIp.SplitterDistance);
            cfg.Set("Tab", currentTab);

            cfg.Set("DefaultAdapter", defaultId ?? "");
            cfg.Set("HiddenAdapters", string.Join(",", hiddenIds));
            cfg.Set("ShowHidden", chkShowHidden.Checked);
            cfg.Set("IpHistory", string.Join("|", fldIp.History));
            cfg.Set("PingHistory", string.Join("|", fldTarget.History));
            cfg.Set("LastPing", fldTarget.Box.Text.Trim());
            cfg.Set("Continuous", chkContinuous.Checked);
            cfg.Set("PuttyProto", segProto.SelectedText);
            cfg.Set("PuttyPort", fldPort.Box.Text.Trim());
            cfg.Set("PuttyUser", fldUser.Box.Text.Trim());
            SaveSerialSettings();
            SaveScanSettings();
            cfg.Save();
        }

        // =====================================================================
        //  Page 1 UI
        // =====================================================================

        Panel BuildIpPage()
        {
            var page = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(S(10), S(10), S(10), S(8)) };
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, S(108)));
            page.Controls.Add(root);

            splitIp = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = S(4), Margin = new Padding(0), FixedPanel = FixedPanel.Panel2 };
            root.Controls.Add(splitIp, 0, 0);

            // --- adapters card (toolbar lives in the card header) ---
            var cardA = new Card("網路介面卡", Theme.Card);
            cardA.Padding = new Padding(cardA.Padding.Left, S(54), cardA.Padding.Right, S(12));
            cardA.AddHeaderControl(HeaderBtn(new ModernButton("⟳  重新整理", BtnKind.Secondary, delegate { RefreshAdapters(); })));
            cardA.AddHeaderControl(HeaderBtn(new ModernButton("★  設為預設網卡", BtnKind.Secondary, delegate { SetDefaultAdapter(); })));
            cardA.AddHeaderControl(HeaderBtn(new ModernButton("隱藏 / 取消隱藏", BtnKind.Secondary, delegate { ToggleHideAdapter(); })));
            chkShowHidden = new ToggleSwitch("顯示已隱藏");
            chkShowHidden.Checked = cfg.GetBool("ShowHidden", false);
            chkShowHidden.CheckedChanged += delegate { RefreshAdapters(); SaveAll(); };
            cardA.AddHeaderControl(chkShowHidden);
            cardA.AddHeaderControl(HeaderBtn(new ModernButton("ncpa.cpl ↗", BtnKind.Ghost, delegate { OpenNcpa(); })));
            cardA.Controls.Add(BuildAdapterList());
            splitIp.Panel1.Padding = new Padding(S(6), S(6), S(6), S(6));
            splitIp.Panel1.Controls.Add(cardA);

            // --- details (current + change) ---
            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
            bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            bottom.Controls.Add(BuildCurrentCard(), 0, 0);
            bottom.Controls.Add(BuildChangeCard(), 1, 0);
            splitIp.Panel2.Controls.Add(bottom);

            // --- log ---
            var cardLog = new Card("執行紀錄", Theme.Card);
            cardLog.Padding = new Padding(S(18), S(46), S(20), S(12));
            txtLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                ForeColor = Theme.Slate,
                Font = Theme.UISmall
            };
            cardLog.Controls.Add(txtLog);
            var clr = new ModernButton("清除", BtnKind.Ghost, delegate { txtLog.Clear(); }) { MinimumSize = new Size(0, S(30)) };
            cardLog.AddHeaderControl(clr);
            root.Controls.Add(cardLog, 0, 1);
            return page;
        }

        ModernButton HeaderBtn(ModernButton b)
        {
            b.MinimumSize = new Size(0, S(34));
            b.Padding = new Padding(S(12), S(4), S(12), S(4));
            return b;
        }

        ListView BuildAdapterList()
        {
            lvAdapters = new ListView
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
            lvAdapters.SmallImageList = new ImageList { ImageSize = new Size(1, S(34)) };
            lvAdapters.ShowItemToolTips = true;   // full name / description on hover
            // description is shown in the 目前設定 subtitle and in the hover tooltip
            lvAdapters.Columns.Add("預設", S(46), HorizontalAlignment.Center);
            lvAdapters.Columns.Add("名稱", S(124));
            lvAdapters.Columns.Add("狀態", S(84));
            lvAdapters.Columns.Add("IP 位址", S(240));
            lvAdapters.Columns.Add("類型", S(86));
            lvAdapters.Columns.Add("MAC", S(164));
            lvAdapters.DrawColumnHeader += DrawAdapterHeader;
            lvAdapters.DrawItem += delegate { };
            lvAdapters.DrawSubItem += DrawAdapterCell;
            lvAdapters.SelectedIndexChanged += delegate { UpdateCurrentPanel(); };
            lvAdapters.MouseUp += delegate { lvAdapters.Invalidate(); };
            lvAdapters.Resize += delegate { FitAdapterColumns(); };

            var menu = new ContextMenuStrip { Font = Theme.UI };
            menu.Items.Add("★ 設為預設網卡", null, delegate { SetDefaultAdapter(); });
            menu.Items.Add("隱藏 / 取消隱藏", null, delegate { ToggleHideAdapter(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("改為 DHCP 自動取得", null, delegate { SetDhcp(); });
            menu.Items.Add("重新整理", null, delegate { RefreshAdapters(); });
            lvAdapters.ContextMenuStrip = menu;
            return lvAdapters;
        }

        // Let the IP column absorb the free width so there is no horizontal scrollbar.
        void FitAdapterColumns()
        {
            const int fill = 3;
            if (lvAdapters.Columns.Count <= fill) return;
            int others = 0;
            for (int i = 0; i < lvAdapters.Columns.Count; i++) if (i != fill) others += lvAdapters.Columns[i].Width;
            int w = lvAdapters.ClientSize.Width - others - SystemInformation.VerticalScrollBarWidth - 2;
            lvAdapters.Columns[fill].Width = Math.Max(S(120), w);
        }

        void DrawAdapterHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var b = new SolidBrush(Theme.Subtle)) g.FillRectangle(b, e.Bounds);
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            Rectangle r = e.Bounds;
            bool starCol = sender == lvAdapters && e.ColumnIndex == 0;
            if (!starCol) r.Inflate(-S(8), 0);
            TextFormatFlags align = starCol ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left;
            Theme.Draw(g, e.Header.Text, Theme.UIBold, r, Theme.Muted, align | TextFormatFlags.EndEllipsis);
        }

        void DrawAdapterCell(object sender, DrawListViewSubItemEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var a = e.Item.Tag as AdapterInfo;
            if (a == null) return;
            bool sel = e.Item.Selected;
            bool hidden = hiddenIds.Contains(a.Id);
            Rectangle cell = e.Bounds;
            if (e.ColumnIndex == 0) cell.Width = lvAdapters.Columns[0].Width;

            Color bg = sel ? Theme.IndigoSoft : (e.ItemIndex % 2 == 0 ? Color.White : Theme.Subtle);
            using (var b = new SolidBrush(bg)) g.FillRectangle(b, cell);
            if (sel && e.ColumnIndex == 0)
                Theme.FillRound(g, new RectangleF(cell.X, cell.Y + S(5), S(4), cell.Height - S(10)), S(2), Theme.Indigo, Theme.Violet);

            Color fg = hidden ? Color.FromArgb(160, 170, 186) : Theme.Text;
            Rectangle r = cell;
            r.Inflate(-S(8), 0);
            switch (e.ColumnIndex)
            {
                case 0:
                    if (a.Id == defaultId) Theme.Draw(g, "★", Theme.UILarge, cell, Theme.Amber, TextFormatFlags.HorizontalCenter);
                    break;
                case 1:
                    Theme.Draw(g, a.Name + (hidden ? "  (已隱藏)" : ""), Theme.UIBold, r, sel ? Theme.Indigo : fg, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                    break;
                case 2:
                {
                    bool up = a.Status == OperationalStatus.Up;
                    Color dc = up ? Theme.Green : Color.FromArgb(148, 163, 184);
                    float d = S(8), dy = r.Y + (r.Height - d) / 2f;
                    if (up) using (var hb = new SolidBrush(Color.FromArgb(60, dc))) g.FillEllipse(hb, r.X - 3, dy - 3, d + 6, d + 6);
                    using (var db = new SolidBrush(dc)) g.FillEllipse(db, r.X, dy, d, d);
                    Rectangle tr = new Rectangle(r.X + S(16), r.Y, r.Width - S(16), r.Height);
                    Theme.Draw(g, a.StatusText, Theme.UI, tr, up ? Theme.Emerald : Theme.Muted, TextFormatFlags.Left);
                    break;
                }
                case 3:
                    Theme.Draw(g, a.IpSummary, Theme.UI, r, hidden ? fg : Theme.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                    break;
                default:
                    Theme.Draw(g, e.SubItem.Text, Theme.UI, r, hidden ? fg : Theme.Muted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                    break;
            }
        }

        Card BuildCurrentCard()
        {
            var card = new Card("目前設定", Theme.Card);
            card.Padding = new Padding(S(18), S(46), S(20), S(8));
            curCard = card;
            btnRestore = new ModernButton("↶  還原", BtnKind.Secondary, delegate { RestoreSnapshot(); }) { MinimumSize = new Size(0, S(32)) };
            btnRestore.Padding = new Padding(S(12), S(2), S(12), S(2));
            card.AddHeaderControl(btnRestore);
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            lstIps = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = S(32),
                BackColor = Color.White,
                Margin = new Padding(0)
            };
            lstIps.DrawItem += DrawIpChip;
            lstIps.SelectedIndexChanged += delegate { lstIps.Invalidate(); LoadSelectedIpIntoEditor(); };

            info = new InfoGrid { Dock = DockStyle.Fill, Margin = new Padding(0, S(4), 0, S(2)) };

            var fl = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Margin = new Padding(0), WrapContents = false };
            var bDel = NetshBtn("✖  刪除選取 IP", BtnKind.Danger, delegate { DeleteSelectedIp(); });
            var bDhcp = NetshBtn("改為 DHCP", BtnKind.Secondary, delegate { SetDhcp(); });
            bDel.Padding = bDhcp.Padding = new Padding(S(12), S(4), S(12), S(4));
            bDel.MinimumSize = bDhcp.MinimumSize = new Size(0, S(34));
            fl.Controls.Add(bDel);
            fl.Controls.Add(bDhcp);

            t.Controls.Add(lstIps, 0, 0);
            t.Controls.Add(info, 0, 1);
            t.Controls.Add(fl, 0, 2);
            card.Controls.Add(t);
            return card;
        }

        void DrawIpChip(object sender, DrawItemEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(lstIps.BackColor)) g.FillRectangle(b, e.Bounds);
            if (e.Index < 0 || e.Index >= lstIps.Items.Count) return;
            var x = lstIps.Items[e.Index] as AddrInfo;
            if (x == null) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            var r = new RectangleF(e.Bounds.X + 1, e.Bounds.Y + S(3), e.Bounds.Width - S(4), e.Bounds.Height - S(6));
            if (sel) Theme.FillRound(g, r, S(10), Theme.Indigo, Theme.Violet);
            else Theme.FillRound(g, r, S(10), Theme.IndigoSoft, Color.FromArgb(240, 249, 255));

            string tag = e.Index == 0 ? "主要" : "額外 " + e.Index;
            Size ts = TextRenderer.MeasureText(tag, Theme.UISmall);
            var tr = new RectangleF(r.X + S(10), r.Y + (r.Height - S(22)) / 2f, ts.Width + S(14), S(22));
            using (GraphicsPath p = Theme.Round(tr, S(11)))
            using (var b = new SolidBrush(sel ? Color.FromArgb(60, 255, 255, 255) : Color.FromArgb(36, Theme.Indigo)))
                g.FillPath(b, p);
            Theme.Draw(g, tag, Theme.UISmall, Rectangle.Round(tr), sel ? Color.White : Theme.Indigo, TextFormatFlags.HorizontalCenter);

            string main = x.Ip + " / " + x.Prefix;
            int mx = (int)tr.Right + S(12);
            int mw = TextRenderer.MeasureText(main, Theme.UIBold).Width;
            Theme.Draw(g, main, Theme.UIBold, new Rectangle(mx, (int)r.Y, mw + S(4), (int)r.Height), sel ? Color.White : Theme.Text, TextFormatFlags.Left);
            // show the dotted mask only when it fits completely (never cut it off)
            int maskX = mx + mw + S(10), maskRoom = (int)r.Right - maskX - S(8);
            if (TextRenderer.MeasureText(x.Mask, Theme.UISmall).Width <= maskRoom)
                Theme.Draw(g, x.Mask, Theme.UISmall, new Rectangle(maskX, (int)r.Y, maskRoom, (int)r.Height),
                           sel ? Color.FromArgb(224, 231, 255) : Theme.Muted, TextFormatFlags.Left);
        }

        Card BuildChangeCard()
        {
            var card = new Card("變更 IP", Theme.Card) { Subtitle = "Gateway 自動帶入" };
            card.Padding = new Padding(S(18), S(46), S(20), S(8));
            var btnProfiles = new ModernButton("設定檔  ▼", BtnKind.Secondary, null) { MinimumSize = new Size(0, S(32)) };
            btnProfiles.Padding = new Padding(S(12), S(2), S(12), S(2));
            btnProfiles.Click += delegate { ShowProfileMenu(btnProfiles); };
            card.AddHeaderControl(btnProfiles);
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            int r = 0;

            fldIp = new InputField(true, 11f) { Height = S(34), Placeholder = "例：192.168.100.100/24" };
            foreach (string h in cfg.GetList("IpHistory", '|')) fldIp.History.Add(h);
            fldIp.Box.TextChanged += delegate { UpdateCalc(true); };
            AddRow(t, r++, "IP / 遮罩", fldIp, null);
            t.SetColumnSpan(fldIp, 2);

            lblCalc = new Label { AutoSize = false, Dock = DockStyle.Fill, Height = S(36), Font = Theme.UISmall, ForeColor = Theme.Muted, Margin = new Padding(S(6), 0, S(4), S(2)) };
            t.Controls.Add(lblCalc, 1, r);
            t.SetColumnSpan(lblCalc, 2);
            r++;

            fldGw = new InputField(false, 11f) { Height = S(34), Placeholder = "留空 = 不設定 Gateway" };
            AddRow(t, r++, "Gateway", fldGw, new ModernButton("自動帶入", BtnKind.Ghost, delegate { UpdateCalc(true); }) { MinimumSize = new Size(0, S(32)) });

            var dns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), AutoSize = true };
            dns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            dns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            fldDns1 = new InputField(false, 10.5f) { Height = S(34), Placeholder = "DNS 1 (可留空)", Dock = DockStyle.Fill };
            fldDns2 = new InputField(false, 10.5f) { Height = S(34), Placeholder = "DNS 2 (可留空)", Dock = DockStyle.Fill };
            fldDns1.Margin = new Padding(0, 0, S(4), 0);
            fldDns2.Margin = new Padding(S(4), 0, 0, 0);
            dns.Controls.Add(fldDns1, 0, 0);
            dns.Controls.Add(fldDns2, 1, 0);
            AddRow(t, r++, "DNS", dns, null);
            t.SetColumnSpan(dns, 2);
            dns.Margin = new Padding(S(4), S(3), S(4), S(3));

            var fl = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Margin = new Padding(0, S(4), 0, 0), WrapContents = false };
            ModernButton apply = NetshBtn("✔  套用 (設為主要 IP)", BtnKind.Primary, delegate { ApplyPrimary(); });
            apply.MinimumSize = new Size(0, S(38));
            fl.Controls.Add(apply);
            ModernButton add = NetshBtn("＋  新增為額外 IP", BtnKind.Secondary, delegate { AddSecondaryIp(); });
            add.MinimumSize = new Size(0, S(38));
            fl.Controls.Add(add);
            t.Controls.Add(fl, 0, r);
            t.SetColumnSpan(fl, 3);
            r++;

            var tip = new ToolTip { AutoPopDelay = 15000 };
            tip.SetToolTip(fldIp.Box, "格式：192.168.100.100/24、192.168.100.54/26 或 192.168.100.100 255.255.255.0\n未輸入遮罩時預設 /24\n右側 ▾ 可選擇歷史紀錄");
            tip.SetToolTip(fldGw.Box, "自動帶入網段最後一個可用位址 (/24 → .254、/26 → .62)，可手動修改");
            tip.SetToolTip(apply, "設定此網卡的 IP (會取代網卡目前所有 IP)");
            tip.SetToolTip(add, "保留原有 IP，再加第二、三組 IP (與 Windows「進階 TCP/IP 設定」相同)");

            t.RowCount = r + 1;
            for (int i = 0; i < r; i++) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            card.Controls.Add(t);
            return card;
        }

        void AddRow(TableLayoutPanel t, int row, string label, Control field, Control extra)
        {
            t.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                ForeColor = Theme.Muted,
                Font = Theme.UIBold,
                Margin = new Padding(0, S(4), S(10), S(4))
            }, 0, row);
            field.Dock = DockStyle.Fill;
            field.Margin = new Padding(S(4), S(3), S(4), S(3));
            t.Controls.Add(field, 1, row);
            if (extra != null) { extra.Anchor = AnchorStyles.Left; t.Controls.Add(extra, 2, row); }
        }

        // =====================================================================
        //  Page 1 logic
        // =====================================================================

        AdapterInfo SelectedAdapter()
        {
            return lvAdapters.SelectedItems.Count > 0 ? lvAdapters.SelectedItems[0].Tag as AdapterInfo : null;
        }

        void RefreshAdapters()
        {
            AdapterInfo sel = SelectedAdapter();
            string keepId = sel != null ? sel.Id : defaultId;

            adapters = AdapterInfo.GetAll()
                .OrderBy(a => a.Id == defaultId ? 0 : 1)
                .ThenBy(a => hiddenIds.Contains(a.Id) ? 1 : 0)
                .ThenBy(a => a.Status == OperationalStatus.Up ? 0 : 1)
                .ThenBy(a => a.Name)
                .ToList();

            lvAdapters.BeginUpdate();
            lvAdapters.Items.Clear();
            ListViewItem toSelect = null;
            foreach (AdapterInfo a in adapters)
            {
                bool hidden = hiddenIds.Contains(a.Id);
                if (hidden && !chkShowHidden.Checked) continue;
                var it = new ListViewItem(a.Id == defaultId ? "★" : "");
                it.SubItems.Add(a.Name);
                it.SubItems.Add(a.StatusText);
                it.SubItems.Add(a.IpSummary);
                it.SubItems.Add(a.TypeText);
                it.SubItems.Add(a.Mac);
                it.Tag = a;
                it.ToolTipText = a.Name + "\n" + a.Description + "\n" + a.IpSummary;
                lvAdapters.Items.Add(it);
                if (a.Id == keepId) toSelect = it;
            }
            if (toSelect == null && lvAdapters.Items.Count > 0) toSelect = lvAdapters.Items[0];
            if (toSelect != null) { toSelect.Selected = true; toSelect.Focused = true; toSelect.EnsureVisible(); }
            lvAdapters.EndUpdate();
            UpdateCurrentPanel();
        }

        void UpdateCurrentPanel()
        {
            AdapterInfo a = SelectedAdapter();
            lstIps.Items.Clear();
            if (a == null)
            {
                curCard.Subtitle = "請在上方選擇網卡";
                curCard.Invalidate();
                info.SetRows(null);
                UpdateRestoreButton();
                return;
            }
            curCard.Subtitle = (a.Id == defaultId ? "★ " : "") + a.Name;   // description: hover the adapter list
            curCard.Invalidate();
            foreach (AddrInfo x in a.Addrs) lstIps.Items.Add(x);
            bool up = a.Status == OperationalStatus.Up;
            info.SetRows(new List<InfoGrid.Row>
            {
                new InfoGrid.Row("狀態", a.StatusText + "  ·  " + (a.Dhcp ? "DHCP 自動取得" : "固定 IP") +
                                         (a.FromRegistry ? "  (未連線，顯示已儲存的設定)" : ""),
                                 up ? Theme.Green : Color.FromArgb(148, 163, 184)),
                new InfoGrid.Row("Gateway", a.Gateways.Count > 0 ? string.Join(", ", a.Gateways) : "(無)"),
                new InfoGrid.Row("DNS", a.Dns.Count > 0 ? string.Join(", ", a.Dns) : "(無)"),
                new InfoGrid.Row("MAC", (a.Mac.Length > 0 ? a.Mac : "—") + "     索引 " + a.Index)
            });
            lvAdapters.Invalidate();
            UpdateRestoreButton();
        }

        void LoadSelectedIpIntoEditor()
        {
            var x = lstIps.SelectedItem as AddrInfo;
            AdapterInfo a = SelectedAdapter();
            if (x == null || a == null) return;
            fldIp.Box.Text = x.Ip + "/" + x.Prefix;   // triggers auto gateway
            uint ip, gw;
            if (IpCalc.TryParseIPv4(x.Ip, out ip))
            {
                foreach (string g in a.Gateways)
                {
                    if (IpCalc.TryParseIPv4(g, out gw) && IpCalc.Network(gw, x.Prefix) == IpCalc.Network(ip, x.Prefix))
                    { fldGw.Box.Text = g; break; }
                }
            }
            fldDns1.Box.Text = a.Dns.Count > 0 ? a.Dns[0] : "";
            fldDns2.Box.Text = a.Dns.Count > 1 ? a.Dns[1] : "";
        }

        void UpdateCalc(bool setGateway)
        {
            uint ip; int prefix; string err;
            string text = fldIp.Box.Text;
            if (text.Trim().Length == 0)
            {
                lblCalc.ForeColor = Theme.Muted;
                lblCalc.Text = "192.168.100.100/24 → Gateway 192.168.100.254\n192.168.100.54/26  → Gateway 192.168.100.62";
                return;
            }
            if (!IpCalc.TryParseCidr(text, out ip, out prefix, out err))
            {
                lblCalc.ForeColor = Theme.Red;
                lblCalc.Text = "⚠ " + err;
                return;
            }
            uint mask = IpCalc.MaskFromPrefix(prefix);
            uint net = IpCalc.Network(ip, prefix);
            uint bc = IpCalc.Broadcast(ip, prefix);
            // at most two lines so the card never grows
            var sb = new StringBuilder();
            sb.Append("遮罩 " + IpCalc.ToStr(mask) + (IpCalc.HasExplicitMask(text) ? "" : " (預設 /24)") + "    廣播 " + IpCalc.ToStr(bc));
            bool bad = prefix <= 30 && (ip == net || ip == bc);
            if (bad) sb.Append("\n⚠ 此 IP 是網段位址或廣播位址，不能使用");
            else if (prefix <= 30)
                sb.Append("\n可用 " + IpCalc.ToStr(net + 1) + " ~ " + IpCalc.ToStr(bc - 1) + "  (" + (bc - net - 1) + " 個)");
            lblCalc.ForeColor = bad ? Theme.Red : Theme.Teal;
            lblCalc.Text = sb.ToString();
            if (setGateway) fldGw.Box.Text = IpCalc.SuggestGateway(ip, prefix);
        }

        void SetDefaultAdapter()
        {
            AdapterInfo a = SelectedAdapter();
            if (a == null) { Warn("請先選擇網卡。"); return; }
            defaultId = a.Id;
            hiddenIds.Remove(a.Id);
            Log("已將「" + a.Name + "」設為預設網卡。");
            RefreshAdapters();
            SaveAll();
        }

        void ToggleHideAdapter()
        {
            AdapterInfo a = SelectedAdapter();
            if (a == null) { Warn("請先選擇網卡。"); return; }
            if (hiddenIds.Contains(a.Id))
            {
                hiddenIds.Remove(a.Id);
                Log("取消隱藏「" + a.Name + "」。");
            }
            else
            {
                if (a.Id == defaultId) { Warn("預設網卡不能隱藏。"); return; }
                hiddenIds.Add(a.Id);
                Log("已隱藏「" + a.Name + "」(開啟「顯示已隱藏的網卡」可再顯示)。");
            }
            RefreshAdapters();
            SaveAll();
        }

        bool ReadEditor(out uint ip, out int prefix, out string gw, out string dns1, out string dns2)
        {
            string err;
            gw = fldGw.Box.Text.Trim();
            dns1 = fldDns1.Box.Text.Trim();
            dns2 = fldDns2.Box.Text.Trim();
            if (!IpCalc.TryParseCidr(fldIp.Box.Text, out ip, out prefix, out err)) { Warn(err); fldIp.Box.Focus(); return false; }
            uint net = IpCalc.Network(ip, prefix), bc = IpCalc.Broadcast(ip, prefix);
            if (prefix <= 30 && (ip == net || ip == bc)) { Warn("此 IP 是網段位址或廣播位址，不能使用。"); return false; }
            uint tmp;
            if (gw.Length > 0)
            {
                if (!IpCalc.TryParseIPv4(gw, out tmp)) { Warn("Gateway 格式錯誤。"); fldGw.Box.Focus(); return false; }
                if (tmp == ip) { Warn("Gateway 不可與 IP 相同。"); fldGw.Box.Focus(); return false; }
                if (IpCalc.Network(tmp, prefix) != net &&
                    !Ask("Gateway " + gw + " 不在 " + IpCalc.ToStr(net) + "/" + prefix + " 網段內，仍要繼續嗎？"))
                    return false;
            }
            if (dns1.Length > 0 && !IpCalc.TryParseIPv4(dns1, out tmp)) { Warn("DNS 1 格式錯誤。"); return false; }
            if (dns2.Length > 0 && !IpCalc.TryParseIPv4(dns2, out tmp)) { Warn("DNS 2 格式錯誤。"); return false; }
            if (dns1.Length == 0 && dns2.Length > 0) { dns1 = dns2; dns2 = ""; }
            return true;
        }

        void ApplyPrimary()
        {
            AdapterInfo a = SelectedAdapter();
            if (a == null) { Warn("請先選擇網卡。"); return; }
            uint ip; int prefix; string gw, dns1, dns2;
            if (!ReadEditor(out ip, out prefix, out gw, out dns1, out dns2)) return;
            if (!EnsureAdmin()) return;

            if (a.Addrs.Count > 1 &&
                !Ask("「" + a.Name + "」目前有 " + a.Addrs.Count + " 組 IP。\n套用後會取代為只有 " + IpCalc.ToStr(ip) + "/" + prefix + "。\n\n" +
                     "(若要保留原有 IP，請改用「新增為額外 IP」)\n\n確定要套用嗎？"))
                return;

            string idx = a.Index.ToString();
            string mask = IpCalc.ToStr(IpCalc.MaskFromPrefix(prefix));
            var cmds = new List<string>();
            cmds.Add("interface ipv4 set address name=" + idx + " source=static address=" + IpCalc.ToStr(ip) +
                     " mask=" + mask + " gateway=" + (gw.Length > 0 ? gw : "none"));
            if (dns1.Length > 0)
                cmds.Add("interface ipv4 set dnsservers name=" + idx + " source=static address=" + dns1 + " register=primary validate=no");
            if (dns2.Length > 0)
                cmds.Add("interface ipv4 add dnsservers name=" + idx + " address=" + dns2 + " index=2 validate=no");

            AddHistory(fldIp, fldIp.Box.Text.Trim());
            TakeSnapshot(a);
            RunNetshBatch("設定主要 IP：" + a.Name + " → " + IpCalc.ToStr(ip) + "/" + prefix +
                          (gw.Length > 0 ? "  GW " + gw : ""), cmds);
        }

        void AddSecondaryIp()
        {
            AdapterInfo a = SelectedAdapter();
            if (a == null) { Warn("請先選擇網卡。"); return; }
            uint ip; int prefix; string gw, dns1, dns2;
            if (!ReadEditor(out ip, out prefix, out gw, out dns1, out dns2)) return;
            if (a.Dhcp)
            {
                Warn("「" + a.Name + "」目前為 DHCP 自動取得，無法新增額外 IP。\n請先用「套用 (設為主要 IP)」設定固定 IP，再新增第二、三組 IP。");
                return;
            }
            string ipStr = IpCalc.ToStr(ip);
            if (a.Addrs.Any(x => x.Ip == ipStr)) { Warn(ipStr + " 已經存在於此網卡。"); return; }
            if (!EnsureAdmin()) return;

            string mask = IpCalc.ToStr(IpCalc.MaskFromPrefix(prefix));
            var cmds = new List<string>();
            cmds.Add("interface ipv4 add address name=" + a.Index + " address=" + ipStr + " mask=" + mask);
            AddHistory(fldIp, fldIp.Box.Text.Trim());
            TakeSnapshot(a);
            RunNetshBatch("新增額外 IP：" + a.Name + " ＋ " + ipStr + "/" + prefix, cmds);
        }

        void DeleteSelectedIp()
        {
            AdapterInfo a = SelectedAdapter();
            var x = lstIps.SelectedItem as AddrInfo;
            if (a == null || x == null) { Warn("請先在「目前設定」清單中選擇要刪除的 IP。"); return; }
            if (a.Dhcp) { Warn("DHCP 取得的 IP 無法刪除。"); return; }
            if (!Ask("確定要從「" + a.Name + "」刪除 " + x.Ip + "/" + x.Prefix + " 嗎？")) return;
            if (!EnsureAdmin()) return;
            TakeSnapshot(a);
            RunNetshBatch("刪除 IP：" + a.Name + " － " + x.Ip,
                new List<string> { "interface ipv4 delete address name=" + a.Index + " address=" + x.Ip });
        }

        void SetDhcp()
        {
            AdapterInfo a = SelectedAdapter();
            if (a == null) { Warn("請先選擇網卡。"); return; }
            if (!Ask("確定要將「" + a.Name + "」改為 DHCP 自動取得 IP 與 DNS 嗎？")) return;
            if (!EnsureAdmin()) return;
            TakeSnapshot(a);
            RunNetshBatch("改為 DHCP：" + a.Name, new List<string>
            {
                "interface ipv4 set address name=" + a.Index + " source=dhcp",
                "interface ipv4 set dnsservers name=" + a.Index + " source=dhcp"
            });
        }

        void RunNetshBatch(string title, List<string> cmds)
        {
            RunNetshBatch(title, cmds, null);
        }

        void RunNetshBatch(string title, List<string> cmds, Action onSuccess)
        {
            if (busy) return;
            SetBusy(true);
            Log("=== " + title + " ===");
            var th = new Thread(() =>
            {
                bool ok = true;
                string lastOutput = "";
                foreach (string c in cmds)
                {
                    Ui(() => Log("> netsh " + c));
                    int code;
                    string output;
                    try { output = Netsh.Run(c, out code); }
                    catch (Exception ex) { output = ex.Message; code = -1; }
                    if (output.Length > 0) { string o = output; Ui(() => Log(o)); }
                    if (code != 0)
                    {
                        ok = false;
                        lastOutput = output;
                        Ui(() => Log("(失敗，代碼 " + code + ")"));
                        break;
                    }
                }
                Ui(() => Log(ok ? "完成。" : "執行失敗，請查看上方訊息。"));
                Thread.Sleep(1500);   // give Windows a moment to apply the change
                Ui(() =>
                {
                    SetBusy(false);
                    if (ok && onSuccess != null) onSuccess();
                    RefreshAdapters();
                    if (!ok) MessageBox.Show(this, "執行失敗：\n\n" + lastOutput, "網路工具", MessageBoxButtons.OK, MessageBoxIcon.Error);
                });
            });
            th.IsBackground = true;
            th.Start();
        }

        void SetBusy(bool b)
        {
            busy = b;
            foreach (Button x in netshButtons) x.Enabled = !b;
            UseWaitCursor = b;
            UpdateRestoreButton();
        }

        void Log(string line)
        {
            string ts = DateTime.Now.ToString("HH:mm:ss   ");
            txtLog.AppendText(ts + line.Replace("\r\n", "\n").Replace("\n", Environment.NewLine + "               ") + Environment.NewLine);
        }

        void OnNetworkChanged(object sender, EventArgs e)
        {
            Ui(() => { netChangeTimer.Stop(); netChangeTimer.Start(); });
        }

        void OpenNcpa()
        {
            try { Process.Start(new ProcessStartInfo("control.exe", "ncpa.cpl") { UseShellExecute = true }); }
            catch (Exception ex) { Warn(ex.Message); }
        }

        // =====================================================================
        //  Page 2 UI
        // =====================================================================

        Panel BuildPingPage()
        {
            var page = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(S(10), S(10), S(10), S(8)) };
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, S(158)));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, S(66)));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, S(70)));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            page.Controls.Add(root);

            // --- target card ---
            var cardT = new Card("連線目標", Theme.Card) { Subtitle = "Ping 通後按「PuTTY 連線」，會自動停止 Ping 並開啟連線" };
            cardT.Padding = new Padding(S(18), S(44), S(20), S(8));
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var r0 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0), WrapContents = false };
            fldTarget = new InputField(true, 12f) { Width = S(270), Height = S(38), Placeholder = "輸入 IP，例如 192.168.1.1" };
            fldTarget.Margin = new Padding(0, S(3), S(8), S(3));
            foreach (string h in cfg.GetList("PingHistory", '|')) fldTarget.History.Add(h);
            fldTarget.Box.Text = cfg.Get("LastPing", "");
            fldTarget.Box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; TogglePing(); }
            };
            r0.Controls.Add(fldTarget);
            btnPing = new ModernButton("▶  Ping", BtnKind.Primary, delegate { TogglePing(); });
            btnPing.Font = new Font(Theme.UI.FontFamily, 10.5f, FontStyle.Bold);
            btnPing.MinimumSize = new Size(S(116), S(40));
            btnPing.Margin = new Padding(S(4), S(2), S(4), S(2));
            r0.Controls.Add(btnPing);
            var btnPutty = new ModernButton("PuTTY 連線  ›", BtnKind.Success, delegate { LaunchPutty(); });
            btnPutty.Font = new Font(Theme.UI.FontFamily, 10.5f, FontStyle.Bold);
            btnPutty.MinimumSize = new Size(S(140), S(40));
            btnPutty.Margin = new Padding(S(4), S(2), S(4), S(2));
            r0.Controls.Add(btnPutty);
            chkContinuous = new ToggleSwitch("持續 Ping (-t)") { Margin = new Padding(S(14), S(9), S(4), S(2)) };
            chkContinuous.Checked = cfg.GetBool("Continuous", true);
            r0.Controls.Add(chkContinuous);
            t.Controls.Add(r0, 0, 0);

            var r1 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, S(2), 0, 0), WrapContents = false };
            r1.Controls.Add(FlowLabel("協定"));
            segProto = new Segmented("SSH", "Telnet", "Serial");
            string savedProto = cfg.Get("PuttyProto", "SSH");
            segProto.SelectedIndex = savedProto == "Telnet" ? 1 : savedProto == "Serial" ? 2 : 0;
            r1.Controls.Add(segProto);
            lblPort = FlowLabel("埠號");
            r1.Controls.Add(lblPort);
            fldPort = new InputField(false, 10.5f) { Width = S(80), Height = S(36) };
            fldPort.Box.Text = cfg.Get("PuttyPort", "22");
            r1.Controls.Add(fldPort);
            lblUser = FlowLabel("使用者");
            r1.Controls.Add(lblUser);
            fldUser = new InputField(false, 10.5f) { Width = S(150), Height = S(36), Placeholder = "可空白" };
            fldUser.Box.Text = cfg.Get("PuttyUser", "");
            r1.Controls.Add(fldUser);
            BuildSerialFields(r1);
            segProto.SelectedIndexChanged += delegate
            {
                string p = fldPort.Box.Text.Trim();
                if (segProto.SelectedText == "Telnet" && (p == "22" || p == "")) fldPort.Box.Text = "23";
                else if (segProto.SelectedText == "SSH" && (p == "23" || p == "")) fldPort.Box.Text = "22";
                ApplyProtoUi();
            };
            ApplyProtoUi();
            r1.Controls.Add(new ModernButton("PuTTY 路徑…", BtnKind.Ghost, delegate { BrowsePutty(); }));
            t.Controls.Add(r1, 0, 1);
            cardT.Controls.Add(t);
            root.Controls.Add(cardT, 0, 0);

            // --- status banner ---
            banner = new StatusBanner { Dock = DockStyle.Fill };
            root.Controls.Add(banner, 0, 1);
            SetIdleBanner();

            // --- stat tiles ---
            var tiles = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0) };
            for (int i = 0; i < 4; i++) tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            tiles.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tileSent = new StatTile("已傳送", Theme.Indigo);
            tileRecv = new StatTile("已收到", Theme.Green);
            tileLoss = new StatTile("遺失率", Theme.Red);
            tileAvg = new StatTile("平均延遲", Theme.Teal);
            tiles.Controls.Add(tileSent, 0, 0);
            tiles.Controls.Add(tileRecv, 1, 0);
            tiles.Controls.Add(tileLoss, 2, 0);
            tiles.Controls.Add(tileAvg, 3, 0);
            root.Controls.Add(tiles, 0, 2);

            // --- terminal ---
            var cardO = new Card("輸出", Theme.Terminal) { Subtitle = "Ping 回應紀錄" };
            rtbPing = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Theme.Terminal,
                ForeColor = TermText,
                Font = new Font("Microsoft JhengHei UI", 10.5f),
                DetectUrls = false,
                WordWrap = false,
                BorderStyle = BorderStyle.None
            };
            rtbPing.HandleCreated += delegate { try { SetWindowTheme(rtbPing.Handle, "DarkMode_Explorer", null); } catch { } };
            cardO.Controls.Add(rtbPing);
            cardO.AddHeaderControl(new ModernButton("清除畫面", BtnKind.Ghost, delegate { rtbPing.Clear(); }) { MinimumSize = new Size(0, S(30)) });
            root.Controls.Add(cardO, 0, 3);
            return page;
        }

        Label FlowLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = Theme.Muted, Font = Theme.UIBold, Margin = new Padding(S(10), S(13), S(2), 0) };
        }

        // =====================================================================
        //  Page 2 logic
        // =====================================================================

        void SetIdleBanner()
        {
            banner.SetState("•", "尚未開始", "輸入 IP 後按「Ping」或按 Enter", Theme.Slate, false);
        }

        void TogglePing()
        {
            if (pingRunning) StopPing(true);
            else StartPing();
        }

        void StartPing()
        {
            string target = fldTarget.Box.Text.Trim();
            if (target.Length == 0) { Warn("請輸入要 Ping 的 IP。"); fldTarget.Box.Focus(); return; }
            AddHistory(fldTarget, target);
            pingTarget = target;
            pSent = pRecv = 0;
            rMin = long.MaxValue; rMax = 0; rSum = 0;
            pingRunning = true;
            int gen = ++pingGen;
            bool cont = chkContinuous.Checked;
            btnPing.Text = "■  停止";
            btnPing.Kind = BtnKind.Danger;
            banner.SetState("…", "Ping " + target + " 中", "等待回應…", Theme.Indigo, true);
            UpdatePingStats();
            AppendPing("", TermText);
            AppendPing("[" + DateTime.Now.ToString("HH:mm:ss") + "] Ping " + target + " (使用 32 位元組的資料)" + (cont ? "，持續執行中…" : ":"), Color.White);
            var th = new Thread(() => PingLoop(gen, target, cont)) { IsBackground = true };
            th.Start();
        }

        void PingLoop(int gen, string target, bool continuous)
        {
            byte[] buf = Encoding.ASCII.GetBytes("abcdefghijklmnopqrstuvwabcdefghi");
            int n = 0;
            using (var p = new Ping())
            {
                while (pingRunning && gen == pingGen && (continuous || n < 4))
                {
                    n++;
                    Stopwatch sw = Stopwatch.StartNew();
                    bool ok = false;
                    long rtt = 0;
                    int ttl = 0;
                    string line;
                    try
                    {
                        PingReply r = p.Send(target, 1000, buf, new PingOptions(128, false));
                        if (r.Status == IPStatus.Success)
                        {
                            ok = true;
                            rtt = r.RoundtripTime;
                            ttl = r.Options != null ? r.Options.Ttl : 0;
                            line = "回覆自 " + r.Address + ": 位元組=" + r.Buffer.Length +
                                   " 時間" + (rtt < 1 ? "<1ms" : "=" + rtt + "ms") + " TTL=" + ttl;
                        }
                        else line = PingStatusText(r.Status);
                    }
                    catch (PingException ex)
                    {
                        string msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                        Ui(() =>
                        {
                            if (gen != pingGen) return;
                            AppendPing("Ping 要求找不到主機 " + target + "。請檢查名稱並再試一次。(" + msg + ")", TermFail);
                            StopPing(false);
                            banner.SetState("✖", "找不到主機  " + target, "請檢查 IP 或名稱是否正確", FailColor, false);
                        });
                        return;
                    }
                    catch (Exception ex)
                    {
                        line = "Ping 失敗：" + ex.Message;
                    }
                    Ui(() => OnPingResult(gen, ok, rtt, ttl, line));
                    while (sw.ElapsedMilliseconds < 1000 && pingRunning && gen == pingGen) Thread.Sleep(50);
                }
            }
            if (!continuous) Ui(() => { if (gen == pingGen && pingRunning) StopPing(true); });
        }

        static string PingStatusText(IPStatus s)
        {
            switch (s)
            {
                case IPStatus.TimedOut: return "要求等候逾時。";
                case IPStatus.DestinationHostUnreachable: return "目的地主機無法連線。";
                case IPStatus.DestinationNetworkUnreachable: return "目的地網路無法連線。";
                case IPStatus.DestinationUnreachable: return "無法連線到目的地。";
                case IPStatus.TtlExpired: return "TTL 在傳輸時到期。";
                case IPStatus.BadRoute: return "路由錯誤。";
                default: return "Ping 失敗：" + s;
            }
        }

        void OnPingResult(int gen, bool ok, long rtt, int ttl, string line)
        {
            if (gen != pingGen) return;
            pSent++;
            if (ok)
            {
                pRecv++;
                rSum += rtt;
                rMin = Math.Min(rMin, rtt);
                rMax = Math.Max(rMax, rtt);
                banner.SetState("✔", "可連線   " + pingTarget,
                    "延遲 " + (rtt < 1 ? "<1" : rtt.ToString()) + " ms   ·   TTL " + ttl + "   ·   可按「PuTTY 連線」", OkColor, true);
            }
            else
            {
                banner.SetState("✖", "無回應   " + pingTarget, line, FailColor, true);
            }
            AppendPing(line, ok ? TermOk : TermFail);
            UpdatePingStats();
        }

        void StopPing(bool printStats)
        {
            if (!pingRunning) return;
            pingRunning = false;
            pingGen++;
            btnPing.Text = "▶  Ping";
            btnPing.Kind = BtnKind.Primary;
            btnPing.Invalidate();
            if (printStats && pSent > 0)
            {
                int lost = pSent - pRecv;
                AppendPing(pingTarget + " 的 Ping 統計資料:", Color.White);
                AppendPing("    封包: 已傳送 = " + pSent + "，已收到 = " + pRecv + "，已遺失 = " + lost +
                           " (" + (lost * 100 / pSent) + "% 遺失)", TermText);
                if (pRecv > 0)
                    AppendPing("    來回時間: 最小值 = " + rMin + "ms，最大值 = " + rMax + "ms，平均 = " + (rSum / pRecv) + "ms", TermText);
            }
            if (printStats)
                banner.SetState("■", "已停止   " + pingTarget, "成功 " + pRecv + " / " + pSent + " 次", Theme.Slate, false);
        }

        void UpdatePingStats()
        {
            if (pSent == 0)
            {
                tileSent.Set("0", ""); tileRecv.Set("0", ""); tileLoss.Set("—", ""); tileAvg.Set("—", "");
                return;
            }
            int lost = pSent - pRecv;
            tileSent.Set(pSent.ToString(), "");
            tileRecv.Set(pRecv.ToString(), "");
            tileLoss.Set((lost * 100 / pSent) + "%", "遺失 " + lost);
            if (pRecv > 0) tileAvg.Set((rSum / pRecv) + " ms", "最小 " + rMin + " · 最大 " + rMax);
            else tileAvg.Set("—", "");
        }

        void AppendPing(string text, Color color)
        {
            if (rtbPing.TextLength > 400000)
            {
                rtbPing.ReadOnly = false;
                rtbPing.Select(0, rtbPing.TextLength - 200000);
                rtbPing.SelectedText = "";
                rtbPing.ReadOnly = true;
            }
            rtbPing.SelectionStart = rtbPing.TextLength;
            rtbPing.SelectionLength = 0;
            rtbPing.SelectionColor = color;
            rtbPing.AppendText(text + Environment.NewLine);
            rtbPing.SelectionStart = rtbPing.TextLength;
            rtbPing.ScrollToCaret();
        }

        void LaunchPutty()
        {
            if (segProto.SelectedText == "Serial") { LaunchSerial(); return; }
            string host = fldTarget.Box.Text.Trim();
            if (host.Length == 0) { Warn("請先輸入 IP。"); fldTarget.Box.Focus(); return; }
            int port;
            if (!int.TryParse(fldPort.Box.Text.Trim(), out port) || port < 1 || port > 65535)
            { Warn("埠號需為 1 ~ 65535。"); fldPort.Box.Focus(); return; }

            bool wasPinging = pingRunning;
            StopPing(true);   // stop pinging first

            string exe = FindPutty();
            if (exe == null)
            {
                if (!Ask("找不到 putty.exe。\n是否手動選擇 PuTTY 的位置？")) return;
                exe = BrowsePutty();
                if (exe == null) return;
            }

            string proto = segProto.SelectedText;
            string args = (proto == "Telnet" ? "-telnet " : "-ssh ") + host + " -P " + port;
            string user = fldUser.Box.Text.Trim();
            if (user.Length > 0) args += " -l " + user;
            try
            {
                Process.Start(new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(exe)
                });
                AddHistory(fldTarget, host);
                AppendPing((wasPinging ? "已停止 Ping，" : "") + "啟動 PuTTY：putty " + args, TermInfo);
                banner.SetState("›", "已啟動 PuTTY  →  " + host, proto + "  ·  埠 " + port + (user.Length > 0 ? "  ·  使用者 " + user : ""), Theme.Sky, false);
                SaveAll();
            }
            catch (Exception ex)
            {
                Warn("無法啟動 PuTTY：" + ex.Message);
            }
        }

        string FindPutty()
        {
            string saved = cfg.Get("PuttyPath", "");
            if (saved.Length > 0 && File.Exists(saved)) return saved;

            var candidates = new List<string>();
            candidates.Add(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "putty.exe"));
            foreach (string d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                string dir = d.Trim().Trim('"');
                if (dir.Length == 0) continue;
                try { candidates.Add(Path.Combine(dir, "putty.exe")); } catch { }
            }
            foreach (string env in new[] { "ProgramW6432", "ProgramFiles", "ProgramFiles(x86)" })
            {
                string pf = Environment.GetEnvironmentVariable(env);
                if (!string.IsNullOrEmpty(pf)) candidates.Add(Path.Combine(pf, "PuTTY", "putty.exe"));
            }
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PuTTY", "putty.exe"));
            foreach (string c in candidates)
            {
                try { if (File.Exists(c)) return c; } catch { }
            }
            return null;
        }

        string BrowsePutty()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "選擇 putty.exe";
                dlg.Filter = "PuTTY (putty.exe)|putty.exe|執行檔 (*.exe)|*.exe";
                string cur = FindPutty();
                if (cur != null) dlg.InitialDirectory = Path.GetDirectoryName(cur);
                if (dlg.ShowDialog(this) != DialogResult.OK) return null;
                cfg.Set("PuttyPath", dlg.FileName);
                cfg.Save();
                MessageBox.Show(this, "已設定 PuTTY 路徑：\n" + dlg.FileName, "網路工具", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return dlg.FileName;
            }
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        ModernButton NetshBtn(string text, BtnKind kind, EventHandler onClick)
        {
            var b = new ModernButton(text, kind, onClick);
            netshButtons.Add(b);
            return b;
        }

        static void AddHistory(InputField f, string val)
        {
            if (val.Length == 0 || f.History == null) return;
            f.History.RemoveAll(x => string.Equals(x, val, StringComparison.OrdinalIgnoreCase));
            f.History.Insert(0, val);
            while (f.History.Count > 20) f.History.RemoveAt(f.History.Count - 1);
        }

        void Ui(Action a)
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); } catch { }
        }

        void Warn(string msg)
        {
            MessageBox.Show(this, msg, "網路工具", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        bool Ask(string msg)
        {
            return MessageBox.Show(this, msg, "網路工具", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        static bool IsAdministrator()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        bool EnsureAdmin()
        {
            if (isAdmin) return true;
            if (Ask("變更 IP 需要系統管理員權限。\n是否以系統管理員身分重新啟動本程式？"))
                RestartAsAdmin();
            return false;
        }

        void RestartAsAdmin()
        {
            if (isAdmin) return;
            SaveAll();
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true, Verb = "runas" });
                Close();
            }
            catch (Win32Exception) { /* UAC cancelled */ }
        }

        static Icon MakeIcon()
        {
            try
            {
                using (var bmp = new Bitmap(32, 32))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.Transparent);
                        var r = new RectangleF(1, 1, 30, 30);
                        using (GraphicsPath p = Theme.Round(r, 8))
                        using (var b = new LinearGradientBrush(r, Theme.Violet, Theme.HeaderC, 45f))
                            g.FillPath(b, p);
                        Theme.DrawGlobe(g, new RectangleF(7, 7, 18, 18), Color.White, 1.8f);
                    }
                    return Icon.FromHandle(bmp.GetHicon());
                }
            }
            catch { return null; }
        }
    }
}
