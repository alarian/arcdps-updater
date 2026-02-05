using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace arcdps_updater;

internal static class ArcdpsUpdater
{
    private const string BaseUrl = "https://www.deltaconnected.com/arcdps/x64/";
    private const string DllName = "d3d11.dll";
    private const string TempDllName = "d3d11.dll.tmp";
    private const string BackupDllName = "d3d11.dll.backup";

    public static string? FindGw2InstallPath()
    {
        // 1. ArenaNet registry key (64-bit and 32-bit views)
        string? path = ReadRegistryPath(
            @"SOFTWARE\ArenaNet\Guild Wars 2", "Path",
            RegistryView.Registry64, RegistryView.Registry32);
        if (path is not null) return path;

        // 2. Steam registry key
        path = ReadRegistryPath(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1284210", "InstallLocation",
            RegistryView.Registry64, RegistryView.Registry32);
        if (path is not null) return path;

        // 3. Common install paths
        string[] commonPaths =
        [
            @"C:\Program Files\Guild Wars 2",
            @"C:\Program Files (x86)\Guild Wars 2",
            @"D:\Program Files\Guild Wars 2",
            @"D:\Program Files (x86)\Guild Wars 2",
            @"D:\Guild Wars 2",
        ];

        foreach (var candidate in commonPaths)
        {
            if (IsValidGw2Directory(candidate))
                return candidate;
        }

        // 4. Running process
        try
        {
            var processes = Process.GetProcessesByName("Gw2-64");
            foreach (var proc in processes)
            {
                try
                {
                    var exePath = proc.MainModule?.FileName;
                    if (exePath is not null)
                    {
                        var dir = Path.GetDirectoryName(exePath);
                        if (dir is not null && IsValidGw2Directory(dir))
                            return dir;
                    }
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch
        {
            // Process access may fail; continue
        }

        return null;
    }

    public static bool IsGw2Running()
    {
        var processes = Process.GetProcessesByName("Gw2-64");
        bool running = processes.Length > 0;
        foreach (var p in processes) p.Dispose();
        return running;
    }

    public static async Task<string> FetchRemoteVersion(HttpClient http)
    {
        var response = await http.GetStringAsync(BaseUrl + DllName + ".version");
        return response.Trim();
    }

    public static async Task<string> FetchRemoteMd5(HttpClient http)
    {
        var response = await http.GetStringAsync(BaseUrl + DllName + ".md5sum");
        // Format: "<hash>  <filename>"
        var hash = response.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return hash.ToLowerInvariant();
    }

    public static string? ComputeLocalMd5(string gw2Path)
    {
        var dllPath = Path.Combine(gw2Path, DllName);
        if (!File.Exists(dllPath))
            return null;

        var bytes = MD5.HashData(File.ReadAllBytes(dllPath));
        return Convert.ToHexStringLower(bytes);
    }

    public static string? GetLocalVersion(string gw2Path)
    {
        var dllPath = Path.Combine(gw2Path, DllName);
        if (!File.Exists(dllPath))
            return null;

        var info = FileVersionInfo.GetVersionInfo(dllPath);
        return info.FileVersion;
    }

    public static async Task DownloadDll(HttpClient http, string gw2Path)
    {
        var tempPath = Path.Combine(gw2Path, TempDllName);

        using var response = await http.GetAsync(BaseUrl + DllName, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;

        await using var contentStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);

        var buffer = new byte[81920];
        long downloaded = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
            downloaded += bytesRead;

            if (totalBytes.HasValue)
            {
                var pct = (double)downloaded / totalBytes.Value * 100;
                Console.Write($"\r  Downloading: {pct:F0}% ({downloaded:N0} / {totalBytes.Value:N0} bytes)");
            }
            else
            {
                Console.Write($"\r  Downloading: {downloaded:N0} bytes");
            }
        }

        Console.WriteLine();
    }

    public static bool VerifyMd5(string filePath, string expectedMd5)
    {
        var bytes = MD5.HashData(File.ReadAllBytes(filePath));
        var actual = Convert.ToHexStringLower(bytes);
        return string.Equals(actual, expectedMd5, StringComparison.OrdinalIgnoreCase);
    }

    public static void ReplaceWithBackup(string gw2Path)
    {
        var targetPath = Path.Combine(gw2Path, DllName);
        var tempPath = Path.Combine(gw2Path, TempDllName);
        var backupPath = Path.Combine(gw2Path, BackupDllName);

        if (File.Exists(targetPath))
        {
            // Remove old backup if it exists
            if (File.Exists(backupPath))
                File.Delete(backupPath);

            File.Move(targetPath, backupPath);
        }

        File.Move(tempPath, targetPath);
    }

    public static void CleanupTemp(string gw2Path)
    {
        var tempPath = Path.Combine(gw2Path, TempDllName);
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    public static string GetDllPath(string gw2Path) => Path.Combine(gw2Path, DllName);
    public static string GetTempDllPath(string gw2Path) => Path.Combine(gw2Path, TempDllName);
    public static string GetBackupDllPath(string gw2Path) => Path.Combine(gw2Path, BackupDllName);

    private static bool IsValidGw2Directory(string path)
    {
        return Directory.Exists(path) && File.Exists(Path.Combine(path, "Gw2-64.exe"));
    }

    private static string? ReadRegistryPath(string subKey, string valueName, params RegistryView[] views)
    {
        foreach (var view in views)
        {
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = hklm.OpenSubKey(subKey);
                var value = key?.GetValue(valueName) as string;
                if (value is null) continue;

                // Registry value may point to the exe rather than the directory
                if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    value = Path.GetDirectoryName(value);

                if (value is not null && IsValidGw2Directory(value))
                    return value;
            }
            catch
            {
                // Registry access may fail; try next view
            }
        }

        return null;
    }
}
