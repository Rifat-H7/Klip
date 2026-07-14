sealed record ClipboardSnapshot(ClipboardSnapshotKind Kind, string Fingerprint, string? Text, List<string> Paths)
{
    public string Key => $"{Kind}:{Fingerprint}";

    public static ClipboardSnapshot? Read()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new KlipException("Clipboard sync is currently supported on Windows only.");
        }

        var paths = WindowsClipboard.ReadFileDropPaths();
        if (paths.Count > 0)
        {
            var fingerprint = string.Join('\n', paths.Select(path =>
            {
                if (File.Exists(path))
                {
                    var info = new FileInfo(path);
                    return $"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
                }

                if (Directory.Exists(path))
                {
                    var info = new DirectoryInfo(path);
                    return $"{path}|dir|{info.LastWriteTimeUtc.Ticks}";
                }

                return path;
            }));

            return new ClipboardSnapshot(ClipboardSnapshotKind.Files, fingerprint, null, paths);
        }

        var text = WindowsClipboard.ReadUnicodeTextValue();
        if (text is not null)
        {
            return new ClipboardSnapshot(ClipboardSnapshotKind.Text, text, text, []);
        }

        return null;
    }

    public SyncMessage? ToSyncMessage(TransferStore transferStore, int transferPort)
    {
        if (Kind == ClipboardSnapshotKind.Text)
        {
            return new SyncMessage(SyncMessageTypes.TextChanged, Text: Text ?? string.Empty);
        }

        var flattened = FlattenPaths(Paths);
        if (flattened.Count == 0)
        {
            return null;
        }

        var transferId = transferStore.Add(flattened.Select(item => item.SourcePath).ToList());
        return new SyncMessage(
            SyncMessageTypes.FilesChanged,
            TransferId: transferId,
            TransferPort: transferPort,
            Items: flattened.Select(item => new VirtualFileItem(item.RelativePath, item.Length, (int)item.Attributes)).ToList());
    }

    private static List<FlattenedClipboardFile> FlattenPaths(List<string> paths)
    {
        var result = new List<FlattenedClipboardFile>();

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                result.Add(new FlattenedClipboardFile(info.Name, info.FullName, info.Length, info.Attributes));
                continue;
            }

            if (!Directory.Exists(path))
            {
                continue;
            }

            var root = new DirectoryInfo(path);
            foreach (var file in root.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                var relative = Path.Combine(root.Name, Path.GetRelativePath(root.FullName, file.FullName));
                result.Add(new FlattenedClipboardFile(relative, file.FullName, file.Length, file.Attributes));
            }
        }

        return result;
    }
}

sealed record FlattenedClipboardFile(string RelativePath, string SourcePath, long Length, FileAttributes Attributes);

enum ClipboardSnapshotKind
{
    Text,
    Files
}
