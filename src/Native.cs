using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NtfsReporter
{
    static class Native
    {
        public static readonly IntPtr INVALID_HANDLE = new IntPtr(-1);

        // ---------------- Ficheros ----------------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WIN32_FIND_DATA
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindFirstFileExW(string lpFileName, int fInfoLevelId, out WIN32_FIND_DATA lpFindFileData,
            int fSearchOp, IntPtr lpSearchFilter, int dwAdditionalFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool FindNextFileW(IntPtr hFindFile, out WIN32_FIND_DATA lpFindFileData);

        [DllImport("kernel32.dll")]
        public static extern bool FindClose(IntPtr hFindFile);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool GetFileSecurityW(string lpFileName, uint RequestedInformation, byte[] pSecurityDescriptor,
            uint nLength, out uint lpnLengthNeeded);

        // ---------------- Cuentas ----------------
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool LookupAccountSidW(string lpSystemName, byte[] Sid, StringBuilder lpName, ref int cchName,
            StringBuilder ReferencedDomainName, ref int cchReferencedDomainName, out int peUse);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool LookupAccountNameW(string lpSystemName, string lpAccountName, byte[] Sid, ref int cbSid,
            StringBuilder ReferencedDomainName, ref int cchReferencedDomainName, out int peUse);

        [DllImport("advapi32.dll")]
        public static extern int GetSecurityDescriptorLength(IntPtr pSecurityDescriptor);

        // ---------------- Privilegios ----------------
        [StructLayout(LayoutKind.Sequential)]
        public struct TOKEN_PRIVILEGES { public int Count; public long Luid; public int Attr; }

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr h, int acc, out IntPtr phtok);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool LookupPrivilegeValueW(string host, string name, ref long pluid);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool AdjustTokenPrivileges(IntPtr htok, bool disall, ref TOKEN_PRIVILEGES newst, int len, IntPtr prev, IntPtr relen);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);

        public static bool EnablePrivilege(string name)
        {
            IntPtr tok;
            if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008, out tok)) return false;
            try
            {
                TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES();
                tp.Count = 1; tp.Attr = 2;
                if (!LookupPrivilegeValueW(null, name, ref tp.Luid)) return false;
                if (!AdjustTokenPrivileges(tok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero)) return false;
                return Marshal.GetLastWin32Error() == 0;
            }
            finally { CloseHandle(tok); }
        }

        // ---------------- NetAPI ----------------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHARE_INFO_1 { public string netname; public uint type; public string remark; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHARE_INFO_502
        {
            public string netname; public int type; public string remark; public int permissions; public int max_uses;
            public int current_uses; public string path; public string passwd; public int reserved; public IntPtr security_descriptor;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct NAME_INFO_0 { public string name; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct USER_INFO_1
        {
            public string name; public string password; public int password_age; public int priv;
            public string home_dir; public string comment; public int flags; public string script_path;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LOCALGROUP_MEMBERS_INFO_0 { public IntPtr sid; }

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        public static extern int NetShareEnum(string server, int level, out IntPtr buf, int prefmaxlen,
            out int entriesread, out int totalentries, ref int resume);

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        public static extern int NetShareGetInfo(string server, string share, int level, out IntPtr buf);

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        public static extern int NetLocalGroupEnum(string server, int level, out IntPtr buf, int prefmaxlen,
            out int entriesread, out int totalentries, ref IntPtr resume);

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        public static extern int NetLocalGroupGetMembers(string server, string group, int level, out IntPtr buf, int prefmaxlen,
            out int entriesread, out int totalentries, ref IntPtr resume);

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        public static extern int NetUserGetLocalGroups(string server, string user, int level, int flags, out IntPtr buf,
            int prefmaxlen, out int entriesread, out int totalentries);

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        public static extern int NetUserGetInfo(string server, string user, int level, out IntPtr buf);

        [DllImport("netapi32.dll")]
        public static extern int NetApiBufferFree(IntPtr buf);

        // ---------------- UI ----------------
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hWnd, string appName, string partList);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, string lp);

        public const int EM_SETCUEBANNER = 0x1501;

        public static void SetCue(System.Windows.Forms.TextBox tb, string text)
        {
            try { SendMessage(tb.Handle, EM_SETCUEBANNER, new IntPtr(1), text); } catch { }
        }

        public static void ExplorerTheme(System.Windows.Forms.Control c)
        {
            try { SetWindowTheme(c.Handle, "explorer", null); } catch { }
        }

        public static string ErrorText(int code)
        {
            switch (code)
            {
                case 5: return "Acceso denegado";
                case 2: case 3: return "No encontrada";
                case 53: return "Ruta de red no encontrada";
                case 64: case 59: return "Error de red";
                case 67: return "Recurso compartido no encontrado";
                case 1326: return "Usuario o contraseña incorrectos";
                case 1219: return "Conflicto de credenciales con otra conexión";
                case 2221: return "Usuario no encontrado";
                default: return new System.ComponentModel.Win32Exception(code).Message + " (" + code + ")";
            }
        }
    }
}
