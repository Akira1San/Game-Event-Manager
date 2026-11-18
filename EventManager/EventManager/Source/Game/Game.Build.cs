using Flax.Build;
using Flax.Build.NativeCpp;

public class Game : GameModule
{
    /// <inheritdoc />
    public override void Init()
    {
        base.Init();

        // C#-only scripting
        BuildNativeCode = false;
    }

    /// <inheritdoc />
    public override void Setup(BuildOptions options)
    {
        base.Setup(options);

        options.ScriptingAPI.IgnoreMissingDocumentationWarnings = true;

        // `Runtime/SaveFile.cs` (the saved-game format) uses System.Text.Json, which is part of
        // .NET's shared framework but not in the reference set Flax hands a C# module by
        // default, so the module failed to compile with "The type or namespace name 'Json' does
        // not exist in the namespace 'System.Text'" until it was listed here.
        // System.Text.Json forwards a few BCL types to sibling assemblies (UTF8Encoding lives in
        // System.Text.Encoding.Extensions), so those are named too rather than pulling in the
        // whole shared framework.
        options.ScriptingAPI.SystemReferences.Add("System.Text.Json");
        options.ScriptingAPI.SystemReferences.Add("System.Text.Encoding.Extensions");
        options.ScriptingAPI.SystemReferences.Add("System.Text.Encodings.Web");

        // Here you can modify the build options for your game module
        // To reference another module use: options.PublicDependencies.Add("Audio");
        // To add C++ define use: options.PublicDefinitions.Add("COMPILE_WITH_FLAX");
        // To learn more see scripting documentation.
    }
}
