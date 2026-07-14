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
