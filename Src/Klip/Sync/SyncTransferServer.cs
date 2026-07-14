static class SyncTransferServer
{
    public static async Task RunAsync(int port, TransferStore transferStore, CancellationToken cancellationToken)
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
