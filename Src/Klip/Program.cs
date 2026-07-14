using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

return await KlipApp.RunAsync(args);

static class Defaults
{
    public const int Port = 45245;
    public const int ChunkSize = 64 * 1024;
}

static class KlipApp
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return await InteractiveMenu.RunAsync();
        }

        if (IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            shutdown.Cancel();
            Console.WriteLine();
            Console.WriteLine("Stopping...");
        };

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "send" => await SendCommand.RunAsync(args[1..], shutdown.Token),
                "receive" or "recv" => await ReceiveCommand.RunAsync(args[1..], shutdown.Token),
                "server" => await SyncServerCommand.RunAsync(args[1..], shutdown.Token),
                "client" => await SyncClientCommand.RunAsync(args[1..], shutdown.Token),
                _ => Fail($"Unknown command '{args[0]}'. Run 'Klip --help'.")
            };
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
        catch (KlipException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        catch (SocketException ex)
        {
            Console.Error.WriteLine($"Socket error: {ex.Message}");
            return 1;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"I/O error: {ex.Message}");
            return 1;
        }
    }

    private static bool IsHelp(string value) =>
        value is "-h" or "--help" or "help" or "/?";

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"Error: {message}");
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
        Klip - TCP file transfer for .NET 8

        Usage:
          Klip receive [--port 45245] [--out <folder>] [--overwrite] [--clipboard]
          Klip send <file> --host <address> [--port 45245] [--chunk-size <bytes>]
          Klip send --clipboard --host <address> [--port 45245]
          Klip server [--port 45245]
          Klip client --host <address> [--port 45245] [--content-port 45246]

        Examples:
          Klip receive --port 45245 --out received
          Klip receive --clipboard --out received
          Klip send .\photo.jpg --host 192.168.1.20 --port 45245
          Klip send --clipboard --host 192.168.1.20
          Klip server
          Klip client --host 192.168.1.20

        Protocol:
          KLIP/1 framed TCP stream with metadata, data chunks, ACK/ERROR frames,
          and SHA-256 verification at the receiver.
        """);
    }
}

static class InteractiveMenu
{
    public static async Task<int> RunAsync()
    {
        while (true)
        {
            TryClear();
            Console.WriteLine("Klip");
            Console.WriteLine("====");
            Console.WriteLine("1. Server - receive synced clipboard and paste here");
            Console.WriteLine("2. Client - watch this clipboard and sync to server");
            Console.WriteLine("3. Send clipboard once");
            Console.WriteLine("4. Send file once");
            Console.WriteLine("5. Receive file once");
            Console.WriteLine("6. Help");
            Console.WriteLine("0. Exit");
            Console.WriteLine();

            var choice = Prompt("Choose", "1");
            Console.WriteLine();

            var args = choice switch
            {
                "1" => BuildServerArgs(),
                "2" => BuildClientArgs(),
                "3" => BuildSendClipboardArgs(),
                "4" => BuildSendFileArgs(),
                "5" => BuildReceiveArgs(),
                "6" => ["--help"],
                "0" => null,
                _ => []
            };

            if (args is null)
            {
                return 0;
            }

            if (args.Length == 0)
            {
                Pause("Unknown choice.");
                continue;
            }

            Console.WriteLine();
            var result = await KlipApp.RunAsync(args);

            if (args[0] is "server" or "client" || result == 130)
            {
                return result;
            }

            Pause("Press Enter to return to the menu...");
        }
    }

    private static string[] BuildServerArgs()
    {
        var port = Prompt("Server port", Defaults.Port.ToString());
        return ["server", "--port", port];
    }

    private static string[] BuildClientArgs()
    {
        var host = PromptRequired("Server IP or host");
        var port = Prompt("Server port", Defaults.Port.ToString());
        var contentPort = Prompt("Client content port", (Defaults.Port + 1).ToString());
        return ["client", "--host", host, "--port", port, "--content-port", contentPort];
    }

    private static string[] BuildSendClipboardArgs()
    {
        var host = PromptRequired("Receiver IP or host");
        var port = Prompt("Receiver port", Defaults.Port.ToString());
        return ["send", "--clipboard", "--host", host, "--port", port];
    }

    private static string[] BuildSendFileArgs()
    {
        var path = PromptRequired("File path");
        var host = PromptRequired("Receiver IP or host");
        var port = Prompt("Receiver port", Defaults.Port.ToString());
        return ["send", path, "--host", host, "--port", port];
    }

    private static string[] BuildReceiveArgs()
    {
        var port = Prompt("Receiver port", Defaults.Port.ToString());
        var output = Prompt("Output folder", "received");
        var copyToClipboard = PromptYesNo("Put received file on clipboard", defaultValue: true);

        return copyToClipboard
            ? ["receive", "--port", port, "--out", output, "--clipboard"]
            : ["receive", "--port", port, "--out", output];
    }

    private static string Prompt(string label, string defaultValue)
    {
        Console.Write($"{label} [{defaultValue}]: ");
        var value = Console.ReadLine();
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
    }

    private static string PromptRequired(string label)
    {
        while (true)
        {
            Console.Write($"{label}: ");
            var value = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim().Trim('"');
            }

            Console.WriteLine("Please enter a value.");
        }
    }

    private static bool PromptYesNo(string label, bool defaultValue)
    {
        var suffix = defaultValue ? "Y/n" : "y/N";
        Console.Write($"{label} [{suffix}]: ");
        var value = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Trim().Equals("y", StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static void Pause(string message)
    {
        Console.WriteLine();
        Console.Write(message);
        Console.ReadLine();
    }

    private static void TryClear()
    {
        try
        {
            Console.Clear();
        }
        catch (IOException)
        {
            Console.WriteLine();
        }
    }
}

static class SendCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var options = SendOptions.Parse(args);
        var source = options.UseClipboard
            ? ClipboardTransferSource.Detect()
            : TransferSource.FromFile(options.FilePath ?? throw new KlipException("Missing file path."));

        Console.WriteLine($"Source: {source.Description}");
        Console.WriteLine($"Hashing {source.FileName}...");
        var hash = await HashStreamAsync(source.OpenRead(), cancellationToken);
        var metadata = new TransferMetadata(
            FileName: source.FileName,
            Length: source.Length,
            Sha256: Convert.ToHexString(hash).ToLowerInvariant());

        using var tcpClient = new TcpClient();
        Console.WriteLine($"Connecting to {options.Host}:{options.Port}...");
        await tcpClient.ConnectAsync(options.Host, options.Port, cancellationToken);
        tcpClient.NoDelay = true;

        await using var network = tcpClient.GetStream();
        var metadataBytes = JsonSerializer.SerializeToUtf8Bytes(metadata, JsonDefaults.Options);
        await KlipProtocol.WriteFrameAsync(network, FrameType.Metadata, metadataBytes, cancellationToken);
        await KlipProtocol.ExpectAckAsync(network, "metadata", cancellationToken);

        Console.WriteLine($"Sending {metadata.FileName} ({FormatBytes(metadata.Length)})...");
        await using var file = source.OpenRead();
        var buffer = new byte[options.ChunkSize];
        long sent = 0;
        int read;

        while ((read = await file.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await KlipProtocol.WriteFrameAsync(network, FrameType.Data, buffer.AsMemory(0, read), cancellationToken);
            sent += read;
            PrintProgress("Sent", sent, metadata.Length);
        }

        await KlipProtocol.WriteFrameAsync(network, FrameType.End, ReadOnlyMemory<byte>.Empty, cancellationToken);
        await KlipProtocol.ExpectAckAsync(network, "transfer complete", cancellationToken);

        Console.WriteLine();
        Console.WriteLine($"Done. Sent {metadata.FileName} ({FormatBytes(metadata.Length)}).");
        return 0;
    }

    private static async Task<byte[]> HashStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        await using var ownedStream = stream;
        using var sha256 = SHA256.Create();
        return await sha256.ComputeHashAsync(ownedStream, cancellationToken);
    }

    private static void PrintProgress(string label, long current, long total)
    {
        var percent = total == 0 ? 100 : current * 100d / total;
        Console.Write($"\r{label}: {FormatBytes(current)} / {FormatBytes(total)} ({percent:0.0}%)");
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}

static class ReceiveCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var options = ReceiveOptions.Parse(args);
        Directory.CreateDirectory(options.OutputFolder);

        var listener = new TcpListener(IPAddress.Any, options.Port);
        listener.Start();

        try
        {
            Console.WriteLine($"Listening on 0.0.0.0:{options.Port}");
            Console.WriteLine($"Saving files to {Path.GetFullPath(options.OutputFolder)}");

            while (!cancellationToken.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken);
                client.NoDelay = true;

                var endpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown peer";
                Console.WriteLine($"Connection from {endpoint}");

                try
                {
                    await ReceiveOneFileAsync(client, options, cancellationToken);
                }
                catch (KlipException ex)
                {
                    Console.Error.WriteLine($"Transfer failed: {ex.Message}");
                    await TrySendErrorAsync(client, ex.Message, cancellationToken);
                }
            }
        }
        finally
        {
            listener.Stop();
        }

        return 0;
    }

    private static async Task ReceiveOneFileAsync(TcpClient client, ReceiveOptions options, CancellationToken cancellationToken)
    {
        await using var network = client.GetStream();
        var metadataFrame = await KlipProtocol.ReadFrameAsync(network, cancellationToken);

        if (metadataFrame.Type != FrameType.Metadata)
        {
            throw new KlipException("Expected metadata frame.");
        }

        var metadata = JsonSerializer.Deserialize<TransferMetadata>(
                metadataFrame.Payload.Array!.AsSpan(metadataFrame.Payload.Offset, metadataFrame.Payload.Count),
                JsonDefaults.Options)
            ?? throw new KlipException("Invalid metadata payload.");

        metadata.Validate();
        var destination = ResolveDestination(options.OutputFolder, metadata.FileName, options.Overwrite);
        var tempPath = destination + ".klip-part";

        Console.WriteLine($"Receiving {metadata.FileName} ({FormatBytes(metadata.Length)})");
        await KlipProtocol.WriteAckAsync(network, $"Ready for {metadata.FileName}", cancellationToken);

        long received = 0;
        using var sha256 = SHA256.Create();

        try
        {
            await using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, Defaults.ChunkSize, useAsync: true))
            {
                while (true)
                {
                    var frame = await KlipProtocol.ReadFrameAsync(network, cancellationToken);

                    if (frame.Type == FrameType.End)
                    {
                        break;
                    }

                    if (frame.Type != FrameType.Data)
                    {
                        throw new KlipException($"Unexpected frame type: {frame.Type}");
                    }

                    received += frame.Payload.Count;
                    if (received > metadata.Length)
                    {
                        throw new KlipException("Received more data than metadata declared.");
                    }

                    await output.WriteAsync(frame.Payload, cancellationToken);
                    sha256.TransformBlock(frame.Payload.Array!, frame.Payload.Offset, frame.Payload.Count, null, 0);
                    PrintProgress("Received", received, metadata.Length);
                }

                sha256.TransformFinalBlock([], 0, 0);
            }

            if (received != metadata.Length)
            {
                throw new KlipException($"Expected {metadata.Length} bytes but received {received} bytes.");
            }

            var actualHash = Convert.ToHexString(sha256.Hash ?? []).ToLowerInvariant();
            if (!StringComparer.OrdinalIgnoreCase.Equals(actualHash, metadata.Sha256))
            {
                throw new KlipException("SHA-256 verification failed.");
            }

            if (File.Exists(destination) && options.Overwrite)
            {
                File.Delete(destination);
            }

            File.Move(tempPath, destination);
            if (options.CopyToClipboard)
            {
                VirtualClipboard.SetFile(Path.GetFullPath(destination), metadata.FileName);
                Console.WriteLine();
                Console.WriteLine("Copied received file to the virtual clipboard.");
            }

            await KlipProtocol.WriteAckAsync(network, "Transfer complete", cancellationToken);

            Console.WriteLine();
            Console.WriteLine($"Saved {destination}");
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    private static async Task TrySendErrorAsync(TcpClient client, string message, CancellationToken cancellationToken)
    {
        try
        {
            await using var network = client.GetStream();
            await KlipProtocol.WriteFrameAsync(network, FrameType.Error, Encoding.UTF8.GetBytes(message), cancellationToken);
        }
        catch
        {
            // The client may already be disconnected; the local error has been printed.
        }
    }

    private static string ResolveDestination(string outputFolder, string fileName, bool overwrite)
    {
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            throw new KlipException("Metadata did not include a valid file name.");
        }

        var destination = Path.Combine(outputFolder, safeName);
        if (overwrite || !File.Exists(destination))
        {
            return destination;
        }

        var name = Path.GetFileNameWithoutExtension(safeName);
        var extension = Path.GetExtension(safeName);

        for (var i = 1; i < 10_000; i++)
        {
            var candidate = Path.Combine(outputFolder, $"{name} ({i}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new KlipException("Could not find a free output file name.");
    }

    private static void PrintProgress(string label, long current, long total)
    {
        var percent = total == 0 ? 100 : current * 100d / total;
        Console.Write($"\r{label}: {FormatBytes(current)} / {FormatBytes(total)} ({percent:0.0}%)");
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}

static class SyncServerCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var options = SyncServerOptions.Parse(args);
        var listener = new TcpListener(IPAddress.Any, options.Port);
        listener.Start();

        try
        {
            Console.WriteLine($"Klip server listening on 0.0.0.0:{options.Port}");
            Console.WriteLine("Waiting for clipboard sync clients...");

            while (!cancellationToken.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken);
                client.NoDelay = true;

                var remoteEndpoint = (IPEndPoint?)client.Client.RemoteEndPoint;
                var remoteAddress = remoteEndpoint?.Address.ToString() ?? "127.0.0.1";
                Console.WriteLine($"Client connected from {remoteEndpoint}");

                try
                {
                    await RunClientSessionAsync(client, remoteAddress, cancellationToken);
                }
                catch (KlipException ex)
                {
                    Console.Error.WriteLine($"Client session ended: {ex.Message}");
                }
                catch (IOException ex)
                {
                    Console.Error.WriteLine($"Client disconnected: {ex.Message}");
                }
            }
        }
        finally
        {
            listener.Stop();
        }

        return 0;
    }

    private static async Task RunClientSessionAsync(TcpClient client, string remoteAddress, CancellationToken cancellationToken)
    {
        await using var stream = client.GetStream();

        while (!cancellationToken.IsCancellationRequested)
        {
            var message = await SyncProtocol.ReadMessageAsync(stream, cancellationToken);
            switch (message.Type)
            {
                case SyncMessageTypes.TextChanged:
                    if (message.Text is null)
                    {
                        throw new KlipException("Clipboard text update did not include text.");
                    }

                    try
                    {
                        WindowsClipboard.SetUnicodeText(message.Text);
                        Console.WriteLine($"Clipboard text updated ({message.Text.Length} chars).");
                    }
                    catch (KlipException ex)
                    {
                        Console.Error.WriteLine($"Clipboard text update failed: {ex.Message}");
                    }
                    break;

                case SyncMessageTypes.FilesChanged:
                    if (message.TransferId is null || message.TransferPort is null || message.Items is null || message.Items.Count == 0)
                    {
                        throw new KlipException("Clipboard file update was incomplete.");
                    }

                    try
                    {
                        var provider = new RemoteVirtualFileProvider(remoteAddress, message.TransferPort.Value, message.TransferId);
                        VirtualClipboard.SetFiles(message.Items, provider.ReadFileBytes);
                        Console.WriteLine($"Clipboard virtual files updated ({message.Items.Count} item(s), live IDataObject, copy drop effect). Bytes will transfer on paste.");
                    }
                    catch (KlipException ex)
                    {
                        Console.Error.WriteLine($"Clipboard virtual file update failed: {ex.Message}");
                    }
                    break;

                default:
                    throw new KlipException($"Unknown sync message type: {message.Type}");
            }
        }
    }
}

static class SyncClientCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var options = SyncClientOptions.Parse(args);
        var transferStore = new TransferStore();

        var transferServer = RunTransferServerAsync(options.ContentPort, transferStore, cancellationToken);

        using var client = new TcpClient();
        Console.WriteLine($"Connecting to Klip server {options.Host}:{options.Port}...");
        await client.ConnectAsync(options.Host, options.Port, cancellationToken);
        client.NoDelay = true;

        await using var stream = client.GetStream();
        Console.WriteLine($"Clipboard sync started. Content listener is on port {options.ContentPort}.");

        string? lastSnapshotKey = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var snapshot = ClipboardSnapshot.Read();
            if (snapshot is not null && snapshot.Key != lastSnapshotKey)
            {
                lastSnapshotKey = snapshot.Key;
                var message = snapshot.ToSyncMessage(transferStore, options.ContentPort);
                if (message is not null)
                {
                    await SyncProtocol.WriteMessageAsync(stream, message, cancellationToken);
                    Console.WriteLine(snapshot.Kind == ClipboardSnapshotKind.Text
                        ? "Sent clipboard text update."
                        : "Sent virtual file metadata. File bytes will wait until paste.");
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
        }

        await transferServer;
        return 0;
    }

    private static async Task RunTransferServerAsync(int port, TransferStore transferStore, CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => HandleTransferClientAsync(client, transferStore, cancellationToken), cancellationToken);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task HandleTransferClientAsync(TcpClient client, TransferStore transferStore, CancellationToken cancellationToken)
    {
        using (client)
        {
            client.NoDelay = true;
            await using var stream = client.GetStream();
            var request = await SyncProtocol.ReadTransferRequestAsync(stream, cancellationToken);
            var path = transferStore.ResolvePath(request.TransferId, request.Index);

            await using var file = File.OpenRead(path);
            var buffer = new byte[Defaults.ChunkSize];
            int read;

            while ((read = await file.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await KlipProtocol.WriteFrameAsync(stream, FrameType.Data, buffer.AsMemory(0, read), cancellationToken);
            }

            await KlipProtocol.WriteFrameAsync(stream, FrameType.End, ReadOnlyMemory<byte>.Empty, cancellationToken);
        }
    }
}

static class SyncProtocol
{
    public static async Task WriteMessageAsync(Stream stream, SyncMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonDefaults.Options);
        await KlipProtocol.WriteFrameAsync(stream, FrameType.Control, payload, cancellationToken);
    }

    public static async Task<SyncMessage> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
    {
        var frame = await KlipProtocol.ReadFrameAsync(stream, cancellationToken);
        if (frame.Type != FrameType.Control)
        {
            throw new KlipException($"Expected control frame but received {frame.Type}.");
        }

        return JsonSerializer.Deserialize<SyncMessage>(
                frame.Payload.Array!.AsSpan(frame.Payload.Offset, frame.Payload.Count),
                JsonDefaults.Options)
            ?? throw new KlipException("Invalid sync message.");
    }

    public static async Task WriteTransferRequestAsync(Stream stream, TransferRequest request, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(request, JsonDefaults.Options);
        await KlipProtocol.WriteFrameAsync(stream, FrameType.Control, payload, cancellationToken);
    }

    public static async Task<TransferRequest> ReadTransferRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        var frame = await KlipProtocol.ReadFrameAsync(stream, cancellationToken);
        if (frame.Type != FrameType.Control)
        {
            throw new KlipException($"Expected transfer request but received {frame.Type}.");
        }

        return JsonSerializer.Deserialize<TransferRequest>(
                frame.Payload.Array!.AsSpan(frame.Payload.Offset, frame.Payload.Count),
                JsonDefaults.Options)
            ?? throw new KlipException("Invalid transfer request.");
    }
}

static class SyncMessageTypes
{
    public const string TextChanged = "clipboard.text";
    public const string FilesChanged = "clipboard.files";
}

sealed record SyncMessage(
    string Type,
    string? Text = null,
    string? TransferId = null,
    int? TransferPort = null,
    List<VirtualFileItem>? Items = null);

sealed record TransferRequest(string TransferId, int Index);

sealed record VirtualFileItem(string RelativePath, long Length, int Attributes);

sealed class RemoteVirtualFileProvider
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _transferId;

    public RemoteVirtualFileProvider(string host, int port, string transferId)
    {
        _host = host;
        _port = port;
        _transferId = transferId;
    }

    public byte[] ReadFileBytes(int index)
    {
        using var client = new TcpClient();
        client.Connect(_host, _port);
        client.NoDelay = true;

        using var stream = client.GetStream();
        SyncProtocol.WriteTransferRequestAsync(stream, new TransferRequest(_transferId, index), CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        using var output = new MemoryStream();
        while (true)
        {
            var frame = KlipProtocol.ReadFrameAsync(stream, CancellationToken.None).GetAwaiter().GetResult();
            if (frame.Type == FrameType.End)
            {
                break;
            }

            if (frame.Type != FrameType.Data)
            {
                throw new KlipException($"Unexpected transfer frame: {frame.Type}");
            }

            output.Write(frame.Payload.Array!, frame.Payload.Offset, frame.Payload.Count);
        }

        return output.ToArray();
    }
}

sealed class TransferStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<string>> _transfers = [];

    public string Add(List<string> sourcePaths)
    {
        var transferId = Guid.NewGuid().ToString("N");
        lock (_gate)
        {
            _transfers[transferId] = sourcePaths;
        }

        return transferId;
    }

    public string ResolvePath(string transferId, int index)
    {
        lock (_gate)
        {
            if (!_transfers.TryGetValue(transferId, out var paths))
            {
                throw new KlipException("Transfer is no longer available.");
            }

            if (index < 0 || index >= paths.Count)
            {
                throw new KlipException("Transfer requested an invalid file index.");
            }

            return paths[index];
        }
    }
}

sealed record ClipboardSnapshot(ClipboardSnapshotKind Kind, string Fingerprint, string? Text, List<string> Paths)
{
    public string Key => $"{Kind}:{Fingerprint}";

    public static ClipboardSnapshot? Read()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new KlipException("Clipboard sync is currently supported on Windows only.");
        }

        var paths = WindowsClipboard.ReadFileDropPaths();
        if (paths.Count > 0)
        {
            var fingerprint = string.Join('\n', paths.Select(path =>
            {
                if (File.Exists(path))
                {
                    var info = new FileInfo(path);
                    return $"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
                }

                if (Directory.Exists(path))
                {
                    var info = new DirectoryInfo(path);
                    return $"{path}|dir|{info.LastWriteTimeUtc.Ticks}";
                }

                return path;
            }));

            return new ClipboardSnapshot(ClipboardSnapshotKind.Files, fingerprint, null, paths);
        }

        var text = WindowsClipboard.ReadUnicodeTextValue();
        if (text is not null)
        {
            return new ClipboardSnapshot(ClipboardSnapshotKind.Text, text, text, []);
        }

        return null;
    }

    public SyncMessage? ToSyncMessage(TransferStore transferStore, int transferPort)
    {
        if (Kind == ClipboardSnapshotKind.Text)
        {
            return new SyncMessage(SyncMessageTypes.TextChanged, Text: Text ?? string.Empty);
        }

        var flattened = FlattenPaths(Paths);
        if (flattened.Count == 0)
        {
            return null;
        }

        var transferId = transferStore.Add(flattened.Select(item => item.SourcePath).ToList());
        return new SyncMessage(
            SyncMessageTypes.FilesChanged,
            TransferId: transferId,
            TransferPort: transferPort,
            Items: flattened.Select(item => new VirtualFileItem(item.RelativePath, item.Length, (int)item.Attributes)).ToList());
    }

    private static List<FlattenedClipboardFile> FlattenPaths(List<string> paths)
    {
        var result = new List<FlattenedClipboardFile>();

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                result.Add(new FlattenedClipboardFile(info.Name, info.FullName, info.Length, info.Attributes));
                continue;
            }

            if (!Directory.Exists(path))
            {
                continue;
            }

            var root = new DirectoryInfo(path);
            foreach (var file in root.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                var relative = Path.Combine(root.Name, Path.GetRelativePath(root.FullName, file.FullName));
                result.Add(new FlattenedClipboardFile(relative, file.FullName, file.Length, file.Attributes));
            }
        }

        return result;
    }
}

sealed record FlattenedClipboardFile(string RelativePath, string SourcePath, long Length, FileAttributes Attributes);

enum ClipboardSnapshotKind
{
    Text,
    Files
}

static class KlipProtocol
{
    private const int HeaderSize = 12;
    private const int MaxPayloadSize = 128 * 1024 * 1024;
    private static readonly byte[] Magic = "KLIP"u8.ToArray();

    public static async Task WriteFrameAsync(Stream stream, FrameType type, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (payload.Length > MaxPayloadSize)
        {
            throw new KlipException($"Frame payload too large: {payload.Length} bytes.");
        }

        var header = new byte[HeaderSize];
        Magic.CopyTo(header, 0);
        header[4] = 1;
        header[5] = (byte)type;
        BinaryPrimitives.WriteInt16BigEndian(header.AsSpan(6, 2), 0);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(8, 4), payload.Length);

        await stream.WriteAsync(header, cancellationToken);
        if (!payload.IsEmpty)
        {
            await stream.WriteAsync(payload, cancellationToken);
        }

        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<KlipFrame> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[HeaderSize];
        await ReadExactlyAsync(stream, header, cancellationToken);

        if (!header.AsSpan(0, 4).SequenceEqual(Magic))
        {
            throw new KlipException("Invalid protocol magic.");
        }

        if (header[4] != 1)
        {
            throw new KlipException($"Unsupported protocol version: {header[4]}.");
        }

        var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(8, 4));
        if (length < 0 || length > MaxPayloadSize)
        {
            throw new KlipException($"Invalid payload length: {length}.");
        }

        var payload = new byte[length];
        if (length > 0)
        {
            await ReadExactlyAsync(stream, payload, cancellationToken);
        }

        var type = (FrameType)header[5];
        if (!Enum.IsDefined(type))
        {
            throw new KlipException($"Unknown frame type: {header[5]}.");
        }

        return new KlipFrame(type, payload);
    }

    public static Task WriteAckAsync(Stream stream, string message, CancellationToken cancellationToken) =>
        WriteFrameAsync(stream, FrameType.Ack, Encoding.UTF8.GetBytes(message), cancellationToken);

    public static async Task ExpectAckAsync(Stream stream, string phase, CancellationToken cancellationToken)
    {
        var frame = await ReadFrameAsync(stream, cancellationToken);
        var message = Encoding.UTF8.GetString(frame.Payload.Array!, frame.Payload.Offset, frame.Payload.Count);

        if (frame.Type == FrameType.Error)
        {
            throw new KlipException($"Remote error during {phase}: {message}");
        }

        if (frame.Type != FrameType.Ack)
        {
            throw new KlipException($"Expected ACK during {phase} but received {frame.Type}.");
        }
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var total = 0;

        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[total..], cancellationToken);
            if (read == 0)
            {
                throw new KlipException("Connection closed unexpectedly.");
            }

            total += read;
        }
    }
}

sealed record TransferSource(string FileName, long Length, string Description, Func<Stream> OpenRead)
{
    public static TransferSource FromFile(string path)
    {
        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists)
        {
            throw new KlipException($"File not found: {path}");
        }

        return new TransferSource(
            Path.GetFileName(fileInfo.FullName),
            fileInfo.Length,
            fileInfo.FullName,
            () => File.OpenRead(fileInfo.FullName));
    }

    public static TransferSource FromText(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var fileName = $"clipboard-{DateTime.Now:yyyyMMdd-HHmmss}.txt";

        return new TransferSource(
            fileName,
            bytes.LongLength,
            "clipboard text",
            () => new MemoryStream(bytes, writable: false));
    }
}

static class ClipboardTransferSource
{
    public static TransferSource Detect()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new KlipException("Clipboard detection is currently supported on Windows only.");
        }

        return WindowsClipboard.ReadFileDrop()
            ?? WindowsClipboard.ReadUnicodeText()
            ?? throw new KlipException("Clipboard does not contain a readable file path or text.");
    }
}

static class WindowsClipboard
{
    private const uint CfUnicodeText = 13;
    private const uint CfHDrop = 15;

    public static TransferSource? ReadFileDrop()
    {
        var paths = ReadFileDropPaths();
        return paths.Select(path => File.Exists(path) ? TransferSource.FromFile(path) : null).FirstOrDefault(source => source is not null);
    }

    public static List<string> ReadFileDropPaths()
    {
        using var clipboard = Open();
        var paths = new List<string>();

        if (!IsClipboardFormatAvailable(CfHDrop))
        {
            return paths;
        }

        var handle = GetClipboardData(CfHDrop);
        if (handle == IntPtr.Zero)
        {
            return paths;
        }

        var count = DragQueryFile(handle, 0xFFFFFFFF, null, 0);
        for (uint i = 0; i < count; i++)
        {
            var length = DragQueryFile(handle, i, null, 0);
            if (length == 0)
            {
                continue;
            }

            var builder = new StringBuilder((int)length + 1);
            DragQueryFile(handle, i, builder, (uint)builder.Capacity);
            var path = builder.ToString();
            if (File.Exists(path) || Directory.Exists(path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    public static TransferSource? ReadUnicodeText()
    {
        var text = ReadUnicodeTextValue();
        return string.IsNullOrEmpty(text) ? null : TransferSource.FromText(text);
    }

    public static string? ReadUnicodeTextValue()
    {
        using var clipboard = Open();
        if (!IsClipboardFormatAvailable(CfUnicodeText))
        {
            return null;
        }

        var handle = GetClipboardData(CfUnicodeText);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var locked = GlobalLock(handle);
        if (locked == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(locked);
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    public static void SetUnicodeText(string text)
    {
        using var clipboard = Open();
        if (!EmptyClipboard())
        {
            throw new KlipException("Could not empty the clipboard.");
        }

        var bytes = Encoding.Unicode.GetBytes(text + '\0');
        var handle = GlobalAlloc(0x0002, (UIntPtr)bytes.Length);
        if (handle == IntPtr.Zero)
        {
            throw new KlipException("Could not allocate clipboard memory.");
        }

        var locked = GlobalLock(handle);
        if (locked == IntPtr.Zero)
        {
            GlobalFree(handle);
            throw new KlipException("Could not lock clipboard memory.");
        }

        try
        {
            Marshal.Copy(bytes, 0, locked, bytes.Length);
        }
        finally
        {
            GlobalUnlock(handle);
        }

        if (SetClipboardData(CfUnicodeText, handle) == IntPtr.Zero)
        {
            GlobalFree(handle);
            throw new KlipException("Could not set clipboard text.");
        }
    }

    private static ClipboardHandle Open()
    {
        if (!OpenClipboard(IntPtr.Zero))
        {
            throw new KlipException("Could not open the clipboard. Try again after the current clipboard owner releases it.");
        }

        return new ClipboardHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(int uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder? lpszFile, uint cch);

    private sealed class ClipboardHandle : IDisposable
    {
        public void Dispose() => CloseClipboard();
    }
}

static class VirtualClipboard
{
#pragma warning disable CA1416
    private static readonly Lazy<StaClipboardWorker> Worker = new(() => new StaClipboardWorker());
#pragma warning restore CA1416
    private static System.Runtime.InteropServices.ComTypes.IDataObject? s_currentDataObject;

    public static void SetFile(string path, string displayName)
    {
        var info = new FileInfo(path);
        SetFiles(
        [
            new VirtualFileItem(displayName, info.Length, (int)info.Attributes)
        ], index =>
        {
            if (index != 0)
            {
                throw new KlipException("Invalid clipboard file index.");
            }

            return File.ReadAllBytes(path);
        });
    }

    public static void SetFiles(IReadOnlyList<VirtualFileItem> items, Func<int, byte[]> readFileBytes)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new KlipException("Receiver clipboard mode is currently supported on Windows only.");
        }

        Worker.Value.Invoke(() =>
        {
            var dataObject = new VirtualFileDataObject(items, readFileBytes);
            var setResult = OleSetClipboard(dataObject);
            if (setResult < 0)
            {
                throw new KlipException($"OleSetClipboard failed with HRESULT 0x{setResult:X8}.");
            }

            s_currentDataObject = dataObject;
        });
    }

    [DllImport("ole32.dll")]
    private static extern int OleSetClipboard(System.Runtime.InteropServices.ComTypes.IDataObject pDataObj);
}

[SupportedOSPlatform("windows")]
sealed class StaClipboardWorker
{
    private readonly BlockingCollection<ClipboardWorkItem> _queue = [];

    public StaClipboardWorker()
    {
        var thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Klip Clipboard STA"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public void Invoke(Action action)
    {
        using var done = new ManualResetEventSlim();
        var item = new ClipboardWorkItem(action, done);
        _queue.Add(item);
        done.Wait();

        if (item.Failure is not null)
        {
            throw item.Failure;
        }
    }

    private void Run()
    {
        var initialized = OleInitialize(IntPtr.Zero);
        if (initialized < 0)
        {
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                item.Failure = new KlipException($"OleInitialize failed with HRESULT 0x{initialized:X8}.");
                item.Done.Set();
            }

            return;
        }

        try
        {
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                try
                {
                    item.Action();
                }
                catch (Exception ex)
                {
                    item.Failure = ex;
                }
                finally
                {
                    item.Done.Set();
                }
            }
        }
        finally
        {
            OleUninitialize();
        }
    }

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(IntPtr pvReserved);

    [DllImport("ole32.dll")]
    private static extern void OleUninitialize();
}

sealed class ClipboardWorkItem
{
    public ClipboardWorkItem(Action action, ManualResetEventSlim done)
    {
        Action = action;
        Done = done;
    }

    public Action Action { get; }

    public ManualResetEventSlim Done { get; }

    public Exception? Failure { get; set; }
}

sealed class VirtualFileDataObject : System.Runtime.InteropServices.ComTypes.IDataObject
{
    private const int SOk = 0;
    private const int ENotImpl = unchecked((int)0x80004001);
    private const int DV_E_FORMATETC = unchecked((int)0x80040064);
    private const int DV_E_TYMED = unchecked((int)0x80040069);
    private const int DataSSameFormatEtc = 0x00040130;
    private const int GmemMoveable = 0x0002;
    private const int FileDescriptorSize = 592;
    private const int MaxPath = 260;
    private const int FdAttributes = 0x00000004;
    private const int FdFileSize = 0x00000040;
    private const int DropEffectCopy = 1;

    private static readonly short FileGroupDescriptorFormat = unchecked((short)RegisterClipboardFormat("FileGroupDescriptorW"));
    private static readonly short FileContentsFormat = unchecked((short)RegisterClipboardFormat("FileContents"));
    private static readonly short PreferredDropEffectFormat = unchecked((short)RegisterClipboardFormat("Preferred DropEffect"));
    private readonly IReadOnlyList<VirtualFileItem> _items;
    private readonly Func<int, byte[]> _readFileBytes;

    public VirtualFileDataObject(IReadOnlyList<VirtualFileItem> items, Func<int, byte[]> readFileBytes)
    {
        _items = items.Count > 0 ? items : throw new KlipException("Virtual clipboard needs at least one file item.");
        _readFileBytes = readFileBytes;
    }

    public void GetData(ref System.Runtime.InteropServices.ComTypes.FORMATETC format, out System.Runtime.InteropServices.ComTypes.STGMEDIUM medium)
    {
        var query = QueryGetData(ref format);
        if (query != SOk)
        {
            Marshal.ThrowExceptionForHR(query);
        }

        if (format.cfFormat == FileGroupDescriptorFormat)
        {
            medium = CreateHGlobalMedium(CreateFileGroupDescriptor());
            return;
        }

        if (format.cfFormat == FileContentsFormat)
        {
            medium = CreateHGlobalMedium(_readFileBytes(format.lindex < 0 ? 0 : format.lindex));
            return;
        }

        if (format.cfFormat == PreferredDropEffectFormat)
        {
            medium = CreateHGlobalMedium(CreatePreferredDropEffect());
            return;
        }

        Marshal.ThrowExceptionForHR(DV_E_FORMATETC);
        throw new InvalidOperationException("Unsupported clipboard format.");
    }

    public void GetDataHere(ref System.Runtime.InteropServices.ComTypes.FORMATETC format, ref System.Runtime.InteropServices.ComTypes.STGMEDIUM medium) =>
        Marshal.ThrowExceptionForHR(ENotImpl);

    public int QueryGetData(ref System.Runtime.InteropServices.ComTypes.FORMATETC format)
    {
        if (format.dwAspect != System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT)
        {
            return DV_E_FORMATETC;
        }

        if ((format.tymed & System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL) == 0)
        {
            return DV_E_TYMED;
        }

        if (format.cfFormat == FileGroupDescriptorFormat)
        {
            return SOk;
        }

        if (format.cfFormat == PreferredDropEffectFormat)
        {
            return SOk;
        }

        if (format.cfFormat == FileContentsFormat && (format.lindex == -1 || (format.lindex >= 0 && format.lindex < _items.Count)))
        {
            return SOk;
        }

        return DV_E_FORMATETC;
    }

    public int GetCanonicalFormatEtc(ref System.Runtime.InteropServices.ComTypes.FORMATETC formatIn, out System.Runtime.InteropServices.ComTypes.FORMATETC formatOut)
    {
        formatOut = formatIn;
        formatOut.ptd = IntPtr.Zero;
        return DataSSameFormatEtc;
    }

    public void SetData(ref System.Runtime.InteropServices.ComTypes.FORMATETC formatIn, ref System.Runtime.InteropServices.ComTypes.STGMEDIUM medium, bool release) =>
        Marshal.ThrowExceptionForHR(ENotImpl);

    public System.Runtime.InteropServices.ComTypes.IEnumFORMATETC EnumFormatEtc(System.Runtime.InteropServices.ComTypes.DATADIR direction)
    {
        if (direction != System.Runtime.InteropServices.ComTypes.DATADIR.DATADIR_GET)
        {
            Marshal.ThrowExceptionForHR(ENotImpl);
        }

        var formats = new List<System.Runtime.InteropServices.ComTypes.FORMATETC>
        {
            CreateFormatEtc(FileGroupDescriptorFormat, -1),
            CreateFormatEtc(PreferredDropEffectFormat, -1)
        };
        formats.AddRange(Enumerable.Range(0, _items.Count).Select(index => CreateFormatEtc(FileContentsFormat, index)));
        return new FormatEtcEnumerator(formats.ToArray());
    }

    public int DAdvise(ref System.Runtime.InteropServices.ComTypes.FORMATETC pFormatetc, System.Runtime.InteropServices.ComTypes.ADVF advf, System.Runtime.InteropServices.ComTypes.IAdviseSink adviseSink, out int connection)
    {
        connection = 0;
        return ENotImpl;
    }

    public void DUnadvise(int connection) => Marshal.ThrowExceptionForHR(ENotImpl);

    public int EnumDAdvise(out System.Runtime.InteropServices.ComTypes.IEnumSTATDATA enumAdvise)
    {
        enumAdvise = null!;
        return ENotImpl;
    }

    private static System.Runtime.InteropServices.ComTypes.FORMATETC CreateFormatEtc(short format, int index) =>
        new()
        {
            cfFormat = format,
            ptd = IntPtr.Zero,
            dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,
            lindex = index,
            tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL
        };

    private static System.Runtime.InteropServices.ComTypes.STGMEDIUM CreateHGlobalMedium(byte[] bytes)
    {
        var handle = GlobalAlloc(GmemMoveable, (UIntPtr)bytes.Length);
        if (handle == IntPtr.Zero)
        {
            throw new OutOfMemoryException("GlobalAlloc failed.");
        }

        var locked = GlobalLock(handle);
        if (locked == IntPtr.Zero)
        {
            GlobalFree(handle);
            throw new OutOfMemoryException("GlobalLock failed.");
        }

        try
        {
            Marshal.Copy(bytes, 0, locked, bytes.Length);
        }
        finally
        {
            GlobalUnlock(handle);
        }

        return new System.Runtime.InteropServices.ComTypes.STGMEDIUM
        {
            tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL,
            unionmember = handle,
            pUnkForRelease = null
        };
    }

    private byte[] CreateFileGroupDescriptor()
    {
        var bytes = new byte[sizeof(uint) + (FileDescriptorSize * _items.Count)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, sizeof(uint)), (uint)_items.Count);

        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var descriptor = bytes.AsSpan(sizeof(uint) + (FileDescriptorSize * i), FileDescriptorSize);
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor[0..4], FdAttributes | FdFileSize);
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor[36..40], (uint)item.Attributes);

            BinaryPrimitives.WriteUInt32LittleEndian(descriptor[64..68], (uint)(item.Length >> 32));
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor[68..72], (uint)(item.Length & 0xFFFFFFFF));

            var encodedName = Encoding.Unicode.GetBytes(SanitizeClipboardFileName(item.RelativePath));
            encodedName.AsSpan(0, Math.Min(encodedName.Length, (MaxPath - 1) * 2)).CopyTo(descriptor[72..]);
        }

        return bytes;
    }

    private static byte[] CreatePreferredDropEffect()
    {
        var bytes = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, DropEffectCopy);
        return bytes;
    }

    private static string SanitizeClipboardFileName(string fileName)
    {
        var safeName = fileName
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            return "klip-clipboard-file";
        }

        return safeName.Length >= MaxPath ? safeName[..(MaxPath - 1)] : safeName;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterClipboardFormat(string lpszFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(int uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);
}

sealed class FormatEtcEnumerator : System.Runtime.InteropServices.ComTypes.IEnumFORMATETC
{
    private readonly System.Runtime.InteropServices.ComTypes.FORMATETC[] _formats;
    private int _index;

    public FormatEtcEnumerator(System.Runtime.InteropServices.ComTypes.FORMATETC[] formats)
    {
        _formats = formats;
    }

    public int Next(int celt, System.Runtime.InteropServices.ComTypes.FORMATETC[] rgelt, int[]? pceltFetched)
    {
        var fetched = 0;
        while (fetched < celt && _index < _formats.Length && fetched < rgelt.Length)
        {
            rgelt[fetched++] = _formats[_index++];
        }

        if (pceltFetched is { Length: > 0 })
        {
            pceltFetched[0] = fetched;
        }

        return fetched == celt ? 0 : 1;
    }

    public int Skip(int celt)
    {
        _index = Math.Min(_index + celt, _formats.Length);
        return _index < _formats.Length ? 0 : 1;
    }

    public int Reset()
    {
        _index = 0;
        return 0;
    }

    public void Clone(out System.Runtime.InteropServices.ComTypes.IEnumFORMATETC newEnum)
    {
        newEnum = new FormatEtcEnumerator(_formats) { _index = _index };
    }
}

sealed record SendOptions(string? FilePath, bool UseClipboard, string Host, int Port, int ChunkSize)
{
    public static SendOptions Parse(string[] args)
    {
        string? filePath = null;
        string? host = null;
        var port = Defaults.Port;
        var chunkSize = Defaults.ChunkSize;
        var useClipboard = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--host" when i + 1 < args.Length:
                    host = args[++i];
                    break;
                case "--port" when i + 1 < args.Length:
                    port = ParsePort(args[++i]);
                    break;
                case "--chunk-size" when i + 1 < args.Length:
                    chunkSize = ParsePositiveInt(args[++i], "chunk size");
                    break;
                case "--clipboard":
                    useClipboard = true;
                    break;
                case "-h" or "--help":
                    throw new KlipException("Usage: Klip send <file> --host <address> [--port 45245] or Klip send --clipboard --host <address>");
                default:
                    if (args[i].StartsWith('-'))
                    {
                        throw new KlipException($"Unknown send option: {args[i]}");
                    }

                    filePath ??= args[i];
                    break;
            }
        }

        if (filePath is null)
        {
            useClipboard = true;
        }

        if (filePath is not null && useClipboard)
        {
            throw new KlipException("Provide either a file path or --clipboard, not both.");
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            throw new KlipException("Missing --host <address>.");
        }

        return new SendOptions(filePath, useClipboard, host, port, chunkSize);
    }

    private static int ParsePort(string value)
    {
        var port = ParsePositiveInt(value, "port");
        if (port > 65535)
        {
            throw new KlipException("Port must be between 1 and 65535.");
        }

        return port;
    }

    private static int ParsePositiveInt(string value, string name)
    {
        if (!int.TryParse(value, out var result) || result <= 0)
        {
            throw new KlipException($"Invalid {name}: {value}");
        }

        return result;
    }
}

sealed record ReceiveOptions(int Port, string OutputFolder, bool Overwrite, bool CopyToClipboard)
{
    public static ReceiveOptions Parse(string[] args)
    {
        var port = Defaults.Port;
        var outputFolder = "received";
        var overwrite = false;
        var copyToClipboard = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port" when i + 1 < args.Length:
                    port = ParsePort(args[++i]);
                    break;
                case "--out" when i + 1 < args.Length:
                    outputFolder = args[++i];
                    break;
                case "--overwrite":
                    overwrite = true;
                    break;
                case "--clipboard":
                    copyToClipboard = true;
                    break;
                case "-h" or "--help":
                    throw new KlipException("Usage: Klip receive [--port 45245] [--out <folder>] [--overwrite] [--clipboard]");
                default:
                    throw new KlipException($"Unknown receive option: {args[i]}");
            }
        }

        return new ReceiveOptions(port, outputFolder, overwrite, copyToClipboard);
    }

    private static int ParsePort(string value)
    {
        if (!int.TryParse(value, out var port) || port is < 1 or > 65535)
        {
            throw new KlipException("Port must be between 1 and 65535.");
        }

        return port;
    }
}

sealed record SyncServerOptions(int Port)
{
    public static SyncServerOptions Parse(string[] args)
    {
        var port = Defaults.Port;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port" when i + 1 < args.Length:
                    port = ParsePort(args[++i]);
                    break;
                case "-h" or "--help":
                    throw new KlipException("Usage: Klip server [--port 45245]");
                default:
                    throw new KlipException($"Unknown server option: {args[i]}");
            }
        }

        return new SyncServerOptions(port);
    }

    private static int ParsePort(string value)
    {
        if (!int.TryParse(value, out var port) || port is < 1 or > 65535)
        {
            throw new KlipException("Port must be between 1 and 65535.");
        }

        return port;
    }
}

sealed record SyncClientOptions(string Host, int Port, int ContentPort)
{
    public static SyncClientOptions Parse(string[] args)
    {
        string? host = null;
        var port = Defaults.Port;
        var contentPort = Defaults.Port + 1;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--host" when i + 1 < args.Length:
                    host = args[++i];
                    break;
                case "--port" when i + 1 < args.Length:
                    port = ParsePort(args[++i], "port");
                    break;
                case "--content-port" when i + 1 < args.Length:
                    contentPort = ParsePort(args[++i], "content port");
                    break;
                case "-h" or "--help":
                    throw new KlipException("Usage: Klip client --host <address> [--port 45245] [--content-port 45246]");
                default:
                    throw new KlipException($"Unknown client option: {args[i]}");
            }
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            throw new KlipException("Missing --host <address>.");
        }

        return new SyncClientOptions(host, port, contentPort);
    }

    private static int ParsePort(string value, string name)
    {
        if (!int.TryParse(value, out var port) || port is < 1 or > 65535)
        {
            throw new KlipException($"{name} must be between 1 and 65535.");
        }

        return port;
    }
}

sealed record TransferMetadata(string FileName, long Length, string Sha256)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(FileName))
        {
            throw new KlipException("File name is required.");
        }

        if (Length < 0)
        {
            throw new KlipException("File length cannot be negative.");
        }

        if (Sha256.Length != 64 || Sha256.Any(c => !Uri.IsHexDigit(c)))
        {
            throw new KlipException("Metadata contains an invalid SHA-256 hash.");
        }
    }
}

sealed record KlipFrame(FrameType Type, ArraySegment<byte> Payload)
{
    public KlipFrame(FrameType type, byte[] payload)
        : this(type, new ArraySegment<byte>(payload))
    {
    }
}

enum FrameType : byte
{
    Metadata = 1,
    Data = 2,
    End = 3,
    Ack = 4,
    Error = 5,
    Control = 6
}

static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

sealed class KlipException(string message) : Exception(message);
