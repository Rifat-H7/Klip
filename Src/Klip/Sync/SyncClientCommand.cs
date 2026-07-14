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
