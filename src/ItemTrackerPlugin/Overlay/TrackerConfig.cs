using Ocrx.Sdk;

namespace ItemTrackerPlugin.Overlay;

/// <summary>
/// The host config the tracker runs on, built in code: its only output is the in-game status line.
/// Look and position are the SDK overlay's defaults, owned by the SDK and not styled per plugin, so
/// there is no config file to seed and nothing for a user to edit here; the tracker's own settings
/// travel through the Settings panel.
/// </summary>
public sealed class TrackerConfig : PluginConfig
{
    /// <summary>The overlay shows <c>{status}</c> and stays up until the plugin clears it.</summary>
    public static TrackerConfig Create() => new()
    {
        Outputs =
        [
            new SinkSpec
            {
                Type = "overlay",
                Overlay = new OverlaySpec { Template = "{" + StatusRecord.StatusField + "}", LingerMs = 0 },
            },
        ],
    };
}
