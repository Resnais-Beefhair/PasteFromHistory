using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using PasteFromHistory.Core;

namespace PasteFromHistory.Services;

internal sealed record LaunchRequest(string Action, string? Folder = null, long Target = 0);

internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly CancellationTokenSource stop = new();
    private readonly string pipeName;
    public bool IsFirst { get; }

    public SingleInstance(string scope = "")
    {
        var identity = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity + scope)))[..16];
        pipeName = Compatibility.LegacyName + "-" + suffix + "-" + System.Diagnostics.Process.GetCurrentProcess().SessionId;
        mutex = new Mutex(true, @"Local\" + pipeName, out var first);
        IsFirst = first;
    }

    public void Listen(Action<LaunchRequest> receive)
    {
        _ = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(stop.Token);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    timeout.CancelAfter(3000);
                    using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                    using var reader = new StreamReader(pipe, leaveOpen: true);
                    await writer.WriteLineAsync(Environment.ProcessId.ToString());
                    var message = await reader.ReadLineAsync(timeout.Token);
                    if (message is { Length: < 16384 } && JsonSerializer.Deserialize<LaunchRequest>(message) is { } request)
                        receive(request);
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException) { }
            }
        });
    }

    public bool Send(LaunchRequest request)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
            pipe.Connect(3500);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            var greeting = reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            if (uint.TryParse(greeting, out var pid)) Native.AllowSetForegroundWindow(pid);
            writer.WriteLine(JsonSerializer.Serialize(request));
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException) { return false; }
    }

    public void Dispose()
    {
        stop.Cancel();
        if (IsFirst) mutex.ReleaseMutex();
        mutex.Dispose();
        // The listener can still observe the cancellation token during shutdown.
    }
}
