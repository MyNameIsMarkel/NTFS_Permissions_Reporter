using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.DirectoryServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace NtfsReporter
{
    // Informacion de una cuenta (cacheada por servidor + SID)
    class AccountInfo
    {
        public SecurityIdentifier Sid;
        public string SidString = "";
        public string Name = "";          // DOMINIO\cuenta
        public string NetBios = "";
        public string Dns = "";
        public string DomainSid = "";
        public string Type = "";
        public int Use;
        public string Disabled = "";      // "Sí" / "No" / "" (no aplica)
        public string Scope = "";
        public string EmptyGroup = "";    // "Sí" / "No" / ""
        public bool Unresolved;
        public bool IsGroup { get { return Use == 2 || Use == 4 || Use == 5; } }
    }

    class ShareEntry { public string Name; public string Remark; public bool Hidden; }

    static class Accounts
    {
        public static readonly SecurityIdentifier Everyone = new SecurityIdentifier("S-1-1-0");
        public static readonly SecurityIdentifier AuthUsers = new SecurityIdentifier("S-1-5-11");
        public static readonly SecurityIdentifier Network = new SecurityIdentifier("S-1-5-2");
        public static readonly SecurityIdentifier Interactive = new SecurityIdentifier("S-1-5-4");
        public static readonly SecurityIdentifier BuiltinUsers = new SecurityIdentifier("S-1-5-32-545");

        static readonly ConcurrentDictionary<string, Lazy<AccountInfo>> cache = new ConcurrentDictionary<string, Lazy<AccountInfo>>();

        // ---------------- LDAP ----------------
        static readonly object ldapLock = new object();
        static int ldapState; // 0 sin comprobar, 1 disponible, 2 no disponible
        static DirectoryEntry rootDse; // mantenerlo abierto reutiliza la conexion LDAP

        public static bool LdapAvailable
        {
            get
            {
                if (ldapState != 0) return ldapState == 1;
                lock (ldapLock)
                {
                    if (ldapState != 0) return ldapState == 1;
                    try
                    {
                        rootDse = new DirectoryEntry("LDAP://RootDSE");
                        object nc = rootDse.Properties["defaultNamingContext"].Value;
                        ldapState = nc != null ? 1 : 2;
                    }
                    catch { ldapState = 2; }
                    return ldapState == 1;
                }
            }
        }

        static string DnsFromDn(string dn)
        {
            if (string.IsNullOrEmpty(dn)) return "";
            List<string> parts = new List<string>();
            foreach (string p in dn.Split(','))
            {
                string t = p.Trim();
                if (t.StartsWith("DC=", StringComparison.OrdinalIgnoreCase)) parts.Add(t.Substring(3));
            }
            return string.Join(".", parts.ToArray());
        }

        // ---------------- Resolucion de cuentas ----------------
        public static AccountInfo Get(SecurityIdentifier sid, string server)
        {
            if (sid == null) return Unknown(null);
            string key = (server ?? "").ToUpperInvariant() + "|" + sid.Value;
            Lazy<AccountInfo> lz = cache.GetOrAdd(key, k => new Lazy<AccountInfo>(() => Build(sid, server)));
            return lz.Value;
        }

        static AccountInfo Unknown(SecurityIdentifier sid)
        {
            AccountInfo a = new AccountInfo();
            a.Sid = sid;
            a.SidString = sid != null ? sid.Value : "";
            a.Name = sid != null ? sid.Value : "?";
            a.Type = "Desconocida";
            a.Use = 8;
            a.Unresolved = true;
            return a;
        }

        static bool Lookup(string server, SecurityIdentifier sid, out string name, out string domain, out int use)
        {
            byte[] b = new byte[sid.BinaryLength];
            sid.GetBinaryForm(b, 0);
            StringBuilder n = new StringBuilder(256), d = new StringBuilder(256);
            int cn = 256, cd = 256;
            if (Native.LookupAccountSidW(server, b, n, ref cn, d, ref cd, out use))
            {
                name = n.ToString(); domain = d.ToString(); return true;
            }
            name = null; domain = null; use = 0;
            return false;
        }

        static AccountInfo Build(SecurityIdentifier sid, string server)
        {
            string name, domain; int use;
            bool ok = Lookup(server, sid, out name, out domain, out use);
            if (!ok && server != null) ok = Lookup(null, sid, out name, out domain, out use);
            if (!ok || use == 6 || use == 8) return Unknown(sid);

            AccountInfo a = new AccountInfo();
            a.Sid = sid;
            a.SidString = sid.Value;
            a.Use = use;
            a.NetBios = domain ?? "";
            a.Name = string.IsNullOrEmpty(domain) ? name : domain + "\\" + name;
            try { if (sid.AccountDomainSid != null) a.DomainSid = sid.AccountDomainSid.Value; } catch { }

            string machine = server != null ? server.Split('.')[0] : Environment.MachineName;
            bool builtin = sid.Value.StartsWith("S-1-5-32-");
            bool local = builtin || string.Equals(domain, machine, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(domain, Environment.MachineName, StringComparison.OrdinalIgnoreCase) && server == null;
            bool wellKnown = use == 5 || string.Equals(domain, "NT AUTHORITY", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(domain, "NT SERVICE", StringComparison.OrdinalIgnoreCase) || sid.AccountDomainSid == null && !builtin;

            if (wellKnown && !builtin)
            {
                a.Type = use == 1 ? "Cuenta especial" : "Grupo especial";
                if (use == 1) a.Use = 1;
                a.Scope = "";
                return a;
            }

            if (local)
            {
                if (use == 4 || use == 2 || builtin)
                {
                    a.Type = "Grupo local";
                    a.Scope = builtin ? "Integrado" : "Local";
                    a.EmptyGroup = LocalGroupMembers(server, name).Count == 0 ? "Sí" : "No";
                }
                else
                {
                    a.Type = name.EndsWith("$") ? "Equipo" : "Usuario local";
                    a.Disabled = LocalUserDisabled(server, name);
                }
                return a;
            }

            // Cuenta de dominio
            a.Type = use == 1 ? (name.EndsWith("$") ? "Equipo" : "Usuario") : use == 9 ? "Equipo" : "Grupo";
            if (LdapAvailable)
            {
                try
                {
                    using (DirectoryEntry de = new DirectoryEntry("LDAP://<SID=" + sid.Value + ">"))
                    {
                        de.RefreshCache(new[] { "objectClass", "userAccountControl", "groupType", "distinguishedName" });
                        a.Dns = DnsFromDn(de.Properties["distinguishedName"].Value as string);
                        PropertyValueCollection oc = de.Properties["objectClass"];
                        bool isComputer = oc.Contains("computer"), isGroup = oc.Contains("group"), isUser = oc.Contains("user");
                        if (isComputer) a.Type = "Equipo";
                        else if (isGroup) a.Type = "Grupo";
                        else if (isUser) a.Type = "Usuario";
                        if ((isUser || isComputer) && de.Properties["userAccountControl"].Value != null)
                        {
                            int uac = Convert.ToInt32(de.Properties["userAccountControl"].Value);
                            a.Disabled = (uac & 2) != 0 ? "Sí" : "No";
                        }
                        if (isGroup)
                        {
                            object gtv = de.Properties["groupType"].Value;
                            if (gtv != null)
                            {
                                int gt = Convert.ToInt32(gtv);
                                switch (gt & 0xF)
                                {
                                    case 2: a.Scope = "Global"; break;
                                    case 4: a.Scope = "Dominio local"; break;
                                    case 8: a.Scope = "Universal"; break;
                                    case 1: a.Scope = "Integrado"; break;
                                }
                                if ((gt & unchecked((int)0x80000000)) == 0) a.Scope += " (distribución)";
                            }
                            a.EmptyGroup = GroupIsEmpty(de, sid) ? "Sí" : "No";
                        }
                    }
                }
                catch { }
            }
            return a;
        }

        static bool GroupIsEmpty(DirectoryEntry de, SecurityIdentifier sid)
        {
            // grupos cuyos miembros lo son por "grupo primario" (no aparecen en 'member')
            string v = sid.Value;
            if (v.EndsWith("-513") || v.EndsWith("-514") || v.EndsWith("-515") || v.EndsWith("-516") || v.EndsWith("-521")) return false;
            try
            {
                de.RefreshCache(new[] { "member;range=0-0" });
                foreach (string pn in de.Properties.PropertyNames)
                    if (pn.StartsWith("member", StringComparison.OrdinalIgnoreCase) && de.Properties[pn].Count > 0) return false;
                return true;
            }
            catch
            {
                try { de.RefreshCache(new[] { "member" }); return de.Properties["member"].Count == 0; }
                catch { return false; }
            }
        }

        static string LocalUserDisabled(string server, string user)
        {
            IntPtr buf;
            if (Native.NetUserGetInfo(server, user, 1, out buf) != 0) return "";
            try
            {
                Native.USER_INFO_1 ui = (Native.USER_INFO_1)Marshal.PtrToStructure(buf, typeof(Native.USER_INFO_1));
                return (ui.flags & 2) != 0 ? "Sí" : "No";
            }
            finally { Native.NetApiBufferFree(buf); }
        }

        public static SecurityIdentifier LookupName(string server, string name)
        {
            byte[] sid = new byte[68];
            int cb = sid.Length, cd = 256, use;
            StringBuilder d = new StringBuilder(256);
            if (!Native.LookupAccountNameW(server, name, sid, ref cb, d, ref cd, out use)) return null;
            return new SecurityIdentifier(sid, 0);
        }

        public static List<SecurityIdentifier> LocalGroupMembers(string server, string group)
        {
            List<SecurityIdentifier> r = new List<SecurityIdentifier>();
            IntPtr buf; int read, total; IntPtr resume = IntPtr.Zero;
            if (Native.NetLocalGroupGetMembers(server, group, 0, out buf, -1, out read, out total, ref resume) != 0) return r;
            try
            {
                int sz = Marshal.SizeOf(typeof(Native.LOCALGROUP_MEMBERS_INFO_0));
                for (int i = 0; i < read; i++)
                {
                    Native.LOCALGROUP_MEMBERS_INFO_0 mi = (Native.LOCALGROUP_MEMBERS_INFO_0)Marshal.PtrToStructure(new IntPtr(buf.ToInt64() + i * sz), typeof(Native.LOCALGROUP_MEMBERS_INFO_0));
                    try { r.Add(new SecurityIdentifier(mi.sid)); } catch { }
                }
            }
            finally { Native.NetApiBufferFree(buf); }
            return r;
        }

        static List<string> NameList(IntPtr buf, int read)
        {
            List<string> r = new List<string>();
            int sz = Marshal.SizeOf(typeof(Native.NAME_INFO_0));
            for (int i = 0; i < read; i++)
                r.Add(((Native.NAME_INFO_0)Marshal.PtrToStructure(new IntPtr(buf.ToInt64() + i * sz), typeof(Native.NAME_INFO_0))).name);
            return r;
        }

        // ---------------- Recursos compartidos ----------------
        public static List<ShareEntry> EnumShares(string server, out string error)
        {
            error = null;
            List<ShareEntry> r = new List<ShareEntry>();
            IntPtr buf; int read, total, resume = 0;
            int rc = Native.NetShareEnum(server, 1, out buf, -1, out read, out total, ref resume);
            if (rc != 0 && rc != 234) { error = Native.ErrorText(rc); return r; }
            try
            {
                int sz = Marshal.SizeOf(typeof(Native.SHARE_INFO_1));
                for (int i = 0; i < read; i++)
                {
                    Native.SHARE_INFO_1 si = (Native.SHARE_INFO_1)Marshal.PtrToStructure(new IntPtr(buf.ToInt64() + i * sz), typeof(Native.SHARE_INFO_1));
                    uint baseType = si.type & 0xFF;
                    if (baseType != 0) continue; // solo discos (no impresoras ni IPC$)
                    ShareEntry e = new ShareEntry();
                    e.Name = si.netname; e.Remark = si.remark ?? "";
                    e.Hidden = si.netname.EndsWith("$") || (si.type & 0x80000000) != 0;
                    r.Add(e);
                }
            }
            finally { Native.NetApiBufferFree(buf); }
            r.Sort((x, y) => string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase));
            return r;
        }

        // Descriptor de seguridad de un recurso compartido. byte[0] = sin restricciones; null = error.
        public static byte[] ShareSd(string server, string share, out string error)
        {
            error = null;
            IntPtr buf;
            int rc = Native.NetShareGetInfo(server, share, 502, out buf);
            if (rc != 0) { error = Native.ErrorText(rc); return null; }
            try
            {
                Native.SHARE_INFO_502 si = (Native.SHARE_INFO_502)Marshal.PtrToStructure(buf, typeof(Native.SHARE_INFO_502));
                if (si.security_descriptor == IntPtr.Zero) return new byte[0];
                int len = Native.GetSecurityDescriptorLength(si.security_descriptor);
                byte[] sd = new byte[len];
                Marshal.Copy(si.security_descriptor, sd, 0, len);
                return sd;
            }
            finally { Native.NetApiBufferFree(buf); }
        }

        // ---------------- Usuario para acceso efectivo ----------------
        public static Principal ResolvePrincipal(string input)
        {
            input = input.Trim();
            SecurityIdentifier sid;
            if (input.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase)) sid = new SecurityIdentifier(input);
            else
            {
                try { sid = (SecurityIdentifier)new NTAccount(input).Translate(typeof(SecurityIdentifier)); }
                catch (Exception) { throw new Exception("No se encuentra la cuenta '" + input + "'.\nUsa DOMINIO\\usuario, usuario@dominio o el SID."); }
            }
            AccountInfo ai = Get(sid, null);
            Principal p = new Principal();
            p.Sid = sid;
            p.Name = ai.Name;
            p.IsGroup = ai.IsGroup;
            p.DomainGroups = new List<SecurityIdentifier>();
            bool fromAd = false;
            if (LdapAvailable)
            {
                try
                {
                    using (DirectoryEntry de = new DirectoryEntry("LDAP://<SID=" + sid.Value + ">"))
                    {
                        de.RefreshCache(new[] { "tokenGroups" });
                        foreach (object o in de.Properties["tokenGroups"])
                        {
                            byte[] b = o as byte[];
                            if (b != null) p.DomainGroups.Add(new SecurityIdentifier(b, 0));
                        }
                    }
                    fromAd = true;
                    p.Source = "Active Directory";
                }
                catch { }
            }
            if (!fromAd)
            {
                string shortName = ai.Name.Contains("\\") ? ai.Name.Substring(ai.Name.IndexOf('\\') + 1) : ai.Name;
                if (!p.IsGroup)
                {
                    IntPtr buf; int read, total;
                    if (Native.NetUserGetLocalGroups(null, shortName, 0, 1, out buf, -1, out read, out total) == 0)
                    {
                        try
                        {
                            foreach (string g in NameList(buf, read))
                            {
                                SecurityIdentifier s = LookupName(null, g);
                                if (s != null) p.DomainGroups.Add(s);
                            }
                        }
                        finally { Native.NetApiBufferFree(buf); }
                    }
                }
                p.Source = "cuentas locales";
            }
            p.DomainGroups.RemoveAll(s => s.Value.StartsWith("S-1-5-32-"));
            return p;
        }

        // Grupos locales del servidor que contienen a algun SID del conjunto
        public static List<SecurityIdentifier> ServerLocalGroups(string server, SidSet set, out string warning)
        {
            warning = null;
            List<SecurityIdentifier> r = new List<SecurityIdentifier>();
            IntPtr buf; int read, total; IntPtr resume = IntPtr.Zero;
            int rc = Native.NetLocalGroupEnum(server, 0, out buf, -1, out read, out total, ref resume);
            if (rc != 0)
            {
                warning = "No se pudieron leer los grupos locales de " + (server ?? "este equipo") + " (" + Native.ErrorText(rc) + "); se asume que pertenece a Usuarios.";
                return r;
            }
            List<string> groups;
            try { groups = NameList(buf, read); }
            finally { Native.NetApiBufferFree(buf); }
            foreach (string g in groups)
            {
                foreach (SecurityIdentifier m in LocalGroupMembers(server, g))
                {
                    if (set.Contains(m))
                    {
                        SecurityIdentifier gs = LookupName(server, g);
                        if (gs != null) r.Add(gs);
                        break;
                    }
                }
            }
            return r;
        }
    }

    class Principal
    {
        public string Name;
        public SecurityIdentifier Sid;
        public bool IsGroup;
        public List<SecurityIdentifier> DomainGroups;
        public string Source;
        public readonly List<string> Warnings = new List<string>();
        readonly Dictionary<string, SidSet> perServer = new Dictionary<string, SidSet>(StringComparer.OrdinalIgnoreCase);

        public SidSet SidsFor(string server)
        {
            string key = server ?? "";
            lock (perServer)
            {
                SidSet s;
                if (perServer.TryGetValue(key, out s)) return s;
                s = new SidSet();
                s.Add(Sid);
                foreach (SecurityIdentifier g in DomainGroups) s.Add(g);
                if (!IsGroup)
                {
                    s.Add(Accounts.Everyone);
                    s.Add(Accounts.AuthUsers);
                    s.Add(server != null ? Accounts.Network : Accounts.Interactive);
                }
                string warn;
                List<SecurityIdentifier> local = Accounts.ServerLocalGroups(server, s, out warn);
                if (warn != null)
                {
                    Warnings.Add(warn);
                    if (!IsGroup) s.Add(Accounts.BuiltinUsers);
                }
                foreach (SecurityIdentifier g in local) s.Add(g);
                perServer[key] = s;
                return s;
            }
        }
    }
}
