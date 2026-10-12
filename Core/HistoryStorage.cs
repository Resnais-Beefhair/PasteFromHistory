using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PasteFromHistory.Core;

public sealed class HistoryStorage(string filePath)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes(Compatibility.LegacyName + ".storage.v1");
    private readonly object gate = new();
    private (ClipEntry[] Entries, bool Enabled)? pending;
    private Task worker = Task.CompletedTask;
    public event Action<string>? Failed;

    public ClipEntry[] Load()
    {
        if (!File.Exists(filePath)) return [];
        if (new FileInfo(filePath).Length > 100L * 1024 * 1024)
            throw new InvalidDataException("The history file is too large.");
        var bytes = ProtectedData.Unprotect(File.ReadAllBytes(filePath), Entropy, DataProtectionScope.CurrentUser);
        try { return JsonSerializer.Deserialize<ClipEntry[]>(bytes) ?? []; }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public void Schedule(IReadOnlyList<ClipEntry> entries, bool enabled)
    {
        lock (gate)
        {
            pending = (entries.ToArray(), enabled);
            if (worker.IsCompleted) worker = Task.Run(Work);
        }
    }

    private void Work()
    {
        while (true)
        {
            (ClipEntry[] Entries, bool Enabled) job;
            lock (gate)
            {
                if (pending == null) { worker = Task.CompletedTask; return; }
                job = pending.Value; pending = null;
            }
            try
            {
                if (!job.Enabled) { File.Delete(filePath); continue; }
                var json = JsonSerializer.SerializeToUtf8Bytes(job.Entries);
                byte[] encrypted;
                try { encrypted = ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser); }
                finally { CryptographicOperations.ZeroMemory(json); }
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                var temp = filePath + ".tmp";
                File.WriteAllBytes(temp, encrypted);
                File.Move(temp, filePath, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
            {
                Failed?.Invoke("History could not be saved. Check folder permissions and free disk space.");
            }
        }
    }

    public void Flush() { Task current; lock (gate) current = worker; current.GetAwaiter().GetResult(); }
}
