using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class GeneralGalleryFocusChecks
{

    internal sealed class GalleryFixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-gallery-focus", new Version(1, 0, 0), []);

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new("fixture:gallery-focus-a", "焦点甲", "supporter", "standard:none", "wei"));
            builder.AddGeneral(new("fixture:gallery-focus-b", "焦点乙", "supporter", "standard:none", "shu"));
        }
    }
}
