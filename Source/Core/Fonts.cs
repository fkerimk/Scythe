using System.Runtime.InteropServices;
using ImGuiNET;
using Raylib_cs;
using static System.Text.Encoding;
using static ImGuiNET.ImGui;
using static ImGuiNET.ImGuiNative;
using static Raylib_cs.Raylib;
using static rlImGui_cs.rlImGui;

internal static class Fonts {

    private const int SmallSize = 11, NormalSize = 16, LargeSize = 32;

    public static ImFontPtr ImMontserratRegular, ImFontAwesomeSmall, ImFontAwesomeNormal, ImFontAwesomeLarge;

    public static Font RlMontserratRegular, RlCascadiaCode, RlFontAwesome;

    private static ImFontConfigPtr _imFontConfigPtr;
    private static IntPtr          _iconRanges;

    public static unsafe void Init() {

        if (!CommandLine.Runtime) {

            _iconRanges = GCHandle.Alloc(new ushort[] { 0xE000, 0xF8FF, 0 }, GCHandleType.Pinned).AddrOfPinnedObject();

            _imFontConfigPtr             = ImFontConfig_ImFontConfig();
            _imFontConfigPtr.OversampleH = 3;
            _imFontConfigPtr.OversampleV = 3;

            ImMontserratRegular = LoadFont<ImFontPtr>("Fonts/montserrat-regular.otf");
            ImFontAwesomeSmall  = LoadFont<ImFontPtr>("Fonts/fa7-free-solid.otf", SmallSize,  true);
            ImFontAwesomeNormal = LoadFont<ImFontPtr>("Fonts/fa7-free-solid.otf", NormalSize, true);
            ImFontAwesomeLarge  = LoadFont<ImFontPtr>("Fonts/fa7-free-solid.otf", LargeSize,  true);

            ReloadFonts();
        }

        RlMontserratRegular = LoadFont<Font>("Fonts/montserrat-regular.otf");
        RlCascadiaCode      = LoadFont<Font>("Fonts/CascadiaCode-Regular.ttf");
        RlFontAwesome       = LoadFont<Font>("Fonts/fa7-free-solid.otf", NormalSize, true);
    }

    public static void UnloadRlFonts() {

        UnloadFont(RlMontserratRegular);
        UnloadFont(RlCascadiaCode);
        UnloadFont(RlFontAwesome);
    }

    private static unsafe T LoadFont<T>(string path, int size = NormalSize, bool useIconRanges = false) {

        if (!PathUtil.GetPath(path, out var fontPath)) throw new FileNotFoundException($"Font {path} not found");

        if (typeof(T) == typeof(Font)) {

            var codepoints = new List<int>();

            if (useIconRanges) {

                string[] icons = [
                    Icons.FaPlay, Icons.FaPause, Icons.FaStop, Icons.FaClock, Icons.FaFilm, Icons.FaCheck, Icons.FaXMark,
                    Icons.FaFile, Icons.FaFolder, Icons.FaLevelUp, Icons.FaDotCircleO, Icons.FaCube, Icons.FaArrowsAlt,
                    Icons.FaPlayCircle, Icons.FaArrows, Icons.FaLightbulbO, Icons.FaVideoCamera, Icons.FaCrosshairs,
                    Icons.FaCode, Icons.FaFileCode, Icons.FaSearch, Icons.FaPlus, Icons.FaMap, Icons.FaFlag,
                    Icons.FaHouse, Icons.FaFileImage, Icons.FaTrashAlt, Icons.FaWandMagicSparkles
                ];

                codepoints.AddRange(icons.Select(icon => char.ConvertToUtf32(icon, 0)).Distinct());

            } else {

                for (var i = 32; i < 127; i++) codepoints.Add(i); // Basic ASCII

                int[] turkishChars = [0x00c7, 0x00e7, 0x011e, 0x011f, 0x0130, 0x0131, 0x00d6, 0x00f6, 0x015e, 0x015f, 0x00dc, 0x00fc];

                codepoints.AddRange(turkishChars);
            }

            fixed (int* pCodepoints = codepoints.ToArray()) {

                var bytes = UTF8.GetBytes(fontPath);

                fixed (byte* pBytes = bytes) {

                    var font = LoadFontEx((sbyte*)pBytes, 96, pCodepoints, codepoints.Count);
                    SetTextureFilter(font.Texture, TextureFilter.Bilinear);

                    return (T)(object)font;
                }
            }
        }

        if (typeof(T) == typeof(ImFontPtr)) {

            return (T)(object)(useIconRanges ? GetIO().Fonts.AddFontFromFileTTF(fontPath, size, _imFontConfigPtr, _iconRanges) : GetIO().Fonts.AddFontFromFileTTF(fontPath, size, _imFontConfigPtr));
        }

        throw new NotSupportedException($"Unsupported font type: {typeof(T)}");
    }
}
