namespace FaceCompareTest;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // 命令行模式：FaceCompareTest.exe <图片1> <图片2> [设备] —— 无界面输出比对结果，便于脚本验证。
        if (args.Length >= 2)
        {
            var device = args.Length >= 3 ? args[2] : "CPU";
            var report = ConsoleRunner.Run(args[0], args[1], device);
            Console.WriteLine(report);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "result.txt"), report);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
