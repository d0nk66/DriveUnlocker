namespace DriveUnlocker.Helpers;

public static class DriveHelper
{
    public static List<DriveInfo> GetEjectableDrives()
    {
        return DriveInfo.GetDrives()
            .Where(drive => drive.IsReady)
            .Where(drive =>
                drive.DriveType == DriveType.Removable ||
                drive.DriveType == DriveType.Fixed)
            .Where(drive => !string.Equals(drive.Name, "C:\\", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static string FormatDriveLabel(DriveInfo drive)
    {
        string label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
            ? "本地磁盘"
            : drive.VolumeLabel;

        long capacityInGb = drive.TotalSize / 1024 / 1024 / 1024;
        return $"{drive.Name.TrimEnd('\\')} · {label} ({capacityInGb}GB)";
    }
}
