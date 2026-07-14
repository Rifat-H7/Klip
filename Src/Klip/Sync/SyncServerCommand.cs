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
                    VirtualClipboard.SetFiles(message.Items, provider.OpenRead);
                        Console.WriteLine($"Clipboard virtual files updated ({message.Items.Count} item(s), live IDataObject, IStream contents). Bytes will transfer on paste.");
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
