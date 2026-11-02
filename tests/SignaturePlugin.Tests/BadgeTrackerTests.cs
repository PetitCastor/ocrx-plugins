using Ocrx.Contracts;
using Ocrx.Sdk;
using Ocrx.Sdk.Testing;
using Xunit;

namespace SignaturePlugin.Tests;

/// <summary>
/// <see cref="BadgeTracker"/> driven through an <see cref="OcrScene"/>: the counter ROI is read for whatever
/// words lie in its rect, so lock, placement, motion, loss, the occasional search and the verification of a
/// found place are exercised end to end. The scene's text is either invented (to place a number exactly) or the
/// recorded OCR of four real cockpit captures (<see cref="CockpitFrame"/>). The budget of all this — OCR area,
/// pushes, tiles — is in <see cref="TrackingBudgetTests"/>.
/// </summary>
public class BadgeTrackerTests
{
    private static NumberBox Number(int x, int y, int width = 56, int height = 16) => new(x, y, width, height);

    /// <summary>"2,000" exactly where the calibrated rect has it: left margin 9, centre 17.5 below the top.</summary>
    private static readonly NumberBox Calibrated2000 = Number(1273, 464, 46, 15);

    [Fact]
    public void Before_anything_is_read_the_set_is_the_calibrated_counter_alone()
    {
        var rig = new TrackerRig();

        Assert.Equal([Rois.Counter], rig.Tracker.CurrentSet());
        Assert.Equal(RoiKind.Detailed, Rois.Counter.Kind);
        Assert.Equal(TrackingState.Idle, rig.Tracker.State);
        Assert.Null(rig.Tracker.LastSeen);
    }

    /// <summary>The number is where the calibration has it: the counter already holds it with the calibrated
    /// margins, so the lock costs nothing — no push.</summary>
    [Fact]
    public void A_number_at_the_calibrated_spot_is_locked_without_moving_the_counter()
    {
        var rig = new TrackerRig(new OcrScene().Add("2,000", Calibrated2000));

        rig.Run(5);

        Assert.Equal(TrackingState.Tracking, rig.Tracker.State);
        Assert.Equal(Calibrated2000, rig.Tracker.LastSeen);
        Assert.Equal(0, rig.Pushes);
        Assert.Equal(Rois.Counter, rig.Tracker.CurrentSet()[0]);
        Assert.Single(rig.Services.VerboseLogs, l => l.Contains("acquired at (1273,464)"));
    }

    /// <summary>"17,200" is wider than "2,000": the calibrated rect would leave 5 px after it, not 15. One push
    /// widens it, and it stays put after that.</summary>
    [Fact]
    public void A_wider_number_widens_the_counter_once_and_then_leaves_it_alone()
    {
        var rig = new TrackerRig(new OcrScene().Add("17,200", Number(1273, 464, 56, 15)));

        rig.Run(30);

        Assert.Equal(1, rig.Pushes);
        var rect = rig.Counter;
        Assert.True(rect.X + rect.Width - (1273 + 56) >= Rois.DigitsMarginRight, "right margin kept");
        Assert.True(1273 - rect.X >= Rois.DigitsMarginLeft, "left margin kept");
        Assert.Equal(Rois.Counter.Scale, rig.Tracker.CurrentSet()[0].Scale);
        Assert.Equal(Rois.Counter.Id, rig.Tracker.CurrentSet()[0].Id);
        Assert.Equal(Rois.Counter.Kind, rig.Tracker.CurrentSet()[0].Kind);
    }

    /// <summary>The placed rect never has less margin than the calibrated one, whatever the width: a number is read,
    /// then a wider one takes its place (a six-digit total after a five-digit one) and the counter widens to keep
    /// its right margin instead of cutting the last digit.</summary>
    [Theory]
    [InlineData(46)]  // "2,000"
    [InlineData(56)]  // "17,200"
    [InlineData(68)]  // "111,700"
    [InlineData(80)]
    public void Placement_keeps_the_calibrated_margins_and_widens_with_the_word(int wordWidth)
    {
        var rig = new TrackerRig(new OcrScene().Add("2,000", Number(1273, 464, 46, 15)));
        rig.Run(3);

        rig.Scene.Remove(_ => true).Add("111,700", Number(1273, 464, wordWidth, 15));
        rig.Run(6);

        var rect = rig.Counter;
        Assert.True(1273 - rect.X >= Rois.DigitsMarginLeft);
        Assert.True(rect.X + rect.Width - (1273 + wordWidth) >= Rois.DigitsMarginRight);
        Assert.True(471.5 - rect.Y >= Rois.DigitsCentreY);
        Assert.True(rect.Y + rect.Height - 471.5 >= Rois.DigitsBelowCentre);
        Assert.True(rect.Width >= Rois.Counter.Rect.Width && rect.Height >= Rois.Counter.Rect.Height);
    }

