using Newtonsoft.Json;
using Polly;
using Polly.Retry;
using System.IO.Abstractions;

internal static class JsonFile {
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

    public static void PopulateInto(string path, object target) {
        if (!FileSystem.File.Exists(path)) return;
        IoPolicy.Execute(() => JsonConvert.PopulateObject(FileSystem.File.ReadAllText(path), target));
    }

    public static T ReadOrDefault<T>(string path, T fallback) {
        if (!FileSystem.File.Exists(path)) return fallback;
        return IoPolicy.Execute(() => JsonConvert.DeserializeObject<T>(FileSystem.File.ReadAllText(path)) ?? fallback);
    }

    public static void WriteIndented(string path, object value, bool ensureDirectory = true) {
        if (ensureDirectory) {
            var directory = FileSystem.Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !FileSystem.Directory.Exists(directory))
                FileSystem.Directory.CreateDirectory(directory);
        }

        IoPolicy.Execute(() => FileSystem.File.WriteAllText(path, JsonConvert.SerializeObject(value, Formatting.Indented)));
    }
}
