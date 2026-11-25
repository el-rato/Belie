namespace WallpaperProfiles.Workspaces;

/// <summary>Identifies a window even after Windows reuses its handle.</summary>
internal sealed record WindowIdentity(long Handle, int ProcessId, long ProcessStart, string ClassName);

/// <param name="Root">The owner a dialog follows, or the window itself.</param>
internal sealed record DesktopWindow(WindowIdentity Identity, nint Root, string Title, string Monitor)
{
    public nint Handle => (nint)Identity.Handle;
}

/// <summary>The window operations workspaces need, so their rules can be tested without touching real windows.</summary>
internal interface IWindowSystem
{
    /// <summary>App windows that are on screen or minimized. Never includes windows Belie should leave alone.</summary>
    IReadOnlyList<DesktopWindow> VisibleWindows();
    /// <summary>Monitors with workspaces; the primary one is first.</summary>
    IReadOnlyList<string> Monitors();
    bool Exists(WindowIdentity window);
    bool IsVisible(nint window);
    void Show(nint window);
    void Hide(nint window);
    nint Foreground();
    void Focus(nint window);
    void FocusDesktop();
}
