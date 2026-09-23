using System.Diagnostics;
using System.Text;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("TPW.PS2.LauncherAudit")]

namespace TPW.PS2.Launcher;

/// <summary>Bounded launcher command execution. Drain BOTH pipes concurrently;
/// keep a bounded diagnostic tail rather than accumulating an entire build log.
/// No shell is involved: each supplied argument is passed as its own argument.</summary>
public static class LauncherProcess
{
    public sealed record Result(int? ExitCode, bool TimedOut, string Stdout, string Stderr,
                                bool Truncated, string Error)
    {
        public bool Succeeded => ExitCode == 0 && !TimedOut && Error == null;
    }

    sealed class Tail(int limit)
    {
        readonly StringBuilder _text = new();
        readonly object _gate = new();
        bool _truncated;
        string _error;
        public async Task Drain(StreamReader reader)
        {
            var buffer = new char[4096];
            try
            {
                int n;
                while ((n = await reader.ReadAsync(buffer.AsMemory())) > 0)
                    lock (_gate)
                    {
                        _text.Append(buffer, 0, n);
                        if (_text.Length > limit)
                        {
                            _text.Remove(0, _text.Length - limit);
                            _truncated = true;
                        }
                    }
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException) { lock (_gate) _error = e.Message; }
        }
        public (string Text, bool Truncated, string Error) Snapshot() { lock (_gate) return (_text.ToString(), _truncated, _error); }
    }

    public static Task<Result> RunAsync(string executable, IEnumerable<string> arguments,
        string workingDirectory, TimeSpan timeout, int maxCharsPerStream = 65536)
        => RunCoreAsync(executable, arguments, workingDirectory, timeout, maxCharsPerStream,
                        process => process.Kill(entireProcessTree: true));

    // Injectable only to the audit assembly, to verify exceptional cleanup without
    // leaving intentionally unkillable processes on a developer's machine.
    internal static async Task<Result> RunCoreAsync(string executable, IEnumerable<string> arguments,
        string workingDirectory, TimeSpan timeout, int maxCharsPerStream, Action<Process> terminate)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (maxCharsPerStream < 1) throw new ArgumentOutOfRangeException(nameof(maxCharsPerStream));
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) return new(null, false, "", "", false, "process did not start");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        { return new(null, false, "", "", false, e.Message); }
        var stdout = new Tail(maxCharsPerStream); var stderr = new Tail(maxCharsPerStream);
        var output = stdout.Drain(process.StandardOutput); var error = stderr.Drain(process.StandardError);
        bool timedOut = false;
        string cleanupError = null;
        try
        {
            // Include pipe draining in the deadline: a descendant can retain a
            // redirected handle even after the original parent has exited.
            await Task.WhenAll(process.WaitForExitAsync(), output, error).WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            timedOut = true;
            try { terminate(process); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException)
            { cleanupError = "process cleanup: " + e.Message; }
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (TimeoutException) { cleanupError ??= "process exit was not confirmed after termination"; }
            process.StandardOutput.Dispose(); process.StandardError.Dispose();
        }
        var so = stdout.Snapshot(); var se = stderr.Snapshot();
        return new(process.HasExited ? process.ExitCode : null, timedOut, so.Text, se.Text,
                   so.Truncated || se.Truncated, timedOut ? cleanupError : so.Error ?? se.Error);
    }
}
