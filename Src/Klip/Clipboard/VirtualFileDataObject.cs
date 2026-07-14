[ComVisible(true)]
sealed class VirtualFileDataObject : System.Runtime.InteropServices.ComTypes.IDataObject, IShellAsyncOperation
{
    private const int SOk = 0;
    private const int ENotImpl = unchecked((int)0x80004001);
    private const int DV_E_FORMATETC = unchecked((int)0x80040064);
    private const int DV_E_TYMED = unchecked((int)0x80040069);
    private const int DataSSameFormatEtc = 0x00040130;
    private const int GmemMoveable = 0x0002;
    private const int FileDescriptorSize = 592;
    private const int MaxPath = 260;
    private const int FdAttributes = 0x00000004;
    private const int FdFileSize = 0x00000040;
    private const int FdProgressUi = 0x00004000;
    private const int DropEffectCopy = 1;

    private static readonly short FileGroupDescriptorFormat = unchecked((short)RegisterClipboardFormat("FileGroupDescriptorW"));
    private static readonly short FileContentsFormat = unchecked((short)RegisterClipboardFormat("FileContents"));
    private static readonly short PreferredDropEffectFormat = unchecked((short)RegisterClipboardFormat("Preferred DropEffect"));
    private readonly IReadOnlyList<VirtualFileItem> _items;
    private readonly Func<int, Stream> _openRead;
    private bool _asyncMode = true;
    private bool _inOperation;

    public VirtualFileDataObject(IReadOnlyList<VirtualFileItem> items, Func<int, Stream> openRead)
    {
        _items = items.Count > 0 ? items : throw new KlipException("Virtual clipboard needs at least one file item.");
        _openRead = openRead;
    }

    public void GetData(ref System.Runtime.InteropServices.ComTypes.FORMATETC format, out System.Runtime.InteropServices.ComTypes.STGMEDIUM medium)
    {
        var query = QueryGetData(ref format);
        if (query != SOk)
        {
            Marshal.ThrowExceptionForHR(query);
        }

        if (format.cfFormat == FileGroupDescriptorFormat)
        {
            medium = CreateHGlobalMedium(CreateFileGroupDescriptor());
            return;
        }

        if (format.cfFormat == FileContentsFormat)
        {
            medium = CreateStreamMedium(format.lindex < 0 ? 0 : format.lindex);
            return;
        }

        if (format.cfFormat == PreferredDropEffectFormat)
        {
            medium = CreateHGlobalMedium(CreatePreferredDropEffect());
            return;
        }

        Marshal.ThrowExceptionForHR(DV_E_FORMATETC);
        throw new InvalidOperationException("Unsupported clipboard format.");
    }

    public void GetDataHere(ref System.Runtime.InteropServices.ComTypes.FORMATETC format, ref System.Runtime.InteropServices.ComTypes.STGMEDIUM medium) =>
        Marshal.ThrowExceptionForHR(ENotImpl);

    public int QueryGetData(ref System.Runtime.InteropServices.ComTypes.FORMATETC format)
    {
        if (format.dwAspect != System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT)
        {
            return DV_E_FORMATETC;
        }

        if (format.cfFormat == FileGroupDescriptorFormat)
        {
            return HasTymed(format, System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL) ? SOk : DV_E_TYMED;
        }

        if (format.cfFormat == PreferredDropEffectFormat)
        {
            return HasTymed(format, System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL) ? SOk : DV_E_TYMED;
        }

        if (format.cfFormat == FileContentsFormat && (format.lindex == -1 || (format.lindex >= 0 && format.lindex < _items.Count)))
        {
            return HasTymed(format, System.Runtime.InteropServices.ComTypes.TYMED.TYMED_ISTREAM) ? SOk : DV_E_TYMED;
        }

        return DV_E_FORMATETC;
    }

    public int GetCanonicalFormatEtc(ref System.Runtime.InteropServices.ComTypes.FORMATETC formatIn, out System.Runtime.InteropServices.ComTypes.FORMATETC formatOut)
    {
        formatOut = formatIn;
        formatOut.ptd = IntPtr.Zero;
        return DataSSameFormatEtc;
    }

    public void SetData(ref System.Runtime.InteropServices.ComTypes.FORMATETC formatIn, ref System.Runtime.InteropServices.ComTypes.STGMEDIUM medium, bool release) =>
        Marshal.ThrowExceptionForHR(ENotImpl);

    public System.Runtime.InteropServices.ComTypes.IEnumFORMATETC EnumFormatEtc(System.Runtime.InteropServices.ComTypes.DATADIR direction)
    {
        if (direction != System.Runtime.InteropServices.ComTypes.DATADIR.DATADIR_GET)
        {
            Marshal.ThrowExceptionForHR(ENotImpl);
        }

        var formats = new List<System.Runtime.InteropServices.ComTypes.FORMATETC>
        {
            CreateFormatEtc(FileGroupDescriptorFormat, -1),
            CreateFormatEtc(PreferredDropEffectFormat, -1)
        };
        formats.AddRange(Enumerable.Range(0, _items.Count).Select(index => CreateStreamFormatEtc(FileContentsFormat, index)));
        return new FormatEtcEnumerator(formats.ToArray());
    }

