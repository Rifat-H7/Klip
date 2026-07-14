static class Defaults
{
    public const int Port = 45245;
    public const int ChunkSize = 64 * 1024;
}

static class KlipApp
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return await InteractiveMenu.RunAsync();
        }

        if (IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            shutdown.Cancel();
            Console.WriteLine();
            Console.WriteLine("Stopping...");
        };

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "send" => await SendCommand.RunAsync(args[1..], shutdown.Token),
                "receive" or "recv" => await ReceiveCommand.RunAsync(args[1..], shutdown.Token),
                "server" => await SyncServerCommand.RunAsync(args[1..], shutdown.Token),
                "client" => await SyncClientCommand.RunAsync(args[1..], shutdown.Token),
                _ => Fail($"Unknown command '{args[0]}'. Run 'Klip --help'.")
            };
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
        catch (KlipException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        catch (SocketException ex)
        {
            Console.Error.WriteLine($"Socket error: {ex.Message}");
            return 1;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"I/O error: {ex.Message}");
            return 1;
        }
    }

    private static bool IsHelp(string value) =>
        value is "-h" or "--help" or "help" or "/?";

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"Error: {message}");
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
        Klip - TCP file transfer for .NET 8

        Usage:
          Klip receive [--port 45245] [--out <folder>] [--overwrite] [--clipboard]
          Klip send <file> --host <address> [--port 45245] [--chunk-size <bytes>]
          Klip send --clipboard --host <address> [--port 45245]
          Klip server [--port 45245] [--content-port 45246]
          Klip client --host <address> [--port 45245] [--content-port 45246]

        Examples:
          Klip receive --port 45245 --out received
          Klip receive --clipboard --out received
          Klip send .\photo.jpg --host 192.168.1.20 --port 45245
          Klip send --clipboard --host 192.168.1.20
          Klip server --content-port 45246
          Klip client --host 192.168.1.20

        Protocol:
          KLIP/1 framed TCP stream with metadata, data chunks, ACK/ERROR frames,
          and SHA-256 verification at the receiver.
        """);
    }
}
