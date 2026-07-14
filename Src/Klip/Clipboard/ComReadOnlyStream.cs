sealed class ComReadOnlyStream : System.Runtime.InteropServices.ComTypes.IStream, IDisposable
{
    private readonly Stream _stream;
    private readonly long _length;

    public ComReadOnlyStream(Stream stream, long length)
    {
        _stream = stream;
        _length = length;
    }

    ~ComReadOnlyStream()
    {
        Dispose();
    }

    public void Read(byte[] pv, int cb, IntPtr pcbRead)
    {
        var read = _stream.Read(pv, 0, cb);
        if (pcbRead != IntPtr.Zero)
        {
            Marshal.WriteInt32(pcbRead, read);
        }
    }

    public void Write(byte[] pv, int cb, IntPtr pcbWritten) =>
        Marshal.ThrowExceptionForHR(unchecked((int)0x8003001D));

    public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
    {
        if (!_stream.CanSeek)
        {
            Marshal.ThrowExceptionForHR(unchecked((int)0x8003001D));
        }

        var origin = dwOrigin switch
        {
            0 => SeekOrigin.Begin,
            1 => SeekOrigin.Current,
            2 => SeekOrigin.End,
            _ => throw new ArgumentOutOfRangeException(nameof(dwOrigin))
        };
        var position = _stream.Seek(dlibMove, origin);
        if (plibNewPosition != IntPtr.Zero)
        {
            Marshal.WriteInt64(plibNewPosition, position);
        }
    }

    public void SetSize(long libNewSize) =>
        Marshal.ThrowExceptionForHR(unchecked((int)0x8003001D));

    public void CopyTo(System.Runtime.InteropServices.ComTypes.IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten)
    {
        var buffer = new byte[Defaults.ChunkSize];
        long totalRead = 0;
        long totalWritten = 0;

        while (totalRead < cb)
        {
            var toRead = (int)Math.Min(buffer.Length, cb - totalRead);
            var read = _stream.Read(buffer, 0, toRead);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
            pstm.Write(buffer, read, IntPtr.Zero);
            totalWritten += read;
        }

        if (pcbRead != IntPtr.Zero)
        {
            Marshal.WriteInt64(pcbRead, totalRead);
        }

        if (pcbWritten != IntPtr.Zero)
        {
            Marshal.WriteInt64(pcbWritten, totalWritten);
        }
    }

    public void Commit(int grfCommitFlags)
    {
    }

    public void Revert() =>
        Marshal.ThrowExceptionForHR(unchecked((int)0x80030001));

    public void LockRegion(long libOffset, long cb, int dwLockType) =>
        Marshal.ThrowExceptionForHR(unchecked((int)0x80030001));

    public void UnlockRegion(long libOffset, long cb, int dwLockType) =>
        Marshal.ThrowExceptionForHR(unchecked((int)0x80030001));

    public void Stat(out System.Runtime.InteropServices.ComTypes.STATSTG pstatstg, int grfStatFlag)
    {
        pstatstg = new System.Runtime.InteropServices.ComTypes.STATSTG
        {
            type = 2,
            cbSize = _length
        };
    }

    public void Clone(out System.Runtime.InteropServices.ComTypes.IStream ppstm)
    {
        ppstm = null!;
        Marshal.ThrowExceptionForHR(unchecked((int)0x80004001));
    }

    public void Dispose()
    {
        _stream.Dispose();
        GC.SuppressFinalize(this);
    }
}
