static class InteractiveMenu
{
    public static async Task<int> RunAsync()
    {
        while (true)
        {
            TryClear();
            Console.WriteLine("Klip");
            Console.WriteLine("====");
            Console.WriteLine("1. Server - receive synced clipboard and paste here");
            Console.WriteLine("2. Client - watch this clipboard and sync to server");
            Console.WriteLine("3. Send clipboard once");
            Console.WriteLine("4. Send file once");
            Console.WriteLine("5. Receive file once");
            Console.WriteLine("6. Help");
            Console.WriteLine("0. Exit");
            Console.WriteLine();

            var choice = Prompt("Choose", "1");
            Console.WriteLine();

            var args = choice switch
            {
                "1" => BuildServerArgs(),
                "2" => BuildClientArgs(),
                "3" => BuildSendClipboardArgs(),
                "4" => BuildSendFileArgs(),
                "5" => BuildReceiveArgs(),
                "6" => ["--help"],
                "0" => null,
                _ => []
            };

            if (args is null)
            {
                return 0;
            }

            if (args.Length == 0)
            {
                Pause("Unknown choice.");
                continue;
            }

            Console.WriteLine();
            var result = await KlipApp.RunAsync(args);

            if (args[0] is "server" or "client" || result == 130)
            {
                return result;
            }

            Pause("Press Enter to return to the menu...");
        }
    }

    private static string[] BuildServerArgs()
    {
        var port = Prompt("Server port", Defaults.Port.ToString());
        return ["server", "--port", port];
    }

    private static string[] BuildClientArgs()
    {
        var host = PromptRequired("Server IP or host");
        var port = Prompt("Server port", Defaults.Port.ToString());
        var contentPort = Prompt("Client content port", (Defaults.Port + 1).ToString());
        return ["client", "--host", host, "--port", port, "--content-port", contentPort];
    }

    private static string[] BuildSendClipboardArgs()
    {
        var host = PromptRequired("Receiver IP or host");
        var port = Prompt("Receiver port", Defaults.Port.ToString());
        return ["send", "--clipboard", "--host", host, "--port", port];
    }

    private static string[] BuildSendFileArgs()
    {
        var path = PromptRequired("File path");
        var host = PromptRequired("Receiver IP or host");
        var port = Prompt("Receiver port", Defaults.Port.ToString());
        return ["send", path, "--host", host, "--port", port];
    }

    private static string[] BuildReceiveArgs()
    {
        var port = Prompt("Receiver port", Defaults.Port.ToString());
        var output = Prompt("Output folder", "received");
        var copyToClipboard = PromptYesNo("Put received file on clipboard", defaultValue: true);

        return copyToClipboard
            ? ["receive", "--port", port, "--out", output, "--clipboard"]
            : ["receive", "--port", port, "--out", output];
    }

    private static string Prompt(string label, string defaultValue)
    {
        Console.Write($"{label} [{defaultValue}]: ");
        var value = Console.ReadLine();
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
    }

    private static string PromptRequired(string label)
    {
        while (true)
        {
            Console.Write($"{label}: ");
            var value = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim().Trim('"');
            }

            Console.WriteLine("Please enter a value.");
        }
    }

    private static bool PromptYesNo(string label, bool defaultValue)
    {
        var suffix = defaultValue ? "Y/n" : "y/N";
        Console.Write($"{label} [{suffix}]: ");
        var value = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Trim().Equals("y", StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static void Pause(string message)
    {
        Console.WriteLine();
        Console.Write(message);
        Console.ReadLine();
    }

    private static void TryClear()
    {
        try
        {
            Console.Clear();
        }
        catch (IOException)
        {
            Console.WriteLine();
        }
    }
}
