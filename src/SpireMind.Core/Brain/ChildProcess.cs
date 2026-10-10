using System.Runtime.InteropServices;
using System.Text;
// The mod compiles Core with global usings for Godot and the game, which may have their own
// Channel. This alias keeps the .NET one there; Process is always written out in full.
using Channel = System.Threading.Channels.Channel;

namespace SpireMind.Core.Brain;

/// <summary>Starts child processes. <see cref="ProcessLauncher"/> is the real one; tests use a fake.</summary>
public interface IProcessLauncher
{
    /// <summary>Starts <paramref name="spec"/> and returns at once. Throws when the program
    /// can't be started (missing, not executable).</summary>
    IChildProcess Start(ChildProcessSpec spec);
}

/// <summary>What to run: the program by absolute path, its arguments (passed as they are, no
/// shell), the folder to run it in (null keeps the current one) and changes to the inherited
/// environment, where a null value removes the variable.</summary>
public sealed record ChildProcessSpec(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory,
    IReadOnlyDictionary<string, string?> EnvironmentOverrides);

/// <summary>
/// A running child process. Its standard input is closed at once, and both output streams are
/// read continuously into unbounded channels, one line per item, so the child can never stall
/// on a full pipe however slowly the lines are taken. Each channel completes when its stream
/// ends. Every member is safe to call from any thread, and none of them throws once the
/// process has started.
/// </summary>
public interface IChildProcess : IDisposable
{
    /// <summary>The process id.</summary>
    int Id { get; }

    /// <summary>Standard output, line by line.</summary>
    System.Threading.Channels.ChannelReader<string> StandardOutput { get; }

    /// <summary>Standard error, line by line.</summary>
    System.Threading.Channels.ChannelReader<string> StandardError { get; }

    /// <summary>Completes with the exit code when the process exits. It doesn't wait for the
    /// output channels, which may still hold lines.</summary>
    Task<int> Exited { get; }

    /// <summary>Asks the process to stop, like Ctrl-C (SIGINT). On Windows, where there is no
    /// such signal, it kills the process instead.</summary>
    void Interrupt();

    /// <summary>Kills the process and every process it started.</summary>
    void Kill();
}

/// <summary>Starts real processes with <see cref="ChildProcess"/>.</summary>
public sealed class ProcessLauncher : IProcessLauncher
{
    /// <summary>The one instance; it has no state.</summary>
    public static ProcessLauncher Instance { get; } = new();

    /// <inheritdoc/>
    public IChildProcess Start(ChildProcessSpec spec) => ChildProcess.Start(spec);
}

/// <summary>
/// <see cref="IChildProcess"/> over <see cref="System.Diagnostics.Process"/>: no shell, the
/// arguments go through ArgumentList as they are, output is read as UTF-8. Disposing a process
/// that is still running kills it (and its children) first, so nothing is left behind.
/// </summary>
public sealed class ChildProcess : IChildProcess
{
    private const int SigInt = 2;

    private readonly System.Diagnostics.Process _process;
    private readonly System.Threading.Channels.Channel<string> _out = Channel.CreateUnbounded<string>(new() { SingleWriter = true });
    private readonly System.Threading.Channels.Channel<string> _err = Channel.CreateUnbounded<string>(new() { SingleWriter = true });
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    private ChildProcess(System.Diagnostics.Process process) => _process = process;

    /// <inheritdoc/>
    public int Id { get; private set; }

    /// <inheritdoc/>
    public System.Threading.Channels.ChannelReader<string> StandardOutput => _out.Reader;

    /// <inheritdoc/>
    public System.Threading.Channels.ChannelReader<string> StandardError => _err.Reader;

    /// <summary>Completes with the exit code when the process exits; after
    /// <see cref="Dispose"/> it completes with -1 if the code wasn't known by then.</summary>
    public Task<int> Exited => _exited.Task;

    /// <summary>Starts <paramref name="spec"/>. Throws (usually <see cref="System.ComponentModel.Win32Exception"/>)
    /// when the program can't be started.</summary>
    public static ChildProcess Start(ChildProcessSpec spec)
    {
        var info = new System.Diagnostics.ProcessStartInfo(spec.FileName)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        foreach (var argument in spec.Arguments)
            info.ArgumentList.Add(argument);
        if (!string.IsNullOrEmpty(spec.WorkingDirectory))
            info.WorkingDirectory = spec.WorkingDirectory;
        foreach (var (name, value) in spec.EnvironmentOverrides)
        {
            if (value is null) info.Environment.Remove(name);
            else info.Environment[name] = value;
        }

        var process = new System.Diagnostics.Process { StartInfo = info, EnableRaisingEvents = true };
        var child = new ChildProcess(process);
        // Hooked up before the start, so an exit can't slip past it.
        process.Exited += (_, _) => child.OnExited();
        try
        {
            process.Start();
        }
        catch
        {
            process.Dispose();
            throw;
        }
        child.Id = process.Id;

        // Nothing is ever piped in: closing stdin now means the child never waits for input.
        try { process.StandardInput.Close(); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { /* it already exited */ }

        var stdout = process.StandardOutput;
        var stderr = process.StandardError;
        _ = Task.Run(() => PumpAsync(stdout, child._out.Writer));
        _ = Task.Run(() => PumpAsync(stderr, child._err.Writer));
        return child;
    }

    /// <inheritdoc/>
    public void Interrupt()
    {
        if (System.OperatingSystem.IsWindows())
        {
            Kill();
            return;
        }
        // Checked first: once the process is gone its id can belong to someone else.
        if (_exited.Task.IsCompleted || Volatile.Read(ref _disposed) != 0 || HasExited())
            return;
        try
        {
            SysKill(Id, SigInt);
        }
        catch (Exception)
        {
            // No libc to call (or P/Invoke isn't allowed where the mod was loaded): a kill still
            // stops it, only less politely.
            Kill();
        }
    }

    /// <inheritdoc/>
    public void Kill()
    {
        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception
                                       or NotSupportedException or ObjectDisposedException or AggregateException)
        {
            // Already gone, or part of the tree couldn't be killed.
        }
    }

    /// <summary>Kills the process (and its children) if it is still running, then releases it.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        if (!_exited.Task.IsCompleted && !HasExited())
            Kill();
        try { _process.Dispose(); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException) { /* best effort */ }
        // A disposed process raises no more events, so nobody may be left waiting.
        _exited.TrySetResult(-1);
        _out.Writer.TryComplete();
        _err.Writer.TryComplete();
    }

    private void OnExited()
    {
        int code;
        try { code = _process.ExitCode; }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException) { code = -1; }
        _exited.TrySetResult(code);
    }

    private bool HasExited()
    {
        try { return _process.HasExited; }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception) { return true; }
    }

    private static async Task PumpAsync(StreamReader reader, System.Threading.Channels.ChannelWriter<string> writer)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
                writer.TryWrite(line);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The process was disposed while the pump was reading.
        }
        finally
        {
            writer.TryComplete();
        }
    }

    // kill(2) from libc. The runtime maps "libc" to the platform's C library on macOS and Linux.
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int SysKill(int pid, int signal);
}
