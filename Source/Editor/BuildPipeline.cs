using System.Diagnostics;
using System.IO.Compression;
using NativeFileDialogNET;
using Newtonsoft.Json;
using static ImGuiNET.ImGui;

internal static class BuildPipeline {

    private const string BuildPopupId = "Build Runtime###BuildRuntime";

    private static bool _showBuildModal;
    private static bool _isBuilding;
    private static string _outputDirectory = "";
    private static bool _buildWindows = true;
    private static bool _buildLinux;

    public static void DrawMenu() {

        if (BeginMenu("Build")) {
            if (MenuItem("Build Runtime...")) OpenBuildModal();
            EndMenu();
        }

        DrawModal();
    }

    private static void OpenBuildModal() {

        LoadSettings();
        _showBuildModal = true;
    }

    private static void DrawModal() {

        if (_showBuildModal) OpenPopup(BuildPopupId);

        if (!Modal.Begin(BuildPopupId, ref _showBuildModal)) return;

        TextWrapped("Build a single runtime executable. Scripts are bundled from Assembly/Scripts.dll, and assets are packed from the project files and the Imports cache.");
        Spacing();
        Text("Output folder");
        SetNextItemWidth(420);
        if (InputText("##BuildOutputDirectory", ref _outputDirectory, 1024)) SaveSettings();
        SameLine();
        if (Button("Browse...", new System.Numerics.Vector2(100, 0))) {
            var selectedDirectory = BrowseForOutputDirectory(_outputDirectory);
            if (!string.IsNullOrWhiteSpace(selectedDirectory)) {
                _outputDirectory = selectedDirectory;
                SaveSettings();
            }
        }

        Spacing();
        if (Checkbox("Windows", ref _buildWindows)) SaveSettings();
        SameLine();
        if (Checkbox("Linux", ref _buildLinux)) SaveSettings();

        Spacing();
        foreach (var platform in GetSelectedPlatforms())
            TextWrapped($"Output: {Path.Combine(string.IsNullOrWhiteSpace(_outputDirectory) ? "." : _outputDirectory, GetRuntimeFileName(platform))}");

        var canBuild = !_isBuilding && !string.IsNullOrWhiteSpace(_outputDirectory) && GetSelectedPlatforms().Count > 0;

        if (!canBuild) BeginDisabled();
        if (Button("Build", new System.Numerics.Vector2(160, 0))) {
            StartBuild(_outputDirectory);
            _showBuildModal = false;
            CloseCurrentPopup();
        }
        if (!canBuild) EndDisabled();

        SameLine();
        if (Button("Cancel", new System.Numerics.Vector2(160, 0))) {
            _showBuildModal = false;
            CloseCurrentPopup();
        }

        Modal.End();
    }

    private static void StartBuild(string outputDirectory) {

        _isBuilding = true;

        Tasks.Run("Build Runtime", task => {

            var tempRoot = Path.Combine(Path.GetTempPath(), "ScytheBuild", Guid.NewGuid().ToString("N"));
            using var prepDone = new System.Threading.ManualResetEventSlim(false);

            try {
                task.Status = "Saving level...";
                Tasks.RunOnMainThread(() => {
                    try {
                        Core.SaveAllDirtyLevels();
                        AssetManager.Init();
                    } finally {
                        prepDone.Set();
                    }
                });
                prepDone.Wait();

                outputDirectory = NormalizeOutputDirectory(outputDirectory);
                Directory.CreateDirectory(tempRoot);
                SaveSettings();

                var bundleZip = Path.Combine(tempRoot, "Bundle.zip");
                CreateBundle(bundleZip, outputDirectory, task);

                var platforms = GetSelectedPlatforms();
                for (var i = 0; i < platforms.Count; i++) {
                    var platform = platforms[i];
                    var outputPath = Path.Combine(outputDirectory, GetRuntimeFileName(platform));
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

                    task.Status = $"Publishing {platform.DisplayName} ({i + 1}/{platforms.Count})...";
                    var publishedBinary = PublishRuntime(bundleZip, Path.Combine(tempRoot, platform.RuntimeId), platform.RuntimeId);

                    File.Copy(publishedBinary, outputPath, overwrite: true);
                }

                task.Status = "Success";
                Notifications.Show($"Build completed: {platforms.Count} target(s)");

            } catch (Exception e) {
                task.Status = "Fail: " + e.Message;
                Notifications.Show($"Build failed: {e.Message}");

            } finally {
                SafeExec.Try(() => Directory.Delete(tempRoot, true));
                Tasks.RunOnMainThread(() => _isBuilding = false);
            }
        });
    }

