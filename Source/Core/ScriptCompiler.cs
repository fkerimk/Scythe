using System.Diagnostics;
using System.Reflection;

internal static class ScriptCompiler {
    public static Assembly? ProjectAssembly;
    
    private static bool _compiling;
    private static bool _queued;

    public static void CompileProject() {
        
        if (CommandLine.Runtime) return;

        if (_compiling) {
            
            _queued = true;
            return;
        }

        _compiling = true;
        _queued = false;

        Tasks.Run("Compile Project Scripts", task => {

            task.Status = "Compiling...";
            
            try {
                
                var scriptsDir = Path.Combine(ScytheConfig.Current.Project, "Scripts");
                
                if (!Directory.Exists(scriptsDir)) {
                    
                    Tasks.RunOnMainThread(() => { _compiling = false; });
                    return;
                }

                var exePath = Path.Combine(AppContext.BaseDirectory, "Scythe.dll");
                var assemblyDir = Path.Combine(ScytheConfig.Current.Project, "Assembly");
                var scriptOutDll = Path.Combine(assemblyDir, "Scripts.dll");
                var projectName = new DirectoryInfo(ScytheConfig.Current.Project).Name;
                var csprojPath = Path.Combine(ScytheConfig.Current.Project, $"{projectName}.csproj");
                var dirPropsPath = Path.Combine(ScytheConfig.Current.Project, "Directory.Build.props");
                
                // Write/Update IDE project file if it doesn't exist
                if (!File.Exists(dirPropsPath)) {
                    File.WriteAllText(dirPropsPath, $"""
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

                // Cache check: Avoid redundant compiling
                if (File.Exists(scriptOutDll)) {
                    
                    var dllTime = File.GetLastWriteTime(scriptOutDll);

                    var csFiles = Directory.GetFiles(ScytheConfig.Current.Project, "*.cs", SearchOption.AllDirectories);

                    var needsCompile = csFiles.Where(f => !f.Contains(Path.DirectorySeparatorChar + "Assembly" + Path.DirectorySeparatorChar) && !f.Contains("/Assembly/")).Any(f => File.GetLastWriteTime(f) > dllTime);

                    if (!needsCompile) {
                        
                        Tasks.RunOnMainThread(() => {
                            
                            LoadRuntime();
                            
                            _compiling = false;
                            
                            foreach (var asset in AssetManager.GetAll<ScriptAsset>()) {
                                
                                asset.Unload();
                                asset.AssignFromAssembly();
                            }
                        });
                        
                        task.Status = "Up-to-date";
                        return;
                    }
                }

                var processInfo = new ProcessStartInfo {
                    
                    FileName = "dotnet",
                    Arguments = $"build \"{csprojPath}\" -c Release",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                process?.WaitForExit();

                if (File.Exists(scriptOutDll) && process?.ExitCode == 0) {
                    var bytes = File.ReadAllBytes(scriptOutDll);
                    var asm = Assembly.Load(bytes);

                    Tasks.RunOnMainThread(() => {
                        
                        ProjectAssembly = asm;
                        _compiling = false;

                        // Notify all ScriptAssets to reload using the new assembly
                        foreach (var asset in AssetManager.GetAll<ScriptAsset>()) {
                            
                            asset.Unload();
                            asset.AssignFromAssembly();
                        }

                        if (_queued) CompileProject();
                    });
                    task.Status = "Completed";
                    
                } else {
                    
                    Console.WriteLine(process?.StandardOutput.ReadToEnd());
                    Console.WriteLine(process?.StandardError.ReadToEnd());
                    task.Status = "Build Failed";
                    Tasks.RunOnMainThread(() => { _compiling = false; if (_queued) CompileProject(); });
                }

            } catch (Exception e) {
                
                task.Status = "Error: " + e.Message;
                Tasks.RunOnMainThread(() => { _compiling = false; if (_queued) CompileProject(); });
            }
        });
    }

    public static void LoadRuntime() {
        
        var assemblyDir = Path.Combine(ScytheConfig.Current.Project, "Assembly");
        var dllPath = Path.Combine(assemblyDir, "Scripts.dll");

        if (!File.Exists(dllPath)) return;
        
        var bytes = File.ReadAllBytes(dllPath);
        ProjectAssembly = Assembly.Load(bytes);
    }
}
