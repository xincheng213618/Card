using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class GeneralGalleryChecks
{
    public static void ClassificationAndLayout(MainViewModel vm, MainWindow window, FrameworkElement root, string output)
    {
        var revision = Program.Engine(vm).Revision;
        var allIds = vm.GeneralGalleryEntries.Select(entry => entry.GeneralId).ToHashSet();
        Program.Assert(vm.GeneralGalleryGroups.SelectMany(group => group.Entries).Select(entry => entry.GeneralId).ToHashSet().SetEquals(allIds) &&
                       vm.GeneralGalleryGroups.Sum(group => group.Entries.Count) == allIds.Count,
            "Grouped browsing duplicated or omitted a registered general.");
        foreach (var (id, group) in new[]
        {
            ("classic:liu-bei", "standard"), ("classic:hua-xiong", "standard"),
            ("classic:zhang-jiao", "myth-wind"), ("classic:taishi-ci", "myth-fire"),
            ("classic:xu-huang", "myth-forest"), ("classic:yan-yan", "myth-yin"),
            ("classic:yu-jin", "fame-1"), ("classic:cheng-pu", "fame-2"),
            ("classic:cao-chong", "fame-3"), ("classic:zhu-huan", "fame-4"),
            ("classic:zhu-zhi", "fame-5"),
            ("classic:shen-guan-yu", "god"),
            ("classic:gongsun-zan", "sp"), ("sp:guan-yu", "sp"),
            ("mou:lu-meng", "mou"), ("boundary:zhang-jiao", "boundary"),
            ("boundary:sima-yi", "boundary")
        })
            Program.Assert(vm.GeneralGalleryEntries.Single(entry => entry.GeneralId == id).GroupId == group,
                $"{id} has an incorrect gallery expansion.");
        Program.Assert(GeneralGalleryCatalog.Classify("classic:unclassified-future-general").Id == "other",
            "A new unknown general was silently presented as a standard general.");
        var simaYi = vm.GeneralGalleryEntries.Single(entry => entry.GeneralId == "boundary:sima-yi");
        Program.Assert(simaYi.Kingdom == "魏" && simaYi.SkillDescription.Contains("反馈") &&
                       simaYi.SkillDescription.Contains("鬼才") &&
                       GeneralArt.HasPortrait(simaYi.GeneralId),
            "Boundary Sima Yi must appear with Wei identity, both skills, and distinct registered artwork.");
        var zhuHuan = vm.GeneralGalleryEntries.Single(entry => entry.GeneralId == "classic:zhu-huan");
        Program.Assert(zhuHuan.Kingdom == "吴" && zhuHuan.SkillDescription.Contains("诱敌") &&
                       zhuHuan.SkillDescription.Contains("结束阶段") &&
                       GeneralArt.HasPortrait(zhuHuan.GeneralId),
            "2014 Zhu Huan needs his Wu/Fame IV gallery text and official portrait.");
        var zhuZhi = vm.GeneralGalleryEntries.Single(entry => entry.GeneralId == "classic:zhu-zhi");
        Program.Assert(zhuZhi.Kingdom == "吴" && zhuZhi.SkillDescription.Contains("安国") &&
                       zhuZhi.SkillDescription.Contains("攻击范围") && zhuZhi.GroupId == "fame-5",
            "2015 Zhu Zhi needs the Wu/Fame V gallery group and Anguo text.");
        var quYi = vm.GeneralGalleryEntries.Single(entry => entry.GeneralId == "classic:qu-yi");
        Program.Assert(quYi.Kingdom == "群" && quYi.GroupId == "other" &&
                       quYi.SkillDescription.Contains("伏骑") &&
                       quYi.SkillDescription.Contains("骄恣") && GeneralArt.HasPortrait(quYi.GeneralId),
            "Qu Yi needs both skills and his own official portrait in the other-expansion gallery.");

        Program.Render(root, 1440, 880, Path.Combine(output, "140-gallery-all.png"));
        var panel = (GeneralGalleryPanel)window.FindName("GeneralGallery");
        panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var scroller = (ScrollViewer)panel.FindName("GalleryScroller");
        scroller.ScrollToBottom();
        root.UpdateLayout();
        Program.Assert(scroller.VerticalOffset > 0, "The grouped card wall is not scrollable.");

        vm.SelectGeneralGallerySeriesCommand.Execute("myth");
        Program.Render(root, 1440, 880, Path.Combine(output, "141-gallery-myth.png"));
        Program.Assert(scroller.VerticalOffset == 0 && vm.GeneralGalleryGroups.Count == 6 &&
                       vm.GeneralGallerySubgroups.Select(option => option.Id).SequenceEqual(new[]
                       { "all", "myth-wind", "myth-fire", "myth-forest", "myth-mountain", "myth-yin", "myth-thunder" }),
            "Mythology must expose all six packs and start browsing at the top.");
        vm.SelectGeneralGalleryGroupCommand.Execute("myth-wind");
        vm.SelectGeneralGalleryFactionCommand.Execute("wei");
        vm.GeneralGallerySearchText = "神速";
        Program.Render(root, 1120, 740, Path.Combine(output, "142-gallery-skill-search.png"));
        foreach (var command in new[] { vm.SelectGeneralGallerySeriesCommand, vm.SelectGeneralGalleryGroupCommand, vm.SelectGeneralGalleryFactionCommand })
        {
            var buttons = Program.Find<Button>(root).Where(button => button.Command == command).ToList();
            var selected = buttons.Single(button => Equals(button.Tag, true));
            Program.Assert(buttons.Where(button => !Equals(button.Tag, true)).All(button =>
                    !Equals(button.Background.ToString(), selected.Background.ToString())),
                "The selected gallery filter has no distinct visible highlight.");
        }
        Program.Assert(vm.GeneralGalleryEntries is [{ GeneralId: "classic:xiahou-yuan" }],
            "Series, pack, faction and skill search did not compose.");
        var card = Program.Find<Button>(root).Single(button =>
            button.Command == vm.SelectGeneralGalleryEntryCommand && button.CommandParameter == vm.GeneralGalleryEntries[0]);
        card.Command!.Execute(card.CommandParameter);
        Program.Render(root, 1120, 740, Path.Combine(output, "143-gallery-details.png"));
        Program.Assert(((Border)card.Template.FindName("Frame", card)).BorderThickness.Left == 3,
            "The selected general has no visible card outline.");
        Program.Assert(vm.HasGeneralGallerySelection && ((Border)panel.FindName("GeneralDetailsPanel")).ActualWidth > 0 &&
                       vm.SelectedGeneralGalleryEntry!.SkillDescription.Contains("神速"),
            "Portrait activation did not expose the general's complete skills.");
        typeof(MainWindow).GetMethod("HandleShortcut", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(window, [Key.Escape, ModifierKeys.None]);
        Program.Assert(!vm.HasGeneralGallerySelection && vm.IsGeneralGalleryOpen,
            "Escape should close details before leaving the gallery.");

        vm.ClearGeneralGalleryFiltersCommand.Execute(null);
        vm.SelectGeneralGallerySeriesCommand.Execute("fame");
        Program.Render(root, 1440, 880, Path.Combine(output, "144-gallery-fame.png"));
        Program.Assert(vm.SelectedGeneralGalleryGroup == "all" && vm.GeneralGalleryGroups.Count == 7 &&
                       vm.GeneralGallerySubgroups.Skip(1).Select(option => option.Id)
                           .SequenceEqual(Enumerable.Range(1, 7).Select(index => $"fame-{index}")),
            "Fame must keep all seven numbered groups, including currently empty groups.");
        vm.SelectGeneralGalleryGroupCommand.Execute("fame-3");
        var fullFameThreeIds = vm.GeneralGalleryEntries.Select(entry => entry.GeneralId).ToHashSet();
        vm.SelectGeneralGalleryFactionCommand.Execute("wei");
        Program.Assert(vm.GeneralGalleryEntries.Select(entry => entry.GeneralId).ToHashSet()
                           .SetEquals(["classic:cao-chong", "classic:guo-huai", "classic:man-chong"]),
            "Fame III / Wei includes another pack or faction.");
        vm.GeneralGallerySearchText = "不存在的武将";
        Program.Assert(vm.HasNoGeneralGalleryResults, "No-match search needs a visible empty state.");
        vm.ClearGeneralGalleryFiltersCommand.Execute(null);
        Program.Assert(vm.SelectedGeneralGallerySeries == "fame" && vm.SelectedGeneralGalleryGroup == "fame-3" &&
                       vm.GeneralGalleryEntries.Select(entry => entry.GeneralId).ToHashSet().SetEquals(fullFameThreeIds),
            "Clearing search should preserve the active series and pack and restore every member.");
        vm.SelectGeneralGalleryGroupCommand.Execute("fame-7");
        Program.Render(root, 1120, 740, Path.Combine(output, "145-gallery-fame-seven.png"));
        Program.Assert(vm.GeneralGalleryGroups is [{ IsEmpty: true, Id: "fame-7" }] && vm.GeneralGalleryEntries.Count == 0,
            "An empty numbered pack must not invent playable generals.");
        vm.SelectGeneralGallerySeriesCommand.Execute("boundary-fame");
        Program.Assert(vm.GeneralGalleryGroups is [{ IsEmpty: false, Id: "boundary-fame" }] &&
                       vm.GeneralGalleryEntries.Select(entry => entry.GeneralId).ToHashSet()
                           .SetEquals(["boundary:xu-sheng", "boundary:zhang-song"]),
            "Boundary Fame lists exactly its registered boundary-fame members.");
        vm.SelectGeneralGallerySeriesCommand.Execute("god");
        vm.SelectGeneralGalleryFactionCommand.Execute("god");
        Program.Assert(vm.GeneralGalleryEntries.Select(entry => entry.GeneralId).ToHashSet()
                           .SetEquals(["classic:shen-guan-yu", "classic:shen-sima-yi"]) &&
                       vm.GeneralGalleryEntries.All(entry => entry.Kingdom == "神"),
            "God generals must be discoverable by the god faction filter without changing Core factions.");

        vm.SelectGeneralGalleryFactionCommand.Execute("all");
        vm.SelectGeneralGallerySeriesCommand.Execute("all");
        Program.Render(root, 1120, 740, Path.Combine(output, "146-gallery-compact.png"));
        foreach (var name in new[] { "CloseGeneralGalleryButton", "GeneralGallerySearchBox" })
        {
            var element = (FrameworkElement)panel.FindName(name);
            var position = element.TranslatePoint(new Point(), root);
            Program.Assert(position.X >= 0 && position.Y >= 0 && position.X + element.ActualWidth <= 1120 &&
                           position.Y + element.ActualHeight <= 740, "Gallery controls escaped the compact viewport.");
        }
        var tabs = (ScrollViewer)panel.FindName("SeriesScroller");
        tabs.ScrollToRightEnd();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        Program.Assert(tabs.HorizontalOffset > 0, "Trailing gallery series must remain reachable on small windows.");
        tabs.ScrollToLeftEnd();
        Program.Assert(Program.Engine(vm).Revision == revision && vm.GeneralGalleryEntries.Select(entry => entry.GeneralId).ToHashSet().SetEquals(allIds),
            "Gallery browsing changed the game or lost registered entries.");
        panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    }
}
