sealed record SendOptions(string? FilePath, bool UseClipboard, string Host, int Port, int ChunkSize)
{
    public static SendOptions Parse(string[] args)
    {
        string? filePath = null;
        string? host = null;
        var port = Defaults.Port;
        var chunkSize = Defaults.ChunkSize;
        var useClipboard = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--host" when i + 1 < args.Length:
                    host = args[++i];
                    break;
                case "--port" when i + 1 < args.Length:
                    port = ParsePort(args[++i]);
                    break;
                case "--chunk-size" when i + 1 < args.Length:
                    chunkSize = ParsePositiveInt(args[++i], "chunk size");
                    break;
                case "--clipboard":
                    useClipboard = true;
                    break;
                case "-h" or "--help":
                    throw new KlipException("Usage: Klip send <file> --host <address> [--port 45245] or Klip send --clipboard --host <address>");
                default:
                    if (args[i].StartsWith('-'))
                    {
                        throw new KlipException($"Unknown send option: {args[i]}");
                    }

                    filePath ??= args[i];
                    break;
            }
        }

        if (filePath is null)
        {
            useClipboard = true;
        }

        if (filePath is not null && useClipboard)
        {
            throw new KlipException("Provide either a file path or --clipboard, not both.");
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            throw new KlipException("Missing --host <address>.");
        }

        return new SendOptions(filePath, useClipboard, host, port, chunkSize);
    }

    private static int ParsePort(string value)
    {
        var port = ParsePositiveInt(value, "port");
        if (port > 65535)
        {
            throw new KlipException("Port must be between 1 and 65535.");
        }

        return port;
    }

    private static int ParsePositiveInt(string value, string name)
    {
        if (!int.TryParse(value, out var result) || result <= 0)
        {
            throw new KlipException($"Invalid {name}: {value}");
        }

        return result;
    }
}

sealed record ReceiveOptions(int Port, string OutputFolder, bool Overwrite, bool CopyToClipboard)
{
    public static ReceiveOptions Parse(string[] args)
    {
        var port = Defaults.Port;
        var outputFolder = "received";
        var overwrite = false;
        var copyToClipboard = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port" when i + 1 < args.Length:
                    port = ParsePort(args[++i]);
                    break;
                case "--out" when i + 1 < args.Length:
                    outputFolder = args[++i];
                    break;
                case "--overwrite":
                    overwrite = true;
                    break;
                case "--clipboard":
                    copyToClipboard = true;
                    break;
                case "-h" or "--help":
                    throw new KlipException("Usage: Klip receive [--port 45245] [--out <folder>] [--overwrite] [--clipboard]");
                default:
                    throw new KlipException($"Unknown receive option: {args[i]}");
            }
        }

        return new ReceiveOptions(port, outputFolder, overwrite, copyToClipboard);
    }

    private static int ParsePort(string value)
    {
        if (!int.TryParse(value, out var port) || port is < 1 or > 65535)
        {
            throw new KlipException("Port must be between 1 and 65535.");
        }

        return port;
    }
}

sealed record SyncServerOptions(int Port, int ContentPort)
{
    public static SyncServerOptions Parse(string[] args)
    {
        var port = Defaults.Port;
        var contentPort = Defaults.Port + 1;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port" when i + 1 < args.Length:
                    port = ParsePort(args[++i]);
                    break;
                case "--content-port" when i + 1 < args.Length:
                    contentPort = ParsePort(args[++i], "content port");
                    break;
                case "-h" or "--help":
                    throw new KlipException("Usage: Klip server [--port 45245] [--content-port 45246]");
                default:
                    throw new KlipException($"Unknown server option: {args[i]}");
            }
        }

        return new SyncServerOptions(port, contentPort);
    }

    private static int ParsePort(string value, string name = "port")
    {
        if (!int.TryParse(value, out var port) || port is < 1 or > 65535)
        {
            throw new KlipException($"{name} must be between 1 and 65535.");
        }

        return port;
    }
}

sealed record SyncClientOptions(string Host, int Port, int ContentPort)
{
    public static SyncClientOptions Parse(string[] args)
    {
        string? host = null;
        var port = Defaults.Port;
        var contentPort = Defaults.Port + 1;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--host" when i + 1 < args.Length:
                    host = args[++i];
                    break;
                case "--port" when i + 1 < args.Length:
                    port = ParsePort(args[++i], "port");
                    break;
                case "--content-port" when i + 1 < args.Length:
                    contentPort = ParsePort(args[++i], "content port");
                    break;
                case "-h" or "--help":
                    throw new KlipException("Usage: Klip client --host <address> [--port 45245] [--content-port 45246]");
                default:
                    throw new KlipException($"Unknown client option: {args[i]}");
            }
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            throw new KlipException("Missing --host <address>.");
        }

        return new SyncClientOptions(host, port, contentPort);
    }

    private static int ParsePort(string value, string name)
    {
        if (!int.TryParse(value, out var port) || port is < 1 or > 65535)
        {
            throw new KlipException($"{name} must be between 1 and 65535.");
        }

        return port;
    }
}
