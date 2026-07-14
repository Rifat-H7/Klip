# Klip

Klip is a Windows-focused TCP file transfer and clipboard sync tool built with C# and .NET 8.

It supports two workflows:

- one-shot file or clipboard transfer
- always-on bidirectional clipboard sync, similar in spirit to remote-control tools where copied text/files can be pasted on the other machine

## Requirements

- Windows for clipboard and virtual-file paste features
- .NET 8 SDK to build from source
- network/firewall access between the two machines

## Build

From the project folder:

```powershell
cd Src\Klip
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

Launching `Klip.exe` without command-line arguments shows an interactive menu.

If the target PC has a broken or missing .NET install, publish a self-contained build:

```powershell
dotnet publish Klip.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o publish-self-contained --configfile NuGet.Online.config
```

Copy the whole `publish-self-contained` folder to the other PC and run `Klip.exe` from inside that folder. Do not copy only `Klip.exe`; it needs the companion runtime files in the same folder.

## One-Shot File Transfer

Run this on the receiving machine:

```powershell
dotnet run -- receive --port 45245 --out received
```

Run this on the sending machine:

```powershell
dotnet run -- send .\example.zip --host 192.168.1.20 --port 45245
```

To also place each received file on the Windows clipboard as a virtual file:

```powershell
dotnet run -- receive --clipboard --port 45245 --out received
```

## One-Shot Clipboard Send

The sender can detect clipboard content on Windows. It checks for copied files first, then copied text. Clipboard text is sent as a generated `.txt` file.

```powershell
dotnet run -- send --clipboard --host 192.168.1.20 --port 45245
```

If no file path is provided, `send` also uses the clipboard:

```powershell
dotnet run -- send --host 192.168.1.20 --port 45245
```

## Bidirectional Clipboard Sync

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
- real file bytes transfer only when the other side pastes
- Explorer may show the native Windows copy dialog when it consumes the virtual file stream

For lazy file/folder paste, each machine must be able to connect to the other machine's `--content-port`. If pasting files does not start, check Windows Firewall for both the main sync port and the content port.

## Current Limitations

- The sync server handles one active client session at a time.
- The protocol is not encrypted or authenticated, so use it only on trusted networks.
- Clipboard sync is Windows-only.
- File/folder sync uses virtual clipboard files; actual bytes move during paste, not when the file is copied.

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
- `Control`: clipboard sync and lazy-transfer control messages

The one-shot receiver writes to a temporary `.klip-part` file, verifies the byte count and SHA-256 hash, then moves the completed file into the output folder.

Clipboard file paste uses Windows `IDataObject` virtual-file formats:

- `FileGroupDescriptorW`
- `FileContents`
- `Preferred DropEffect`
