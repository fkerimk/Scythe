using Raylib_cs;

internal static class Colors {

    // generic
    public static Color Clear => new(0f, 0f, 0f, 0f);
    public static Color Primary => new(255, 110, 40, 255);
    public static Color TextDisabled => new(110, 110, 125, 255);
    private static Color PrimarySoft => new(180, 80, 30, 255);
    public static Color Back => new(18, 18, 26, 255);
    public static Color Game => new(12, 12, 14, 255);
    public static Color Grid => new(80, 100, 200, 20);
    public static Color PlayModeTint => new(Primary.R / 2, Primary.G / 2, Primary.B / 2, 255);

    // ImGui
    public static Color GuiText => new(230, 230, 245, 255);
    public static Color GuiTextDisabled => new(110, 110, 125, 255);

    public static Color GuiWindowBg => new(22, 22, 30, 255);

    //ChildBg,
    //PopupBg,
    public static Color GuiBorder => new(45, 45, 60, 255);

    //BorderShadow,
    public static Color GuiFrameBg => new(32, 32, 48, 255);
    public static Color GuiFrameBgHovered => new(55, 55, 85, 255);
    public static Color GuiFrameBgActive => PrimarySoft;
    public static Color GuiFieldOverride => new(78, 54, 28, 255);
    public static Color GuiFieldOverrideHovered => new(102, 69, 35, 255);
    public static Color GuiFieldOverrideActive => new(138, 86, 36, 255);
    public static Color GuiTitleBg => new(18, 18, 25, 255);
    public static Color GuiTitleBgActive => new(30, 30, 42, 255);

    public static Color GuiTitleBgCollapsed => GuiTitleBg;

    //MenuBarBg,
    //ScrollbarBg,
    //ScrollbarGrab,
    //ScrollbarGrabHovered,
    //ScrollbarGrabActive,
    public static Color GuiCheckMark => Primary;

    //SliderGrab,
    //SliderGrabActive,
    public static Color GuiButton => new(45, 45, 65, 255);
    public static Color GuiButtonHovered => new(70, 70, 105, 255);
    public static Color GuiButtonActive => Primary;
    public static Color GuiHeader => new(40, 40, 58, 255);
    public static Color GuiHeaderHovered => new(65, 65, 95, 255);

    public static Color GuiHeaderActive => Primary;

    //Separator,
    //SeparatorHovered,
    //SeparatorActive,
    public static Color GuiResizeGrip => Clear;
    public static Color GuiResizeGripHovered => new(0.45f, 0.45f, 0.65f, 1.0f);
    public static Color GuiResizeGripActive => Primary;
    public static Color GuiTabHovered => new(60, 60, 95, 255);
    public static Color GuiTab => new(28, 28, 42, 255);
    public static Color GuiTabSelected => new(40, 40, 65, 255);
    public static Color GuiTabSelectedOverline => Primary;
    public static Color GuiTabDimmed => GuiTab;
    public static Color GuiTabDimmedSelected => GuiTabSelected;
    public static Color GuiTabDimmedSelectedOverline => GuiTabDimmed;

    public static Color GuiDockingPreview => Primary;

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
    public static Color GuiDragDropTarget => Primary;

    //NavCursor,
    //NavWindowingHighlight,
    //NavWindowingDimBg,
    //ModalWindowDimBg,

    // gui
    public static Color GuiTreeEnabled => new(0.5f, 0.5f, 0.5f);
    public static Color GuiTreeDisabled => new(0.3f, 0.2f, 0.2f);
    public static Color GuiTreeSelected => PrimarySoft;
    public static Color GuiTypeObject => new(0.5f, 0.5f, 0.5f);
    public static Color GuiTypeModel => new(0.5f, 0.9f, 0.9f);
    public static Color GuiTypeTransform => new(0.9f, 0.5f, 0.2f);
    public static Color GuiTypeAnimation => new(0.9f, 0.5f, 0.9f);
    public static Color GuiTypeLight => new(0.9f, 0.9f, 0.5f);
    public static Color GuiTypeCamera => new(0.5f, 0.9f, 0.9f);
    public static Color GuiTypePhysics => new(78, 207, 113);

    // collections
    public static Color GuiCollection => new(255, 204, 51, 255);
    public static Color GuiCollectionMuted => new(115, 115, 115, 255);
    public static Color GuiCollectionLevel => new(237, 150, 94, 255);
    public static Color GuiCollectionMaterial => new(214, 96, 66, 255);
    public static Color GuiCollectionModel => new(133, 219, 245, 255);
    public static Color GuiCollectionPrefab => new(209, 148, 237, 255);
    public static Color GuiCollectionScript => new(120, 199, 242, 255);
    public static Color GuiCollectionTexture => new(120, 224, 158, 255);

    // preview code
    public static Color GuiCodeText => new(220, 220, 230, 255);
    public static Color GuiCodeKeyword => new(86, 156, 214, 255);
    public static Color GuiCodeType => new(78, 201, 176, 255);
    public static Color GuiCodeString => new(214, 157, 133, 255);
    public static Color GuiCodeComment => new(106, 153, 85, 255);
    public static Color GuiCodeNumber => new(181, 206, 168, 255);
    public static Color GuiCodePreprocessor => new(198, 120, 221, 255);
    public static Color GuiCodePlain => new(212, 212, 212, 255);
}
