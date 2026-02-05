namespace arcdps_updater;

internal class Program
{
    static async Task<int> Main()
    {
        Console.WriteLine();
        Console.WriteLine("  arcdps updater");
        Console.WriteLine("  ==============");
        Console.WriteLine();

        // 1. Detect GW2 install path
        var gw2Path = ArcdpsUpdater.FindGw2InstallPath();
        if (gw2Path is null)
        {
            WriteError("Could not find a Guild Wars 2 installation.");
            WriteError("Ensure GW2 is installed, or run this tool from the GW2 directory.");
            return 1;
        }

        WriteSuccess($"Found GW2: {gw2Path}");
        Console.WriteLine();

        // 2. Check if GW2 is running
        if (ArcdpsUpdater.IsGw2Running())
        {
            WriteWarning("Guild Wars 2 is currently running.");
            WriteWarning("Please close the game before updating arcdps (the DLL will be locked).");
            return 1;
        }

        // 3. Fetch remote version + MD5 in parallel
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("arcdps-updater/1.0");

        string remoteVersion;
        string remoteMd5;
        try
        {
            var versionTask = ArcdpsUpdater.FetchRemoteVersion(http);
            var md5Task = ArcdpsUpdater.FetchRemoteMd5(http);
            await Task.WhenAll(versionTask, md5Task);

            remoteVersion = versionTask.Result;
            remoteMd5 = md5Task.Result;
        }
        catch (HttpRequestException ex)
        {
            WriteError($"Failed to fetch update info: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"  Remote version: {remoteVersion}");

        // 4. Compute local MD5 and version
        var localMd5 = ArcdpsUpdater.ComputeLocalMd5(gw2Path);
        var localVersion = ArcdpsUpdater.GetLocalVersion(gw2Path);

        if (localVersion is not null)
            Console.WriteLine($"  Local version:  {localVersion}");
        else if (localMd5 is not null)
            Console.WriteLine($"  Local status:   installed (version unknown)");
        else
            Console.WriteLine($"  Local status:   not installed");

        Console.WriteLine();

        // 5. Compare
        if (localMd5 is not null && string.Equals(localMd5, remoteMd5, StringComparison.OrdinalIgnoreCase))
        {
            WriteSuccess("arcdps is already up to date.");
            return 0;
        }

        // 6. Prompt user
        Console.Write("  Proceed with update? [Y/n] ");
        var input = Console.ReadLine()?.Trim();
        if (input is not null && input.Length > 0 && !input.Equals("y", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("  Update cancelled.");
            return 0;
        }

        Console.WriteLine();

        // 7. Download
        try
        {
            await ArcdpsUpdater.DownloadDll(http, gw2Path);
        }
        catch (HttpRequestException ex)
        {
            WriteError($"Download failed: {ex.Message}");
            ArcdpsUpdater.CleanupTemp(gw2Path);
            return 1;
        }
        catch (IOException ex)
        {
            WriteError($"Download failed (IO): {ex.Message}");
            ArcdpsUpdater.CleanupTemp(gw2Path);
            return 1;
        }

        // 8. Verify MD5
        var tempPath = ArcdpsUpdater.GetTempDllPath(gw2Path);
        if (!ArcdpsUpdater.VerifyMd5(tempPath, remoteMd5))
        {
            WriteError("MD5 verification failed — downloaded file does not match expected hash.");
            WriteError("The download may be corrupted. Please try again.");
            ArcdpsUpdater.CleanupTemp(gw2Path);
            return 1;
        }

        WriteSuccess("MD5 verified.");

        // 9. Backup and replace
        try
        {
            ArcdpsUpdater.ReplaceWithBackup(gw2Path);
        }
        catch (UnauthorizedAccessException)
        {
            WriteError("Access denied. Try running as administrator.");
            ArcdpsUpdater.CleanupTemp(gw2Path);
            return 1;
        }
        catch (IOException ex)
        {
            WriteError($"Failed to replace DLL: {ex.Message}");
            ArcdpsUpdater.CleanupTemp(gw2Path);
            return 1;
        }

        // 10. Done
        Console.WriteLine();
        WriteSuccess($"arcdps updated to {remoteVersion}.");

        if (File.Exists(ArcdpsUpdater.GetBackupDllPath(gw2Path)))
            Console.WriteLine($"  Previous version backed up to {ArcdpsUpdater.GetBackupDllPath(gw2Path)}");

        Console.WriteLine();
        return 0;
    }

    private static void WriteSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  {message}");
        Console.ResetColor();
    }

    private static void WriteWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  {message}");
        Console.ResetColor();
    }

    private static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  {message}");
        Console.ResetColor();
    }
}
