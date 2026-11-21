// Runs the Unity EditMode test sources (pure C# parts) and the integration checks under plain .NET.
//   dotnet run                -> all tests
//   dotnet run -- Integration -> only names containing "Integration"
public static class Program
{
    public static int Main(string[] args) => Runner.RunAll(typeof(Program).Assembly, args.Length > 0 ? args[0] : null);
}
