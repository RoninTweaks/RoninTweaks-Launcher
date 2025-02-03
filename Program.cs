using ConsoleApp7;
using System.Diagnostics;
using System.Text;

/// <summary>
/// RoninTweaks Launcher - An open-source application installer and updater
/// This launcher provides a robust download mechanism with chunked downloads,
/// parallel processing, progress visualization, and automatic updates.
/// 
/// Key Features:
/// - Parallel chunk-based downloading with retry mechanism
/// - Real-time progress visualization with color-coded progress bar
/// - Automatic Windows Defender exclusion management
/// - Error handling and user feedback
/// - Self-updating capabilities
/// </summary>
class Program
{
    // HTTP client configuration with reasonable timeout and buffer size limits
    private static readonly HttpClient client = new()
    {
        Timeout = TimeSpan.FromSeconds(60),
        MaxResponseContentBufferSize = 1024 * 1024 * 50  // 50MB max buffer size
    };

    // Download configuration constants
    private const int CHUNK_SIZE = 2 * 1024 * 1024;  // 2MB chunks for optimal download performance
    private const int MAX_PARALLEL_DOWNLOADS = 16;    // Limit parallel downloads to prevent overwhelming the network
    private const int MAX_RETRIES = 3;               // Maximum retry attempts for failed chunk downloads

