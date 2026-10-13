using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace NtfsReporter
{
    // Base comun para dialogos con el mismo estilo
    class DialogBase : Form
    {
        protected readonly Panel Body = new Panel();
        protected readonly FlowLayoutPanel Buttons = new FlowLayoutPanel();
        readonly Label subtitle = new Label();

        public DialogBase(string title, string sub, int width, int height)
        {
            Text = title;
            Font = Theme.Body;
            BackColor = Theme.Card;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Theme.S(width), Theme.S(height));
            Icon = Program.AppIconCached;

            Panel head = new Panel();
            head.Dock = DockStyle.Top; head.Height = Theme.S(64); head.BackColor = Theme.Card;
            head.Padding = new Padding(Theme.S(20), Theme.S(14), Theme.S(20), 0);
            Label t = Ui.Label(title, Theme.Title, Theme.Ink);
            t.Location = new Point(Theme.S(20), Theme.S(10)); t.Margin = Padding.Empty;
            subtitle.Text = sub; subtitle.Font = Theme.Subtitle; subtitle.ForeColor = Theme.Muted; subtitle.AutoSize = true;
            subtitle.Location = new Point(Theme.S(21), Theme.S(38));
            head.Controls.Add(t); head.Controls.Add(subtitle);

            Buttons.Dock = DockStyle.Bottom; Buttons.Height = Theme.S(58); Buttons.FlowDirection = FlowDirection.RightToLeft;
            Buttons.Padding = new Padding(Theme.S(16), Theme.S(12), Theme.S(16), Theme.S(12)); Buttons.BackColor = Theme.Subtle;

            Body.Dock = DockStyle.Fill; Body.Padding = new Padding(Theme.S(20), Theme.S(4), Theme.S(20), Theme.S(12)); Body.BackColor = Theme.Card;
            Controls.Add(Body); Controls.Add(Buttons); Controls.Add(head);
        }

        protected FlatButton AddButton(string text, string glyph, BtnKind kind, DialogResult result)
        {
            FlatButton b = new FlatButton(text, glyph, kind);
            if (result != DialogResult.None) b.Click += delegate { DialogResult = result; Close(); };
            Buttons.Controls.Add(b);
            return b;
        }
    }

    // ------------------------------------------------------------------
    // Añadir rutas manualmente
    // ------------------------------------------------------------------
    class AddPathsDialog : DialogBase
    {
        readonly TextBox box = new TextBox();
        public List<string> Result = new List<string>();

        public AddPathsDialog() : base("Añadir directorios", "Escribe o pega una ruta por línea: D:\\Datos o \\\\servidor\\recurso\\carpeta", 620, 380)
        {
            box.Multiline = true; box.ScrollBars = ScrollBars.Vertical; box.Dock = DockStyle.Fill; box.Font = new Font("Consolas", 10f);
            box.BorderStyle = BorderStyle.FixedSingle; box.AcceptsReturn = true;
            FlatButton browse = new FlatButton("Examinar carpeta...", Theme.GBrowse, BtnKind.Secondary);
            browse.Dock = DockStyle.Bottom;
            browse.Click += delegate
            {
                using (FolderBrowserDialog d = new FolderBrowserDialog())
                {
                    d.ShowNewFolderButton = false;
                    d.Description = "Elige una carpeta a analizar";
                    if (d.ShowDialog(this) == DialogResult.OK)
                    {
                        if (box.Text.Length > 0 && !box.Text.EndsWith(Environment.NewLine)) box.AppendText(Environment.NewLine);
                        box.AppendText(d.SelectedPath + Environment.NewLine);
                    }
                }
            };
            Panel spacer = new Panel(); spacer.Height = Theme.S(8); spacer.Dock = DockStyle.Bottom;
            Body.Controls.Add(box); Body.Controls.Add(spacer); Body.Controls.Add(browse);
            browse.Width = Theme.S(180);

            FlatButton ok = AddButton("Añadir", Theme.GAdd, BtnKind.Primary, DialogResult.None);
            AddButton("Cancelar", null, BtnKind.Secondary, DialogResult.Cancel);
            ok.Click += delegate
            {
                List<string> bad = new List<string>();
                foreach (string line in box.Lines)
                {
                    string p = Paths.Normalize(line);
                    if (p.Length == 0) continue;
                    if (Paths.LooksValid(p)) Result.Add(p); else bad.Add(p);
                }
                if (bad.Count > 0)
                {
                    MessageBox.Show(this, "Estas líneas no parecen rutas válidas y se ignorarán:\n\n" + string.Join("\n", bad.ToArray()), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                DialogResult = Result.Count > 0 ? DialogResult.OK : DialogResult.Cancel;
                Close();
            };
            Shown += delegate { box.Focus(); };
        }
    }

    // ------------------------------------------------------------------
    // Elegir recursos compartidos de un servidor
    // ------------------------------------------------------------------
    class ShareBrowserDialog : DialogBase
    {
        readonly InputBox server = new InputBox("Nombre o IP del servidor (ej. srvofi)", Theme.GServer);
        readonly CheckedListBox list = new CheckedListBox();
        readonly CheckBox hidden = Ui.Check("Mostrar recursos ocultos ($)", false);
        readonly Label status = new Label();
        List<ShareEntry> shares = new List<ShareEntry>();
        string currentServer;
        public List<string> Result = new List<string>();

        public ShareBrowserDialog(string lastServer) : base("Recursos compartidos de un servidor", "Lista las carpetas compartidas de un servidor y elige cuáles analizar", 560, 520)
        {
            FlatButton find = new FlatButton("Buscar", Theme.GSearch, BtnKind.Primary);
            server.Width = Theme.S(330);
            server.Text = lastServer ?? "";
            FlowLayoutPanel top = Ui.Row(server, find);
            top.Dock = DockStyle.Top;

            FlowLayoutPanel opts = Ui.Row(hidden);
            FlatButton all = new FlatButton("Marcar todos", Theme.GCheck, BtnKind.Ghost);
            FlatButton none = new FlatButton("Ninguno", null, BtnKind.Ghost);
            opts.Controls.Add(all); opts.Controls.Add(none);
            opts.Dock = DockStyle.Top;

            list.Dock = DockStyle.Fill; list.CheckOnClick = true; list.BorderStyle = BorderStyle.FixedSingle; list.Font = Theme.Body;
            list.IntegralHeight = false;
            status.Dock = DockStyle.Bottom; status.Height = Theme.S(24); status.ForeColor = Theme.Muted; status.TextAlign = ContentAlignment.MiddleLeft;

            Body.Controls.Add(list); Body.Controls.Add(status); Body.Controls.Add(opts); Body.Controls.Add(top);

            FlatButton ok = AddButton("Añadir seleccionados", Theme.GAdd, BtnKind.Primary, DialogResult.None);
            AddButton("Cancelar", null, BtnKind.Secondary, DialogResult.Cancel);

            find.Click += delegate { Search(); };
            server.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Search(); } };
            hidden.CheckedChanged += delegate { Fill(); };
            all.Click += delegate { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, true); };
            none.Click += delegate { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, false); };
            ok.Click += delegate
            {
                foreach (object o in list.CheckedItems)
                {
                    ShareItem si = (ShareItem)o;
                    Result.Add(@"\\" + currentServer + "\\" + si.Share.Name);
                }
                if (Result.Count == 0) { MessageBox.Show(this, "Marca al menos un recurso compartido.", Text); return; }
                DialogResult = DialogResult.OK; Close();
            };
            Shown += delegate { server.Box.Focus(); if (server.Text.Length > 0) server.Box.SelectAll(); };
        }

        public string ServerName { get { return currentServer; } }

        class ShareItem
        {
            public ShareEntry Share;
            public override string ToString() { return Share.Name + (Share.Remark.Length > 0 ? "   —   " + Share.Remark : ""); }
        }

        void Search()
        {
            string srv = server.Text.Trim().TrimStart('\\').Split('\\')[0];
            if (srv.Length == 0) return;
            status.Text = "Consultando " + srv + "...";
            status.ForeColor = Theme.Muted;
            list.Items.Clear();
            UseWaitCursor = true;
            Thread t = new Thread(() =>
            {
                string err;
                List<ShareEntry> r = Accounts.EnumShares(srv, out err);
                BeginInvoke((Action)(() =>
                {
                    UseWaitCursor = false;
                    if (err != null) { status.Text = "No se pudo consultar " + srv + ": " + err; status.ForeColor = Theme.Danger; return; }
                    currentServer = srv;
                    shares = r;
                    Fill();
                }));
            });
            t.IsBackground = true;
            t.Start();
        }

        void Fill()
        {
            list.Items.Clear();
            int hiddenCount = 0;
            foreach (ShareEntry s in shares)
            {
                if (s.Hidden && !hidden.Checked) { hiddenCount++; continue; }
                list.Items.Add(new ShareItem { Share = s });
            }
            if (currentServer != null)
            {
                status.ForeColor = Theme.Muted;
                status.Text = list.Items.Count + " recursos compartidos en " + currentServer + (hiddenCount > 0 ? " (" + hiddenCount + " ocultos)" : "");
            }
        }
    }

    // ------------------------------------------------------------------
    // Exportar
    // ------------------------------------------------------------------
    class ExportDialog : DialogBase
    {
        readonly ComboBox format = new ComboBox();
        readonly RadioButton scopeView = new RadioButton(), scopeAll = new RadioButton();
        readonly CheckedListBox cols = new CheckedListBox();
        readonly IExportSource viewSource, allSource;
        readonly string title;
        readonly bool[] visibleCols;

        public ExportDialog(IExportSource view, IExportSource all, bool[] visible, string reportTitle)
            : base("Exportar resultados", "Elige el formato, qué filas y qué columnas quieres incluir", 560, 600)
        {
            viewSource = view; allSource = all; title = reportTitle; visibleCols = visible;

            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill; t.ColumnCount = 1; t.RowCount = 6; t.BackColor = Theme.Card;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            t.Controls.Add(Ui.Label("Formato", Theme.BodyBold, Theme.Ink));
            format.DropDownStyle = ComboBoxStyle.DropDownList; format.Width = Theme.S(320); format.Font = Theme.Body;
            format.Items.AddRange(Exporters.Formats);
            format.SelectedIndex = 0;
            t.Controls.Add(format);

            t.Controls.Add(Ui.Label("Filas", Theme.BodyBold, Theme.Ink));
            scopeView.Text = "Solo las filas mostradas (con filtros): " + view.RowCount.ToString("N0");
            scopeView.AutoSize = true; scopeView.Checked = true;
            scopeAll.AutoSize = true;
            FlowLayoutPanel sc = new FlowLayoutPanel(); sc.FlowDirection = FlowDirection.TopDown; sc.AutoSize = true; sc.WrapContents = false;
            sc.Controls.Add(scopeView);
            if (all != null && all.RowCount != view.RowCount)
            {
                scopeAll.Text = "Todas las filas: " + all.RowCount.ToString("N0");
                sc.Controls.Add(scopeAll);
            }
            else scopeView.Text = "Todas las filas: " + view.RowCount.ToString("N0");
            t.Controls.Add(sc);

            FlowLayoutPanel ch = Ui.Row(Ui.Label("Columnas", Theme.BodyBold, Theme.Ink));
            FlatButton bAll = new FlatButton("Todas", null, BtnKind.Ghost);
            FlatButton bNone = new FlatButton("Ninguna", null, BtnKind.Ghost);
            FlatButton bVis = new FlatButton("Las visibles", null, BtnKind.Ghost);
            ch.Controls.Add(bAll); ch.Controls.Add(bNone); ch.Controls.Add(bVis);
            t.Controls.Add(ch);

            cols.Dock = DockStyle.Fill; cols.CheckOnClick = true; cols.BorderStyle = BorderStyle.FixedSingle; cols.IntegralHeight = false; cols.Font = Theme.Body;
            for (int i = 0; i < view.ColumnCount; i++) cols.Items.Add(view.ColumnName(i), visible[i]);
            t.Controls.Add(cols);
            Body.Controls.Add(t);

            bAll.Click += delegate { for (int i = 0; i < cols.Items.Count; i++) cols.SetItemChecked(i, true); };
            bNone.Click += delegate { for (int i = 0; i < cols.Items.Count; i++) cols.SetItemChecked(i, false); };
            bVis.Click += delegate { for (int i = 0; i < cols.Items.Count; i++) cols.SetItemChecked(i, visibleCols[i]); };

            FlatButton ok = AddButton("Exportar...", Theme.GExport, BtnKind.Primary, DialogResult.None);
            AddButton("Cancelar", null, BtnKind.Secondary, DialogResult.Cancel);
            ok.Click += delegate { DoExport(ok); };
        }

        void DoExport(FlatButton ok)
        {
            List<int> c = new List<int>();
            for (int i = 0; i < cols.Items.Count; i++) if (cols.GetItemChecked(i)) c.Add(i);
            if (c.Count == 0) { MessageBox.Show(this, "Marca al menos una columna.", Text); return; }
            ExportFormat f = (ExportFormat)format.SelectedItem;
            IExportSource src = scopeAll.Checked && allSource != null ? allSource : viewSource;
            string file;
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = f.Filter;
                d.FileName = "Permisos_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + f.Extension;
                d.OverwritePrompt = true;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                file = d.FileName;
            }
            ok.Enabled = false; ok.Text = "Exportando...";
            UseWaitCursor = true;
            int[] colArr = c.ToArray();
            Thread t = new Thread(() =>
            {
                Exception error = null;
                try { f.Write(src, colArr, file, title); }
                catch (Exception ex) { error = ex; }
                BeginInvoke((Action)(() =>
                {
                    UseWaitCursor = false; ok.Enabled = true; ok.Text = "Exportar...";
                    if (error != null) { MessageBox.Show(this, "No se pudo exportar:\n" + error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    if (MessageBox.Show(this, "Exportado correctamente:\n" + file + "\n\n¿Quieres abrirlo ahora?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        try { System.Diagnostics.Process.Start(file); } catch { }
                    }
                    DialogResult = DialogResult.OK; Close();
                }));
            });
            t.IsBackground = true;
            t.Start();
        }
    }
}
