# Klip

Klip is a small TCP file transfer CLI built with C# and .NET 8.

## Build

```powershell
dotnet build
```

## Run the EXE menu

Publish the Windows executable:

```powershell
dotnet publish Klip.csproj -c Release -r win-x64 --self-contained false -o publish
```

Then open:

```powershell
.\publish\Klip.exe
```

Launching the exe without command-line arguments shows a menu where you can choose server, client, send, or receive mode.

If the target PC has a broken or missing .NET install, publish a self-contained build:

```powershell
dotnet publish Klip.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o publish-self-contained --configfile NuGet.Online.config
```

Copy the whole `publish-self-contained` folder to the other PC and run `Klip.exe` from inside that folder. Do not copy only `Klip.exe`; it needs the companion runtime files in the same folder.

## Receive a file

Run this on the machine that should receive the file:

```powershell
dotnet run -- receive --port 45245 --out received
```

To also place each received file on the Windows clipboard as a virtual file:

```powershell
dotnet run -- receive --clipboard --port 45245 --out received
```

## Send a file

Run this on the machine that has the file:

```powershell
dotnet run -- send .\example.zip --host 192.168.1.20 --port 45245
```

## Send clipboard content

The sender can detect clipboard content on Windows. It checks for copied files first, then copied text. Clipboard text is sent as a generated `.txt` file.

```powershell
dotnet run -- send --clipboard --host 192.168.1.20 --port 45245
```

If no file path is provided, `send` also uses the clipboard:

```powershell
dotnet run -- send --host 192.168.1.20 --port 45245
```

## Protocol

Klip uses a framed TCP protocol:

- `KLIP` magic bytes
- protocol version `1`
- frame type
- payload length
- payload bytes

Frame types:

- `Metadata`: JSON file name, byte length, and SHA-256 hash
- `Data`: file chunk bytes
- `End`: marks the end of file data
- `Ack`: receiver acknowledgement
- `Error`: receiver-side error message

The receiver writes to a temporary `.klip-part` file, verifies the byte count and SHA-256 hash, then moves the completed file into the output folder.

With `receive --clipboard`, the receiver also publishes a Windows `IDataObject` that exposes:

- `FileGroupDescriptorW`
- `FileContents`

This lets compatible paste targets receive the incoming file from the clipboard as a virtual file object.

## Always-on clipboard sync

Start the server on one machine:

```powershell
dotnet run -- server --port 45245 --content-port 45246
```

Start the client on the other machine:

```powershell
dotnet run -- client --host 192.168.1.20 --port 45245 --content-port 45246
```

After startup:

- copied text syncs in both directions
- copied files/folders sync as virtual clipboard metadata in both directions
- real file bytes are transferred only when the other side pastes

For lazy file/folder paste, each machine must be able to connect to the other machine's `--content-port`.
