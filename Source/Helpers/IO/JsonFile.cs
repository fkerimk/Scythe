using Newtonsoft.Json;
#if !SCYTHE_RUNTIME_BUILD
using Polly;
using Polly.Retry;
using System.IO.Abstractions;
#endif

internal static class JsonFile {
#if !SCYTHE_RUNTIME_BUILD
    private static readonly IFileSystem FileSystem = new FileSystem();
    private static readonly ResiliencePipeline IoPolicy = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions {
            MaxRetryAttempts = 2,
            Delay = TimeSpan.FromMilliseconds(20),
            BackoffType = DelayBackoffType.Linear,
            ShouldHandle = new PredicateBuilder()
                .Handle<IOException>()
                .Handle<UnauthorizedAccessException>()
        })
        .Build();
#endif

    public static void PopulateInto(string path, object target) {
#if !SCYTHE_RUNTIME_BUILD
        if (!FileSystem.File.Exists(path)) return;
        IoPolicy.Execute(() => JsonConvert.PopulateObject(FileSystem.File.ReadAllText(path), target));
#else
        if (!File.Exists(path)) return;
        JsonConvert.PopulateObject(File.ReadAllText(path), target);
#endif
    }

    public static T ReadOrDefault<T>(string path, T fallback) {
#if !SCYTHE_RUNTIME_BUILD
        if (!FileSystem.File.Exists(path)) return fallback;
        return IoPolicy.Execute(() => JsonConvert.DeserializeObject<T>(FileSystem.File.ReadAllText(path)) ?? fallback);
#else
        if (!File.Exists(path)) return fallback;
        return JsonConvert.DeserializeObject<T>(File.ReadAllText(path)) ?? fallback;
#endif
    }

    public static void WriteIndented(string path, object value, bool ensureDirectory = true) {
        if (ensureDirectory) {
#if !SCYTHE_RUNTIME_BUILD
            var directory = FileSystem.Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !FileSystem.Directory.Exists(directory))
                FileSystem.Directory.CreateDirectory(directory);
#else
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
#endif
        }

#if !SCYTHE_RUNTIME_BUILD
        IoPolicy.Execute(() => FileSystem.File.WriteAllText(path, JsonConvert.SerializeObject(value, Formatting.Indented)));
#else
        File.WriteAllText(path, JsonConvert.SerializeObject(value, Formatting.Indented));
#endif
    }
}
