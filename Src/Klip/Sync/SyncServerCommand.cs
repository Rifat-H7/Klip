static class SyncServerCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var options = SyncServerOptions.Parse(args);
        var transferStore = new TransferStore();
        var transferServer = SyncTransferServer.RunAsync(options.ContentPort, transferStore, cancellationToken);
        var listener = new TcpListener(IPAddress.Any, options.Port);
        listener.Start();

        try
        {
            Console.WriteLine($"Klip server listening on 0.0.0.0:{options.Port}");
            Console.WriteLine($"Local content listener is on port {options.ContentPort}.");
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
                    await using var stream = client.GetStream();
                    await BidirectionalClipboardSync.RunAsync(
                        stream,
                        remoteAddress,
                        options.ContentPort,
                        transferStore,
                        cancellationToken);
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

        await transferServer;
        return 0;
    }
}
