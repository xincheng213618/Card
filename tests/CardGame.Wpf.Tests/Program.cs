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
            var output = args.FirstOrDefault() ?? Path.Combine(Path.GetTempPath(), "card-ui-check");
            Directory.CreateDirectory(output);
            Check("player guide renders current actions, private hand hints and searchable card rules", () => PlayerGuideChecks.ControlsAndSearch(output));
            Check("guide modal preserves selection and pauses then resumes the original timer policy", PlayerGuideChecks.ModalLifecycle);
            Check("tutorial positions are deterministic real command histories", TutorialChecks.RealScenarios);
            Check("four tutorial lessons complete and restore the suspended match", () => TutorialChecks.CompleteCourseAndRestore(output));
            Check("layout and embedded portraits load without opening a window", () => CheckLayout(output));
            Check("large hands keep every card reachable", CheckLargeHand);
            Check("hand overflow supports wheel browsing and reveals newly selected cards", () => HandNavigationChecks.OverflowAndSelection(output));
            Check("selection, target toggle, cancel and play use legal actions", CheckSelections);
            Check("skill conversion is explicit and shares the confirmation flow", CheckConversions);
            Check("expanded content exposes the active-skill command in WPF", ActiveSkillChecks.Controls);
            Check("classic identity renders and restores multiple skills with base HP", () => ClassicGeneralUiChecks.MultiSkillSelectionAndRestore(output));
            Check("classic Tiandu restores and claims through the WPF choice surface", () => ClassicGeneralUiChecks.TianduChoiceAndRestore(output));
            Check("classic Fanjian restores and resolves through the WPF suit choice surface", () => ClassicGeneralUiChecks.FanjianChoiceAndRestore(output));
            Check("classic Guanxing restores and orders private cards through the WPF choice surface", () => ClassicGeneralUiChecks.GuanxingChoiceAndRestore(output));
            Check("classic Hujia restores and requests Wei responses through the WPF choice surface", () => ClassicGeneralUiChecks.HujiaChoiceAndRestore(output));
            Check("skill drafts confirm through Enter and resume intact after guides and tutorials", () => SkillInteractionChecks.ConfirmAndResume(output));
            Check("hand responses select exact cards and confirm through shared controls", () => HandResponseChecks.Controls(output));
            Check("Wusheng hand responses restore versioned prompts and confirm explicit conversions", () => WushengResponseChecks.ControlsAndSavedRules(output));
            Check("national Wusheng reveal during response refreshes the WPF controls", () => WushengResponseChecks.NationalRevealDuringResponse(output));
            Check("Iron Chain selects seats directly and preserves multi-target drafts through guides and tutorials", () => CardTargetChecks.DirectSelection(output));
            Check("recast uses a distinct action, public feedback and version-aware saved rules", () => RecastUiChecks.ControlsAndOldSaves(output));
            Check("response context distinguishes recipients, opponents and private prompts", DecisionContextChecks.Semantics);
            Check("match reports aggregate public outcomes without double counting", MatchSummaryChecks.Aggregation);
            Check("completed history survives relaunch, deduplicates endings and isolates damaged files", () => HistoryChecks.PersistenceAndFailures(output));
            Check("history modal preserves selected actions and pauses then resumes AI", () => HistoryChecks.ModalLifecycle(output));
            Check("device preferences persist before play and survive legacy loads and tutorials", () => PreferencesChecks.StartupAndMigration(output));
            Check("invalid preferences remain recoverable and slider writes coalesce safely", () => PreferencesChecks.FailuresAndDebounce(output));
            Check("play advice uses private player views and preserves the original selection", () => AdviceChecks.ControlsAndPrivacy(output));
            Check("playback speed persists and dead players can pause and resume observation", () => PlaybackChecks.SettingsAndSpectating(output));
            Check("expanded rescue content renders and commits Jijiu dying choices", JijiuChecks.ControlsAndDying);
            Check("opaque target-card slots render privately and commit through WPF commands", () => TargetCardChecks.Controls(output));
            Check("new game settings and multi-card discard work through controls", () => CheckSetupAndDiscard(output));
            Check("team selection, guides, tutorial return and saved results follow the actual team", () => TeamExperienceChecks.ControlsAndRestore(output));
            Check("national dual-general controls preserve privacy, reveal and saved outcomes", () => NationalExperienceChecks.ControlsAndRestore(output));
            Check("national WPF matches reach faction results through player commands", NationalExperienceChecks.CompleteMatches);
            Check("six-player national controls preserve solo faction labels and saved reveals", () => NationalExperienceChecks.AmbitiousControlsAndRestore(output));
            Check("dual-general health previews survive selection, tutorials and shipped saves", () => NationalHealthChecks.ControlsAndOldPackage(output));
            Check("dual portraits protect hidden slots and retain legacy skill semantics", NationalSeatChecks.PrivacyAndLegacy);
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
            Console.WriteLine($"{_passed}/{(args.Contains("--verify-native-audio") ? 51 : 50)} WPF checks passed. Renders: {output}");
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
