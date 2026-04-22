using System.Runtime.InteropServices;

namespace DriveUnlocker.Core;

public static class DriveEjector
{
    // ── P/Invoke ──────────────────────────────────────────────────────────────

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        IntPtr hDevice, uint dwIoControlCode,
        IntPtr lpInBuffer, int nInBufferSize,
        IntPtr lpOutBuffer, int nOutBufferSize,
        out int lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, int Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr DeviceInfoSet, IntPtr DeviceInfoData,
        ref Guid InterfaceClassGuid, int MemberIndex,
        ref SpDeviceInterfaceData DeviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr DeviceInfoSet, ref SpDeviceInterfaceData DeviceInterfaceData,
        IntPtr DeviceInterfaceDetailData, int DeviceInterfaceDetailDataSize,
        ref int RequiredSize, ref SpDevinfoData DeviceInfoData);

    [DllImport("setupapi.dll")]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(
        out uint pdnDevInst, uint dnDevInst, int ulFlags);

    // Passing IntPtr.Zero for pVetoType and pszVetoName tells the PnP manager
    // to show its own shell balloon on success and its own reason dialog on failure.
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Request_Device_Eject(
        uint dnDevInst, IntPtr pVetoType, IntPtr pszVetoName,
        int ulNameLength, int ulFlags);

    // ── Structs ───────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public int cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StorageDeviceNumber
    {
        public int DeviceType;
        public int DeviceNumber;
        public int PartitionNumber;
    }

    // ── Constants ─────────────────────────────────────────────────────────────

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint FSCTL_LOCK_VOLUME = 0x00090018;
    private const uint FSCTL_DISMOUNT_VOLUME = 0x00090020;
    private const uint IOCTL_STORAGE_MEDIA_REMOVAL = 0x002D4804;
    private const uint IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x002D1080;
    private const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x00560000;
    private const int CR_SUCCESS = 0;
    private const int DIGCF_PRESENT = 0x00000002;
    private const int DIGCF_DEVICEINTERFACE = 0x00000010;

    // VOLUME_DISK_EXTENTS layout (x64):
    //   DWORD NumberOfDiskExtents  (4 bytes at offset 0)
    //   4 bytes padding            (to align DISK_EXTENT to 8-byte boundary)
    //   DISK_EXTENT Extents[n]     (each 24 bytes: DWORD+pad(4)+LONGLONG+LONGLONG)
    private const int VolumeDiskExtentsHeaderBytes = 8;
    private const int DiskExtentStride = 24;

    private static readonly IntPtr InvalidHandle = new(-1);
    private static readonly Guid GuidDevInterfaceDisk =
        new("53f56307-b6bf-11d0-94f2-00a0c91efb8b");

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Safely ejects the drive using the same mechanism as Windows
    /// "Safely Remove Hardware". Shows the shell notification on success.
    /// Handles multi-partition USB hard drives.
    /// </summary>
    public static bool Eject(char driveLetter)
    {
        char letter = char.ToUpperInvariant(driveLetter);
        string volumePath = $@"\\.\{letter}:";

        // 1. Find the physical disk number(s) backing this volume
        List<uint> diskNumbers = GetDiskNumbers(volumePath);
        if (diskNumbers.Count == 0) return false;

        // 2. Enumerate ALL volumes on those same physical disks.
        //    This is critical for USB drives with multiple partitions: we must
        //    lock and dismount every partition or the eject will be vetoed.
        List<string> allVolumes = GetAllVolumesOnDisks(diskNumbers);
        if (allVolumes.Count == 0) allVolumes.Add(volumePath);

        // 3. Lock + dismount each volume to flush and release open handles
        foreach (string vol in allVolumes)
            LockAndDismount(vol);

        // 4. Find the PnP device instance for the physical disk
        uint diskDevInst = FindDiskDevInst(diskNumbers[0]);
        if (diskDevInst == 0) return false;

        // 5. Get the PARENT device instance.
        //    CRITICAL: calling CM_Request_Device_Eject on the disk node itself
        //    returns PNP_VetoIllegalDeviceRequest on USB hard drives.
        //    The parent (USB device node) is what can actually be ejected.
        if (CM_Get_Parent(out uint parentDevInst, diskDevInst, 0) != CR_SUCCESS)
            return false;

        // 6. Request eject, with retry (the OS may need a moment after dismount).
        //    Null pVetoType / pszVetoName → Windows displays its own shell balloon.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (CM_Request_Device_Eject(
                    parentDevInst, IntPtr.Zero, IntPtr.Zero, 0, 0) == CR_SUCCESS)
                return true;

            if (attempt < 2)
                Thread.Sleep(500);
        }

        return false;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>Returns the physical disk number(s) that back the given volume path.</summary>
    private static List<uint> GetDiskNumbers(string volumePath)
    {
        var result = new List<uint>();

        // Must use GENERIC_READ | GENERIC_WRITE — passing 0 is a common bug
        // that causes DeviceIoControl to silently fail.
        IntPtr hVol = CreateFile(volumePath,
            GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);

        if (hVol == InvalidHandle) return result;

        try
        {
            int bufSize = VolumeDiskExtentsHeaderBytes + 8 * DiskExtentStride;
            IntPtr buf = Marshal.AllocHGlobal(bufSize);
            try
            {
                if (DeviceIoControl(hVol, IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS,
                    IntPtr.Zero, 0, buf, bufSize, out _, IntPtr.Zero))
                {
                    int count = Marshal.ReadInt32(buf, 0);
                    for (int i = 0; i < count; i++)
                    {
                        // DiskNumber is the first DWORD of each DISK_EXTENT
                        uint diskNum = (uint)Marshal.ReadInt32(
                            buf, VolumeDiskExtentsHeaderBytes + i * DiskExtentStride);
                        if (!result.Contains(diskNum))
                            result.Add(diskNum);
                    }
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        finally { CloseHandle(hVol); }

        return result;
    }

    /// <summary>
    /// Returns the \\.\X: paths of every volume whose underlying disk
    /// number intersects with the given set.
    /// </summary>
    private static List<string> GetAllVolumesOnDisks(List<uint> diskNumbers)
    {
        var result = new List<string>();
        foreach (string drv in Environment.GetLogicalDrives())
        {
            string vol = $@"\\.\{drv.TrimEnd('\\')}";
            if (!vol.EndsWith(':')) vol += ':';
            if (GetDiskNumbers(vol).Any(n => diskNumbers.Contains(n))
                && !result.Contains(vol))
                result.Add(vol);
        }
        return result;
    }

    /// <summary>
    /// Locks and dismounts a volume to flush its filesystem and release
    /// open handles before the PnP manager attempts to eject the device.
    /// Non-fatal: we continue even if locking fails (the CM eject call
    /// will surface the veto reason if something is still blocking).
    /// </summary>
    private static void LockAndDismount(string volumePath)
    {
        IntPtr hVol = CreateFile(volumePath,
            GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);

        if (hVol == InvalidHandle) return;

        try
        {
            // Retry lock: Windows Explorer may transiently hold the volume
            for (int i = 0; i < 10; i++)
            {
                if (DeviceIoControl(hVol, FSCTL_LOCK_VOLUME,
                    IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
                    break;
                Thread.Sleep(200);
            }

            // Dismount: flushes the filesystem and invalidates all open handles
            DeviceIoControl(hVol, FSCTL_DISMOUNT_VOLUME,
                IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);

            // Allow media removal: PREVENT_MEDIA_REMOVAL.PreventMediaRemoval = 0
            IntPtr pAllow = Marshal.AllocHGlobal(1);
            try
            {
                Marshal.WriteByte(pAllow, 0);
                DeviceIoControl(hVol, IOCTL_STORAGE_MEDIA_REMOVAL,
                    pAllow, 1, IntPtr.Zero, 0, out _, IntPtr.Zero);
            }
            finally { Marshal.FreeHGlobal(pAllow); }
        }
        finally { CloseHandle(hVol); }
    }

    /// <summary>
    /// Finds the PnP device instance handle (DevInst) for the physical disk
    /// identified by the given disk number, by enumerating SetupDi disk interfaces.
    /// </summary>
    private static uint FindDiskDevInst(uint diskNumber)
    {
        var diskGuid = GuidDevInterfaceDisk;
        IntPtr devInfoSet = SetupDiGetClassDevs(ref diskGuid, IntPtr.Zero, IntPtr.Zero,
            DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);

        if (devInfoSet == InvalidHandle) return 0;

        try
        {
            for (int idx = 0; ; idx++)
            {
                var ifaceData = new SpDeviceInterfaceData
                {
                    cbSize = Marshal.SizeOf<SpDeviceInterfaceData>()
                };
                if (!SetupDiEnumDeviceInterfaces(devInfoSet, IntPtr.Zero,
                    ref diskGuid, idx, ref ifaceData))
                    break;

                var devinfoData = new SpDevinfoData
                {
                    cbSize = Marshal.SizeOf<SpDevinfoData>()
                };
                int reqSize = 0;

                // First call: probe required buffer size
                SetupDiGetDeviceInterfaceDetail(devInfoSet, ref ifaceData,
                    IntPtr.Zero, 0, ref reqSize, ref devinfoData);
                if (reqSize == 0) continue;

                IntPtr detailBuf = Marshal.AllocHGlobal(reqSize);
                try
                {
                    // SP_DEVICE_INTERFACE_DETAIL_DATA.cbSize: 8 on x64, 6 on x86
                    Marshal.WriteInt32(detailBuf, IntPtr.Size == 8 ? 8 : 6);

                    if (!SetupDiGetDeviceInterfaceDetail(devInfoSet, ref ifaceData,
                        detailBuf, reqSize, ref reqSize, ref devinfoData))
                        continue;

                    // DevicePath starts at offset 4 (after the DWORD cbSize)
                    string diskPath = Marshal.PtrToStringUni(detailBuf + 4) ?? string.Empty;

                    // Access = 0 is sufficient for IOCTL_STORAGE_GET_DEVICE_NUMBER
                    IntPtr hDisk = CreateFile(diskPath, 0,
                        FILE_SHARE_READ | FILE_SHARE_WRITE,
                        IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);

                    if (hDisk == InvalidHandle) continue;

                    try
                    {
                        int sdnSize = Marshal.SizeOf<StorageDeviceNumber>();
                        IntPtr sdnBuf = Marshal.AllocHGlobal(sdnSize);
                        try
                        {
                            if (DeviceIoControl(hDisk, IOCTL_STORAGE_GET_DEVICE_NUMBER,
                                IntPtr.Zero, 0, sdnBuf, sdnSize, out _, IntPtr.Zero))
                            {
                                var sdn = Marshal.PtrToStructure<StorageDeviceNumber>(sdnBuf);
                                if ((uint)sdn.DeviceNumber == diskNumber)
                                    return devinfoData.DevInst;
                            }
                        }
                        finally { Marshal.FreeHGlobal(sdnBuf); }
                    }
                    finally { CloseHandle(hDisk); }
                }
                finally { Marshal.FreeHGlobal(detailBuf); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(devInfoSet); }

        return 0;
    }
}