    /// <summary>The real thing: each of four cockpit frames, cold start, with their MFDs, "READY TO SCAN", "15.9km",
    /// "90º" and the power-MFD "4.314" all in the scene. The badge is at the calibrated spot; the tracker locks it
    /// and nothing else.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void On_a_real_cockpit_frame_the_counter_locks_the_badge_and_only_the_badge(int frameIndex)
    {
        // The frame's decoys as recorded, and the badge as the counter reads it at scale 6.
        var scene = OcrScene.From(CockpitFrame.Load()[frameIndex]).Remove(w => CockpitFrame.IsBadge(w.Box));
        var (text, box) = CockpitFrame.RealBadges[frameIndex];
        var rig = new TrackerRig(scene.Add(text, box));

        for (var tick = 0; tick < 40; tick++)
        {
            rig.Step();
            if (rig.Tracker.LastSeen is { } seen)
                Assert.True(CockpitFrame.IsBadge(seen), $"tick {tick}: locked on {seen}, a decoy");
        }

        Assert.Equal(TrackingState.Tracking, rig.Tracker.State);
        Assert.Equal(1, rig.Acquired);
        Assert.Equal(0, rig.Rejected);
    }

    /// <summary>The same frames with the badge taken off: nothing on the HUD may lock, however long it runs, and
    /// idle costs nothing — the calibrated counter, no tiles, no pushes.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void On_a_real_cockpit_frame_without_the_badge_nothing_is_locked_and_nothing_is_spent(int frameIndex)
    {
        var scene = OcrScene.From(CockpitFrame.Load()[frameIndex]).Remove(w => CockpitFrame.IsBadge(w.Box));
        var rig = new TrackerRig(scene);

        rig.Run(300);

        Assert.Equal(TrackingState.Idle, rig.Tracker.State);
        Assert.Null(rig.Tracker.LastSeen);
        Assert.Equal(0, rig.Pushes);
        Assert.Equal(0, rig.TileTicks);
        Assert.Equal(300 * TrackingStats.BaselineArea, rig.Area);
    }

    /// <summary>A number is read in the calibrated rect, then walks left at the measured 10 px a tick. The counter
    /// reads it on every tick and is never lost.</summary>
    [Fact]
    public void A_number_walking_left_is_read_on_every_tick_and_never_lost()
    {
        var rig = new TrackerRig();
        var x = 1273;
        var unread = 0;
        for (var tick = 0; tick < 75; tick++)
        {
            if (tick >= 15) x -= 10;
            rig.Scene.Remove(_ => true).Add("17,200", Number(x, 464, 56, 15));
            rig.Step();
            if (rig.Tracker.Missed != 0)
                unread++;
        }

        Assert.Equal(TrackingState.Tracking, rig.Tracker.State);
        Assert.InRange(unread, 0, 2); // the first tick of motion, before the rect has headroom, may miss
        Assert.Equal(1, rig.Acquired);
    }

    /// <summary>Up and left together, then a stop, then back: the velocity changes sign and the counter follows.</summary>
    [Fact]
    public void Reversals_and_stops_are_followed()
    {
        var rig = new TrackerRig();
        int x = 1273, y = 464;
        var unread = 0;
        for (var tick = 0; tick < 130; tick++)
        {
            if (tick is >= 15 and < 40) { x -= 10; y -= 4; }
            else if (tick is >= 55 and < 80) { x += 10; y += 4; }
            rig.Scene.Remove(_ => true).Add("17,200", Number(x, y, 56, 15));
            rig.Step();
            if (rig.Tracker.Missed != 0) unread++;
        }

        Assert.InRange(unread, 0, 6);
        Assert.Equal(TrackingState.Tracking, rig.Tracker.State);
    }

