sealed record KlipFrame(FrameType Type, ArraySegment<byte> Payload)
{
    public KlipFrame(FrameType type, byte[] payload)
        : this(type, new ArraySegment<byte>(payload))
    {
    }
}

enum FrameType : byte
{
    Metadata = 1,
    Data = 2,
    End = 3,
    Ack = 4,
    Error = 5,
    Control = 6
}
