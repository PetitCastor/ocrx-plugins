using Ocrx.Contracts;
using Ocrx.Sdk;
using Ocrx.Sdk.Testing;

namespace SignaturePlugin.Tests;

/// <summary>
/// One simulated engine, screen and tracker: the <see cref="OcrScene"/> is scanned through the ROI set the
/// tracker last produced, one tick later — the engine's own lag — and everything the engine would be asked
/// to do is counted: ROI pushes (the set changing), OCR area (upscaled pixels summed over every ROI, the
/// thing OCR time follows) and search tiles.
/// </summary>
internal sealed class TrackerRig
{
    private IReadOnlyList<RoiSubscription> _held;

    public TrackerRig(OcrScene? scene = null, Func<double, bool>? isKnown = null, TimeSpan? scanInterval = null)
    {
        Scene = scene ?? new OcrScene();
        Tracker = new BadgeTracker(Rois.Counter, isKnown);
        Tracker.Frame(Scene.Width, Scene.Height, scanInterval ?? TimeSpan.FromMilliseconds(100));
        _held = Tracker.CurrentSet();
    }

    public OcrScene Scene { get; }
    public BadgeTracker Tracker { get; }
    public FakePluginServices Services { get; } = new();

    /// <summary>The set the engine holds, which is the one the next tick is scanned with.</summary>
    public IReadOnlyList<RoiSubscription> Held => _held;

    public int Ticks { get; private set; }

    /// <summary>Times the set changed: each is an <c>UpdateRoisAsync</c> and the engine work behind it.</summary>
    public int Pushes { get; private set; }

    /// <summary>Ticks whose scan carried a search tile.</summary>
    public int TileTicks { get; private set; }

    /// <summary>Upscaled OCR pixels summed over every ROI of every tick.</summary>
    public double Area { get; private set; }

    /// <summary>The OCR area the last tick was scanned with.</summary>
    public double LastArea { get; private set; }

    /// <summary>Whether the last tick's scan carried a search tile.</summary>
    public bool LastHadTile { get; private set; }

    public int Acquired => Services.VerboseLogs.Count(l => l.Contains("acquired"));
    public int Rejected => Services.VerboseLogs.Count(l => l.Contains("rejected: no number read"));
    public int Searched => Services.VerboseLogs.Count(l => l.Contains("found by search"));

    public RoiRect Counter => Tracker.CurrentSet()[0].Rect;

    /// <summary>One tick: scan, observe, and push whatever changed.</summary>
    public void Step()
    {
        var scanned = _held;
        LastArea = 0;
        LastHadTile = false;
        foreach (var roi in scanned)
        {
            var frame = RoiScaler.ToFrame(roi.Rect, Scene.Width, Scene.Height);
            LastArea += frame.Width * roi.Scale * frame.Height * roi.Scale;
            if (roi.Id == BadgeTracker.TileId)
                LastHadTile = true;
        }

        Area += LastArea;
        if (LastHadTile)
            TileTicks++;

        Tracker.Observe(Scene.Scan(scanned), Services);
        var next = Tracker.CurrentSet();
        if (!next.SequenceEqual(_held))
            Pushes++;
        _held = next;
        Ticks++;
    }

    public void Run(int ticks)
    {
        for (var i = 0; i < ticks; i++)
            Step();
    }
}
