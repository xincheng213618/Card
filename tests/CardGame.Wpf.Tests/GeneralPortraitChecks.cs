using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class GeneralPortraitChecks
{
    public static void SelectionAndPersistence(string output)
    {
        var path = Path.Combine(output, "skin-preferences", Guid.NewGuid().ToString("N"), "preferences.json");
        var store = new FilePlayerPreferencesStore(path);
        string selectedId;
        string selectedSkin;
        using (var vm = new MainViewModel(false, 721019, showSetup: false, useExpandedContent: true, preferencesStore: store)
               { IsMotionEnabled = false })
        {
            var window = new MainWindow(vm);
            var root = (FrameworkElement)window.Content;
            vm.OpenGeneralGalleryCommand.Execute(null);
            var revision = Program.Engine(vm).Revision;
            var missing = vm.GeneralGalleryEntries.Where(entry => entry.SeriesId != "demo" && !entry.GeneralId.StartsWith("composed:", StringComparison.Ordinal) && !entry.HasPortrait).Select(entry => entry.GeneralId).ToArray();
            Program.Assert(missing.Length == 0, "Formal generals have missing portraits: " + string.Join(", ", missing));
            foreach (var entry in vm.GeneralGalleryEntries.Where(entry => entry.HasPortrait))
            {
                var image = (BitmapImage)((ImageBrush)entry.PortraitBrush).ImageSource;
                Program.Assert(image.PixelWidth > 100 && image.PixelHeight > 100 &&
                               !image.UriSource.ToString().Contains("generals-atlas"),
                    entry.GeneralId + " still uses an atlas or a thumbnail instead of full artwork.");
            }
            var subjects = new[] { "zhao-yun", "ma-chao", "gan-ning", "zhou-yu", "huang-yueying", "zhen-ji", "sima-yi", "guo-jia" };
            Program.Assert(subjects.Select(id => GeneralArt.GetSkin("classic:" + id)!.LocalPath).Distinct().Count() == subjects.Length,
                "Unrelated characters still share placeholder artwork.");
            Program.Assert(!GeneralArt.HasPortrait("") && !GeneralArt.HasPortrait("classic:unknown-general"),
                "Unknown or hidden generals must not acquire another character's portrait.");

            var maDai = vm.GeneralGalleryEntries.Single(entry => entry.GeneralId == "classic:ma-dai");
            vm.SelectGeneralGalleryEntryCommand.Execute(maDai);
            Program.Render(root, 1440, 920, Path.Combine(output, "150-general-details-ma-dai.png"));
            var panel = (GeneralGalleryPanel)window.FindName("GeneralGallery");
            var imageControl = (Image)panel.FindName("SelectedGeneralPortraitImage");
            Program.Assert(ReferenceEquals(imageControl.Source, maDai.Portrait.Image) && imageControl.ActualHeight > 300,
                "Selected details must show the actual large portrait.");
            Program.Assert(!((Border)panel.FindName("GalleryBrowseSurface")).IsEnabled,
                "Details must block the gallery underneath.");
            vm.SelectGeneralDetailsTabCommand.Execute("skins");
            Program.Render(root, 1440, 920, Path.Combine(output, "151-general-skins-ma-dai.png"));
            Program.Assert(maDai.Skins.Count > 1 && vm.IsGeneralSkinsTab,
                "The skin tab must list real alternative Ma Dai artwork.");
            var alternate = maDai.Skins.First(skin => skin.Id != maDai.Portrait.SkinId);
            var skinButton = Program.Find<Button>(root).Single(button => button.Command == vm.SelectGeneralSkinCommand && Equals(button.CommandParameter, alternate));
            skinButton.Command!.Execute(skinButton.CommandParameter);
            Program.Render(root, 1120, 740, Path.Combine(output, "152-general-skin-selected-compact.png"));
            Program.Assert(maDai.Portrait.SkinId == alternate.Id && ReferenceEquals(imageControl.Source, maDai.Portrait.Image),
                "A skin click did not update the full-size image.");
            var details = (Border)panel.FindName("GeneralDetailsPanel");
            var corner = details.TranslatePoint(new Point(details.ActualWidth, details.ActualHeight), root);
            Program.Assert(corner.X <= 1120 && corner.Y <= 740, "Details escaped the compact viewport.");
            vm.NextGeneralDetailsCommand.Execute(null);
            Program.Assert(vm.SelectedGeneralGalleryEntry != maDai, "Next-general navigation did not change the details.");
            vm.PreviousGeneralDetailsCommand.Execute(null);
            Program.Assert(vm.SelectedGeneralGalleryEntry == maDai && maDai.Portrait.SkinId == alternate.Id,
                "Returning to a general lost the skin selection.");
            Program.Assert(Program.Engine(vm).Revision == revision, "Changing appearance changed the game rules or state.");

            var choice = vm.GeneralChoices.First(candidate => GeneralArt.GetSkins(candidate.GeneralId).Count > 1);
            selectedId = choice.GeneralId;
            var entryForChoice = vm.GeneralGalleryEntries.Single(entry => entry.GeneralId == selectedId);
            vm.SelectGeneralGalleryEntryCommand.Execute(entryForChoice);
            var choiceSkin = entryForChoice.Skins.First(skin => skin.Id != entryForChoice.Portrait.SkinId);
            selectedSkin = choiceSkin.Id;
            vm.SelectGeneralSkinCommand.Execute(choiceSkin);
            Program.Assert(ReferenceEquals(choice.Portrait, entryForChoice.Portrait) && choice.Portrait.SkinId == selectedSkin,
                "Gallery and general selection are not sharing the chosen skin.");
            vm.CloseGeneralGalleryDetailsCommand.Execute(null);
            vm.CloseGeneralGalleryCommand.Execute(null);
            vm.SelectGeneralChoiceCommand.Execute(choice);
            Program.Assert(vm.Seats.Any(seat => seat.GeneralId == selectedId && seat.Portrait.SkinId == selectedSkin),
                "The battle portrait did not inherit the chosen skin.");
            Program.Assert(vm.FlushPreferences(), "Skin preferences could not be saved.");
            window.Content = null;
            window.Close();
        }
        using var reopened = new MainViewModel(false, 721019, showSetup: false, useExpandedContent: true, preferencesStore: new FilePlayerPreferencesStore(path));
        reopened.OpenGeneralGalleryCommand.Execute(null);
        Program.Assert(reopened.GeneralGalleryEntries.Single(entry => entry.GeneralId == selectedId).Portrait.SkinId == selectedSkin,
            "Relaunch lost the selected skin.");
        var saved = store.Read()!;
        store.Write(saved with { GeneralSkins = new() { [GeneralArt.NormalizeKey(selectedId)] = "removed-skin" } });
        using var invalid = new MainViewModel(false, 721019, showSetup: false, useExpandedContent: true, preferencesStore: new FilePlayerPreferencesStore(path));
        invalid.OpenGeneralGalleryCommand.Execute(null);
        Program.Assert(invalid.GeneralGalleryEntries.Single(entry => entry.GeneralId == selectedId).Portrait.SkinId == GeneralArt.GetSkin(selectedId)!.Id,
            "An obsolete skin preference must fall back to the registered default.");
    }
}
