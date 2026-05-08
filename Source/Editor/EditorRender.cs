using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using Newtonsoft.Json;
using static ImGuiNET.ImGui;
using static Raylib_cs.Raylib;

internal class EditorRender() : Viewport("Render (Editor)") {

    public RenderTexture2D Rt = new(), OutlineRt = new();
    public Vector2 TexSize = Vector2.One, TexTemp = Vector2.Zero;
    public static Vector2 RelativeMouse3D;

    private static string GetPath() => Path.Join(ScytheConfig.Current.Project, "Project", "EditorRender.json");

    public void Load() {

        var path = GetPath();

        if (File.Exists(path)) {

            SafeExec.Try(() => {

                    var settings = JsonConvert.DeserializeObject<EditorRenderSettings>(File.ReadAllText(path));

                    if (settings == null) return;

                    foreach (var relPath in settings.OpenLevels) {

                        var absPath = Path.Join(ScytheConfig.Current.Project, relPath);
                        if (File.Exists(absPath)) Editor.OpenLevel(absPath);
                    }

                    if (settings.ActiveLevelIndex >= 0 && settings.ActiveLevelIndex < Core.OpenLevels.Count) Core.SetActiveLevel(settings.ActiveLevelIndex);
                }
            );
        }

        EnsureAtLeastOneLevelOpen();
    }

    private void EnsureAtLeastOneLevelOpen() {

        if (Core.OpenLevels.Count > 0) return;

        var configuredStartup = ProjectConfig.Current.StartupLevel?.Replace('\\', '/');

        if (!string.IsNullOrWhiteSpace(configuredStartup)) {

            var startupPath = Path.IsPathRooted(configuredStartup)
                ? configuredStartup
                : Path.Combine(ScytheConfig.Current.Project, configuredStartup);

            startupPath = Path.GetFullPath(startupPath);

            if (File.Exists(startupPath)) {
                Editor.OpenLevel(startupPath);
                return;
            }
        }

        if (PathUtil.GetPath("Levels/Main.level.json", out var mainLevelPath)) {
            Editor.OpenLevel(mainLevelPath);
            return;
        }

        if (PathUtil.GetPath("Levels/Main.json", out var mainPath)) {
            Editor.OpenLevel(mainPath);
        } else {

            var levelsDir = Path.Join(ScytheConfig.Current.Project, "Levels");

            if (Directory.Exists(levelsDir)) {

                var firstJson = Directory.GetFiles(levelsDir, "*.json", SearchOption.AllDirectories).FirstOrDefault();

                if (firstJson != null)
                    Editor.OpenLevel(firstJson);
                else
                    Editor.CreateLevel(Path.Combine(levelsDir, "Main.json"));

            } else {

                Directory.CreateDirectory(levelsDir);
                Editor.CreateLevel(Path.Combine(levelsDir, "Main.json"));
            }
        }
    }

    public void Save() {

        var path = GetPath();
        var dir = Path.GetDirectoryName(path);
        if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var settings = new EditorRenderSettings { OpenLevels = Core.OpenLevels.Select(l => Path.GetRelativePath(ScytheConfig.Current.Project, l.JsonPath).Replace('\\', '/')).ToList(), ActiveLevelIndex = Core.ActiveLevelIndex };

        File.WriteAllText(path, JsonConvert.SerializeObject(settings, Formatting.Indented));
    }

    private class EditorRenderSettings {

        public List<string> OpenLevels { get; init; } = [];
        public int ActiveLevelIndex { get; init; } = -1;
    }

    protected override void OnDraw() {

        EnsureAtLeastOneLevelOpen();

        if (Core.OpenLevels.Count == 0) return;

        if (BeginTabBar("##LevelTabs", ImGuiTabBarFlags.AutoSelectNewTabs | ImGuiTabBarFlags.Reorderable)) {

            for (var i = 0; i < Core.OpenLevels.Count; i++) {

                var level = Core.OpenLevels[i];
                var label = $"{level.Name}{(level.IsDirty ? " *" : "")}###level_{level.GetHashCode()}";

                var open = true;
                var isSelected = Core.ActiveLevelIndex == i;
                var flags = ImGuiTabItemFlags.None;

                if (Core.ShouldFocusActiveLevel && isSelected) flags |= ImGuiTabItemFlags.SetSelected;

                if (BeginTabItem(label, ref open, flags)) {

                    if (!isSelected) Core.SetActiveLevel(i);

                    if (Rt.Texture is { Width: > 0, Height: > 0 }) {

                        var tex = (IntPtr)Rt.Texture.Id;
                        var contentPos = GetCursorScreenPos();
                        var avail = GetContentRegionAvail();

                        Image(tex, avail, new Vector2(0, 1), new Vector2(1, 0));

                        var mouse = GetMousePosition();
                        var relX = Raymath.Clamp((mouse.X - contentPos.X) / avail.X, 0, 1);
                        var relY = Raymath.Clamp((mouse.Y - contentPos.Y) / avail.Y, 0, 1);

                        relX = (relX - 0.5f) * (avail.X / GetScreenWidth()) * (GetScreenHeight() / avail.Y) + 0.5f;
                        RelativeMouse3D = new Vector2(relX * GetScreenWidth(), relY * GetScreenHeight());

                        TexSize = avail;
                    }

                    EndTabItem();
                }

                if (open) continue;

                Core.CloseLevel(i);

                break;
            }

            EndTabBar();
        }

        // Draw FPS in top-right corner of the viewport using ImGui DrawList (so it stays on top)
        if (Rt.Texture is { Width: > 0, Height: > 0 }) {

            const float fontSize = 26f;

            var fpsText = GetFPS().ToString();
            var textSize = CalcTextSize(fpsText) * (fontSize / GetFontSize());
            var padding = new Vector2(10, 5);

            // Draw on top of everything in this window
            var drawList = GetWindowDrawList();
            var pos = WindowPos + new Vector2(ContentRegion.X - textSize.X - padding.X, GetFrameHeight() + padding.Y);

            drawList.AddText(GetFont(), fontSize, pos + new Vector2(1, 1), ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 1)), fpsText); // Shadow
            drawList.AddText(GetFont(), fontSize, pos, ColorConvertFloat4ToU32(Colors.Primary.ToVector4()), fpsText);
        }

        Core.ShouldFocusActiveLevel = false;
    }
}
