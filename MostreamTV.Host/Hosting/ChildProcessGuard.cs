using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace MostreamTV.Host.Hosting;

internal static class ChildProcessGuard
{
    private const int JobObjectExtendedLimitInformationClass = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const int ProcessBasicInformationClass = 0;

    private static readonly object JobLock = new();
    private static IntPtr _jobHandle;

    private static readonly string RuntimeRoot = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MoStreamTV",
            "Runtime"))
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    private static readonly string[] ManagedRuntimeProcessNames =
    [
        "syncplayServer",
        "SyncplayConsole",
        "cloudflared",
    ];

    public static void Attach(Process process)
    {
        try
        {
            if (process.HasExited)
                return;

            var job = EnsureJobHandle();
            if (job != IntPtr.Zero)
                AssignProcessToJobObject(job, process.Handle);
        }
        catch
        {
            // Best-effort OS lifetime binding; explicit shutdown remains primary.
        }
    }

    public static void CleanupOrphanedRuntimeProcesses()
    {
        var currentPid = Environment.ProcessId;
        var hasSiblingHost = HasOtherHostProcess(currentPid);

        foreach (var procName in ManagedRuntimeProcessNames)
        {
            Process[] candidates;
            try
            {
                candidates = Process.GetProcessesByName(procName);
            }
            catch
            {
                continue;
            }

            foreach (var proc in candidates)
            {
                try
                {
                    var exePath = proc.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(exePath))
                        continue;

                    var fullExePath = Path.GetFullPath(exePath);
                    if (!fullExePath.StartsWith(RuntimeRoot, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!IsOrphanedProcess(proc, currentPid, hasSiblingHost))
                        continue;

                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(1500);
                }
                catch
                {
                    // Ignore processes that exited concurrently or cannot be inspected.
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
    }

    public static bool IsTcpPortAvailable(int port)
    {
        if (port is < 1 or > 65535)
            return false;

        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                ExclusiveAddressUse = true,
            };
            socket.Bind(new IPEndPoint(IPAddress.Any, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public static int FindAvailableTcpPort(int preferredPort, int maxAttempts = 50)
    {
        for (var offset = 0; offset < maxAttempts; offset++)
        {
            var candidate = preferredPort + offset;
            if (candidate > 65535)
                break;
            if (IsTcpPortAvailable(candidate))
                return candidate;
        }

        return preferredPort;
    }

    private static bool IsOrphanedProcess(Process child, int currentPid, bool hasSiblingHost)
    {
        if (TryGetParentProcessId(child.Handle, out var parentPid))
        {
            if (parentPid == currentPid)
                return false;

            if (parentPid <= 0)
                return !hasSiblingHost;

            try
            {
                using var parent = Process.GetProcessById(parentPid);
                // If the parent PID was reused by a newer process, the original parent exited.
                return parent.HasExited || parent.StartTime > child.StartTime;
            }
            catch
            {
                return true;
            }
        }

        return !hasSiblingHost;
    }

    private static bool HasOtherHostProcess(int currentPid)
    {
        foreach (var hostName in new[] { "MostreamTV.Host", "MoStreamTV.Host" })
        {
            try
            {
                foreach (var proc in Process.GetProcessesByName(hostName))
                {
                    try
                    {
                        if (proc.Id != currentPid && !proc.HasExited)
                            return true;
                    }
                    catch
                    {
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }
            catch
            {
            }
        }

        return false;
    }

    private static bool TryGetParentProcessId(IntPtr processHandle, out int parentPid)
    {
        parentPid = 0;
        var pbi = new ProcessBasicInformation();
        var status = NtQueryInformationProcess(
            processHandle,
            ProcessBasicInformationClass,
            ref pbi,
            Marshal.SizeOf<ProcessBasicInformation>(),
            out _);

        if (status != 0)
            return false;

        var raw = pbi.InheritedFromUniqueProcessId.ToUInt64();
        if (raw > int.MaxValue)
            return false;

        parentPid = (int)raw;
        return true;
    }

    private static IntPtr EnsureJobHandle()
    {
        if (_jobHandle != IntPtr.Zero)
            return _jobHandle;

        lock (JobLock)
        {
            if (_jobHandle != IntPtr.Zero)
                return _jobHandle;

            var handle = CreateJobObjectW(IntPtr.Zero, null);
            if (handle == IntPtr.Zero)
                return IntPtr.Zero;

            var info = new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation
                {
                    LimitFlags = JobObjectLimitKillOnJobClose,
                },
            };

            var length = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
            var ptr = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(info, ptr, fDeleteOld: false);
                if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformationClass, ptr, (uint)length))
                {
                    CloseHandle(handle);
                    return IntPtr.Zero;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            _jobHandle = handle;
            return _jobHandle;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public UIntPtr UniqueProcessId;
        public UIntPtr InheritedFromUniqueProcessId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob,
        int jobObjectInfoClass,
        IntPtr lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref ProcessBasicInformation processInformation,
        int processInformationLength,
        out int returnLength);
}
