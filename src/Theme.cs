using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NetworkTools
{
    // =========================================================================
    //  Colours, fonts and drawing helpers
    // =========================================================================
    public static class Theme
    {
        public static float Scale = 1f;
        public static int S(int v) { return (int)Math.Round(v * Scale); }

        public static readonly Color Bg = Color.FromArgb(236, 240, 247);
        public static readonly Color Card = Color.White;
        public static readonly Color Border = Color.FromArgb(221, 227, 238);
        public static readonly Color Text = Color.FromArgb(30, 41, 59);
        public static readonly Color Muted = Color.FromArgb(100, 116, 139);
        public static readonly Color Subtle = Color.FromArgb(247, 249, 252);
        public static readonly Color Indigo = Color.FromArgb(79, 70, 229);
        public static readonly Color Violet = Color.FromArgb(124, 58, 237);
        public static readonly Color IndigoSoft = Color.FromArgb(238, 240, 255);
        public static readonly Color Teal = Color.FromArgb(13, 148, 136);
        public static readonly Color Emerald = Color.FromArgb(5, 150, 105);
        public static readonly Color Green = Color.FromArgb(16, 185, 129);
        public static readonly Color Red = Color.FromArgb(225, 29, 72);
        public static readonly Color Amber = Color.FromArgb(245, 158, 11);
        public static readonly Color Sky = Color.FromArgb(2, 132, 199);
        public static readonly Color Slate = Color.FromArgb(71, 85, 105);
        public static readonly Color HeaderA = Color.FromArgb(30, 27, 75);
        public static readonly Color HeaderB = Color.FromArgb(91, 33, 182);
        public static readonly Color HeaderC = Color.FromArgb(13, 116, 144);
        public static readonly Color Terminal = Color.FromArgb(15, 23, 42);

        public static Font UI, UIBold, UISmall, UILarge, Title, Stat, BannerTitle, BannerSub;

        public static void Init(float scale)
        {
            Scale = scale;
            const string f = "Microsoft JhengHei UI";
            UI = new Font(f, 9.75f);
            UIBold = new Font(f, 9.75f, FontStyle.Bold);
            UISmall = new Font(f, 9f);
            UILarge = new Font(f, 12f, FontStyle.Bold);
            Title = new Font("Segoe UI Semibold", 17f);
            Stat = new Font("Segoe UI Semibold", 15f);
            BannerTitle = new Font(f, 12.5f, FontStyle.Bold);
            BannerSub = new Font(f, 10f);
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static bool IsDark(Color c) { return (c.R * 299 + c.G * 587 + c.B * 114) / 1000 < 128; }

        public static Color ParentBack(Control c)
        {
            return c.Parent != null ? c.Parent.BackColor : Bg;
        }

        public static void FillRound(Graphics g, RectangleF r, float radius, Color a, Color b)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (GraphicsPath p = Round(r, radius))
            using (var br = new LinearGradientBrush(new RectangleF(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), a, b, LinearGradientMode.Horizontal))
                g.FillPath(br, p);
        }

        public static void DrawShadow(Graphics g, RectangleF r, float radius)
        {
            for (int i = 1; i <= 4; i++)
            {
                using (GraphicsPath p = Round(new RectangleF(r.X - i * 0.3f, r.Y + i, r.Width + i * 0.6f, r.Height), radius + i))
                using (var b = new SolidBrush(Color.FromArgb(9, 15, 23, 42)))
                    g.FillPath(b, p);
            }
        }

        public static void DrawGlobe(Graphics g, RectangleF r, Color c, float width)
        {
            using (var pen = new Pen(c, width))
            {
                g.DrawEllipse(pen, r);
                g.DrawEllipse(pen, r.X + r.Width * 0.28f, r.Y, r.Width * 0.44f, r.Height);
                g.DrawLine(pen, r.X, r.Y + r.Height / 2, r.Right, r.Y + r.Height / 2);
                g.DrawLine(pen, r.X + r.Width / 2, r.Y, r.X + r.Width / 2, r.Bottom);
            }
        }

        public static void Draw(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags extra)
        {
            TextRenderer.DrawText(g, s, f, r, c, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | extra);
        }

        public static void SetupDoubleBuffer(Control c)
        {
            typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                           .SetValue(c, true, null);
        }
    }

    // =========================================================================
    //  Rounded card with optional title, subtitle and header buttons
    // =========================================================================
    public class Card : Panel
    {
        public string Title, Subtitle;
        readonly List<Control> headerControls = new List<Control>();

        public Card(string title, Color fill)
        {
            Title = title;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = fill;
            ForeColor = Theme.IsDark(fill) ? Color.White : Theme.Text;
            int top = string.IsNullOrEmpty(title) ? Theme.S(16) : Theme.S(50);
            Padding = new Padding(Theme.S(18), top, Theme.S(20), Theme.S(20));
            Margin = new Padding(Theme.S(6));
            Dock = DockStyle.Fill;
        }

        public void AddHeaderControl(Control c)
        {
            headerControls.Add(c);
            Controls.Add(c);
            c.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            PerformLayout();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int x = Width - Theme.S(20);
            for (int i = headerControls.Count - 1; i >= 0; i--)
            {
                Control c = headerControls[i];
                Size sz = c.GetPreferredSize(Size.Empty);
                x -= sz.Width;
                c.SetBounds(x, Theme.S(28) - sz.Height / 2, sz.Width, sz.Height);
                x -= Theme.S(6);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.ParentBack(this));
            var r = new RectangleF(Theme.S(2), Theme.S(1), Width - Theme.S(5), Height - Theme.S(7));
            if (r.Width <= 0 || r.Height <= 0) return;
            float rad = Theme.S(14);
            Theme.DrawShadow(g, r, rad);
            using (GraphicsPath p = Theme.Round(r, rad))
            {
                using (var b = new SolidBrush(BackColor)) g.FillPath(b, p);
                if (!Theme.IsDark(BackColor))
                    using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, p);
            }
            if (!string.IsNullOrEmpty(Title))
            {
                var bar = new RectangleF(r.X + Theme.S(18), r.Y + Theme.S(17), Theme.S(4), Theme.S(18));
                using (GraphicsPath p = Theme.Round(bar, Theme.S(2)))
                using (var br = new LinearGradientBrush(new RectangleF(bar.X, bar.Y - 1, bar.Width, bar.Height + 2), Theme.Violet, Theme.Teal, LinearGradientMode.Vertical))
                    g.FillPath(br, p);
                int tx = (int)r.X + Theme.S(30);
                var tr = new Rectangle(tx, (int)r.Y + Theme.S(12), Width, Theme.S(28));
                Theme.Draw(g, Title, Theme.UILarge, tr, ForeColor, TextFormatFlags.Left);
                if (!string.IsNullOrEmpty(Subtitle))
                {
                    int w = TextRenderer.MeasureText(Title, Theme.UILarge).Width;
                    int right = Width - Theme.S(26);
                    foreach (Control c in headerControls) if (c.Visible) right = Math.Min(right, c.Left - Theme.S(8));
                    var sr = new Rectangle(tx + w + Theme.S(4), tr.Y + Theme.S(2), Math.Max(0, right - tx - w - Theme.S(4)), tr.Height);
                    Color sc = Theme.IsDark(BackColor) ? Color.FromArgb(148, 163, 184) : Theme.Muted;
                    Theme.Draw(g, Subtitle, Theme.UISmall, sr, sc, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                }
            }
        }
    }

    // =========================================================================
    //  Rounded gradient button
    // =========================================================================
    public enum BtnKind { Primary, Success, Danger, Secondary, Ghost }

    public class ModernButton : Button
    {
        public BtnKind Kind;
        bool hover, down;

        public ModernButton(string text, BtnKind kind, EventHandler onClick)
        {
            Text = text;
            Kind = kind;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = (kind == BtnKind.Primary || kind == BtnKind.Success || kind == BtnKind.Danger) ? Theme.UIBold : Theme.UI;
            AutoSize = true;
            Padding = new Padding(Theme.S(16), Theme.S(6), Theme.S(16), Theme.S(6));
            Margin = new Padding(Theme.S(4));
            MinimumSize = new Size(0, Theme.S(36));
            if (onClick != null) Click += onClick;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size t = TextRenderer.MeasureText(Text, Font);
            return new Size(Math.Max(MinimumSize.Width, t.Width + Padding.Horizontal),
                            Math.Max(MinimumSize.Height, t.Height + Padding.Vertical));
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color pb = Theme.ParentBack(this);
            g.Clear(pb);
            bool dark = Theme.IsDark(pb);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float rad = Theme.S(10);

            Color c1, c2, fg;
            Color border = Color.Empty;
            switch (Kind)
            {
                case BtnKind.Primary: c1 = Theme.Indigo; c2 = Theme.Violet; fg = Color.White; break;
                case BtnKind.Success: c1 = Theme.Teal; c2 = Theme.Emerald; fg = Color.White; break;
                case BtnKind.Danger: c1 = Color.FromArgb(244, 63, 94); c2 = Theme.Red; fg = Color.White; break;
                case BtnKind.Secondary:
                    c1 = c2 = dark ? Theme.Mix(pb, Color.White, 0.08f) : Color.White;
                    fg = dark ? Color.White : Theme.Text;
                    border = dark ? Theme.Mix(pb, Color.White, 0.2f) : Theme.Border;
                    break;
                default: // Ghost
                    c1 = c2 = pb;
                    fg = dark ? Color.FromArgb(203, 213, 225) : Theme.Indigo;
                    break;
            }
            if (hover && Enabled)
            {
                if (Kind == BtnKind.Ghost) c1 = c2 = dark ? Theme.Mix(pb, Color.White, 0.12f) : Theme.IndigoSoft;
                else if (Kind == BtnKind.Secondary) { border = Theme.Indigo; fg = dark ? Color.White : Theme.Indigo; }
                else { c1 = Theme.Mix(c1, Color.White, 0.12f); c2 = Theme.Mix(c2, Color.White, 0.12f); }
            }
            if (down && Enabled) { c1 = Theme.Mix(c1, Color.Black, 0.12f); c2 = Theme.Mix(c2, Color.Black, 0.12f); }
            if (!Enabled) { c1 = Theme.Mix(c1, pb, 0.55f); c2 = Theme.Mix(c2, pb, 0.55f); fg = Theme.Mix(fg, pb, 0.5f); }

            if ((Kind == BtnKind.Primary || Kind == BtnKind.Success || Kind == BtnKind.Danger) && Enabled && !down)
            {
                using (GraphicsPath sp = Theme.Round(new RectangleF(r.X + Theme.S(2), r.Y + Theme.S(3), r.Width - Theme.S(4), r.Height - Theme.S(2)), rad))
                using (var sb = new SolidBrush(Color.FromArgb(40, c2)))
                    g.FillPath(sb, sp);
                r = new RectangleF(r.X, r.Y, r.Width, r.Height - Theme.S(2));
            }
            Theme.FillRound(g, r, rad, c1, c2);
            if (border != Color.Empty)
                using (GraphicsPath p = Theme.Round(r, rad))
                using (var pen = new Pen(border)) g.DrawPath(pen, p);
            Theme.Draw(g, Text, Font, Rectangle.Round(r), fg, TextFormatFlags.HorizontalCenter);
        }
    }

    // =========================================================================
    //  iOS-style toggle switch
    // =========================================================================
    public class ToggleSwitch : CheckBox
    {
        public ToggleSwitch(string text)
        {
            Text = text;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = true;
            Cursor = Cursors.Hand;
            Font = Theme.UI;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size t = TextRenderer.MeasureText(Text, Font);
            return new Size(Theme.S(48) + t.Width, Math.Max(Theme.S(26), t.Height));
        }

        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color pb = Theme.ParentBack(this);
            g.Clear(pb);
            int tw = Theme.S(40), th = Theme.S(22);
            var tr = new RectangleF(1, (Height - th) / 2f, tw, th);
            if (Checked) Theme.FillRound(g, tr, th / 2f, Theme.Indigo, Theme.Violet);
            else Theme.FillRound(g, tr, th / 2f, Color.FromArgb(203, 213, 225), Color.FromArgb(203, 213, 225));
            float k = th - Theme.S(6);
            float kx = Checked ? tr.Right - k - Theme.S(3) : tr.X + Theme.S(3);
            using (var sb = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillEllipse(sb, kx, tr.Y + Theme.S(4), k, k);
            g.FillEllipse(Brushes.White, kx, tr.Y + Theme.S(3), k, k);
            Theme.Draw(g, Text, Font, new Rectangle(tw + Theme.S(8), 0, Width - tw - Theme.S(8), Height),
                       Theme.IsDark(pb) ? Color.White : Theme.Text, TextFormatFlags.Left);
        }
    }

    // =========================================================================
    //  Segmented selector (e.g. SSH | Telnet)
    // =========================================================================
    public class Segmented : Control
    {
        readonly string[] items;
        int sel;
        public event EventHandler SelectedIndexChanged;

        public Segmented(params string[] items)
        {
            this.items = items;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Theme.UIBold;
            Cursor = Cursors.Hand;
            Size = new Size(Theme.S(78) * items.Length, Theme.S(36));
            Margin = new Padding(Theme.S(4));
        }

        public int SelectedIndex
        {
            get { return sel; }
            set
            {
                if (value < 0 || value >= items.Length || value == sel) return;
                sel = value;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        public string SelectedText { get { return items[sel]; } }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int w = (Width - Theme.S(6)) / items.Length;
            SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, (e.X - Theme.S(3)) / Math.Max(1, w)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.ParentBack(this));
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRound(g, r, Theme.S(10), Color.FromArgb(241, 245, 249), Color.FromArgb(241, 245, 249));
            using (GraphicsPath p = Theme.Round(r, Theme.S(10)))
            using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, p);
            float w = (Width - Theme.S(6)) / (float)items.Length;
            for (int i = 0; i < items.Length; i++)
            {
                var sr = new RectangleF(Theme.S(3) + i * w, Theme.S(3), w, Height - Theme.S(7));
                if (i == sel) Theme.FillRound(g, sr, Theme.S(8), Theme.Indigo, Theme.Violet);
                Theme.Draw(g, items[i], Font, Rectangle.Round(sr), i == sel ? Color.White : Theme.Muted, TextFormatFlags.HorizontalCenter);
            }
        }
    }

    // =========================================================================
    //  Rounded text field with optional history dropdown and placeholder
    // =========================================================================
    public class InputField : Panel
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        public readonly TextBox Box;
        public readonly List<string> History;
        /// <summary>When set, the ▾ menu shows these {display, value} pairs instead of the history.</summary>
        public Func<List<string[]>> Choices;
        public event EventHandler Picked;
        string placeholder = "";
        bool focused, dropHover;
        Rectangle dropRect;

        public InputField(bool withHistory, float fontSize)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            Box = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Font = new Font(Theme.UI.FontFamily, fontSize),
                BackColor = Color.White,
                ForeColor = Theme.Text
            };
            Controls.Add(Box);
            Box.GotFocus += delegate { focused = true; Invalidate(); };
            Box.LostFocus += delegate { focused = false; Invalidate(); };
            Box.HandleCreated += delegate { ApplyPlaceholder(); };
            if (withHistory) History = new List<string>();
            Height = Theme.S(38);
            Margin = new Padding(Theme.S(4));
        }

        public string Placeholder
        {
            get { return placeholder; }
            set { placeholder = value ?? ""; ApplyPlaceholder(); }
        }

        void ApplyPlaceholder()
        {
            if (Box.IsHandleCreated) SendMessage(Box.Handle, 0x1501, (IntPtr)1, placeholder); // EM_SETCUEBANNER
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int dropW = History != null ? Theme.S(32) : 0;
            int px = Theme.S(12);
            Box.SetBounds(px, (Height - Box.Height) / 2, Math.Max(10, Width - px * 2 - dropW), Box.Height);
            dropRect = new Rectangle(Width - dropW - Theme.S(2), 0, dropW, Height);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool h = History != null && dropRect.Contains(e.Location);
            Cursor = h ? Cursors.Hand : Cursors.IBeam;
            if (h != dropHover) { dropHover = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); dropHover = false; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (History != null && dropRect.Contains(e.Location)) ShowHistory();
            else Box.Focus();
        }

        void ShowHistory()
        {
            var menu = new ContextMenuStrip { Font = Theme.UI, ShowImageMargin = false };
            if (Choices != null)
            {
                List<string[]> items = Choices();
                if (items.Count == 0) menu.Items.Add("(沒有可用的項目)").Enabled = false;
                foreach (string[] it in items)
                {
                    string v = it[1];
                    menu.Items.Add("  " + it[0] + "  ", null, delegate
                    {
                        Box.Text = v;
                        Box.Focus();
                        Box.SelectionStart = Box.TextLength;
                        if (Picked != null) Picked(this, EventArgs.Empty);
                    });
                }
            }
            else if (History.Count == 0)
            {
                menu.Items.Add("(尚無歷史紀錄)").Enabled = false;
            }
            else
            {
                foreach (string h in History)
                {
                    string v = h;
                    menu.Items.Add("  " + v + "  ", null, delegate
                    {
                        Box.Text = v;
                        Box.Focus();
                        Box.SelectionStart = Box.TextLength;
                    });
                }
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("清除歷史紀錄", null, delegate { History.Clear(); });
            }
            menu.MinimumSize = new Size(Width, 0);
            menu.Show(this, new Point(0, Height));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.ParentBack(this));
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            if (focused)
            {
                using (GraphicsPath glow = Theme.Round(r, Theme.S(10)))
                using (var gp = new Pen(Color.FromArgb(60, Theme.Indigo), Theme.S(4)))
                    g.DrawPath(gp, glow);
            }
            using (GraphicsPath p = Theme.Round(r, Theme.S(10)))
            {
                g.FillPath(Brushes.White, p);
                using (var pen = new Pen(focused ? Theme.Indigo : Theme.Border, focused ? 1.6f : 1f)) g.DrawPath(pen, p);
            }
            if (History != null)
            {
                if (dropHover)
                    using (GraphicsPath hp = Theme.Round(new RectangleF(dropRect.X + 2, Theme.S(5), dropRect.Width - Theme.S(5), Height - Theme.S(10)), Theme.S(7)))
                    using (var hb = new SolidBrush(Theme.IndigoSoft)) g.FillPath(hb, hp);
                float cx = dropRect.X + dropRect.Width / 2f - 1, cy = Height / 2f;
                float a = Theme.S(4);
                using (var pen = new Pen(dropHover ? Theme.Indigo : Theme.Muted, 1.8f))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    g.DrawLines(pen, new[] { new PointF(cx - a, cy - a / 2), new PointF(cx, cy + a / 2), new PointF(cx + a, cy - a / 2) });
                }
            }
        }
    }

    // =========================================================================
    //  Key / value list (current adapter info)
    // =========================================================================
    public class InfoGrid : Control
    {
        public class Row
        {
            public string Key, Value;
            public Color Dot;
            public Row(string k, string v) { Key = k; Value = v; Dot = Color.Empty; }
            public Row(string k, string v, Color dot) { Key = k; Value = v; Dot = dot; }
        }

        List<Row> rows = new List<Row>();

        public InfoGrid()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Theme.UI;
        }

        int RowH { get { return Theme.S(25); } }

        public void SetRows(List<Row> r)
        {
            rows = r ?? new List<Row>();
            Height = Math.Max(1, rows.Count * RowH);
            if (Parent != null) Parent.PerformLayout();
            Invalidate();
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            return new Size(Math.Max(proposedSize.Width, Theme.S(100)), Math.Max(1, rows.Count * RowH));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            int keyW = Theme.S(82);
            for (int i = 0; i < rows.Count; i++)
            {
                int y = i * RowH;
                Row row = rows[i];
                Theme.Draw(g, row.Key, Theme.UI, new Rectangle(0, y, keyW, RowH), Theme.Muted, TextFormatFlags.Left);
                int vx = keyW;
                if (row.Dot != Color.Empty)
                {
                    int d = Theme.S(9);
                    using (var b = new SolidBrush(Color.FromArgb(50, row.Dot))) g.FillEllipse(b, vx - 2, y + (RowH - d) / 2f - 2, d + 4, d + 4);
                    using (var b = new SolidBrush(row.Dot)) g.FillEllipse(b, vx, y + (RowH - d) / 2f, d, d);
                    vx += d + Theme.S(8);
                }
                Theme.Draw(g, row.Value, Theme.UI, new Rectangle(vx, y, Width - vx, RowH), Theme.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            }
        }
    }

    // =========================================================================
    //  Big coloured ping status banner with pulse animation
    // =========================================================================
    public class StatusBanner : Control
    {
        string glyph = "•", title = "", sub = "";
        Color color = Theme.Slate;
        /// <summary>0..1 draws a progress line along the bottom; negative hides it.</summary>
        public float Progress = -1;
        float phase;
        readonly Timer timer;

        public StatusBanner()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            timer = new Timer { Interval = 40 };
            timer.Tick += delegate { phase += 0.035f; if (phase > 1f) phase = 0f; Invalidate(); };
            Margin = new Padding(Theme.S(6));
        }

        public void SetState(string glyph, string title, string sub, Color color, bool pulse)
        {
            this.glyph = glyph; this.title = title; this.sub = sub; this.color = color;
            if (!pulse) phase = 0;
            timer.Enabled = pulse;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.ParentBack(this));
            var r = new RectangleF(Theme.S(2), Theme.S(1), Width - Theme.S(5), Height - Theme.S(7));
            if (r.Width <= 0 || r.Height <= 0) return;
            float rad = Theme.S(16);
            Theme.DrawShadow(g, r, rad);
            Theme.FillRound(g, r, rad, color, Theme.Mix(color, Color.White, 0.28f));

            // decorative rings
            var clip = g.Clip;
            using (GraphicsPath p = Theme.Round(r, rad)) g.SetClip(p);
            using (var pen = new Pen(Color.FromArgb(28, 255, 255, 255), Theme.S(14)))
            {
                g.DrawEllipse(pen, r.Right - Theme.S(130), r.Y - Theme.S(60), Theme.S(170), Theme.S(170));
                g.DrawEllipse(pen, r.Right - Theme.S(260), r.Bottom - Theme.S(30), Theme.S(120), Theme.S(120));
            }
            if (Progress >= 0)
            {
                float ph = Theme.S(4);
                using (var tb = new SolidBrush(Color.FromArgb(45, 255, 255, 255))) g.FillRectangle(tb, r.X, r.Bottom - ph, r.Width, ph);
                using (var pb = new SolidBrush(Color.FromArgb(220, 255, 255, 255))) g.FillRectangle(pb, r.X, r.Bottom - ph, r.Width * Math.Min(1f, Progress), ph);
            }
            g.Clip = clip;

            // icon with pulse ring
            float rr = Math.Min(Theme.S(21), r.Height / 2 - Theme.S(6));
            float cx = r.X + Theme.S(16) + rr, cy = r.Y + r.Height / 2;
            if (phase > 0)
            {
                float pr = rr + phase * Theme.S(5);
                using (var pen = new Pen(Color.FromArgb((int)(150 * (1 - phase)), 255, 255, 255), 2f))
                    g.DrawEllipse(pen, cx - pr, cy - pr, pr * 2, pr * 2);
            }
            using (var b = new SolidBrush(Color.FromArgb(55, 255, 255, 255))) g.FillEllipse(b, cx - rr, cy - rr, rr * 2, rr * 2);
            Theme.Draw(g, glyph, Theme.BannerTitle, new Rectangle((int)(cx - rr), (int)(cy - rr), (int)(rr * 2), (int)(rr * 2)), Color.White, TextFormatFlags.HorizontalCenter);

            // title and subtitle on one line (compact banner)
            int tx = (int)(cx + rr + Theme.S(14));
            int tw = (int)r.Right - tx - Theme.S(12);
            var line = new Rectangle(tx, (int)r.Y, tw, (int)r.Height);
            int titleW = Math.Min(tw, TextRenderer.MeasureText(g, title, Theme.BannerTitle, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + Theme.S(4));
            Theme.Draw(g, title, Theme.BannerTitle, new Rectangle(tx, line.Y, titleW, line.Height), Color.White, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            if (!string.IsNullOrEmpty(sub))
            {
                int sx = tx + titleW + Theme.S(18);
                int sw = (int)r.Right - sx - Theme.S(12);
                if (sw > Theme.S(30))
                {
                    using (var sep = new Pen(Color.FromArgb(110, 255, 255, 255), 1f))
                        g.DrawLine(sep, sx - Theme.S(9), cy - Theme.S(9), sx - Theme.S(9), cy + Theme.S(9));
                    Theme.Draw(g, sub, Theme.BannerSub, new Rectangle(sx, line.Y, sw, line.Height), Theme.Mix(color, Color.White, 0.85f), TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                }
            }
        }
    }

    // =========================================================================
    //  Statistic tile
    // =========================================================================
    public class StatTile : Control
    {
        public string Caption;
        public Color Accent;
        string value = "—", note = "";

        public StatTile(string caption, Color accent)
        {
            Caption = caption;
            Accent = accent;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Margin = new Padding(Theme.S(6));
            Dock = DockStyle.Fill;
        }

        public void Set(string value, string note)
        {
            this.value = value; this.note = note ?? "";
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.ParentBack(this));
            var r = new RectangleF(Theme.S(2), Theme.S(1), Width - Theme.S(5), Height - Theme.S(7));
            if (r.Width <= 0 || r.Height <= 0) return;
            float rad = Theme.S(14);
            Theme.DrawShadow(g, r, rad);
            using (GraphicsPath p = Theme.Round(r, rad))
            {
                g.FillPath(Brushes.White, p);
                using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, p);
            }
            // compact horizontal layout: caption (+ note) on the left, big value on the right
            var bar = new RectangleF(r.X + Theme.S(12), r.Y + Theme.S(10), Theme.S(4), r.Height - Theme.S(20));
            Theme.FillRound(g, bar, Theme.S(2), Accent, Accent);
            int tx = (int)r.X + Theme.S(24);
            int right = (int)r.Right - Theme.S(14);
            const TextFormatFlags measure = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            int vw = TextRenderer.MeasureText(g, value, Theme.Stat, Size.Empty, measure).Width + Theme.S(4);
            vw = Math.Min(vw, Math.Max(0, right - tx - Theme.S(50)));
            Theme.Draw(g, value, Theme.Stat, new Rectangle(right - vw, (int)r.Y, vw, (int)r.Height), Theme.Text, TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            int lw = right - vw - Theme.S(8) - tx;
            if (note.Length > 0)
            {
                int mid = (int)(r.Y + r.Height / 2);
                Theme.Draw(g, Caption, Theme.UISmall, new Rectangle(tx, mid - Theme.S(20), lw, Theme.S(20)), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                Theme.Draw(g, note, Theme.UISmall, new Rectangle(tx, mid + Theme.S(1), lw, Theme.S(20)), Theme.Slate, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
            else
                Theme.Draw(g, Caption, Theme.UISmall, new Rectangle(tx, (int)r.Y, lw, (int)r.Height), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    // =========================================================================
    //  Gradient header with constellation art, navigation pills and admin badge
    // =========================================================================
    public class HeaderPanel : Control
    {
        readonly string[] tabs;
        readonly bool admin;
        readonly PointF[] stars;
        readonly Rectangle[] tabRects;
        Rectangle badgeRect;
        int hover = -2;
        public int Active;
        public event EventHandler TabChanged, AdminClicked;

        public HeaderPanel(string[] tabs, bool admin)
        {
            this.tabs = tabs;
            this.admin = admin;
            tabRects = new Rectangle[tabs.Length];
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Top;
            Height = Theme.S(96);
            var rnd = new Random(1119);
            stars = new PointF[38];
            for (int i = 0; i < stars.Length; i++)
                stars[i] = new PointF((float)(0.28 + rnd.NextDouble() * 0.74), (float)(rnd.NextDouble() * 1.1 - 0.05));
        }

        string BadgeText { get { return admin ? "系統管理員" : "一般權限 · 點此提升"; } }

        void LayoutItems()
        {
            Size bs = TextRenderer.MeasureText(BadgeText, Theme.UISmall);
            int bw = bs.Width + Theme.S(34);
            badgeRect = new Rectangle(Width - Theme.S(22) - bw, (Height - Theme.S(30)) / 2, bw, Theme.S(30));
            int h = Theme.S(40), y = (Height - h) / 2;
            int x = badgeRect.Left - Theme.S(22);
            for (int i = tabs.Length - 1; i >= 0; i--)
            {
                int w = TextRenderer.MeasureText(tabs[i], Theme.UIBold).Width + Theme.S(32);
                x -= w;
                tabRects[i] = new Rectangle(x, y, w, h);
                x -= Theme.S(6);
            }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutItems(); }

        int HitTest(Point p)
        {
            for (int i = 0; i < tabRects.Length; i++) if (tabRects[i].Contains(p)) return i;
            if (badgeRect.Contains(p)) return -1;
            return -2;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = HitTest(e.Location);
            Cursor = (h >= 0 || (h == -1 && !admin)) ? Cursors.Hand : Cursors.Default;
            if (h != hover) { hover = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -2; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int h = HitTest(e.Location);
            if (h >= 0 && h != Active)
            {
                Active = h;
                Invalidate();
                if (TabChanged != null) TabChanged(this, EventArgs.Empty);
            }
            else if (h == -1 && !admin && AdminClicked != null) AdminClicked(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle rc = ClientRectangle;
            if (rc.Width <= 0 || rc.Height <= 0) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            LayoutItems();

            using (var lg = new LinearGradientBrush(rc, Theme.HeaderA, Theme.HeaderC, 0f))
            {
                var cb = new ColorBlend(3);
                cb.Colors = new[] { Theme.HeaderA, Theme.HeaderB, Theme.HeaderC };
                cb.Positions = new[] { 0f, 0.55f, 1f };
                lg.InterpolationColors = cb;
                g.FillRectangle(lg, rc);
            }

            // soft orbs
            using (var b = new SolidBrush(Color.FromArgb(16, 255, 255, 255)))
            {
                g.FillEllipse(b, rc.Width * 0.58f, -Theme.S(90), Theme.S(240), Theme.S(240));
                g.FillEllipse(b, rc.Width * 0.86f, Theme.S(24), Theme.S(170), Theme.S(170));
                g.FillEllipse(b, -Theme.S(50), Theme.S(40), Theme.S(150), Theme.S(150));
            }

            // constellation / network art
            PointF[] pts = stars.Select(p => new PointF(p.X * rc.Width, p.Y * rc.Height)).ToArray();
            float maxD = Theme.S(120);
            for (int i = 0; i < pts.Length; i++)
            {
                for (int j = i + 1; j < pts.Length; j++)
                {
                    float dx = pts[i].X - pts[j].X, dy = pts[i].Y - pts[j].Y;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (d >= maxD) continue;
                    using (var pen = new Pen(Color.FromArgb((int)(75 * (1 - d / maxD)), 255, 255, 255), 1f))
                        g.DrawLine(pen, pts[i], pts[j]);
                }
            }
            for (int i = 0; i < pts.Length; i++)
            {
                float s = i % 4 == 0 ? Theme.S(3) : Theme.S(2);
                using (var b = new SolidBrush(Color.FromArgb(i % 4 == 0 ? 200 : 140, 255, 255, 255)))
                    g.FillEllipse(b, pts[i].X - s, pts[i].Y - s, s * 2, s * 2);
            }
            using (var p = new Pen(Color.FromArgb(50, 255, 255, 255))) g.DrawLine(p, 0, rc.Height - 1, rc.Width, rc.Height - 1);

            // logo
            var logo = new Rectangle(Theme.S(24), (Height - Theme.S(50)) / 2, Theme.S(50), Theme.S(50));
            using (GraphicsPath lp = Theme.Round(logo, Theme.S(15)))
            {
                using (var b = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) g.FillPath(b, lp);
                using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255))) g.DrawPath(pen, lp);
            }
            Theme.DrawGlobe(g, new RectangleF(logo.X + Theme.S(12), logo.Y + Theme.S(12), logo.Width - Theme.S(24), logo.Height - Theme.S(24)), Color.White, 1.8f * Theme.Scale);

            // title
            int tx = logo.Right + Theme.S(16);
            int titleRight = tabRects.Length > 0 ? tabRects[0].Left - Theme.S(10) : rc.Width;
            Theme.Draw(g, "Network Tools", Theme.Title, new Rectangle(tx, logo.Top - Theme.S(6), titleRight - tx, Theme.S(36)), Color.White, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            Theme.Draw(g, "網路工具箱", Theme.UISmall, new Rectangle(tx + Theme.S(2), logo.Top + Theme.S(28), titleRight - tx, Theme.S(22)), Color.FromArgb(199, 210, 254), TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

            // nav pills
            for (int i = 0; i < tabs.Length; i++)
            {
                Rectangle t = tabRects[i];
                bool act = i == Active;
                using (GraphicsPath p = Theme.Round(t, t.Height / 2f))
                {
                    if (act)
                    {
                        using (GraphicsPath sp = Theme.Round(new RectangleF(t.X, t.Y + Theme.S(3), t.Width, t.Height), t.Height / 2f))
                        using (var sb = new SolidBrush(Color.FromArgb(50, 0, 0, 0))) g.FillPath(sb, sp);
                        g.FillPath(Brushes.White, p);
                    }
                    else
                    {
                        using (var b = new SolidBrush(Color.FromArgb(hover == i ? 80 : 46, 255, 255, 255))) g.FillPath(b, p);
                        using (var pen = new Pen(Color.FromArgb(60, 255, 255, 255))) g.DrawPath(pen, p);
                    }
                }
                Theme.Draw(g, tabs[i], Theme.UIBold, t, act ? Theme.HeaderB : Color.White, TextFormatFlags.HorizontalCenter);
            }

            // admin badge
            using (GraphicsPath p = Theme.Round(badgeRect, badgeRect.Height / 2f))
            {
                using (var b = new SolidBrush(Color.FromArgb(!admin && hover == -1 ? 70 : 38, 0, 0, 0))) g.FillPath(b, p);
                using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255))) g.DrawPath(pen, p);
            }
            Color dot = admin ? Color.FromArgb(52, 211, 153) : Theme.Amber;
            float dd = Theme.S(8), dx0 = badgeRect.X + Theme.S(13), dy0 = badgeRect.Y + (badgeRect.Height - dd) / 2f;
            using (var b = new SolidBrush(Color.FromArgb(70, dot))) g.FillEllipse(b, dx0 - 3, dy0 - 3, dd + 6, dd + 6);
            using (var b = new SolidBrush(dot)) g.FillEllipse(b, dx0, dy0, dd, dd);
            Theme.Draw(g, BadgeText, Theme.UISmall, new Rectangle(badgeRect.X + Theme.S(27), badgeRect.Y, badgeRect.Width - Theme.S(30), badgeRect.Height), Color.White, TextFormatFlags.Left);
        }
    }
}
