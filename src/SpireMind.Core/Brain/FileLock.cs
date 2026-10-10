using System.Diagnostics;
// The mod compiles Core with global usings for Godot and the game, and Godot has its own FileAccess.
// These aliases keep the .NET ones there.
using FileAccess = System.IO.FileAccess;
using Thread = System.Threading.Thread;

namespace SpireMind.Core.Brain;

/// <summary>
/// An exclusive lock shared by every copy of the game on this machine, held for the few
/// milliseconds a read-change-write of a shared file takes (spend.json, the shared notes).
///
/// It opens a lock file with <see cref="FileShare.None"/>, which .NET turns into an flock on
/// macOS and Linux and a share-mode lock on Windows. The lock goes when the handle is closed,
/// also when the process dies, so a crashed copy never leaves it held. flock belongs to the
/// open file, not the process, so two opens in one process conflict too. The lock file itself
/// is never deleted: a copy could otherwise lock a file another copy has just unlinked, and
/// both would think they hold the lock.
/// </summary>
public static class FileLock
{
    /// <summary>How long to wait between tries while another copy holds the lock.</summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(20);

    /// <summary>
    /// Takes the lock at <paramref name="path"/>, creating the file and its folder when needed,
    /// and retrying every <see cref="RetryInterval"/> while someone else holds it. Disposing the
    /// result releases it.
    /// </summary>
    /// <exception cref="TimeoutException">The lock was still held after <paramref name="timeout"/>.</exception>
    /// <exception cref="IOException">The file or its folder can't be created or opened.</exception>
    /// <exception cref="UnauthorizedAccessException">The file can't be opened for writing.</exception>
    public static IDisposable Acquire(string path, TimeSpan timeout)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var waited = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(full, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1);
            }
            catch (IOException ex) when (IsHeld(ex))
            {
                if (waited.Elapsed >= timeout)
                    throw new TimeoutException($"{Path.GetFileName(full)} was still locked after {timeout.TotalSeconds:0.##} s.", ex);
                Thread.Sleep(RetryInterval);
            }
        }
    }

    // The lock being held shows as a plain IOException ("being used by another process").
    // Its subclasses (a missing folder, a path too long) won't go away by waiting.
    private static bool IsHeld(IOException ex) => ex.GetType() == typeof(IOException);
}
