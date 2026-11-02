#nullable disable
// Snapshot of StatsDirect Windows help host (MIT).
// Original: StatsDirectUI/UI/ToolWindows/ToolPaneSession.cs
// See provenance.json for source hashes and the deliberately limited editor integration changes.
using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StatsDirect.HelpEditor.App.AppHelp;

/// <summary>Owns one live tool session and its presentation. Docking never creates an MDI document.</summary>
internal sealed class ToolPaneSession : IDisposable
{
    private ToolWindow window;
    private Control view;
    private ToolDockHost dock;
    private Form host, modalOwner;
    private Rectangle? floatingBounds;
    private bool visible, preferFloating, temporary, changing, disposed;
    internal event Action Hidden;
    internal ToolWindow Window => window;
    internal Control View => view;
    internal ToolDockHost DockHost => dock;
    internal bool IsDocked => visible && window == null;
    internal bool IsVisible => visible;
    internal bool IsTemporary => temporary;

    // Native message boxes disable their owner's HWND without necessarily
    // updating Control.Enabled. Check both when deciding whether a tool can dock.
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr handle);
    private static bool IsInteractive(Form form) => form.Enabled && (!form.IsHandleCreated || IsWindowEnabled(form.Handle));

    internal static Form FindOwner(Form parent) => Application.OpenForms.Cast<Form>().LastOrDefault(f => f.Modal && f.Visible) ?? parent?.MdiParent ?? parent;

    private readonly Size defaultSize;
    private readonly Size? minimumWindowSize;
    private readonly Action<bool, bool> presentation;
    private readonly Action focus;
    private readonly Func<Action, Task> preservePosition;
    private readonly Action<string> notice;
    private string title;

    internal ToolPaneSession(Form host, Control view, string name, DockStyle edge, int length, Size defaultSize,
        Action<bool, bool> presentation, Action focus, Action<string> notice, Func<Action, Task> preservePosition = null,
        int minimumDockLength = 240, Size? minimumWindowSize = null)
    {
        this.host = host;
        this.view = view;
        this.defaultSize = defaultSize;
        this.minimumWindowSize = minimumWindowSize;
        this.presentation = presentation;
        this.focus = focus;
        this.notice = notice;
        this.preservePosition = preservePosition;
        title = "StatsDirect " + name;
        dock = new ToolDockHost(host, name, edge, length, minimumDockLength);
        host.FormClosed += HostClosed;
        Application.Idle += CheckModality;
    }

    internal void Show(bool focusTool)
    {
        if (disposed) return;
        Control previousFocus = FocusedControl(host);
        visible = true;
        UpdatePresentation();
        if (window != null)
        {
            if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
            window.Activate();
        }
        if (focusTool) focus?.Invoke();
        else if (IsDocked && previousFocus?.CanFocus == true) previousFocus.Focus();
    }


    internal async Task ToggleModeAsync()
    {
        if (!visible || temporary || changing) return;
        changing = true;
        try
        {
            void Move()
            {
                if (disposed || !visible) return;
                preferFloating = window == null;
                changing = false;
                UpdatePresentation();
                changing = true;
                if (window != null) window.Activate();
                focus?.Invoke();
            }
            if (preservePosition != null) await preservePosition(Move); else Move();
        }
        catch (Exception ex) { if (!disposed) notice?.Invoke("Could not move the window: " + ex.Message); }
        finally { changing = false; }
    }

    internal void Hide()
    {
        if (view == null || disposed) return;
        bool wasVisible = visible;
        visible = false;
        temporary = false;
        UnhookModal();
        ParkView();
        CloseFrame();
        dock.SetVisible(false);
        if (host.Enabled) host.ActiveMdiChild?.Activate();
        if (wasVisible) Hidden?.Invoke();
    }

    private void CheckModality(object sender, EventArgs e)
    {
        if (visible && !changing && !disposed) UpdatePresentation();
    }

    private void UpdatePresentation()
    {
        if (changing || !visible || host.IsDisposed) return;
        changing = true;
        try
        {
            Form owner = FindOwner(host);
            bool modal = owner != host || !IsInteractive(host);
            temporary = modal;
            if (modal || preferFloating)
            {
                if (window == null || window.IsDisposed || window.Owner != owner || !IsInteractive(window))
                    Float(owner, modal);
                presentation(true, !modal);
            }
            else
            {
                if (window != null || view.Parent != dock.Panel || !dock.Panel.Visible)
                {
                    UnhookModal();
                    ParkView();
                    CloseFrame();
                    dock.SetVisible(true);
                    presentation(false, true);
                }
            }
        }
        finally { changing = false; }
    }

    private void Float(Form owner, bool modal)
    {
        Control previousFocus = FocusedControl(Form.ActiveForm ?? owner);
        UnhookModal();
        ParkView();
        CloseFrame();
        dock.SetVisible(false);
        window = new ToolWindow(title, minimumWindowSize);
        window.Controls.Add(view);
        window.CloseRequested += Hide;
        UpdateTitle();
        if (modal && owner != host)
        {
            modalOwner = owner;
            modalOwner.FormClosed += ModalClosed;
        }
        window.Show(owner);
        var screen = Screen.FromControl(owner).WorkingArea;
        float scale = window.DeviceDpi / 96f;
        var size = floatingBounds?.Size ?? new Size((int)(defaultSize.Width * scale), (int)(defaultSize.Height * scale));
        size = new Size(Math.Min(size.Width, screen.Width), Math.Min(size.Height, screen.Height));
        var location = floatingBounds?.Location ?? new Point(Math.Min(host.Right, screen.Right) - size.Width, screen.Top + (screen.Height - size.Height) / 2);
        location.X = Math.Clamp(location.X, screen.Left, screen.Right - size.Width);
        location.Y = Math.Clamp(location.Y, screen.Top, screen.Bottom - size.Height);
        window.Bounds = new Rectangle(location, size);
        // Reparenting a focused WinForms editor can activate its new frame even
        // with ShowWithoutActivation. Automatic moves must preserve the dialog's
        // input focus; an explicit Show/Pop out focuses the tool afterwards.
        if (previousFocus?.CanFocus == true && !view.Contains(previousFocus)) previousFocus.Focus();
    }

    private void ParkView()
    {
        if (view.Parent != dock.Panel) dock.Panel.Controls.Add(view);
    }

    private void CloseFrame()
    {
        if (window == null) return;
        if (!window.IsDisposed)
        {
            floatingBounds = window.WindowState == FormWindowState.Normal ? window.Bounds : window.RestoreBounds;
            window.Retiring = true;
            window.CloseRequested -= Hide;
            window.Close();
            window.Dispose();
        }
        window = null;
    }

    private void ModalClosed(object sender, FormClosedEventArgs e)
    {
        // Detach before an owned frame is disposed. Restore after ShowDialog's
        // native modal loop re-enables the workspace, not inside FormClosed.
        UnhookModal();
        ParkView();
        CloseFrame();
        if (visible && !host.IsDisposed) host.BeginInvoke(new Action(UpdatePresentation));
    }

    private void UnhookModal()
    {
        if (modalOwner != null) modalOwner.FormClosed -= ModalClosed;
        modalOwner = null;
    }
    internal void SetTitle(string value) { title = value; UpdateTitle(); }
    private void UpdateTitle() { if (window != null) window.Text = title; }
    private void HostClosed(object sender, FormClosedEventArgs e) => Dispose();
    private static Control FocusedControl(Control control)
    {
        if (control.Focused) return control;
        foreach (Control child in control.Controls)
            if (child.ContainsFocus) return FocusedControl(child);
        return null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Application.Idle -= CheckModality;
        UnhookModal();
        if (host != null) host.FormClosed -= HostClosed;
        if (view != null)
        {
            view.Parent?.Controls.Remove(view);
            view.Dispose();
        }
        CloseFrame();
        dock?.Dispose();
    }
}
