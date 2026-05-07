using System.Diagnostics;
using System.IO.Compression;
using static ImGuiNET.ImGui;

internal static class BuildPipeline {

    private const string BuildPopupId = "Build Runtime###BuildRuntime";

    private static bool _showBuildModal;
    private static bool _isBuilding;
    private static string _outputPath = "";

    public static void DrawMenu() {

        if (BeginMenu("Build")) {
            if (MenuItem("Build Runtime...")) OpenBuildModal();
            EndMenu();
        }

        DrawModal();
    }

    private static void OpenBuildModal() {

        _outputPath = GetDefaultOutputPath();
        _showBuildModal = true;
    }

    private static void DrawModal() {

        if (_showBuildModal) OpenPopup(BuildPopupId);

        if (!BeginPopupModal(BuildPopupId, ref _showBuildModal, ImGuiNET.ImGuiWindowFlags.AlwaysAutoResize)) return;

        TextWrapped("Tek dosya bir runtime exe uretir. Scriptler Assembly/Scripts.dll uzerinden, assetler proje dosyalari ve Imports cache'i uzerinden pakete gomulur.");
        Spacing();
        Text("Output exe");
        SetNextItemWidth(520);
        InputText("##BuildOutputPath", ref _outputPath, 1024);

        var canBuild = !_isBuilding && !string.IsNullOrWhiteSpace(_outputPath);

        if (!canBuild) BeginDisabled();
        if (Button("Build", new System.Numerics.Vector2(160, 0))) {
            StartBuild(_outputPath);
            _showBuildModal = false;
            CloseCurrentPopup();
        }
        if (!canBuild) EndDisabled();

        SameLine();
        if (Button("Cancel", new System.Numerics.Vector2(160, 0))) {
            _showBuildModal = false;
            CloseCurrentPopup();
        }

        EndPopup();
    }

    private static void StartBuild(string outputPath) {

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

                outputPath = NormalizeOutputPath(outputPath);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                Directory.CreateDirectory(tempRoot);

                var bundleZip = Path.Combine(tempRoot, "Bundle.zip");
                CreateBundle(bundleZip, outputPath, task);

                task.Status = "Publishing runtime...";
                var publishedExe = PublishRuntime(bundleZip, Path.Combine(tempRoot, "Publish"));

                File.Copy(publishedExe, outputPath, overwrite: true);
                task.Status = "Success";
                Notifications.Show($"Build saved: {Path.GetFileName(outputPath)}");

            } catch (Exception e) {
                task.Status = "Fail: " + e.Message;
                Notifications.Show($"Build failed: {e.Message}");

            } finally {
                SafeExec.Try(() => Directory.Delete(tempRoot, true));
                Tasks.RunOnMainThread(() => _isBuilding = false);
            }
        });
    }

    private static void CreateBundle(string bundleZip, string outputPath, BackgroundTask task) {

        task.Status = "Compiling scripts...";
        if (!ScriptCompiler.BuildProjectAssembly(loadIntoRuntime: false, out var scriptDll, out var error, task))
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Script build failed." : error);

        var bundleRoot = Path.Combine(Path.GetDirectoryName(bundleZip)!, "Bundle");
        var projectRoot = Path.Combine(bundleRoot, "Project");
        var resourcesRoot = Path.Combine(bundleRoot, "Resources");

        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(resourcesRoot);

        task.Status = "Packing project files...";
        CopyDirectory(PathUtil.GetResourcesRoot(), resourcesRoot, _ => true);
        CopyDirectory(
            ScytheConfig.Current.Project,
            projectRoot,
            file => ShouldIncludeProjectFile(file, outputPath)
        );

        if (!string.IsNullOrWhiteSpace(scriptDll) && File.Exists(scriptDll)) {
            var bundledDll = Path.Combine(projectRoot, "Assembly", "Scripts.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(bundledDll)!);
            File.Copy(scriptDll, bundledDll, overwrite: true);
        }

        if (File.Exists(bundleZip)) File.Delete(bundleZip);

        task.Status = "Compressing bundle...";
        ZipFile.CreateFromDirectory(bundleRoot, bundleZip, CompressionLevel.Optimal, includeBaseDirectory: false);
    }

    private static string PublishRuntime(string bundleZip, string publishDir) {

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
        processInfo.ArgumentList.Add("win-x64");
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

        var exe = Directory.GetFiles(publishDir, "*.exe", SearchOption.TopDirectoryOnly)
                           .OrderBy(path => path)
                           .FirstOrDefault();

        if (exe == null) throw new FileNotFoundException("Published exe not found.");
        return exe;
    }

    private static void CopyDirectory(string sourceDir, string destDir, Func<string, bool> includeFile) {

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories)) {
            if (!includeFile(file)) continue;

            var relative = Path.GetRelativePath(sourceDir, file);
            var destination = Path.Combine(destDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static bool ShouldIncludeProjectFile(string file, string outputPath) {

        var fullPath = Path.GetFullPath(file);
        var normalized = fullPath.Replace('\\', '/');
        var assemblySegment = "/Assembly/";
        var projectSegment = "/Project/";

        if (string.Equals(fullPath, outputPath, StringComparison.OrdinalIgnoreCase)) return false;
        if (normalized.Contains(assemblySegment, StringComparison.OrdinalIgnoreCase)) return false;
        if (normalized.Contains(projectSegment, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(Path.GetFileName(fullPath), "Directory.Build.props", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(Path.GetExtension(fullPath), ".csproj", StringComparison.OrdinalIgnoreCase)) return false;

        return true;
    }

    private static string NormalizeOutputPath(string outputPath) {

        outputPath = outputPath.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(outputPath)) throw new InvalidOperationException("Output exe path is required.");

        if (!outputPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) outputPath += ".exe";
        return Path.GetFullPath(outputPath);
    }

    private static string GetDefaultOutputPath() {

        var projectName = string.IsNullOrWhiteSpace(ProjectConfig.Current.Name)
            ? new DirectoryInfo(ScytheConfig.Current.Project).Name
            : ProjectConfig.Current.Name;

        return Path.Combine(ScytheConfig.Current.Project, "Build", $"{projectName}.exe");
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
}
