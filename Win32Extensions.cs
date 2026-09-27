using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Borderless1942;

public static class Win32Extensions
{
    public static Monitor GetPrimaryMonitor()
    {
        nint handle = PInvoke.MonitorFromPoint(new POINT
        {
            x = 0,
            y = 0
        }, 0);

        return new(handle);
    }

    public static nint GetMainWindowHandle(this Process process)
	{
		try
		{
			// Process caches the main window handle, refresh it so a recreated window is found
			process.Refresh();
			return process.MainWindowHandle;
		}
		catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
		{
			return 0;
		}
	}

	public static bool IsRunning(this Process process)
	{
		try
		{
			return !process.HasExited;
		}
		catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
		{
			return false;
		}
	}

	public static DateTime? GetStartTime(this Process process)
	{
		try
		{
			return process.StartTime;
		}
		catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
		{
			return null;
		}
	}
}

public readonly record struct Window(nint Handle)
{
	private HWND Win32Handle => new(Handle);

	public Monitor GetCurrentMonitor()
	{
		nint handle = PInvoke.MonitorFromWindow(Win32Handle, 0);
		return new(handle);
	}

	public void SetPosition(int x, int y, int width, int height)
	{
		var flags = SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE |
			SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER | SET_WINDOW_POS_FLAGS.SWP_NOSENDCHANGING | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED;
		PInvoke.SetWindowPos(Win32Handle, PInvoke.HWND_TOPMOST, x, y, width, height, flags);
		PInvoke.SendMessage(Win32Handle, PInvoke.WM_EXITSIZEMOVE, default, default);
	}

	public Rectangle GetBounds()
	{
		var windowInfo = new WINDOWINFO();
		PInvoke.GetWindowInfo(Win32Handle, ref windowInfo);
		return Rectangle.From(windowInfo.rcWindow);
	}

	public void RemoveBorders()
	{
		const int BorderStyles = (int)(WINDOW_STYLE.WS_THICKFRAME | WINDOW_STYLE.WS_DLGFRAME | WINDOW_STYLE.WS_BORDER);
		const int BorderExStyles = (int)(WINDOW_EX_STYLE.WS_EX_DLGMODALFRAME | WINDOW_EX_STYLE.WS_EX_WINDOWEDGE | WINDOW_EX_STYLE.WS_EX_CLIENTEDGE | WINDOW_EX_STYLE.WS_EX_STATICEDGE);

		// Only touch the window when it has borders, as this is called on every update
		var style = PInvoke.GetWindowLong(Win32Handle, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
		var exStyle = PInvoke.GetWindowLong(Win32Handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
		if ((style & BorderStyles) == 0 && (exStyle & BorderExStyles) == 0)
		{
			return;
		}

		_ = PInvoke.SetWindowLong(Win32Handle, WINDOW_LONG_PTR_INDEX.GWL_STYLE, style & ~BorderStyles);
		_ = PInvoke.SetWindowLong(Win32Handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle & ~BorderExStyles);
		PInvoke.SendMessage(Win32Handle, PInvoke.WM_EXITSIZEMOVE, default, default);
	}
}

public readonly record struct Monitor(nint Handle)
{
	private HMONITOR Win32Handle => new(Handle);

	public unsafe Rectangle GetBounds()
	{
		var monitorInfo = new MONITORINFO
		{
			cbSize = (uint)sizeof(MONITORINFO)
		};
		PInvoke.GetMonitorInfo(Win32Handle, ref monitorInfo);
		return Rectangle.From(monitorInfo.rcMonitor);
	}
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct Rectangle(int Left, int Top, int Right, int Bottom)
{
	public int Width => Right - Left;
	public int Height => Bottom - Top;

	internal static Rectangle From(RECT rectangle) => Unsafe.As<RECT, Rectangle>(ref rectangle);
}