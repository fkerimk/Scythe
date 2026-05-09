using System.Reflection;
using Scriban;

internal static class ScriptCompiler {
    public static Assembly? ProjectAssembly;

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
              <HintPath>{{ exe_path }}</HintPath>
            </Reference>
          </ItemGroup>
        </Project>
        """);
    
    private static bool _compiling;
    private static bool _queued;
    private static bool _pendingPlayModeRefresh;

    public static bool BuildProjectAssembly(bool loadIntoRuntime, out string scriptOutDll, out string error, BackgroundTask? task = null) {

        scriptOutDll = "";
        error = "";

        if (CommandLine.Runtime && loadIntoRuntime) {
            error = "Runtime mode cannot compile scripts.";
            return false;
        }
        var assemblyDir = Path.Combine(ScytheConfig.Current.Project, "Assembly");
        scriptOutDll = Path.Combine(assemblyDir, "Scripts.dll");


        var exePath = Path.Combine(AppContext.BaseDirectory, "Scythe.dll");
        var projectName = new DirectoryInfo(ScytheConfig.Current.Project).Name;
        var csprojPath = Path.Combine(ScytheConfig.Current.Project, $"{projectName}.csproj");
        var dirPropsPath = Path.Combine(ScytheConfig.Current.Project, "Directory.Build.props");

        EnsureBuildProjectFiles(dirPropsPath, csprojPath, exePath);

        if (File.Exists(scriptOutDll) && !NeedsCompile(scriptOutDll)) {

            if (loadIntoRuntime) LoadCompiledAssembly(scriptOutDll, assignAssetsOnMainThread: true);
            task?.Status = "Up-to-date";
            return true;
        }

        task?.Status = "Compiling...";

        var result = CommandRunner.Run("dotnet", [
            "build",
            csprojPath,
            "-c",
            "Release"
        ]);

        if (!File.Exists(scriptOutDll) || result.ExitCode != 0) {

            error = result.GetPreferredError("dotnet build failed.");
            return false;
        }

        if (loadIntoRuntime) LoadCompiledAssembly(scriptOutDll, assignAssetsOnMainThread: true);
        task?.Status = "Success";
        return true;
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
                    if (!BuildProjectAssembly(loadIntoRuntime: true, out _, out var error, task)) {
                        Console.WriteLine(error);
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

    public static void LoadRuntime() {
        
        var assemblyDir = Path.Combine(ScytheConfig.Current.Project, "Assembly");
        var dllPath = Path.Combine(assemblyDir, "Scripts.dll");

        if (!File.Exists(dllPath)) return;
        LoadCompiledAssembly(dllPath, assignAssetsOnMainThread: false);
    }

    private static void EnsureBuildProjectFiles(string dirPropsPath, string csprojPath, string exePath) {

        WriteTemplateIfMissing(dirPropsPath, DirectoryBuildPropsTemplate);
        WriteTemplateIfMissing(csprojPath, ScriptProjectTemplate, new { exe_path = exePath });
    }

    private static void WriteTemplateIfMissing(string path, Template template, object? model = null) {

        if (File.Exists(path)) return;
        File.WriteAllText(path, template.Render(model, member => member.Name));
    }

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

        foreach (var component in obj.Components.Values) {
            if (component is Script script) {
                script.PrepareForHotReload();
                script.UnloadAndQuit();
            }
        }

        foreach (var child in obj.Children.Values.ToArray()) ReloadScripts(child);
    }

    public static bool ConsumePendingPlayModeRefresh() {

        if (!_pendingPlayModeRefresh) return false;

        _pendingPlayModeRefresh = false;
        return true;
    }
}
