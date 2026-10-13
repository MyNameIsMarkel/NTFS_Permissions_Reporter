using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace NtfsReporter
{
    // ------------------------------------------------------------------
    // Modelo del informe
    // ------------------------------------------------------------------
    class RootInfo { public string Path; public string Server; }

    class AceEntry
    {
        public AccountInfo Account;
        public byte Type;   // 0 permitir, 1 denegar, otro = no estandar
        public byte Flags;
        public uint Mask;
        public bool Inherited { get { return (Flags & 0x10) != 0; } }
    }

    class DirNode
    {
        public string Path, Name;
        public DirNode Parent;
        public RootInfo Root;
        public int Depth;
        public List<DirNode> Children;
        public AccountInfo Owner;
        public bool Protected, NullDacl, HasExplicit, IdenticalToParent;
        public AceEntry[] Aces = new AceEntry[0];
        public string Error;
        public string AclKey;
        public bool FilterHit, FilterPath; // usados por la vista de arbol
        string fullList;

        public void AddChild(DirNode c)
        {
            lock (this)
            {
                if (Children == null) Children = new List<DirNode>();
                Children.Add(c);
            }
        }

        public DirNode[] ChildrenSnapshot()
        {
            lock (this) { return Children == null ? new DirNode[0] : Children.ToArray(); }
        }

        public string FullList
        {
            get
            {
                if (fullList != null) return fullList;
                if (Error != null) return fullList = "";
                if (NullDacl) return fullList = "Todos: Control total (sin DACL)";
                StringBuilder b = new StringBuilder();
                foreach (AceEntry a in Aces)
                {
                    if (b.Length > 0) b.Append(" | ");
                    b.Append(a.Type == 1 ? "Denegar " : a.Type == 0 ? "Permitir " : "ACE ");
                    b.Append(a.Account.Name).Append(": ").Append(Sec.Simple(a.Mask));
                    b.Append(" (").Append(Sec.AppliesTo(a.Flags)).Append(a.Inherited ? ", heredado)" : ")");
                }
                return fullList = b.ToString();
            }
        }
    }

    struct RowRef
    {
        public DirNode Dir;
        public int Ace;   // -1 = fila de error / carpeta sin entradas
        public RowRef(DirNode d, int a) { Dir = d; Ace = a; }
        public AceEntry Entry { get { return Ace >= 0 ? Dir.Aces[Ace] : null; } }
    }

    // ------------------------------------------------------------------
    // Campos (columnas y criterios de filtro)
    // ------------------------------------------------------------------
    class FieldDef
    {
        public string Name;
        public string English;
        public bool IsBool;
        public bool DefaultVisible;
        public int Width;
        public Func<RowRef, string> Get;
        public int Index;
    }

    static class Fields
    {
        public static readonly List<FieldDef> All = new List<FieldDef>();
        public static FieldDef Dir, Account, AccessType, Inherited;

        static string YesNo(bool b) { return b ? "Sí" : "No"; }

        static FieldDef Add(string name, string english, int width, bool visible, bool isBool, Func<RowRef, string> get)
        {
            FieldDef f = new FieldDef();
            f.Name = name; f.English = english; f.Width = width; f.DefaultVisible = visible; f.IsBool = isBool; f.Get = get;
            f.Index = All.Count;
            All.Add(f);
            return f;
        }

        static Fields()
        {
            Dir = Add("Directorio", "Directory", 420, true, false, r => r.Dir.Path);
            Account = Add("Nombre de cuenta", "Account name", 230, true, false, r => r.Ace < 0 ? "" : r.Entry.Account.Name);
            Add("Tipo de cuenta", "Account type", 110, true, false, r => r.Ace < 0 ? "" : r.Entry.Account.Type);
            Add("Permiso (simple)", "Permission (simple)", 170, true, false, r => r.Ace < 0 ? "" : Sec.Simple(r.Entry.Mask));
            AccessType = Add("Tipo de acceso", "Permission access type", 90, true, false, r => r.Ace < 0 ? "" : r.Entry.Type == 1 ? "Denegar" : r.Entry.Type == 0 ? "Permitir" : "Otro");
            Add("Se aplica a", "Apply To", 220, true, false, r => r.Ace < 0 ? "" : Sec.AppliesTo(r.Entry.Flags));
            Inherited = Add("Permiso heredado", "Permission is inherited", 80, true, true, r => r.Ace < 0 ? "" : YesNo(r.Entry.Inherited));
            Add("El directorio hereda permisos", "Directory inherits permissions", 90, true, true, r => r.Dir.Error != null ? "" : YesNo(!r.Dir.Protected));
            Add("Permisos idénticos al directorio padre", "Permissions are identical to parent directory", 90, true, true, r => r.Dir.Error != null ? "" : YesNo(r.Dir.IdenticalToParent));
            Add("Permiso (avanzado)", "Permission (advanced)", 320, false, false, r => r.Ace < 0 ? "" : Sec.Advanced(r.Entry.Mask));
            Add("SID de la cuenta", "Account SID", 260, false, false, r => r.Ace < 0 ? "" : r.Entry.Account.SidString);
            Add("Dominio (NetBIOS)", "Domain name (NetBIOS)", 110, false, false, r => r.Ace < 0 ? "" : r.Entry.Account.NetBios);
            Add("Dominio (DNS)", "Domain name (DNS)", 150, false, false, r => r.Ace < 0 ? "" : r.Entry.Account.Dns);
            Add("SID del dominio", "Domain SID", 230, false, false, r => r.Ace < 0 ? "" : r.Entry.Account.DomainSid);
            Add("Cuenta deshabilitada", "Account disabled", 90, false, true, r => r.Ace < 0 ? "" : r.Entry.Account.Disabled);
            Add("Ámbito del grupo", "Group Scope", 110, false, false, r => r.Ace < 0 ? "" : r.Entry.Account.Scope);
            Add("Grupo sin miembros", "Group contains no members", 90, false, true, r => r.Ace < 0 ? "" : r.Entry.Account.EmptyGroup);
            Add("SID sin resolver", "SID cannot be resolved to account name", 80, false, true, r => r.Ace < 0 ? "" : YesNo(r.Entry.Account.Unresolved));
            Add("Lista completa de permisos del directorio", "Directory full permissions list", 500, false, false, r => r.Dir.FullList);
            Add("Propietario", "Owner", 200, false, false, r => r.Dir.Owner != null ? r.Dir.Owner.Name : "");
            Add("Nivel", "Depth", 50, false, false, r => r.Dir.Depth.ToString());
            Add("Error", "Error", 200, true, false, r => r.Dir.Error ?? "");
        }
    }

    // ------------------------------------------------------------------
    // Recorrido paralelo de carpetas
    // ------------------------------------------------------------------
    class ScanItem { public string Path; public int Depth; public object State; }

    class Scanner
    {
        readonly BlockingCollection<ScanItem> queue = new BlockingCollection<ScanItem>();
        readonly CancellationTokenSource cts = new CancellationTokenSource();
        int pending, live;
        public long Folders, Errors;
        public int MaxDepth = int.MaxValue;
        public bool FollowReparse;
        public Func<ScanItem, object> Visit;
        public Action<ScanItem, int> EnumError;
        public Action Completed;

        public void Start(IList<ScanItem> roots, int threads)
        {
            if (roots.Count == 0) { if (Completed != null) Completed(); return; }
            pending = roots.Count;
            foreach (ScanItem r in roots) queue.Add(r);
            live = threads;
            for (int i = 0; i < threads; i++)
            {
                Thread t = new Thread(Worker);
                t.IsBackground = true;
                t.Start();
            }
        }

        public void Cancel() { cts.Cancel(); }
        public bool Cancelled { get { return cts.IsCancellationRequested; } }

        void Worker()
        {
            try
            {
                foreach (ScanItem it in queue.GetConsumingEnumerable(cts.Token))
                {
                    try
                    {
                        object st = Visit(it);
                        Interlocked.Increment(ref Folders);
                        if (it.Depth < MaxDepth && !cts.IsCancellationRequested) Children(it, st);
                    }
                    catch (Exception) { Interlocked.Increment(ref Errors); }
                    finally
                    {
                        if (Interlocked.Decrement(ref pending) == 0) queue.CompleteAdding();
                    }
                }
            }
            catch (OperationCanceledException) { }
            if (Interlocked.Decrement(ref live) == 0 && Completed != null) Completed();
        }

        void Children(ScanItem it, object st)
        {
            Native.WIN32_FIND_DATA fd;
            IntPtr h = Native.FindFirstFileExW(Paths.Long(Paths.Join(it.Path, "*")), 1, out fd, 1, IntPtr.Zero, 2);
            if (h == Native.INVALID_HANDLE)
            {
                int e = Marshal.GetLastWin32Error();
                if (e != 2 && e != 18)
                {
                    Interlocked.Increment(ref Errors);
                    if (EnumError != null) EnumError(new ScanItem { Path = it.Path, Depth = it.Depth, State = st }, e);
                }
                return;
            }
            try
            {
                do
                {
                    if ((fd.dwFileAttributes & 0x10) == 0) continue;
                    string n = fd.cFileName;
                    if (n == "." || n == "..") continue;
                    if ((fd.dwFileAttributes & 0x400) != 0 && !FollowReparse) continue;
                    Interlocked.Increment(ref pending);
                    queue.Add(new ScanItem { Path = Paths.Join(it.Path, n), Depth = it.Depth + 1, State = st });
                } while (Native.FindNextFileW(h, out fd));
            }
            finally { Native.FindClose(h); }
        }
    }

    // ------------------------------------------------------------------
    // Informe de permisos: lectura de cada carpeta
    // ------------------------------------------------------------------
    static class PermissionReader
    {
        public static DirNode Read(ScanItem it, DirNode parent, RootInfo root)
        {
            DirNode n = new DirNode();
            n.Path = it.Path;
            n.Name = it.Depth == 0 ? it.Path : Paths.NameOf(it.Path);
            n.Parent = parent;
            n.Root = root;
            n.Depth = it.Depth;

            int err;
            byte[] sd = Sec.ReadSd(it.Path, out err);
            if (sd == null)
            {
                n.Error = Native.ErrorText(err);
                return n;
            }
            n.Protected = Sec.IsProtected(sd);
            SecurityIdentifier owner = Sec.Owner(sd);
            if (owner != null) n.Owner = Accounts.Get(owner, root.Server);
            bool nullDacl;
            List<RawAce> raw = Sec.Aces(sd, out nullDacl);
            n.NullDacl = nullDacl;
            if (nullDacl)
            {
                AceEntry e = new AceEntry { Account = Accounts.Get(Accounts.Everyone, root.Server), Type = 0, Flags = 3, Mask = Sec.FULL };
                n.Aces = new[] { e };
                n.AclKey = "NULL";
            }
            else
            {
                AceEntry[] arr = new AceEntry[raw.Count];
                List<string> key = new List<string>(raw.Count);
                for (int i = 0; i < raw.Count; i++)
                {
                    RawAce r = raw[i];
                    AceEntry e = new AceEntry();
                    e.Type = r.Type; e.Flags = r.Flags; e.Mask = r.Mask;
                    e.Account = r.Sid != null ? Accounts.Get(r.Sid, root.Server) : new AccountInfo { Name = "(ACE tipo " + r.Type + ")", Type = "Desconocida", Unresolved = true };
                    if (!r.Inherited) n.HasExplicit = true;
                    if (!r.InheritOnly && r.Sid != null)
                        key.Add(r.Sid.Value + "|" + r.Type + "|" + (Sec.MapGeneric(r.Mask) & ~Sec.SYNC).ToString("X"));
                    arr[i] = e;
                }
                key.Sort(StringComparer.Ordinal);
                n.AclKey = string.Join(";", key.ToArray());
                n.Aces = arr;
            }
            n.IdenticalToParent = parent != null && parent.Error == null && parent.AclKey == n.AclKey && !n.HasExplicit;
            return n;
        }
    }

    // ------------------------------------------------------------------
    // Acceso efectivo de un usuario
    // ------------------------------------------------------------------
    class EffRow
    {
        public string Path, Rights, Ntfs, Share, Error;
        public uint Mask;
        public bool Explicit, Protected, Owner;
        public int Depth;
    }

    class EffRoot { public SidSet Set; public bool ShareKnown; public uint ShareMask; public string Server; }
    class EffNode { public EffRoot Root; public uint Parent; public EffNode(EffRoot r, uint p) { Root = r; Parent = p; } }
}
