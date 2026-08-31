namespace VisitorApp;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new MainPage()) { Title = "VisitorApp" };

#if WINDOWS
		// Kiosk 模式：窗口创建后切换为 FullScreen 呈现器 ——
		// 无标题栏、无边框、覆盖任务栏；退出程序入口在「系统设置」页。
		// 注意：Created 触发时原生窗口可能尚未完全就绪，需延迟重试，否则 SetPresenter 会抛异常。
		window.Created += (_, _) => _ = EnterFullScreenAsync(window);
#endif

		return window;
	}

#if WINDOWS
	/// <summary>
	/// 全屏无边框：优先 WinUI AppWindow FullScreen 呈现器；不生效时兜底用 Win32
	/// 直接去掉窗口边框样式并擑满屏幕（对 WebView2 宿主窗口更可靠）。
	/// </summary>
	private static async Task EnterFullScreenAsync(Window window)
	{
		var logPath = System.IO.Path.Combine(AppContext.BaseDirectory, "fullscreen.log");
		void Log(string msg) { try { System.IO.File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss.fff} {msg}\n"); } catch { } }

		for (var attempt = 1; attempt <= 10; attempt++)
		{
			// 枚举本进程的可见顶层窗口拿句柄（避开 MAUI Handler 的 WinRT 互操作差异）。
			var hwnd = FindOwnTopWindow();
			if (hwnd != IntPtr.Zero)
			{
				try
				{
					ApplyWin32FullScreen(hwnd);
					await Task.Delay(200);
					if (IsFullScreen(hwnd)) { Log($"尝试 {attempt}: 全屏成功"); return; }
				}
				catch (Exception ex)
				{
					Log($"尝试 {attempt}: Win32 失败: {ex.Message}");
				}
			}
			await Task.Delay(300);
		}
		Log("全屏设置最终未生效");
	}

	/// <summary>查找本进程第一个可见的顶层窗口（即应用主窗口）。</summary>
	private static IntPtr FindOwnTopWindow()
	{
		var self = System.Diagnostics.Process.GetCurrentProcess().Id;
		IntPtr found = IntPtr.Zero;
		Win32.EnumWindows((h, _) =>
		{
			if (Win32.IsWindowVisible(h) && Win32.GetWindowThreadProcessId(h, out var pid) != 0 && pid == self)
			{
				found = h;
				return false;
			}
			return true;
		}, IntPtr.Zero);
		return found;
	}

	private static bool IsFullScreen(IntPtr hwnd)
	{
		if (hwnd == IntPtr.Zero) return false;
		Win32.GetWindowRect(hwnd, out var rect);
		var sw = Win32.GetSystemMetrics(0);
		var sh = Win32.GetSystemMetrics(1);
		return rect.Left <= 0 && rect.Top <= 0 && rect.Right >= sw && rect.Bottom >= sh;
	}

	/// <summary>去掉标题栏/边框样式并擑满屏幕。</summary>
	private static void ApplyWin32FullScreen(IntPtr hwnd)
	{
		var sw = Win32.GetSystemMetrics(0);
		var sh = Win32.GetSystemMetrics(1);
		var style = Win32.GetWindowLong(hwnd, Win32.GWL_STYLE);
		// 移除标题栏、边框、最大化框，保留窗口可见与子窗口关系。
		style &= ~(Win32.WS_CAPTION | Win32.WS_THICKFRAME | Win32.WS_MAXIMIZEBOX);
		Win32.SetWindowLong(hwnd, Win32.GWL_STYLE, style);
		Win32.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, sw, sh,
			Win32.SWP_NOZORDER | Win32.SWP_FRAMECHANGED | Win32.SWP_SHOWWINDOW);
	}

	private static class Win32
	{
		public const int GWL_STYLE = -16;
		public const long WS_CAPTION = 0x00C00000L;
		public const long WS_THICKFRAME = 0x00040000L;
		public const long WS_MAXIMIZEBOX = 0x00010000L;
		public const uint SWP_NOZORDER = 0x0004;
		public const uint SWP_FRAMECHANGED = 0x0020;
		public const uint SWP_SHOWWINDOW = 0x0040;

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		public static extern long GetWindowLong(IntPtr hWnd, int nIndex);

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		public static extern long SetWindowLong(IntPtr hWnd, int nIndex, long dwNewLong);

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		public static extern int GetSystemMetrics(int nIndex);

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		public static extern bool IsWindowVisible(IntPtr hWnd);

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		public static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

		public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

		[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
		public struct RECT { public int Left, Top, Right, Bottom; }
	}
#endif
}
