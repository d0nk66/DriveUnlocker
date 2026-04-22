using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace DriveUnlocker.Core;

[SuppressMessage("Interoperability", "SYSLIB1054:Use LibraryImportAttribute instead of DllImportAttribute", Justification = "当前 P/Invoke 签名包含数组与结构体封送，优先保持 Restart Manager 调用稳定性。")]
public static class RestartManagerScanner
{
    private const int CchRmMaxAppName = 255;
    private const int CchRmMaxSvcName = 63;
    private const int ErrorMoreData = 234;

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(
        out uint pSessionHandle,
        int dwSessionFlags,
        string strSessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint pSessionHandle,
        uint nFiles,
        string[] rgsFileNames,
        uint nApplications,
        [In] RM_UNIQUE_PROCESS[]? rgApplications,
        uint nServices,
        string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
        ref uint lpdwRebootReasons);

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxAppName + 1)]
        public string strAppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxSvcName + 1)]
        public string strServiceShortName;

        public RM_APP_TYPE ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    private enum RM_APP_TYPE
    {
        RmUnknownApp = 0,
        RmMainWindow = 1,
        RmOtherWindow = 2,
        RmService = 3,
        RmExplorer = 4,
        RmConsole = 5,
        RmCritical = 1000
    }

    public static List<LockingProcess> ScanDrive(string drivePath)
    {
        if (string.IsNullOrWhiteSpace(drivePath))
        {
            throw new ArgumentException("驱动器路径不能为空。", nameof(drivePath));
        }

        List<LockingProcess> result = [];
        string sessionKey = Guid.NewGuid().ToString("N");

        EnsureRestartManagerSuccess(
            RmStartSession(out uint sessionHandle, 0, sessionKey),
            "无法启动 Restart Manager 会话");

        try
        {
            string normalizedDrivePath = NormalizeDrivePath(drivePath);
            string[] resources = CollectResources(normalizedDrivePath);

            int registerResult = RmRegisterResources(
                sessionHandle,
                (uint)resources.Length,
                resources,
                0,
                null,
                0,
                null);
            EnsureRestartManagerSuccess(registerResult, "无法注册目标驱动器资源");

            uint processInfoCount = 0;
            uint rebootReasons = 0;

            int firstGetListResult = RmGetList(
                sessionHandle,
                out uint processInfoNeeded,
                ref processInfoCount,
                null,
                ref rebootReasons);

            if (firstGetListResult != 0 && firstGetListResult != ErrorMoreData)
            {
                EnsureRestartManagerSuccess(firstGetListResult, "读取占用进程数量失败");
            }

            if (processInfoNeeded == 0)
            {
                return result;
            }

            RM_PROCESS_INFO[] processInfo = new RM_PROCESS_INFO[processInfoNeeded];
            processInfoCount = processInfoNeeded;

            int secondGetListResult = RmGetList(
                sessionHandle,
                out processInfoNeeded,
                ref processInfoCount,
                processInfo,
                ref rebootReasons);
            EnsureRestartManagerSuccess(secondGetListResult, "读取占用进程详情失败");

            foreach (RM_PROCESS_INFO info in processInfo.Take((int)processInfoCount))
            {
                try
                {
                    using Process process = Process.GetProcessById(info.Process.dwProcessId);
                    result.Add(new LockingProcess
                    {
                        Pid = process.Id,
                        Name = process.ProcessName,
                        ExePath = GetProcessPath(process),
                        AppName = info.strAppName
                    });
                }
                catch (ArgumentException)
                {
                    // 进程已退出，按 PRD 要求静默跳过。
                }
            }

            return result;
        }
        finally
        {
            _ = RmEndSession(sessionHandle);
        }
    }

    private static string[] CollectResources(string drivePath)
    {
        IEnumerable<string> files = Directory.EnumerateFiles(
                drivePath,
                "*",
                new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true,
                    MaxRecursionDepth = 6
                })
            .Take(5000);

        return [drivePath, .. files];
    }

    private static string NormalizeDrivePath(string drivePath)
    {
        return drivePath.EndsWith(Path.DirectorySeparatorChar)
            ? drivePath
            : $"{drivePath}{Path.DirectorySeparatorChar}";
    }

    private static void EnsureRestartManagerSuccess(int resultCode, string message)
    {
        if (resultCode == 0)
        {
            return;
        }

        throw new InvalidOperationException($"{message}，错误码：{resultCode}");
    }

    private static string GetProcessPath(Process proc)
    {
        try
        {
            return proc.MainModule?.FileName ?? "（无法获取）";
        }
        catch (Win32Exception)
        {
            return "（权限不足）";
        }
        catch (InvalidOperationException)
        {
            return "（无法获取）";
        }
    }
}

public sealed class LockingProcess
{
    public int Pid { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ExePath { get; set; } = string.Empty;

    public string AppName { get; set; } = string.Empty;
}