    private static void CreateBundle(string bundleZip, string outputDirectory, BackgroundTask task) {

        task.Status = "Compiling scripts...";
        if (!ScriptCompiler.BuildProjectAssembly(loadIntoRuntime: false, out var scriptDll, out var error, task))
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Script build failed." : error);

        var bundleRoot = Path.Combine(Path.GetDirectoryName(bundleZip)!, "Bundle");
        var projectRoot = Path.Combine(bundleRoot, "Project");
        var builtInRoot = Path.Combine(bundleRoot, "Collection");

        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(builtInRoot);

        task.Status = "Packing project files...";
        CopyDirectory(PathUtil.GetBuiltInCollectionRoot(), builtInRoot, _ => true);
        CopyDirectory(
            ScytheConfig.Current.Project,
            projectRoot,
            file => ShouldIncludeProjectFile(file, outputDirectory)
        );

        if (!string.IsNullOrWhiteSpace(scriptDll) && File.Exists(scriptDll)) {
            var bundledDll = Path.Combine(projectRoot, "Assembly", "Scripts.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(bundledDll)!);
            File.Copy(scriptDll, bundledDll, overwrite: true);
        }

        if (File.Exists(bundleZip)) File.Delete(bundleZip);

        task.Status = "Compressing bundle...";
        ZipFile.CreateFromDirectory(bundleRoot, bundleZip, CompressionLevel.SmallestSize, includeBaseDirectory: false);
    }

    private static string PublishRuntime(string bundleZip, string publishDir, string runtimeId) {

        Directory.CreateDirectory(publishDir);

        var projectFile = FindEngineProjectFile();
        var processInfo = new ProcessStartInfo {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        processInfo.ArgumentList.Add("publish");
        processInfo.ArgumentList.Add(projectFile);
        processInfo.ArgumentList.Add("-c");
        processInfo.ArgumentList.Add("Release");
        processInfo.ArgumentList.Add("-r");
        processInfo.ArgumentList.Add(runtimeId);
        processInfo.ArgumentList.Add("-o");
        processInfo.ArgumentList.Add(publishDir);
        processInfo.ArgumentList.Add("-p:ScytheRuntimeBuild=true");
        processInfo.ArgumentList.Add($"-p:ScytheBundlePath={bundleZip}");
        processInfo.ArgumentList.Add("-p:DebugSymbols=false");
        processInfo.ArgumentList.Add("-p:DebugType=None");

        using var process = Process.Start(processInfo);
        process?.WaitForExit();

        if (process?.ExitCode != 0) {
            var stdOut = process?.StandardOutput.ReadToEnd() ?? "";
            var stdErr = process?.StandardError.ReadToEnd() ?? "";
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stdErr) ? stdOut : stdErr);
        }

        var publishedBinary = FindPublishedBinary(publishDir, runtimeId);

        if (publishedBinary == null) throw new FileNotFoundException($"Published binary not found for {runtimeId}.");
        return publishedBinary;
    }

