using System;
using System.Drawing;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("NTFS Permissions Reporter")]
[assembly: AssemblyProduct("NTFS Permissions Reporter")]
[assembly: AssemblyDescription("Auditoría de permisos NTFS y recursos compartidos")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace NtfsReporter
{
    static class Program
    {
        public static bool IsAdmin;
        public static Icon AppIconCached;

        [STAThread]
        static void Main()
        {
            try { IsAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } catch { }
            // Permite leer los permisos aunque el administrador no tenga acceso a la carpeta (como robocopy /B)
            try { Native.EnablePrivilege("SeBackupPrivilege"); Native.EnablePrivilege("SeRestorePrivilege"); } catch { }
            ThreadPool.SetMinThreads(64, 64);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
                MessageBox.Show("Se ha producido un error inesperado:\n\n" + e.Exception.Message, "NTFS Permissions Reporter", MessageBoxButtons.OK, MessageBoxIcon.Error);

            Theme.Init();
            Settings.Load();
            try { AppIconCached = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            if (AppIconCached == null) AppIconCached = Theme.AppIcon();
            Application.Run(new MainForm());
        }
    }
}
