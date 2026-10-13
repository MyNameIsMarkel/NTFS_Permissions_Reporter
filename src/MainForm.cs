using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Security.Principal;
using System.Windows.Forms;

namespace NtfsReporter
{
    partial class MainForm : Form
    {
        // Cabecera
        readonly FlatButton navReport = new FlatButton("Informe de permisos", Theme.GReport, BtnKind.HeaderTab);
        readonly FlatButton navEff = new FlatButton("Acceso efectivo de un usuario", Theme.GUser, BtnKind.HeaderTab);

        // Barra lateral
        readonly ListBox dirList = new ListBox();
        readonly Label dirCount = new Label();
        readonly NumericUpDown numDepth = Ui.Number(0, 9999, 50);
        readonly CheckBox chkUnlimited = Ui.Check("Sin límite", true);
        readonly NumericUpDown numThreads = Ui.Number(1, 256, 32);
        readonly CheckBox chkFollow = Ui.Check("Seguir uniones y enlaces simbólicos", false);
        readonly CheckBox chkSkipIdentical = Ui.Check("Omitir carpetas idénticas a su padre", false);
        readonly FlatButton btnRun = new FlatButton("Analizar", Theme.GPlay, BtnKind.Primary);

        // Paginas
        readonly Panel pageHost = new Panel();
        readonly Panel pageReport = new Panel();
        readonly Panel pageEff = new Panel();

        // Barra de estado
        readonly Label lblStatus = new Label();
        readonly Label lblCounts = new Label();
        readonly ProgressBar progress = new ProgressBar();

        readonly Timer timer = new Timer();
        readonly Stopwatch watch = new Stopwatch();
        Scanner scanner;
        bool running;
        volatile string prepText;
        bool effMode;

