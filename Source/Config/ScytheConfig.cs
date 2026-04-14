using Newtonsoft.Json;

internal class ScytheConfig {

    [JsonIgnore] public static ScytheConfig Current = new();
    public string Project = null!;
}

internal static class OldConfig {

    public static class Runtime {

        public static int FpsLock = -1;
        public static bool DrawFps = false;
        public static bool DrawLights = false;
        public static bool NoShade = false;
        public static int PbrAlbedo = 1;
        public static int PbrNormal = 1;
        public static int PbrMra = 1;
        public static int PbrEmissive = 1;
        public static bool GenTangents = true;
    }

    public static class Editor {

        public static int FpsLock = -1;
        public static bool DrawFps = true;
        public static bool DrawLights = true;
        public static bool NoShade = false;
        public static int PbrAlbedo = 1;
        public static int PbrNormal = 1;
        public static int PbrMra = 1;
        public static int PbrEmissive = 1;
        public static bool GenTangents = true;
    }
}