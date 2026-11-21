// Runs the Unity EditMode test sources (pure C# parts) and the integration checks under plain .NET.
//   dotnet run                -> all tests
//   dotnet run -- Integration -> only names containing "Integration"
//   dotnet run -- scene < scene.json -> composite shader uniforms for a scene (Tools/ShaderPreview)
public static class Program
{
    public static int Main(string[] args)
        => args.Length > 0 && args[0] == "scene" ? SceneDump.Run() : Runner.RunAll(typeof(Program).Assembly, args.Length > 0 ? args[0] : null);
}
