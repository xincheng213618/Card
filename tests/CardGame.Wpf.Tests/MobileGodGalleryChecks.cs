using System.IO;
using System.Windows.Media;
using CardGame.Content.Standard;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class MobileGodGalleryChecks
{
    public static void GodSeriesPortraitsAndDetails(string output)
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        using var model = new MainViewModel(false, 17, true, new MemorySaveStore(),
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore(),
            contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false };
        model.OpenGeneralGalleryCommand.Execute(null);
        var revision = Program.Engine(model).Revision;
        var panel = new GeneralGalleryPanel { DataContext = model };
        try
        {
            foreach (var id in new[] { "mobile:shen-guo-jia", "mobile:shen-sun-ce", "mobile:shen-taishi-ci",
                         "mobile:shen-xun-yu", "anniversary:shen-jiang-wei", "anniversary:shen-deng-ai",
                         "anniversary:shen-zhang-fei", "anniversary:shen-hua-tuo", "anniversary:shen-xu-chu",
                         "mobile:shen-lu-su", "anniversary:shen-ma-chao", "mobile:shen-hua-tuo" })
            {
                model.GeneralGallerySearchText = string.Empty;
                var entry = model.GeneralGalleryEntries.Single(item => item.GeneralId == id);
                Program.Assert(GeneralGalleryCatalog.Classify(id) is { Id: "god", SeriesId: "god" },
                    "Each formal god edition must appear in the selectable god series.");
                model.GeneralGallerySearchText = entry.Name;
                Program.Assert(model.GeneralGalleryEntries.Contains(entry) && entry.IsPlayable && entry.HasPortrait,
                    "Searching a god must retain its playable edition and official local portrait.");
                model.SelectGeneralGalleryEntryCommand.Execute(entry);
                Program.Assert(model.SelectedGeneralGalleryEntry == entry &&
                    entry.Portrait.Brush is ImageBrush { ImageSource: not null } &&
                    entry.Skills.Count > 0 && entry.Skills.All(skill =>
                        !string.IsNullOrWhiteSpace(skill.Name) && !string.IsNullOrWhiteSpace(skill.Description)),
                    "God details must decode the actual portrait and expose complete skill text.");
                var grantedSkill = id switch
                {
                    "mobile:shen-guo-jia" => "mobile:zuoxing",
                    "mobile:shen-taishi-ci" => "mobile-god:shenzhu",
                    "anniversary:shen-deng-ai" => "anniversary-god:cuixin",
                    _ => null
                };
                if (grantedSkill is not null)
                    Program.Assert(entry.Skills.Any(skill => skill.Description.Contains(
                        registry.GetSkill(grantedSkill).Description, StringComparison.Ordinal)),
                        "Awakening and mission details must visibly explain the complete granted skill.");
                Program.Render(panel, 1120, 820, Path.Combine(output, GeneralArt.NormalizeKey(id) + "-gallery.png"));
                var scroller = Program.Find<System.Windows.Controls.ScrollViewer>(panel)
                    .Single(view => view.Name == "GeneralSkillsScroller");
                if (scroller.ScrollableHeight > 0)
                {
                    scroller.ScrollToEnd();
                    panel.UpdateLayout();
                    Program.Assert(Math.Abs(scroller.VerticalOffset - scroller.ScrollableHeight) < 1,
                        "The full mission and granted skill descriptions must remain reachable.");
                    scroller.ScrollToTop();
                    panel.UpdateLayout();
                }
                model.CloseGeneralGalleryDetailsCommand.Execute(null);
            }
            Program.Assert(Program.Engine(model).Revision == revision,
                "Browsing formal god editions must not advance gameplay.");
            Program.Assert(CardArt.Get(CardGame.Core.CardKind.QizhengXiangsheng) is not null,
                "The actual Tianzuo derived card must have a readable local face.");
        }
        finally { panel.DataContext = null; }
    }
}
