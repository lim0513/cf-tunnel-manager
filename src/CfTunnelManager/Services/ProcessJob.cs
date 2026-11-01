using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CfTunnelManager.Services;

/// <summary>A Windows job that kills its processes when this app exits, even after a crash.</summary>
internal sealed partial class ProcessJob : IDisposable
{
    private const int ExtendedLimitInformation = 9;
    private const uint KillOnJobClose = 0x2000;

    private readonly nint _handle;

    public ProcessJob()
    {
        _handle = CreateJobObjectW(0, null);
        if (_handle == 0)
        {
            throw new Win32Exception();
        }

        var info = new ExtendedLimit { Basic = new BasicLimit { LimitFlags = KillOnJobClose } };
        if (!SetInformationJobObject(_handle, ExtendedLimitInformation, ref info, Marshal.SizeOf<ExtendedLimit>()))
        {
            throw new Win32Exception();
        }
    }

    public void Add(Process process)
    {
        if (!AssignProcessToJobObject(_handle, process.Handle))
        {
            throw new Win32Exception();
        }
    }

    public void Dispose() => CloseHandle(_handle);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateJobObjectW(nint attributes, string? name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(nint job, int infoClass, ref ExtendedLimit info, int length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(nint job, nint process);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimit
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
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
    private struct ExtendedLimit
    {
        public BasicLimit Basic;
        public IoCounters Io;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }
}
