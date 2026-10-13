using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace NtfsReporter
{
    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(243, 245, 249);
        public static readonly Color Card = Color.White;
        public static readonly Color Border = Color.FromArgb(222, 227, 235);
        public static readonly Color Ink = Color.FromArgb(17, 24, 39);
        public static readonly Color Muted = Color.FromArgb(100, 112, 133);
        public static readonly Color Subtle = Color.FromArgb(246, 248, 251);
        public static readonly Color Accent = Color.FromArgb(37, 99, 235);
        public static readonly Color AccentHover = Color.FromArgb(29, 78, 216);
        public static readonly Color AccentPressed = Color.FromArgb(30, 64, 175);
        public static readonly Color AccentSoft = Color.FromArgb(227, 236, 255);
        public static readonly Color Header = Color.FromArgb(15, 27, 61);
        public static readonly Color HeaderInk = Color.FromArgb(190, 203, 230);
        public static readonly Color Danger = Color.FromArgb(220, 38, 38);
        public static readonly Color DangerSoft = Color.FromArgb(254, 236, 236);
        public static readonly Color Success = Color.FromArgb(22, 163, 74);
        public static readonly Color Warning = Color.FromArgb(217, 119, 6);
        public static readonly Color Selection = Color.FromArgb(219, 232, 255);

        public static float Scale = 1f;
        public static int S(int px) { return (int)Math.Round(px * Scale); }

        public static Font Body, BodyBold, Small, Title, Subtitle, Section, Icon, IconSmall, IconLarge;

        public static void Init()
        {
            using (Bitmap b = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(b)) Scale = Math.Max(1f, g.DpiX / 96f);

            Body = new Font("Segoe UI", 9.5f);
            BodyBold = new Font("Segoe UI Semibold", 9.5f);
            Small = new Font("Segoe UI", 8.5f);
            Title = new Font("Segoe UI Semibold", 14f);
            Subtitle = new Font("Segoe UI", 9f);
            Section = new Font("Segoe UI Semibold", 10.5f);
            string iconFamily = HasFont("Segoe Fluent Icons") ? "Segoe Fluent Icons" : HasFont("Segoe MDL2 Assets") ? "Segoe MDL2 Assets" : null;
            HasIcons = iconFamily != null;
            Icon = new Font(iconFamily ?? "Segoe UI", 10f);
            IconSmall = new Font(iconFamily ?? "Segoe UI", 8.5f);
            IconLarge = new Font(iconFamily ?? "Segoe UI", 30f);
        }

        public static bool HasIcons;

        static bool HasFont(string name)
        {
            using (InstalledFontCollection fc = new InstalledFontCollection())
                foreach (FontFamily f in fc.Families) if (f.Name == name) return true;
            return false;
        }

        // Glifos de Segoe MDL2 / Fluent Icons
        public const string GAdd = "", GFolder = "", GServer = "", GImport = "", GPlay = "",
            GStop = "", GFilter = "", GTable = "", GTree = "", GExport = "", GDelete = "",
            GSearch = "", GColumns = "", GShield = "", GUser = "", GClear = "", GCheck = "",
            GWarning = "", GRefresh = "", GClose = "", GLock = "", GBrowse = "", GReport = "";

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void StyleGrid(DataGridView g)
        {
            g.BackgroundColor = Card;
            g.BorderStyle = BorderStyle.None;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            g.EnableHeadersVisualStyles = false;
            g.GridColor = Color.FromArgb(236, 239, 244);
            g.RowHeadersVisible = false;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.AllowUserToResizeRows = false;
            g.AllowUserToOrderColumns = true;
            g.ReadOnly = true;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.MultiSelect = true;
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.ColumnHeadersHeight = S(34);
            g.RowTemplate.Height = S(26);
            g.ColumnHeadersDefaultCellStyle.BackColor = Subtle;
            g.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
            g.ColumnHeadersDefaultCellStyle.Font = BodyBold;
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Subtle;
            g.ColumnHeadersDefaultCellStyle.Padding = new Padding(S(6), 0, S(4), 0);
            g.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            g.DefaultCellStyle.Font = Body;
            g.DefaultCellStyle.ForeColor = Ink;
            g.DefaultCellStyle.BackColor = Card;
            g.DefaultCellStyle.SelectionBackColor = Selection;
            g.DefaultCellStyle.SelectionForeColor = Ink;
            g.DefaultCellStyle.Padding = new Padding(S(6), 0, S(4), 0);
            g.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 251, 253);
            g.ShowCellToolTips = true;
            typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(g, true, null);
        }

        // Iconos de carpeta dibujados en tiempo de ejecucion (0 normal, 1 permisos explicitos, 2 herencia deshabilitada, 3 error, 4 raiz)
        public static ImageList FolderIcons()
        {
            int sz = S(18);
            ImageList il = new ImageList();
            il.ColorDepth = ColorDepth.Depth32Bit;
            il.ImageSize = new Size(sz, sz);
            il.Images.Add(DrawFolder(sz, Color.FromArgb(245, 189, 66), Color.Empty));
            il.Images.Add(DrawFolder(sz, Color.FromArgb(245, 189, 66), Accent));
            il.Images.Add(DrawFolder(sz, Color.FromArgb(245, 189, 66), Warning));
            il.Images.Add(DrawFolder(sz, Color.FromArgb(203, 213, 225), Danger));
            il.Images.Add(DrawRoot(sz));
            return il;
        }

        static Bitmap DrawFolder(int sz, Color body, Color badge)
        {
            Bitmap b = new Bitmap(sz, sz);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float u = sz / 18f;
                using (SolidBrush back = new SolidBrush(ControlPaint.Dark(body, 0.08f)))
                using (GraphicsPath tab = Round(new Rectangle((int)(1 * u), (int)(3 * u), (int)(7 * u), (int)(4 * u)), (int)Math.Max(1, u)))
                    g.FillPath(back, tab);
                using (SolidBrush br = new SolidBrush(body))
                using (GraphicsPath p = Round(new Rectangle((int)(1 * u), (int)(5 * u), (int)(16 * u), (int)(11 * u)), (int)Math.Max(1, 2 * u)))
                    g.FillPath(br, p);
                if (badge != Color.Empty)
                {
                    using (SolidBrush bb = new SolidBrush(badge))
                    using (Pen wp = new Pen(Color.White, Math.Max(1f, 1.3f * u)))
                    {
                        RectangleF c = new RectangleF(9.5f * u, 9.5f * u, 8f * u, 8f * u);
                        g.FillEllipse(bb, c);
                        g.DrawEllipse(wp, c);
                    }
                }
            }
            return b;
        }

        static Bitmap DrawRoot(int sz)
        {
            Bitmap b = new Bitmap(sz, sz);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float u = sz / 18f;
                using (SolidBrush br = new SolidBrush(Color.FromArgb(71, 85, 105)))
                using (GraphicsPath p = Round(new Rectangle((int)(1 * u), (int)(4 * u), (int)(16 * u), (int)(10 * u)), (int)Math.Max(1, 2 * u)))
                    g.FillPath(br, p);
                using (SolidBrush led = new SolidBrush(Color.FromArgb(74, 222, 128)))
                    g.FillEllipse(led, 12.5f * u, 8f * u, 2.5f * u, 2.5f * u);
                using (Pen pn = new Pen(Color.FromArgb(148, 163, 184), Math.Max(1f, u)))
                    g.DrawLine(pn, 3.5f * u, 9.2f * u, 10f * u, 9.2f * u);
            }
            return b;
        }

        public static Icon AppIcon()
        {
            int sz = 64;
            using (Bitmap b = new Bitmap(sz, sz))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (GraphicsPath p = Round(new Rectangle(2, 2, sz - 4, sz - 4), 14))
                    using (LinearGradientBrush lg = new LinearGradientBrush(new Rectangle(0, 0, sz, sz), Color.FromArgb(59, 130, 246), Color.FromArgb(30, 58, 138), 45f))
                        g.FillPath(lg, p);
                    using (GraphicsPath f = Round(new Rectangle(12, 22, 40, 28), 5))
                    using (SolidBrush w = new SolidBrush(Color.FromArgb(250, 204, 21)))
                        g.FillPath(w, f);
                    using (GraphicsPath t = Round(new Rectangle(12, 17, 18, 9), 3))
                    using (SolidBrush w2 = new SolidBrush(Color.FromArgb(234, 179, 8)))
                        g.FillPath(w2, t);
                    using (SolidBrush k = new SolidBrush(Color.FromArgb(30, 58, 138)))
                    {
                        g.FillEllipse(k, 26, 28, 12, 12);
                        g.FillRectangle(k, 30, 37, 4, 9);
                    }
                }
                return System.Drawing.Icon.FromHandle(b.GetHicon());
            }
        }
    }

    // ------------------------------------------------------------------
    // Boton plano moderno con icono
    // ------------------------------------------------------------------
    enum BtnKind { Primary, Secondary, Ghost, Danger, Toggle, HeaderTab }

    class FlatButton : Control
    {
        BtnKind kind = BtnKind.Secondary;
        bool hover, down, isChecked;
        string glyph;
        public int Badge;

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            BackColor = Color.Transparent;
            Font = Theme.Body;
            Cursor = Cursors.Hand;
            Height = Theme.S(32);
            Margin = new Padding(Theme.S(3));
        }

        public FlatButton(string text, string glyph, BtnKind kind) : this()
        {
            Text = text; this.glyph = glyph; this.kind = kind;
            if (kind == BtnKind.Primary) Font = Theme.BodyBold;
            FitWidth();
        }

        [DefaultValue(BtnKind.Secondary)]
        public BtnKind Kind { get { return kind; } set { kind = value; Invalidate(); } }
        public string Glyph { get { return glyph; } set { glyph = value; FitWidth(); Invalidate(); } }
        public bool Checked { get { return isChecked; } set { isChecked = value; Invalidate(); } }

        public void FitWidth()
        {
            using (Graphics g = CreateGraphics())
            {
                int w = Theme.S(14) * 2;
                if (!string.IsNullOrEmpty(Text)) w += TextRenderer.MeasureText(g, Text, Font).Width;
                if (!string.IsNullOrEmpty(glyph) && Theme.HasIcons) w += Theme.S(string.IsNullOrEmpty(Text) ? 6 : 24);
                if (Badge > 0) w += Theme.S(22);
                Width = Math.Max(w, Height);
            }
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); FitWidth(); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Focus(); Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty);
            base.OnKeyUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg, fg, border = Color.Empty;
            switch (kind)
            {
                case BtnKind.Primary:
                    bg = down ? Theme.AccentPressed : hover ? Theme.AccentHover : Theme.Accent; fg = Color.White; break;
                case BtnKind.Danger:
                    bg = down ? Color.FromArgb(153, 27, 27) : hover ? Color.FromArgb(185, 28, 28) : Theme.Danger; fg = Color.White; break;
                case BtnKind.Ghost:
                    bg = down ? Color.FromArgb(226, 232, 240) : hover ? Color.FromArgb(238, 242, 247) : Color.Transparent; fg = Theme.Ink; break;
                case BtnKind.Toggle:
                    if (isChecked) { bg = Theme.AccentSoft; fg = Theme.AccentPressed; border = Color.FromArgb(191, 211, 254); }
                    else { bg = hover ? Color.FromArgb(241, 244, 249) : Theme.Card; fg = Theme.Ink; border = Theme.Border; }
                    break;
                case BtnKind.HeaderTab:
                    bg = isChecked ? Color.FromArgb(40, 58, 110) : hover ? Color.FromArgb(28, 43, 86) : Color.Transparent;
                    fg = isChecked ? Color.White : Theme.HeaderInk; break;
                default:
                    bg = down ? Color.FromArgb(226, 232, 240) : hover ? Color.FromArgb(241, 244, 249) : Theme.Card; fg = Theme.Ink; border = Theme.Border; break;
            }
            if (!Enabled) { fg = Color.FromArgb(160, 170, 185); if (kind == BtnKind.Primary || kind == BtnKind.Danger) { bg = Color.FromArgb(203, 213, 225); fg = Color.White; } }

            using (GraphicsPath p = Theme.Round(r, Theme.S(6)))
            {
                if (bg.A > 0) using (SolidBrush b = new SolidBrush(bg)) g.FillPath(b, p);
                if (border != Color.Empty) using (Pen pn = new Pen(border)) g.DrawPath(pn, p);
                if (Focused && ShowFocusCues)
                    using (Pen fp = new Pen(Color.FromArgb(147, 197, 253), 2f)) g.DrawPath(fp, p);
            }

            int x = Theme.S(14);
            Size ts = string.IsNullOrEmpty(Text) ? Size.Empty : TextRenderer.MeasureText(g, Text, Font);
            int contentW = ts.Width + (glyph != null && Theme.HasIcons ? Theme.S(string.IsNullOrEmpty(Text) ? 0 : 24) + (string.IsNullOrEmpty(Text) ? Theme.S(16) : 0) : 0) + (Badge > 0 ? Theme.S(22) : 0);
            x = Math.Max(Theme.S(8), (Width - contentW) / 2);
            if (glyph != null && Theme.HasIcons)
            {
                Rectangle gr = new Rectangle(x, 0, Theme.S(18), Height);
                TextRenderer.DrawText(g, glyph, Theme.Icon, gr, fg, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                x += string.IsNullOrEmpty(Text) ? Theme.S(18) : Theme.S(24);
            }
            if (!string.IsNullOrEmpty(Text))
            {
                TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, ts.Width + 4, Height), fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
                x += ts.Width + Theme.S(4);
            }
            if (Badge > 0)
            {
                string bt = Badge.ToString();
                Rectangle br = new Rectangle(x, (Height - Theme.S(18)) / 2, Theme.S(18), Theme.S(18));
                using (SolidBrush bb = new SolidBrush(Theme.Accent)) g.FillEllipse(bb, br);
                TextRenderer.DrawText(g, bt, Theme.Small, br, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    // ------------------------------------------------------------------
    // Tarjeta con borde redondeado y titulo opcional
    // ------------------------------------------------------------------
    class Card : Panel
    {
        public string Title;
        public string Glyph;
        public int TitleHeight { get { return string.IsNullOrEmpty(Title) ? 0 : Theme.S(36); } }

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Padding = new Padding(Theme.S(12));
        }

        public Card(string title, string glyph) : this()
        {
            Title = title; Glyph = glyph;
            Padding = new Padding(Theme.S(12), Theme.S(12) + TitleHeight, Theme.S(12), Theme.S(12));
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Color parentBg = Parent != null ? Parent.BackColor : Theme.Bg;
            e.Graphics.Clear(parentBg);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Theme.Round(r, Theme.S(10)))
            using (SolidBrush b = new SolidBrush(BackColor))
            using (Pen pn = new Pen(Theme.Border))
            {
                e.Graphics.FillPath(b, p);
                e.Graphics.DrawPath(pn, p);
            }
            if (!string.IsNullOrEmpty(Title))
            {
                int x = Theme.S(14);
                if (!string.IsNullOrEmpty(Glyph) && Theme.HasIcons)
                {
                    TextRenderer.DrawText(e.Graphics, Glyph, Theme.Icon, new Rectangle(x, Theme.S(10), Theme.S(20), Theme.S(28)), Theme.Accent, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    x += Theme.S(26);
                }
                TextRenderer.DrawText(e.Graphics, Title, Theme.Section, new Rectangle(x, Theme.S(10), Width - x - Theme.S(12), Theme.S(28)), Theme.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }
    }

    // ------------------------------------------------------------------
    // Caja de texto con borde redondeado
    // ------------------------------------------------------------------
    class InputBox : Panel
    {
        public readonly TextBox Box = new TextBox();
        string glyph;
        bool focused;

        public InputBox(string cue, string glyph)
        {
            this.glyph = glyph;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Height = Theme.S(32);
            Margin = new Padding(Theme.S(3));
            Box.BorderStyle = BorderStyle.None;
            Box.Font = Theme.Body;
            Box.BackColor = Theme.Card;
            Box.ForeColor = Theme.Ink;
            Controls.Add(Box);
            Box.GotFocus += delegate { focused = true; Invalidate(); };
            Box.LostFocus += delegate { focused = false; Invalidate(); };
            Box.HandleCreated += delegate { Native.SetCue(Box, cue); };
            Click += delegate { Box.Focus(); };
            Cursor = Cursors.IBeam;
        }

        public override string Text { get { return Box.Text; } set { Box.Text = value; } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int left = Theme.S(10) + (glyph != null && Theme.HasIcons ? Theme.S(22) : 0);
            Box.SetBounds(left, (Height - Box.PreferredHeight) / 2 + 1, Width - left - Theme.S(10), Box.PreferredHeight);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Color parentBg = Parent != null ? Parent.BackColor : Theme.Card;
            e.Graphics.Clear(parentBg);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), Theme.S(6)))
            using (SolidBrush b = new SolidBrush(BackColor))
            using (Pen pn = new Pen(focused ? Theme.Accent : Theme.Border, focused ? 1.6f : 1f))
            {
                e.Graphics.FillPath(b, p);
                e.Graphics.DrawPath(pn, p);
            }
            if (glyph != null && Theme.HasIcons)
                TextRenderer.DrawText(e.Graphics, glyph, Theme.IconSmall, new Rectangle(Theme.S(10), 0, Theme.S(18), Height), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // Etiqueta simple con estilo
    static class Ui
    {
        public static Label Label(string text, Font font, Color color)
        {
            Label l = new Label();
            l.Text = text; l.Font = font; l.ForeColor = color; l.AutoSize = true; l.BackColor = Color.Transparent;
            l.Margin = new Padding(Theme.S(3), Theme.S(8), Theme.S(3), Theme.S(3));
            return l;
        }

        public static FlowLayoutPanel Row(params Control[] cs)
        {
            FlowLayoutPanel f = new FlowLayoutPanel();
            f.AutoSize = true; f.AutoSizeMode = AutoSizeMode.GrowAndShrink; f.WrapContents = false; f.Margin = new Padding(0);
            f.BackColor = Color.Transparent;
            f.Controls.AddRange(cs);
            return f;
        }

        public static CheckBox Check(string text, bool value)
        {
            CheckBox c = new CheckBox();
            c.Text = text; c.Checked = value; c.AutoSize = true; c.Font = Theme.Body; c.ForeColor = Theme.Ink;
            c.Margin = new Padding(Theme.S(3), Theme.S(6), Theme.S(3), Theme.S(3));
            c.BackColor = Color.Transparent;
            return c;
        }

        public static NumericUpDown Number(int min, int max, int value)
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = min; n.Maximum = max; n.Value = value; n.Width = Theme.S(70); n.Font = Theme.Body;
            n.BorderStyle = BorderStyle.FixedSingle;
            n.Margin = new Padding(Theme.S(3), Theme.S(4), Theme.S(3), Theme.S(3));
            n.TextAlign = HorizontalAlignment.Center;
            return n;
        }
    }
}
