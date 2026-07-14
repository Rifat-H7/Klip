sealed class RemoteVirtualFileProvider
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _transferId;

    public RemoteVirtualFileProvider(string host, int port, string transferId)
    {
        _host = host;
        _port = port;
        _transferId = transferId;
    }

    public Stream OpenRead(int index)
    {
        return new RemoteFileContentStream(_host, _port, _transferId, index);
    }
}

sealed class RemoteFileContentStream : Stream
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _transferId;
    private readonly int _index;
    private TcpClient? _client;
    private NetworkStream? _network;
    private ArraySegment<byte> _currentChunk = ArraySegment<byte>.Empty;
    private bool _completed;

    public RemoteFileContentStream(string host, int port, string transferId, int index)
    {
        _host = host;
        _port = port;
        _transferId = transferId;
        _index = index;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || offset + count > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (count == 0 || _completed)
        {
            return 0;
        }

        EnsureConnected();

        while (_currentChunk.Count == 0)
        {
            var frame = KlipProtocol.ReadFrameAsync(_network!, CancellationToken.None).GetAwaiter().GetResult();
            if (frame.Type == FrameType.End)
            {
                _completed = true;
                return 0;
            }

            if (frame.Type != FrameType.Data)
            {
                throw new KlipException($"Unexpected transfer frame: {frame.Type}");
            }

            _currentChunk = frame.Payload;
        }

        var toCopy = Math.Min(count, _currentChunk.Count);
        Buffer.BlockCopy(_currentChunk.Array!, _currentChunk.Offset, buffer, offset, toCopy);
        _currentChunk = new ArraySegment<byte>(
            _currentChunk.Array!,
            _currentChunk.Offset + toCopy,
            _currentChunk.Count - toCopy);
        return toCopy;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _network?.Dispose();
            _client?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void EnsureConnected()
    {
        if (_client is not null)
        {
            return;
        }

        _client = new TcpClient();
        _client.Connect(_host, _port);
        _client.NoDelay = true;
        _network = _client.GetStream();
        SyncProtocol.WriteTransferRequestAsync(_network, new TransferRequest(_transferId, _index), CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }
}