    /// <summary>OCR word boxes wobble by a few pixels between reads; that must not rebuild the set or throw the placement.</summary>
    [Fact]
    public void A_standing_number_with_wobbling_boxes_leaves_the_set_alone()
    {
        var rig = new TrackerRig(new OcrScene().Add("17,200", Number(1273, 464, 56, 15)));
        rig.Run(3);
        var pushes = rig.Pushes;
        var settled = rig.Tracker.CurrentSet();

        for (var i = 0; i < 40; i++)
        {
            rig.Scene.Remove(_ => true).Add("17,200", Number(1273 + i % 3 - 1, 464 + (i % 4 - 1) * 2, 56 + i % 2, 15));
            rig.Step();
        }

        Assert.Equal(pushes, rig.Pushes);
        Assert.True(settled.SequenceEqual(rig.Tracker.CurrentSet()));
    }

    /// <summary>One missed read in a few is OCR, not loss: nothing is searched for and nothing is pushed.</summary>
    [Fact]
    public void A_short_run_of_unread_ticks_is_not_a_loss()
    {
        var number = Calibrated2000;
        var rig = new TrackerRig(new OcrScene().Add("2,000", number));
        rig.Run(5);

        rig.Scene.Remove(_ => true);
        rig.Run(BadgeTracker.LostAfterMisses - 1);
        Assert.Equal(TrackingState.Tracking, rig.Tracker.State);
        Assert.Equal(0, rig.TileTicks);
        Assert.Equal(0, rig.Pushes);

        rig.Scene.Add("2,000", number);
        rig.Step();
        Assert.Equal(0, rig.Tracker.Missed);
    }

    /// <summary>A number that went away for good: lost after a few ticks, one tile every few ticks, and at
    /// the timeout the counter is back at the calibrated rect, pushed once — after which it costs nothing.</summary>
    [Fact]
    public void A_vanished_number_is_searched_for_rarely_then_given_up_on()
    {
        var rig = new TrackerRig(new OcrScene().Add("17,200", Number(1273, 464, 56, 15)));
        rig.Run(15);
        var placed = rig.Counter;
        Assert.NotEqual(Rois.Counter.Rect, placed);
        rig.Scene.Remove(_ => true);

        var tileTicks = new List<int>();
        var pushesBefore = rig.Pushes;
        var timeoutTicks = (int)Math.Ceiling(BadgeTracker.LostTimeout / TimeSpan.FromMilliseconds(100));
        for (var tick = 1; tick <= timeoutTicks + 2; tick++)
        {
            rig.Step();
            if (rig.LastHadTile)
                tileTicks.Add(tick);
            if (tick < BadgeTracker.LostAfterMisses)
                Assert.Equal(TrackingState.Tracking, rig.Tracker.State);
        }

        // Never more than one tile in any window of SearchEveryTicks ticks, and none before the loss.
        Assert.NotEmpty(tileTicks);
        Assert.All(tileTicks, t => Assert.True(t > BadgeTracker.LostAfterMisses));
        for (var i = 1; i < tileTicks.Count; i++)
            Assert.True(tileTicks[i] - tileTicks[i - 1] >= BadgeTracker.SearchEveryTicks, $"tiles at {string.Join(",", tileTicks)}");
        Assert.Contains(rig.Services.VerboseLogs, l => l.Contains("lost near"));
        Assert.Contains(rig.Services.VerboseLogs, l => l.Contains("calibrated position"));

        // Gave up: idle, calibrated, and from here on nothing is pushed or searched.
        Assert.Equal(TrackingState.Idle, rig.Tracker.State);
        Assert.Equal([Rois.Counter], rig.Tracker.CurrentSet());
        var pushes = rig.Pushes;
        var tiles = rig.TileTicks;
        rig.Run(100);
        Assert.Equal(pushes, rig.Pushes);
        Assert.Equal(tiles, rig.TileTicks);

        // Tiles come and go (two pushes each), plus the one that puts the counter back.
        Assert.InRange(pushes - pushesBefore, 1, 2 * tileTicks.Count + 2);
    }

