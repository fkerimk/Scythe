using System.Reflection;
#if !SCYTHE_RUNTIME_BUILD
using FluentResults;
using Scriban;
#endif

internal static class ScriptCompiler {
    public static Assembly? ProjectAssembly;

#if !SCYTHE_RUNTIME_BUILD
    private static readonly Template DirectoryBuildPropsTemplate = Template.Parse("""
        <Project>
          <PropertyGroup>
            <BaseIntermediateOutputPath>Assembly\obj\</BaseIntermediateOutputPath>
            <MSBuildProjectExtensionsPath>Assembly\obj\</MSBuildProjectExtensionsPath>
          </PropertyGroup>
        </Project>
        """);

    private static readonly Template ScriptProjectTemplate = Template.Parse("""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <ProduceReferenceAssembly>false</ProduceReferenceAssembly>
            <BaseOutputPath>Assembly\bin\</BaseOutputPath>
            <OutputPath>Assembly</OutputPath>
            <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
            <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
            <AssemblyName>Scripts</AssemblyName>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Raylib-cs" Version="7.0.2" />
            <Reference Include="Scythe">
              <HintPath>{{ exePath }}</HintPath>
            </Reference>
          </ItemGroup>
        </Project>
        """);
#endif
    
#if !SCYTHE_RUNTIME_BUILD
    private static bool _compiling;
    private static bool _queued;
#endif
    private static bool _pendingPlayModeRefresh;

#if !SCYTHE_RUNTIME_BUILD
    public static Result<string> BuildProjectAssembly(bool loadIntoRuntime, BackgroundTask? task = null) {

        if (CommandLine.Runtime && loadIntoRuntime) {
            return Result.Fail("Runtime mode cannot compile scripts.");
        }

        var assemblyDir = Path.Combine(ScytheConfig.Current.Project, "Assembly");
        var scriptOutDll = Path.Combine(assemblyDir, "Scripts.dll");

        var assemblyName = Assembly.GetExecutingAssembly().GetName().Name ?? "Scythe";
        var exePath = Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll");
        if (!File.Exists(exePath))
            exePath = Assembly.GetExecutingAssembly().Location;
        var projectName = new DirectoryInfo(ScytheConfig.Current.Project).Name;
        var csprojPath = Path.Combine(ScytheConfig.Current.Project, $"{projectName}.csproj");
        var dirPropsPath = Path.Combine(ScytheConfig.Current.Project, "Directory.Build.props");

        EnsureBuildProjectFiles(dirPropsPath, csprojPath, exePath);

        if (File.Exists(scriptOutDll) && !NeedsCompile(scriptOutDll)) {

            if (loadIntoRuntime) LoadCompiledAssembly(scriptOutDll, assignAssetsOnMainThread: true);
            task?.Status = "Up-to-date";
            return Result.Ok(scriptOutDll);
        }

        task?.Status = "Compiling...";

        var result = CommandRunner.Run("dotnet", [
            "build",
            csprojPath,
            "-c",
            "Release"
        ]);

        if (!File.Exists(scriptOutDll) || result.ExitCode != 0) {
            return Result.Fail(result.GetPreferredError("dotnet build failed."));
        }

        if (loadIntoRuntime) LoadCompiledAssembly(scriptOutDll, assignAssetsOnMainThread: true);
        task?.Status = "Success";
        return Result.Ok(scriptOutDll);
    }
    
    public static void CompileProject() {
        
        if (CommandLine.Runtime) return;

        if (_compiling) {
            
            _queued = true;
            return;
        }

        _compiling = true;
        _queued = false;

        Tasks.Run("Compile Project Scripts", task => {

            try {

                while (true) {

                    _queued = false;
                    var buildResult = BuildProjectAssembly(loadIntoRuntime: true, task);
                    if (buildResult.IsFailed) {
                        Console.WriteLine(buildResult.Errors.FirstOrDefault()?.Message ?? "Script build failed.");
                        task.Status = "Fail";
                        break;
                    }

                    if (_queued) continue;
                    break;
                }

                Tasks.RunOnMainThread(() => { _compiling = false; });

            } catch (Exception e) {
                
                task.Status = "Fail: " + e.Message;
                Tasks.RunOnMainThread(() => { _compiling = false; });
            }
        });
    }
#endif

    public static void LoadRuntime() {
        
        var assemblyDir = Path.Combine(ScytheConfig.Current.Project, "Assembly");
        var dllPath = Path.Combine(assemblyDir, "Scripts.dll");

        if (!File.Exists(dllPath)) return;
        LoadCompiledAssembly(dllPath, assignAssetsOnMainThread: false);
    }

#if !SCYTHE_RUNTIME_BUILD
    private static void EnsureBuildProjectFiles(string dirPropsPath, string csprojPath, string exePath) {

        WriteTemplateIfOutdated(dirPropsPath, DirectoryBuildPropsTemplate);
        WriteTemplateIfOutdated(csprojPath, ScriptProjectTemplate, new { exePath });
    }

    private static void WriteTemplateIfOutdated(string path, Template template, object? model = null) {

        var rendered = template.Render(model, member => member.Name);

        if (File.Exists(path)) {
            var existing = File.ReadAllText(path);
            if (string.Equals(existing, rendered, StringComparison.Ordinal)) return;
        }

        File.WriteAllText(path, rendered);
    }
#endif

    private static bool NeedsCompile(string scriptOutDll) {

        var dllTime = File.GetLastWriteTime(scriptOutDll);
        var csFiles = Directory.GetFiles(ScytheConfig.Current.Project, "*.cs", SearchOption.AllDirectories);
        return csFiles.Where(f => !f.Contains(Path.DirectorySeparatorChar + "Assembly" + Path.DirectorySeparatorChar) && !f.Contains("/Assembly/"))
                      .Any(f => File.GetLastWriteTime(f) > dllTime);
    }

    private static void LoadCompiledAssembly(string dllPath, bool assignAssetsOnMainThread) {

        var bytes = File.ReadAllBytes(dllPath);
        var asm = Assembly.Load(bytes);

        void Assign() {
            ProjectAssembly = asm;

            foreach (var asset in AssetManager.GetAll<ScriptAsset>()) {
                asset.Unload();
                asset.AssignFromAssembly();
            }

            // Hot reload: force all Script components to recreate their instances from the new assembly
            if (Core.IsPlaying) {
                BackgroundScripts.PrepareForHotReload();
                ReloadAllScriptInstances();
                _pendingPlayModeRefresh = true;
                Notifications.Show("Scripts Hot Reloaded");
            }
        }

        if (assignAssetsOnMainThread) Tasks.RunOnMainThread(Assign);
        else Assign();
    }

    /// <summary>
    /// Forces all Script components in open levels to drop their current instances
    /// so that Core.Load() recreates them from the latest compiled assembly on the next frame.
    /// </summary>
    private static void ReloadAllScriptInstances() {

        foreach (var level in Core.OpenLevels)
            ReloadScripts(level.Root);
    }

    private static void ReloadScripts(Obj obj) {

        foreach (var component in obj.ComponentEntries.Values) {
            if (component is Script script) {
                script.PrepareForHotReload();
                script.UnloadAndQuit();
            }
        }

        foreach (var child in obj.ChildEntries.Values.ToArray()) ReloadScripts(child);
    }

    public static bool ConsumePendingPlayModeRefresh() {

        if (!_pendingPlayModeRefresh) return false;

        _pendingPlayModeRefresh = false;
        return true;
    }
}
