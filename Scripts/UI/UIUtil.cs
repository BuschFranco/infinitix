namespace ShooterLoop;

using Godot;

public static class UIUtil
{
    public static StyleBoxFlat CreatePanelStyle(Color borderColor)
    {
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.043f, 0.024f, 0.078f, 0.94f);
        style.BorderColor = new Color(borderColor, 0.85f);
        style.SetBorderWidthAll(3);
        // Square, like every other edge in the game. This one line is the whole art direction for
        // five different panels -- shop, reward picker, confirm dialog, cosmetics.
        style.SetCornerRadiusAll(0);
        style.SetContentMarginAll(16f);
        style.ContentMarginTop = 14f;
        style.ContentMarginBottom = 14f;
        return style;
    }

    // --- Logo tie-in ----------------------------------------------------------------------------
    //
    // The logo has its own signature under the wordmark: two stacked streaks, magenta above cyan,
    // the lower one shorter and offset right. This reproduces that as a small standalone accent —
    // two flat ColorRects, still perfectly axis-aligned (no diagonal, matching the rest of the
    // game's right-angle rule) — so any screen's header can echo the logo without touching its
    // title text or panel shape.
    private static readonly Color SpeedLineMagenta = new(1f, 0.31f, 0.847f, 0.85f);
    private static readonly Color SpeedLineCyan = new(0.4902f, 0.9922f, 0.9961f, 0.85f);

    /// <summary>
    /// Builds the centered accent and inserts it into <paramref name="parent"/> right after
    /// <paramref name="afterIndex"/> (typically the title label's own index) — the caller doesn't
    /// need to know this is two ColorRects under a CenterContainer, just where it should sit.
    /// </summary>
    public static void AddSpeedLines(Control parent, int afterIndex, float width = 140f)
    {
        var center = new CenterContainer { CustomMinimumSize = new Vector2(0f, 10f) };

        var box = new Control { CustomMinimumSize = new Vector2(width, 10f) };
        box.AddChild(new ColorRect
        {
            Color = SpeedLineMagenta,
            Position = Vector2.Zero,
            Size = new Vector2(width, 3f),
        });
        box.AddChild(new ColorRect
        {
            Color = SpeedLineCyan,
            Position = new Vector2(width * 0.18f, 6f),
            Size = new Vector2(width * 0.7f, 3f),
        });
        center.AddChild(box);

        parent.AddChild(center);
        parent.MoveChild(center, afterIndex + 1);
    }

    // --- Keeping modal panels on screen -------------------------------------------------------
    //
    // A ScrollContainer inside a CenterContainer has no height of its own to speak of: it reports a
    // near-zero minimum for the axis it scrolls, which is what lets it scroll in the first place. So
    // its custom_minimum_size doesn't merely raise a floor there, it *is* the height — and until one
    // is set, the panel around it grows to whatever its content wants and runs off the screen.
    //
    // Several screens set that height to a hardcoded number measured against the 648px landscape
    // viewport. Those numbers were correct when they were written and silently wrong the moment
    // anything changed: swapping the project font for one with a taller line box grew every label in
    // the game and pushed panels off the bottom of the screen, with nothing in the code to notice.
    // Deriving the height from the live viewport instead means the panel is bounded by construction.

    /// <summary>
    /// The tallest a scroll area may be and still leave room for the rest of its panel. Use this when
    /// the caller wants to animate toward the value; <see cref="FitScrollToViewport"/> assigns it.
    /// </summary>
    public static float AvailableScrollHeight(Control panel, Control scroll,
        float fraction = 0.92f, float minHeight = 80f)
    {
        if (panel == null || scroll == null) return minHeight;

        // The scroll contributes exactly its custom_minimum_size to the panel's minimum (see above),
        // so taking that back out leaves the chrome — titles, buttons, margins — it has to share the
        // panel with. Reading a stale value is harmless: it's the same one being subtracted.
        float chrome = panel.GetCombinedMinimumSize().Y - scroll.CustomMinimumSize.Y;
        return Mathf.Max(minHeight, scroll.GetViewportRect().Size.Y * fraction - chrome);
    }

    /// <summary>
    /// Sizes a scroll area to its content, capped so the panel around it fits on screen. Short menus
    /// stay short — the cap is a ceiling, not a target.
    /// </summary>
    public static void FitScrollToViewport(ScrollContainer scroll, Control panel,
        float fraction = 0.92f, float minHeight = 120f)
    {
        if (scroll == null || panel == null) return;

        var content = scroll.GetChildCount() > 0 ? scroll.GetChild(0) as Control : null;
        float needed = content?.GetCombinedMinimumSize().Y ?? 0f;
        float available = AvailableScrollHeight(panel, scroll, fraction, minHeight);

        scroll.CustomMinimumSize = new Vector2(
            scroll.CustomMinimumSize.X, Mathf.Max(minHeight, Mathf.Min(needed, available)));
    }

    // --- Tap outside to close --------------------------------------------------------------------
    //
    // Every modal's Dim (a full-rect ColorRect behind the panel) already blocks clicks from passing
    // through to whatever's underneath — that's Control's default mouse_filter (Stop), untouched in
    // every .tscn — but nothing ever listened on it, so tapping the dimmed backdrop today just does
    // nothing. This makes it act like the screen's own Close.

    /// <summary>Tapping/clicking <paramref name="dim"/> calls <paramref name="close"/> — wire this to
    /// a screen's "Dim" node and its own Close() so backing out doesn't require finding the button.
    /// Not every screen should get this (a forced choice like the reward picker shouldn't be
    /// dismissible by tapping past it) — see docs/navigation.md for which ones do.</summary>
    public static void WireDimToClose(Control dim, Action close)
    {
        dim.GuiInput += @event =>
        {
            if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }
                or InputEventScreenTouch { Pressed: true })
                close();
        };
    }
}
