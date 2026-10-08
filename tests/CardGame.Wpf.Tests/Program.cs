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
    private static int _failed;
    private static string[] _nameFilters = [];
    private static readonly HashSet<string> MatchedFilters = new(StringComparer.OrdinalIgnoreCase);
    private static bool _verbose;
    private static string? _startAfterName;
    private static bool _startAfterReached;
    private static readonly BindingListener BindingErrors = new();

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--filter", StringComparer.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Use --filter=<name>; an incomplete filter must not start the full suite.");
            return 2;
        }

        if (args.Any(argument => argument.StartsWith("--", StringComparison.Ordinal) &&
            argument != "--record-motion" && argument != "--verify-native-audio" && argument != "--verbose" &&
            !argument.StartsWith("--filter=", StringComparison.OrdinalIgnoreCase) &&
            !argument.StartsWith("--start-after=", StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine("Unknown WPF check option. Use --filter=<name>.");
            return 2;
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/CardGame.Wpf;component/Themes/TableTheme.xaml")
        });
        PresentationTraceSources.DataBindingSource.Listeners.Add(BindingErrors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        try
        {
            _verbose = args.Contains("--verbose", StringComparer.OrdinalIgnoreCase);
            _nameFilters = args.Where(argument => argument.StartsWith("--filter=", StringComparison.OrdinalIgnoreCase))
                .Select(argument => argument["--filter=".Length..].Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (_nameFilters.Any(value => value.Length == 0))
            {
                Console.Error.WriteLine("A WPF check filter cannot be empty.");
                return 2;
            }
            _startAfterName = args.FirstOrDefault(argument =>
                    argument.StartsWith("--start-after=", StringComparison.OrdinalIgnoreCase))?
                ["--start-after=".Length..].Trim();
            _startAfterReached = _startAfterName is null;
            var output = args.FirstOrDefault(argument =>
                !argument.StartsWith("--", StringComparison.Ordinal)) ??
                Path.Combine(Path.GetTempPath(), "card-ui-check");
            Directory.CreateDirectory(output);

            Check("table interaction refresh cost and stable presentation", () => TableInteractionChecks.SelectionCost(output));
            Check("overlapping hand cards keep stable pointer targets", () => HandPointerChecks.OverlapAndPointer(output));
            Check("stored grain updates its badge without replacing hand controls", () => HandGrainChecks.StoredCardKeepsItsControlAndUpdatesItsZone(output));
            Check("Last zone conversion presentation: restored judgment material uses central controls", () => LastZoneConversionUiChecks.RestoredJudgmentMaterialUsesExistingConversionControls(output));
            Check("Original hand tag presentation: real entity refresh and cold restore", () => OriginalHandTagUiChecks.RefreshAndColdRestore(output));
            Check("original card artwork preserves physical card identity and interaction", () => CardArtworkChecks.FacesAndInteractions(output));
            Check("Program Iron Chain guide follows actual target count and submits set", ProgramIronChainUiChecks.TargetCountGuideAndSubmission);
            Check("reference table layout preserves equipment and skill controls", () => TableSurfaceChecks.EquipmentAndSkillControls(output));
            Check("action dock keeps confirm cancel and end stable through real selection", () => TableSurfaceChecks.ActionDockSelection(output));
            Check("player guide renders current actions, private hand hints and searchable card rules", () => PlayerGuideChecks.ControlsAndSearch(output));
            Check("guide modal preserves selection and pauses then resumes the original timer policy", PlayerGuideChecks.ModalLifecycle);
            Check("general selection previews candidates before one explicit confirmation", () => CheckGeneralSelectionPreview(output));
            Check("general selection input respects overlays and consecutive national prompts", () => GeneralSelectionChecks.InputBoundaries(output));
            Check("mode lobby filters real identity, team and national entries without changing the match", () => CheckModeLobby(output));
            Check("selection, target toggle, cancel and play use legal actions", CheckSelections);
            Check("skill conversion is explicit and shares the confirmation flow", CheckConversions);
            Check("conversion choices retain exact provenance without leaking trusted action details", CardConversionUiChecks.Run);
            Check("composed skills share the generic WPF draft and submit their stable program identity", SkillProgramUiChecks.ActiveSelectionAndSubmission);
            Check("Program named choice renders shared labels and resumes parent", () => ProgramChoiceUiChecks.NamedChoiceUsesSharedSurfaceAndCommand(output));
            Check("Program paid choices preserve actor control and opaque slots", () => ProgramPaidChoiceUiChecks.SharedChoiceSurfaces(output));
            Check("declaration and general library views protect private faces", () => RuleInformationUiChecks.PrivateFacesAndPublicDeclarations(output));
            Check("Jin faction presentation: gallery names filters colors and embedded images", JinFactionPresentationChecks.GalleryNamesFiltersColorsAndEmbeddedImages);
            Check("Program deferred cards preserve private views and public pile ownership", () => DeferredCardsUiChecks.PrivateViewAndPublicPileRestoreThroughSharedControls(output));
            Check("Program conversion tiers restore actual shared skill state", () => ConfiguredConversionsUiChecks.TierRestoresWithActualUpgrade(output));
            Check("zero-entity round conversions use public target drafts and exact PlayCard commands", TieredRoundZeroUseUiChecks.PublicZeroUseTargetsAndRealCardSubmission);
            Check("Draw-funded zero Use exact source private draw child and stale action", TieredRoundZeroUseUiChecks.DrawFundedZeroUseSubmitsExactSourceAndPrivateDrawChild);
            Check("chained state basic UI: false marker selects exact source", ChainedStateBasicUiChecks.FalseMarkerSelectsAndConfirmsExactSource);
            Check("Program conversion polarity stays visible with active entry and restore", () => ProgramConversionPolarityUiChecks.PolarityRemainsVisibleWithActiveEntryAndRestore(output));
            Check("Program private damage offers restore exact shared faces", () => ConfiguredConversionsUiChecks.PrivateOfferRestoresThroughSharedFaces(output));
            Check("Program public card choices restore exact faces and submit current choices", () => PublicProgramCardsUiChecks.RevealedChoicesRestoreAndSubmit(output));
            Check("Program public states restore choice cycle and persistent pile", () => PublicStateUiChecks.AlternatingStateAndPersistentPileRestore(output));
            Check("Program public rule states restore quota limit and self prohibition", () => PublicStateUiChecks.PublicRuleStatesAndComparedHandsRestore(output));
            Check("Program beneficiary suit shield restores actual death grant", () => BeneficiaryStateUiChecks.ActualDeathGiftShieldRestores(output));
            Check("Program response exchange states restore entity restriction and upgrade", () => ResponseExchangeStateUiChecks.ActualEntityRestrictionAndUpgradeRestore(output));
            Check("Program public markers restore actual payment transfer and consumption", () => PublicMarkerUiChecks.ActualMarkersRestoreAndTransfer(output));
            Check("Program deferred hand alignment restores native private recipient and exact payment", () => DeferredHandAlignmentUiChecks.NativeRecipientDraftRestores(output));
            Check("Program private top quota restores exact faces and top order", () => QuotaTopUiChecks.NativePrivateViewAndOrder(output));
            Check("Program exact top count restores private shared order", () => ExactTopReorderUiChecks.ManualLoadAndPartition(output));
            Check("Program population count private faces restore shared free order", () => PopulationTopReorderUiChecks.PrivateFacesRestoreAndNativeOrder(output));
            Check("Program issued phase use ban restores native restrictions and controls", () => PhaseGiftUiChecks.IssuedPhaseBanRestores(output));
            Check("Program completed faction gift restores provider and Lord choices", () => PhaseGiftUiChecks.ProviderAndLordChoicesRestore(output));
            Check("Program public pile exchange restores optional count and public faces", () => PublicPileExchangeUiChecks.OptionalHandCountAndPublicFacesRestore(output));
            Check("Program public piles restore separate sources and local source loss", () => PublicPileExchangeUiChecks.MultipleSourcesRestoreSeparatePublicPiles(output));
            Check("Program paid suit allowance restores source loss and phase expiry", () => PhaseSuitAllowanceUiChecks.PaidAllowanceSurvivesSourceLossAndExpires(output));
            Check("Program completed multiple costs restore native face ordering", () => PhaseSuitAllowanceUiChecks.MultipleCompletedCostsRestoreFaceOrder(output));
            Check("Program owned-card sets render a private shared draft and commit once", () => ProgramOwnedCardsUiChecks.PrivateSetUsesSharedChoiceSurface(output));
            Check("public pile costs use the named foreign pile and shared confirmation draft", () => PublicPileSkillUiChecks.ForeignPublicPileCostsUseSharedDraft(output));
            Check("hand responses select exact cards and confirm through shared controls", () => HandResponseChecks.Controls(output));
            Check("response context distinguishes recipients, opponents and private prompts", DecisionContextChecks.Semantics);
            Check("device preferences persist before play and survive legacy loads and tutorials", () => PreferencesChecks.StartupAndMigration(output));
            Check("invalid preferences remain recoverable and slider writes coalesce safely", () => PreferencesChecks.FailuresAndDebounce(output));
            Check("playback batches internal steps while preserving human boundaries and replay", PlaybackChecks.AutomaticStepsPreserveCommittedBoundaries);
            Check("opaque target-card slots render privately and commit through WPF commands", () => TargetCardChecks.Controls(output));
            Check("dual portraits protect hidden slots and retain legacy skill semantics", NationalSeatChecks.PrivacyAndReveal);
            Check("saved UI boundaries restore and continue through actual files", () => PersistenceChecks.RoundTrips(output));
            Check("failed writes and incompatible reads preserve the active game", () => PersistenceChecks.FailedFilesPreserveGame(output));
            Check("autosave coalesces commands and resumes after closing", PersistenceChecks.AutomaticAndExit);
            Check("continue and save controls render in the small window", () => CheckSaveViews(output));
            Check("battle feedback exposes only committed public actions", FeedbackChecks.PublicProjection);
            Check("battle feedback retires answered inquiries without dropping results", () => FeedbackChecks.InquiryLifetime(output));
            Check("card flights use public faces and preserve prompts", () => FeedbackChecks.CardFlights(output));
            Check("audio follows committed actions and survives mute, background and device failure", AudioChecks.CommandRouting);
            if (args.Contains("--verify-native-audio")) Check("official native audio plays MP3 and converted WAV at zero volume", OfficialAudioChecks.NativeSilentPlayback);
            Check("sound controls, shipped assets and compatible JSON preferences are valid", () => AudioChecks.SettingsAndAssets(output));
            if (args.Contains("--verify-native-audio")) Check("native WPF audio opens and completes every effect at zero volume", AudioChecks.NativeSilentPlayback);
            if (!_startAfterReached)
                throw new InvalidOperationException($"No WPF check matched start-after '{_startAfterName}'.");
            foreach (var filter in _nameFilters)
                if (!MatchedFilters.Contains(filter))
                    throw new InvalidOperationException($"No WPF checks matched filter '{filter}'.");
            Assert(BindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, BindingErrors.Errors.Take(15)));
            Console.WriteLine($"{_passed} WPF checks passed, {_failed} failed. Renders: {output}");
            return _failed == 0 ? 0 : 1;
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
        if (!_startAfterReached)
        {
            if (string.Equals(name, _startAfterName, StringComparison.Ordinal))
                _startAfterReached = true;
            return;
        }
        var matchingFilters = _nameFilters.Where(filter => name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (_nameFilters.Length > 0 && matchingFilters.Length == 0)
            return;
        foreach (var filter in matchingFilters) MatchedFilters.Add(filter);
        var started = Stopwatch.GetTimestamp();
        try
        {
            action();
            _passed++;
            Console.WriteLine($"[PASS] {name}");
        }
        catch (Exception exception)
        {
            _failed++;
            Console.Error.WriteLine($"[FAIL] {name}: {exception}");
        }
        finally
        {
            if (_verbose) Console.WriteLine($"[TIME] {name}: {Stopwatch.GetElapsedTime(started).TotalMilliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)} ms");
        }
    }

    private static MainViewModel NewViewModel(int seed = 721019) => new(false, seed, showSetup: false, saveStore: new MemorySaveStore()) { IsMotionEnabled = false };



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
        Render(root, 1440, 880, Path.Combine(output, "134-home-lobby.png"));
        Render(root, 1120, 740, Path.Combine(output, "134-home-lobby-compact.png"));
        Assert(!vm.IsLobbyConfigurationOpen && ((HomeLobby)window.FindName("LobbyHome")).IsEnabled,
            "Startup must show the home lobby before the detailed setup.");
        var home = (HomeLobby)window.FindName("LobbyHome");
        foreach (var (buttonName, categoryId) in new[]
        {
            ("ClassicBattleButton", "identity"), ("TeamBattleButton", "team"),
            ("NationalBattleButton", "national"), ("EnterBattleButton", "all")
        })
        {
            var entry = (Button)home.FindName(buttonName);
            Assert(entry.ActualWidth > 150 && entry.ActualHeight > 100 && entry.Command!.CanExecute(entry.CommandParameter),
                $"Home entry {buttonName} is not accessible at the minimum layout size.");
            entry.Command!.Execute(entry.CommandParameter);
            root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            Assert(vm.IsLobbyConfigurationOpen && vm.SelectedModeCategory.Id == categoryId && !home.IsEnabled,
                "A home entry did not open its real category or isolate the modal input.");
            vm.BackToLobbyCommand.Execute(null);
        }
        vm.OpenLobbyCategoryCommand.Execute("all");
        Render(root, 1120, 740, Path.Combine(output, "134-mode-lobby.png"));
        var lobby = (ListBox)window.FindName("TableModeChoices");
        Assert(lobby.ActualHeight > 0 && lobby.Items.Count == 11 &&
               (ListBox)window.FindName("ModeCategoryChoices") is { Items.Count: 4 },
            "Mode cards or category controls are inaccessible.");
        ModeSelectionChecks.RefreshBoundSelection(vm, lobby);

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
        var shortcut = typeof(MainWindow).GetMethod("HandleShortcut", BindingFlags.NonPublic | BindingFlags.Instance)!;
        shortcut.Invoke(window, [Key.Escape, ModifierKeys.None]);
        Assert(!vm.IsLobbyConfigurationOpen && vm.IsNewGameSetupOpen,
            "Escape from setup must return to the lobby without entering the suspended game.");
        vm.OpenSettingsCommand.Execute(null);
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Assert(!((Border)window.FindName("NewGameSetupPanel")).IsEnabled,
            "Settings must isolate home lobby input.");
        shortcut.Invoke(window, [Key.Escape, ModifierKeys.None]);
        Assert(!vm.IsSettingsOpen && vm.IsNewGameSetupOpen, "Closing settings must preserve the lobby.");
        vm.CancelNewGameSetupCommand.Execute(null);
        vm.NewGameCommand.Execute(null);
        Assert(!vm.IsLobbyConfigurationOpen, "Reopening the lobby must not retain a stale configuration overlay.");
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
        var action = engine.GetHumanLegalActions().First(a =>
            a.CardId == redCard.Id && a.PlayedCardKind == CardKind.Slash &&
            a.ConversionSource?.SkillId == "standard:wusheng");
        vm.SelectCardCommand.Execute(redCard);
        var conversionChoice = vm.EquipmentPlayChoices.Single(choice =>
            choice.Cards.SequenceEqual([redCard.Id]) &&
            choice.Parameters.GetValueOrDefault("conversion-skill-id") == action.ConversionSource!.SkillId &&
            choice.Parameters.GetValueOrDefault("conversion-binding-id") == action.ConversionSource.BindingId &&
            choice.Parameters.GetValueOrDefault("conversion-instance-id") == action.ConversionSource.SkillInstanceId);
        vm.SelectEquipmentPlayChoiceCommand.Execute(conversionChoice);
        vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == action.TargetSeat));
        Assert(vm.CanPlaySelectedAsSlash, "The selected Wusheng source should enable this exact converted Slash.");
        var revision = engine.Revision;
        vm.PlaySelectedAsSlashCommand.Execute(null);
        Assert(engine.Revision > revision && !vm.HasSelection, "Wusheng did not commit and clear the selected card.");
        Assert(vm.RecentPlays.Any(play => play.Name == "杀"), "Table should show the effective public card name.");
        Assert(vm.BattleCues.Any(cue => cue.Kind == CardGame.Wpf.Presentation.BattleCueKind.Card && cue.Label == "杀" && cue.CardKind == CardKind.Slash), "Converted Slash feedback exposed the physical card name or artwork.");
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
