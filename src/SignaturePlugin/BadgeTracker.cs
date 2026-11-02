using Ocrx.Contracts;
using Ocrx.Sdk;

namespace SignaturePlugin;

/// <summary>
/// Follows the RS signature number across the screen with the counter ROI itself, and places that ROI
/// where the number is going to be.
/// </summary>
/// <remarks>
/// The badge is not a fixed HUD element: it is anchored to the scanned target in world space, so it travels
/// as the ship turns (measured 2026-11-01: 8-10 px per 100 ms). The counter is a <c>Detailed</c> ROI at the
/// calibrated scale 6, so each read of it already says where the number is — the word box OCR drew around
/// it, accurate to about half a pixel at that scale (sd 0.5 px, max 3.5 over 324 shifted reads of four real
/// cockpit frames; the same reading at scale 2-3 wobbled by up to 9 px). Nothing depends on the badge's
/// colour, shape or icon, which differ from ship to ship.
/// <para>
/// Costs, because this runs in the engine's scan loop on a game machine, are the design:
/// </para>
/// <list type="bullet">
/// <item><b>Idle</b> (nothing read): the calibrated counter, no tiles, no pushes — exactly the plugin
/// before tracking existed.</item>
/// <item><b>Tracking</b>: the same single ROI, still read at scale 6; it is moved only when the number is
/// about to leave the safe zone inside it. A push is not free (the engine re-publishes the ROI set to
/// its UI and overlay), so the rect is placed with <see cref="HoldTicks"/> ticks of headroom in the direction
/// of motion and left alone until used up: nothing while the number stands still, and at the measured
/// 10 px a tick a push about every <see cref="HoldTicks"/> ticks. Margins only ever grow past the
/// calibrated ones — a tight crop misreads 4 as 8 — never shrink below them.</item>
/// <item><b>Lost</b> (the number read within the last <see cref="LostTimeout"/> and not now): one small
/// <c>Detailed</c> tile, <see cref="TileFrameWidth"/>x<see cref="TileFrameHeight"/> at scale
/// <see cref="TileScale"/>, every <see cref="SearchEveryTicks"/> ticks, centred where the number should
/// be by dead reckoning. Then it gives up: the counter returns to the calibrated rect, once.</item>
/// </list>
/// There is no whole-screen search: tiling the screen with OCR tiles on every tick was measured at 25-55 ms of
/// OCR per tick and a push almost every tick, so a badge that appears far from the calibrated spot is not found
/// by this plugin, and one that leaves the rect is chased only briefly.
/// <para>
/// Verification: a place a search tile found is a word OCR liked the look of, and a decoy can look the
/// same. If the counter, moved there, does not read a number within <see cref="RejectAfterBlankTicks"/>
/// ticks the place is discarded and ignored for a while (doubling each time it comes back).
/// </para>
/// <para>
/// Not thread-safe: driven from the tick loop only (see <c>SignaturePlugin.FollowAsync</c>).
/// </para>
/// </remarks>
internal sealed class BadgeTracker
{
    /// <summary>Search tile size in frame pixels, read at <see cref="TileScale"/>: about 12 ms (400x250 at 2x
    /// found a number that was plainly on screen at 28-30 of 35 positions on the four cockpit frames). Big
    /// enough to catch a 150 px flick of the camera, small enough to be one read every few ticks.</summary>
    public const int TileFrameWidth = 400, TileFrameHeight = 250;

    public const double TileScale = 2.0;

    /// <summary>Ticks without a read before the number counts as lost and searching starts.</summary>
    public const int LostAfterMisses = 4;

    /// <summary>While lost, one search tile this often.</summary>
    public const int SearchEveryTicks = 5;

    /// <summary>How long after its last read a lost number is looked for before the counter goes back.</summary>
    public static readonly TimeSpan LostTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Ticks a place found by a search tile gets to produce a read before it is called a decoy.</summary>
    public const int RejectAfterBlankTicks = 6;

    /// <summary>Ticks a rejected place is ignored the first time; doubles on each repeat up to <see cref="MaxRejectTicks"/>.</summary>
    public const int RejectTicks = 150, MaxRejectTicks = 1200;

    /// <summary>A candidate this close to a rejected place (reference px) is the same decoy.</summary>
    public const int RejectRadius = 60;

