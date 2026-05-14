internal class AssetSidecarData {

    public string GUID = "";
    public TextureImportSettings TextureImport = new();
    public Dictionary<string, string> ScriptConfig = [];

    internal class TextureImportSettings : ICloneable {

        [RecordHistory]
        public int MaxSize = 0;
        [RecordHistory]
        public string ResizeFilter = "Bilinear";
        [RecordHistory]
        public string Format = "Source";
        [RecordHistory]
        public string Compression = "Balanced";
        [RecordHistory]
        public int Quality = 90;
        [RecordHistory]
        public string TextureFilter = "Bilinear";

        public object Clone() => ObjectGraph.DeepClone(this);
    }
}
