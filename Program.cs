using Borderless1942;
using System.Diagnostics;

var monitorBounds = Win32Extensions.GetPrimaryMonitor().GetBounds();
var defaultWidth = monitorBounds.Width;
var defaultHeight = monitorBounds.Height;
var width = defaultWidth;
var height = defaultHeight;
var skipConfigEdits = false;

List<string> additionalArgs = new();

for (int i = 0; i < args.Length; i++)
{
	if (args[i].Equals("-width", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
	{
		if (int.TryParse(args[i + 1], out int parsedWidth))
		{
			width = parsedWidth;
		}
		// Skip the value so it isn't passed on to BF1942
		i++;
	}
	else if (args[i].Equals("-height", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
	{
		if (int.TryParse(args[i + 1], out int parsedHeight))
		{
			height = parsedHeight;
		}
		// Skip the value so it isn't passed on to BF1942
		i++;
	}
	else if (args[i].Equals("-noedit", StringComparison.OrdinalIgnoreCase))
	{
		skipConfigEdits = true;
	}
	else
		additionalArgs.Add(args[i]);
}

if (!skipConfigEdits)
{
	// Update Video.con files if they exist
	var modsPath = Path.Combine(Environment.CurrentDirectory, "Mods", "bf1942", "Settings");
	if (Directory.Exists(modsPath))
	{
		// Update resolution in profile Video.con files
		var videoConFiles = Directory.GetFiles(Path.Combine(modsPath, "Profiles"), "Video.con", SearchOption.AllDirectories);
		foreach (var videoConFile in videoConFiles)
		{
			var lines = File.ReadAllLines(videoConFile);
			for (int i = 0; i < lines.Length; i++)
			{
				if (lines[i].StartsWith("game.setGameDisplayMode"))
				{
					// Keep the user's chosen refresh rate, only the resolution needs to match the monitor
					var parts = lines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
					var refreshRate = parts.Length > 4 && int.TryParse(parts[4], out var parsedRefreshRate) ? parsedRefreshRate : 60;
					lines[i] = $"game.setGameDisplayMode {width} {height} 32 {refreshRate}";
				}
			}
			File.WriteAllLines(videoConFile, lines);
			Log($"[Updated resolution in {videoConFile} to {width}x{height}]");
		}

		// Update fullscreen setting in VideoDefault.con
		var videoDefaultPath = Path.Combine(modsPath, "VideoDefault.con");
		if (File.Exists(videoDefaultPath))
		{
			var lines = File.ReadAllLines(videoDefaultPath);
			for (int i = 0; i < lines.Length; i++)
			{
				if (lines[i].StartsWith("renderer.setFullScreen"))
				{
					lines[i] = "renderer.setFullScreen 0";
				}
			}
			File.WriteAllLines(videoDefaultPath, lines);
			Log($"[Updated fullscreen setting in VideoDefault.con]");
		}
	}
}

// Start Process
var processStartInfo = new ProcessStartInfo
{
	FileName = @"BF1942.exe",
};

foreach (var arg in additionalArgs)
	processStartInfo.ArgumentList.Add(arg);

var process = Process.Start(processStartInfo)!;
Log($"[BF1942 Process Has Started] [{process.Id}]");

// BF1942 restarts itself (eg. when the server changes map or mod) by starting a new BF1942.exe and then exiting.
// The old process can still be running when the new one opens its window, and DataField42 can run in between
// while it downloads maps, so always follow the newest running BF1942 process instead of waiting for a single one.
var exitTimeout = TimeSpan.FromSeconds(15);
var lastSeenRunning = DateTime.UtcNow;
var nextProcessScan = DateTime.MinValue;
Window? window = null;

while (true)
{
	var isRunning = process.IsRunning();
	if (!isRunning || DateTime.UtcNow >= nextProcessScan)
	{
		nextProcessScan = DateTime.UtcNow.AddSeconds(1);
		var newestProcess = FindNewestGameProcess();
		if (newestProcess is not null && newestProcess.Id != process.Id)
		{
			Log($"[BF1942 Process Has Changed] [{process.Id} -> {newestProcess.Id}]");
			process.Dispose();
			process = newestProcess;
			window = null;
			isRunning = true;
		}
		else
		{
			newestProcess?.Dispose();
		}
	}

	if (isRunning)
	{
		lastSeenRunning = DateTime.UtcNow;

		// Re-read the main window every time as BF1942 can recreate it, and reapply the window style
		// as the game can add the borders back when it changes display mode
		var handle = process.GetMainWindowHandle();
		if (handle != 0)
		{
			if (window?.Handle != handle)
			{
				window = new Window(handle);
				Log($"[BF1942 Window Found] [{process.Id}] [0x{handle:X}]");
			}

			window.Value.RemoveBorders();
			UpdateWindowPosition(window.Value, width, height);
		}
	}
	else if (Process.GetProcessesByName("DataField42").Length > 0)
	{
		// DataField42 starts BF1942 again once it has finished syncing files
		lastSeenRunning = DateTime.UtcNow;
	}
	else if (DateTime.UtcNow - lastSeenRunning > exitTimeout)
	{
		Log("[BF1942 Process Has Exited]");
		break;
	}

	await Task.Delay(100);
}

static Process? FindNewestGameProcess()
{
	Process? newestProcess = null;
	var newestStartTime = DateTime.MinValue;
	foreach (var candidate in Process.GetProcessesByName("BF1942"))
	{
		var startTime = candidate.GetStartTime();
		if (startTime is not null && startTime > newestStartTime && candidate.IsRunning())
		{
			newestProcess?.Dispose();
			newestProcess = candidate;
			newestStartTime = startTime.Value;
		}
		else
		{
			candidate.Dispose();
		}
	}
	return newestProcess;
}

static void Log(string message)
{
	Console.WriteLine($"[Borderless1942]: [{DateTime.Now:yyyy-MM-dd - hh:mm:ss tt}] {message}");
}

static void UpdateWindowPosition(Window window, int width, int height)
{
	var monitorBounds = window.GetCurrentMonitor().GetBounds();
	var x = monitorBounds.Left + (monitorBounds.Width - width) / 2;
	var y = monitorBounds.Top + (monitorBounds.Height - height) / 2;
	window.SetPosition(x, y, width, height);
}
