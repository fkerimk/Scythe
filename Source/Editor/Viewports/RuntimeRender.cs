using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using static ImGuiNET.ImGui;
using static Raylib_cs.Raylib;

internal class RuntimeRender : Viewport {

    public RenderTexture2D Rt = new();
    public Vector2 TexSize = Vector2.One, TexTemp = Vector2.Zero;
    public Vector2 ScreenPos;

    public RuntimeRender() : base("Render (Runtime)") { CustomStyle = new CustomStyle { WindowPadding = Vector2.Zero }; }

    protected override void OnDraw() {

        if (Rt.Texture.Id > 0) {

            var tex = (IntPtr)Rt.Texture.Id;
            var contentAvail = GetContentRegionAvail();
            ScreenPos = GetCursorScreenPos();

            // Draw game render
            Image(tex, contentAvail, new Vector2(0, 1), new Vector2(1, 0));
            TexSize = contentAvail;

            // Draw overlay ui
            var padding = new Vector2(10, 10);
            SetCursorScreenPos(ScreenPos + padding);

            // Semi-transparent background for the control panel
            var drawList = GetWindowDrawList();
            var bgMin = ScreenPos + padding - new Vector2(4, 4);
            var bgMax = bgMin + new Vector2(145, 30); // Adjust based on content
            drawList.AddRectFilled(bgMin, bgMax, ColorConvertFloat4ToU32(new Vector4(0.08f, 0.08f, 0.1f, 0.75f)), 6.0f);
            drawList.AddRect(bgMin, bgMax, ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 0.1f)), 6.0f);

            BeginGroup();

            var icon = Core.IsPlaying ? Icons.FaStop : Icons.FaPlay;
            var statusText = Core.IsPlaying ? "Playing..." : "Game View";
            var color = Core.IsPlaying ? Colors.Primary : Colors.GuiText;

            // Use FontAwesome for the button
            PushFont(Fonts.ImFontAwesomeSmall);
            PushStyleColor(ImGuiCol.Text, color.ToVector4());
            SetCursorPosX(GetCursorPosX() + 4);

            if (Button($"{icon}##PlayToggle", new Vector2(30, 22))) {

                var center = ScreenPos + contentAvail / 2f;
                Editor.TogglePlayMode(center);
            }

            PopStyleColor();
            PopFont();

            SameLine();
            SetCursorPosY(GetCursorPosY() + 2); // Center text vertically
            TextDisabled(statusText);

            EndGroup();

            const float fontSize = 26f;

            var fpsText = GetFPS().ToString();
            var textSize = CalcTextSize(fpsText) * (fontSize / GetFontSize());
            var fpsPadding = new Vector2(10, 10);

            var pos = ScreenPos + new Vector2(contentAvail.X - textSize.X - fpsPadding.X, fpsPadding.Y);

            drawList.AddText(GetFont(), fontSize, pos + new Vector2(1, 1), ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 1)), fpsText);
            drawList.AddText(GetFont(), fontSize, pos, ColorConvertFloat4ToU32(Colors.Primary.ToVector4()), fpsText);
        } else
            TextDisabled("Waiting for first frame...");
    }
}
