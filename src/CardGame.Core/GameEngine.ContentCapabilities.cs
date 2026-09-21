namespace CardGame.Core;

public sealed partial class GameEngine
{
    /// <summary>
    /// Content availability is owned by the content package, not by the global
    /// replay rules version. The package signature and content hash already
    /// provide the compatibility boundary for checkpoints.
    /// </summary>
    private bool HasClassicGeneralPackage(Version minimumVersion) =>
        IsClassicIdentityMode &&
        _contentRegistry?.Packages.Any(package =>
            package.Id == "standard-classic-generals" &&
            package.Version >= minimumVersion) == true;
}
