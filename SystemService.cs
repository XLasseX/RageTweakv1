using System.Diagnostics;
using System.Net.NetworkInformation;

namespace FortniteTweakTool.Services;

public static class SystemService
{
    public static string GetSystemReport()
    {
        var fortniteRunning = Process.GetProcessesByName("FortniteClient-Win64-Shipping").Length > 0;
        var lines = new List<string>();
        lines.Add($"Windows: {Environment.OSVersion}");
        lines.Add($"64-bit OS: {Environment.Is64BitOperatingSystem}");
        lines.Add($"CPU: {Environment.ProcessorCount} logische Prozessoren");
        lines.Add($"RAM: {GetRam()}");
        lines.Add($"GPU: {GetGpuNames()}");
        lines.Add($"Energieplan: {GetPowerPlan()}");
        lines.Add($"Fortnite-Prozess: {(fortniteRunning ? "läuft" : "nicht erkannt")}");
        lines.Add($"Administratorrechte: {IsAdmin()}");
        return string.Join(Environment.NewLine, lines);
    }

    public static string GetRam()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("select TotalPhysicalMemory from Win32_ComputerSystem");
            foreach (System.Management.ManagementBaseObject o in searcher.Get())
            {
                if (o["TotalPhysicalMemory"] is ulong bytes)
                    return $"{bytes / 1024d / 1024d / 1024d:0.0} GB";
            }
            return "Nicht verfügbar";
        }
        catch { return "Nicht verfügbar"; }
    }

    public static string GetGpuNames()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("select Name from Win32_VideoController");
            var names = searcher.Get().Cast<System.Management.ManagementObject>()
                .Select(x => x["Name"]?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x));
            return string.Join(", ", names);
        }
        catch { return "GPU-Abfrage nicht verfügbar"; }
    }

    public static string GetPowerPlan()
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo("powercfg", "/getactivescheme") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            var output = p?.StandardOutput.ReadToEnd().Trim();
            p?.WaitForExit(2000);
            return string.IsNullOrWhiteSpace(output) ? "Nicht verfügbar" : output;
        }
        catch { return "Nicht verfügbar"; }
    }

    public static async Task<(long? ms, string status)> PingAsync(string host = "1.1.1.1")
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 1500);
            return reply.Status == IPStatus.Success
                ? (reply.RoundtripTime, $"{host}: {reply.RoundtripTime} ms (einzelner ICMP-Test)")
                : (null, $"Ping fehlgeschlagen: {reply.Status}");
        }
        catch (Exception ex) { return (null, $"Ping nicht verfügbar: {ex.Message}"); }
    }

    public static bool IsAdmin()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }
}
