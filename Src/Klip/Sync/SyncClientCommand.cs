static class SyncClientCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var options = SyncClientOptions.Parse(args);
        var transferStore = new TransferStore();
        var transferServer = SyncTransferServer.RunAsync(options.ContentPort, transferStore, cancellationToken);

        using var client = new TcpClient();
        Console.WriteLine($"Connecting to Klip server {options.Host}:{options.Port}...");
        await client.ConnectAsync(options.Host, options.Port, cancellationToken);
        client.NoDelay = true;

        await using var stream = client.GetStream();
        var remoteEndpoint = (IPEndPoint?)client.Client.RemoteEndPoint;
        var remoteAddress = remoteEndpoint?.Address.ToString() ?? options.Host;
        Console.WriteLine($"Bidirectional clipboard sync started. Local content listener is on port {options.ContentPort}.");

        await BidirectionalClipboardSync.RunAsync(
            stream,
            remoteAddress,
            options.ContentPort,
            transferStore,
            cancellationToken);

        await transferServer;
        return 0;
    }
}
