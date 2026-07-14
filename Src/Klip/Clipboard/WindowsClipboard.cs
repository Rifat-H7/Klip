static class WindowsClipboard
{
    private const uint CfUnicodeText = 13;
    private const uint CfHDrop = 15;

    public static TransferSource? ReadFileDrop()
    {
        var paths = ReadFileDropPaths();
        return paths.Select(path => File.Exists(path) ? TransferSource.FromFile(path) : null).FirstOrDefault(source => source is not null);
    }

    public static List<string> ReadFileDropPaths()
    {
        using var clipboard = Open();
        var paths = new List<string>();

        if (!IsClipboardFormatAvailable(CfHDrop))
        {
            return paths;
        }

        var handle = GetClipboardData(CfHDrop);
        if (handle == IntPtr.Zero)
        {
            return paths;
        }

        var count = DragQueryFile(handle, 0xFFFFFFFF, null, 0);
        for (uint i = 0; i < count; i++)
        {
            var length = DragQueryFile(handle, i, null, 0);
            if (length == 0)
            {
                continue;
            }

            var builder = new StringBuilder((int)length + 1);
            DragQueryFile(handle, i, builder, (uint)builder.Capacity);
            var path = builder.ToString();
            if (File.Exists(path) || Directory.Exists(path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    public static TransferSource? ReadUnicodeText()
    {
        var text = ReadUnicodeTextValue();
        return string.IsNullOrEmpty(text) ? null : TransferSource.FromText(text);
    }

    public static string? ReadUnicodeTextValue()
    {
        using var clipboard = Open();
        if (!IsClipboardFormatAvailable(CfUnicodeText))
        {
            return null;
        }

        var handle = GetClipboardData(CfUnicodeText);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var locked = GlobalLock(handle);
        if (locked == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(locked);
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    public static void SetUnicodeText(string text)
    {
        using var clipboard = Open();
        if (!EmptyClipboard())
        {
            throw new KlipException("Could not empty the clipboard.");
        }

        var bytes = Encoding.Unicode.GetBytes(text + '\0');
        var handle = GlobalAlloc(0x0002, (UIntPtr)bytes.Length);
        if (handle == IntPtr.Zero)
        {
            throw new KlipException("Could not allocate clipboard memory.");
        }

        var locked = GlobalLock(handle);
        if (locked == IntPtr.Zero)
        {
            GlobalFree(handle);
            throw new KlipException("Could not lock clipboard memory.");
        }

        try
        {
            Marshal.Copy(bytes, 0, locked, bytes.Length);
        }
        finally
        {
            GlobalUnlock(handle);
        }

        if (SetClipboardData(CfUnicodeText, handle) == IntPtr.Zero)
        {
            GlobalFree(handle);
            throw new KlipException("Could not set clipboard text.");
        }
    }

    private static ClipboardHandle Open()
    {
        if (!OpenClipboard(IntPtr.Zero))
        {
            throw new KlipException("Could not open the clipboard. Try again after the current clipboard owner releases it.");
        }

        return new ClipboardHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(int uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder? lpszFile, uint cch);

    private sealed class ClipboardHandle : IDisposable
    {
        public void Dispose() => CloseClipboard();
    }
}
