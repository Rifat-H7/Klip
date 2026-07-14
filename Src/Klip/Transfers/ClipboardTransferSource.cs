static class ClipboardTransferSource
{
    public static TransferSource Detect()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new KlipException("Clipboard detection is currently supported on Windows only.");
        }

        return WindowsClipboard.ReadFileDrop()
            ?? WindowsClipboard.ReadUnicodeText()
            ?? throw new KlipException("Clipboard does not contain a readable file path or text.");
    }
}
