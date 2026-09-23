using System.Diagnostics;
using TPW.PS2.Launcher;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("TPW.PS2.LauncherUiAudit")]

/// <summary>External-effect seams for window tests. Production uses these real defaults;
/// the headless audit supplies closed fake command/process boundaries and its own directory.</summary>
internal sealed class LauncherWindowServices
{
    public bool AllowEnvironmentActions { get; init; } = true;
    public Func<string, string> FindExecutable { get; init; } = MainWindow.Which;
    public Func<string, string[], string, TimeSpan, Task<LauncherProcess.Result>> Execute { get; init; }
        = (exe, args, cwd, timeout) => LauncherProcess.RunAsync(exe, args, cwd, timeout);
    public Func<ProcessStartInfo, Process> Start { get; init; } = Process.Start;
}
