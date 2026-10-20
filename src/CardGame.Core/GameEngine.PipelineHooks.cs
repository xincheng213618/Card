using System.Reflection;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Scans the same partial-class surface as the rule observers: character
    // Runtime files are compiled into this assembly and register themselves.
    private static readonly Lazy<ProgramPipelineHookCatalog> ProgramPipelineHookRegistrations = new(
        () => ProgramPipelineHookCatalog.Discover(
            typeof(GameEngine).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)),
        true);
    private readonly ProgramPipelineHooks _programPipelineHooks;
}