    /// <summary>A search-tile candidate this near an interior tile edge (frame px) may be cut by it.</summary>
    public const int EdgeMarginPx = 8;

    /// <summary>
    /// Ticks of motion the counter is given room for when it is placed. The number leaves a rect placed with
    /// room for N ticks after N ticks at that speed, so at the measured 10 px a tick this is a push every
    /// 5 ticks (2 a second) however fast it is going, as long as the headroom below is not the limit.
    /// </summary>
    public const int HoldTicks = 5;

    /// <summary>Most headroom added in the direction of motion, reference px at FOV zoom 1. Measured on four
    /// cockpit frames at scale 6: extra margin of 80 px on the left (the pin icon comes into the crop) or the
    /// right, and 40 above, still read correctly once the pin and other words are discarded
    /// (<see cref="CounterReader"/> takes the number word alone); OCR time for a 120x44 rect (1.7x the area) is the same
    /// as for 70x44 within noise (about 4-8 ms), and rises only past ~2x.</summary>
    public const int MaxExtraX = 60, MaxExtraY = 30;

    /// <summary>Cap on the placed rect's area against the calibrated one, so a fast, diagonal move cannot
    /// make the counter a large crop.</summary>
    public const double MaxAreaFactor = 2.4;

    /// <summary>Extra margin all round (reference px) for the wobble of OCR word boxes at scale 6 (sd 0.5 px;
    /// max seen 1.5 across, 3.5 down), so a number standing still never reaches the edge of its safe zone.</summary>
    public const int SlackX = 2, SlackY = 4;

    /// <summary>Largest displacement, in reference pixels per tick, the placement extrapolates.</summary>
    public const int MaxLead = 24;

    /// <summary>Consecutive cut reads the rect is moved for, and the widest the number is taken to grow because
    /// of them, as a multiple of the calibrated counter width (a seven-character number is ~1.7 times "2,000").</summary>
    public const int MaxCutRun = 3;

    public const double MaxCutWidth = 1.5;

    /// <summary>About one digit's advance (reference px at zoom 1; "2,000" is 46 wide for five characters): how
    /// far past a cut the number is taken to extend.</summary>
    public const int GlyphPx = 10;

    /// <summary>OCR boxes wobble by about a pixel; that is not motion.</summary>
    private const int JitterPx = 2;

    /// <summary>Two reads further apart than this (reference px) are two places, not one moving badge.</summary>
    private const int JumpPx = 120;

    /// <summary>Id of the search tile. Never reused for the counter, whose id "counter" is what in-flight ticks match.</summary>
    public static readonly RoiId TileId = "badge-tile";

    private readonly Func<double, bool>? _isKnown;
    private RoiSubscription _counter;
    private double _zoom = 1.0;
    private int _frameWidth = RoiScaler.ReferenceWidth, _frameHeight = RoiScaler.ReferenceHeight;
    private ReferenceFit _fit = ReferenceFit.For(RoiScaler.ReferenceWidth, RoiScaler.ReferenceHeight);
    private int _tileWidth, _tileHeight;
    private int _lostTimeoutTicks = 30;

    private NumberBox? _box;          // reference space; null = idle, use the calibrated spot
    private RoiRect? _placed;         // reference space; null = the calibrated rect is what the engine holds
    private double _vx, _vy;          // reference px per tick
    private double _lastDx, _lastDy;  // the previous read's displacement, past the wobble band
    private int _missed;              // ticks since the number was last seen
    private bool _confirmed;          // the counter itself has read the number here
    private int _blank;               // ticks since a search tile found this place without the counter reading it
    private bool _searchNext;         // the next set carries the search tile
    private bool _failureReported;
    private int _tick;
    private bool _cutReported;
    private int _cutRun;              // consecutive cut reads: one that persists cannot be repaired by moving the rect

    private readonly List<(double X, double Y, int Expires, int Ttl)> _rejected = [];

    /// <param name="counter">The calibrated counter ROI.</param>
    /// <param name="isKnown">Whether a value lands on the signature table; strong evidence for a candidate.
    /// Null means none is known.</param>
    public BadgeTracker(RoiSubscription counter, Func<double, bool>? isKnown = null)
    {
        _counter = counter;
        _isKnown = isKnown;
        SizeTile();
    }

