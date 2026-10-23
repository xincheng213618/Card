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
                         "mobile:shen-lu-su", "anniversary:shen-ma-chao", "mobile:shen-hua-tuo",
                         "anniversary:shen-pang-tong", "anniversary:shen-zhong-hui", "anniversary:shen-zhang-jiao",
                         "anniversary:shen-dian-wei", "anniversary:shen-huang-zhong", "anniversary:shen-cao-pi",
                         "mobile:shen-lu-xun", "mobile:shen-gan-ning", "mobile:shen-liu-bei",
                         "mobile:shen-zhao-yun", "mobile:shen-zhuge-liang", "mobile:shen-zhang-liao",
                         "mobile:shen-cao-cao", "mobile:shen-lu-bu", "mobile:shen-zhou-yu",
                         "mobile:shen-guan-yu", "mobile:shen-lu-meng", "mobile:shen-sima-yi",
                         "anniversary:shen-sun-quan", "ol:shen-zhen-ji", "ol:shen-cao-pi",
                         "mobile:shen-jiang-wei", "mobile:shen-ma-chao", "ol:shen-zhao-yun-2016",
                         "ol:shen-cao-cao", "ol:shen-sima-yi-2016", "anniversary:shen-zhang-liao",
                         "anniversary:shen-guan-yu", "anniversary:shen-liu-bei", "anniversary:shen-lu-xun",
                         "happy:shen-guo-jia", "happy:shen-sun-quan", "happy:shen-zhang-liao",
                         "happy:shen-lu-bu", "happy:shen-zhen-ji", "happy:shen-lu-su",
                         "happy:shen-cao-cao", "happy:shen-zhou-yu", "happy:shen-zhuge-liang",
                         "happy:shen-lu-meng", "happy:shen-zhao-yun", "happy:shen-gan-ning",
                         "happy:shen-zhang-jiao", "happy:shen-taishi-ci", "happy:shen-hua-tuo",
                         "happy:shen-liu-bei", "happy:shen-sima-yi", "happy:shen-guan-yu",
                         "happy:shen-lu-xun", "happy:shen-xu-chu", "happy:shen-dian-wei",
                         "happy:shen-zhong-hui", "happy:shen-deng-ai", "happy:shen-pang-tong", "happy:shen-diao-chan",
                         "happy:shen-wei-yan", "happy:shen-da-qiao-xiao-qiao", "happy:shen-zhang-fei", "happy:shen-zuo-ci" })
            {
                model.GeneralGallerySearchText = string.Empty;
                var entry = model.GeneralGalleryEntries.Single(item => item.GeneralId == GodGeneralRulesCatalog.CanonicalId(id));
                Program.Assert(GeneralGalleryCatalog.Classify(id) is { Id: "god", SeriesId: "god" },
                    "Each formal god edition must appear in the selectable god series.");
                model.GeneralGallerySearchText = entry.Name;
                Program.Assert(model.GeneralGalleryEntries.Contains(entry) && entry.IsPlayable && entry.HasPortrait,
                    $"Searching a god must retain its canonical playable edition and official local portrait ({id}: present={model.GeneralGalleryEntries.Contains(entry)}, playable={entry.IsPlayable}, portrait={entry.HasPortrait}).");
                if (id == "anniversary:shen-sun-quan")
                    Program.Assert(entry.RulesLabel == "沧溟" && entry.FactionId == "wu",
                        "The new god Sun Quan must show his actual Wu faction and distinct Cangming rules label.");
                if (id is "ol:shen-zhen-ji" or "ol:shen-cao-pi")
                    Program.Assert(entry.RulesLabel == "神武再世" && entry.RulesDifference.Contains("2020神武再世正式活动", StringComparison.Ordinal),
                        "Historical formal event generals must visibly identify their actual edition.");
                if (id == "ol:shen-zhao-yun-2016")
                    Program.Assert(entry.RulesLabel == "体力张龙魂" && entry.RulesDifference.Contains("2016年OL正式版", StringComparison.Ordinal),
                        "The original formal Zhao Yun must visibly distinguish his variable payment from later editions.");
                if (id is "ol:shen-cao-cao" or "ol:shen-sima-yi-2016")
                    Program.Assert(entry.RulesDifference.Contains("2016年OL正式", StringComparison.Ordinal),
                        "Historical OL god editions must identify their formal 2016 rules.");
                if (id == "anniversary:shen-zhang-liao")
                    Program.Assert(entry.RulesLabel == "猝袭废栏" && entry.RulesDifference.Contains("2026年9月", StringComparison.Ordinal),
                        "Anniversary Zhang Liao must expose the formal revised rules rather than borrowed-skill rules.");
                if (id is "anniversary:shen-guan-yu" or "anniversary:shen-liu-bei")
                    Program.Assert(entry.RulesDifference.Contains("2026年7月", StringComparison.Ordinal),
                        "The revised anniversary gods must identify their formal July edition.");
                if (id == "anniversary:shen-lu-xun")
                    Program.Assert(entry.RulesLabel == "群体绽火" && entry.RulesDifference.Contains("每名所选角色", StringComparison.Ordinal),
                        "Anniversary Zhanhuo must visibly distinguish damage to every selected target.");
                if (id.StartsWith("happy:", StringComparison.Ordinal))
                    Program.Assert(entry.RulesDifference.Contains("欢乐三国杀", StringComparison.Ordinal) && entry.RulesLabel.Length > 0,
                        "Happy god editions must identify their actual product and distinct rules.");
                model.SelectGeneralGalleryEntryCommand.Execute(entry);
                Program.Assert(model.SelectedGeneralGalleryEntry == entry &&
                    entry.Portrait.Brush is ImageBrush { ImageSource: not null } &&
                    entry.Skills.Count > 0 && entry.Skills.All(skill =>
                        !string.IsNullOrWhiteSpace(skill.Name) && !string.IsNullOrWhiteSpace(skill.Description)),
                    "God details must decode the actual portrait and expose complete skill text.");
                var grantedSkill = id switch
                {
                    "mobile:shen-guo-jia" => "mobile:zuoxing",
                    "happy:shen-guo-jia" => "happy:zuoxing",
                    "mobile:shen-taishi-ci" => "mobile-god:shenzhu",
                    "happy:shen-taishi-ci" => "happy:shenzhu",
                    "anniversary:shen-deng-ai" => "anniversary-god:cuixin",
                    "happy:shen-deng-ai" => "happy:cuixin",
                    "anniversary:shen-zhong-hui" => "anniversary:lishi",
                    "happy:shen-zhong-hui" => "happy:lishi",
                    "mobile:shen-sima-yi" => "mobile-god:jilue",
                    "mobile:shen-jiang-wei" => "mobile:huitian",
                    "ol:shen-sima-yi-2016" => "ol:jilue-2016",
                    _ => null
                };
                if (grantedSkill is not null)
                    Program.Assert(entry.Skills.Any(skill => skill.Description.Contains(
                        registry.GetSkill(grantedSkill).Description, StringComparison.Ordinal)),
                        $"Awakening and mission details must visibly explain the complete granted skill ({id}).");
                Program.Render(panel, 1120, 820, Path.Combine(output, GeneralArt.NormalizeKey(id) + "-gallery.png"));
                if (id is "anniversary:shen-ma-chao" or "anniversary:shen-jiang-wei" or "anniversary:shen-huang-zhong"
                    or "mobile:shen-sun-ce" or "mobile:shen-xun-yu")
                {
                    var classic = entry.Portrait.SkinId;
                    var happySkin = id switch
                    {
                        "anniversary:shen-ma-chao" => "happy-classic",
                        "anniversary:shen-huang-zhong" => "happy-guide-20260923",
                        "mobile:shen-sun-ce" or "mobile:shen-xun-yu" => "happy-guide-20260923",
                        _ => "happy-guide"
                    };
                    var happy = entry.Skins.Single(skin => skin.Id == happySkin);
                    model.SelectGeneralSkinCommand.Execute(happy);
                    Program.Assert(entry.Portrait.SkinName.StartsWith("欢乐", StringComparison.Ordinal) &&
                        entry.Portrait.Brush is ImageBrush { ImageSource: not null } &&
                        entry.RulesDifference.Contains(id.StartsWith("mobile:", StringComparison.Ordinal)
                            ? "移动版及欢乐三国杀等价版" : "周年及欢乐三国杀等价版", StringComparison.Ordinal),
                        "The Happy portrait is a real skin of the same reviewed playable rules.");
                    Program.Render(panel, 1120, 820, Path.Combine(output, GeneralArt.NormalizeKey(id) + "-happy-gallery.png"));
                    model.SelectGeneralSkinCommand.Execute(entry.Skins.Single(skin => skin.Id == classic));
                }
                var viewport = (System.Windows.Controls.Border)panel.FindName("SelectedGeneralPortraitViewport");
                var portrait = (System.Windows.Controls.Image)panel.FindName("SelectedGeneralPortraitImage");
                var bounds = portrait.TransformToAncestor(viewport).TransformBounds(new System.Windows.Rect(portrait.RenderSize));
                Program.Assert(Math.Abs(bounds.Left + (bounds.Width - viewport.ActualWidth) / 2) < 1,
                    "Landscape illustrations must stay centered inside the clipped portrait instead of losing the face at the right edge.");
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
