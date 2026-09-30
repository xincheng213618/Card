using CardGame.Content.Standard;
using CardGame.Core;

internal static class NationalHealthChecks
{
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }



    private sealed class VitalsPackage(int hp) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("test-vitals", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder) => builder.AddGeneral(new("test:general", "体力测试", "", "standard:none", BaseHp: hp));
    }
}
