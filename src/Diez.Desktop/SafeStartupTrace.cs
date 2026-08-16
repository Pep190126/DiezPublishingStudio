using System.Text;

namespace DiezPublishingStudio;

internal static class SafeStartupTrace
{
    private const string FileName = "safe-startup-trace.log";
    private static readonly object Gate = new();

    public static void Reset(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory());
                File.WriteAllText(Path(), Header() + Timestamped(message), Encoding.UTF8);
            }
        }
        catch { }
    }

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory());
                File.AppendAllText(Path(), Timestamped(message), Encoding.UTF8);
            }
        }
        catch { }
    }

    public static IDisposable StartHeartbeat(int intervalMilliseconds = 100)
    {
        var cancellation = new CancellationTokenSource();
        var thread = new Thread(() =>
        {
            Write("heartbeat-thread-started | managedThread=" + Environment.CurrentManagedThreadId);
            var sequence = 0;
            while (!cancellation.IsCancellationRequested)
            {
                if (cancellation.Token.WaitHandle.WaitOne(intervalMilliseconds))
                    break;

                sequence++;
                Write("heartbeat | seq=" + sequence + " | pid=" + Environment.ProcessId);
            }

            Write("heartbeat-thread-stopped");
        })
        {
            IsBackground = true,
            Name = "Diez Safe Startup Heartbeat"
        };
        thread.Start();
        return new HeartbeatHandle(cancellation, thread);
    }

    public static string Path() => System.IO.Path.Combine(LogDirectory(), FileName);

    private static string LogDirectory() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Diez Publishing Studio",
        "logs");

    private static string Header() =>
        "Diez Publishing Studio safe startup trace" + Environment.NewLine +
        "Version: " + ProductInfo.Version + Environment.NewLine +
        "Started: " + DateTimeOffset.Now.ToString("O") + Environment.NewLine;

    private static string Timestamped(string message) =>
        DateTimeOffset.Now.ToString("O") + " | " + message + Environment.NewLine;

    private sealed class HeartbeatHandle : IDisposable
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly Thread _thread;
        private int _disposed;

        public HeartbeatHandle(CancellationTokenSource cancellation, Thread thread)
        {
            _cancellation = cancellation;
            _thread = thread;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { _cancellation.Cancel(); } catch { }
            try
            {
                if (_thread.IsAlive)
                    _thread.Join(500);
            }
            catch { }
            _cancellation.Dispose();
        }
    }
}
