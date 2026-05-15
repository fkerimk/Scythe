using System.Numerics;

internal class AnimationAsset : Asset {

    public List<AnimationClip> Animations = [];
    private ModelAsset.ModelSettings _settings = new();

    public override bool Load() {

        ImportedFile = "";
        if (!System.IO.File.Exists(File)) return false;

        try {

            var jsonPath = File + ".json";
            if (System.IO.File.Exists(jsonPath)) {

                var settings = JsonFile.ReadOrDefault(jsonPath, new ModelAsset.ModelSettings());
                _settings = settings;
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
                if (changed) JsonFile.WriteIndented(jsonPath, settings);
            } else
                _settings = new ModelAsset.ModelSettings();

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
#if !SCYTHE_RUNTIME_BUILD
            Animations = ModelAsset.BuildAnimationClips(AssimpLoader.Load(File).Animations, _settings);
            return true;
#else
            return false;
#endif
        }

        if (TryLoadCompiledAnimations(ResolvedFile)) return true;

        AssetManager.DeleteImportedCache(this);
        ImportedFile = AssetManager.GetImportedModelFile(File, GUID);

        if (string.Equals(Path.GetExtension(ResolvedFile), ".scymodel", StringComparison.OrdinalIgnoreCase) && TryLoadCompiledAnimations(ResolvedFile))
            return true;

#if !SCYTHE_RUNTIME_BUILD
        Animations = ModelAsset.BuildAnimationClips(AssimpLoader.Load(File).Animations, _settings);
        return true;
#else
        return false;
#endif
    }

    private bool TryLoadCompiledAnimations(string cacheFile) {

        if (!CompiledAssetCache.LoadModel(cacheFile, out _, out _, out _, out _, out var compiledAnimations))
            return false;

        Animations = ModelAsset.BuildAnimationClips(compiledAnimations, _settings);
        return true;
    }
}
