using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Content.Standard;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.ViewModels;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Audio;
using CardGame.Wpf.Presentation;

internal static class Program
{
    private static int _passed;
    private static readonly BindingListener BindingErrors = new();

    [STAThread]
    private static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/CardGame.Wpf;component/Themes/TableTheme.xaml")
        });
        PresentationTraceSources.DataBindingSource.Listeners.Add(BindingErrors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        try
        {
            var output = args.FirstOrDefault(argument =>
                !argument.StartsWith("--", StringComparison.Ordinal)) ??
                Path.Combine(Path.GetTempPath(), "card-ui-check");
            Directory.CreateDirectory(output);
            if (args.Contains("--only-classic-rende", StringComparer.Ordinal))
            {
                Check("classic Rende remains repeatable before Jijiang opens its separate target draft",
                    () => ClassicGeneralUiChecks.JijiangActiveAction(output));
                Assert(BindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, BindingErrors.Errors.Take(15)));
                Console.WriteLine($"{_passed} targeted WPF check passed. Renders: {output}");
                return 0;
            }
            if (args.Contains("--only-classic-shared-skills", StringComparer.Ordinal))
            {
                Check("classic shared skills render their distinct trigger and state identities",
                    () => ClassicGeneralUiChecks.SharedSkillIdentityMetadata(output));
                Assert(BindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, BindingErrors.Errors.Take(15)));
                Console.WriteLine($"{_passed} targeted WPF check passed. Renders: {output}");
                return 0;
            }
            if (args.Contains("--only-mou-lu-meng", StringComparer.Ordinal))
            {
                Check("formal Mou Lu Meng renders compound skill metadata and Hengye growth",
                    () => ClassicGeneralUiChecks.MouLuMengSkillState(output));
                Assert(BindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, BindingErrors.Errors.Take(15)));
                Console.WriteLine($"{_passed} targeted WPF check passed. Renders: {output}");
                return 0;
            }
            if (args.Contains("--only-sp-guan-yu", StringComparer.Ordinal))
            {
                Check("formal SP Guan Yu renders independent official art and complete skills",
                    () => ClassicGeneralUiChecks.SpGuanYuPortraitAndCard(output));
                Assert(BindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, BindingErrors.Errors.Take(15)));
                Console.WriteLine($"{_passed} targeted WPF check passed. Renders: {output}");
                return 0;
            }
            if (args.Contains("--only-yan-yan", StringComparer.Ordinal))
            {
                Check("formal Yan Yan renders independent official art and Juzhan state",
                    () => ClassicGeneralUiChecks.YanYanConversionCard(output));
                Assert(BindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, BindingErrors.Errors.Take(15)));
                Console.WriteLine($"{_passed} targeted WPF check passed. Renders: {output}");
                return 0;
            }
            if (args.Contains("--only-cao-zhang", StringComparer.Ordinal))
            {
                Check("formal Cao Zhang renders official art and the original Jiangchi choice",
                    () => ClassicGeneralUiChecks.CaoZhangJiangchiCard(output));
                Assert(BindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, BindingErrors.Errors.Take(15)));
                Console.WriteLine($"{_passed} targeted WPF check passed. Renders: {output}");
                return 0;
            }
            if (args.Contains("--only-ma-dai", StringComparer.Ordinal))
            {
                Check("formal Ma Dai renders official art and staged Qianxi",
                    () => ClassicGeneralUiChecks.MaDaiQianxiCard(output));
                Assert(BindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, BindingErrors.Errors.Take(15)));
                Console.WriteLine($"{_passed} targeted WPF check passed. Renders: {output}");
                return 0;
            }
            if (args.Contains("--only-gao-shun", StringComparer.Ordinal))
            {
                Check("formal Gao Shun renders official art and exact Xianzhen state",
                    () => ClassicGeneralUiChecks.GaoShunXianzhenCard(output));
                Assert(BindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, BindingErrors.Errors.Take(15)));
                Console.WriteLine($"{_passed} targeted WPF check passed. Renders: {output}");
                return 0;
            }
            if (args.Contains("--only-liu-biao", StringComparer.Ordinal))
            {
                Check("formal Liu Biao renders official art and exact Zishou state",
                    () => ClassicGeneralUiChecks.LiuBiaoZishouCard(output));
                Assert(BindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, BindingErrors.Errors.Take(15)));
                Console.WriteLine($"{_passed} targeted WPF check passed. Renders: {output}");
                return 0;
            }
            if (args.Contains("--only-wang-yi", StringComparer.Ordinal))
            {
                Check("formal Wang Yi renders official art and private Zhenlie/Miji prompts",
                    () => WangYiUiChecks.CardAndPrivatePrompts(output));
                Assert(BindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, BindingErrors.Errors.Take(15)));
                Console.WriteLine($"{_passed} targeted WPF check passed. Renders: {output}");
                return 0;
            }
            if (args.Contains("--only-zhong-hui", StringComparer.Ordinal))
            {
                Check("formal Zhong Hui renders official art, Authority and acquired Paiyi",
                    () => ZhongHuiUiChecks.CardPromptsAuthorityAndPaiyi(output));
                Assert(BindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, BindingErrors.Errors.Take(15)));
                Console.WriteLine($"{_passed} targeted WPF check passed. Renders: {output}");
                return 0;
            }
            if (args.Contains("--only-xun-you", StringComparer.Ordinal))
            {
                Check("formal Xun You renders official art and private Qice/Zhiyu interactions",
                    () => XunYouUiChecks.CardAndPrivatePrompts(output));
                Assert(BindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, BindingErrors.Errors.Take(15)));
                Console.WriteLine($"{_passed} targeted WPF check passed. Renders: {output}");
                return 0;
            }
            if (args.Contains("--only-liao-hua", StringComparer.Ordinal))
            {
                Check("formal Liao Hua renders official art, Dangxian and the private Fuli choice",
                    () => LiaoHuaUiChecks.CardExtraPhaseAndFuli(output));
                Assert(BindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, BindingErrors.Errors.Take(15)));
                Console.WriteLine($"{_passed} targeted WPF check passed. Renders: {output}");
                return 0;
            }
            if (args.Contains("--only-guan-xing-zhang-bao", StringComparer.Ordinal))
            {
                Check("formal Guan Xing and Zhang Bao render Fuhun draft and turn grant",
                    () => GuanXingZhangBaoUiChecks.CardDraftAndTurnGrant(output));
                Assert(BindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, BindingErrors.Errors.Take(15)));
                Console.WriteLine($"{_passed} targeted WPF check passed. Renders: {output}");
                return 0;
            }
            if (args.Contains("--only-bu-lian-shi", StringComparer.Ordinal))
            {
                Check("formal Bu Lian Shi renders official art, Anxu draft and Zhuiyi prompt",
                    () => BuLianShiUiChecks.CardAnxuAndZhuiyi(output));
                Assert(BindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, BindingErrors.Errors.Take(15)));
                Console.WriteLine($"{_passed} targeted WPF check passed. Renders: {output}");
                return 0;
            }
            Check("player guide renders current actions, private hand hints and searchable card rules", () => PlayerGuideChecks.ControlsAndSearch(output));
            Check("guide modal preserves selection and pauses then resumes the original timer policy", PlayerGuideChecks.ModalLifecycle);
            Check("general gallery combines registered series, faction and text filters without changing the match", () => CheckGeneralGallery(output));
            Check("general selection previews candidates before one explicit confirmation", () => CheckGeneralSelectionPreview(output));
            Check("new games reveal only the player's identity and objective before general selection", () => CheckIdentityReveal(output));
            Check("the human skill rail distinguishes available active and automatic skills", () => CheckHumanSkillRail(output));
            Check("mode lobby filters real identity, team and national entries without changing the match", () => CheckModeLobby(output));
            Check("lobby games reveal the dealt private opening hand without changing the match", () => CheckOpeningDealTransition(output));
            Check("battle report combines public event and related-seat filters without changing the match", () => CheckBattleLogFilters(output));
            Check("tutorial positions are deterministic real command histories", TutorialChecks.RealScenarios);
            Check("four tutorial lessons complete and restore the suspended match", () => TutorialChecks.CompleteCourseAndRestore(output));
            Check("layout and embedded portraits load without opening a window", () => CheckLayout(output));
            Check("large hands keep every card reachable", CheckLargeHand);
            Check("hand overflow supports wheel browsing and reveals newly selected cards", () => HandNavigationChecks.OverflowAndSelection(output));
            Check("selection, target toggle, cancel and play use legal actions", CheckSelections);
            Check("skill conversion is explicit and shares the confirmation flow", CheckConversions);
            Check("conversion choices retain exact provenance without leaking trusted action details", CardConversionUiChecks.Run);
            Check("expanded content exposes the active-skill command in WPF", ActiveSkillChecks.Controls);
            Check("composed skills share the generic WPF draft and submit their stable program identity", SkillProgramUiChecks.ActiveSelectionAndSubmission);
            Check("classic setup selects and persists standard or military physical decks", () => ClassicGeneralUiChecks.SelectableDeckExpansion(output));
            Check("formal Shen Guan Yu renders attributed classic art and complete skills", () => ClassicGeneralUiChecks.ShenGuanYuPortraitAndCard(output));
            Check("formal Yan Yan renders Juzhan conversion metadata and initial Yang state", () => ClassicGeneralUiChecks.YanYanConversionCard(output));
            Check("formal SP Guan Yu renders independent official art and complete skills", () => ClassicGeneralUiChecks.SpGuanYuPortraitAndCard(output));
            Check("formal Mou Lu Meng renders compound skill metadata and Hengye growth", () => ClassicGeneralUiChecks.MouLuMengSkillState(output));
            Check("formal Cao Zhang renders official art and original Jiangchi", () => ClassicGeneralUiChecks.CaoZhangJiangchiCard(output));
            Check("formal Ma Dai renders official art and staged Qianxi", () => ClassicGeneralUiChecks.MaDaiQianxiCard(output));
            Check("formal Gao Shun renders official art and exact Xianzhen state", () => ClassicGeneralUiChecks.GaoShunXianzhenCard(output));
            Check("formal Liu Biao renders official art and exact Zishou state", () => ClassicGeneralUiChecks.LiuBiaoZishouCard(output));
            Check("formal Wang Yi renders official art and private Zhenlie/Miji prompts", () => WangYiUiChecks.CardAndPrivatePrompts(output));
            Check("formal Zhong Hui renders official art, Authority and acquired Paiyi", () => ZhongHuiUiChecks.CardPromptsAuthorityAndPaiyi(output));
            Check("formal Xun You renders official art and private Qice/Zhiyu interactions", () => XunYouUiChecks.CardAndPrivatePrompts(output));
            Check("formal Liao Hua renders official art, Dangxian and the private Fuli choice", () => LiaoHuaUiChecks.CardExtraPhaseAndFuli(output));
            Check("formal Guan Xing and Zhang Bao render Fuhun draft and turn grant", () => GuanXingZhangBaoUiChecks.CardDraftAndTurnGrant(output));
            Check("formal Bu Lian Shi renders official art, Anxu draft and Zhuiyi prompt", () => BuLianShiUiChecks.CardAnxuAndZhuiyi(output));
            Check("classic identity renders and restores multiple skills with base HP", () => ClassicGeneralUiChecks.MultiSkillSelectionAndRestore(output));
            Check("classic Tiandu restores and claims through the WPF choice surface", () => ClassicGeneralUiChecks.TianduChoiceAndRestore(output));
            Check("classic Fanjian restores and resolves through the WPF suit choice surface", () => ClassicGeneralUiChecks.FanjianChoiceAndRestore(output));
            Check("classic Guanxing restores and orders private cards through the WPF choice surface", () => ClassicGeneralUiChecks.GuanxingChoiceAndRestore(output));
            Check("classic Hujia restores and requests Wei responses through the WPF choice surface", () => ClassicGeneralUiChecks.HujiaChoiceAndRestore(output));
            Check("classic Rende remains repeatable before Jijiang opens its separate target draft", () => ClassicGeneralUiChecks.JijiangActiveAction(output));
            Check("classic shared skills render their distinct trigger and state identities", () => ClassicGeneralUiChecks.SharedSkillIdentityMetadata(output));
            Check("skill drafts confirm through Enter and resume intact after guides and tutorials", () => SkillInteractionChecks.ConfirmAndResume(output));
            Check("hand responses select exact cards and confirm through shared controls", () => HandResponseChecks.Controls(output));
            Check("Wusheng hand responses restore versioned prompts and confirm explicit conversions", () => WushengResponseChecks.ControlsAndSavedRules(output));
            Check("Borrowed Sword exposes ordered targets and exact owner responses", () => BorrowedSwordUiChecks.OrderedTargetsAndOwnerResponse(output));
            Check("Stone Axe exposes exact two-card costs and commits through WPF", () => StoneAxeUiChecks.ExactCostResponse(output));
            Check("Zhangba selects two hand cards and one Slash target through WPF", () => ZhangbaUiChecks.ActiveDraft(output));
            Check("Cixiong restores and commits its staged private WPF choice", () => CixiongDoubleSwordsUiChecks.ActivationChoice(output));
            Check("Qinglong restores and commits an exact same-target Slash choice", () => QinglongCrescentBladeUiChecks.ExactFollowupChoice(output));
            Check("Ice Sword restores and commits sequential opaque target-card choices", () => IceSwordUiChecks.SequentialOpaqueChoices(output));
            Check("Qilin Bow restores and commits an exact public mount choice", () => QilinBowUiChecks.ExactMountChoice(output));
            Check("Mengjin restores and commits an opaque hand or public equipment choice", () => MengjinUiChecks.OpaqueTargetCardChoice(output));
            Check("Quhu restores and commits its winning attributed damage target", () => QuhuUiChecks.WinningDamageTarget(output));
            Check("dead Wuhun owners choose maximum Nightmare targets through WPF", () => WuhunUiChecks.DeathTargetChoice(output));
            Check("Shuangxiong restores its private draw replacement choice", () => ShuangxiongUiChecks.DrawChoice(output));
            Check("Fangtian Halberd restores and commits an exact multi-target Slash", () => FangtianHalberdUiChecks.ExactTargetCombination(output));
            Check("Guding Blade renders its locked empty-hand damage increase", () => GudingBladeUiChecks.LockedDamageFeedback(output));
            Check("Zhuque Fan exposes and renders its Fire Slash conversion", () => ZhuqueFanUiChecks.FireSlashConversionFeedback(output));
            Check("Tengjia renders its locked Fire damage increase", () => TengjiaUiChecks.FireDamageFeedback(output));
            Check("Silver Lion renders its locked damage cap", () => SilverLionUiChecks.DamageCapFeedback(output));
            Check("national Wusheng reveal during response refreshes the WPF controls", () => WushengResponseChecks.NationalRevealDuringResponse(output));
            Check("Iron Chain selects seats directly and preserves multi-target drafts through guides and tutorials", () => CardTargetChecks.DirectSelection(output));
            Check("recast uses a distinct action, public feedback and version-aware saved rules", () => RecastUiChecks.ControlsAndOldSaves(output));
            Check("response context distinguishes recipients, opponents and private prompts", DecisionContextChecks.Semantics);
            Check("match reports aggregate public outcomes without double counting", MatchSummaryChecks.Aggregation);
            Check("completed history survives relaunch, deduplicates endings and isolates damaged files", () => HistoryChecks.PersistenceAndFailures(output));
            Check("history modal preserves selected actions and pauses then resumes AI", () => HistoryChecks.ModalLifecycle(output));
            Check("device preferences persist before play and survive legacy loads and tutorials", () => PreferencesChecks.StartupAndMigration(output));
            Check("invalid preferences remain recoverable and slider writes coalesce safely", () => PreferencesChecks.FailuresAndDebounce(output));
            Check("settings modal groups real preferences and match pacing without changing the game", () => CheckSettingsPanel(output));
            Check("play advice uses private player views and preserves the original selection", () => AdviceChecks.ControlsAndPrivacy(output));
            Check("playback speed persists and dead players can pause and resume observation", () => PlaybackChecks.SettingsAndSpectating(output));
            Check("expanded rescue content renders and commits Jijiu dying choices", JijiuChecks.ControlsAndDying);
            Check("classic Jijiu renders and commits equipped rescue choices", () => JijiuChecks.EquipmentControls(output));
            Check("opaque target-card slots render privately and commit through WPF commands", () => TargetCardChecks.Controls(output));
            Check("new game settings and multi-card discard work through controls", () => CheckSetupAndDiscard(output));
            Check("team selection, guides, tutorial return and saved results follow the actual team", () => TeamExperienceChecks.ControlsAndRestore(output));
            Check("national dual-general controls preserve privacy, reveal and saved outcomes", () => NationalExperienceChecks.ControlsAndRestore(output));
            Check("national WPF matches reach faction results through player commands", NationalExperienceChecks.CompleteMatches);
            Check("six-player national controls preserve solo faction labels and saved reveals", () => NationalExperienceChecks.AmbitiousControlsAndRestore(output));
            Check("national Zhang Jiao mode preserves formal skills, hidden slots and package-aware saves", () => NationalExperienceChecks.ZhangJiaoControlsAndRestore(output));
            Check("dual-general health previews survive selection, tutorials and shipped saves", () => NationalHealthChecks.ControlsAndOldPackage(output));
            Check("dual portraits protect hidden slots and retain legacy skill semantics", NationalSeatChecks.PrivacyAndLegacy);
            Check("dual portraits project ordered multi-skill lists without leaking hidden slots", NationalSeatChecks.MultiSkillProjection);
            Check("dual-seat controls preserve targeting, half-reveal saves and public relationships", () => NationalSeatChecks.ControlsAndRelations(output));
            Check("auto advance pauses at a human decision and can be paused", CheckAutoAdvance);
            Check("saved UI boundaries restore and continue through actual files", () => PersistenceChecks.RoundTrips(output));
            Check("failed writes and incompatible reads preserve the active game", () => PersistenceChecks.FailedFilesPreserveGame(output));
            Check("autosave coalesces commands and resumes after closing", PersistenceChecks.AutomaticAndExit);
            Check("continue and save controls render in the small window", () => CheckSaveViews(output));
            Check("battle feedback exposes only committed public actions", FeedbackChecks.PublicProjection);
            Check("hand controls retain order and animation preferences persist", () => FeedbackChecks.HandAndPreferences(output));
            Check("actual battle feedback renders without changing decisions or intercepting input", () => FeedbackChecks.RenderAndLifecycle(output, args.Contains("--record-motion")));
            Check("audio follows committed actions and survives mute, background and device failure", AudioChecks.CommandRouting);
            Check("sound controls, shipped assets and compatible JSON preferences are valid", () => AudioChecks.SettingsAndAssets(output));
            if (args.Contains("--verify-native-audio")) Check("native WPF audio opens and completes every effect at zero volume", AudioChecks.NativeSilentPlayback);
            Check("complete matches can be played through the UI commands", () => CheckMatches(output));
            Assert(BindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, BindingErrors.Errors.Take(15)));
            Console.WriteLine($"{_passed} WPF checks passed. Renders: {output}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally { app.Shutdown(); }
    }

    private static void Check(string name, Action action)
    {
        action();
        _passed++;
        Console.WriteLine($"[PASS] {name}");
    }

    private static MainViewModel NewViewModel(int seed = 721019) => new(false, seed, showSetup: false, saveStore: new MemorySaveStore()) { IsMotionEnabled = false };

    private static void CheckSettingsPanel(string output)
    {
        using var vm = NewViewModel();
        var window = new MainWindow(vm);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var table = (FrameworkElement)window.FindName("TableSurface");
        var revision = Engine(vm).Revision;
        var state = SnapshotJson.Serialize(Engine(vm).State);

        vm.IsLogOpen = true;
        vm.OpenSettingsCommand.Execute(null);
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Assert(vm.IsSettingsOpen && !vm.IsLogOpen && !vm.IsHelpOpen && !vm.IsHistoryOpen && !vm.IsGeneralGalleryOpen,
            "Opening settings did not establish one modal surface.");
        Assert(!table.IsEnabled, "Settings did not block the table.");
        Assert(((CheckBox)window.FindName("SettingsSoundToggle")).IsChecked == vm.IsSoundEnabled &&
               ((CheckBox)window.FindName("SettingsMotionToggle")).IsChecked == vm.IsMotionEnabled &&
               ((CheckBox)window.FindName("SettingsAutoAdvanceToggle")).IsChecked == vm.IsAutoAdvance &&
               ((ListBox)window.FindName("SettingsPlaybackSpeedSelector")).Items.Count == vm.PlaybackSpeeds.Count,
            "Settings controls are not bound to the real preference and pacing state.");

        vm.IsSoundEnabled = !vm.IsSoundEnabled;
        vm.IsMotionEnabled = !vm.IsMotionEnabled;
        vm.SelectedPlaybackSpeed = vm.PlaybackSpeeds.Last();
        Render(root, 1120, 740, Path.Combine(output, "139-settings.png"));
        Assert(((FrameworkElement)window.FindName("SettingsPanel")).ActualWidth > 0,
            "Settings did not fit in the small window.");
        Assert(Engine(vm).Revision == revision && SnapshotJson.Serialize(Engine(vm).State) == state,
            "Changing presentation settings changed the match.");

        vm.CloseSettingsCommand.Execute(null);
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Assert(!vm.IsSettingsOpen && table.IsEnabled, "Closing settings left the table disabled.");

        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        Assert(vm.CanStepAi, "Settings timer fixture has no AI continuation.");
        vm.IsAutoAdvance = true;
        vm.OpenSettingsCommand.Execute(null);
        var paused = SnapshotJson.Serialize(Engine(vm).State);
        Pump(TimeSpan.FromMilliseconds(1400));
        Assert(SnapshotJson.Serialize(Engine(vm).State) == paused && vm.IsAutoAdvance,
            "Settings changed the game while the original auto timer was enabled.");
        vm.CloseSettingsCommand.Execute(null);
        Pump(TimeSpan.FromMilliseconds(750));
        Assert(SnapshotJson.Serialize(Engine(vm).State) != paused && vm.IsAutoAdvance,
            "Closing settings did not resume the original auto-advance policy.");
        vm.IsAutoAdvance = false;
    }

    private static void CheckGeneralGallery(string output)
    {
        using var vm = new MainViewModel(false, 721019, showSetup: false, saveStore: new MemorySaveStore(), useExpandedContent: true)
        {
            IsMotionEnabled = false
        };
        var window = new MainWindow(vm);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        vm.OpenGeneralGalleryCommand.Execute(null);
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Assert(vm.IsGeneralGalleryOpen && vm.GeneralGalleryEntries.Count == Engine(vm).ContentRegistry!.Generals.Count,
            "Gallery did not expose every registered general exactly once.");
        Assert(vm.GeneralGallerySeries.Select(option => option.Id).SequenceEqual(new[] { "all", "classic", "boundary", "standard", "national" }),
            "Gallery series do not match the registered content families.");
        Assert(!((FrameworkElement)window.FindName("TableSurface")).IsEnabled, "Gallery must block table input.");
        var revisionBeforeFilters = Engine(vm).Revision;
        vm.SelectGeneralGallerySeriesCommand.Execute("boundary");
        Assert(vm.GeneralGalleryEntries is [{ GeneralId: "boundary:zhang-jiao", Name: "界张角" }],
            "Boundary series must expose only the independently registered boundary Zhang Jiao.");
        vm.SelectGeneralGallerySeriesCommand.Execute("national");
        Assert(vm.GeneralGalleryEntries.Count == 14 && vm.GeneralGalleryEntries.All(entry => entry.SeriesId == "national") &&
               vm.GeneralGalleryEntries.Count(entry => entry.FactionId == "qun") == 2,
            "National series leaked another content family or omitted a registered trial general.");
        vm.SelectGeneralGalleryFactionCommand.Execute("wu");
        Assert(vm.GeneralGalleryEntries.Count == 0, "Combined national/Wu filters should reflect the current Wei/Shu/ambitious trial pool.");
        vm.SelectGeneralGalleryFactionCommand.Execute("wei");
        Assert(vm.GeneralGalleryEntries.Count == 6 && vm.GeneralGalleryEntries.All(entry => entry.FactionId == "wei" && entry.SeriesId == "national"),
            "Series and faction filters did not compose.");
        vm.SelectGeneralGallerySeriesCommand.Execute("classic");
        Assert(vm.GeneralGalleryEntries.Count > 0 && vm.GeneralGalleryEntries.All(entry => entry.FactionId == "wei" && entry.SeriesId == "classic"),
            "Classic series retained national entries.");
        vm.SelectGeneralGallerySeriesCommand.Execute("all");
        vm.SelectGeneralGalleryFactionCommand.Execute("all");
        vm.GeneralGallerySearchText = "连营";
        Assert(vm.GeneralGalleryEntries.Count == 1 && vm.GeneralGalleryEntries[0].Name == "陆逊", "Skill search did not find Lu Xun.");
        Assert(Engine(vm).Revision == revisionBeforeFilters, "Browsing gallery filters changed the match.");
        vm.GeneralGallerySearchText = string.Empty;
        Render(root, 1120, 740, Path.Combine(output, "130-general-gallery.png"));
        Assert(Find<Button>(root).Count(button =>
                   button.Command == vm.SelectGeneralGallerySeriesCommand && Equals(button.Tag, true)) == 1 &&
               Find<Button>(root).Count(button =>
                   button.Command == vm.SelectGeneralGalleryFactionCommand && Equals(button.Tag, true)) == 1,
            "Gallery does not visibly distinguish its selected series and faction filters.");

        vm.CloseGeneralGalleryCommand.Execute(null);
        var selectedGeneralId = vm.GeneralChoices[0].GeneralId;
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        for (var i = 0; i < 20 && !vm.CanEndTurn; i++)
        {
            AdvanceToDecision(vm);
            if (vm.SkillChoices.Count > 0) vm.SelectSkillChoiceCommand.Execute(vm.SkillChoices.Last());
        }
        Assert(vm.CanEndTurn,
            $"Gallery lifecycle setup did not reach the human play phase (status={Engine(vm).State.Status}, " +
            $"phase={Engine(vm).State.Phase}, prompt={Engine(vm).PendingDecision?.Kind}, seat={Engine(vm).PendingDecision?.PlayerSeat}, " +
            $"selectedGeneral={selectedGeneralId}).");
        vm.EndTurnCommand.Execute(null);
        if (vm.IsDiscardSelectionPending) ResolveDiscard(vm);
        Assert(vm.CanStepAi, $"Expected an AI boundary for modal pause verification: status={Engine(vm).State.Status}, phase={Engine(vm).State.Phase}, prompt={vm.PromptText}.");
        vm.IsAutoAdvance = true;
        vm.OpenGeneralGalleryCommand.Execute(null);
        var revision = Engine(vm).Revision;
        Pump(TimeSpan.FromMilliseconds(750));
        Assert(Engine(vm).Revision == revision, "AI advanced behind the general gallery.");
        vm.CloseGeneralGalleryCommand.Execute(null);
        Pump(TimeSpan.FromMilliseconds(750));
        Assert(Engine(vm).Revision > revision || vm.HasGameOver, "AI did not resume after closing the general gallery.");
        vm.IsAutoAdvance = false;
        Assert(BindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, BindingErrors.Errors.Take(10)));
        window.Content = null;
        window.Close();
    }

    private static void CheckGeneralSelectionPreview(string output)
    {
        using var vm = new MainViewModel(false, 721019, showSetup: false, saveStore: new MemorySaveStore(), useExpandedContent: true)
        {
            IsMotionEnabled = false
        };
        var window = new MainWindow(vm);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Assert(vm.IsGeneralSelectionPending && vm.SelectedGeneralChoice == vm.GeneralChoices[0] && vm.CanConfirmGeneralChoice,
            "The first candidate was not prepared as a reversible preview.");
        var revision = Engine(vm).Revision;
        var secondId = vm.GeneralChoices[1].GeneralId;
        vm.PreviewGeneralChoiceCommand.Execute(vm.GeneralChoices[1]);
        Assert(Engine(vm).Revision == revision && vm.SelectedGeneralChoice?.GeneralId == secondId &&
               vm.GeneralChoices.Count(choice => choice.IsPreviewSelected) == 1,
            "Previewing a second candidate committed a command or left ambiguous highlights.");
        vm.IsDeveloperView = true;
        vm.IsDeveloperView = false;
        Assert(vm.SelectedGeneralChoice?.GeneralId == secondId && Engine(vm).Revision == revision,
            "A presentation refresh lost or committed the candidate preview.");
        Render(root, 1120, 740, Path.Combine(output, "131-general-selection-confirm.png"));
        var confirm = (Button)window.FindName("ConfirmGeneralChoiceButton");
        Assert(confirm.IsEnabled && confirm.ActualHeight > 0 &&
               Find<Button>(root).Count(button => button.Command == vm.PreviewGeneralChoiceCommand) == vm.GeneralChoices.Count,
            "Candidate preview or confirmation controls are inaccessible.");
        vm.ConfirmGeneralChoiceCommand.Execute(null);
        Assert(Engine(vm).Revision > revision && !vm.IsGeneralSelectionPending && vm.SelectedGeneralChoice is null,
            "Explicit confirmation did not submit exactly one general choice.");
        Assert(BindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, BindingErrors.Errors.Take(10)));
        window.Content = null;
        window.Close();
    }

    private static void CheckIdentityReveal(string output)
    {
        using var vm = new MainViewModel(false, 721019, showSetup: true, saveStore: new MemorySaveStore(), useExpandedContent: true)
        {
            IsMotionEnabled = false
        };
        var window = new MainWindow(vm);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        vm.SelectedTableMode = vm.TableModes.Single(mode => mode.ModeId == "identity:classic-5");
        vm.SelectedStartingRole = vm.StartingRoles.Single(role => role.Role == Role.Rebel);
        vm.StartNewGameCommand.Execute(null);
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        var revision = Engine(vm).Revision;
        Assert(vm.IsIdentityRevealOpen && vm.IdentityRevealTitle == "身 份 揭 示" && vm.IdentityRevealRole == "反贼" &&
               vm.IdentityObjective.Contains("击败主公") && vm.IdentityRevealRoster.Contains("反贼 2"),
            "Identity reveal did not follow the actual five-player role and objective.");
        Render(root, 1120, 740, Path.Combine(output, "132-identity-reveal.png"));
        Assert(!((FrameworkElement)window.FindName("TableSurface")).IsEnabled && (vm.IsGeneralSelectionPending || vm.CanStepAi),
            $"Identity reveal did not block the prepared setup progression: table={((FrameworkElement)window.FindName("TableSurface")).IsEnabled}, general={vm.IsGeneralSelectionPending}, ai={vm.CanStepAi}, status={Engine(vm).State.Status}.");
        vm.ContinueFromIdentityRevealCommand.Execute(null);
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Assert(!vm.IsIdentityRevealOpen && Engine(vm).Revision == revision &&
               ((FrameworkElement)window.FindName("TableSurface")).IsEnabled,
            "Continuing from identity reveal changed the engine or left selection inaccessible.");

        vm.SelectedTableMode = vm.TableModes.Single(mode => mode.ModeId == "team:standard-2v2");
        vm.SelectedStartingTeam = vm.StartingTeams.Single(team => team.TeamId == "team:red");
        vm.StartNewGameCommand.Execute(null);
        Assert(vm.IsIdentityRevealOpen && vm.IdentityRevealTitle == "阵 营 揭 示" && vm.IdentityRevealRole == "赤队" &&
               vm.IdentityObjective.Contains("击败青队") && vm.IdentityRevealRoster.Contains("阵营公开"),
            "Public-team reveal exposed the wrong side or objective.");
        vm.ContinueFromIdentityRevealCommand.Execute(null);

        vm.SelectedTableMode = vm.TableModes.Single(mode => mode.ModeId == "national:lite-4");
        vm.StartNewGameCommand.Execute(null);
        Assert(vm.IsIdentityRevealOpen && vm.IdentityRevealTitle == "势 力 揭 示" && (vm.IdentityRevealRole is "魏" or "蜀") &&
               vm.IdentityObjective.Contains("势力同伴") && vm.IdentityRevealRoster.Contains("暗置"),
            "National reveal did not preserve the player's private faction context.");
        Assert(BindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, BindingErrors.Errors.Take(10)));
        window.Content = null;
        window.Close();
    }

    private static void CheckHumanSkillRail(string output)
    {
        MainViewModel? found = null;
        for (var seed = 1; seed <= 256; seed++)
        {
            var candidate = new MainViewModel(false, seed, showSetup: false, saveStore: new MemorySaveStore(), useExpandedContent: true)
            {
                IsMotionEnabled = false
            };
            var sunQuan = candidate.GeneralChoices.FirstOrDefault(choice => choice.GeneralId == "classic:sun-quan");
            if (sunQuan is not null)
            {
                candidate.SelectGeneralChoiceCommand.Execute(sunQuan);
                AdvanceToDecision(candidate);
                if (candidate.CanEndTurn) { found = candidate; break; }
            }
            candidate.Dispose();
        }

        using var vm = found ?? throw new InvalidOperationException("No bounded classic Sun Quan skill-rail fixture reached play.");
        var skills = vm.HumanSkillCards;
        Assert(skills.Count == 2 && skills.Any(skill => skill.Name == "制衡" && skill.TypeText == "主动技" && skill.IsAvailable && skill.StateText == "当前可发动") &&
               skills.Any(skill => skill.Name == "救援" && skill.TypeText == "状态技 · 主公技 · 锁定技" && !skill.IsAvailable && skill.StateText == "规则自动生效"),
            "The human skill rail did not distinguish an active entry from explicit execution forms and tags.");

        MainViewModel? rebelFound = null;
        for (var seed = 1; seed <= 256; seed++)
        {
            var candidate = new MainViewModel(false, seed, showSetup: true, saveStore: new MemorySaveStore(), useExpandedContent: true)
            {
                IsMotionEnabled = false
            };
            candidate.SelectedTableMode = candidate.TableModes.Single(mode => mode.ModeId == "identity:classic-5");
            candidate.SelectedStartingRole = candidate.StartingRoles.Single(role => role.Role == Role.Rebel);
            candidate.StartNewGameCommand.Execute(null);
            candidate.ContinueFromIdentityRevealCommand.Execute(null);
            AdvanceToDecision(candidate);
            var sunQuan = candidate.GeneralChoices.FirstOrDefault(choice => choice.GeneralId == "classic:sun-quan");
            if (sunQuan is not null)
            {
                candidate.SelectGeneralChoiceCommand.Execute(sunQuan);
                AdvanceToDecision(candidate);
                if (candidate.CanEndTurn)
                {
                    rebelFound = candidate;
                    break;
                }
            }
            candidate.Dispose();
        }

        using var rebelVm = rebelFound ??
            throw new InvalidOperationException("No bounded Rebel Sun Quan skill-rail fixture reached play.");
        Assert(rebelVm.HumanSkillCards is [{ Name: "制衡" }] &&
               rebelVm.HumanSkillCards.All(skill => !skill.TypeText.Contains("主公技", StringComparison.Ordinal)),
            "A non-Lord skill rail still displayed a printed Lord skill as owned.");

        MainViewModel? lockedFound = null;
        for (var seed = 1; seed <= 256; seed++)
        {
            var candidate = new MainViewModel(false, seed, showSetup: false, saveStore: new MemorySaveStore(), useExpandedContent: true)
            {
                IsMotionEnabled = false
            };
            var zhangFei = candidate.GeneralChoices.FirstOrDefault(choice => choice.GeneralId == "classic:zhang-fei");
            if (zhangFei is not null)
            {
                candidate.SelectGeneralChoiceCommand.Execute(zhangFei);
                AdvanceToDecision(candidate);
                if (candidate.CanEndTurn)
                {
                    lockedFound = candidate;
                    break;
                }
            }
            candidate.Dispose();
        }

        using var lockedVm = lockedFound ??
            throw new InvalidOperationException("No bounded classic Zhang Fei skill-rail fixture reached play.");
        Assert(lockedVm.HumanSkillCards is
            [{ Name: "咆哮", TypeText: "状态技 · 锁定技", StateText: "规则自动生效", IsAvailable: false }],
            "The current Paoxiao rail still used the legacy ambiguous skill type text.");

        var revision = Engine(vm).Revision;
        var window = new MainWindow(vm);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        Render(root, 1120, 740, Path.Combine(output, "133-human-skill-rail.png"));
        var panel = (FrameworkElement)window.FindName("HumanSkillPanel");
        var cards = (ItemsControl)window.FindName("HumanSkillCards");
        Assert(panel.ActualWidth >= 180 && panel.ActualHeight > 170 && cards.Items.Count == 2 &&
               Find<TextBlock>(cards).Any(text => text.Text == "当前可发动" && text.ActualHeight > 0),
            "The human skill rail or its actionable state is inaccessible in the minimum window.");
        Assert(Engine(vm).Revision == revision, "Rendering the human skill rail changed the game.");
        Assert(BindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, BindingErrors.Errors.Take(10)));
        window.Content = null;
        window.Close();

        var rebelWindow = new MainWindow(rebelVm);
        rebelWindow.ApplyTemplate();
        var rebelRoot = (FrameworkElement)rebelWindow.Content;
        Render(rebelRoot, 1120, 740, Path.Combine(output, "165-rebel-lord-skill-qualification.png"));
        var rebelCards = (ItemsControl)rebelWindow.FindName("HumanSkillCards");
        Assert(rebelCards.Items.Count == 1 &&
               Find<TextBlock>(rebelCards).All(text => text.Text != "救援" && !text.Text.Contains("主公技", StringComparison.Ordinal)),
            "The rendered non-Lord skill rail exposed Jiuyuan or a Lord-skill label.");
        rebelWindow.Content = null;
        rebelWindow.Close();

        var lockedWindow = new MainWindow(lockedVm);
        lockedWindow.ApplyTemplate();
        var lockedRoot = (FrameworkElement)lockedWindow.Content;
        Render(lockedRoot, 1120, 740, Path.Combine(output, "167-locked-state-skill-metadata.png"));
        var lockedCards = (ItemsControl)lockedWindow.FindName("HumanSkillCards");
        Assert(lockedCards.Items.Count == 1 &&
               Find<TextBlock>(lockedCards).Any(text => text.Text == "状态技 · 锁定技" && text.ActualHeight > 0),
            "The rendered locked-state label is inaccessible in the minimum window.");
        lockedWindow.Content = null;
        lockedWindow.Close();
    }

    private static void CheckModeLobby(string output)
    {
        using var vm = new MainViewModel(false, 721019, showSetup: true, saveStore: new MemorySaveStore(), useExpandedContent: true)
        {
            IsMotionEnabled = false
        };
        var revision = Engine(vm).Revision;
        var window = new MainWindow(vm);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        Assert(vm.VisibleTableModes.Count == 11 && vm.SelectedModeCategory.Id == "all" &&
               vm.VisibleTableModes.Select(mode => mode.ModeBadge).Distinct().Count() == 5,
            "The expanded mode lobby did not expose all registered entry families.");
        Render(root, 1120, 740, Path.Combine(output, "134-mode-lobby.png"));
        var lobby = (ListBox)window.FindName("TableModeChoices");
        Assert(lobby.ActualHeight > 0 && lobby.Items.Count == 11 &&
               (ListBox)window.FindName("ModeCategoryChoices") is { Items.Count: 4 },
            "Mode cards or category controls are inaccessible.");

        vm.SelectedModeCategory = vm.ModeCategories.Single(category => category.Id == "national");
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Assert(vm.VisibleTableModes.Count == 3 && vm.VisibleTableModes.All(mode => mode.ModeId.StartsWith("national:")) &&
               vm.SelectedTableMode.ModeId.StartsWith("national:") && vm.IsNationalModeSelection,
            "National category leaked another mode or retained an invalid selection.");
        vm.SelectedModeCategory = vm.ModeCategories.Single(category => category.Id == "team");
        Assert(vm.VisibleTableModes.Count == 1 && vm.SelectedTableMode.ModeId == "team:standard-2v2" && vm.IsTeamModeSelection,
            "Team category did not select its real registered entry.");
        vm.SelectedModeCategory = vm.ModeCategories.Single(category => category.Id == "identity");
        Assert(vm.VisibleTableModes.Count == 7 && vm.VisibleTableModes.All(mode => mode.ModeId.StartsWith("identity:")) && vm.IsIdentityModeSelection &&
               vm.VisibleTableModes.Count(mode => mode.ModeId.Contains("classic-boundary", StringComparison.Ordinal)) == 2,
            "Identity category did not expose its seven actual modes including both boundary rosters.");
        Assert(Engine(vm).Revision == revision && vm.IsNewGameSetupOpen,
            "Browsing the mode lobby changed or replaced the suspended match.");
        Assert(BindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, BindingErrors.Errors.Take(10)));
        window.Content = null;
        window.Close();
    }

    private static void CheckOpeningDealTransition(string output)
    {
        using var vm = new MainViewModel(false, 721019, showSetup: true, saveStore: new MemorySaveStore(), useExpandedContent: true)
        {
            IsMotionEnabled = true
        };
        var window = new MainWindow(vm);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(1120, 740));
        root.Arrange(new Rect(0, 0, 1120, 740));
        root.UpdateLayout();
        var start = Find<Button>(root).Single(button => Equals(button.Content, "开 始 对 局"));
        Assert(start.Command == vm.StartNewGameFromLobbyCommand, "The mode lobby bypasses the opening-deal presentation command.");
        start.Command.Execute(start.CommandParameter);
        Assert(vm.IsIdentityRevealOpen && !vm.IsOpeningDealVisible, "The opening hand appeared before the private identity reveal.");
        vm.ContinueFromIdentityRevealCommand.Execute(null);
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        AdvanceToDecision(vm);

        Assert(vm.IsOpeningDealVisible && vm.OpeningHandCards.Count >= 4 &&
               vm.OpeningHandCards.All(dealt => vm.Hand.Any(card => card.Id == dealt.Id)),
            "The opening transition did not expose the complete dealt human hand.");
        var dealtIds = vm.OpeningHandCards.Select(card => card.Id).ToArray();
        Assert(vm.OpeningHandCards.Select(card => card.Id).SequenceEqual(dealtIds),
            "The opening transition did not preserve the private hand order.");
        var revision = Engine(vm).Revision;
        var state = SnapshotJson.Serialize(Engine(vm).State);
        Render(root, 1120, 740, Path.Combine(output, "135-opening-deal.png"));
        Assert(((FrameworkElement)window.FindName("OpeningDealOverlay")).ActualHeight > 0 &&
               ((Button)window.FindName("DismissOpeningDealButton")).IsEnabled,
            "The opening transition or its skip control is inaccessible.");

        vm.DismissOpeningDealCommand.Execute(null);
        Assert(!vm.IsOpeningDealVisible && vm.OpeningHandCards.Count == 0,
            "Dismissing the opening transition left private cards in its presentation buffer.");
        Assert(Engine(vm).Revision == revision && SnapshotJson.Serialize(Engine(vm).State) == state &&
               dealtIds.All(id => vm.Hand.Any(card => card.Id == id)),
            "Dismissing the presentation changed the match or lost a dealt opening card.");
        Assert(BindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, BindingErrors.Errors.Take(10)));
        window.Content = null;
        window.Close();
    }

    private static void CheckBattleLogFilters(string output)
    {
        using var vm = NewViewModel();
        var window = new MainWindow(vm);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        AdvanceToDecision(vm);
        var action = Engine(vm).GetHumanLegalActions().First(candidate =>
            candidate.CardId is not null && candidate.TargetSeats.Count <= 1 && candidate.TargetCardId is null);
        vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == action.CardId));
        if (action.TargetSeat is { } target)
            vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == target));
        Assert(vm.CanConfirmSelected, "Battle-report fixture could not commit its public card action.");
        vm.ConfirmSelectedCommand.Execute(null);

        Assert(vm.FilteredBattleLog.Count == vm.GameLog.Count && vm.BattleLogCategories.Count == 6 &&
               vm.BattleLogSeats.Count == vm.Seats.Count + 2,
            "Battle report did not index every public log or expose complete filters.");
        var revision = Engine(vm).Revision;
        var state = SnapshotJson.Serialize(Engine(vm).State);
        vm.SelectBattleLogCategoryCommand.Execute("action");
        Assert(vm.FilteredBattleLog.Count > 0 && vm.FilteredBattleLog.All(entry => entry.CategoryId == "action"),
            "Action filter leaked another public event category.");
        vm.SelectBattleLogSeatCommand.Execute("seat:0");
        Assert(vm.FilteredBattleLog.Count > 0 && vm.FilteredBattleLog.All(entry => entry.ActorSeat == 0 || entry.TargetSeat == 0),
            "Related-seat filter leaked an unrelated record.");
        vm.SelectBattleLogCategoryCommand.Execute("all");
        vm.SelectBattleLogSeatCommand.Execute("system");
        Assert(vm.FilteredBattleLog.Count > 0 && vm.FilteredBattleLog.All(entry => entry.ActorSeat is null && entry.TargetSeat is null),
            "System filter included a player-attributed record.");
        vm.SelectBattleLogSeatCommand.Execute("all");
        vm.IsLogOpen = true;
        Render(root, 1120, 740, Path.Combine(output, "138-filtered-battle-report.png"));
        Assert(((ListBox)window.FindName("FilteredBattleLogList")).Items.Count == vm.FilteredBattleLog.Count &&
               Find<Button>(root).Count(button => button.Command == vm.SelectBattleLogCategoryCommand && button.ActualHeight > 0) == 6,
            "Battle-report filter controls or filtered rows are inaccessible.");
        Assert(Find<Button>(root).Count(button =>
                   button.Command == vm.SelectBattleLogCategoryCommand && Equals(button.Tag, true)) == 1 &&
               Find<Button>(root).Count(button =>
                   button.Command == vm.SelectBattleLogSeatCommand && Equals(button.Tag, true)) == 1,
            "Battle report does not visibly distinguish its selected category and seat filters.");
        Assert(Engine(vm).Revision == revision && SnapshotJson.Serialize(Engine(vm).State) == state,
            "Browsing battle-report filters changed the match.");
        Assert(BindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, BindingErrors.Errors.Take(10)));
        window.Content = null;
        window.Close();
    }

    internal static GameEngine Engine(MainViewModel vm) =>
        (GameEngine)typeof(MainViewModel).GetField("_game", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;

    internal static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void CheckLayout(string output)
    {
        using var vm = NewViewModel();
        var window = new MainWindow(vm);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        Render(root, 1440, 860, Path.Combine(output, "01-general-selection.png"));
        Assert(vm.GeneralChoices.Count == 3, "Expected three private generals.");
        foreach (var choice in vm.GeneralChoices) Assert(choice.PortraitBrush is ImageBrush, "Missing portrait.");
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        AdvanceToDecision(vm);
        Render(root, 1440, 860, Path.Combine(output, "02-table.png"));
        Assert(Find<HandPanel>(root).Any(), "Hand layout did not materialize.");
        Assert(Find<Button>(root).Any(button => button.Visibility == Visibility.Visible && button.ActualHeight > 0 && Equals(button.Content, "结束出牌")), "End-turn control missing.");
        Render(root, 1120, 740, Path.Combine(output, "03-small-table.png"));
        var visibleButtons = Find<Button>(root).Where(button => button.Visibility == Visibility.Visible && button.IsEnabled && button.ActualHeight > 0).ToArray();
        Assert(visibleButtons.Length >= 8, "Player controls missing.");
        var slash = vm.Hand.First(card => card.Name == "杀");
        vm.SelectCardCommand.Execute(slash);
        vm.SelectTargetCommand.Execute(vm.Seats.First(seat => seat.IsLegalTarget));
        Render(root, 1440, 860, Path.Combine(output, "04-selected-target.png"));
        Assert(vm.CanConfirmSelected && !vm.HasAlternateSlash, "A physical Slash should not offer a redundant conversion.");
        vm.ClearSelectionCommand.Execute(null);
        for (var i = 0; i < 16 && !vm.HasChoicePrompt && !vm.HasGameOver; i++)
        {
            if (vm.IsDiscardSelectionPending) ResolveDiscard(vm);
            if (vm.CanEndTurn) vm.EndTurnCommand.Execute(null);
            AdvanceToDecision(vm);
        }
        Assert(vm.HasChoicePrompt, "Fixture did not reach an actual response.");
        Render(root, 1440, 860, Path.Combine(output, "05-response.png"));
        Assert(vm.IsHarvestSelectionPending, "Fixture should exercise picking a public card.");
        var publicCard = vm.PublicRevealedCards.First();
        Assert(vm.SelectRevealedCardCommand.CanExecute(publicCard), "Public card is not clickable.");
        var beforeChoice = Engine(vm).Revision;
        vm.SelectRevealedCardCommand.Execute(publicCard);
        Assert(Engine(vm).Revision > beforeChoice && !vm.PublicRevealedCards.Any(card => card.Id == publicCard.Id), "Public card selection did not commit.");
        vm.IsLogOpen = true;
        vm.IsDeveloperView = true;
        Render(root, 1120, 740, Path.Combine(output, "06-report.png"));
        Assert(BindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, BindingErrors.Errors.Take(15)));
        window.Content = null;
        window.Close();
    }

    internal static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Find<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    internal static void Render(FrameworkElement root, int width, int height, string path)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static void CheckLargeHand()
    {
        var panel = new HandPanel { ViewportWidth = 720 };
        for (var i = 0; i < 30; i++) panel.Children.Add(new Border { Width = 110, Height = 160 });
        panel.Measure(new Size(double.PositiveInfinity, 190));
        panel.Arrange(new Rect(panel.DesiredSize));
        Assert(panel.DesiredSize.Width > 720, "Dense hand needs a scrollable extent.");
        var lastX = -1.0;
        foreach (FrameworkElement child in panel.Children)
        {
            var x = child.TranslatePoint(new Point(), panel).X;
            Assert(x > lastX, "Card edge is unreachable.");
            Assert(x + child.ActualWidth <= panel.ActualWidth + 1, "Last card is clipped.");
            lastX = x;
        }
    }

    private static void CheckSelections()
    {
        using var vm = NewViewModel();
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        AdvanceToDecision(vm);
        var engine = Engine(vm);
        var action = engine.GetHumanLegalActions().First(a => a.CardId.HasValue && a.TargetSeats.Count == 1 && a.TargetCardId is null);
        var card = vm.Hand.Single(c => c.Id == action.CardId);
        vm.SelectCardCommand.Execute(card);
        var target = vm.Seats.Single(seat => seat.Seat == action.TargetSeat);
        Assert(target.IsLegalTarget, "Legal target is not highlighted.");
        if (target.IsSelectedTarget) vm.SelectTargetCommand.Execute(target);
        vm.SelectTargetCommand.Execute(target);
        Assert(vm.CanPlaySelected && target.IsSelectedTarget, "Selecting the target must enable confirmation.");
        vm.SelectTargetCommand.Execute(target);
        Assert(!target.IsSelectedTarget, "Clicking the target again should deselect it.");
        vm.ClearSelectionCommand.Execute(null);
        Assert(!vm.HasSelection && vm.Hand.All(c => !c.IsSelected) && vm.Seats.All(s => !s.IsSelectedTarget), "Cancel left stale highlights.");
        vm.SelectCardCommand.Execute(card);
        if (!target.IsSelectedTarget) vm.SelectTargetCommand.Execute(target);
        var revision = engine.Revision;
        vm.PlaySelectedCardCommand.Execute(null);
        Assert(engine.Revision > revision, "Confirmed card was not played.");
        Assert(!vm.HasSelection, "Selection did not clear after play.");
        Assert(!vm.PromptText.StartsWith("操作未执行"), vm.PromptText);
    }

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void CheckConversions()
    {
        using var vm = NewViewModel();
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices.Single(choice => choice.Name == "关羽"));
        AdvanceToDecision(vm);
        var engine = Engine(vm);
        var peach = vm.Hand.Single(card => card.Name == "桃");
        Assert(!peach.IsPlayable, "Black Peach should not be usable at full health with Wusheng.");
        var redCard = vm.Hand.First(card => card.Name != "杀" && card.SuitGlyph is "♥" or "♦");
        var action = engine.GetHumanLegalActions().First(a => a.CardId == redCard.Id && a.PlayedCardKind == CardKind.Slash);
        vm.SelectCardCommand.Execute(redCard);
        vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == action.TargetSeat));
        Assert(vm.CanPlaySelectedAsSlash, "Red card conversion should be available.");
        var revision = engine.Revision;
        vm.PlaySelectedAsSlashCommand.Execute(null);
        Assert(engine.Revision > revision && !vm.HasSelection, "Wusheng did not commit and clear the selected card.");
        Assert(vm.RecentPlays.Any(play => play.Name == "杀"), "Table should show the effective public card name.");
        Assert(vm.BattleCues.Any(cue => cue.Kind == CardGame.Wpf.Presentation.BattleCueKind.Card && cue.Label == "杀"), "Converted Slash feedback exposed the physical card name.");
    }

    private static void CheckAutoAdvance()
    {
        using var vm = NewViewModel();
        vm.IsAutoAdvance = true;
        var revision = Engine(vm).Revision;
        Pump(TimeSpan.FromMilliseconds(750));
        Assert(vm.IsGeneralSelectionPending && Engine(vm).Revision == revision, "AI crossed private general selection.");
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        vm.IsAutoAdvance = false;
        AdvanceToDecision(vm);
        revision = Engine(vm).Revision;
        vm.IsAutoAdvance = true;
        Pump(TimeSpan.FromMilliseconds(750));
        Assert(vm.CanEndTurn && Engine(vm).Revision == revision, "AI played on behalf of the human.");
        vm.EndTurnCommand.Execute(null);
        revision = Engine(vm).Revision;
        Pump(TimeSpan.FromMilliseconds(750));
        Assert(Engine(vm).Revision > revision || vm.HasGameOver, "AI did not resume after ending play.");
        Assert(vm.IsDiscardSelectionPending, "Auto advance should pause for the player's discard.");
        vm.IsAutoAdvance = false;
        revision = Engine(vm).Revision;
        Pump(TimeSpan.FromMilliseconds(750));
        Assert(Engine(vm).Revision == revision, "Pause did not stop auto advance.");
        ResolveDiscard(vm);
        vm.IsAutoAdvance = true;
        vm.NewGameCommand.Execute(null);
        revision = Engine(vm).Revision;
        Pump(TimeSpan.FromMilliseconds(750));
        Assert(Engine(vm).Revision == revision, "AI should pause behind the new-game settings.");
        vm.CancelNewGameSetupCommand.Execute(null);
        Pump(TimeSpan.FromMilliseconds(750));
        Assert(Engine(vm).Revision > revision || vm.HasGameOver, "Canceling setup should resume the same match.");
    }

    internal static void AdvanceToDecision(MainViewModel vm)
    {
        for (var i = 0; i < 10000 && vm.CanStepAi; i++) vm.StepAiCommand.Execute(null);
    }

    internal static void ResolveDiscard(MainViewModel vm)
    {
        vm.RecommendDiscardCommand.Execute(null);
        Assert(vm.CanConfirmSelected, "Recommended discard has the wrong size.");
        vm.ConfirmSelectedCommand.Execute(null);
        Assert(!vm.IsDiscardSelectionPending && vm.SelectedDiscardCount == 0, "Discard did not finish.");
    }

    private static void CheckSetupAndDiscard(string output)
    {
        using var vm = NewViewModel();
        var window = new MainWindow(vm);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var previous = SnapshotJson.Serialize(Engine(vm).State);
        vm.NewGameCommand.Execute(null);
        Render(root, 1120, 740, Path.Combine(output, "07-new-game.png"));
        Assert(vm.IsNewGameSetupOpen, "New-game settings did not open.");
        var table = (FrameworkElement)window.FindName("TableSurface");
        Assert(!table.IsEnabled, "Settings must prevent focus and actions on the underlying table.");
        vm.CancelNewGameSetupCommand.Execute(null);
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Assert(table.IsEnabled, "Canceling setup left the table disabled.");
        Assert(SnapshotJson.Serialize(Engine(vm).State) == previous, "Canceling new game changed the match.");
        var teamSetupRendered = false;
        foreach (var mode in vm.TableModes)
        {
            if (mode.ModeId.StartsWith("national:", StringComparison.Ordinal)) continue;
            foreach (var role in vm.StartingRoles)
            {
                vm.SelectedTableMode = mode;
                vm.SelectedStartingRole = role;
                if (mode.ModeId == "team:standard-2v2" && !teamSetupRendered)
                {
                    vm.NewGameCommand.Execute(null);
                    root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                    Render(root, 1120, 740, Path.Combine(output, "07-team-setup.png"));
                    Assert(((ListBox)window.FindName("RoleChoices")).Visibility == Visibility.Collapsed,
                        "Public-team setup should hide identity choices.");
                    Assert(((FrameworkElement)window.FindName("TableModeChoices")).ActualHeight > 0,
                        "Public-team mode choice is not visible.");
                    vm.CancelNewGameSetupCommand.Execute(null);
                    teamSetupRendered = true;
                }
                vm.StartNewGameCommand.Execute(null);
                Assert(vm.Seats.Count == mode.PlayerCount && vm.TopSeats.Count == mode.PlayerCount - 3, "Table size was not applied.");
                Assert(vm.RightPlayer!.Seat == mode.PlayerCount - 1, "Last opponent is not visible.");
                var human = Engine(vm).State.Players.Single(player => player.IsHuman);
                if (mode.ModeId == "team:standard-2v2")
                {
                    Assert(human.TeamId == "team:blue" && human.Role is Role.TeamA or Role.TeamB,
                        "Public-team setup did not apply the human team.");
                    Assert(vm.TableModeText.Contains("2v2公开阵营") && vm.IdentityObjective.Contains("青队"),
                        "Public-team presentation did not switch its objective text.");
                }
                else
                {
                    Assert(role.Role is null || human.Role == role.Role, "Chosen identity was not applied.");
                }
                Assert(vm.GeneralSelectionSubtitle.Contains(vm.HumanPlayer!.RoleLabel), "Selection screen shows the wrong identity.");
            }
        }
        vm.SelectedTableMode = vm.TableModes.Single(mode => mode.PlayerCount == 5);
        vm.SelectedStartingRole = vm.StartingRoles.Single(role => role.Role == Role.Lord);
        vm.StartNewGameCommand.Execute(null);
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        AdvanceToDecision(vm);
        Render(root, 1120, 740, Path.Combine(output, "08-five-player.png"));
        vm.EndTurnCommand.Execute(null);
        AdvanceToDecision(vm);
        Assert(vm.IsDiscardSelectionPending && vm.Hand.All(card => card.IsPlayable), "Discard hand should allow all candidate cards.");
        vm.SelectCardCommand.Execute(vm.Hand[0]);
        Assert(vm.SelectedDiscardCount == 1, "Discard card did not select.");
        vm.SelectCardCommand.Execute(vm.Hand[0]);
        Assert(vm.SelectedDiscardCount == 0 && !vm.CanConfirmSelected, "Discard card did not toggle off.");
        foreach (var card in vm.Hand.Take(vm.RequiredDiscardCount + 1)) vm.SelectCardCommand.Execute(card);
        Assert(!vm.CanConfirmSelected, "Over-selection should not enable discard.");
        vm.ClearSelectionCommand.Execute(null);
        Assert(vm.SelectedDiscardCount == 0, "Cancel did not clear discard selection.");
        vm.RecommendDiscardCommand.Execute(null);
        Assert(vm.CanConfirmSelected && vm.SelectedDiscardCount == vm.RequiredDiscardCount, "Recommendation did not fill exact count.");
        Render(root, 1120, 740, Path.Combine(output, "09-discard-selection.png"));
        Assert(Find<Button>(root).Any(button => button.Command == vm.ConfirmSelectedCommand && button.IsEnabled && button.Visibility == Visibility.Visible), "Discard confirmation is inaccessible.");
        var chosen = vm.Hand.Where(card => card.IsSelected).Select(card => card.Id).ToArray();
        vm.ConfirmSelectedCommand.Execute(null);
        Assert(!vm.IsDiscardSelectionPending && chosen.All(id => vm.Hand.All(card => card.Id != id)), "Selected cards were not discarded.");
        Assert(BindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, BindingErrors.Errors.Take(10)));
        window.Content = null;
        window.Close();
    }

    private static void CheckSaveViews(string output)
    {
        var store = new MemorySaveStore();
        using var vm = new MainViewModel(false, 721019, false, store) { IsMotionEnabled = false };
        var window = new MainWindow(vm);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        AdvanceToDecision(vm);
        vm.SaveGameCommand.Execute(null);
        Assert(vm.FlushPendingSave() && vm.HasAnySave, vm.SaveStatus);
        vm.NewGameCommand.Execute(null);
        Render(root, 1120, 740, Path.Combine(output, "10-continue-game.png"));
        var resume = Find<Button>(root).Single(button => Equals(button.Content, "继续上次对局"));
        Assert(resume.IsEnabled && resume.ActualHeight > 0 && resume.TranslatePoint(new Point(), root).Y >= 0, "Continue control is inaccessible.");
        vm.CancelNewGameSetupCommand.Execute(null);
        vm.IsLogOpen = true;
        Render(root, 1120, 740, Path.Combine(output, "11-save-drawer.png"));
        var manualButton = (Button)window.FindName("LoadManualSaveButton");
        Assert(manualButton.IsEnabled && manualButton.ActualHeight > 0 && manualButton.TranslatePoint(new Point(), root).X >= 760, "Manual load control is inaccessible.");
        vm.NewGameCommand.Execute(null);
        var incompatible = store.Read(GameSaveSlot.Manual);
        store.Write(GameSaveSlot.Manual, incompatible with { Checkpoint = incompatible.Checkpoint with { SchemaVersion = 999 } });
        vm.LoadManualGameCommand.Execute(null);
        Render(root, 1120, 740, Path.Combine(output, "12-save-error.png"));
        Assert(vm.HasSaveError && vm.IsNewGameSetupOpen, "Failed load should leave the settings visible with an explanation.");
        Assert(BindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, BindingErrors.Errors.Take(10)));
        window.Content = null;
        window.Close();
    }

    private static void CheckMatches(string output)
    {
        PlayerGuideChecks.ObservedPrompts.Clear();
        var responseCount = 0;
        var playedCount = 0;
        var recastCount = 0;
        foreach (var (seed, count, role) in new[] { (721019, 8, Role.Lord), (561923, 8, Role.Lord), (402155, 8, Role.Lord), (827331, 5, Role.Rebel), (174322, 8, Role.Renegade) })
        {
            using var vm = NewViewModel(seed);
            var outcomes = new List<GameSound>();
            vm.SoundsRequested += (_, args) => outcomes.AddRange(args.Sounds.Where(sound => sound is GameSound.Victory or GameSound.Defeat or GameSound.Draw));
            vm.SelectedTableMode = vm.TableModes.Single(mode => mode.PlayerCount == count);
            vm.SelectedStartingRole = vm.StartingRoles.Single(option => option.Role == role);
            vm.StartNewGameCommand.Execute(null);
            var engine = Engine(vm);
            for (var step = 0; step < 18000 && !vm.HasGameOver; step++)
            {
                PlayerGuideChecks.CheckCurrentState(vm);
                if (vm.IsGeneralSelectionPending) vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
                else if (vm.IsDiscardSelectionPending) ResolveDiscard(vm);
                else if (vm.CanStepAi) vm.StepAiCommand.Execute(null);
                else if (vm.CanEndTurn)
                {
                    AdviceChecks.VerifyLive(vm);
                    var action = engine.GetHumanLegalActions().FirstOrDefault(a => a.CardId.HasValue && a.TargetSeats.Count <= 1 && a.TargetCardId is null);
                    if (action is null) vm.EndTurnCommand.Execute(null);
                    else
                    {
                        vm.SelectCardCommand.Execute(vm.Hand.Single(c => c.Id == action.CardId));
                        if (action.TargetSeat is { } seat && !vm.Seats.Single(s => s.Seat == seat).IsSelectedTarget)
                            vm.SelectTargetCommand.Execute(vm.Seats.Single(s => s.Seat == seat));
                        if (action.Kind == LegalActionKind.Recast && vm.CanRecastSelected) { vm.RecastSelectedCommand.Execute(null); recastCount++; }
                        else if (vm.CanConfirmSelected) { vm.ConfirmSelectedCommand.Execute(null); playedCount++; }
                        else vm.EndTurnCommand.Execute(null);
                    }
                }
                else
                {
                    var choices = new (IEnumerable<PromptChoice>, ICommand)[]
                    {
                        (vm.ResponseChoices, vm.SelectResponseChoiceCommand), (vm.DyingChoices, vm.SelectDyingChoiceCommand),
                        (vm.HarvestChoices, vm.SelectHarvestChoiceCommand), (vm.TargetCardChoices, vm.SelectTargetCardChoiceCommand),
                        (vm.FireAttackChoices, vm.SelectFireAttackChoiceCommand),
                        (vm.NullificationChoices, vm.SelectNullificationChoiceCommand), (vm.SkillChoices, vm.SelectSkillChoiceCommand)
                    };
                    var found = choices.FirstOrDefault(pair => pair.Item1.Any());
                    Assert(found.Item1 is not null, $"UI has no action at {engine.State.Status}.");
                    found.Item2.Execute(found.Item1!.First());
                    responseCount++;
                }
                Assert(!vm.PromptText.Contains("未执行"), $"Seed {seed}: {vm.PromptText}");
            }
            Assert(vm.HasGameOver, $"Seed {seed} did not reach results.");
            PlayerGuideChecks.CheckCurrentState(vm);
            Assert(!string.IsNullOrWhiteSpace(vm.GameOverText), "Winner missing from results.");
            Assert(vm.Seats.All(seat => seat.RoleLabel != "?"), "Identities should be revealed at game over.");
            var expectedOutcome = engine.State.Winner switch
            {
                Winner.Draw => GameSound.Draw,
                Winner.LordAndLoyalists when role is Role.Lord or Role.Loyalist => GameSound.Victory,
                Winner.Rebels when role == Role.Rebel => GameSound.Victory,
                Winner.Renegade when role == Role.Renegade => GameSound.Victory,
                _ => GameSound.Defeat
            };
            Assert(outcomes.SequenceEqual([expectedOutcome]), "A completed match announced the wrong result or announced it twice.");
            Assert(vm.GameOutcomeTitle == (expectedOutcome == GameSound.Victory ? "胜 利" : expectedOutcome == GameSound.Defeat ? "败 北" : "平 局"), "Result headline does not describe the player's outcome.");
            var expectedReport = MatchSummaryChecks.VerifyCompleted(vm);
            var resultWindow = new MainWindow(vm);
            Render((FrameworkElement)resultWindow.Content, 1120, 740, Path.Combine(output, $"19-{expectedOutcome.ToString().ToLowerInvariant()}.png"));
            MatchSummaryChecks.VerifyControls(resultWindow);
            resultWindow.Content = null;
            resultWindow.Close();
            var restored = GameReplay.Restore(engine.CreateCheckpoint(), StandardContentRegistry.CreateWithTeamModes());
            Assert(expectedReport == System.Text.Json.JsonSerializer.Serialize(MatchSummary.Create(restored.CreateSnapshot(0), restored.Events)), "Restored full match changed result statistics.");
            Assert(SnapshotJson.Serialize(engine.CreateSnapshot(0, true)) == SnapshotJson.Serialize(restored.CreateSnapshot(0, true)), "A complete UI match did not restore from its command journal.");
        }
        Assert(playedCount > 5 && responseCount > 5, "Full matches did not exercise enough interactions.");
        Console.WriteLine($"  Played {playedCount} cards, recast {recastCount} cards and answered {responseCount} prompts across 5 completed matches.");
        var requiredGuidance = new[] { "SelectGeneral", "PlayCard", "DiscardCards", "SelectHarvestCard", "RespondSlash", "Completed" };
        Assert(requiredGuidance.All(PlayerGuideChecks.ObservedPrompts.Contains), "Completed matches omitted expected human guide boundaries.");
        Console.WriteLine($"  Read-only player guidance: {string.Join(", ", PlayerGuideChecks.ObservedPrompts.Order())}.");
    }

    private sealed class BindingListener : TraceListener
    {
        public List<string> Errors { get; } = [];
        public override void Write(string? message) { }
        public override void WriteLine(string? message)
        {
            if (message is not null && (message.Contains("Error:") || message.Contains("Warning:"))) Errors.Add(message);
        }
    }
}