    private static void CopyDirectory(string sourceDir, string destDir, Func<string, bool> includeFile) {

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories)) {
            if (!includeFile(file)) continue;

            var relative = Path.GetRelativePath(sourceDir, file);
            var destination = Path.Combine(destDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            
            var ext = Path.GetExtension(file).ToLowerInvariant();
            var isImport = file.Contains("/Imports/", StringComparison.OrdinalIgnoreCase) || file.Contains("\\Imports\\", StringComparison.OrdinalIgnoreCase);
            var isBuiltIn = file.Contains("/Collection/", StringComparison.OrdinalIgnoreCase) || file.Contains("\\Collection\\", StringComparison.OrdinalIgnoreCase);
            
            if (!isImport && !isBuiltIn && ext is ".fbx" or ".obj" or ".gltf" or ".png" or ".jpg" or ".jpeg" or ".tga" or ".bmp" or ".webp" or ".avif" or ".cs") {
                File.WriteAllBytes(destination, []);
            } else {
                File.Copy(file, destination, overwrite: true);
            }
        }
    }

    private static bool ShouldIncludeProjectFile(string file, string outputDirectory) {

        var fullPath = Path.GetFullPath(file);
        var normalized = fullPath.Replace('\\', '/');
        var assemblySegment = "/Assembly/";
        var projectSegment = "/Project/";
        var outputRoot = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (fullPath.StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase)) return false;
        if (normalized.Contains(assemblySegment, StringComparison.OrdinalIgnoreCase)) return false;
        if (normalized.Contains(projectSegment, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(Path.GetFileName(fullPath), "Directory.Build.props", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(Path.GetExtension(fullPath), ".csproj", StringComparison.OrdinalIgnoreCase)) return false;

        return true;
    }

    private static string NormalizeOutputDirectory(string outputDirectory) {

        outputDirectory = outputDirectory.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(outputDirectory)) throw new InvalidOperationException("Output folder is required.");

        return Path.GetFullPath(outputDirectory);
    }

    private static string GetDefaultOutputDirectory() {

        return Path.Combine(ScytheConfig.Current.Project, "Build");
    }

    private static string GetRuntimeFileName(BuildTarget platform) {

        var projectName = string.IsNullOrWhiteSpace(ProjectConfig.Current.Name)
            ? new DirectoryInfo(ScytheConfig.Current.Project).Name
            : ProjectConfig.Current.Name;

        return platform.RuntimeId switch {
            "win-x64" => $"{projectName}-windows.exe",
            "linux-x64" => $"{projectName}-linux",
            _ => $"{projectName}-{platform.RuntimeId}"
        };
    }

    private static string? BrowseForOutputDirectory(string initialDirectory) {

        try {
            using var dialog = new NativeFileDialog().SelectFolder();
            var initialPath = string.IsNullOrWhiteSpace(initialDirectory) ? GetDefaultOutputDirectory() : initialDirectory;
            var result = dialog.Open(out string[]? output, initialPath);

            if (result != DialogResult.Okay || output == null || output.Length == 0) return null;

            return output[0];

        } catch (Exception e) {
            Notifications.Show($"Folder dialog failed: {e.Message}");
            return null;
        }
    }

    private static string? FindPublishedBinary(string publishDir, string runtimeId) {

        var engineName = Path.GetFileNameWithoutExtension(FindEngineProjectFile());
        var expectedName = runtimeId.StartsWith("win-", StringComparison.OrdinalIgnoreCase) ? engineName + ".exe" : engineName;
        var expectedPath = Path.Combine(publishDir, expectedName);

        if (File.Exists(expectedPath)) return expectedPath;

        return Directory.GetFiles(publishDir, "*", SearchOption.TopDirectoryOnly)
            .Where(path => !path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path)
            .FirstOrDefault();
    }

    private static List<BuildTarget> GetSelectedPlatforms() {

        var targets = new List<BuildTarget>();

        if (_buildWindows) targets.Add(new BuildTarget("Windows", "win-x64"));
        if (_buildLinux) targets.Add(new BuildTarget("Linux", "linux-x64"));

        return targets;
    }

    private static string GetSettingsPath() => Path.Combine(ScytheConfig.Current.Project, "Project", "BuildSettings.json");

    private static void LoadSettings() {

        var path = GetSettingsPath();
        var settings = File.Exists(path)
            ? JsonConvert.DeserializeObject<BuildSettings>(File.ReadAllText(path)) ?? new BuildSettings()
            : new BuildSettings();

        _outputDirectory = string.IsNullOrWhiteSpace(settings.OutputDirectory) ? GetDefaultOutputDirectory() : settings.OutputDirectory;
        _buildWindows = settings.BuildWindows;
        _buildLinux = settings.BuildLinux;
    }

    private static void SaveSettings() {

        var path = GetSettingsPath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var settings = new BuildSettings {
            OutputDirectory = _outputDirectory,
            BuildWindows = _buildWindows,
            BuildLinux = _buildLinux
        };

        File.WriteAllText(path, JsonConvert.SerializeObject(settings, Formatting.Indented));
    }

    private static string FindEngineProjectFile() {

        var candidates = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };

        foreach (var start in candidates) {

            var current = new DirectoryInfo(start);
            while (current != null) {

                var candidate = Path.Combine(current.FullName, "Scythe.csproj");
                if (File.Exists(candidate)) return candidate;
                current = current.Parent;
            }
        }

        throw new FileNotFoundException("Scythe.csproj could not be located.");
    }

    private sealed record BuildTarget(string DisplayName, string RuntimeId);

    private sealed class BuildSettings {

        public string OutputDirectory { get; init; } = "";
        public bool BuildWindows { get; init; } = true;
        public bool BuildLinux { get; init; }
    }
}
