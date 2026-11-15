using System.Diagnostics;
using Microsoft.UI.Xaml;

namespace Simplex.Services;

public static class AdminRestart
{
    public static void Restart()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe == null) return;
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Exit();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Benutzer hat die UAC-Abfrage abgelehnt.
        }
        catch (Exception ex)
        {
            Log.Write("Admin-Neustart: " + ex.Message);
        }
    }
}
