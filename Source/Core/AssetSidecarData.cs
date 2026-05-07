internal class AssetSidecarData {

    public string GUID = "";
    public TextureImportSettings TextureImport = new();

    internal class TextureImportSettings : ICloneable {

        public int MaxSize = 0;
        public string ResizeFilter = "Bilinear";
        public string Compression = "Normal";

        public object Clone() => new TextureImportSettings {
            MaxSize = MaxSize,
            ResizeFilter = ResizeFilter,
            Compression = Compression
        };
    }
}
