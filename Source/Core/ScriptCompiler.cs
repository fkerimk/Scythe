using System.Diagnostics;
using System.Reflection;

internal static class ScriptCompiler {
    public static Assembly? ProjectAssembly;
    
    private static bool _compiling;
    private static bool _queued;

    public static bool BuildProjectAssembly(bool loadIntoRuntime, out string scriptOutDll, out string error, BackgroundTask? task = null) {

        scriptOutDll = "";
        error = "";

        if (CommandLine.Runtime && loadIntoRuntime) {
            error = "Runtime mode cannot compile scripts.";
            return false;
        }

        var scriptsDir = Path.Combine(ScytheConfig.Current.Project, "Scripts");
        var assemblyDir = Path.Combine(ScytheConfig.Current.Project, "Assembly");
        scriptOutDll = Path.Combine(assemblyDir, "Scripts.dll");

        if (!Directory.Exists(scriptsDir)) return true;

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

        var processInfo = new ProcessStartInfo {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        processInfo.ArgumentList.Add("build");
        processInfo.ArgumentList.Add(csprojPath);
        processInfo.ArgumentList.Add("-c");
        processInfo.ArgumentList.Add("Release");

        using var process = Process.Start(processInfo);
        process?.WaitForExit();

        if (!File.Exists(scriptOutDll) || process?.ExitCode != 0) {

            var stdOut = process?.StandardOutput.ReadToEnd() ?? "";
            var stdErr = process?.StandardError.ReadToEnd() ?? "";
            error = string.IsNullOrWhiteSpace(stdErr) ? stdOut : stdErr;
            if (string.IsNullOrWhiteSpace(error)) error = "dotnet build failed.";
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
                var scriptsDir = Path.Combine(ScytheConfig.Current.Project, "Scripts");

                if (!Directory.Exists(scriptsDir)) {

                    Tasks.RunOnMainThread(() => { _compiling = false; });
                    return;
                }

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

        if (!File.Exists(dirPropsPath)) {
            File.WriteAllText(dirPropsPath, """
                                           <Project>
                                             <PropertyGroup>
                                               <BaseIntermediateOutputPath>Assembly\obj\</BaseIntermediateOutputPath>
                                               <MSBuildProjectExtensionsPath>Assembly\obj\</MSBuildProjectExtensionsPath>
                                             </PropertyGroup>
                                           </Project>
                                           """);
        }

        if (!File.Exists(csprojPath)) {
            File.WriteAllText(csprojPath, $"""
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
                                                 <HintPath>{exePath}</HintPath>
                                               </Reference>
                                             </ItemGroup>
                                           </Project>
                                           """);
        }
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
        }

        if (assignAssetsOnMainThread) Tasks.RunOnMainThread(Assign);
        else Assign();
    }
}
