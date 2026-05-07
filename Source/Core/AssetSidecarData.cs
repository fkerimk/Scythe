internal class AssetSidecarData {

    public string GUID = "";
    public TextureImportSettings TextureImport = new();

    internal class TextureImportSettings : ICloneable {

        public int MaxSize = 0;
        public string ResizeFilter = "Bilinear";
        public string Format = "Source";
        public string Compression = "Balanced";
        public int Quality = 90;

        public object Clone() => new TextureImportSettings {
            MaxSize = MaxSize,
            ResizeFilter = ResizeFilter,
            Format = Format,
            Compression = Compression,
            Quality = Quality
        };
    }
}
