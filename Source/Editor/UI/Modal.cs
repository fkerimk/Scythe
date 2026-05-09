using System.Numerics;
using ImGuiNET;
using static ImGuiNET.ImGui;

internal static class Modal {

    public static bool Begin(string id, ref bool isOpen, bool centerOnAppearing = true, bool hideTitleBar = false, ImGuiWindowFlags extraFlags = ImGuiWindowFlags.None) {

        if (centerOnAppearing) {
            var viewport = GetMainViewport();
            SetNextWindowPos(viewport.GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        }

        PushCommonStyle();

        var flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove | extraFlags;
        if (hideTitleBar) flags |= ImGuiWindowFlags.NoTitleBar;

        if (BeginPopupModal(id, ref isOpen, flags)) return true;

        PopCommonStyle();
        return false;
    }

    public static bool BeginPopup(string id, Vector2? appearingPosition = null, ImGuiWindowFlags extraFlags = ImGuiWindowFlags.None) {

        if (appearingPosition.HasValue) SetNextWindowPos(appearingPosition.Value, ImGuiCond.Appearing);

        PushCommonStyle();

        if (ImGuiNET.ImGui.BeginPopup(id, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove | extraFlags)) return true;

        PopCommonStyle();
        return false;
    }

    public static void End() {

        EndPopup();
        PopCommonStyle();
    }

    private static void PushCommonStyle() {

        PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(24, 24));
        PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10, 8));
        PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 12));
        PushStyleColor(ImGuiCol.ModalWindowDimBg, new Vector4(0f, 0f, 0f, 0.75f));
    }

    private static void PopCommonStyle() {

        PopStyleColor();
        PopStyleVar(3);
    }
}
