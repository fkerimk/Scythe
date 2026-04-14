using System.Reflection;

internal class ScriptAsset : Asset {

    public Assembly? Assembly;
    public Type? ScriptType;

    public override bool Load() {
        
        if (!System.IO.File.Exists(File)) return false;

        ScriptCompiler.CompileProject(); 
        AssignFromAssembly();
        return true;
    }

    public void AssignFromAssembly() {
        
        if (ScriptCompiler.ProjectAssembly == null) return;
        
        Assembly = ScriptCompiler.ProjectAssembly;
        var typeName = Path.GetFileNameWithoutExtension(File);
        ScriptType = Assembly.GetTypes().FirstOrDefault(t => t.Name == typeName && typeof(ScytheScript).IsAssignableFrom(t) && !t.IsAbstract);
        IsLoaded = true;
    }

    public override void Unload() {
        
        Assembly = null;
        ScriptType = null;
        IsLoaded = false;
    }
}