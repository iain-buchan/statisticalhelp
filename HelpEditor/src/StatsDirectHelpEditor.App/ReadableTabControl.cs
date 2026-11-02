namespace StatsDirect.HelpEditor.App;

/// <summary>Native tabs with readable labels and a distinct selected state.</summary>
internal sealed class ReadableTabControl : TabControl
{
    private readonly Font labelFont = new("Arial", 12, FontStyle.Bold);
    private bool updatingMetrics;
    private bool metricsPending;

    internal event EventHandler? HeaderMetricsChanged;

    internal ReadableTabControl()
    {
        Font = labelFont;
        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Normal;
        Multiline = false;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        QueueHeaderMetrics();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        QueueHeaderMetrics();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        QueueHeaderMetrics();
    }

    private int Scale(int value) => (int)Math.Ceiling(value * DeviceDpi / 96d);

    private void QueueHeaderMetrics()
    {
        if (!IsHandleCreated || IsDisposed || Disposing || metricsPending) return;
        metricsPending = true;
        // Native TabControl.Padding can recreate its handle. Defer until the
        // current handle/font/DPI notification has finished creating it.
        BeginInvoke((Action)(() => { metricsPending = false; UpdateHeaderMetrics(); }));
    }

    private void UpdateHeaderMetrics()
    {
        if (!IsHandleCreated || IsDisposed || Disposing || updatingMetrics) return;
        updatingMetrics = true;
        try
        {
            Padding = new Point(Scale(18), Scale(8));
            // Width stays native/content-sized; only the minimum header height
            // is fixed. Four different-length workspace names still fit.
            ItemSize = new Size(Scale(80), Math.Max(Scale(44), Font.Height + Scale(16)));
            Invalidate();
            HeaderMetricsChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { updatingMetrics = false; }
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= TabCount) return;
        bool selected = e.Index == SelectedIndex;
        bool highContrast = SystemInformation.HighContrast;
        Color background = selected
            ? highContrast ? SystemColors.Highlight : Color.FromArgb(7, 59, 92)
            : highContrast ? SystemColors.Window : Color.FromArgb(230, 238, 245);
        Color foreground = selected
            ? highContrast ? SystemColors.HighlightText : Color.White
            : !Enabled ? SystemColors.GrayText : highContrast ? SystemColors.WindowText : Color.FromArgb(32, 51, 68);
        Color border = highContrast ? SystemColors.WindowText : Color.FromArgb(54, 86, 111);
        Color strip = highContrast ? SystemColors.Control : Color.FromArgb(204, 216, 228);
        using (var brush = new SolidBrush(strip)) e.Graphics.FillRectangle(brush, e.Bounds);
        Rectangle bounds = e.Bounds;
        bounds.Inflate(-Scale(3), -Scale(3));
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, bounds);
        if (selected)
        {
            Color accent = highContrast ? SystemColors.HighlightText : Color.FromArgb(255, 196, 71);
            using var brush = new SolidBrush(accent);
            e.Graphics.FillRectangle(brush, bounds.Left + Scale(2), bounds.Bottom - Scale(6), bounds.Width - Scale(4), Scale(4));
        }
        using (var pen = new Pen(border, Math.Max(1, Scale(2))) { Alignment = System.Drawing.Drawing2D.PenAlignment.Inset })
            e.Graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        Rectangle text = Rectangle.Inflate(bounds, -Scale(10), -Scale(4));
        TextRenderer.DrawText(e.Graphics, TabPages[e.Index].Text, Font, text, foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        if (selected && Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -Scale(7), -Scale(7)), foreground, background);
    }

    protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) labelFont.Dispose();
    }
}