    /// <summary>The timeout is three seconds of the engine's scan interval, not of a fixed number of ticks — but
    /// never fewer than <see cref="BadgeTracker.LostAfterMisses"/> + 1, so at 750 ms or more it is that many ticks.</summary>
    [Theory]
    [InlineData(100, 30)]
    [InlineData(250, 12)]
    [InlineData(500, 6)]
    [InlineData(1000, 5)]
    public void The_give_up_time_follows_the_scan_interval(int intervalMs, int expectedTicks)
    {
        var rig = new TrackerRig(new OcrScene().Add("2,000", Calibrated2000), scanInterval: TimeSpan.FromMilliseconds(intervalMs));
        rig.Run(5);
        rig.Scene.Remove(_ => true);

        var ticksToIdle = 0;
        while (rig.Tracker.State != TrackingState.Idle && ticksToIdle < 200)
        {
            rig.Step();
            ticksToIdle++;
        }

        Assert.InRange(ticksToIdle, expectedTicks, expectedTicks + 2);
    }

    /// <summary>The number comes back to where the counter is: it is read again without any search.</summary>
    [Fact]
    public void A_number_that_returns_to_the_counter_is_read_again()
    {
        var rig = new TrackerRig(new OcrScene().Add("2,000", Calibrated2000));
        rig.Run(5);
        rig.Scene.Remove(_ => true);
        rig.Run(12);
        Assert.Equal(TrackingState.Lost, rig.Tracker.State);

        rig.Scene.Add("2,000", Calibrated2000);
        rig.Step();

        Assert.Equal(TrackingState.Tracking, rig.Tracker.State);
        Assert.Equal(2, rig.Acquired);
    }

    /// <summary>A flick of the camera: the number is 150 px away from the counter. A search tile finds it, the counter
    /// goes there and reads it.</summary>
    [Fact]
    public void A_number_that_jumps_away_is_found_by_a_search_tile()
    {
        var rig = new TrackerRig(new OcrScene().Add("2,000", Calibrated2000));
        rig.Run(10);

        rig.Scene.Remove(_ => true).Add("2,000", Number(1273 - 150, 464 - 70, 46, 15));
        rig.Run(40);

        Assert.Equal(TrackingState.Tracking, rig.Tracker.State);
        Assert.Equal(1, rig.Searched);
        Assert.Equal(0, rig.Tracker.Missed);
        var seen = rig.Tracker.LastSeen!.Value;
        Assert.Equal(1123, seen.X);
        Assert.Equal(394, seen.Y);
    }

    /// <summary>A number cut by the tile's edge reads as a fragment ("7,200" for "117,200"), which must not be
    /// taken for a place.</summary>
    [Fact]
    public void A_number_cut_by_the_search_tile_edge_is_not_taken_for_a_place()
    {
        var rig = new TrackerRig(new OcrScene().Add("2,000", Calibrated2000));
        rig.Run(10);
        rig.Scene.Remove(_ => true);
        rig.Run(BadgeTracker.LostAfterMisses); // the tile is in the set for the next scan
        Assert.Equal(2, rig.Held.Count);
        var tile = rig.Held[1].Rect;

        // Straddling the tile's left edge: OCR sees a valid-looking "7,200" against the edge.
        rig.Scene.Add("117,200", Number((int)tile.X - 20, (int)tile.Y + 100, 68));
        rig.Step();

        Assert.Equal(0, rig.Searched);
    }

    /// <summary>A word a search tile finds that looks like a signature and is not one. The counter moved onto it reads nothing, so the place is
    /// thrown away — and ignored the next time.</summary>
    [Fact]
    public void A_decoy_found_by_a_search_tile_is_rejected_when_the_counter_cannot_read_it()
    {
        var decoy = Number(1150, 420, 56, 16);
        var scene = new OcrScene().Add("2,000", Calibrated2000);
        // OCR reads the decoy in a search tile but not at the counter's scale 6, which is how it looks like a number and is not one.
        scene.Blind = (rect, scale) => scale >= 5 && rect.X < 1400 && rect.X + rect.Width > 1090 && rect.Y < 440 && rect.Y + rect.Height > 380;
        var rig = new TrackerRig(scene);
        rig.Run(10);

        scene.Remove(_ => true).Add("11,200", decoy);
        rig.Run(60);

        Assert.Equal(1, rig.Searched);
        Assert.Equal(1, rig.Rejected);
        Assert.Contains(rig.Services.VerboseLogs, l => l.Contains("rejected: no number read"));
        Assert.Equal(TrackingState.Idle, rig.Tracker.State);
        Assert.Equal([Rois.Counter], rig.Tracker.CurrentSet());
    }

