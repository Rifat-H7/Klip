static class BidirectionalClipboardSync
{
    public static async Task RunAsync(
        Stream stream,
        string remoteAddress,
        int localContentPort,
        TransferStore transferStore,
        CancellationToken cancellationToken)
    {
        var suppressedSnapshotKeys = new HashSet<string>();
        using var sessionShutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var receiveTask = ReceiveRemoteChangesAsync(stream, remoteAddress, suppressedSnapshotKeys, sessionShutdown.Token);
        var publishTask = PublishLocalChangesAsync(stream, localContentPort, transferStore, suppressedSnapshotKeys, sessionShutdown.Token);

        var completed = await Task.WhenAny(receiveTask, publishTask);
        sessionShutdown.Cancel();

        await completed;
        await Task.WhenAll(receiveTask, publishTask);
    }

    private static async Task ReceiveRemoteChangesAsync(
        Stream stream,
        string remoteAddress,
        HashSet<string> suppressedSnapshotKeys,
        CancellationToken cancellationToken)
    {
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
                        SuppressNextLocalPublish(suppressedSnapshotKeys, ClipboardSnapshot.CreateTextKey(message.Text));
                        Console.WriteLine($"Remote clipboard text received ({message.Text.Length} chars).");
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
                        Console.WriteLine($"Remote virtual files received ({message.Items.Count} item(s)). Bytes will transfer on paste.");
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

    private static async Task PublishLocalChangesAsync(
        Stream stream,
        int localContentPort,
        TransferStore transferStore,
        HashSet<string> suppressedSnapshotKeys,
        CancellationToken cancellationToken)
    {
        string? lastSnapshotKey = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var snapshot = ClipboardSnapshot.Read();
            if (snapshot is not null && snapshot.Key != lastSnapshotKey)
            {
                lastSnapshotKey = snapshot.Key;
                if (ShouldSuppressLocalPublish(suppressedSnapshotKeys, snapshot.Key))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
                    continue;
                }

                var message = snapshot.ToSyncMessage(transferStore, localContentPort);
                if (message is not null)
                {
                    await SyncProtocol.WriteMessageAsync(stream, message, cancellationToken);
                    Console.WriteLine(snapshot.Kind == ClipboardSnapshotKind.Text
                        ? "Sent local clipboard text update."
                        : "Sent local virtual file metadata. File bytes will wait until paste.");
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
        }
    }

    private static void SuppressNextLocalPublish(HashSet<string> suppressedSnapshotKeys, string snapshotKey)
    {
        lock (suppressedSnapshotKeys)
        {
            suppressedSnapshotKeys.Add(snapshotKey);
        }
    }

    private static bool ShouldSuppressLocalPublish(HashSet<string> suppressedSnapshotKeys, string snapshotKey)
    {
        lock (suppressedSnapshotKeys)
        {
            return suppressedSnapshotKeys.Remove(snapshotKey);
        }
    }
}
