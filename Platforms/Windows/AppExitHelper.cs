#if WINDOWS
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VisitorApp.Platforms.Windows;

/// <summary>
/// 应用退出助手：先清理本进程派生的子进程（WebView2 渲染进程等），再终止主进程。
/// </summary>
public static class AppExitHelper
{
    /// <summary>清理所有相关进程并退出程序。</summary>
    public static void Exit()
    {
        try
        {
            var self = Process.GetCurrentProcess();
            // 收集以本进程为父进程的所有子进程（WebView2 / RestartAgent 等）。
            var children = Process.GetProcesses()
                .Where(p =>
                {
                    try { return p.Id != self.Id && GetParentId(p.Id) == self.Id; }
                    catch { return false; }
                })
                .ToList();

            foreach (var child in children)
            {
                try { child.Kill(entireProcessTree: true); } catch { /* 忽略已退出的子进程 */ }
            }
        }
        catch
        {
            // 清理失败不阻止退出。
        }

        // 强制终止主进程（比 Environment.Exit 更彻底，确保不残留）。
        Process.GetCurrentProcess().Kill(entireProcessTree: true);
    }

    /// <summary>通过进程快照（CreateToolhelp32Snapshot）查父进程 ID，无需 WMI。</summary>
    private static int GetParentId(int processId)
    {
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero) return -1;
        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (!Process32First(snapshot, ref entry)) return -1;
            do
            {
                if (entry.th32ProcessID == (uint)processId) return (int)entry.th32ParentProcessID;
            } while (Process32Next(snapshot, ref entry));
            return -1;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    private const uint TH32CS_SNAPPROCESS = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
#else

namespace VisitorApp.Platforms.Windows;

/// <summary>非 Windows 平台占位实现。</summary>
public static class AppExitHelper
{
    public static void Exit() { }
}

#endif
