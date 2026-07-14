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

