sealed record TransferSource(string FileName, long Length, string Description, Func<Stream> OpenRead)
{
    public static TransferSource FromFile(string path)
    {
        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists)
        {
            throw new KlipException($"File not found: {path}");
        }

        return new TransferSource(
            Path.GetFileName(fileInfo.FullName),
            fileInfo.Length,
            fileInfo.FullName,
            () => File.OpenRead(fileInfo.FullName));
    }

    public static TransferSource FromText(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var fileName = $"clipboard-{DateTime.Now:yyyyMMdd-HHmmss}.txt";

        return new TransferSource(
            fileName,
            bytes.LongLength,
            "clipboard text",
            () => new MemoryStream(bytes, writable: false));
    }
}
