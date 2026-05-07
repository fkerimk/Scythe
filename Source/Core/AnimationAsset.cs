using System.Numerics;

internal class AnimationAsset : Asset {

    public List<AnimationClip> Animations = [];

    public override bool Load() {

        ImportedFile = "";
        if (!System.IO.File.Exists(File)) return false;

        try {

            var jsonPath = File + ".json";
            if (System.IO.File.Exists(jsonPath)) {

                var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<ModelAsset.ModelSettings>(System.IO.File.ReadAllText(jsonPath)) ?? new ModelAsset.ModelSettings();
                var changed = false;
                if (string.IsNullOrWhiteSpace(settings.AnimationGUID)) {

                    settings.AnimationGUID = System.Guid.NewGuid().ToString("N");
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(settings.GUID)) {

                    settings.GUID = System.Guid.NewGuid().ToString("N");
                    changed = true;
                }

                GUID = settings.AnimationGUID;
                if (changed) System.IO.File.WriteAllText(jsonPath, Newtonsoft.Json.JsonConvert.SerializeObject(settings, Newtonsoft.Json.Formatting.Indented));
            }

            ImportedFile = AssetManager.GetImportedModelFile(File, GUID);
            if (!TryLoadImportedOrRebuild()) return false;

        } catch {

            return false;
        }

        IsLoaded = true;

        return true;
    }

    public override void Unload() => Animations.Clear();

    public override IEnumerable<string> GetWatchedFiles() {

        yield return File;
        yield return File + ".json";
    }

    private bool TryLoadImportedOrRebuild() {

        if (!string.Equals(Path.GetExtension(ResolvedFile), ".scymodel", StringComparison.OrdinalIgnoreCase)) {
            Animations = AssimpLoader.Load(File).Animations;
            return true;
        }

        if (TryLoadCompiledAnimations(ResolvedFile)) return true;

        AssetManager.DeleteImportedCache(this);
        ImportedFile = AssetManager.GetImportedModelFile(File, GUID);

        if (string.Equals(Path.GetExtension(ResolvedFile), ".scymodel", StringComparison.OrdinalIgnoreCase) && TryLoadCompiledAnimations(ResolvedFile))
            return true;

        Animations = AssimpLoader.Load(File).Animations;
        return true;
    }

    private bool TryLoadCompiledAnimations(string cacheFile) {

        if (!CompiledAssetCache.LoadModel(cacheFile, out _, out _, out _, out _, out var compiledAnimations))
            return false;

        Animations = compiledAnimations;
        return true;
    }
}
