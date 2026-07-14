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