    /// <summary>A rejected place stays ignored: the next time the number is lost and the tile sees the same decoy,
    /// it is not locked again.</summary>
    [Fact]
    public void A_rejected_place_is_ignored_the_next_time()
    {
        var decoy = Number(1150, 420, 56, 16);
        var scene = new OcrScene().Add("2,000", Calibrated2000);
        scene.Blind = (rect, scale) => scale >= 5 && rect.X < 1400 && rect.X + rect.Width > 1090 && rect.Y < 440 && rect.Y + rect.Height > 380;
        var rig = new TrackerRig(scene);
        rig.Run(10);
        scene.Remove(_ => true).Add("11,200", decoy);
        rig.Run(60);
        Assert.Equal(1, rig.Rejected);

        // The real number returns, then goes again with the decoy still on screen.
        scene.Add("2,000", Calibrated2000);
        rig.Run(10);
        Assert.Equal(TrackingState.Tracking, rig.Tracker.State);
        scene.Remove(w => w.Text == "2,000");
        rig.Run(60);

        Assert.Equal(1, rig.Searched);   // still just the first: the second time the decoy was passed over
        Assert.Equal(1, rig.Rejected);
    }

    [Fact]
    public void A_fov_change_zooms_the_remembered_number_and_the_margins()
    {
        var rig = new TrackerRig(new OcrScene().Add("17,200", Number(1273, 464, 56, 15)));
        rig.Run(4);

        var factor = StarCitizenFovZoom.Factor(77.5521);
        var zoomed = StarCitizenFovZoom.Apply(Rois.Counter, factor);
        rig.Tracker.Zoom(zoomed, factor);

        var seen = rig.Tracker.LastSeen!.Value;
        Assert.Equal((int)Math.Round(1280 + (1273 - 1280) * factor), seen.X);
        Assert.Equal((int)Math.Round(720 + (464 - 720) * factor), seen.Y);
        Assert.Equal((int)Math.Round(56 * factor), seen.Width);

        var placed = rig.Tracker.CurrentSet()[0];
        Assert.Equal(zoomed.Scale, placed.Scale);
        Assert.True(placed.Rect.Width >= zoomed.Rect.Width && placed.Rect.Height >= zoomed.Rect.Height);
        Assert.True(seen.X - placed.Rect.X >= Math.Round(Rois.DigitsMarginLeft * factor));
    }

    [Fact]
    public void An_unseen_number_under_fov_zoom_leaves_the_zoomed_calibrated_rect()
    {
        var rig = new TrackerRig();
        var factor = StarCitizenFovZoom.Factor(83.9863);
        var zoomed = StarCitizenFovZoom.Apply(Rois.Counter, factor);

        rig.Tracker.Zoom(zoomed, factor);

        Assert.Equal([zoomed], rig.Tracker.CurrentSet());
    }

    /// <summary>At 4K reference and frame differ: the position must come back in reference pixels and the rect go out in them.</summary>
    [Fact]
    public void At_4k_the_number_is_found_in_frame_pixels_and_reported_in_reference_ones()
    {
        // The calibrated rect (1264,454 70x44) is frame (1896,681 105x66); the number sits at reference (1273,464).
        var rig = new TrackerRig(new OcrScene(3840, 2160).Add("2,000", new NumberBox(1910, 696, 69, 22)));

        rig.Run(3);

        var seen = Assert.NotNull(rig.Tracker.LastSeen);
        Assert.InRange(seen.X, 1272, 1274);
        Assert.InRange(seen.Y, 463, 465);
        Assert.InRange(seen.Width, 45, 47);
    }