    public TrackingState State => _box is null ? TrackingState.Idle : _missed >= LostAfterMisses ? TrackingState.Lost : TrackingState.Tracking;

    /// <summary>Where the number was last seen (the word box, reference pixels); null while idle.</summary>
    public NumberBox? LastSeen => _box;

    /// <summary>Ticks since the number was last seen.</summary>
    public int Missed => _missed;

    /// <summary>What the counter read on the last tick that carried it, decided once for everyone: what to
    /// observe, whether an edge of the rect cut it, and where the number is. Null before the first such tick.</summary>
    public CounterReading? LastReading { get; private set; }

    /// <summary>The counter's Detailed result of the last tick that carried it, and the search tile's, so the
    /// plugin need not materialise them again: null when the tick had none.</summary>
    public OcrRegionResult? LastCounterRead { get; private set; }

    public OcrRegionResult? LastTileRead { get; private set; }

    /// <summary>The captured frame's size and the engine's scan interval. The tile is sized in frame pixels,
    /// so a new size resizes it; the interval turns <see cref="LostTimeout"/> into ticks — but never fewer than
    /// <see cref="LostAfterMisses"/> + 1, so at an interval of 750 ms or more the search gives up after that many
    /// ticks (3.75 s at 750 ms, 5 s at 1 s) rather than before it has begun.</summary>
    public void Frame(int width, int height, TimeSpan? scanInterval = null)
    {
        if (scanInterval is { } interval && interval > TimeSpan.Zero)
            _lostTimeoutTicks = Math.Clamp((int)Math.Ceiling(LostTimeout / interval), LostAfterMisses + 1, 120);

        if (width <= 0 || height <= 0 || (width == _frameWidth && height == _frameHeight))
            return;

        _frameWidth = width;
        _frameHeight = height;
        _fit = ReferenceFit.For(width, height);
        SizeTile();
    }

    /// <summary>
    /// The counter rect and FOV zoom factor now in force (<see cref="StarCitizenFovZoom"/>). The remembered
    /// number zooms with them — about screen centre, by the ratio of the two factors — and the counter is
    /// placed again, so a FOV change moves the search with the badge instead of leaving it behind.
    /// </summary>
    public void Zoom(RoiSubscription zoomedCounter, double factor)
    {
        if (_box is { } box && factor != _zoom && _zoom > 0)
        {
            var ratio = factor / _zoom;
            _box = new NumberBox(
                (int)Math.Round(StarCitizenFovZoom.ZoomX(box.X, ratio)),
                (int)Math.Round(StarCitizenFovZoom.ZoomY(box.Y, ratio)),
                (int)Math.Round(box.Width * ratio),
                (int)Math.Round(box.Height * ratio));
            _vx = _vy = 0;
        }

        _counter = zoomedCounter;
        _zoom = factor;
        _placed = null;
        if (_box is not null)
            _placed = Place(_box.Value);
    }

    /// <summary>The ROI set to hold: the counter first (<c>SignaturePlugin.Counter</c> reads index 0), and, on
    /// the one tick in <see cref="SearchEveryTicks"/> that a lost number is searched for, the search tile.</summary>
    public IReadOnlyList<RoiSubscription> CurrentSet()
    {
        var counter = _placed is { } placed ? _counter with { Rect = placed } : _counter;
        if (!_searchNext || _box is null)
            return [counter];

        var (cx, cy) = DeadReckoned(_box.Value);
        return [counter, new RoiSubscription(TileId, TileAt(cx, cy), TileScale, RoiKind.Detailed)];
    }

