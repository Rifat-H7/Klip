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
