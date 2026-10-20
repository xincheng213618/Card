using System.Reflection;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Legacy engine handlers and explicit independent modules share the same
    // immutable discovery surface; phase/order still belongs to each handler.
    private static readonly Lazy<ProgramPipelineHookCatalog> ProgramPipelineHookRegistrations = new(
        () => ProgramPipelineHookCatalog.Discover(
            RuntimeModuleMethods),
        true);
    private readonly ProgramPipelineHooks _programPipelineHooks;
}
