static class VirtualClipboard
{
#pragma warning disable CA1416
    private static readonly Lazy<StaClipboardWorker> Worker = new(() => new StaClipboardWorker());
#pragma warning restore CA1416
    private static System.Runtime.InteropServices.ComTypes.IDataObject? s_currentDataObject;

    public static void SetFile(string path, string displayName)
    {
        var info = new FileInfo(path);
        SetFiles(
        [
            new VirtualFileItem(displayName, info.Length, (int)info.Attributes)
        ], index =>
        {
            if (index != 0)
            {
                throw new KlipException("Invalid clipboard file index.");
            }

            return File.OpenRead(path);
        });
    }

    public static void SetFiles(IReadOnlyList<VirtualFileItem> items, Func<int, Stream> openRead)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new KlipException("Receiver clipboard mode is currently supported on Windows only.");
        }

        Worker.Value.Invoke(() =>
        {
            var dataObject = new VirtualFileDataObject(items, openRead);
            var setResult = OleSetClipboard(dataObject);
            if (setResult < 0)
            {
                throw new KlipException($"OleSetClipboard failed with HRESULT 0x{setResult:X8}.");
            }

            s_currentDataObject = dataObject;
        });
    }

    [DllImport("ole32.dll")]
    private static extern int OleSetClipboard(System.Runtime.InteropServices.ComTypes.IDataObject pDataObj);
}
