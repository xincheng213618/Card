using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class GeneralRosterCategoryChecks
{
    public static void GodRulesMergeSkinsAndDistinguishVersions(string output)
    {
        using var model = new MainViewModel(false, 31, true, new MemorySaveStore(), useExpandedContent: true,
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore(),
            contentRegistry: StandardContentRegistry.CreateWithClassicGenerals())
        { IsMotionEnabled = false, IsMusicEnabled = false, IsSoundEnabled = false };
        model.SelectedStartingRole = model.StartingRoles.Single(role => role.Role == Role.Lord);
        model.StartNewGameCommand.Execute(null);
        model.ContinueFromIdentityRevealCommand.Execute(null);
        model.ToggleGeneralRosterCommand.Execute(null);
        model.SelectCustomGeneralCategoryCommand.Execute("god");
        var gods = model.CustomGeneralChoices.ToArray();
        var revision = Program.Engine(model).Revision;
        Program.Assert(gods.All(choice => GodGeneralRulesCatalog.IsCanonical(choice.GeneralId)) &&
            gods.Select(choice => choice.Name).Distinct(StringComparer.Ordinal).Count() == gods.Length &&
            model.CustomGeneralCategories.Single(category => category.Id == "god").Count == gods.Length,
            "God roster counts and cards contain one entry per reviewed rule set and distinct names for retained versions.");
        foreach (var id in new[] { "classic:shen-cao-cao", "mobile:shen-cao-cao", "ol:shen-lu-xun", "mobile:shen-lu-xun",
                     "anniversary:shen-sun-quan", "anniversary:shen-cao-pi", "ol:shen-cao-pi", "ol:shen-zhen-ji",
                     "anniversary:shen-jiang-wei", "mobile:shen-jiang-wei", "anniversary:shen-ma-chao", "mobile:shen-ma-chao",
                     "ol:shen-zhao-yun-2016", "ol:shen-cao-cao", "ol:shen-sima-yi-2016", "anniversary:shen-zhang-liao",
                     "anniversary:shen-guan-yu", "anniversary:shen-liu-bei", "anniversary:shen-lu-xun",
                     "happy:shen-guo-jia", "happy:shen-sun-quan", "happy:shen-zhang-liao",
                     "happy:shen-lu-bu", "happy:shen-zhen-ji", "mobile:shen-lu-su", "happy:shen-lu-su",
                     "classic:shen-zhou-yu", "mobile:shen-zhuge-liang", "happy:shen-cao-cao",
                     "happy:shen-zhou-yu", "happy:shen-zhuge-liang", "classic:shen-lu-meng",
                     "happy:shen-lu-meng", "happy:shen-zhao-yun", "happy:shen-gan-ning",
                     "mobile:shen-taishi-ci", "happy:shen-taishi-ci", "happy:shen-zhang-jiao", "happy:shen-hua-tuo",
                     "happy:shen-liu-bei", "happy:shen-sima-yi", "happy:shen-guan-yu",
                     "happy:shen-lu-xun", "anniversary:shen-xu-chu", "happy:shen-xu-chu", "ol:shen-dian-wei", "happy:shen-dian-wei",
                     "anniversary:shen-zhong-hui", "happy:shen-zhong-hui", "anniversary:shen-deng-ai", "happy:shen-deng-ai",
                     "anniversary:shen-pang-tong", "happy:shen-pang-tong", "happy:shen-diao-chan",
                     "happy:shen-wei-yan", "happy:shen-da-qiao-xiao-qiao", "happy:shen-zhang-fei", "happy:shen-zuo-ci" })
        {
            var choice = gods.Single(choice => choice.GeneralId == id);
            Program.Assert(choice.EditionText.Contains(GodGeneralRulesCatalog.Difference(id), StringComparison.Ordinal),
                "The selection preview explains each retained version's actual gameplay difference.");
        }
        foreach (var id in new[] { "anniversary:shen-ma-chao", "anniversary:shen-jiang-wei" })
            Program.Assert(gods.Single(choice => choice.GeneralId == id).EditionText.Contains("周年及欢乐三国杀等价版", StringComparison.Ordinal),
                "Shared Happy and anniversary rules remain one selectable general with both products identified.");
        foreach (var id in new[] { "mobile:shen-sun-ce", "mobile:shen-xun-yu" })
            Program.Assert(gods.Single(choice => choice.GeneralId == id).EditionText.Contains("移动版及欢乐三国杀等价版", StringComparison.Ordinal),
                "Shared Happy and mobile rules remain one selectable general with both products identified.");
        var window = new MainWindow(model);
        try
        {
            var choice = gods.Single(choice => choice.GeneralId == "classic:shen-guan-yu");
            model.PreviewGeneralChoiceCommand.Execute(choice);
            Program.Assert(model.SelectedGeneralVariants.All(version => GodGeneralRulesCatalog.IsCanonical(version.GeneralId)),
                "Equivalent editions cannot return through the version buttons.");
            Program.Render((FrameworkElement)window.Content, 1440, 900, Path.Combine(output, "god-rules-roster.png"));
            var roster = (ListBox)window.FindName("CustomGeneralRoster");
            Program.Assert(new[] { "随机归心", "自选归心" }.All(label => Program.Find<TextBlock>(roster)
                .Any(block => block.Text == label && block.Visibility == Visibility.Visible)),
                "The card wall visibly separates the actual rules labels from general names.");
            model.CustomGeneralSearch = "随机归心";
            Program.Assert(model.CustomGeneralChoices.Single().GeneralId == "classic:shen-cao-cao",
                "Gameplay labels are searchable independently of shared skill names.");
            model.OpenGeneralGalleryCommand.Execute(null);
            model.GeneralGallerySearchText = string.Empty;
            foreach (var (alias, canonical) in GodGeneralRulesCatalog.Aliases)
            {
                Program.Assert(model.GeneralGalleryEntries.All(entry => entry.GeneralId != alias) &&
                    model.GeneralGalleryEntries.Count(entry => entry.GeneralId == canonical) == 1,
                    "The gallery hides equivalent historical registrations and exposes exactly one canonical general.");
                var canonicalPaths = GeneralArt.GetSkins(canonical).Select(skin => skin.LocalPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                Program.Assert(GeneralArt.GetSkins(alias).All(skin => canonicalPaths.Contains(skin.LocalPath)),
                    "All artwork of a merged edition remains available as canonical skins.");
            }
            var entry = model.GeneralGalleryEntries.Single(entry => entry.GeneralId == choice.GeneralId);
            model.SelectGeneralGalleryEntryCommand.Execute(entry);
            var skin = entry.Skins.Single(skin => skin.LocalPath.EndsWith("official-mobile-shen-guan-yu.jpg", StringComparison.Ordinal));
            var hash = Program.Engine(model).ContentRegistry!.ContentHash;
            var skills = choice.SkillDescription;
            model.SelectGeneralSkinCommand.Execute(skin);
            Program.Assert(ReferenceEquals(choice.Portrait, entry.Portrait) && entry.Portrait.SkinId == skin.Id &&
                entry.Portrait.Brush is System.Windows.Media.ImageBrush { ImageSource: not null } &&
                choice.GeneralId == entry.GeneralId && choice.SkillDescription == skills &&
                Program.Engine(model).ContentRegistry!.ContentHash == hash && Program.Engine(model).Revision == revision,
                "Selecting a merged skin updates shared portraits without changing rules, general identity or game state.");
            var panel = new CardGame.Wpf.Controls.GeneralGalleryPanel { DataContext = model };
            try { Program.Render(panel, 1120, 820, Path.Combine(output, "god-rules-details.png")); }
            finally { panel.DataContext = null; }
        }
        finally { window.Content = null; window.Close(); }
    }

    public static void SeriesSearchAndModeBoundaries(string output)
    {
        using var model = new MainViewModel(false, 31, true, new MemorySaveStore(), useExpandedContent: true,
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore(),
            contentRegistry: StandardContentRegistry.CreateWithClassicGenerals())
        { IsMotionEnabled = false, IsMusicEnabled = false, IsSoundEnabled = false };
        model.SelectedStartingRole = model.StartingRoles.Single(role => role.Role == Role.Lord);
        model.StartNewGameCommand.Execute(null);
        model.ContinueFromIdentityRevealCommand.Execute(null);
        model.ToggleGeneralRosterCommand.Execute(null);
        var all = model.CustomGeneralChoices.ToArray();
        var game = Program.Engine(model);
        var before = SnapshotJson.Serialize(game.CreateSnapshot(0));
        var accepted = game.AcceptedCommands.Count;
        var categories = model.CustomGeneralCategories.ToArray();
        Program.Assert(categories.Single(category => category.Id == "all").Count == all.Length &&
            categories.Where(category => category.Id != "all").Sum(category => category.Count) == all.Length &&
            new[] { "boundary", "mou", "magic", "god", "le", "wumiao" }.All(id => categories.Any(category => category.Id == id)),
            "Every currently eligible edition belongs to one counted category, including separate magic and god categories.");
        Program.Assert(GeneralRosterCatalog.Classify("boundary:ju-shou") == "boundary" &&
            GeneralRosterCatalog.Classify("classic:shen-sima-yi") == "god" &&
            GeneralRosterCatalog.Classify("ol:shen-sima-yi") == "god" &&
            GeneralRosterCatalog.Classify("magic:guan-yu") == "magic" &&
            GeneralRosterCatalog.Classify("le:xiao-qiao") == "le",
            "Broad categories merge both boundary packs and keep formal magic, god and new Le editions distinct.");
        var window = new MainWindow(model);
        var root = (FrameworkElement)window.Content;
        try
        {
            foreach (var category in categories)
            {
                model.SelectCustomGeneralCategoryCommand.Execute(category.Id);
                Program.Assert(model.CustomGeneralChoices.Select(choice => choice.GeneralId).ToHashSet().SetEquals(
                        all.Where(choice => category.Id == "all" || choice.RosterCategoryId == category.Id).Select(choice => choice.GeneralId)) &&
                    model.CustomGeneralChoices.Count == category.Count,
                    "A category contains exactly its current prompt's editions without broadening the mode roster.");
            }
            model.SelectCustomGeneralCategoryCommand.Execute("boundary");
            var target = model.CustomGeneralChoices.Single(choice => choice.GeneralId == "boundary:sima-yi");
            model.CustomGeneralSearch = target.Name;
            Program.Assert(model.CustomGeneralChoices.Contains(target) &&
                model.CustomGeneralChoices.All(choice => choice.RosterCategoryId == "boundary" && choice.Name.Contains(target.Name)) &&
                model.CustomGeneralCategories.SequenceEqual(categories),
                "Search narrows the selected series while category totals retain the full eligible roster.");
            model.CustomGeneralSearch = "__missing_series_general__";
            Program.Assert(model.HasNoCustomGeneralChoices, "An empty series search exposes its empty state.");
            model.CustomGeneralSearch = string.Empty;
            foreach (var (width, height) in new[] { (1120, 740), (1440, 900), (1920, 1080) })
            {
                Program.Render(root, width, height, Path.Combine(output, $"custom-general-series-{width}.png"));
                var tabs = (ItemsControl)window.FindName("CustomGeneralCategoryTabs");
                var roster = (ListBox)window.FindName("CustomGeneralRoster");
                var confirm = (FrameworkElement)window.FindName("ConfirmGeneralChoiceButton");
                var panel = (FrameworkElement)window.FindName("GeneralSelectionPanel");
                var buttons = Program.Find<Button>(tabs).ToArray();
                var selected = buttons.Single(button => Equals(button.CommandParameter, "boundary"));
                Program.Assert(buttons.Length == categories.Length &&
                    selected.Tag is true && selected.Background.ToString() != buttons.First().Background.ToString() &&
                    buttons.All(button => button.Visibility == Visibility.Visible && button.IsEnabled && button.TransformToAncestor(tabs)
                        .TransformBounds(new Rect(button.RenderSize)).Right <= tabs.ActualWidth + 1) &&
                    roster.ActualHeight > 120 && Program.Find<ListBoxItem>(roster).Count() is > 0 and <= 64 &&
                    new Rect(panel.RenderSize).Contains(confirm.TransformToAncestor(panel).TransformBounds(new Rect(confirm.RenderSize))),
                    "Wrapped category buttons, selected styling, virtualized results and confirmation fit compact and wide layouts.");
            }
            Program.Assert(before == SnapshotJson.Serialize(game.CreateSnapshot(0)) && game.AcceptedCommands.Count == accepted,
                "Browsing categories, searching and previewing must not submit commands or change the published private prompt.");
            model.SelectCustomGeneralCategoryCommand.Execute("__unknown_category__");
            Program.Assert(model.SelectedCustomGeneralCategory == "boundary", "Unknown category input cannot retire current results.");
            model.ToggleGeneralRosterCommand.Execute(null);
            model.ToggleGeneralRosterCommand.Execute(null);
            Program.Assert(model.SelectedCustomGeneralCategory == "boundary" && model.CustomGeneralChoices.All(choice => choice.RosterCategoryId == "boundary"),
                "Returning from candidates preserves the current series for the same mode.");
            model.SelectedTableMode = model.TableModes.Single(mode => mode.ModeId == "identity:classic-5");
            model.StartNewGameCommand.Execute(null);
            model.ContinueFromIdentityRevealCommand.Execute(null);
            model.ToggleGeneralRosterCommand.Execute(null);
            Program.Assert(model.SelectedCustomGeneralCategory == "all" &&
                model.CustomGeneralCategories.All(category => category.Id is "all" or "standard" or "myth") &&
                model.CustomGeneralChoices.All(choice => choice.RosterCategoryId is "standard" or "myth"),
                "Switching to the five-player mode drops unavailable categories and restores its own eligible roster.");
        }
        finally { window.Content = null; window.Close(); }
    }
}