    public int DAdvise(ref System.Runtime.InteropServices.ComTypes.FORMATETC pFormatetc, System.Runtime.InteropServices.ComTypes.ADVF advf, System.Runtime.InteropServices.ComTypes.IAdviseSink adviseSink, out int connection)
    {
        connection = 0;
        return ENotImpl;
    }

    public void DUnadvise(int connection) => Marshal.ThrowExceptionForHR(ENotImpl);

    public int EnumDAdvise(out System.Runtime.InteropServices.ComTypes.IEnumSTATDATA enumAdvise)
    {
        enumAdvise = null!;
        return ENotImpl;
    }

    public void SetAsyncMode(bool doOperationAsync)
    {
        _asyncMode = doOperationAsync;
    }

    public void GetAsyncMode(out bool isOperationAsync)
    {
        isOperationAsync = _asyncMode;
    }

    public void StartOperation(IntPtr bindContext)
    {
        _inOperation = true;
    }

    public void InOperation(out bool inAsyncOperation)
    {
        inAsyncOperation = _inOperation;
    }

    public void EndOperation(int result, IntPtr bindContext, uint effects)
    {
        _inOperation = false;
    }

    private static System.Runtime.InteropServices.ComTypes.FORMATETC CreateFormatEtc(short format, int index) =>
        new()
        {
            cfFormat = format,
            ptd = IntPtr.Zero,
            dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,
            lindex = index,
            tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL
        };

    private static System.Runtime.InteropServices.ComTypes.FORMATETC CreateStreamFormatEtc(short format, int index) =>
        new()
        {
            cfFormat = format,
            ptd = IntPtr.Zero,
            dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,
            lindex = index,
            tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_ISTREAM
        };

    private static bool HasTymed(System.Runtime.InteropServices.ComTypes.FORMATETC format, System.Runtime.InteropServices.ComTypes.TYMED tymed) =>
        (format.tymed & tymed) != 0;

    private static System.Runtime.InteropServices.ComTypes.STGMEDIUM CreateHGlobalMedium(byte[] bytes)
    {
        var handle = GlobalAlloc(GmemMoveable, (UIntPtr)bytes.Length);
        if (handle == IntPtr.Zero)
        {
            throw new OutOfMemoryException("GlobalAlloc failed.");
        }

        var locked = GlobalLock(handle);
        if (locked == IntPtr.Zero)
        {
            GlobalFree(handle);
            throw new OutOfMemoryException("GlobalLock failed.");
        }

        try
        {
            Marshal.Copy(bytes, 0, locked, bytes.Length);
        }
        finally
        {
            GlobalUnlock(handle);
        }

        return new System.Runtime.InteropServices.ComTypes.STGMEDIUM
        {
            tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL,
            unionmember = handle,
            pUnkForRelease = null
        };
    }

    private System.Runtime.InteropServices.ComTypes.STGMEDIUM CreateStreamMedium(int index)
    {
        var stream = new ComReadOnlyStream(_openRead(index), _items[index].Length);
#pragma warning disable CA1416
        var streamPointer = Marshal.GetComInterfaceForObject(stream, typeof(System.Runtime.InteropServices.ComTypes.IStream));
#pragma warning restore CA1416
        return new System.Runtime.InteropServices.ComTypes.STGMEDIUM
        {
            tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_ISTREAM,
            unionmember = streamPointer,
            pUnkForRelease = null
        };
    }

    private byte[] CreateFileGroupDescriptor()
    {
        var bytes = new byte[sizeof(uint) + (FileDescriptorSize * _items.Count)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, sizeof(uint)), (uint)_items.Count);

        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var descriptor = bytes.AsSpan(sizeof(uint) + (FileDescriptorSize * i), FileDescriptorSize);
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor[0..4], FdAttributes | FdFileSize | FdProgressUi);
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor[36..40], (uint)item.Attributes);

            BinaryPrimitives.WriteUInt32LittleEndian(descriptor[64..68], (uint)(item.Length >> 32));
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor[68..72], (uint)(item.Length & 0xFFFFFFFF));

            var encodedName = Encoding.Unicode.GetBytes(SanitizeClipboardFileName(item.RelativePath));
            encodedName.AsSpan(0, Math.Min(encodedName.Length, (MaxPath - 1) * 2)).CopyTo(descriptor[72..]);
        }

        return bytes;
    }

    private static byte[] CreatePreferredDropEffect()
    {
        var bytes = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, DropEffectCopy);
        return bytes;
    }

    private static string SanitizeClipboardFileName(string fileName)
    {
        var safeName = fileName
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            return "klip-clipboard-file";
        }

        return safeName.Length >= MaxPath ? safeName[..(MaxPath - 1)] : safeName;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterClipboardFormat(string lpszFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(int uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);
}
