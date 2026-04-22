# DriveUnlocker

[中文说明](README.zh.md)

Windows will sometimes refuse to eject an external drive with the unhelpful message "This device is currently in use." DriveUnlocker shows you exactly which processes are holding the drive, lets you kill them, and ejects the drive — all in one window.

## Requirements

- Windows 10 or 11 (x64)
- .NET 8 Runtime — [download here](https://dotnet.microsoft.com/download/dotnet/8.0) *(lite build only; the standalone build includes everything)*
- Administrator privileges (required to scan all processes)

## Download

Two builds are available on the [Releases](https://github.com/d0nk66/disk-unlocker/releases) page:

| Build | Size | Notes |
|---|---|---|
| `DriveUnlocker-lite.exe` | ~300 KB | Requires .NET 8 Runtime to be installed |
| `DriveUnlocker-standalone.exe` | ~12 MB | No dependencies, runs anywhere |

## Usage

1. Run `DriveUnlocker.exe` as Administrator (right-click → Run as administrator, or it will prompt you).
2. Select the drive you want to eject from the dropdown.
3. Click **Scan** to find locking processes, or go straight to **Eject** if you want to try without scanning first.
4. Kill individual processes with the **Kill** button on each row, or use **Kill All & Eject** to clear everything and eject in one step.

## Building from source

```
git clone https://github.com/d0nk66/disk-unlocker.git
cd DriveUnlocker
dotnet build DriveUnlocker.sln
```

To produce a release binary, run `build.bat` in the project root. It outputs both a lite and a standalone build under `dist/`.

## How it works

Process scanning uses the [Windows Restart Manager API](https://learn.microsoft.com/en-us/windows/win32/rstmgr/restart-manager-portal) (`rstrtmgr.dll`) — the same mechanism Windows Installer uses to detect file locks during software updates. Ejection goes through `CM_Request_Device_Eject` in `cfgmgr32.dll`, which is what the system tray "Safely Remove Hardware" option calls internally, so you get the same shell notification on success. For drives with multiple partitions, the tool locks and dismounts each volume before requesting the eject, to avoid failures from leftover open handles.

## License

MIT