    /// <summary>The rect never leaves the reference canvas: a negative or past-the-edge origin is a per-ROI error.</summary>
    [Fact]
    public void A_number_at_the_screen_edge_places_the_counter_inside_the_canvas()
    {
        var rig = new TrackerRig(new OcrScene().Add("2,000", Calibrated2000));
        rig.Run(5);
        rig.Scene.Remove(_ => true).Add("2,000", Number(2500, 700, 46, 15));
        rig.Run(40);

        var rect = rig.Counter;
        Assert.True(rect.X + rect.Width <= 2560 && rect.Y + rect.Height <= 1440);
    }

    /// <summary>A ROI the engine refuses is said once, not every tick.</summary>
    [Fact]
    public void A_failing_counter_is_reported_once()
    {
        var tracker = new BadgeTracker(Rois.Counter);
        var services = new FakePluginServices();

        for (var i = 0; i < 5; i++)
            tracker.Observe(new TickDataBuilder().Errored("counter", "ocr failed").Build(), services);

        var line = Assert.Single(services.Logs);
        Assert.Contains("counter", line);
        Assert.Contains("ocr failed", line);
        Assert.Equal(TrackingState.Idle, tracker.State);
    }

    /// <summary>OCR drops the comma sometimes ("3600"): there is no candidate, but the read parses, and that is a position.</summary>
    [Fact]
    public void A_read_the_parser_accepts_but_the_candidate_filter_does_not_still_gives_a_position()
    {
        var rig = new TrackerRig(new OcrScene().Add("3600", Number(1273, 464, 40, 15)));

        rig.Run(3);

        Assert.Equal(TrackingState.Tracking, rig.Tracker.State);
        Assert.Equal(1273, rig.Tracker.LastSeen!.Value.X);
    }

    // ---- the live failures of 2026-11-01 ----

    /// <summary>The 18 px "40" the tracker was locked on, and the other fragments seen ("140", ", 240", "240"): never a lock.</summary>
    [Theory]
    [InlineData("40", 18, 12)]
    [InlineData("140", 27, 14)]
    [InlineData(", 240", 30, 14)]
    [InlineData("240", 27, 14)]
    [InlineData("1,200", 14, 12)]
    public void A_fragment_in_the_counter_is_never_acquired(string text, int width, int height)
    {
        var rig = new TrackerRig(new OcrScene().Add(text, Number(1277, 451, width, height)));

        rig.Run(30);

        Assert.Equal(TrackingState.Idle, rig.Tracker.State);
        Assert.Equal(0, rig.Acquired);
        Assert.Equal(0, rig.Pushes);
    }

    /// <summary>
    /// The false capture. "17,140" is being followed; the counter's left edge slices its first digit and OCR reads
    /// "7,140" — a valid number, box against the edge. It is cut: no text, no lock change, and the counter is moved
    /// one glyph left so the next read has the whole number.
    /// </summary>
    [Fact]
    public void A_number_cut_on_the_left_is_not_a_reading_and_the_counter_is_recentred()
    {
        var rig = new TrackerRig(new OcrScene().Add("17,140", Number(1273, 464, 56, 15)));
        rig.Run(6);
        Assert.Equal("17,140", rig.Tracker.LastReading!.Text);
        var held = rig.Counter;
        var pushes = rig.Pushes;

        // The number is 9 px further left than the rect expects: its first digit is outside.
        rig.Scene.Remove(_ => true).Add("17,140", Number((int)held.X - 9, 464, 56, 15));
        rig.Step();

        var cut = rig.Tracker.LastReading!;
        Assert.Equal(CounterCut.Left, cut.Cut);
        Assert.Equal("", cut.Text);
        Assert.Equal("7,140", cut.Raw);
        Assert.Equal(TrackingState.Tracking, rig.Tracker.State);
        Assert.Equal(pushes + 1, rig.Pushes);
        Assert.True(rig.Counter.X < held.X, "the counter moves towards the cut");

        // The re-centred rect has the whole number, and it reads again.
        rig.Step();
        Assert.Equal("17,140", rig.Tracker.LastReading!.Text);
        Assert.False(rig.Tracker.LastReading.IsCut);
        Assert.Equal(pushes + 1, rig.Pushes);
        Assert.Contains(rig.Services.VerboseLogs, l => l.Contains("cut by the counter's left edge"));
    }