        public MainForm()
        {
            Text = "NTFS Permissions Reporter";
            Icon = Program.AppIconCached;
            Font = Theme.Body;
            BackColor = Theme.Bg;
            MinimumSize = new Size(Theme.S(1000), Theme.S(640));
            Size = new Size(Theme.S(1400), Theme.S(860));
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            Panel body = new Panel();
            body.Dock = DockStyle.Fill;
            body.Padding = new Padding(Theme.S(14), Theme.S(14), Theme.S(14), Theme.S(8));
            body.BackColor = Theme.Bg;

            pageHost.Dock = DockStyle.Fill;
            pageHost.BackColor = Theme.Bg;
            pageReport.Dock = DockStyle.Fill; pageReport.BackColor = Theme.Bg;
            pageEff.Dock = DockStyle.Fill; pageEff.BackColor = Theme.Bg;
            BuildReportPage(pageReport);
            BuildEffPage(pageEff);
            pageHost.Controls.Add(pageReport);
            pageHost.Controls.Add(pageEff);
            pageEff.Visible = false;

            Panel gap = new Panel(); gap.Dock = DockStyle.Left; gap.Width = Theme.S(14); gap.BackColor = Theme.Bg;
            Control sidebar = BuildSidebar();

            body.Controls.Add(pageHost);
            body.Controls.Add(gap);
            body.Controls.Add(sidebar);

            Controls.Add(body);
            Controls.Add(BuildStatusBar());
            Controls.Add(BuildHeader());

            navReport.Checked = true;
            navReport.Click += delegate { ShowPage(false); };
            navEff.Click += delegate { ShowPage(true); };

            timer.Interval = 250;
            timer.Tick += delegate { Drain(); };
            timer.Start();

            LoadSettings();
            UpdateEmptyState();
            FormClosing += delegate { if (scanner != null) scanner.Cancel(); SaveSettings(); };
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.F5) { e.Handled = true; if (running) StopScan(); else StartScan(); }
                else if (e.Control && e.KeyCode == Keys.F && !effMode) { e.Handled = true; search.Box.Focus(); search.Box.SelectAll(); }
                else if (e.Control && e.KeyCode == Keys.E) { e.Handled = true; if (effMode) ExportEff(); else ExportReport(); }
                else if (e.Control && e.KeyCode == Keys.L && !effMode) { e.Handled = true; ToggleFilters(!filterCard.Visible); }
            };
        }

        // ------------------------------------------------------------------
        // Cabecera
        // ------------------------------------------------------------------
        Control BuildHeader()
        {
            Panel h = new HeaderPanel();
            h.Dock = DockStyle.Top;
            h.Height = Theme.S(64);
            h.BackColor = Theme.Header;

            Label logo = new Label();
            logo.Text = Theme.HasIcons ? Theme.GShield : "";
            logo.Font = new Font(Theme.Icon.FontFamily, 20f);
            logo.ForeColor = Color.FromArgb(96, 165, 250);
            logo.AutoSize = true;
            logo.Location = new Point(Theme.S(18), Theme.S(16));
            Label t = new Label();
            t.Text = "NTFS Permissions Reporter";
            t.Font = Theme.Title; t.ForeColor = Color.White; t.AutoSize = true;
            t.Location = new Point(Theme.S(Theme.HasIcons ? 58 : 18), Theme.S(9));
            Label s = new Label();
            s.Text = "Auditoría de permisos NTFS y recursos compartidos";
            s.Font = Theme.Subtitle; s.ForeColor = Theme.HeaderInk; s.AutoSize = true;
            s.Location = new Point(Theme.S(Theme.HasIcons ? 60 : 20), Theme.S(36));
            h.Controls.Add(logo); h.Controls.Add(t); h.Controls.Add(s);

            FlowLayoutPanel nav = new FlowLayoutPanel();
            nav.AutoSize = false; nav.WrapContents = false; nav.BackColor = Color.Transparent;
            navReport.Height = navEff.Height = Theme.S(36);
            navReport.FitWidth(); navEff.FitWidth();
            nav.Controls.Add(navReport); nav.Controls.Add(navEff);
            nav.Size = new Size(navReport.Width + navEff.Width + navReport.Margin.Horizontal * 2 + Theme.S(4), navReport.Height + navReport.Margin.Vertical);
            nav.Location = new Point(Theme.S(440), Theme.S(12));
            h.Controls.Add(nav);

            Control admin;
            if (Program.IsAdmin)
            {
                Label a = new Label();
                a.Text = "● Administrador";
                a.Font = Theme.BodyBold;
                a.ForeColor = Color.FromArgb(134, 239, 172); a.AutoSize = true;
                admin = a;
            }
            else
            {
                FlatButton b = new FlatButton("Reiniciar como administrador", Theme.GShield, BtnKind.HeaderTab);
                b.Checked = true;
                b.Click += delegate { RestartAsAdmin(); };
                admin = b;
            }
            admin.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            h.Controls.Add(admin);
            h.Resize += delegate
            {
                admin.Location = new Point(h.Width - admin.Width - Theme.S(18), (h.Height - admin.Height) / 2);
                nav.Location = new Point(Math.Max(Theme.S(440), (h.Width - nav.Width) / 2), (h.Height - nav.Height) / 2);
            };
            return h;
        }

        class HeaderPanel : Panel
        {
            public HeaderPanel() { DoubleBuffered = true; }
        }

        void RestartAsAdmin()
        {
            try
            {
                SaveSettings();
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath);
                psi.Verb = "runas"; psi.UseShellExecute = true;
                Process.Start(psi);
                Close();
            }
            catch { }
        }

        // ------------------------------------------------------------------
        // Barra lateral: directorios + opciones
        // ------------------------------------------------------------------
        Control BuildSidebar()
        {
            Panel side = new Panel();
            side.Dock = DockStyle.Left;
            side.Width = Theme.S(340);
            side.BackColor = Theme.Bg;

            // --- Directorios ---
            Card dirs = new Card("Directorios a analizar", Theme.GFolder);
            dirs.Dock = DockStyle.Fill;

            FlatButton bManual = new FlatButton("Ruta", Theme.GAdd, BtnKind.Secondary);
            FlatButton bShares = new FlatButton("Shares", Theme.GServer, BtnKind.Secondary);
            FlatButton bImport = new FlatButton("Importar", Theme.GImport, BtnKind.Secondary);
            ToolTip tip = new ToolTip();
            tip.SetToolTip(bManual, "Escribir o pegar rutas manualmente");
            tip.SetToolTip(bShares, "Elegir recursos compartidos de un servidor");
            tip.SetToolTip(bImport, "Importar una lista de rutas desde un archivo de texto (una por línea)");
            FlowLayoutPanel addRow = Ui.Row(bManual, bShares, bImport);
            addRow.Dock = DockStyle.Top;
            addRow.Padding = new Padding(0, 0, 0, Theme.S(6));

            dirList.Dock = DockStyle.Fill;
            dirList.BorderStyle = BorderStyle.None;
            dirList.BackColor = Theme.Card;
            dirList.DrawMode = DrawMode.OwnerDrawFixed;
            dirList.ItemHeight = Theme.S(42);
            dirList.SelectionMode = SelectionMode.MultiExtended;
            dirList.IntegralHeight = false;
            dirList.AllowDrop = true;
            dirList.DrawItem += DrawDirItem;
            dirList.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete) RemoveSelectedDirs();
                if (e.Control && e.KeyCode == Keys.V) { AddDirs(Paths.SplitLinesSafe(Clipboard.GetText())); e.Handled = true; }
                if (e.Control && e.KeyCode == Keys.A) { for (int i = 0; i < dirList.Items.Count; i++) dirList.SetSelected(i, true); }
            };
            dirList.DragEnter += (s, e) => { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text) ? DragDropEffects.Copy : DragDropEffects.None; };
            dirList.DragDrop += (s, e) =>
            {
                List<string> l = new List<string>();
                string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null) foreach (string f in files) if (Directory.Exists(f)) l.Add(f);
                string txt = e.Data.GetData(DataFormats.Text) as string;
                if (txt != null) l.AddRange(Paths.SplitLinesSafe(txt));
                AddDirs(l);
            };
            ContextMenuStrip dcm = new ContextMenuStrip();
            dcm.Items.Add("Quitar seleccionados", null, delegate { RemoveSelectedDirs(); });
            dcm.Items.Add("Copiar rutas", null, delegate { CopyDirs(); });
            dcm.Items.Add("Pegar rutas", null, delegate { AddDirs(Paths.SplitLinesSafe(Clipboard.GetText())); });
            dcm.Items.Add(new ToolStripSeparator());
            dcm.Items.Add("Abrir en el Explorador", null, delegate { if (dirList.SelectedItem != null) OpenExplorer((string)dirList.SelectedItem); });
            dirList.ContextMenuStrip = dcm;

            Panel listFrame = new Panel();
            listFrame.Dock = DockStyle.Fill;
            listFrame.Padding = new Padding(1);
            listFrame.BackColor = Theme.Border;
            listFrame.Controls.Add(dirList);

            FlatButton bRemove = new FlatButton("Quitar", Theme.GDelete, BtnKind.Ghost);
            FlatButton bClear = new FlatButton("Vaciar", Theme.GClear, BtnKind.Ghost);
            dirCount.AutoSize = true; dirCount.ForeColor = Theme.Muted; dirCount.Font = Theme.Small;
            dirCount.Margin = new Padding(Theme.S(6), Theme.S(10), 0, 0);
            FlowLayoutPanel bottomRow = Ui.Row(bRemove, bClear, dirCount);
            bottomRow.Dock = DockStyle.Bottom;
            bottomRow.Padding = new Padding(0, Theme.S(6), 0, 0);

            dirs.Controls.Add(listFrame);
            dirs.Controls.Add(bottomRow);
            dirs.Controls.Add(addRow);

            bManual.Click += delegate
            {
                using (AddPathsDialog d = new AddPathsDialog()) if (d.ShowDialog(this) == DialogResult.OK) AddDirs(d.Result);
            };
            bShares.Click += delegate
            {
                using (ShareBrowserDialog d = new ShareBrowserDialog(Settings.Get("lastServer", "")))
                {
                    if (d.ShowDialog(this) == DialogResult.OK) { AddDirs(d.Result); Settings.Set("lastServer", d.ServerName); }
                }
            };
            bImport.Click += delegate { ImportDirs(); };
            bRemove.Click += delegate { RemoveSelectedDirs(); };
            bClear.Click += delegate
            {
                if (dirList.Items.Count == 0) return;
                if (MessageBox.Show(this, "¿Quitar todos los directorios de la lista?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                { dirList.Items.Clear(); UpdateDirCount(); }
            };

            // --- Opciones ---
            Card opts = new Card("Opciones de análisis", Theme.GColumns);
            opts.Dock = DockStyle.Bottom;
            TableLayoutPanel ot = new TableLayoutPanel();
            ot.Dock = DockStyle.Fill; ot.ColumnCount = 1; ot.BackColor = Color.Transparent; ot.AutoSize = true;
            chkUnlimited.CheckedChanged += delegate { numDepth.Enabled = !chkUnlimited.Checked; };
            numDepth.Enabled = false;
            ot.Controls.Add(Ui.Row(Ui.Label("Niveles de subcarpetas", Theme.Body, Theme.Ink), numDepth, chkUnlimited));
            ot.Controls.Add(Ui.Row(Ui.Label("Hilos en paralelo", Theme.Body, Theme.Ink), numThreads));
            tip.SetToolTip(numThreads, "En rutas de red, más hilos = más rápido (la espera es la latencia de red). 32-64 suele ir bien.");
            ot.Controls.Add(chkFollow);
            ot.Controls.Add(chkSkipIdentical);
            tip.SetToolTip(chkSkipIdentical, "Reduce mucho el número de filas: solo se guardan las carpetas cuyos permisos cambian respecto a la carpeta padre.");
            opts.Controls.Add(ot);
            opts.Height = opts.TitleHeight + Theme.S(24) + Theme.S(4 * 36);

            Panel runWrap = new Panel();
            runWrap.Dock = DockStyle.Bottom; runWrap.Height = Theme.S(62); runWrap.BackColor = Theme.Bg;
            runWrap.Padding = new Padding(0, Theme.S(12), 0, 0);
            btnRun.Dock = DockStyle.Fill;
            btnRun.Font = new Font("Segoe UI Semibold", 11f);
            btnRun.Click += delegate { if (running) StopScan(); else StartScan(); };
            runWrap.Controls.Add(btnRun);

            Panel sp = new Panel(); sp.Dock = DockStyle.Bottom; sp.Height = Theme.S(12); sp.BackColor = Theme.Bg;

            side.Controls.Add(dirs);
            side.Controls.Add(sp);
            side.Controls.Add(opts);
            side.Controls.Add(runWrap);
            return side;
        }

        void DrawDirItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            string p = (string)dirList.Items[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(sel ? Theme.Selection : Theme.Card)) g.FillRectangle(b, e.Bounds);
            bool unc = Paths.IsUnc(p);
            int x = e.Bounds.X + Theme.S(10);
            if (Theme.HasIcons)
            {
                TextRenderer.DrawText(g, unc ? Theme.GServer : Theme.GFolder, Theme.Icon, new Rectangle(x, e.Bounds.Y, Theme.S(22), e.Bounds.Height),
                    unc ? Theme.Accent : Theme.Warning, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                x += Theme.S(28);
            }
            string name = Paths.NameOf(p);
            int w = e.Bounds.Right - x - Theme.S(6);
            TextRenderer.DrawText(g, name, Theme.BodyBold, new Rectangle(x, e.Bounds.Y + Theme.S(4), w, Theme.S(18)), Theme.Ink, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, p, Theme.Small, new Rectangle(x, e.Bounds.Y + Theme.S(22), w, Theme.S(16)), Theme.Muted, TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            using (Pen pn = new Pen(Color.FromArgb(240, 242, 246))) g.DrawLine(pn, e.Bounds.X, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        }

        void AddDirs(IEnumerable<string> paths)
        {
            HashSet<string> existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object o in dirList.Items) existing.Add((string)o);
            int added = 0;
            dirList.BeginUpdate();
            foreach (string raw in paths)
            {
                string p = Paths.Normalize(raw);
                if (!Paths.LooksValid(p) || existing.Contains(p)) continue;
                existing.Add(p);
                dirList.Items.Add(p);
                added++;
            }
            dirList.EndUpdate();
            UpdateDirCount();
            if (added > 0) SetStatus(added + " directorio(s) añadidos.");
        }

        void RemoveSelectedDirs()
        {
            List<object> sel = new List<object>();
            foreach (object o in dirList.SelectedItems) sel.Add(o);
            foreach (object o in sel) dirList.Items.Remove(o);
            UpdateDirCount();
        }

        void CopyDirs()
        {
            List<string> l = new List<string>();
            foreach (object o in (dirList.SelectedItems.Count > 0 ? (System.Collections.IEnumerable)dirList.SelectedItems : dirList.Items)) l.Add((string)o);
            if (l.Count > 0) Clipboard.SetText(string.Join(Environment.NewLine, l.ToArray()));
        }

        void ImportDirs()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = "Listas de rutas (*.txt;*.csv;*.lst)|*.txt;*.csv;*.lst|Todos los archivos|*.*";
                d.Title = "Importar lista de directorios";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    List<string> good = new List<string>();
                    int bad = 0;
                    foreach (string line in File.ReadAllLines(d.FileName))
                    {
                        string t = line.Trim();
                        if (t.Length == 0 || t.StartsWith("#") || t.StartsWith(";")) continue;
                        if (t.Contains(";") && !t.StartsWith("\\\\")) t = t.Split(';')[0];
                        if (t.Contains(",") && !Directory.Exists(t)) t = t.Split(',')[0];
                        string p = Paths.Normalize(t);
                        if (Paths.LooksValid(p)) good.Add(p); else bad++;
                    }
                    int before = dirList.Items.Count;
                    AddDirs(good);
                    int added = dirList.Items.Count - before;
                    MessageBox.Show(this, added + " directorio(s) importados." + (bad > 0 ? "\n" + bad + " línea(s) ignoradas por no ser rutas válidas." : "") +
                        (good.Count - added > 0 ? "\n" + (good.Count - added) + " ya estaban en la lista." : ""), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(this, "No se pudo leer el archivo:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        void UpdateDirCount()
        {
            int n = dirList.Items.Count;
            dirCount.Text = n == 0 ? "Sin directorios" : n == 1 ? "1 directorio" : n + " directorios";
            dirList.Invalidate();
        }

        List<string> SelectedRoots()
        {
            List<string> l = new List<string>();
            foreach (object o in dirList.Items) l.Add((string)o);
            return l;
        }

        static void OpenExplorer(string path)
        {
            try { Process.Start("explorer.exe", "\"" + path + "\""); } catch { }
        }

        // ------------------------------------------------------------------
        // Barra de estado
        // ------------------------------------------------------------------
        Control BuildStatusBar()
        {
            Panel s = new Panel();
            s.Dock = DockStyle.Bottom;
            s.Height = Theme.S(30);
            s.BackColor = Theme.Card;
            s.Padding = new Padding(Theme.S(16), 0, Theme.S(16), 0);
            s.Paint += (o, e) => { using (Pen p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, 0, s.Width, 0); };
            lblStatus.Dock = DockStyle.Fill; lblStatus.TextAlign = ContentAlignment.MiddleLeft; lblStatus.ForeColor = Theme.Muted;
            lblStatus.AutoEllipsis = true;
            lblCounts.Dock = DockStyle.Right; lblCounts.AutoSize = false; lblCounts.Width = Theme.S(420); lblCounts.TextAlign = ContentAlignment.MiddleRight; lblCounts.ForeColor = Theme.Ink;
            progress.Dock = DockStyle.Right; progress.Width = Theme.S(140); progress.Style = ProgressBarStyle.Marquee; progress.Visible = false;
            progress.MarqueeAnimationSpeed = 25;
            Panel pw = new Panel(); pw.Dock = DockStyle.Right; pw.Width = Theme.S(156); pw.Padding = new Padding(Theme.S(8), Theme.S(10), Theme.S(8), Theme.S(10));
            pw.Controls.Add(progress); progress.Dock = DockStyle.Fill;
            s.Controls.Add(lblStatus);
            s.Controls.Add(pw);
            s.Controls.Add(lblCounts);
            SetStatus(Program.IsAdmin ? "Listo." : "Listo. Sin permisos de administrador algunas carpetas pueden no ser legibles.");
            return s;
        }

        void SetStatus(string text) { lblStatus.Text = text; }

        // ------------------------------------------------------------------
        // Paginas y analisis
        // ------------------------------------------------------------------
        void ShowPage(bool eff)
        {
            if (running) return;
            effMode = eff;
            navReport.Checked = !eff; navEff.Checked = eff;
            pageReport.Visible = !eff; pageEff.Visible = eff;
            chkSkipIdentical.Enabled = !eff;
            btnRun.Text = eff ? "Calcular acceso" : "Analizar";
            UpdateCounts();
        }

        void SetRunning(bool r)
        {
            running = r;
            btnRun.Text = r ? "Detener" : (effMode ? "Calcular acceso" : "Analizar");
            btnRun.Glyph = r ? Theme.GStop : Theme.GPlay;
            btnRun.Kind = r ? BtnKind.Danger : BtnKind.Primary;
            btnRun.Invalidate();
            progress.Visible = r;
            navReport.Enabled = navEff.Enabled = !r;
        }

        void StartScan()
        {
            List<string> roots = SelectedRoots();
            if (roots.Count == 0)
            {
                MessageBox.Show(this, "Añade al menos un directorio: con «Ruta», «Shares», «Importar» o arrastrando carpetas a la lista.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (effMode) StartEffective(roots); else StartReport(roots);
        }

        void StopScan()
        {
            if (scanner != null) scanner.Cancel();
            SetStatus("Deteniendo...");
        }

        int Depth { get { return chkUnlimited.Checked ? int.MaxValue : (int)numDepth.Value; } }

        void Drain()
        {
            DrainReport();
            DrainEff();
            if (running)
            {
                string pt = prepText;
                if (pt != null) SetStatus(pt);
                else if (scanner != null)
                {
                    double sec = Math.Max(0.001, watch.Elapsed.TotalSeconds);
                    SetStatus(string.Format("Analizando...  {0:N0} carpetas  ·  {1:N0} carpetas/s  ·  {2:N0} errores  ·  {3:mm\\:ss}",
                        scanner.Folders, scanner.Folders / sec, scanner.Errors, watch.Elapsed));
                }
            }
        }

        void FinishScan()
        {
            watch.Stop();
            Drain();
            SetRunning(false);
            if (scanner != null)
            {
                double sec = Math.Max(0.001, watch.Elapsed.TotalSeconds);
                SetStatus(string.Format("{0}  {1:N0} carpetas en {2:N1} s ({3:N0}/s)  ·  {4:N0} errores",
                    scanner.Cancelled ? "Detenido." : "Terminado.", scanner.Folders, sec, scanner.Folders / sec, scanner.Errors));
            }
            if (effMode) OnEffFinished(); else OnReportFinished();
        }

        void ShowError(Exception ex)
        {
            prepText = null;
            SetRunning(false);
            SetStatus("Error: " + ex.Message);
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // ------------------------------------------------------------------
        // Preferencias
        // ------------------------------------------------------------------
        void LoadSettings()
        {
            string dirs = Settings.Get("dirs", "");
            if (dirs.Length > 0) AddDirs(dirs.Split('|'));
            UpdateDirCount();
            chkUnlimited.Checked = Settings.GetBool("unlimited", true);
            numDepth.Value = Math.Max(numDepth.Minimum, Math.Min(numDepth.Maximum, Settings.GetInt("depth", 50)));
            numThreads.Value = Math.Max(numThreads.Minimum, Math.Min(numThreads.Maximum, Settings.GetInt("threads", 32)));
            chkFollow.Checked = Settings.GetBool("follow", false);
            chkSkipIdentical.Checked = Settings.GetBool("skipIdentical", false);
            effUser.Text = Settings.Get("effUser", Environment.UserDomainName + "\\" + Environment.UserName);
            string cols = Settings.Get("columns", "");
            if (cols.Length > 0)
            {
                HashSet<string> vis = new HashSet<string>(cols.Split('|'));
                foreach (FieldDef f in Fields.All) colVisible[f.Index] = vis.Contains(f.English);
                ApplyColumnVisibility();
            }
            if (Settings.GetBool("treeView", false)) SetView(true);
        }

        void SaveSettings()
        {
            Settings.Set("dirs", string.Join("|", SelectedRoots().ToArray()));
            Settings.Set("unlimited", chkUnlimited.Checked);
            Settings.Set("depth", (int)numDepth.Value);
            Settings.Set("threads", (int)numThreads.Value);
            Settings.Set("follow", chkFollow.Checked);
            Settings.Set("skipIdentical", chkSkipIdentical.Checked);
            Settings.Set("effUser", effUser.Text);
            List<string> vis = new List<string>();
            foreach (FieldDef f in Fields.All) if (colVisible[f.Index]) vis.Add(f.English);
            Settings.Set("columns", string.Join("|", vis.ToArray()));
            Settings.Set("treeView", treeMode);
            Settings.Save();
        }
    }
}
