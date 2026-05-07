internal abstract class Asset {

    public bool IsLoaded { get; protected set; }
    public string GUID { get; internal set; } = "";
    public string File { get; internal set; } = "";
    public Raylib_cs.Texture2D? Thumbnail { get; internal set; }
    public bool ThumbnailDirty { get; internal set; } = true;

    public virtual bool Load() => true;
    public virtual void Unload() { }
    public virtual IEnumerable<string> GetWatchedFiles() {

        yield return File;
    }

    public void InvalidateThumbnail() => ThumbnailDirty = true;
}
