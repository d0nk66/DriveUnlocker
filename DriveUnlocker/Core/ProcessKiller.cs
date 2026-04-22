using System.Diagnostics;

namespace DriveUnlocker.Core;

public static class ProcessKiller
{
    public sealed record KillResult(bool Success, string Message);

    public static KillResult Kill(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            string processName = process.ProcessName;

            process.Kill(entireProcessTree: false);
            process.WaitForExit(3000);

            return new KillResult(true, $"已终止进程 {processName} (PID: {pid})");
        }
        catch (ArgumentException)
        {
            return new KillResult(true, $"进程 {pid} 已不存在");
        }
        catch (Exception ex)
        {
            return new KillResult(false, $"终止失败：{ex.Message}");
        }
    }

    public static List<KillResult> KillAll(IEnumerable<int> pids)
    {
        return pids.Select(Kill).ToList();
    }
}
