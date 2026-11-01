using System.Runtime.InteropServices;

namespace CfTunnelManager.Services;

/// <summary>Keeps one running copy per Windows session and lets a later copy wake it.</summary>
internal sealed partial class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\CfTunnelManager.SingleInstance";
    private const string SignalName = @"Local\CfTunnelManager.Activate";
    private const int AnyProcess = -1;

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _signal;
    private RegisteredWaitHandle? _registration;

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var created);
        IsFirst = created;
        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
    }

    public bool IsFirst { get; }

    /// <summary>Calls <paramref name="activate"/> on a worker thread for each later launch.</summary>
    public void Listen(Action activate)
    {
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _signal, (_, _) => activate(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void ActivateFirstInstance()
    {
        // Windows only lets the foreground process hand over the foreground.
        AllowSetForegroundWindow(AnyProcess);
        _signal.Set();
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        if (IsFirst)
        {
            _mutex.ReleaseMutex();
        }
        _mutex.Dispose();
        _signal.Dispose();
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
