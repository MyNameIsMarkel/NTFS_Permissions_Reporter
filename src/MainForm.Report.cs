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
        // Datos
        readonly List<DirNode> rootNodes = new List<DirNode>();
        readonly List<DirNode> allDirs = new List<DirNode>();
        readonly List<RowRef> allRows = new List<RowRef>();
        List<RowRef> viewRows = new List<RowRef>();
        readonly ConcurrentQueue<DirNode> pendingDirs = new ConcurrentQueue<DirNode>();
        FilterSet filter = new FilterSet();
        readonly bool[] colVisible = new bool[Fields.All.Count];
        FieldDef sortField; bool sortAsc = true;
        bool skipIdenticalRun;

        // Controles
        readonly FlatButton btnTable = new FlatButton("Tabla", Theme.GTable, BtnKind.Toggle);
        readonly FlatButton btnTree = new FlatButton("Árbol", Theme.GTree, BtnKind.Toggle);
        readonly InputBox search = new InputBox("Buscar en los resultados...", Theme.GSearch);
        readonly FlatButton btnFilters = new FlatButton("Filtros", Theme.GFilter, BtnKind.Toggle);
        readonly FlatButton btnColumns = new FlatButton("Columnas", Theme.GColumns, BtnKind.Secondary);
        readonly FlatButton btnExport = new FlatButton("Exportar", Theme.GExport, BtnKind.Primary);
        readonly Card filterCard = new Card();
        readonly FlowLayoutPanel ruleList = new FlowLayoutPanel();
        readonly ComboBox cmbMatch = new ComboBox();
        readonly DataGridView grid = new DataGridView();
        readonly SplitContainer treeSplit = new SplitContainer();
        readonly TreeView tree = new TreeView();
        readonly DataGridView detailGrid = new DataGridView();
        readonly Label detailHeader = new Label();
        readonly Panel emptyState = new Panel();
        readonly Label emptyText = new Label();
        readonly System.Windows.Forms.Timer filterTimer = new System.Windows.Forms.Timer();
        List<RowRef> detailRows = new List<RowRef>();
        bool treeMode;
        Panel contentHost;

        // ------------------------------------------------------------------
        // Construccion
        // ------------------------------------------------------------------
        void BuildReportPage(Panel page)
        {
            foreach (FieldDef f in Fields.All) colVisible[f.Index] = f.DefaultVisible;

            // --- Barra de herramientas ---
            Card bar = new Card();
            bar.Dock = DockStyle.Top;
            bar.Height = Theme.S(56);
            bar.Padding = new Padding(Theme.S(10), Theme.S(9), Theme.S(10), Theme.S(9));

            btnTable.Checked = true;
            FlowLayoutPanel left = Ui.Row(btnTable, btnTree);
            left.Dock = DockStyle.Left;
            search.Width = Theme.S(320);
            search.Margin = new Padding(Theme.S(16), Theme.S(3), Theme.S(3), Theme.S(3));
            left.Controls.Add(search);

            FlowLayoutPanel right = Ui.Row(btnFilters, btnColumns, btnExport);
            right.Dock = DockStyle.Right;
            bar.Controls.Add(left);
            bar.Controls.Add(right);

            btnTable.Click += delegate { SetView(false); };
            btnTree.Click += delegate { SetView(true); };
            btnFilters.Click += delegate { ToggleFilters(!filterCard.Visible); };
            btnColumns.Click += delegate { ShowColumnMenu(); };
            btnExport.Click += delegate { ExportReport(); };
            search.Box.TextChanged += delegate { ScheduleFilter(); };

            // --- Panel de filtros ---
            BuildFilterCard();

            // --- Contenido ---
            Card content = new Card();
            content.Dock = DockStyle.Fill;
            content.Padding = new Padding(Theme.S(6));
            contentHost = new Panel();
            contentHost.Dock = DockStyle.Fill;
            contentHost.BackColor = Theme.Card;
            content.Controls.Add(contentHost);

            BuildGrid();
            BuildTree();
            BuildEmptyState();
            contentHost.Controls.Add(grid);
            contentHost.Controls.Add(treeSplit);
            contentHost.Controls.Add(emptyState);
            treeSplit.Visible = false;

            Panel gap1 = new Panel(); gap1.Dock = DockStyle.Top; gap1.Height = Theme.S(12); gap1.BackColor = Theme.Bg;
            Panel gap2 = new Panel(); gap2.Dock = DockStyle.Top; gap2.Height = Theme.S(12); gap2.BackColor = Theme.Bg;

            page.Controls.Add(content);
            page.Controls.Add(gap2);
            page.Controls.Add(filterCard);
            page.Controls.Add(gap1);
            page.Controls.Add(bar);
            filterCard.Visible = false;
            filterCard.VisibleChanged += delegate { gap2.Visible = filterCard.Visible; };
            gap2.Visible = false;

            filterTimer.Interval = 350;
            filterTimer.Tick += delegate { filterTimer.Stop(); ApplyFilter(); };
        }

        void BuildEmptyState()
        {
            emptyState.Dock = DockStyle.Fill;
            emptyState.BackColor = Theme.Card;
            Label icon = new Label();
            icon.Text = Theme.HasIcons ? Theme.GShield : "";
            icon.Font = Theme.IconLarge; icon.ForeColor = Color.FromArgb(191, 211, 254);
            icon.AutoSize = true;
            emptyText.Font = new Font("Segoe UI", 11f); emptyText.ForeColor = Theme.Muted; emptyText.AutoSize = true;
            emptyText.TextAlign = ContentAlignment.MiddleCenter;
            emptyText.Text = "Añade directorios en el panel de la izquierda y pulsa «Analizar».\n\nPuedes escribir rutas, elegir recursos compartidos de un servidor,\nimportar un .txt o arrastrar carpetas a la lista.";
            emptyState.Controls.Add(icon); emptyState.Controls.Add(emptyText);
            emptyState.Resize += delegate
            {
                icon.Location = new Point((emptyState.Width - icon.Width) / 2, emptyState.Height / 2 - icon.Height - Theme.S(30));
                emptyText.Location = new Point((emptyState.Width - emptyText.Width) / 2, emptyState.Height / 2 - Theme.S(10));
            };
        }

        void UpdateEmptyState()
        {
            bool empty = allDirs.Count == 0 && !running;
            emptyState.Visible = empty;
            if (empty) emptyState.BringToFront();
            grid.Visible = !empty && !treeMode;
            treeSplit.Visible = !empty && treeMode;
        }

        // ---------------- Tabla ----------------
        void BuildGrid()
        {
            ConfigureGrid(grid, Fields.All);
            grid.CellValueNeeded += (s, e) =>
            {
                if (e.RowIndex < viewRows.Count) e.Value = ((FieldDef)grid.Columns[e.ColumnIndex].Tag).Get(viewRows[e.RowIndex]);
            };
            grid.CellFormatting += (s, e) => { if (e.RowIndex < viewRows.Count) FormatRow(viewRows[e.RowIndex], e.CellStyle); };
            grid.ColumnHeaderMouseClick += (s, e) => SortBy((FieldDef)grid.Columns[e.ColumnIndex].Tag);
            grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0 && e.RowIndex < viewRows.Count) RevealInTree(viewRows[e.RowIndex].Dir); };
            grid.ContextMenuStrip = BuildRowMenu(grid, () => viewRows);
            grid.CellMouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && !grid.Rows[e.RowIndex].Selected)
                {
                    grid.ClearSelection();
                    grid.Rows[e.RowIndex].Selected = true;
                    grid.CurrentCell = grid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)];
                }
            };
        }

        void ConfigureGrid(DataGridView g, IEnumerable<FieldDef> fields)
        {
            g.Dock = DockStyle.Fill;
            Theme.StyleGrid(g);
            g.VirtualMode = true;
            foreach (FieldDef f in fields)
            {
                DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
                c.HeaderText = f.Name;
                c.Tag = f;
                c.Width = Theme.S(f.Width);
                c.SortMode = DataGridViewColumnSortMode.Programmatic;
                c.ToolTipText = f.English;
                c.Visible = colVisible[f.Index];
                g.Columns.Add(c);
            }
        }

        static readonly Font rowBold = new Font("Segoe UI Semibold", 9.5f);

        void FormatRow(RowRef r, DataGridViewCellStyle st)
        {
            if (r.Dir.Error != null) { st.ForeColor = Theme.Danger; st.BackColor = Theme.DangerSoft; return; }
            if (r.Ace < 0) { st.ForeColor = Theme.Muted; return; }
            AceEntry a = r.Entry;
            if (a.Type == 1) { st.ForeColor = Theme.Danger; st.SelectionForeColor = Theme.Danger; }
            else if (!a.Inherited) { st.ForeColor = Theme.AccentPressed; st.SelectionForeColor = Theme.AccentPressed; }
            if (a.Account.Unresolved) st.ForeColor = Theme.Warning;
        }

        ContextMenuStrip BuildRowMenu(DataGridView g, Func<List<RowRef>> rows)
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Items.Add("Copiar celda", null, delegate { if (g.CurrentCell != null && g.CurrentCell.Value != null) Clipboard.SetText(g.CurrentCell.Value.ToString()); });
            m.Items.Add("Copiar filas seleccionadas", null, delegate { CopyRows(g, rows()); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Filtrar: mostrar solo este valor", null, delegate { FilterFromCell(g, rows(), Ops.Is); });
            m.Items.Add("Filtrar: excluir este valor", null, delegate { FilterFromCell(g, rows(), Ops.IsNot); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Ver en el árbol", null, delegate { RowRef? r = CurrentRow(g, rows()); if (r.HasValue) RevealInTree(r.Value.Dir); });
            m.Items.Add("Abrir carpeta en el Explorador", null, delegate { RowRef? r = CurrentRow(g, rows()); if (r.HasValue) OpenExplorer(r.Value.Dir.Path); });
            m.Items.Add("Ver acceso efectivo de esta cuenta", null, delegate
            {
                RowRef? r = CurrentRow(g, rows());
                if (!r.HasValue || r.Value.Ace < 0) return;
                effUser.Text = r.Value.Entry.Account.SidString.Length > 0 && r.Value.Entry.Account.Unresolved ? r.Value.Entry.Account.SidString : r.Value.Entry.Account.Name;
                ShowPage(true);
            });
            return m;
        }

        static RowRef? CurrentRow(DataGridView g, List<RowRef> rows)
        {
            if (g.CurrentCell == null || g.CurrentCell.RowIndex < 0 || g.CurrentCell.RowIndex >= rows.Count) return null;
            return rows[g.CurrentCell.RowIndex];
        }

        void CopyRows(DataGridView g, List<RowRef> rows)
        {
            List<DataGridViewColumn> cols = new List<DataGridViewColumn>();
            foreach (DataGridViewColumn c in g.Columns) if (c.Visible) cols.Add(c);
            cols.Sort((a, b) => a.DisplayIndex.CompareTo(b.DisplayIndex));
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            List<int> idx = new List<int>();
            foreach (DataGridViewRow r in g.SelectedRows) idx.Add(r.Index);
            idx.Sort();
            if (idx.Count > 50000) { MessageBox.Show(this, "Demasiadas filas para copiar. Usa «Exportar».", Text); return; }
            foreach (DataGridViewColumn c in cols) sb.Append(c.HeaderText).Append('\t');
            sb.AppendLine();
            foreach (int i in idx)
            {
                if (i >= rows.Count) continue;
                foreach (DataGridViewColumn c in cols) sb.Append(((FieldDef)c.Tag).Get(rows[i])).Append('\t');
                sb.AppendLine();
            }
            if (idx.Count > 0) Clipboard.SetText(sb.ToString());
        }

        void FilterFromCell(DataGridView g, List<RowRef> rows, int op)
        {
            RowRef? r = CurrentRow(g, rows);
            if (!r.HasValue || g.CurrentCell == null) return;
            FieldDef f = (FieldDef)g.Columns[g.CurrentCell.ColumnIndex].Tag;
            AddRuleRow(f, op, f.Get(r.Value));
            ToggleFilters(true);
            ApplyFilter();
        }

        void ShowColumnMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            foreach (FieldDef f in Fields.All)
            {
                FieldDef ff = f;
                ToolStripMenuItem it = new ToolStripMenuItem(f.Name);
                it.Checked = colVisible[f.Index];
                it.ToolTipText = f.English;
                it.Click += delegate { colVisible[ff.Index] = !colVisible[ff.Index]; ApplyColumnVisibility(); };
                m.Items.Add(it);
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Mostrar todas", null, delegate { for (int i = 0; i < colVisible.Length; i++) colVisible[i] = true; ApplyColumnVisibility(); });
            m.Items.Add("Restablecer", null, delegate { foreach (FieldDef f in Fields.All) colVisible[f.Index] = f.DefaultVisible; ApplyColumnVisibility(); });
            m.Show(btnColumns, new Point(0, btnColumns.Height));
        }

        void ApplyColumnVisibility()
        {
            bool any = false;
            foreach (bool b in colVisible) any |= b;
            if (!any) colVisible[Fields.Dir.Index] = true;
            foreach (DataGridViewColumn c in grid.Columns) c.Visible = colVisible[((FieldDef)c.Tag).Index];
            foreach (DataGridViewColumn c in detailGrid.Columns) c.Visible = colVisible[((FieldDef)c.Tag).Index] && c.Tag != Fields.Dir;
            if (!filter.IsEmpty) ScheduleFilter();
        }

        void SortBy(FieldDef f)
        {
            if (running) { SetStatus("Espera a que termine el análisis para ordenar."); return; }
            sortAsc = sortField == f ? !sortAsc : true;
            sortField = f;
            ApplySort();
            foreach (DataGridViewColumn c in grid.Columns)
                c.HeaderCell.SortGlyphDirection = c.Tag == f ? (sortAsc ? SortOrder.Ascending : SortOrder.Descending) : SortOrder.None;
            grid.Invalidate();
        }

        void ApplySort()
        {
            if (sortField == null || viewRows.Count == 0) return;
            Cursor = Cursors.WaitCursor;
            FieldDef f = sortField;
            string[] keys = new string[viewRows.Count];
            RowRef[] items = viewRows.ToArray();
            int[] order = new int[items.Length];
            for (int i = 0; i < items.Length; i++) { keys[i] = f.Get(items[i]) ?? ""; order[i] = i; }
            StringComparer cmp = StringComparer.CurrentCultureIgnoreCase;
            bool asc = sortAsc;
            bool numeric = f.English == "Depth";
            Array.Sort(order, (a, b) =>
            {
                int c = numeric ? int.Parse(keys[a]).CompareTo(int.Parse(keys[b])) : cmp.Compare(keys[a], keys[b]);
                if (c == 0) c = a.CompareTo(b);
                return asc ? c : -c;
            });
            List<RowRef> sorted = new List<RowRef>(items.Length);
            foreach (int i in order) sorted.Add(items[i]);
            viewRows = sorted;
            Cursor = Cursors.Default;
        }

        // ---------------- Arbol ----------------
        void BuildTree()
        {
            treeSplit.Dock = DockStyle.Fill;
            treeSplit.BackColor = Theme.Border;
            treeSplit.SplitterWidth = Theme.S(5);
            treeSplit.Panel1.BackColor = Theme.Card;
            treeSplit.Panel2.BackColor = Theme.Card;
            bool splitSet = false;
            treeSplit.SizeChanged += delegate
            {
                if (splitSet || treeSplit.Width < Theme.S(300)) return;
                splitSet = true;
                try { treeSplit.SplitterDistance = (int)(treeSplit.Width * 0.36); } catch { }
            };

            tree.Dock = DockStyle.Fill;
            tree.BorderStyle = BorderStyle.None;
            tree.Font = Theme.Body;
            tree.ImageList = Theme.FolderIcons();
            tree.HideSelection = false;
            tree.ShowLines = false;
            tree.FullRowSelect = true;
            tree.ItemHeight = Theme.S(26);
            tree.HandleCreated += delegate { Native.ExplorerTheme(tree); };
            tree.BeforeExpand += (s, e) => PopulateChildren(e.Node);
            tree.AfterSelect += (s, e) => ShowDetail(e.Node != null ? e.Node.Tag as DirNode : null);
            ContextMenuStrip tm = new ContextMenuStrip();
            tm.Items.Add("Abrir carpeta en el Explorador", null, delegate { if (tree.SelectedNode != null) OpenExplorer(((DirNode)tree.SelectedNode.Tag).Path); });
            tm.Items.Add("Copiar ruta", null, delegate { if (tree.SelectedNode != null) Clipboard.SetText(((DirNode)tree.SelectedNode.Tag).Path); });
            tm.Items.Add("Expandir todo lo de debajo", null, delegate { if (tree.SelectedNode != null) { tree.BeginUpdate(); tree.SelectedNode.ExpandAll(); tree.EndUpdate(); } });
            tree.ContextMenuStrip = tm;

            Panel legend = new Panel();
            legend.Dock = DockStyle.Bottom; legend.Height = Theme.S(30); legend.BackColor = Theme.Subtle;
            legend.Paint += (s, e) =>
            {
                int x = Theme.S(10);
                string[] names = { "Heredados", "Explícitos", "Herencia deshabilitada", "Error" };
                for (int i = 0; i < 4; i++)
                {
                    e.Graphics.DrawImage(tree.ImageList.Images[i], x, (legend.Height - tree.ImageList.ImageSize.Height) / 2);
                    x += tree.ImageList.ImageSize.Width + Theme.S(4);
                    Size sz = TextRenderer.MeasureText(names[i], Theme.Small);
                    TextRenderer.DrawText(e.Graphics, names[i], Theme.Small, new Point(x, (legend.Height - sz.Height) / 2), Theme.Muted);
                    x += sz.Width + Theme.S(12);
                }
            };
            treeSplit.Panel1.Controls.Add(tree);
            treeSplit.Panel1.Controls.Add(legend);

            detailHeader.Dock = DockStyle.Top;
            detailHeader.Height = Theme.S(64);
            detailHeader.Padding = new Padding(Theme.S(12), Theme.S(8), Theme.S(12), Theme.S(4));
            detailHeader.Font = Theme.Body;
            detailHeader.ForeColor = Theme.Ink;
            detailHeader.BackColor = Theme.Subtle;
            detailHeader.AutoEllipsis = true;
            detailHeader.Text = "Selecciona una carpeta del árbol para ver sus permisos.";

            List<FieldDef> detailFields = new List<FieldDef>();
            foreach (FieldDef f in Fields.All) if (f != Fields.Dir) detailFields.Add(f);
            ConfigureGrid(detailGrid, detailFields);
            detailGrid.CellValueNeeded += (s, e) =>
            {
                if (e.RowIndex < detailRows.Count) e.Value = ((FieldDef)detailGrid.Columns[e.ColumnIndex].Tag).Get(detailRows[e.RowIndex]);
            };
            detailGrid.CellFormatting += (s, e) => { if (e.RowIndex < detailRows.Count) FormatRow(detailRows[e.RowIndex], e.CellStyle); };
            detailGrid.ContextMenuStrip = BuildRowMenu(detailGrid, () => detailRows);

            treeSplit.Panel2.Controls.Add(detailGrid);
            treeSplit.Panel2.Controls.Add(detailHeader);
        }

        void SetView(bool treeView)
        {
            treeMode = treeView;
            btnTable.Checked = !treeView; btnTree.Checked = treeView;
            if (treeView && !running) RebuildTree();
            UpdateEmptyState();
        }

        int IconFor(DirNode d)
        {
            if (d.Error != null) return 3;
            if (d.Depth == 0 && Paths.IsUnc(d.Path)) return 4;
            if (d.Protected) return 2;
            if (d.HasExplicit) return 1;
            return 0;
        }

        bool VisibleInTree(DirNode d) { return filter.IsEmpty || d.FilterHit || d.FilterPath; }

        TreeNode MakeNode(DirNode d)
        {
            TreeNode n = new TreeNode(d.Name);
            n.Tag = d;
            n.ImageIndex = n.SelectedImageIndex = IconFor(d);
            if (!filter.IsEmpty && d.FilterHit) n.NodeFont = rowBold;
            if (d.IdenticalToParent && filter.IsEmpty) n.ForeColor = Theme.Muted;
            if (d.Error != null) { n.ForeColor = Theme.Danger; n.ToolTipText = d.Error; }
            foreach (DirNode c in d.ChildrenSnapshot())
                if (VisibleInTree(c)) { n.Nodes.Add(new TreeNode("…")); break; }
            return n;
        }

        void PopulateChildren(TreeNode node)
        {
            if (node.Nodes.Count != 1 || node.Nodes[0].Tag != null) return;
            DirNode d = (DirNode)node.Tag;
            DirNode[] kids = d.ChildrenSnapshot();
            Array.Sort(kids, (a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            tree.BeginUpdate();
            node.Nodes.Clear();
            List<TreeNode> list = new List<TreeNode>();
            foreach (DirNode k in kids) if (VisibleInTree(k)) list.Add(MakeNode(k));
            node.Nodes.AddRange(list.ToArray());
            tree.EndUpdate();
        }

        void RebuildTree()
        {
            string selected = tree.SelectedNode != null ? ((DirNode)tree.SelectedNode.Tag).Path : null;
            tree.BeginUpdate();
            tree.Nodes.Clear();
            foreach (DirNode r in rootNodes) if (VisibleInTree(r)) tree.Nodes.Add(MakeNode(r));
            if (tree.Nodes.Count == 1) { tree.Nodes[0].Expand(); }
            if (!filter.IsEmpty)
            {
                // expandir automaticamente hasta las coincidencias (con limite para no bloquear la UI)
                int budget = 300;
                Queue<TreeNode> q = new Queue<TreeNode>();
                foreach (TreeNode n in tree.Nodes) q.Enqueue(n);
                while (q.Count > 0 && budget > 0)
                {
                    TreeNode n = q.Dequeue();
                    DirNode d = (DirNode)n.Tag;
                    if (d.FilterPath && !d.FilterHit) { n.Expand(); budget--; foreach (TreeNode c in n.Nodes) q.Enqueue(c); }
                }
            }
            tree.EndUpdate();
            if (selected != null) RevealPath(selected, false);
            if (tree.SelectedNode == null && tree.Nodes.Count > 0) tree.SelectedNode = tree.Nodes[0];
        }

        void RevealInTree(DirNode d)
        {
            SetView(true);
            RevealPath(d.Path, true);
        }

        void RevealPath(string path, bool focus)
        {
            // localizar el nodo raiz y bajar por la cadena de padres
            DirNode target = null;
            foreach (DirNode d in allDirs) if (string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase)) { target = d; break; }
            if (target == null) return;
            List<DirNode> chain = new List<DirNode>();
            for (DirNode x = target; x != null; x = x.Parent) chain.Insert(0, x);
            TreeNodeCollection col = tree.Nodes;
            TreeNode found = null;
            foreach (DirNode step in chain)
            {
                found = null;
                foreach (TreeNode n in col) if (n.Tag == step) { found = n; break; }
                if (found == null) return;
                if (step != target) { PopulateChildren(found); found.Expand(); col = found.Nodes; }
            }
            if (found != null) { tree.SelectedNode = found; found.EnsureVisible(); if (focus) tree.Focus(); }
        }

        void ShowDetail(DirNode d)
        {
            detailRows = new List<RowRef>();
            if (d == null) { detailGrid.RowCount = 0; return; }
            List<string> lines = new List<string>();
            lines.Add(d.Path);
            if (d.Error != null) lines.Add("Error: " + d.Error);
            else
            {
                lines.Add("Propietario: " + (d.Owner != null ? d.Owner.Name : "?") + "     ·     Herencia: " + (d.Protected ? "deshabilitada" : "habilitada") +
                          "     ·     " + (d.HasExplicit ? "Tiene permisos explícitos" : "Solo permisos heredados") + (d.IdenticalToParent ? "     ·     Idénticos al padre" : ""));
                if (!filter.IsEmpty) lines.Add("Mostrando solo las entradas que cumplen los filtros.");
            }
            detailHeader.Text = string.Join("\n", lines.ToArray());
            if (d.Error != null || d.Aces.Length == 0) detailRows.Add(new RowRef(d, -1));
            else for (int i = 0; i < d.Aces.Length; i++) detailRows.Add(new RowRef(d, i));
            if (!filter.IsEmpty) detailRows = detailRows.FindAll(r => filter.Match(r));
            detailGrid.RowCount = 0;
            detailGrid.RowCount = detailRows.Count;
            detailGrid.Invalidate();
        }

        // ---------------- Filtros ----------------
        void BuildFilterCard()
        {
            filterCard.Dock = DockStyle.Top;
            filterCard.Padding = new Padding(Theme.S(14), Theme.S(10), Theme.S(14), Theme.S(10));

            Label l1 = Ui.Label("Mostrar las filas que cumplan", Theme.BodyBold, Theme.Ink);
            cmbMatch.DropDownStyle = ComboBoxStyle.DropDownList; cmbMatch.Font = Theme.Body; cmbMatch.Width = Theme.S(220);
            cmbMatch.Items.AddRange(new object[] { "todas las condiciones (Y)", "alguna de las condiciones (O)" });
            cmbMatch.SelectedIndex = 0;
            cmbMatch.Margin = new Padding(Theme.S(3), Theme.S(5), Theme.S(3), Theme.S(3));
            cmbMatch.SelectedIndexChanged += delegate { ScheduleFilter(); };
            FlatButton add = new FlatButton("Añadir condición", Theme.GAdd, BtnKind.Ghost);
            FlatButton clear = new FlatButton("Quitar filtros", Theme.GClear, BtnKind.Ghost);
            add.Click += delegate { AddRuleRow(Fields.Account, Ops.Contains, ""); };
            clear.Click += delegate { ruleList.Controls.Clear(); ResizeFilterCard(); ApplyFilter(); };
            FlowLayoutPanel head = Ui.Row(l1, cmbMatch, add, clear);
            head.Dock = DockStyle.Top;

            ruleList.Dock = DockStyle.Top;
            ruleList.FlowDirection = FlowDirection.TopDown;
            ruleList.WrapContents = false;
            ruleList.AutoSize = true;
            ruleList.BackColor = Color.Transparent;
            ruleList.Padding = new Padding(0, Theme.S(4), 0, 0);

            filterCard.Controls.Add(ruleList);
            filterCard.Controls.Add(head);
            ResizeFilterCard();
        }

        void ToggleFilters(bool show)
        {
            filterCard.Visible = show;
            btnFilters.Checked = show || ruleList.Controls.Count > 0;
            if (show && ruleList.Controls.Count == 0) AddRuleRow(Fields.Account, Ops.Contains, "");
        }

        void ResizeFilterCard()
        {
            filterCard.Height = Theme.S(20) + Theme.S(40) + ruleList.Controls.Count * Theme.S(40) + Theme.S(6);
            int active = 0;
            foreach (Control c in ruleList.Controls) if (((RuleRow)c).IsActive) active++;
            btnFilters.Badge = active;
            btnFilters.FitWidth();
            btnFilters.Checked = filterCard.Visible || active > 0;
            btnFilters.Invalidate();
        }

        void AddRuleRow(FieldDef f, int op, string value)
        {
            RuleRow r = new RuleRow(this, f, op, value);
            ruleList.Controls.Add(r);
            ResizeFilterCard();
            r.FocusValue();
        }

        internal void RemoveRule(RuleRow r)
        {
            ruleList.Controls.Remove(r);
            r.Dispose();
            ResizeFilterCard();
            ApplyFilter();
        }

        internal void ScheduleFilter()
        {
            filterTimer.Stop();
            filterTimer.Start();
        }

        internal List<string> DistinctValues(FieldDef f, int max)
        {
            if (f.IsBool) return new List<string> { "Sí", "No" };
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> r = new List<string>();
            int scanned = 0;
            foreach (RowRef row in allRows)
            {
                string v = f.Get(row);
                if (!string.IsNullOrEmpty(v) && set.Add(v)) { r.Add(v); if (r.Count >= max) break; }
                if (++scanned > 400000) break;
            }
            r.Sort(StringComparer.CurrentCultureIgnoreCase);
            return r;
        }

        FilterSet BuildFilterSet()
        {
            FilterSet fs = new FilterSet();
            fs.MatchAll = cmbMatch.SelectedIndex != 1;
            foreach (Control c in ruleList.Controls)
            {
                RuleRow rr = (RuleRow)c;
                if (rr.IsActive) fs.Rules.Add(rr.ToRule());
            }
            fs.QuickText = search.Text.Trim();
            foreach (FieldDef f in Fields.All) if (colVisible[f.Index]) fs.QuickFields.Add(f);
            return fs;
        }

        void ApplyFilter()
        {
            filter = BuildFilterSet();
            ResizeFilterCard();
            Cursor = Cursors.WaitCursor;
            if (filter.IsEmpty) viewRows = new List<RowRef>(allRows);
            else
            {
                List<RowRef> v = new List<RowRef>();
                foreach (RowRef r in allRows) if (filter.Match(r)) v.Add(r);
                viewRows = v;
            }
            // marcas para el arbol
            foreach (DirNode d in allDirs) { d.FilterHit = false; d.FilterPath = false; }
            if (!filter.IsEmpty)
            {
                foreach (RowRef r in viewRows)
                {
                    if (r.Dir.FilterHit) continue;
                    r.Dir.FilterHit = true;
                    for (DirNode p = r.Dir.Parent; p != null && !p.FilterPath; p = p.Parent) p.FilterPath = true;
                }
            }
            ApplySort();
            grid.RowCount = 0;
            grid.RowCount = viewRows.Count;
            grid.Invalidate();
            if (treeMode && !running) RebuildTree();
            Cursor = Cursors.Default;
            UpdateCounts();
        }

        void UpdateCounts()
        {
            if (effMode)
            {
                lblCounts.Text = effView.Count == effRows.Count ? effRows.Count.ToString("N0") + " carpetas" : effView.Count.ToString("N0") + " de " + effRows.Count.ToString("N0") + " carpetas";
                return;
            }
            if (allRows.Count == 0) { lblCounts.Text = ""; return; }
            string s = allDirs.Count.ToString("N0") + " carpetas  ·  ";
            s += filter.IsEmpty ? allRows.Count.ToString("N0") + " entradas" : viewRows.Count.ToString("N0") + " de " + allRows.Count.ToString("N0") + " entradas";
            lblCounts.Text = s;
        }

        // ------------------------------------------------------------------
        // Analisis
        // ------------------------------------------------------------------
        void StartReport(List<string> roots)
        {
            allDirs.Clear(); allRows.Clear(); rootNodes.Clear();
            viewRows = new List<RowRef>();
            DirNode dummy; while (pendingDirs.TryDequeue(out dummy)) { }
            sortField = null;
            foreach (DataGridViewColumn c in grid.Columns) c.HeaderCell.SortGlyphDirection = SortOrder.None;
            grid.RowCount = 0;
            tree.Nodes.Clear();
            ShowDetail(null);
            filter = BuildFilterSet();
            skipIdenticalRun = chkSkipIdentical.Checked;
            SetRunning(true);
            UpdateEmptyState();
            watch.Reset(); watch.Start();

            int depth = Depth, threads = (int)numThreads.Value;
            bool follow = chkFollow.Checked;
            prepText = "Comprobando el directorio activo...";
            Thread prep = new Thread(() =>
            {
                try
                {
                    bool ldap = Accounts.LdapAvailable; // se comprueba una vez antes de lanzar los hilos
                    prepText = null;
                    List<ScanItem> items = new List<ScanItem>();
                    foreach (string r in roots) items.Add(new ScanItem { Path = r, Depth = 0, State = new RootInfo { Path = r, Server = Paths.ServerOf(r) } });
                    Scanner s = new Scanner();
                    s.MaxDepth = depth; s.FollowReparse = follow;
                    s.Visit = it =>
                    {
                        DirNode parent = it.State as DirNode;
                        RootInfo root = parent != null ? parent.Root : (RootInfo)it.State;
                        DirNode n = PermissionReader.Read(it, parent, root);
                        if (n.Error != null) Interlocked.Increment(ref s.Errors);
                        if (parent != null) parent.AddChild(n);
                        pendingDirs.Enqueue(n);
                        return n;
                    };
                    s.EnumError = (it, code) =>
                    {
                        DirNode n = it.State as DirNode;
                        if (n != null && n.Error == null) n.Error = "No se pudo listar el contenido: " + Native.ErrorText(code);
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

        void DrainReport()
        {
            if (pendingDirs.IsEmpty) return;
            int before = viewRows.Count;
            int n = 0;
            DirNode d;
            bool filtered = !filter.IsEmpty;
            while (n < 50000 && pendingDirs.TryDequeue(out d))
            {
                n++;
                allDirs.Add(d);
                if (d.Parent == null) rootNodes.Add(d);
                if (skipIdenticalRun && d.IdenticalToParent && d.Error == null) continue;
                if (d.Error != null || d.Aces.Length == 0) AddRow(new RowRef(d, -1), filtered);
                else for (int i = 0; i < d.Aces.Length; i++) AddRow(new RowRef(d, i), filtered);
            }
            if (allDirs.Count > 0 && emptyState.Visible) UpdateEmptyState();
            if (viewRows.Count != before) grid.RowCount = viewRows.Count;
            UpdateCounts();
        }

        void AddRow(RowRef r, bool filtered)
        {
            allRows.Add(r);
            if (!filtered || filter.Match(r))
            {
                viewRows.Add(r);
                if (filtered)
                {
                    r.Dir.FilterHit = true;
                    for (DirNode p = r.Dir.Parent; p != null && !p.FilterPath; p = p.Parent) p.FilterPath = true;
                }
            }
        }

        void OnReportFinished()
        {
            UpdateEmptyState();
            if (sortField != null) ApplySort();
            grid.RowCount = 0;
            grid.RowCount = viewRows.Count;
            if (treeMode) RebuildTree();
            UpdateCounts();
        }

        // ------------------------------------------------------------------
        // Exportar
        // ------------------------------------------------------------------
        class RowSource : IExportSource
        {
            readonly List<RowRef> rows;
            public RowSource(List<RowRef> r) { rows = r; }
            public int ColumnCount { get { return Fields.All.Count; } }
            public string ColumnName(int c) { return Fields.All[c].Name; }
            public int RowCount { get { return rows.Count; } }
            public string Cell(int r, int c) { return Fields.All[c].Get(rows[r]); }
        }

        void ExportReport()
        {
            if (allRows.Count == 0) { MessageBox.Show(this, "Todavía no hay resultados. Analiza algún directorio primero.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (running) { MessageBox.Show(this, "Espera a que termine el análisis o detenlo antes de exportar.", Text); return; }
            bool[] vis = (bool[])colVisible.Clone();
            using (ExportDialog d = new ExportDialog(new RowSource(new List<RowRef>(viewRows)), new RowSource(allRows), vis, "Informe de permisos NTFS"))
                d.ShowDialog(this);
        }
    }

    // ------------------------------------------------------------------
    // Fila de condicion de filtro:  [campo] [operador] [valor] [x]
    // ------------------------------------------------------------------
    class RuleRow : FlowLayoutPanel
    {
        readonly MainForm owner;
        readonly ComboBox field = new ComboBox(), op = new ComboBox(), value = new ComboBox();
        bool loaded;

        public RuleRow(MainForm owner, FieldDef f, int o, string v)
        {
            this.owner = owner;
            AutoSize = true; WrapContents = false; Margin = new Padding(0, 0, 0, Theme.S(2)); BackColor = Color.Transparent;

            field.DropDownStyle = ComboBoxStyle.DropDownList; field.Width = Theme.S(290); field.Font = Theme.Body; field.MaxDropDownItems = 25;
            foreach (FieldDef fd in Fields.All) field.Items.Add(new FieldItem { F = fd });
            op.DropDownStyle = ComboBoxStyle.DropDownList; op.Width = Theme.S(150); op.Font = Theme.Body;
            op.Items.AddRange(Ops.Names);
            value.DropDownStyle = ComboBoxStyle.DropDown; value.Width = Theme.S(320); value.Font = Theme.Body; value.MaxDropDownItems = 20;
            value.AutoCompleteMode = AutoCompleteMode.SuggestAppend; value.AutoCompleteSource = AutoCompleteSource.ListItems;
            FlatButton del = new FlatButton("", Theme.GClose, BtnKind.Ghost);
            del.Width = del.Height = Theme.S(30);
            if (!Theme.HasIcons) del.Text = "×";

            foreach (Control c in new Control[] { field, op, value }) c.Margin = new Padding(Theme.S(3), Theme.S(4), Theme.S(3), Theme.S(3));
            Controls.Add(field); Controls.Add(op); Controls.Add(value); Controls.Add(del);

            for (int i = 0; i < field.Items.Count; i++) if (((FieldItem)field.Items[i]).F == f) field.SelectedIndex = i;
            op.SelectedIndex = o;
            value.Text = v ?? "";

            field.SelectedIndexChanged += delegate
            {
                loaded = false;
                value.Items.Clear();
                if (Current.IsBool) { value.Items.AddRange(new object[] { "Sí", "No" }); value.Text = "Sí"; op.SelectedIndex = Ops.Is; loaded = true; }
                owner.ScheduleFilter();
            };
            op.SelectedIndexChanged += delegate { owner.ScheduleFilter(); };
            value.TextChanged += delegate { owner.ScheduleFilter(); };
            value.DropDown += delegate
            {
                if (loaded) return;
                loaded = true;
                string t = value.Text;
                value.Items.Clear();
                foreach (string s in owner.DistinctValues(Current, 500)) value.Items.Add(s);
                value.Text = t;
            };
            del.Click += delegate { owner.RemoveRule(this); };
        }

        class FieldItem
        {
            public FieldDef F;
            public override string ToString() { return F.Name; }
        }

        FieldDef Current { get { return ((FieldItem)field.SelectedItem).F; } }

        public bool IsActive { get { return value.Text.Length > 0 || op.SelectedIndex == Ops.Is || op.SelectedIndex == Ops.IsNot; } }

        public FilterRule ToRule()
        {
            return new FilterRule { Field = Current, Op = op.SelectedIndex, Value = value.Text };
        }

        public void FocusValue() { if (IsHandleCreated) BeginInvoke((Action)(() => value.Focus())); }

    }
}
