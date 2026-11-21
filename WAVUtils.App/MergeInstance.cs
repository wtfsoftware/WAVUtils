using System.IO.Pipes;
using System.Text;

namespace WAVUtils.App;

/// <summary>
/// Explorer invokes a file-class context menu verb once per selected file (a static
/// verb never receives the whole multi-selection). The first "--explorer" process
/// becomes a collector; the others forward their file over a named pipe and exit.
/// </summary>
internal static class MergeInstance
{
    const string MutexName = @"Local\WAVUtils.Merge";
    const string PipeName = "WAVUtils.Merge";

    // shortcut: the settle window is a heuristic — Explorer starts the per-file
    // processes a few ms apart, but cold starts (AV scan, network share) can push
    // the last client over a short window; 1.2 s cost, measured, split batches at
    // 400 ms. Upgrade to a COM verb (ICommandProvider) if batches still mis-group.
    const int SettleMs = 1200;

    // Held until process exit (released by the OS); never disposed here.
    static Mutex? _mutex;

    /// <summary>
    /// Returns the full input list for this merge. An empty array means the caller's
    /// files were handed to the running collector and the caller must exit. If the
    /// collector is unavailable, the un-forwarded files are returned so the request
    /// is never lost.
    /// </summary>
    public static async Task<string[]> CollectAsync(string[] ownFiles)
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirst);
        if (!isFirst)
        {
            var notHandedOff = new List<string>();
            foreach (var file in ownFiles)
            {
                // One retry: the collector can be mid-restart between pipe instances.
                if (!await SendAsync(file) && !await SendAsync(file))
                    notHandedOff.Add(file);
            }
            return notHandedOff.ToArray();
        }
        return await ServeAsync(ownFiles);
    }

    private static async Task<string[]> ServeAsync(string[] ownFiles)
    {
        // Explorer randomizes PIDs, so selection order is recovered from each
        // process's creation time: Explorer launches the per-file processes in
        // selection order, and GetProcessTimes creation ticks are precise enough
        // to keep sequential CreateProcess calls distinct. Ties keep arrival order.
        var files = new List<(long Order, string Path)>();
        long ownOrder = CreationTicks();
        foreach (var own in ownFiles)
            files.Add((ownOrder, own));
        var quietSince = DateTime.UtcNow;
        while (true)
        {
            int remaining = SettleMs - (int)(DateTime.UtcNow - quietSince).TotalMilliseconds;
            if (remaining <= 0)
                break;

            using var server = new NamedPipeServerStream(
                PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            var wait = server.WaitForConnectionAsync();
            // Observe the Dispose-induced cancellation so the task is never unobserved.
            _ = wait.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
            if (await Task.WhenAny(wait, Task.Delay(remaining)) != wait)
                break;
            await wait;

            string? path = null;
            try
            {
                var read = new StreamReader(server, Encoding.UTF8).ReadLineAsync();
                if (await Task.WhenAny(read, Task.Delay(2000)) == read)
                    path = await read;
            }
            catch (IOException)
            {
                // Client vanished mid-send; it falls back to standalone mode.
            }
            if (path != null)
            {
                // Wire format: "<creationTicks>\t<path>"; entries without a parseable
                // timestamp sort last, in arrival order (OrderBy is stable).
                int tab = path.IndexOf('\t');
                long order = long.MaxValue;
                string filePath = path;
                if (tab > 0 && long.TryParse(path.AsSpan(0, tab), out long parsed))
                {
                    order = parsed;
                    filePath = path.Substring(tab + 1);
                }
                // Record before acking: a broken ack (client closed right after reading
                // it) must not lose a path we already received. If the ack really never
                // arrived, the client's standalone run only yields its own error.
                if (!files.Exists(e => string.Equals(e.Path, filePath, StringComparison.OrdinalIgnoreCase)))
                    files.Add((order, filePath));
                try
                {
                    await server.WriteAsync(Encoding.UTF8.GetBytes("OK\n"));
                }
                catch (IOException)
                {
                    // Client closed the pipe after reading the ack.
                }
            }
            quietSince = DateTime.UtcNow;
        }
        return files.OrderBy(e => e.Order).Select(e => e.Path).ToArray();
    }

    [System.Runtime.InteropServices.DllImport("kernel32")]
    private static extern bool GetProcessTimes(IntPtr h, out long creation, out long exit, out long kernel, out long user);

    private static long CreationTicks()
    {
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        GetProcessTimes(self.Handle, out long creation, out _, out _, out _);
        return creation;
    }

    private static async Task<bool> SendAsync(string file)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            // The collector can be the LAST process ready (cold image load of a fresh
            // build, AV scan, network share) — clients must outlive its startup.
            await pipe.ConnectAsync(15000);
            await pipe.WriteAsync(Encoding.UTF8.GetBytes(CreationTicks() + "\t" + file + "\n"));
            await pipe.FlushAsync();
            var ack = new byte[1];
            int n = await pipe.ReadAsync(ack);
            if (n == 1 && ack[0] == 'O')
            {
                // Don't cut the pipe before the server's ack write completes: wait for
                // its disconnect (bounded) instead of closing right away.
                await Task.WhenAny(pipe.ReadAsync(ack).AsTask(), Task.Delay(500));
                return true;
            }
            return false;
        }
        catch (Exception)
        {
            // Collector dead or busy: caller merges on its own instead of losing the file.
            return false;
        }
    }
}
