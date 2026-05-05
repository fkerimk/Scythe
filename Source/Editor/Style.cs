using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using static ImGuiNET.ImGui;

internal static class Style {

    private static int _pushes;
    private static readonly List<int> PushedColors = [];
    private static readonly List<int> PushedVars = [];

    public static void Push(CustomStyle? style = null) {

        if (PushedColors.Count == _pushes) PushedColors.Add(0);
        if (PushedVars.Count == _pushes) PushedVars.Add(0);

        GetStyle().WindowMenuButtonPosition = ImGuiDir.None;

        Set(ImGuiCol.Text, Colors.GuiText);
        Set(ImGuiCol.TextDisabled, Colors.GuiTextDisabled);
        Set(ImGuiCol.WindowBg, Colors.GuiWindowBg);

        Set(ImGuiCol.ChildBg, Colors.GuiWindowBg);
        Set(ImGuiCol.PopupBg, Colors.GuiWindowBg);
        Set(ImGuiCol.Border, Colors.GuiBorder);

        //BorderShadow,
        Set(ImGuiCol.FrameBg, Colors.GuiFrameBg);
        Set(ImGuiCol.FrameBgHovered, Colors.GuiFrameBgHovered);
        Set(ImGuiCol.FrameBgActive, Colors.GuiFrameBgActive);
        Set(ImGuiCol.TitleBg, Colors.GuiTitleBg);
        Set(ImGuiCol.TitleBgActive, Colors.GuiTitleBgActive);
        Set(ImGuiCol.TitleBgCollapsed, Colors.GuiTitleBgCollapsed);

        //MenuBarBg,
        //ScrollbarBg,
        //ScrollbarGrab,
        //ScrollbarGrabHovered,
        //ScrollbarGrabActive,
        Set(ImGuiCol.CheckMark, Colors.GuiCheckMark);

        //SliderGrab,
        //SliderGrabActive,
        Set(ImGuiCol.Button, Colors.GuiButton);
        Set(ImGuiCol.ButtonHovered, Colors.GuiButtonHovered);
        Set(ImGuiCol.ButtonActive, Colors.GuiButtonActive);
        Set(ImGuiCol.Header, Colors.GuiHeader);
        Set(ImGuiCol.HeaderHovered, Colors.GuiHeaderHovered);
        Set(ImGuiCol.HeaderActive, Colors.GuiHeaderActive);

        //Separator,
        //SeparatorHovered,
        //SeparatorActive,
        Set(ImGuiCol.ResizeGrip, Colors.GuiResizeGrip);
        Set(ImGuiCol.ResizeGripHovered, Colors.GuiResizeGripHovered);
        Set(ImGuiCol.ResizeGripActive, Colors.GuiResizeGripActive);
        Set(ImGuiCol.TabHovered, Colors.GuiTabHovered);
        Set(ImGuiCol.Tab, Colors.GuiTab);
        Set(ImGuiCol.TabSelected, Colors.GuiTabSelected);
        Set(ImGuiCol.TabSelectedOverline, Colors.GuiTabSelectedOverline);
        Set(ImGuiCol.TabDimmed, Colors.GuiTabDimmed);
        Set(ImGuiCol.TabDimmedSelected, Colors.GuiTabDimmedSelected);
        Set(ImGuiCol.TabDimmedSelectedOverline, Colors.GuiTabDimmedSelectedOverline);
        Set(ImGuiCol.DockingPreview, Colors.GuiDockingPreview);

        //DockingEmptyBg,
        //PlotLines,
        //PlotLinesHovered,
        //PlotHistogram,
        //PlotHistogramHovered,
        //TableHeaderBg,
        //TableBorderStrong,
        //TableBorderLight,
        //TableRowBg,
        //TableRowBgAlt,
        //TextLink,
        //TextSelectedBg,
        Set(ImGuiCol.DragDropTarget, Colors.GuiDragDropTarget);

        //NavCursor,
        Set(ImGuiCol.NavWindowingHighlight, new Color(255, 255, 255, 180));
        Set(ImGuiCol.NavWindowingDimBg, new Color(200, 200, 200, 50));
        Set(ImGuiCol.ModalWindowDimBg, new Color(0, 0, 0, 150));

        if (style?.WindowPadding != null) Set(ImGuiStyleVar.WindowPadding, style.Value.WindowPadding.Value);
        if (style?.CellPadding != null) Set(ImGuiStyleVar.CellPadding, style.Value.CellPadding.Value);
        if (style?.FramePadding != null) Set(ImGuiStyleVar.FramePadding, style.Value.FramePadding.Value);
        if (style?.SeparatorTextPadding != null) Set(ImGuiStyleVar.SeparatorTextPadding, style.Value.SeparatorTextPadding.Value);

        _pushes++;
    }

    private static void Set(ImGuiCol id, Color color) {

        if (Core.IsPlaying) {

            // Apply subtle red tint in play mode
            var tint = Colors.PlayModeTint;

            // Blend original color with tint (20% tint strength)
            // Raylib's ColorLerp is nice but let's do it manually for precision with alpha
            var r = (byte)Raymath.Lerp(color.R, tint.R, 0.15f);
            var g = (byte)Raymath.Lerp(color.G, tint.G, 0.15f);
            var b = (byte)Raymath.Lerp(color.B, tint.B, 0.15f);

            color = new Color(r, g, b, color.A);
        }

        PushStyleColor(id, color.ToVector4());
        PushedColors[_pushes]++;
    }

    private static void Set(ImGuiStyleVar id, Vector2 value) {

        PushStyleVar(id, value);
        PushedVars[_pushes]++;
    }

    public static void Pop() {

        for (var i = 0; i < PushedColors[_pushes - 1]; i++) PopStyleColor();
        PushedColors.RemoveAt(_pushes - 1);

        for (var i = 0; i < PushedVars[_pushes - 1]; i++) PopStyleVar();
        PushedVars.RemoveAt(_pushes - 1);

        _pushes--;
    }
}

public struct CustomStyle {

    public Vector2? WindowPadding = null;
    public Vector2? CellPadding = null;
    public Vector2? FramePadding = null;
    public Vector2? SeparatorTextPadding = null;

    public CustomStyle() { }
}