using System.Security.Principal;
using DriveUnlocker.Forms;

namespace DriveUnlocker;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) => ShowUnhandledException(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ShowUnhandledException(args.ExceptionObject as Exception);

        ApplicationConfiguration.Initialize();

        if (!IsAdministrator())
        {
            MessageBox.Show(
                "DriveUnlocker 需要管理员权限才能扫描所有进程。\n请右键以管理员身份运行。",
                "权限不足",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        Application.Run(new MainForm());
    }

    private static void ShowUnhandledException(Exception? exception)
    {
        string errorMessage = exception?.ToString() ?? "未知异常";

        MessageBox.Show(
            $"程序发生未处理异常：\n\n{errorMessage}\n\n请以管理员身份重试。",
            "DriveUnlocker 错误",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static bool IsAdministrator()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
