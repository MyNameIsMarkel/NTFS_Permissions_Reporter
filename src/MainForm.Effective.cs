using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace NtfsReporter
{
    partial class MainForm
    {
        readonly InputBox effUser = new InputBox("DOMINIO\\usuario, usuario@dominio o SID", Theme.GUser);
        readonly CheckBox chkEffChanges = Ui.Check("Mostrar solo donde cambia el acceso", true);
        readonly CheckBox chkEffHideNone = Ui.Check("Ocultar carpetas sin acceso", false);
        readonly Label effInfo = new Label();
        readonly DataGridView effGrid = new DataGridView();
        readonly List<EffRow> effRows = new List<EffRow>();
        List<EffRow> effView = new List<EffRow>();
        readonly ConcurrentQueue<EffRow> effPending = new ConcurrentQueue<EffRow>();
        static readonly string[] effCols = { "Carpeta", "Acceso efectivo", "Permiso NTFS", "Recurso compartido", "Permisos explícitos", "Herencia deshabilitada", "Es propietario", "Nivel", "Error" };
        static readonly int[] effWidths = { 520, 220, 200, 140, 90, 100, 90, 50, 220 };

        void BuildEffPage(Panel page)
        {
            Card top = new Card();
            top.Dock = DockStyle.Top;
            top.Padding = new Padding(Theme.S(14), Theme.S(12), Theme.S(14), Theme.S(10));

            Label lu = Ui.Label("Usuario o grupo", Theme.BodyBold, Theme.Ink);
            effUser.Width = Theme.S(340);
            effUser.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; if (!running) StartScan(); } };
            FlatButton exp = new FlatButton("Exportar", Theme.GExport, BtnKind.Primary);
            exp.Click += delegate { ExportEff(); };
            chkEffChanges.CheckedChanged += delegate { RefilterEff(); };
            chkEffHideNone.CheckedChanged += delegate { RefilterEff(); };

            FlowLayoutPanel row = Ui.Row(lu, effUser, chkEffChanges, chkEffHideNone);
            row.Dock = DockStyle.Left;
            FlowLayoutPanel rightRow = Ui.Row(exp);
            rightRow.Dock = DockStyle.Right;
            Panel line1 = new Panel(); line1.Dock = DockStyle.Top; line1.Height = Theme.S(40); line1.BackColor = Color.Transparent;
            line1.Controls.Add(row); line1.Controls.Add(rightRow);

            effInfo.Dock = DockStyle.Top;
            effInfo.AutoSize = false;
            effInfo.Height = Theme.S(46);
            effInfo.ForeColor = Theme.Muted;
            effInfo.Padding = new Padding(Theme.S(4), Theme.S(6), 0, 0);
            effInfo.Text = "Calcula qué acceso real tiene un usuario en cada carpeta, teniendo en cuenta todos sus grupos (también los anidados),\n" +
                           "las denegaciones, los grupos locales del servidor y los permisos del recurso compartido. Usa los directorios de la izquierda.";

            top.Controls.Add(effInfo);
            top.Controls.Add(line1);
            top.Height = Theme.S(24) + Theme.S(40) + Theme.S(46);

            Card content = new Card();
            content.Dock = DockStyle.Fill;
            content.Padding = new Padding(Theme.S(6));
            effGrid.Dock = DockStyle.Fill;
            Theme.StyleGrid(effGrid);
            effGrid.VirtualMode = true;
            for (int i = 0; i < effCols.Length; i++)
            {
                DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
                c.HeaderText = effCols[i]; c.Width = Theme.S(effWidths[i]); c.SortMode = DataGridViewColumnSortMode.NotSortable;
                effGrid.Columns.Add(c);
            }
            effGrid.CellValueNeeded += (s, e) => { if (e.RowIndex < effView.Count) e.Value = EffCell(effView[e.RowIndex], e.ColumnIndex); };
            effGrid.CellFormatting += (s, e) =>
            {
                if (e.RowIndex >= effView.Count) return;
                EffRow r = effView[e.RowIndex];
                if (r.Error != null) { e.CellStyle.ForeColor = Theme.Danger; e.CellStyle.BackColor = Theme.DangerSoft; }
                else if ((r.Mask & ~Sec.SYNC) == 0) e.CellStyle.ForeColor = Theme.Muted;
                else if ((r.Mask & Sec.FULL & ~Sec.SYNC) == (Sec.FULL & ~Sec.SYNC)) e.CellStyle.ForeColor = Theme.Success;
            };
            ContextMenuStrip m = new ContextMenuStrip();
            m.Items.Add("Copiar ruta", null, delegate { EffRow r = CurrentEff(); if (r != null) Clipboard.SetText(r.Path); });
            m.Items.Add("Abrir carpeta en el Explorador", null, delegate { EffRow r = CurrentEff(); if (r != null) OpenExplorer(r.Path); });
            effGrid.ContextMenuStrip = m;
            content.Controls.Add(effGrid);

            Panel gap = new Panel(); gap.Dock = DockStyle.Top; gap.Height = Theme.S(12); gap.BackColor = Theme.Bg;
            page.Controls.Add(content);
            page.Controls.Add(gap);
            page.Controls.Add(top);
        }

        EffRow CurrentEff()
        {
            if (effGrid.CurrentCell == null || effGrid.CurrentCell.RowIndex >= effView.Count) return null;
            return effView[effGrid.CurrentCell.RowIndex];
        }

        static string EffCell(EffRow r, int c)
        {
            switch (c)
            {
                case 0: return r.Path;
                case 1: return r.Error != null ? "" : r.Rights;
                case 2: return r.Error != null ? "" : r.Ntfs;
                case 3: return r.Share ?? "";
                case 4: return r.Error != null ? "" : r.Explicit ? "Sí" : "No";
                case 5: return r.Error != null ? "" : r.Protected ? "Sí" : "No";
                case 6: return r.Error != null ? "" : r.Owner ? "Sí" : "No";
                case 7: return r.Depth.ToString();
                case 8: return r.Error ?? "";
            }
            return "";
        }

        // Cada fila guarda si su acceso es distinto al de la carpeta padre
        class EffRowEx : EffRow { public bool Changed; }

        bool EffVisible(EffRow r)
        {
            EffRowEx x = (EffRowEx)r;
            if (r.Error != null) return true;
            if (chkEffChanges.Checked && !x.Changed) return false;
            if (chkEffHideNone.Checked && (r.Mask & ~Sec.SYNC) == 0) return false;
            return true;
        }

        void RefilterEff()
        {
            effView = effRows.FindAll(EffVisible);
            effGrid.RowCount = 0;
            effGrid.RowCount = effView.Count;
            UpdateCounts();
        }

        void StartEffective(List<string> roots)
        {
            string user = effUser.Text.Trim();
            if (user.Length == 0) { MessageBox.Show(this, "Escribe el usuario o grupo a comprobar.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); effUser.Box.Focus(); return; }
            effRows.Clear(); effView = new List<EffRow>(); effGrid.RowCount = 0;
            EffRow dummy; while (effPending.TryDequeue(out dummy)) { }
            SetRunning(true);
            watch.Reset(); watch.Start();
            int depth = Depth, threads = (int)numThreads.Value;
            bool follow = chkFollow.Checked;

            Thread prep = new Thread(() =>
            {
                try
                {
                    prepText = "Resolviendo los grupos de " + user + "...";
                    Principal p = Accounts.ResolvePrincipal(user);
                    List<ScanItem> items = new List<ScanItem>();
                    List<string> notes = new List<string>();
                    foreach (string root in roots)
                    {
                        string server = Paths.ServerOf(root);
                        prepText = "Calculando grupos locales de " + (server ?? "este equipo") + "...";
                        EffRoot ctx = new EffRoot();
                        ctx.Server = server;
                        ctx.Set = p.SidsFor(server);
                        string srv, share;
                        if (Paths.TryParseUnc(root, out srv, out share))
                        {
                            string err;
                            byte[] ssd = Accounts.ShareSd(srv, share, out err);
                            if (ssd == null) notes.Add("\\\\" + srv + "\\" + share + ": permisos del recurso no disponibles (" + err + ")");
                            else
                            {
                                ctx.ShareKnown = true;
                                ctx.ShareMask = ssd.Length == 0 ? Sec.FULL : Sec.Effective(ssd, ctx.Set);
                                notes.Add("\\\\" + srv + "\\" + share + ": " + Sec.ShareName(ctx.ShareMask));
                            }
                        }
                        items.Add(new ScanItem { Path = root, Depth = 0, State = new EffNode(ctx, 0xFFFFFFFF) });
                    }
                    List<string> all = new List<string>(p.Warnings);
                    string info = p.Name + (p.IsGroup ? " (grupo)" : "") + "  ·  pertenece a " + p.DomainGroups.Count + " grupos (" + p.Source + ")";
                    if (notes.Count > 0) info += "  ·  Recurso compartido: " + string.Join("  |  ", notes.ToArray());
                    if (all.Count > 0) info += "\nAviso: " + string.Join(" ", all.ToArray());
                    BeginInvoke((Action)(() => { effInfo.Text = info; effInfo.ForeColor = Theme.Ink; }));
                    prepText = null;

                    Scanner s = new Scanner();
                    s.MaxDepth = depth; s.FollowReparse = follow;
                    s.EnumError = (it, code) => effPending.Enqueue(new EffRowEx { Path = it.Path, Depth = it.Depth, Error = "No se pudo listar el contenido: " + Native.ErrorText(code), Changed = true });
                    s.Visit = it =>
                    {
                        EffNode n = (EffNode)it.State;
                        int err;
                        byte[] sd = Sec.ReadSd(it.Path, out err);
                        if (sd == null)
                        {
                            effPending.Enqueue(new EffRowEx { Path = it.Path, Depth = it.Depth, Error = Native.ErrorText(err), Changed = true });
                            Interlocked.Increment(ref s.Errors);
                            return n;
                        }
                        bool ex, prot, own;
                        uint ntfs = Sec.Effective(sd, n.Root.Set, out ex, out prot, out own);
                        uint eff = n.Root.ShareKnown ? (ntfs & n.Root.ShareMask) : ntfs;
                        effPending.Enqueue(new EffRowEx
                        {
                            Path = it.Path, Depth = it.Depth, Mask = eff, Rights = Sec.Simple(eff), Ntfs = Sec.Simple(ntfs),
                            Share = n.Root.ShareKnown ? Sec.ShareName(n.Root.ShareMask) : null,
                            Explicit = ex, Protected = prot, Owner = own, Changed = it.Depth == 0 || eff != n.Parent
                        });
                        return eff == n.Parent ? n : new EffNode(n.Root, eff);
                    };
                    s.Completed = () => { try { BeginInvoke((Action)FinishScan); } catch { } };
                    scanner = s;
                    s.Start(items, threads);
                }
                catch (Exception ex) { BeginInvoke((Action)(() => ShowError(ex))); }
            });
            prep.IsBackground = true;
            prep.Start();
        }

        void DrainEff()
        {
            if (effPending.IsEmpty) return;
            int before = effView.Count;
            EffRow r; int n = 0;
            while (n < 50000 && effPending.TryDequeue(out r))
            {
                n++;
                effRows.Add(r);
                if (EffVisible(r)) effView.Add(r);
            }
            if (effView.Count != before) effGrid.RowCount = effView.Count;
            UpdateCounts();
        }

        void OnEffFinished()
        {
            effRows.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.CurrentCultureIgnoreCase));
            RefilterEff();
        }

        class EffSource : IExportSource
        {
            readonly List<EffRow> rows;
            public EffSource(List<EffRow> r) { rows = r; }
            public int ColumnCount { get { return effCols.Length; } }
            public string ColumnName(int c) { return effCols[c]; }
            public int RowCount { get { return rows.Count; } }
            public string Cell(int r, int c) { return EffCell(rows[r], c); }
        }

        void ExportEff()
        {
            if (effRows.Count == 0) { MessageBox.Show(this, "Todavía no hay resultados.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (running) { MessageBox.Show(this, "Espera a que termine el cálculo.", Text); return; }
            bool[] vis = new bool[effCols.Length];
            for (int i = 0; i < vis.Length; i++) vis[i] = true;
            using (ExportDialog d = new ExportDialog(new EffSource(effView), new EffSource(effRows), vis, "Acceso efectivo de " + effUser.Text.Trim()))
                d.ShowDialog(this);
        }
    }
}
