internal class AssetSidecarData {

    public string GUID = "";
    public TextureImportSettings TextureImport = new();
    public Dictionary<string, string> ScriptConfig = [];

    internal class TextureImportSettings : ICloneable {

        public int MaxSize = 0;
        public string ResizeFilter = "Bilinear";
        public string Format = "Source";
        public string Compression = "Balanced";
        public int Quality = 90;
        public string TextureFilter = "Bilinear";

        public object Clone() => new TextureImportSettings {
            MaxSize = MaxSize,
            ResizeFilter = ResizeFilter,
            Format = Format,
            Compression = Compression,
            Quality = Quality,
            TextureFilter = TextureFilter
        };
    }
}