    /// <summary>
    /// Updates what is known from this tick: the counter's own read first — which is also the position — and,
    /// when a search tile was part of the set, what it found. Whichever the tick carries is used; each result
    /// carries its own frame rect, so a tick still in flight from before the last push is as good as a fresh one.
    /// </summary>
    public void Observe(TickData tick, IPluginServices services)
    {
        LastCounterRead = null;
        LastTileRead = null;
        if (!tick.TryGetOcr(_counter.Id, out var counterRead))
        {
            if (tick.Status(_counter.Id) == RoiStatus.Failed)
                ReportFailure(tick.ErrorMessage(_counter.Id) ?? "the engine reported a ROI failure.", _counter.Id, services);
            return;
        }

        _failureReported = false;
        _tick++;
        _searchNext = false;
        LastCounterRead = counterRead;
        if (tick.Status(TileId) != RoiStatus.NotSubscribed)
        {
            if (tick.TryGetOcr(TileId, out var tileRead))
                LastTileRead = tileRead;
            else
                ReportFailure(tick.ErrorMessage(TileId) ?? "its OCR payload could not be read.", TileId, services);
        }

        var reading = CounterReader.Read(counterRead, _isKnown, _fit.Scale * _zoom, FollowedFrameBox());
        LastReading = reading;

        if (reading.IsCut)
        {
            Cut(reading, services);
            return;
        }

        if (reading.HasNumber)
        {
            Seen(ToReference(reading.Box!.Value), services);
            return;
        }

        if (_box is null)
            return;

        if (TileHit() is { } found)
        {
            // The same place again while the counter still cannot read it is not new evidence: it keeps counting
            // towards the rejection instead of starting over.
            if (!_confirmed && Math.Abs(found.CentreX - _box.Value.CentreX) <= RejectRadius && Math.Abs(found.CentreY - _box.Value.CentreY) <= RejectRadius)
                NotRead(services);
            else
                Relock(found, services);
            return;
        }

        NotRead(services);
    }

