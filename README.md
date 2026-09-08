# OCRX Plugins

This repository is the official source for plugins used by OCRX Engine. A
plugin uses `Ocrx.Sdk` to subscribe to the engine's OCR stream, interpret the
text extracted from screen images, and emit plugin-specific records. The SDK
owns the connection, subscription, reconnection, and shutdown lifecycle.

## Current official plugins

| Plugin | Purpose |
| --- | --- |
| [SignaturePlugin](src/SignaturePlugin/README.md) | Interprets mining scan signatures as ore-cluster metadata. |

## Create a plugin

### 1. Create the projects

From the repository root, create a plugin project and its tests under the
existing source layout:

```powershell
dotnet new console -o src/MyPlugin
dotnet new xunit -o tests/MyPlugin.Tests
dotnet sln OcrxPlugins.slnx add src/MyPlugin/MyPlugin.csproj
dotnet sln OcrxPlugins.slnx add tests/MyPlugin.Tests/MyPlugin.Tests.csproj
```

Target plain `net10.0` and reference the released SDK packages. Keep all
`Ocrx.*` packages on the same version train:

```xml
<!-- src/MyPlugin/MyPlugin.csproj -->
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <TargetFramework>net10.0</TargetFramework>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Ocrx.Contracts" Version="2.0.0" />
  <PackageReference Include="Ocrx.Sdk" Version="2.0.0" />
</ItemGroup>

<ItemGroup>
  <None Update="config.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

The test project references the plugin plus the SDK test helpers:

```xml
<!-- tests/MyPlugin.Tests/MyPlugin.Tests.csproj -->
<ItemGroup>
  <ProjectReference Include="..\..\src\MyPlugin\MyPlugin.csproj" />
  <PackageReference Include="Ocrx.Sdk.Testing" Version="2.0.0" />
</ItemGroup>
```

Create `src/MyPlugin/config.json` with the pipe used by the engine:

```json
{
  "pipeName": "OCRX.Engine",
  "saveDebugFrames": false
}
```

### 2. Declare the text to read

An ROI tells the engine which part of the image to OCR. Coordinates use OCRX's
2560×1440 reference space; the engine maps them to the captured frame.

```csharp
// src/MyPlugin/PluginRois.cs
using Ocrx.Contracts;
using Ocrx.Sdk;

namespace MyPlugin;

public static class PluginRois
{
    public static readonly RoiSubscription Status =
        new("status", new RoiRect(1000, 110, 420, 100), 3.0, RoiKind.Text);

    public static readonly IReadOnlyList<RoiSubscription> All = [Status];
}
```

### 3. Interpret each OCR tick

Implement `IOcrxPlugin`. This example emits a record only when the recognized
text changes:

```csharp
// src/MyPlugin/MyPlugin.cs
using Ocrx.Sdk;

namespace MyPlugin;

public sealed class MyPlugin : IOcrxPlugin
{
    private string? _lastValue;

    public string Name => "MyPlugin";
    public IReadOnlyList<RoiSubscription> Rois => PluginRois.All;

    public Task OnTickAsync(TickContext ctx, CancellationToken ct)
    {
        if (!ctx.Tick.TryGetText(PluginRois.Status.Id, out var text))
            return Task.CompletedTask;

        var value = text.Trim();
        if (value.Length == 0 || value == _lastValue)
            return Task.CompletedTask;

        _lastValue = value;
        ctx.Services.Emit(new CaptureRecord(
            ctx.Tick.Timestamp, Name, TriggerKind.Auto, value));
        return Task.CompletedTask;
    }
}
```

The `TryGetText` result distinguishes a failed OCR read from a genuine blank
value. Add the host entry point in `src/MyPlugin/Program.cs`:

```csharp
using Ocrx.Sdk;

return await OcrxPluginHost.RunAsync(new MyPlugin.MyPlugin(), args);
```

### 4. Run and test

Start OCRX Engine, then run the plugin from the repository root:

```powershell
dotnet run --project src/MyPlugin -- --verbose
```

Run the repository test suite before submitting a change:

```powershell
dotnet test OcrxPlugins.slnx --filter "Category!=Integration"
```

## References

- [SignaturePlugin](src/SignaturePlugin/README.md) — the current official plugin.
- [SDK plugin-authoring guide](https://github.com/PetitCastor/ocrx-sdk/blob/master/docs/PLUGIN-AUTHORING.md) — detailed SDK behavior and testing guidance.
