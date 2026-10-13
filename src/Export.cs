using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace NtfsReporter
{
    interface IExportSource
    {
        int ColumnCount { get; }
        string ColumnName(int col);
        int RowCount { get; }
        string Cell(int row, int col);
    }

    class ExportFormat
    {
        public string Name, Extension, Filter;
        public Action<IExportSource, int[], string, string> Write; // origen, columnas, fichero, titulo
        public override string ToString() { return Name; }
    }

    static class Exporters
    {
        public static readonly ExportFormat[] Formats = {
            new ExportFormat { Name = "Excel (.xlsx)", Extension = ".xlsx", Filter = "Libro de Excel|*.xlsx", Write = WriteXlsx },
            new ExportFormat { Name = "CSV para Excel (separado por ;)", Extension = ".csv", Filter = "CSV|*.csv", Write = (s, c, f, t) => WriteDelimited(s, c, f, ';', true) },
            new ExportFormat { Name = "CSV estándar (separado por ,)", Extension = ".csv", Filter = "CSV|*.csv", Write = (s, c, f, t) => WriteDelimited(s, c, f, ',', true) },
            new ExportFormat { Name = "Texto tabulado (.txt)", Extension = ".txt", Filter = "Texto|*.txt", Write = (s, c, f, t) => WriteDelimited(s, c, f, '\t', false) },
            new ExportFormat { Name = "Página web (.html)", Extension = ".html", Filter = "HTML|*.html", Write = WriteHtml },
            new ExportFormat { Name = "JSON (.json)", Extension = ".json", Filter = "JSON|*.json", Write = WriteJson },
            new ExportFormat { Name = "XML (.xml)", Extension = ".xml", Filter = "XML|*.xml", Write = WriteXml },
        };

        // ---------------- CSV / TXT ----------------
        static void WriteDelimited(IExportSource s, int[] cols, string file, char sep, bool quote)
        {
            using (StreamWriter w = new StreamWriter(file, false, new UTF8Encoding(true), 1 << 16))
            {
                StringBuilder b = new StringBuilder();
                for (int i = 0; i < cols.Length; i++) { if (i > 0) b.Append(sep); AppendDelim(b, s.ColumnName(cols[i]), sep, quote); }
                w.WriteLine(b.ToString());
                for (int r = 0; r < s.RowCount; r++)
                {
                    b.Length = 0;
                    for (int i = 0; i < cols.Length; i++) { if (i > 0) b.Append(sep); AppendDelim(b, s.Cell(r, cols[i]), sep, quote); }
                    w.WriteLine(b.ToString());
                }
            }
        }

        static void AppendDelim(StringBuilder b, string v, char sep, bool quote)
        {
            v = v ?? "";
            if (!quote) { b.Append(v.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ')); return; }
            if (v.IndexOf(sep) >= 0 || v.IndexOf('"') >= 0 || v.IndexOf('\n') >= 0 || v.IndexOf('\r') >= 0)
                b.Append('"').Append(v.Replace("\"", "\"\"")).Append('"');
            else b.Append(v);
        }

        // ---------------- XML util ----------------
        static string X(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            StringBuilder b = new StringBuilder(v.Length + 8);
            foreach (char c in v)
            {
                switch (c)
                {
                    case '<': b.Append("&lt;"); break;
                    case '>': b.Append("&gt;"); break;
                    case '&': b.Append("&amp;"); break;
                    case '"': b.Append("&quot;"); break;
                    default:
                        if (c < 0x20 && c != '\t' && c != '\n' && c != '\r') b.Append(' ');
                        else b.Append(c);
                        break;
                }
            }
            return b.ToString();
        }

        // ---------------- XLSX (Open XML minimo, sin dependencias) ----------------
        static string ColName(int i)
        {
            string s = "";
            i++;
            while (i > 0) { int m = (i - 1) % 26; s = (char)('A' + m) + s; i = (i - 1) / 26; }
            return s;
        }

        static void Entry(ZipArchive z, string name, string content)
        {
            ZipArchiveEntry e = z.CreateEntry(name, CompressionLevel.Optimal);
            using (StreamWriter w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(content);
        }

        static void WriteXlsx(IExportSource s, int[] cols, string file, string title)
        {
            if (s.RowCount > 1048575) throw new Exception("Excel admite como máximo 1.048.575 filas. Aplica un filtro o exporta a CSV.");
            if (File.Exists(file)) File.Delete(file);
            using (FileStream fs = new FileStream(file, FileMode.CreateNew))
            using (ZipArchive z = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                Entry(z, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                    "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                    "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                    "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                    "</Types>");
                Entry(z, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                    "</Relationships>");
                Entry(z, "xl/workbook.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                    "<sheets><sheet name=\"Permisos\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
                    (s.RowCount > 0 ? "<definedNames><definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"0\" hidden=\"1\">Permisos!$A$1:$" + ColName(cols.Length - 1) + "$" + (s.RowCount + 1) + "</definedName></definedNames>" : "") +
                    "</workbook>");
                Entry(z, "xl/_rels/workbook.xml.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                    "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                    "</Relationships>");
                Entry(z, "xl/styles.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                    "<fonts count=\"2\"><font><sz val=\"10\"/><name val=\"Segoe UI\"/></font><font><b/><sz val=\"10\"/><color rgb=\"FFFFFFFF\"/><name val=\"Segoe UI\"/></font></fonts>" +
                    "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
                    "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF1E3A8A\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
                    "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                    "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                    "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                    "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/></cellXfs>" +
                    "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
                    "</styleSheet>");

                ZipArchiveEntry se = z.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal);
                using (StreamWriter w = new StreamWriter(se.Open(), new UTF8Encoding(false), 1 << 16))
                {
                    w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                    w.Write("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
                    w.Write("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
                    w.Write("<cols>");
                    for (int i = 0; i < cols.Length; i++)
                    {
                        int maxLen = s.ColumnName(cols[i]).Length;
                        int sample = Math.Min(s.RowCount, 500);
                        for (int r = 0; r < sample; r++) { string v = s.Cell(r, cols[i]); if (v != null && v.Length > maxLen) maxLen = v.Length; }
                        double width = Math.Min(80, Math.Max(8, maxLen * 1.1 + 2));
                        w.Write("<col min=\"" + (i + 1) + "\" max=\"" + (i + 1) + "\" width=\"" + width.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\" customWidth=\"1\"/>");
                    }
                    w.Write("</cols><sheetData>");
                    StringBuilder b = new StringBuilder();
                    b.Append("<row r=\"1\">");
                    for (int i = 0; i < cols.Length; i++)
                        b.Append("<c r=\"").Append(ColName(i)).Append("1\" t=\"inlineStr\" s=\"1\"><is><t>").Append(X(s.ColumnName(cols[i]))).Append("</t></is></c>");
                    b.Append("</row>");
                    w.Write(b.ToString());
                    for (int r = 0; r < s.RowCount; r++)
                    {
                        b.Length = 0;
                        int rn = r + 2;
                        b.Append("<row r=\"").Append(rn).Append("\">");
                        for (int i = 0; i < cols.Length; i++)
                        {
                            string v = s.Cell(r, cols[i]);
                            if (string.IsNullOrEmpty(v)) continue;
                            if (v.Length > 32000) v = v.Substring(0, 32000);
                            b.Append("<c r=\"").Append(ColName(i)).Append(rn).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">").Append(X(v)).Append("</t></is></c>");
                        }
                        b.Append("</row>");
                        w.Write(b.ToString());
                    }
                    w.Write("</sheetData>");
                    if (s.RowCount > 0) w.Write("<autoFilter ref=\"A1:" + ColName(cols.Length - 1) + (s.RowCount + 1) + "\"/>");
                    w.Write("</worksheet>");
                }
            }
        }

        // ---------------- HTML ----------------
        static void WriteHtml(IExportSource s, int[] cols, string file, string title)
        {
            using (StreamWriter w = new StreamWriter(file, false, new UTF8Encoding(false), 1 << 16))
            {
                w.Write("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
                w.Write("<title>" + X(title) + "</title><style>");
                w.Write(":root{--bg:#f4f6fa;--card:#fff;--ink:#0f172a;--mut:#64748b;--line:#e2e8f0;--acc:#1e3a8a;--head:#1e3a8a;--deny:#b91c1c}");
                w.Write("@media(prefers-color-scheme:dark){:root{--bg:#0b1220;--card:#111a2e;--ink:#e2e8f0;--mut:#94a3b8;--line:#22304d;--acc:#93c5fd;--head:#1e3a8a;--deny:#f87171}}");
                w.Write("*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:13px/1.45 'Segoe UI',system-ui,sans-serif}");
                w.Write("header{padding:22px 28px 10px}h1{margin:0;font-size:20px;font-weight:600}p.meta{margin:4px 0 0;color:var(--mut)}");
                w.Write(".bar{padding:8px 28px}#q{width:min(420px,100%);padding:8px 12px;border:1px solid var(--line);border-radius:8px;background:var(--card);color:var(--ink);font:inherit}");
                w.Write(".wrap{margin:8px 28px 28px;background:var(--card);border:1px solid var(--line);border-radius:10px;overflow:auto;max-height:calc(100vh - 150px)}");
                w.Write("table{border-collapse:collapse;width:100%}th{position:sticky;top:0;background:var(--head);color:#fff;text-align:left;font-weight:600;padding:8px 10px;white-space:nowrap}");
                w.Write("td{padding:6px 10px;border-top:1px solid var(--line);vertical-align:top}tr:nth-child(even) td{background:rgba(127,127,127,.04)}tr.deny td{color:var(--deny)}");
                w.Write("</style></head><body><header><h1>" + X(title) + "</h1><p class=\"meta\">Generado el " + DateTime.Now.ToString("dd/MM/yyyy HH:mm") + " · " + s.RowCount.ToString("N0") + " filas</p></header>");
                w.Write("<div class=\"bar\"><input id=\"q\" placeholder=\"Buscar en la tabla...\" oninput=\"f(this.value)\"></div><div class=\"wrap\"><table><thead><tr>");
                int denyCol = -1;
                for (int i = 0; i < cols.Length; i++)
                {
                    string n = s.ColumnName(cols[i]);
                    if (n == "Tipo de acceso") denyCol = i;
                    w.Write("<th>" + X(n) + "</th>");
                }
                w.Write("</tr></thead><tbody>");
                StringBuilder b = new StringBuilder();
                for (int r = 0; r < s.RowCount; r++)
                {
                    b.Length = 0;
                    bool deny = denyCol >= 0 && s.Cell(r, cols[denyCol]) == "Denegar";
                    b.Append(deny ? "<tr class=\"deny\">" : "<tr>");
                    for (int i = 0; i < cols.Length; i++) b.Append("<td>").Append(X(s.Cell(r, cols[i]))).Append("</td>");
                    b.Append("</tr>");
                    w.Write(b.ToString());
                }
                w.Write("</tbody></table></div><script>function f(q){q=q.toLowerCase();for(const r of document.querySelectorAll('tbody tr'))r.style.display=r.textContent.toLowerCase().includes(q)?'':'none'}</script></body></html>");
            }
        }

        // ---------------- JSON ----------------
        static string J(string v)
        {
            if (v == null) return "\"\"";
            StringBuilder b = new StringBuilder(v.Length + 2);
            b.Append('"');
            foreach (char c in v)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4"));
                        else b.Append(c);
                        break;
                }
            }
            return b.Append('"').ToString();
        }

        static void WriteJson(IExportSource s, int[] cols, string file, string title)
        {
            using (StreamWriter w = new StreamWriter(file, false, new UTF8Encoding(false), 1 << 16))
            {
                string[] keys = new string[cols.Length];
                for (int i = 0; i < cols.Length; i++) keys[i] = J(s.ColumnName(cols[i]));
                w.Write("{\n  \"titulo\": " + J(title) + ",\n  \"generado\": " + J(DateTime.Now.ToString("s")) + ",\n  \"filas\": [\n");
                StringBuilder b = new StringBuilder();
                for (int r = 0; r < s.RowCount; r++)
                {
                    b.Length = 0;
                    b.Append("    {");
                    for (int i = 0; i < cols.Length; i++)
                    {
                        if (i > 0) b.Append(", ");
                        b.Append(keys[i]).Append(": ").Append(J(s.Cell(r, cols[i])));
                    }
                    b.Append(r < s.RowCount - 1 ? "},\n" : "}\n");
                    w.Write(b.ToString());
                }
                w.Write("  ]\n}\n");
            }
        }

        // ---------------- XML ----------------
        static void WriteXml(IExportSource s, int[] cols, string file, string title)
        {
            using (StreamWriter w = new StreamWriter(file, false, new UTF8Encoding(false), 1 << 16))
            {
                w.Write("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Informe titulo=\"" + X(title) + "\" generado=\"" + DateTime.Now.ToString("s") + "\">\n");
                string[] names = new string[cols.Length];
                for (int i = 0; i < cols.Length; i++) names[i] = X(s.ColumnName(cols[i]));
                StringBuilder b = new StringBuilder();
                for (int r = 0; r < s.RowCount; r++)
                {
                    b.Length = 0;
                    b.Append("  <Fila>\n");
                    for (int i = 0; i < cols.Length; i++)
                        b.Append("    <Campo nombre=\"").Append(names[i]).Append("\">").Append(X(s.Cell(r, cols[i]))).Append("</Campo>\n");
                    b.Append("  </Fila>\n");
                    w.Write(b.ToString());
                }
                w.Write("</Informe>\n");
            }
        }
    }
}
