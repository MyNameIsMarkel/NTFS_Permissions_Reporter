using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace NtfsReporter
{
    // ------------------------------------------------------------------
    // Rutas
    // ------------------------------------------------------------------
    static class Paths
    {
        public static string Long(string p)
        {
            if (p.StartsWith(@"\\?\")) return p;
            if (p.StartsWith(@"\\")) return @"\\?\UNC\" + p.Substring(2);
            return @"\\?\" + p;
        }

        public static string Normalize(string p)
        {
            if (p == null) return "";
            p = p.Trim().Trim('"').Trim().Replace('/', '\\');
            while (p.Length > 3 && p.EndsWith("\\")) p = p.Substring(0, p.Length - 1);
            if (p.Length == 2 && p[1] == ':') p += "\\";
            return p;
        }

        public static string Join(string dir, string name)
        {
            return dir.EndsWith("\\") ? dir + name : dir + "\\" + name;
        }

        public static bool IsUnc(string p) { return p.StartsWith(@"\\"); }

        public static bool TryParseUnc(string p, out string server, out string share)
        {
            server = null; share = null;
            if (!IsUnc(p)) return false;
            string[] parts = p.Substring(2).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return false;
            server = parts[0]; share = parts[1];
            return true;
        }

        public static string ServerOf(string p)
        {
            string s, sh;
            return TryParseUnc(p, out s, out sh) ? s : null;
        }

        public static string NameOf(string p)
        {
            string t = p.TrimEnd('\\');
            int i = t.LastIndexOf('\\');
            return i >= 0 && i < t.Length - 1 ? t.Substring(i + 1) : p;
        }

        public static List<string> SplitLinesSafe(string text)
        {
            List<string> r = new List<string>();
            if (string.IsNullOrEmpty(text)) return r;
            foreach (string line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string n = Normalize(line);
                if (n.Length > 0) r.Add(n);
            }
            return r;
        }

        public static bool LooksValid(string p)
        {
            if (p.Length < 3) return false;
            if (IsUnc(p)) { string s, sh; return TryParseUnc(p, out s, out sh); }
            return char.IsLetter(p[0]) && p[1] == ':' && p[2] == '\\';
        }
    }

    // ------------------------------------------------------------------
    // Conjunto de SIDs (busqueda directa sobre bytes, sin asignar memoria)
    // ------------------------------------------------------------------
    class SidSet
    {
        readonly Dictionary<ulong, List<byte[]>> map = new Dictionary<ulong, List<byte[]>>();
        public readonly List<SecurityIdentifier> Items = new List<SecurityIdentifier>();

        static ulong Hash(byte[] b, int off, int len)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < len; i++) { h ^= b[off + i]; h *= 1099511628211UL; }
            return h;
        }

        public bool Add(SecurityIdentifier sid)
        {
            if (sid == null) return false;
            byte[] b = new byte[sid.BinaryLength];
            sid.GetBinaryForm(b, 0);
            if (Contains(b, 0)) return false;
            ulong h = Hash(b, 0, b.Length);
            List<byte[]> l;
            if (!map.TryGetValue(h, out l)) { l = new List<byte[]>(1); map[h] = l; }
            l.Add(b);
            Items.Add(sid);
            return true;
        }

        public bool Contains(SecurityIdentifier sid)
        {
            if (sid == null) return false;
            byte[] b = new byte[sid.BinaryLength];
            sid.GetBinaryForm(b, 0);
            return Contains(b, 0);
        }

        public bool Contains(byte[] buf, int off)
        {
            if (off + 8 > buf.Length) return false;
            int len = 8 + 4 * buf[off + 1];
            if (off + len > buf.Length) return false;
            List<byte[]> l;
            if (!map.TryGetValue(Hash(buf, off, len), out l)) return false;
            foreach (byte[] c in l)
            {
                if (c.Length != len) continue;
                bool eq = true;
                for (int i = 0; i < len; i++) if (c[i] != buf[off + i]) { eq = false; break; }
                if (eq) return true;
            }
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Descriptores de seguridad
    // ------------------------------------------------------------------
    class RawAce
    {
        public byte Type;     // 0 permitir, 1 denegar, otros = no estandar
        public byte Flags;
        public uint Mask;
        public SecurityIdentifier Sid;
        public bool Inherited { get { return (Flags & 0x10) != 0; } }
        public bool InheritOnly { get { return (Flags & 0x08) != 0; } }
    }

    static class Sec
    {
        public const uint OWNER = 1, DACL = 4;
        public const uint FULL = 0x1F01FF;
        public const uint SYNC = 0x100000;
        [ThreadStatic] static byte[] tlsBuf;

        // Devuelve un buffer reutilizado por hilo: hay que usarlo antes de la siguiente llamada.
        public static byte[] ReadSd(string path, out int err)
        {
            byte[] buf = tlsBuf;
            if (buf == null) { buf = new byte[8192]; tlsBuf = buf; }
            uint need;
            string lp = Paths.Long(path);
            if (Native.GetFileSecurityW(lp, OWNER | DACL, buf, (uint)buf.Length, out need)) { err = 0; return buf; }
            err = Marshal.GetLastWin32Error();
            if (err == 122 && need > buf.Length)
            {
                buf = new byte[need]; tlsBuf = buf;
                if (Native.GetFileSecurityW(lp, OWNER | DACL, buf, (uint)buf.Length, out need)) { err = 0; return buf; }
                err = Marshal.GetLastWin32Error();
            }
            return null;
        }

        public static uint MapGeneric(uint m)
        {
            if ((m & 0x80000000) != 0) m |= 0x120089;
            if ((m & 0x40000000) != 0) m |= 0x120116;
            if ((m & 0x20000000) != 0) m |= 0x1200A0;
            if ((m & 0x10000000) != 0) m |= FULL;
            return m & FULL;
        }

        static int I32(byte[] b, int o) { return b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24); }
        static int U16(byte[] b, int o) { return b[o] | (b[o + 1] << 8); }

        public static bool IsProtected(byte[] sd) { return (U16(sd, 2) & 0x1000) != 0; }

        public static SecurityIdentifier Owner(byte[] sd)
        {
            int o = I32(sd, 4);
            if (o <= 0) return null;
            try { return new SecurityIdentifier(sd, o); } catch { return null; }
        }

        // Acceso efectivo de un conjunto de SIDs (misma logica que AccessCheck: ACE en orden)
        public static uint Effective(byte[] sd, SidSet set, out bool hasExplicit, out bool prot, out bool isOwner)
        {
            hasExplicit = false;
            int control = U16(sd, 2);
            prot = (control & 0x1000) != 0;
            int offOwner = I32(sd, 4);
            isOwner = offOwner > 0 && set.Contains(sd, offOwner);
            int offDacl = I32(sd, 16);
            if ((control & 0x4) == 0 || offDacl == 0) return FULL;
            int count = U16(sd, offDacl + 4);
            int p = offDacl + 8;
            uint granted = 0, denied = 0;
            for (int i = 0; i < count && p + 8 <= sd.Length; i++)
            {
                int type = sd[p], flags = sd[p + 1], size = U16(sd, p + 2);
                if (size < 8) break;
                if (type == 0 || type == 1)
                {
                    if ((flags & 0x10) == 0) hasExplicit = true;
                    if ((flags & 0x08) == 0 && set.Contains(sd, p + 8))
                    {
                        uint m = MapGeneric((uint)I32(sd, p + 4));
                        if (type == 0) granted |= m & ~denied;
                        else denied |= m & ~granted;
                    }
                }
                p += size;
            }
            return granted;
        }

        public static uint Effective(byte[] sd, SidSet set)
        {
            bool a, b, c;
            return Effective(sd, set, out a, out b, out c);
        }

        public static List<RawAce> Aces(byte[] sd, out bool nullDacl)
        {
            List<RawAce> r = new List<RawAce>();
            int control = U16(sd, 2);
            int offDacl = I32(sd, 16);
            nullDacl = (control & 0x4) == 0 || offDacl == 0;
            if (nullDacl) return r;
            int count = U16(sd, offDacl + 4);
            int p = offDacl + 8;
            for (int i = 0; i < count && p + 8 <= sd.Length; i++)
            {
                int size = U16(sd, p + 2);
                if (size < 8) break;
                RawAce a = new RawAce();
                a.Type = sd[p]; a.Flags = sd[p + 1]; a.Mask = (uint)I32(sd, p + 4);
                if (a.Type == 0 || a.Type == 1 || a.Type == 9 || a.Type == 10)
                {
                    try { a.Sid = new SecurityIdentifier(sd, p + 8); } catch { }
                }
                r.Add(a);
                p += size;
            }
            return r;
        }

        // ---------------- Textos ----------------
        static readonly Dictionary<uint, string> simpleCache = new Dictionary<uint, string>();
        static readonly Dictionary<uint, string> advCache = new Dictionary<uint, string>();
        static readonly string[] appliesCache = new string[32];

        public static string Simple(uint mask)
        {
            lock (simpleCache)
            {
                string s;
                if (simpleCache.TryGetValue(mask, out s)) return s;
                s = BuildSimple(mask);
                simpleCache[mask] = s;
                return s;
            }
        }

        static string BuildSimple(uint m)
        {
            m = MapGeneric(m) & ~SYNC;
            if (m == 0) return "Sin acceso";
            const uint F = FULL & ~SYNC, MOD = 0x0301BF, RX = 0x0200A9, R = 0x020089, W = 0x000116;
            if ((m & F) == F) return "Control total";
            List<string> parts = new List<string>();
            uint covered = 0;
            if ((m & MOD) == MOD) { parts.Add("Modificar"); covered |= MOD; }
            else
            {
                if ((m & RX) == RX) { parts.Add("Lectura y ejecución"); covered |= RX; }
                else if ((m & R) == R) { parts.Add("Leer"); covered |= R; }
                if ((m & W) == W) { parts.Add("Escribir"); covered |= W; }
            }
            if ((m & ~covered) != 0) parts.Add("Especial");
            return string.Join(", ", parts.ToArray());
        }

        static readonly uint[] advBits = { 0x1, 0x2, 0x4, 0x8, 0x10, 0x20, 0x40, 0x80, 0x100, 0x10000, 0x20000, 0x40000, 0x80000 };
        static readonly string[] advNames = {
            "Mostrar carpeta / leer datos", "Crear archivos / escribir datos", "Crear carpetas / anexar datos",
            "Leer atributos extendidos", "Escribir atributos extendidos", "Recorrer carpeta / ejecutar archivo",
            "Eliminar subcarpetas y archivos", "Leer atributos", "Escribir atributos", "Eliminar",
            "Leer permisos", "Cambiar permisos", "Tomar posesión" };

        public static string Advanced(uint mask)
        {
            lock (advCache)
            {
                string s;
                if (advCache.TryGetValue(mask, out s)) return s;
                uint m = MapGeneric(mask);
                List<string> l = new List<string>();
                for (int i = 0; i < advBits.Length; i++) if ((m & advBits[i]) != 0) l.Add(advNames[i]);
                s = l.Count == 0 ? "Ninguno" : string.Join(", ", l.ToArray());
                advCache[mask] = s;
                return s;
            }
        }

        public static string AppliesTo(int flags)
        {
            int key = flags & 0x0F;
            string c = appliesCache[key];
            if (c != null) return c;
            int f = flags & 0x0B;
            string s;
            switch (f)
            {
                case 0: s = "Solo esta carpeta"; break;
                case 3: s = "Esta carpeta, subcarpetas y archivos"; break;
                case 2: s = "Esta carpeta y subcarpetas"; break;
                case 1: s = "Esta carpeta y archivos"; break;
                case 11: s = "Solo subcarpetas y archivos"; break;
                case 10: s = "Solo subcarpetas"; break;
                case 9: s = "Solo archivos"; break;
                default: s = "Ninguno"; break;
            }
            if ((flags & 0x04) != 0) s += " (solo un nivel)";
            appliesCache[key] = s;
            return s;
        }

        public static string ShareName(uint m)
        {
            m &= FULL;
            if (m == FULL) return "Control total";
            if ((m & 0x1301BF) == 0x1301BF) return "Cambiar";
            if ((m & 0x1200A9) == 0x1200A9) return "Leer";
            if (m == 0) return "Sin acceso";
            return Simple(m);
        }
    }
}
