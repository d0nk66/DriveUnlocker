# DriveUnlocker

Windows 在弹出外接硬盘时，偶尔会报"设备正在使用中"，却不告诉你是哪个程序在占用。DriveUnlocker 会列出所有占用该盘的进程，支持单独或批量终止，并在清除后弹出驱动器。

## 系统要求

- Windows 10 / 11（x64）
- .NET 8 Runtime — [下载地址](https://dotnet.microsoft.com/download/dotnet/8.0)（仅 lite 版需要，standalone 版自带运行时）
- 管理员权限（扫描所有进程）

## 下载

Release提供两种打包方式：

| 版本 | 大小 | 说明 |
|---|---|---|
| `DriveUnlocker-lite` | 约 300 KB | 需要系统已安装 .NET 8 Runtime |
| `DriveUnlocker` | 约 73 MB | 无任何依赖，开箱即用 |

## 使用方法

1. 以管理员身份运行（右键 → 以管理员身份运行，或程序会自动提示）。
2. 在顶部下拉框中选择要弹出的驱动器。
3. 点击**扫描占用**查找占用进程，或直接点击**弹出驱动器**尝试直接弹出。
4. 可以对单行点击 **Kill** 终止单个进程，也可以勾选后点击 **Kill 选中项**，或直接点击 **Kill 全部并弹出**一键清除并弹出。

## 从源码构建

```
git clone https://github.com/yourname/DriveUnlocker.git
cd DriveUnlocker
dotnet build DriveUnlocker.sln
```

发布包构建：在项目根目录运行 `build.bat` 可生成发布包，输出到 `dist/lite/` 和 `dist/standalone/`。

## 实现说明

进程扫描使用 [Windows Restart Manager API](https://learn.microsoft.com/zh-cn/windows/win32/rstmgr/restart-manager-portal)（`rstrtmgr.dll`），这是 Windows Installer 在软件更新时检测文件占用的相同机制，准确率高。

驱动器弹出调用 `cfgmgr32.dll` 的 `CM_Request_Device_Eject`，与系统托盘"安全删除硬件"按钮走的是同一套流程，弹出成功后会触发资源管理器的通知。对于有多个分区的移动硬盘，程序会先逐一锁定并卸载所有分区，再发起弹出请求，避免因残留句柄导致弹出失败。

## 许可证

MIT
