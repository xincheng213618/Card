namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static readonly Lazy<ProgramTargetResolverCatalog> ProgramTargetResolverMetadata = new(() =>
        new(typeof(ProgramTargetResolverCatalog).Assembly.GetTypes()), true);
    private ProgramTargetResolverRegistry? _programTargetResolvers;
    private ProgramTargetResolverRegistry ProgramTargetResolvers =>
        _programTargetResolvers ??= new(ProgramTargetResolverMetadata.Value, ProgramSelectionQueries);

}