    /// <summary>The place a search tile found, if any: the candidate nearest where the number should be, clear of
    /// tile edges and of places already rejected.</summary>
    private NumberBox? TileHit()
    {
        if (LastTileRead is not { } ocr || _box is not { } followed)
            return null;

        var (ax, ay) = DeadReckoned(followed);
        NumberBox? best = null;
        var bestScore = double.MaxValue;
        foreach (var candidate in SignatureCandidates.Find(ocr, _isKnown))
        {
            if (CutByTileEdge(candidate))
                continue;

            var box = ToReference(candidate.Box);
            if (IsRejected(box))
                continue;

            var score = Math.Abs(box.CentreX - ax) + Math.Abs(box.CentreY - ay) - (candidate.Known ? 300 : 0);
            if (score < bestScore)
            {
                best = box;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>The counter read the number: this is its place, wherever it was before.</summary>
    private void Seen(NumberBox hit, IPluginServices services)
    {
        var reacquired = _box is null || _missed >= LostAfterMisses;
        var previous = _box;

        _confirmed = true;
        _blank = 0;
        _searchNext = false;
        _cutReported = false;
        _cutRun = 0;

        if (previous is { } p)
        {
            var dx = hit.X - p.X;
            var dy = hit.CentreY - p.CentreY;
            if (Math.Abs(dx) > JumpPx || Math.Abs(dy) > JumpPx)
            {
                _vx = _vy = 0;
            }
            else
            {
                // Displacement per tick since the last read (more than one tick ago after a miss); a single
                // noisy read counts for half, so it cannot throw the next placement a whole step ahead.
                var ticks = _missed + 1;
                _vx = Velocity(_vx, dx, 1, ticks, ref _lastDx);
                _vy = Velocity(_vy, dy, 2, ticks, ref _lastDy);
            }

            // Standing still: keep the old box, so a wobbling read changes nothing downstream.
            if (Math.Abs(dx) <= JitterPx && Math.Abs(dy) <= JitterPx && Math.Abs(hit.Width - p.Width) <= JitterPx)
                hit = p;
        }
        else
        {
            _vx = _vy = 0;
        }

        _box = hit;
        _missed = 0;
        EnsurePlaced();

        if (reacquired)
            services.LogVerbose($"signature badge acquired at ({hit.X},{hit.Y}) {hit.Width}x{hit.Height}");
    }

    /// <summary>
    /// The velocity after another read. A displacement within <paramref name="band"/> pixels is wobble, not motion
    /// (a read's vertical position wobbles more than its horizontal one), and so is one that does not continue
    /// the direction of the last: motion is two displacements the same way, half the old estimate and half the new,
    /// anything else lets the estimate decay. A single noisy read can therefore never swing the next placement.
    /// </summary>
    private static double Velocity(double velocity, double displacement, double band, int ticks, ref double last)
    {
        var d = Math.Abs(displacement) <= band ? 0 : displacement / ticks;
        var continues = d != 0 && Math.Sign(d) == Math.Sign(last);
        last = d;
        return continues ? Math.Clamp(0.5 * velocity + 0.5 * d, -MaxLead, MaxLead) : 0.5 * velocity;
    }

    /// <summary>A search tile found a place the number may be: the counter goes there, and has
    /// <see cref="RejectAfterBlankTicks"/> ticks to read it.</summary>
    private void Relock(NumberBox found, IPluginServices services)
    {
        _box = found;
        _vx = _vy = 0;
        _missed = 0;
        _confirmed = false;
        _blank = 0;
        _placed = null;
        _placed = Place(found);
        services.LogVerbose($"signature badge candidate at ({found.X},{found.Y}) {found.Width}x{found.Height} found by search");
    }

    private void NotRead(IPluginServices services)
    {
        _missed++;
        if (!_confirmed)
            _blank++;

        if (!_confirmed && _blank >= RejectAfterBlankTicks)
        {
            Reject(services);
            return;
        }

        if (_missed > _lostTimeoutTicks)
        {
            services.LogVerbose("signature badge not found again — counter ROI back at the calibrated position");
            Forget();
            return;
        }

        if (_missed == LostAfterMisses && _box is { } last)
            services.LogVerbose($"signature badge lost near ({last.X},{last.Y}) — searching for it");

        // Dead reckoning: if the number would by now have left the counter, follow it.
        EnsurePlaced();

        if (_missed >= LostAfterMisses && (_missed - LostAfterMisses) % SearchEveryTicks == 0)
            _searchNext = true;
    }

    private void Reject(IPluginServices services)
    {
        var box = _box!.Value;

        // An entry that has expired is still remembered, so the same decoy coming back is waited out for twice
        // as long; one not seen for a long time after its expiry is a fresh start.
        _rejected.RemoveAll(r => _tick - r.Expires > 2 * MaxRejectTicks);
        var rejected = _rejected.FindIndex(r => Math.Abs(r.X - box.CentreX) <= RejectRadius && Math.Abs(r.Y - box.CentreY) <= RejectRadius);
        var ttl = rejected >= 0 ? Math.Min(MaxRejectTicks, _rejected[rejected].Ttl * 2) : RejectTicks;
        if (rejected >= 0)
            _rejected.RemoveAt(rejected);
        _rejected.Add((box.CentreX, box.CentreY, _tick + ttl, ttl));

        services.LogVerbose($"signature badge candidate at ({box.X},{box.Y}) rejected: no number read");
        Forget();
    }

    /// <summary>Back to idle: the counter returns to the calibrated rect, nothing is searched.</summary>
    private void Forget()
    {
        _box = null;
        _placed = null;
        _vx = _vy = _lastDx = _lastDy = 0;
        _missed = 0;
        _confirmed = false;
        _blank = 0;
        _searchNext = false;
    }

    private bool IsRejected(NumberBox box)
        => _rejected.Any(r => r.Expires > _tick && Math.Abs(r.X - box.CentreX) <= RejectRadius && Math.Abs(r.Y - box.CentreY) <= RejectRadius);

    private bool CutByTileEdge(SignatureCandidate c)
    {
        var (b, t) = (c.Box, c.Tile);
        return (t.X > 0 && b.X - t.X < EdgeMarginPx)
            || (t.Y > 0 && b.Y - t.Y < EdgeMarginPx)
            || (t.Right < _frameWidth && t.Right - b.Right < EdgeMarginPx)
            || (t.Bottom < _frameHeight && t.Bottom - b.Bottom < EdgeMarginPx);
    }

    /// <summary>The number being followed, in frame pixels, for comparing a new read with: null until the counter
    /// itself has read it (a place a search tile found is not yet that).</summary>
    private NumberBox? FollowedFrameBox() => _box is { } b && _confirmed
        ? new NumberBox(
            (int)Math.Round(_fit.ToFrameX(b.X)), (int)Math.Round(_fit.ToFrameY(b.Y)),
            (int)Math.Round(b.Width * _fit.Scale), (int)Math.Round(b.Height * _fit.Scale))
        : null;

    /// <summary>
    /// An edge of the counter sliced into the number. What was read is a fragment, so it is neither a place to
    /// lock onto nor to follow: from idle, or on a place not yet confirmed, it counts as nothing read. On a number
    /// already being followed it says the rect is off by about a glyph — the box is widened by one glyph towards
    /// the cut and the counter placed again, one push, so the next read has the whole number.
    /// <para>
    /// That is bounded: at most <see cref="MaxCutRun"/> cut reads in a row, no widening past
    /// <see cref="MaxCutWidth"/> times the calibrated width, and none towards a canvas edge the rect already
    /// touches (the clamp cannot give it more margin there). A cut that outlasts the repair is not evidence of a
    /// number that is there: it counts as a read that found nothing, so the loss and the give-up still happen.
    /// </para>
    /// </summary>
    private void Cut(CounterReading reading, IPluginServices services)
    {
        if (_box is not { } box || !_confirmed)
        {
            if (_box is not null)
                NotRead(services);
            return;
        }

        var piece = ToReference(reading.Box!.Value);
        var glyph = (int)Math.Round(GlyphPx * _zoom);
        var left = Math.Min(piece.X, box.X) - (reading.Cut == CounterCut.Left ? glyph : 0);
        var widened = new NumberBox(left, piece.Y, Math.Max(box.Right, piece.Right) - left + (reading.Cut == CounterCut.Left ? 0 : glyph), piece.Height);

        var held = _placed ?? _counter.Rect;
        var atEdge = reading.Cut == CounterCut.Left ? held.X == 0 : held.X + held.Width >= RoiScaler.ReferenceWidth;
        if (++_cutRun > MaxCutRun || atEdge || widened.Width > MaxCutWidth * _counter.Rect.Width)
        {
            NotRead(services);
            return;
        }

        _box = widened;
        _missed = 0;
        EnsurePlaced();

        if (!_cutReported)
            services.LogVerbose($"signature read cut by the counter's {(reading.Cut == CounterCut.Left ? "left" : "right")} edge near ({piece.X},{piece.Y}) — counter ROI re-centred");
        _cutReported = true;
    }

    /// <summary>Moves the counter only when the number, one tick on, would be inside the margins a read needs
    /// no longer — the calibrated ones.</summary>
    private void EnsurePlaced()
    {
        if (_box is not { } box)
            return;

        var held = _placed ?? _counter.Rect;
        if (!Fits(held, Predicted(box, _missed + 1)))
            _placed = Place(Predicted(box, _missed));
    }

    /// <summary>Whether a number at <paramref name="at"/> keeps the calibrated margins inside <paramref name="rect"/>:
    /// <see cref="Rois.DigitsMarginLeft"/> / <see cref="Rois.DigitsMarginRight"/> beside it and the digits' centre
    /// <see cref="Rois.DigitsCentreY"/> below the top and <see cref="Rois.DigitsBelowCentre"/> above the bottom —
    /// measured against the rect that could be placed, which at the edge of the canvas is cut off there: a rect
    /// flush with the edge has all the margin the canvas has, and a number moving along that edge costs no push.</summary>
    private bool Fits(RoiRect rect, NumberBox at)
    {
        var left = Math.Round(Rois.DigitsMarginLeft * _zoom);
        var right = Math.Round(Rois.DigitsMarginRight * _zoom);
        var needLeft = Math.Max(0, at.X - left);
        var needRight = Math.Min(RoiScaler.ReferenceWidth, at.Right + right);
        var needTop = Math.Max(0, at.CentreY - Rois.DigitsCentreY * _zoom);
        var needBottom = Math.Min(RoiScaler.ReferenceHeight, at.CentreY + Rois.DigitsBelowCentre * _zoom);

        // A pixel of tolerance: the calibrated rect itself holds a badge sitting exactly where it was calibrated
        // with margins that round to these, and must not cost a push for half a pixel.
        return rect.X <= needLeft + 1
            && rect.X + rect.Width >= needRight - 1
            && rect.Y <= needTop + 1
            && rect.Y + rect.Height >= needBottom - 1;
    }

    /// <summary>
    /// The counter rect for a number at <paramref name="box"/> moving at the current velocity: the calibrated
    /// margins (plus a little slack) around it, widened in the direction of motion by <see cref="HoldTicks"/>
    /// ticks of travel, and never smaller than the calibrated rect.
    /// </summary>
    private RoiRect Place(NumberBox box)
    {
        var z = _zoom;
        var calibrated = _counter.Rect;
        double left = Math.Round(Rois.DigitsMarginLeft * z), right = Math.Round(Rois.DigitsMarginRight * z);
        double above = Rois.DigitsCentreY * z, below = Rois.DigitsBelowCentre * z;

        // A velocity under 1.5 px a tick is estimate noise, not a direction worth growing the crop for.
        double vx = Math.Abs(_vx) < 1.5 ? 0 : _vx, vy = Math.Abs(_vy) < 1.5 ? 0 : _vy;
        double exL = Math.Clamp(-vx * HoldTicks, 0, MaxExtraX * z), exR = Math.Clamp(vx * HoldTicks, 0, MaxExtraX * z);
        double exU = Math.Clamp(-vy * HoldTicks, 0, MaxExtraY * z), exD = Math.Clamp(vy * HoldTicks, 0, MaxExtraY * z);

        // Keep the area within MaxAreaFactor of the calibrated rect by giving back headroom, not margin.
        var budget = MaxAreaFactor * calibrated.Width * calibrated.Height;
        for (var i = 0; i < 20; i++)
        {
            var w = box.Width + left + right + 2 * SlackX + exL + exR;
            var h = above + below + 2 * SlackY + exU + exD;
            if (w * h <= budget || (exL + exR + exU + exD) < 1)
                break;
            exL *= 0.85; exR *= 0.85; exU *= 0.85; exD *= 0.85;
        }

        var x = box.X - left - SlackX - exL;
        var width = Math.Max(calibrated.Width, box.Width + left + right + 2 * SlackX + exL + exR);
        var y = box.CentreY - above - SlackY - exU;
        var height = Math.Max(calibrated.Height, above + below + 2 * SlackY + exU + exD);

        width = Math.Min(width, RoiScaler.ReferenceWidth);
        height = Math.Min(height, RoiScaler.ReferenceHeight);
        return new RoiRect(
            (uint)Math.Clamp(Math.Round(x), 0, RoiScaler.ReferenceWidth - Math.Round(width)),
            (uint)Math.Clamp(Math.Round(y), 0, RoiScaler.ReferenceHeight - Math.Round(height)),
            (uint)Math.Round(width), (uint)Math.Round(height));
    }

    /// <summary>The box <paramref name="ticks"/> ticks on (at most 20), at the current velocity.</summary>
    private NumberBox Predicted(NumberBox box, int ticks)
    {
        ticks = Math.Min(ticks, 20);
        return box with { X = box.X + (int)Math.Round(_vx * ticks), Y = box.Y + (int)Math.Round(_vy * ticks) };
    }

    /// <summary>Where the number should be now, centred: the last read moved on by its velocity for every tick since.</summary>
    private (double X, double Y) DeadReckoned(NumberBox box)
    {
        var ticks = Math.Min(_missed + 1, 20);
        return (box.CentreX + _vx * ticks, box.CentreY + _vy * ticks);
    }

    private void ReportFailure(string reason, RoiId id, IPluginServices services)
    {
        if (_failureReported)
            return;

        _failureReported = true;
        services.Log($"SignaturePlugin: ROI '{id}' failed ({reason}) — the counter ROI cannot follow the badge until it reads.");
    }

    private NumberBox ToReference(NumberBox frame) => new(
        (int)Math.Round(_fit.ToReferenceX(frame.X)),
        (int)Math.Round(_fit.ToReferenceY(frame.Y)),
        (int)Math.Round(frame.Width / _fit.Scale),
        (int)Math.Round(frame.Height / _fit.Scale));

    private void SizeTile()
    {
        _tileWidth = Math.Min(RoiScaler.ReferenceWidth, (int)Math.Floor(TileFrameWidth / _fit.Scale));
        _tileHeight = Math.Min(RoiScaler.ReferenceHeight, (int)Math.Floor(TileFrameHeight / _fit.Scale));
    }

    private RoiRect TileAt(double centreX, double centreY) => new(
        (uint)Math.Round(Math.Clamp(centreX - _tileWidth / 2.0, 0, RoiScaler.ReferenceWidth - _tileWidth)),
        (uint)Math.Round(Math.Clamp(centreY - _tileHeight / 2.0, 0, RoiScaler.ReferenceHeight - _tileHeight)),
        (uint)_tileWidth, (uint)_tileHeight);
}