    [Fact]
    public void A_number_cut_on_the_right_widens_the_counter()
    {
        var rig = new TrackerRig(new OcrScene().Add("17,140", Number(1273, 464, 56, 15)));
        rig.Run(6);
        var held = rig.Counter;

        // The number grew past the rect (a longer one, from the same place): it runs out beyond the right edge.
        rig.Scene.Remove(_ => true).Add("17,140", Number(1273, 464, (int)(held.X + held.Width) + 20 - 1273, 15));
        rig.Step();

        Assert.Equal(CounterCut.Right, rig.Tracker.LastReading!.Cut);
        Assert.True(rig.Counter.X + rig.Counter.Width > held.X + held.Width, "the rect grew on the right");
    }

    /// <summary>A cut on a place a search tile only suggested is not evidence of anything: it counts as a read that found nothing.</summary>
    [Fact]
    public void A_cut_read_never_acquires_from_idle()
    {
        var rig = new TrackerRig(new OcrScene().Add("17,140", Number(1255, 464, 56, 15))); // first digit outside the calibrated rect

        rig.Run(20);

        Assert.Equal(TrackingState.Idle, rig.Tracker.State);
        Assert.Equal(0, rig.Acquired);
        Assert.Equal(0, rig.Pushes);
    }

    /// <summary>
    /// The distance line under the badge, and every way OCR wrote it. Alone in the counter it is never acquired,
    /// followed, or the cause of a push; the comma variant ("15,9km") is the dangerous one.
    /// </summary>
    [Theory]
    [InlineData("15.9km")]
    [InlineData("15,9km")]
    [InlineData("15.9krn")]
    [InlineData("15.9kin")]
    [InlineData("15.9kfrn")]
    [InlineData("15.9gkm")]
    [InlineData("15.9gkrn")]
    [InlineData("15.9krTl")]
    [InlineData("IS.9krn")]
    [InlineData("5.9km")]
    [InlineData("fknmYD100")]
    public void A_distance_line_in_the_counter_is_never_acquired(string text)
    {
        var rig = new TrackerRig(new OcrScene().Add(text, Number(1273, 491, 50, 13)));

        rig.Run(40);

        Assert.Equal(TrackingState.Idle, rig.Tracker.State);
        Assert.Equal(0, rig.Acquired);
        Assert.Equal(0, rig.Pushes);
        Assert.Equal("", rig.Tracker.LastReading!.Text);
    }

    /// <summary>The number's line and the distance line together: the number is followed, the line is ignored,
    /// and when the number goes the line does not take its place.</summary>
    [Fact]
    public void The_distance_line_does_not_take_the_place_of_a_number_that_goes()
    {
        var scene = new OcrScene().Add("17,200", Number(1273, 463, 56, 15)).Add("15,9km", Number(1273, 491, 50, 13));
        var rig = new TrackerRig(scene);
        rig.Run(10);
        var seen = rig.Tracker.LastSeen!.Value;
        Assert.Equal(463, seen.Y);

        scene.Remove(w => w.Text == "17,200");
        rig.Run(20);

        // Lost, still remembering the number's place; the line 28 px below did not take it over.
        Assert.Equal(TrackingState.Lost, rig.Tracker.State);
        Assert.Equal(463, rig.Tracker.LastSeen!.Value.Y);
        Assert.Equal(1, rig.Acquired);

        rig.Run(40);
        Assert.Equal(TrackingState.Idle, rig.Tracker.State);
        Assert.Equal(1, rig.Acquired);
    }

    /// <summary>Going down 6 px a tick, the extra margin below the number takes in the distance line; the number is
    /// still the thing followed.</summary>
    [Fact]
    public void A_number_moving_down_with_its_distance_line_is_followed_not_the_line()
    {
        var rig = new TrackerRig();
        var y = 463;
        for (var tick = 0; tick < 80; tick++)
        {
            if (tick >= 15) y += 6;
            rig.Scene.Remove(_ => true).Add("17,200", Number(1273, y, 56, 15)).Add("15,9km", Number(1273, y + 28, 50, 13));
            rig.Step();
            if (tick >= 20)
                Assert.InRange(rig.Tracker.LastSeen!.Value.Y, y - 12, y + 3); // never the line, 28 px below
        }

        Assert.Equal(1, rig.Acquired);
    }

    // ---- the edge of the canvas ----

