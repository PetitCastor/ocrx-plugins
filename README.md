# Build an OCRX plugin

An OCRX plugin is a small `net10.0` console application that turns OCRX screen
readings into useful records. You declare the screen regions to read and write
the tracker logic. The OCRX Engine owns capture, OCR, the local connection,
reconnects, cancellation, and the plugin's run summary.

You do **not** need to clone the engine or this repository to create a plugin.
Start with the public SDK template and NuGet packages.

## How the public pieces fit together

| Component | Purpose | Visibility |
| --- | --- | --- |
| [OCRX releases](https://github.com/PetitCastor/ocrx-releases/releases) | The Windows engine binary that your plugin connects to. | Public |
| [OCRX SDK](https://github.com/PetitCastor/ocrx-sdk) | `Ocrx.Contracts`, `Ocrx.Sdk`, testing tools, and `dotnet new` template. | Public |
| This repository | Maintained example plugins and the catalog used to publish them. | Public |
| `ocrx-engine` | Capture-engine source. Plugin authors consume its released binary; its source is private. | Private |

The boundary is deliberate: plugins reference published `Ocrx.*` packages from
NuGet, never a local engine or SDK project through `ProjectReference`.

## Create your first plugin

### 1. Install the prerequisites

- Windows 10 or 11, with an OCR language pack installed for the language you
  want the engine to read.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- A running OCRX Engine. Download and unpack the appropriate
  `Ocrx.Engine-*-win-x64.zip` from [OCRX releases](https://github.com/PetitCastor/ocrx-releases/releases).

The engine normally listens on the local `OCRX.Engine` named pipe. No game,
engine checkout, Windows capture code, or gRPC client is required in your
plugin project.

### 2. Generate a project

Open PowerShell in the directory where you keep your own code, then run:

```powershell
dotnet new install Ocrx.Plugin.Template --version 2.0.0
dotnet new ocrx-plugin -n MyPlugin
cd MyPlugin
dotnet test tests/MyPlugin.Tests.csproj --filter "Category!=Integration"
```

The template creates the application, a unit-test project, `config.json`, and
a working example that reads one text region. All SDK packages are pinned to
the same OCRX 2 version train. Use `dotnet new ocrx-plugin -h` to see template
options, including `--SdkVersion` when you intentionally need a different
published SDK version.

### 3. Start it against OCRX Engine

Start `Ocrx.Engine.exe`, then launch the generated plugin:

```powershell
dotnet run -- --verbose
```

On first connect, the host reports the engine version, frame size, cadence, and
the regions it subscribed to. The host handles waiting for the engine and
reconnecting if it restarts.

`config.json` contains the shared engine settings:

```json
{
  "pipeName": "OCRX.Engine",
  "saveDebugFrames": false
}
```

Keep `pipeName` aligned with the engine. You can override it for one run with
`--pipe <name>`. `--verbose` enables diagnostic messages without changing the
plugin's normal output.

### 4. Calibrate the first region

The generated `Rois.Counter` is only a placeholder. Before adding tracker
logic, point it at a real part of the UI:

1. Set `"saveDebugFrames": true` in `config.json`.
2. Run the plugin with `--verbose` while the target screen is visible.
3. Trigger the engine capture hotkey (configured by the engine; commonly
   `Ctrl+Shift+F12`).
4. Use the PNG path printed by the plugin to compare the captured crop with
   the desired UI element.
5. Adjust `RoiRect(x, y, width, height)` and `Scale` in `Rois.cs`, then repeat.

Every ROI uses OCRX's fixed **2560×1440 reference space**. The engine maps that
rectangle to the actual captured frame, so your plugin should not scale it for
the monitor itself. For small text, an OCR `Scale` of 2–4 is usually a good
starting point.

### 5. Replace the example tracker

`Rois.cs` declares what to scan; `MyCapturePlugin.cs` decides what each scanned
tick means. A text tracker has this shape:

```csharp
public Task OnTickAsync(TickContext ctx, CancellationToken ct)
{
    if (!ctx.Tick.TryGetText(MyPlugin.Rois.Counter.Id, out var text))
        return Task.CompletedTask;

    var value = text.Trim();
    if (value.Length == 0 || value == _last)
        return Task.CompletedTask;

    _last = value;
    ctx.Services.Emit(new CaptureRecord(
        ctx.Tick.Timestamp, Name, TriggerKind.Auto, value));
    return Task.CompletedTask;
}
```

Use the `TryGetText`, `TryGetOcr`, and `TryGetPixels` accessors. They preserve
the important distinction between a blank result and a region that failed to
read. Ticks are delivered sequentially, so ordinary per-plugin state such as
`_last` does not need locking.

Choose the ROI kind that fits the job:

| Need | ROI kind | Read from the tick |
| --- | --- | --- |
| A label, counter, or status line | `Text` | `TryGetText` |
| Word positions in a table or panel | `Detailed` | `TryGetOcr` |
| A small colour or visual-state probe | `Pixels` | `TryGetPixels` |

The plugin host already owns the pipe protocol and lifecycle. Keep your code
on this SDK boundary: do not add capture code, generated protobuf types, or a
direct gRPC client.

### 6. Test and package it

Run fast unit tests without an engine:

```powershell
dotnet test tests/MyPlugin.Tests.csproj --filter "Category!=Integration"
```

When you add replay-parity tests, point them at an unpacked or built engine and
run the full test project. Replay needs a Windows OCR language pack and a
corpus, but not a running game:

```powershell
$env:OCRX_ENGINE_PATH = "C:\tools\ocrx\Ocrx.Engine.exe"
dotnet test tests/MyPlugin.Tests.csproj
```

To distribute a standalone Windows build:

```powershell
dotnet publish -c Release -r win-x64 --self-contained
```

Run the published executable while OCRX Engine is running. Keep its
`config.json` beside the executable when your plugin relies on the default
configuration.

## Contribute a maintained plugin here

This repository contains maintained SDK consumers, not the SDK or engine.
`SignaturePlugin` is the stable catalog entry; `MissionPlugin` and
`RefineryPlugin` remain as development and regression examples.

To add a maintained plugin to this repository, place the application under
`src/MyPlugin/`, its tests under `tests/MyPlugin.Tests/`, and add both to
`OcrxPlugins.slnx`:

```powershell
dotnet sln OcrxPlugins.slnx add src/MyPlugin/MyPlugin.csproj
dotnet sln OcrxPlugins.slnx add tests/MyPlugin.Tests/MyPlugin.Tests.csproj
```

Use released `Ocrx.Contracts`, `Ocrx.Sdk`, and, where useful,
`Ocrx.Sdk.Testing` packages at the same version. Do not create a cross-repo
project reference. `engine-version.txt` pins the released engine used by this
repository's replay CI; update it only after that engine release exists.

Add a new catalog entry only when it is ready to be released. `plugins.json`
lists stable plugins; release selection is controlled by the
[release manifests](.github/release-manifests/README.md).

## Next steps

- [Full plugin-authoring guide](https://github.com/PetitCastor/ocrx-sdk/blob/master/docs/PLUGIN-AUTHORING.md) — configuration, outputs, error handling, session events, calibration, and tests.
- [Replay and parity testing](https://github.com/PetitCastor/ocrx-sdk/blob/master/docs/REPLAY.md).
- [Compatibility guide](https://github.com/PetitCastor/ocrx-sdk/blob/master/docs/COMPATIBILITY.md) — engine, SDK, and protocol compatibility.
- [SignaturePlugin guide](src/SignaturePlugin/README.md) — a complete maintained example.