    // Application configuration
    private static readonly string APP_NAME = "RoninTweaksCLI";
    private static readonly string INSTALL_PATH = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        APP_NAME
    );
    private static readonly string EXE_PATH = Path.Combine(INSTALL_PATH, $"{APP_NAME}.exe");

    // Progress bar configuration
    private const string ProgressBarChars = "█▓▒░";  // Characters used for progress visualization
    private const int ProgressBarWidth = 30;         // Width of the progress bar in console characters
    private static readonly ConsoleColor DefaultColor = Console.ForegroundColor;

    /// <summary>
    /// Downloads the application binary in chunks with parallel processing.
    /// Implements a robust download mechanism with retry logic and progress visualization.
    /// </summary>
    /// <returns>The downloaded file as a byte array, or null if the download fails</returns>
    private static async Task<byte[]?> DownloadFileAsync()
    {
        Console.CursorVisible = false;
        try
        {
            // Display header with application branding
            Console.Clear();
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                     RoninTweaks Launcher                     ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝\n");

            // Get the file size from the server
            long fileSize = long.Parse(await client.GetStringAsync("https://ronintweaks.com/api/RoninTweaksCLIUpdate/size"));
            var result = new byte[fileSize];

            // Calculate chunk boundaries for parallel downloading
            var chunks = Enumerable.Range(0, (int)Math.Ceiling(fileSize / (double)CHUNK_SIZE))
                .Select(i => (Start: i * CHUNK_SIZE, End: Math.Min((i + 1) * CHUNK_SIZE - 1, fileSize - 1)))
                .ToList();

            // Initialize tracking variables for download progress
            var downloadedBytes = new long[chunks.Count];
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var lastUpdateTime = stopwatch.ElapsedMilliseconds;
            var lastDownloadedBytes = 0L;
            var failedChunks = 0;
            var lockObj = new object();

            // Implement parallel downloads with semaphore to limit concurrent connections
            using var semaphore = new SemaphoreSlim(MAX_PARALLEL_DOWNLOADS);
            var tasks = chunks.Select(async (chunk, index) =>
            {
                for (int retry = 0; retry <= MAX_RETRIES; retry++)
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        // Download individual chunk with range request
                        using var response = await client.GetAsync(
                            $"https://ronintweaks.com/api/RoninTweaksCLIUpdate/get/{chunk.Start}/{chunk.End}",
                            HttpCompletionOption.ResponseHeadersRead);

                        if (response.IsSuccessStatusCode)
                        {
                            var chunkData = await response.Content.ReadAsByteArrayAsync();
                            Buffer.BlockCopy(chunkData, 0, result, (int)chunk.Start, chunkData.Length);

                            // Update progress under lock to prevent race conditions
                            lock (lockObj)
                            {
                                downloadedBytes[index] = chunkData.Length;
                                UpdateProgress(
                                    downloadedBytes.Sum(),
                                    fileSize,
                                    stopwatch,
                                    ref lastUpdateTime,
                                    ref lastDownloadedBytes,
                                    failedChunks
                                );
                            }
                            return;
                        }
                    }
                    catch (Exception) when (retry < MAX_RETRIES)
                    {
                        continue;  // Retry on temporary failures
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                    await Task.Delay(100 * (retry + 1));  // Exponential backoff
                }
                Interlocked.Increment(ref failedChunks);
                throw new Exception($"Failed to download chunk {chunk.Start}-{chunk.End}");
            });

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            DisplayCompletionStatus(stopwatch.Elapsed, fileSize);
            return result;
        }
        catch (Exception ex)
        {
            DisplayError(ex);
            return null;
        }
        finally
        {
            Console.CursorVisible = true;
        }
    }

    /// <summary>
    /// Updates the download progress display with current speed and ETA calculations
    /// </summary>
    private static void UpdateProgress(
        long totalDownloaded,
        long totalSize,
        Stopwatch stopwatch,
        ref long lastUpdateTime,
        ref long lastDownloadedBytes,
        int failedChunks)
    {
        var currentTime = stopwatch.ElapsedMilliseconds;
        var timeInterval = currentTime - lastUpdateTime;

        // Update progress display every 100ms to prevent console flickering
        if (timeInterval >= 100)
        {
            var bytesInterval = totalDownloaded - lastDownloadedBytes;
            var speed = bytesInterval * 1000.0 / timeInterval;
            var progress = (double)totalDownloaded / totalSize;

            var remainingBytes = totalSize - totalDownloaded;
            var eta = speed > 0 ? TimeSpan.FromSeconds(remainingBytes / speed) : TimeSpan.Zero;

            DrawProgressBar(progress, speed, eta, totalDownloaded, totalSize, failedChunks);

            lastUpdateTime = currentTime;
            lastDownloadedBytes = totalDownloaded;
        }
    }

    /// <summary>
    /// Draws a color-coded progress bar with download statistics
    /// </summary>
    private static void DrawProgressBar(double progress, double speed, TimeSpan eta, long downloaded, long total, int failedChunks)
    {
        int filledWidth = (int)(progress * ProgressBarWidth);
        var progressBar = new StringBuilder();

        // Create gradient effect in progress bar
        for (int i = 0; i < ProgressBarWidth; i++)
        {
            if (i < filledWidth)
                progressBar.Append(ProgressBarChars[0]);
            else if (i == filledWidth)
                progressBar.Append(ProgressBarChars[1]);
            else if (i == filledWidth + 1)
                progressBar.Append(ProgressBarChars[2]);
            else
                progressBar.Append(ProgressBarChars[3]);
        }

        // Display progress statistics
        Console.SetCursorPosition(0, 5);
        Console.Write($"  Progress: ");
        Console.ForegroundColor = GetProgressColor(progress);
        Console.Write($"{progressBar}");
        Console.ForegroundColor = DefaultColor;
        Console.WriteLine($" {progress:P1}");

        Console.WriteLine($"  Speed:    {FormatSize((long)speed)}/s");
        Console.WriteLine($"  Size:     {FormatSize(downloaded)} / {FormatSize(total)}");
        Console.WriteLine($"  Time:     {FormatTime(eta)} remaining");

        // Display warning for failed chunks
        if (failedChunks > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  Warning:   {failedChunks} chunk(s) failed, retrying...");
            Console.ForegroundColor = DefaultColor;
        }
    }

    /// <summary>
    /// Displays the final download statistics upon successful completion
    /// </summary>
    private static void DisplayCompletionStatus(TimeSpan elapsed, long totalSize)
    {
        Console.SetCursorPosition(0, 5);
        Console.WriteLine(new string(' ', Console.BufferWidth));
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  ✓ Download completed successfully!");
        Console.ForegroundColor = DefaultColor;
        Console.WriteLine($"  • Total size: {FormatSize(totalSize)}");
        Console.WriteLine($"  • Time taken: {FormatDetailedTime(elapsed)}");
        Console.WriteLine($"  • Average speed: {FormatSize((long)(totalSize / elapsed.TotalSeconds))}/s");
        Console.WriteLine();
    }

    /// <summary>
    /// Displays error information when download fails
    /// </summary>
    private static void DisplayError(Exception ex)
    {
        Console.SetCursorPosition(0, 5);
        Console.WriteLine(new string(' ', Console.BufferWidth));
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  ✗ Download failed!");
        Console.ForegroundColor = DefaultColor;
        Console.WriteLine($"  • Error: {ex.Message}");
        Console.WriteLine($"  • Please check your internet connection and try again.");
        Console.WriteLine();
    }

    /// <summary>
    /// Returns appropriate color for progress bar based on completion percentage
    /// </summary>
    private static ConsoleColor GetProgressColor(double progress) =>
        progress switch
        {
            >= 0.9 => ConsoleColor.Green,
            >= 0.6 => ConsoleColor.Cyan,
            >= 0.3 => ConsoleColor.Yellow,
            _ => ConsoleColor.Red
        };

    /// <summary>
    /// Formats byte sizes into human-readable strings with appropriate units
    /// </summary>
    private static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:F2} {sizes[order]}";
    }

    /// <summary>
    /// Formats time spans into concise human-readable strings
    /// </summary>
    private static string FormatTime(TimeSpan timeSpan)
    {
        if (timeSpan.TotalHours >= 1)
            return $"{timeSpan.TotalHours:F1}h";
        if (timeSpan.TotalMinutes >= 1)
            return $"{timeSpan.TotalMinutes:F1}m";
        return $"{timeSpan.TotalSeconds:F0}s";
    }

    /// <summary>
    /// Formats time spans into detailed strings with hours, minutes, and seconds
    /// </summary>
    private static string FormatDetailedTime(TimeSpan timeSpan)
    {
        var parts = new List<string>();
        if (timeSpan.Hours > 0) parts.Add($"{timeSpan.Hours}h");
        if (timeSpan.Minutes > 0) parts.Add($"{timeSpan.Minutes}m");
        parts.Add($"{timeSpan.Seconds}s");
        return string.Join(" ", parts);
    }

    /// <summary>
    /// Adds the application directory to Windows Defender exclusions
    /// Note: Requires administrative privileges
    /// </summary>
    private static async Task AddWindowsDefenderExclusion()
    {
        Console.Title = "RoninTweaks Launcher";
        try
        {
            // Inform the user
            var dialogResult = MessageBox.Show(
                "RoninTweaks requires adding its installation directory to Windows Defender exclusions to prevent false positives.\n\n" +
                $"Installation Path: {INSTALL_PATH}\n\n" +
                "Do you want to proceed with adding this exclusion? (You can remove it later in Windows Defender settings.)",
                "RoninTweaks Suite - Defender Exclusion", 0x04
            );

            // Check the user's response
            if (dialogResult == 6)
            {
                Console.WriteLine("Adding Windows Defender exclusion...");
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command Add-MpPreference -ExclusionPath '{INSTALL_PATH}'",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    Verb = "runas"  // Request administrative privileges
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                }

                MessageBox.Show(
                    "Windows Defender exclusion has been added successfully.",
                    "RoninTweaks Suite"
                );
            }
            else
            {
                MessageBox.Show(
                    "Installation completed without adding Windows Defender exclusions. If you experience issues, you can add the exclusion manually.",
                    "RoninTweaks Suite"
                );
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to add Windows Defender exclusion: {ex.Message}\n\n" +
                "You may need to manually add the exclusion in Windows Defender settings.",
                "RoninTweaks Suite"
            );
        }
    }


    /// <summary>
    /// Handles the installation or update process for the application
    /// Creates necessary directories, downloads the latest version,
    /// and manages file replacement
    /// </summary>
    private static async Task InstallOrUpdate()
    {
        try
        {
            Directory.CreateDirectory(INSTALL_PATH);

            var newBytes = await DownloadFileAsync();
            if (newBytes == null)
            {
                throw new Exception("Failed to download updates");
            }

            // Handle file replacement, checking for running instances
            if (File.Exists(EXE_PATH))
            {
                try
                {
                    File.Delete(EXE_PATH);
                }
                catch (Exception)
                {
                    MessageBox.Show(
                        "Application is currently running. Please close it and try again.",
                        "RoninTweaks Suite"
                    );
                    return;
                }
            }

            await File.WriteAllBytesAsync(EXE_PATH, newBytes);
            await AddWindowsDefenderExclusion();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Installation failed: {ex.Message}",
                "RoninTweaks Suite"
            );
            throw;
        }
    }

    /// <summary>
    /// Main entry point for the launcher
    /// Handles the installation/update process and launches the application
    /// </summary>
    static async Task Main(string[] args)
    {
        Console.Title = "RoninTweaks Launcher";

        if (!Directory.Exists("Data"))
        {
            MessageBox.Show(
                "Welcome to RoninTweaks CLI - Installer\n\n" +
                "🔄 Preparing to install/update application\n\n" +
                "Visit: https://ronintweaks.com",
                "RoninTweaks"
            );
        }

        try
        {
            // Check if installation is needed
            if (!File.Exists(EXE_PATH))
            {
                await InstallOrUpdate();
            }

            // Launch the application
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = EXE_PATH,
                    UseShellExecute = false  // Direct process execution without shell
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to launch application: {ex.Message}",
                    "RoninTweaks Suite"
                );
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"An unexpected error occurred: {ex.Message}",
                "RoninTweaks Suite"
            );
        }
    }
}