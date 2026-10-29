namespace ItemTrackerPlugin.Screen;

/// <summary>Which inventory screen the frame shows, decided from the panel headers alone.</summary>
public enum ScreenContext
{
    /// <summary>Nothing recognisable. The Freight Manager is a world-space screen with no fixed
    /// regions and lands here; reading its frames produced junk in the tooltip regions.</summary>
    Unknown,

    /// <summary>The right panel header reads <c>LOCAL / LOCAL</c>: the station storage of a kiosk.</summary>
    Kiosk,

    /// <summary>The right panel header reads <c>&lt;SHIP&gt; / PERSONAL STORAGE</c>.</summary>
    ShipStorage,

    /// <summary>No right panel, but the left one is the player's own inventory.</summary>
    Personal,
}
