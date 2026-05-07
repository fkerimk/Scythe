using System.Numerics;
using ImGuiNET;

internal abstract class Viewport(string title) {

    public string Title { get; } = title;
    public CustomStyle? CustomStyle;
    protected ImGuiWindowFlags WindowFlags { get; init; } = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoNav;

    public Vector2 WindowPos;
    public Vector2 ContentRegion;
    public Vector2 RelativeMouse;

    public bool IsOpen = true;
    public bool IsHovered;
    public bool IsFocused;
    public bool ShouldFocus;
    public bool AutoHideWhenEmpty;

    public void Draw() {

        if (!IsOpen) return;
        if (AutoHideWhenEmpty && !HasContent()) {
            IsHovered = false;
            IsFocused = false;
            return;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
        if (CustomStyle != null) Style.Push(CustomStyle);

        if (ShouldFocus) {

            ImGui.SetNextWindowFocus();
            ShouldFocus = false;
        }

        if (!ImGui.Begin(Title, ref IsOpen, WindowFlags)) {

            IsHovered = false;

            ImGui.End();
            ImGui.PopStyleVar();
            if (CustomStyle != null) Style.Pop();

            return;
        }

        WindowPos = ImGui.GetWindowPos();
        ContentRegion = ImGui.GetContentRegionAvail();
        RelativeMouse = ImGui.GetMousePos() - WindowPos - ImGui.GetCursorStartPos();
        IsHovered = ImGui.IsWindowHovered();
        IsFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);

        OnDraw();

        ImGui.End();
        ImGui.PopStyleVar();

        if (CustomStyle != null) Style.Pop();
    }

    protected virtual bool HasContent() => true;
    protected abstract void OnDraw();
}