    /// <summary>Walks a number left at 10 px a tick until its left edge is at <paramref name="x"/>.</summary>
    private static void WalkLeftTo(TrackerRig rig, int x)
    {
        var at = 1273;
        for (var tick = 0; tick < 15; tick++)
        {
            rig.Scene.Remove(_ => true).Add("17,140", Number(at, 464, 56, 15));
            rig.Step();
        }

        while (at > x)
        {
            at = Math.Max(x, at - 10);
            rig.Scene.Remove(_ => true).Add("17,140", Number(at, 464, 56, 15));
            rig.Step();
        }
    }

    /// <summary>
    /// A number moving along the left edge of the canvas: the rect is flush with the edge and cannot have the full
    /// margin there, so "the margin is short" must not be a reason to move it on every read. Pushes follow the
    /// motion along the edge at the usual rate, not one per tick.
    /// </summary>
    [Fact]
    public void A_number_moving_along_the_canvas_edge_pushes_at_the_usual_rate()
    {
        var rig = new TrackerRig();
        WalkLeftTo(rig, 5);
        Assert.Equal(0u, rig.Counter.X); // flush with the edge
        Assert.Equal(TrackingState.Tracking, rig.Tracker.State);

        var pushes = rig.Pushes;
        var y = 464;
        for (var tick = 0; tick < 60; tick++)
        {
            y += 6;
            rig.Scene.Remove(_ => true).Add("17,140", Number(5, y, 56, 15));
            rig.Step();
        }

        Assert.Equal(TrackingState.Tracking, rig.Tracker.State);
        Assert.InRange((rig.Pushes - pushes) / 6.0, 0, 2.6); // 60 ticks of 100 ms
    }

    /// <summary>A number standing against the edge, rect flush with it: nothing is pushed.</summary>
    [Fact]
    public void A_number_standing_at_the_canvas_edge_costs_no_pushes()
    {
        var rig = new TrackerRig();
        WalkLeftTo(rig, 5);
        var pushes = rig.Pushes;

        rig.Run(60);

        Assert.Equal(pushes, rig.Pushes);
        Assert.Equal(TrackingState.Tracking, rig.Tracker.State);
    }

    /// <summary>
    /// A cut that persists where the rect cannot be moved any further — the number runs off the canvas edge — is
    /// not repaired by widening the box again and again, and not evidence the number is there: the box stays
    /// bounded, the run of cuts counts as reads that found nothing, and the loss and the give-up still come.
    /// </summary>
    [Fact]
    public void A_cut_that_persists_at_the_canvas_edge_is_bounded_and_ends_in_give_up()
    {
        var rig = new TrackerRig();
        WalkLeftTo(rig, 5);
        var widest = 0;

        for (var tick = 0; tick < 80; tick++)
        {
            var rect = rig.Counter;
            rig.Scene.Remove(_ => true).Add("17,140", Number((int)rect.X - 9, 464, 56, 15)); // first digit outside, whatever the rect does
            rig.Step();
            if (rig.Tracker.LastSeen is { } seen)
                widest = Math.Max(widest, seen.Width);
        }

        Assert.True(widest <= BadgeTracker.MaxCutWidth * Rois.Counter.Rect.Width, $"the box grew to {widest}");
        Assert.Equal(TrackingState.Idle, rig.Tracker.State);
        Assert.Equal([Rois.Counter], rig.Tracker.CurrentSet());
        Assert.Contains(rig.Services.VerboseLogs, l => l.Contains("calibrated position"));
    }

    /// <summary>The widening is also capped in number: a cut that repeats in the middle of the canvas is given up on too.</summary>
    [Fact]
    public void A_cut_that_repeats_mid_canvas_is_repaired_a_few_times_then_counted_as_loss()
    {
        var rig = new TrackerRig(new OcrScene().Add("17,140", Number(1273, 464, 56, 15)));
        rig.Run(8);

        for (var tick = 0; tick < 60; tick++)
        {
            // A badge that keeps sliding out from under the edge as fast as the rect is moved.
            var rect = rig.Counter;
            rig.Scene.Remove(_ => true).Add("17,140", Number((int)rect.X - 9, 464, 56, 15));
            rig.Step();
        }

        Assert.NotEqual(TrackingState.Tracking, rig.Tracker.State);
    }
}
