using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Audio;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class WenheActivityUiChecks
{
    public static void NativeEliminationRanksDriveSummaryHistoryAndWinnerSound(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new RankUiFixture());
        var original = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, PlayerCount = 8, HumanSeat = 0, HumanRole = null, ModeId = RankUiFixture.Mode,
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false,
            WenheAspirationRank = 1, MaxTurns = 20
        }, registry);
        Accept(new StartGameCommand());
        for (var step = 0; step < 80 && original.PendingDecision is not { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 }; step++)
            Accept(new AdvanceOneStepCommand(original.Revision));
        Accept(new RequestGeneralRosterCommand(0, true, original.Revision, original.PendingDecision!.PromptId));
        Accept(new SelectGeneralCommand(0, RankUiFixture.Owner, original.Revision, original.PendingDecision!.PromptId));
        for (var step = 0; step < 160 && original.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }; step++)
            Accept(new AdvanceOneStepCommand(original.Revision));
        Program.Assert(original.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
            "The small physical-Dodge survival fixture must resolve real first-round chaos and reach its human turn.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, original.CreateCheckpoint()));
        var history = new MemoryMatchHistoryStore();
        using var vm = new MainViewModel(false, 17, true, store, historyStore: history, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        vm.LoadManualGameCommand.Execute(null);
        var window = new MainWindow(vm);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        try
        {
            var game = Program.Engine(vm);
            Layout();
            for (var victim = 1; victim < 8; victim++)
            {
                vm.SelectActiveSkillCommand.Execute(vm.HumanActiveSkillActions.Single(action => action.ProgramSkillId == RankUiFixture.Driver));
                vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == victim));
                Click((Button)window.FindName("PlayCardButton"));
                for (var step = 0; step < 96 && game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }; step++)
                    vm.StepAiCommand.Execute(null);
                Program.Assert(game.CreateSnapshot(0).Players[victim] is { IsAlive: true, Hp: 3, SurvivalRank: null } &&
                    game.Events.Count(entry => entry.Payload is ProgramBindingResolvedEvent
                        { SkillId: "wenhe:niepan", Activated: true, Completed: true } resolved && resolved.OwnerSeat == victim) == 1 &&
                    vm.Seats.Single(seat => seat.Seat == victim).IsAlive,
                    "The first lethal confirmation must genuinely consume the tier-five opponent's native Nirvana, leave it alive and award no premature rank.");
                vm.SelectActiveSkillCommand.Execute(vm.HumanActiveSkillActions.Single(action => action.ProgramSkillId == RankUiFixture.Driver));
                vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == victim));
                Program.Assert(vm.CanConfirmActiveSkill && vm.CanConfirmSelected,
                    "The actual active-skill draft must allow the selected independent opponent before committing its native defeat.");
                Click((Button)window.FindName("PlayCardButton"));
                for (var step = 0; step < 96 && !vm.HasGameOver &&
                         game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }; step++) vm.StepAiCommand.Execute(null);
                var ranked = game.CreateSnapshot(0).Players[victim];
                Program.Assert(!ranked.IsAlive && ranked.SurvivalRank == 9 - victim &&
                    vm.Seats.Single(seat => seat.Seat == victim).UnavailableStatusText == $"第{9 - victim}名",
                    "Each actual death must immediately display its final survival rank through the existing dead-seat template.");
            }
            var snapshot = game.CreateSnapshot(0);
            var summary = vm.CompletedMatch!;
            Program.Assert(vm.HasGameOver && snapshot is { Winner: Winner.SoleSurvivor, WinnerSeat: 0 } &&
                GameSoundRules.Outcome(snapshot) == GameSound.Victory && vm.GameOutcomeTitle == "第 1 名" &&
                vm.GameOverText.Contains("第一名") && vm.WenheAspirationText.Contains("志向已达成") &&
                summary is { IsSurvival: true, CampColumnHeader: "名次" } &&
                summary.Players.Select(player => player.SurvivalRank).SequenceEqual(Enumerable.Range(1, 8).Select(rank => (int?)rank)) &&
                summary.Players.All(player => player.RankOrCamp == $"第{player.SurvivalRank}名") &&
                summary.HumanPerformance.Contains("第1名") && vm.MatchHistory.Single() is
                    { Outcome: MatchOutcome.Win, OutcomeLabel: "第1名" } entry && entry.Title.Contains("第1名") &&
                !entry.Title.Contains("未知"),
                "A genuine survivor victory must use WinnerSeat for audio/history, order the public report by rank and evaluate the private aspiration without identity camps.");
            var report = (ItemsControl)window.FindName("MatchResultRows");
            Program.Assert(report.Items.Count == 8 && report.ItemsSource == summary.Players,
                "The visible completed-match table must bind the actual ranked rows rather than re-sort by seat.");
            Program.Render(root, 1440, 880, Path.Combine(output, "172-wenhe-ranking-report.png"));
            vm.SaveGameCommand.Execute(null);
            vm.LoadManualGameCommand.Execute(null);
            Program.Assert(!vm.HasSaveError && vm.GameOutcomeTitle == "第 1 名" &&
                vm.CompletedMatch!.Players.Select(player => player.SurvivalRank).SequenceEqual(summary.Players.Select(player => player.SurvivalRank)) &&
                vm.MatchHistory.Count == 1,
                "Restoring a completed survival save must retain all public ranks without recording a second match history entry.");
            var aspirationResult = typeof(MainViewModel).GetMethod("WenheAspirationResultText", BindingFlags.Static | BindingFlags.NonPublic)!;
            var allDeadDraw = snapshot with { Winner = Winner.Draw, WinnerSeat = null,
                Players = snapshot.Players.Select(player => player with { IsAlive = false }).ToArray() };
            Program.Assert(((string)aspirationResult.Invoke(null, [allDeadDraw, new WenheAspirationSnapshot(1, false, false)])!).Contains("未达成") &&
                ((string)aspirationResult.Invoke(null, [allDeadDraw, new WenheAspirationSnapshot(2, false, false)])!).Contains("已达成") &&
                ((string)aspirationResult.Invoke(null, [snapshot, new WenheAspirationSnapshot(1, false, false)])!).Contains("已达成"),
                "The same public first-place death rank in an all-dead Draw must fail a champion aspiration, retain top-two rank credit and leave a genuine sole-survivor champion successful.");
        }
        finally { window.Content = null; window.DataContext = null; window.Close(); }

        void Accept(GameCommand command)
        { var result = original.Submit(command); Program.Assert(result.Accepted, result.Error?.Message ?? "Native rank fixture command rejected."); }
        void Click(Button button)
        {
            Layout();
            Program.Assert(button.IsEnabled && button.Command!.CanExecute(button.CommandParameter), "The rank-fixture confirmation button must be truly enabled.");
            typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
            Layout();
        }
        void Layout()
        {
            root.Measure(new Size(1440, 880)); root.Arrange(new Rect(0, 0, 1440, 880)); root.UpdateLayout();
            root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout();
        }
    }

    public static void SurvivalLobbyPrivateGoalEightSeatsAndCrossConfigurationRestore(string output)
    {
        var peach = new CardViewModel
        {
            Id = 1, Kind = CardKind.Peach, Name = "桃", KindLabel = "基本牌", SuitGlyph = "♥", Rank = "Q",
            Description = "Real Peach material with a round-scoped poison identity.", IsPlayable = false
        };
        var artworkChanges = new List<string?>();
        peach.PropertyChanged += (_, args) => artworkChanges.Add(args.PropertyName);
        var printedArtwork = peach.Artwork;
        peach.IdentityKind = CardKind.Poison;
        Program.Assert(peach.Kind == CardKind.Peach && peach.IdentityLabel == "毒" && peach.HasArtwork &&
            ReferenceEquals(peach.Artwork, CardArt.Get(CardKind.Poison)) && !ReferenceEquals(peach.Artwork, printedArtwork) &&
            artworkChanges.Contains(nameof(CardViewModel.Artwork)) && artworkChanges.Contains(nameof(CardViewModel.HasArtwork)),
            "A real Peach's round poison identity must display poison art and notify both bindings without changing the printed entity kind.");
        artworkChanges.Clear();
        peach.IdentityKind = null;
        Program.Assert(peach.Kind == CardKind.Peach && !peach.HasIdentity && ReferenceEquals(peach.Artwork, printedArtwork) &&
            artworkChanges.Contains(nameof(CardViewModel.Artwork)) && artworkChanges.Contains(nameof(CardViewModel.HasArtwork)),
            "Ending the poison identity must restore the printed Peach artwork through live binding notifications.");
        peach.IdentityKind = CardKind.FireSlash;
        Program.Assert(ReferenceEquals(peach.Artwork, printedArtwork), "Existing non-poison conversion identities must retain their printed-card artwork behavior.");
        var registry = ComposedSkillContentRegistry.CreateShowcase(includeWenheActivities: true);
        var store = new MemorySaveStore();
        using var vm = new MainViewModel(false, 17, showSetup: true, saveStore: store,
            useExpandedContent: true, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false, ManualDiscardEnabled = false };
        var window = new MainWindow(vm);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        try
        {
            vm.OpenLobbyCategoryCommand.Execute("activity");
            vm.SelectedTableMode = vm.VisibleTableModes.Single(option => option.ModeId == "activity:wenhe-2019");
            Layout();
            var goals = (ListBox)window.FindName("WenheAspirationChoices");
            Program.Assert(vm.IsActivityModeSelection && vm.IsWenheActivityModeSelection &&
                !vm.IsTeamModeSelection && !vm.IsIdentityModeSelection && vm.ShowModeSetupText &&
                vm.SelectedTableMode is { PlayerCount: 8, ModeBadge: "五阶生存" } &&
                vm.WenheAspirations.Select(option => option.GoalRank).SequenceEqual([1, 2, 4, 6]) &&
                vm.SelectedWenheAspiration.GoalRank == 1 && goals.Visibility == Visibility.Visible && goals.Items.Count == 4 &&
                vm.TeamModeSetupText.Contains("七名AI对手") && vm.TeamModeSetupText.Contains("起手五张") &&
                vm.TeamModeSetupText.Contains("体力及上限+2") && vm.TeamModeSetupText.Contains("免费志向") &&
                vm.CurrentGuideTitle.Contains("文和乱武") && vm.CurrentGuideBody.Contains("各自求生") &&
                !vm.CurrentGuideBody.Contains("队友"),
                "The actual Wenhe lobby must expose four free private aspirations and eight independent tier-five survivors, without identity or team selection.");
            goals.SelectedItem = vm.WenheAspirations.Single(option => option.GoalRank == 4);
            Layout();
            Program.Assert(vm.SelectedWenheAspiration.GoalRank == 4 && vm.TeamModeSetupText.Contains("前四"),
                "The bound goal selector must update the actual pregame option rather than only its displayed text.");
            Program.Render(root, 1440, 880, Path.Combine(output, "170-wenhe-survival-setup.png"));
            vm.StartNewGameFromLobbyCommand.Execute(null);
            Program.Assert(vm.IdentityRevealTitle.Contains("各 自 求 生") && vm.IdentityRevealRoster.Contains("七名AI"),
                "The real introduction must explain independent opponents rather than inventing a coalition.");
            vm.ContinueFromIdentityRevealCommand.Execute(null);
            Until(() => vm.IsGeneralSelectionPending);
            Program.Assert(vm.GeneralSelectionContextTitle.Contains("独立求生") &&
                vm.GeneralSelectionContextDetail.Contains("七名AI对手") &&
                vm.GeneralChoices.All(choice => choice.HealthText == $"五阶体力上限 {registry.Generals[choice.GeneralId].BaseHp + 2}"),
                "The native private general draft must preview exact tier-five health and seven AI opponents.");
            vm.ToggleGeneralRosterCommand.Execute(null);
            Program.Assert(vm.CustomGeneralChoices.Count > 0 && vm.CustomGeneralChoices.All(choice =>
                registry.Generals[choice.GeneralId].FactionId != "god" &&
                choice.HealthText == $"五阶体力上限 {registry.Generals[choice.GeneralId].BaseHp + 2}") &&
                vm.CustomGeneralChoices.Any(choice => choice.GeneralId == "classic:gan-ning") &&
                vm.CustomGeneralChoices.All(choice => choice.GeneralId is not ("boundary:gan-ning" or "classic:hua-tuo")),
                "The real expanded roster must retain ordinary Gan Ning while applying official bans and five-tier health previews.");
            vm.PreviewGeneralChoiceCommand.Execute(vm.CustomGeneralChoices.Single(choice => choice.GeneralId == "classic:guan-yu"));
            vm.ConfirmGeneralChoiceCommand.Execute(null);
            Until(() => Program.Engine(vm).Events.Any(entry => entry.Payload is SetupCompletedEvent));
            var game = Program.Engine(vm);
            var snapshot = game.CreateSnapshot(0);
            Program.Assert(snapshot.ModeKind == ContentModeKind.Survival && snapshot.Players.Count == 8 &&
                snapshot.Players.Count(player => player.IsHuman) == 1 && snapshot.Players.All(player =>
                    player.TeamId is null && player.Role == Role.Contestant &&
                    player.MaxHp == registry.Generals[player.GeneralId].BaseHp + 2) &&
                snapshot.WenheAspiration is { GoalRank: 4, CanUseChampionMark: false } &&
                vm.Seats.Count == 8 && !vm.Seats.Any(seat => seat.IsTeammate) &&
                vm.Seats.Where(seat => !seat.IsHuman).All(seat => seat.RoleLabel == "对手" && !seat.CanMarkIdentity) &&
                vm.HumanSkillCards.All(skill => skill.ContentId is not ("wenhe:rules" or "wenhe:chaos-resolution" or "wenhe:champion-mark")) &&
                vm.Seats.All(seat => !seat.SkillName.Contains("乱武结算") && !seat.SkillName.Contains("文和乱武规则") &&
                    !seat.SkillName.Contains("志在必得")) &&
                vm.AliveText.Contains("各自求生") && vm.IdentityObjective.Contains("最后") &&
                vm.WenheAspirationText.Contains("前四") && vm.TableArenaTitle == "文 和 乱 武",
                "The live eight-seat table must use real independent contestants, seven unmarkable opponents and only the human's private goal.");
            Until(() => game.Events.Any(entry => entry.Payload is WenheRoundRuleChangedEvent));
            Layout();
            var rule = game.CreateSnapshot(0).WenheRule!;
            var banner = (FrameworkElement)window.FindName("WenheRuleBanner");
            Program.Assert(rule is { RoundNumber: 1, Rule: WenheRuleKind.Chaos } &&
                banner.Visibility == Visibility.Visible && vm.WenheRoundText.Contains("第 1 轮") &&
                vm.WenheCurrentRuleDescription.Contains("最近") && vm.TableModeText.Contains("乱武"),
                "The actual first-round lifecycle event must feed the visible rule banner independently of the ordinary action-turn counter.");
            Program.Render(root, 1440, 880, Path.Combine(output, "171-wenhe-eight-survivors.png"));
            vm.SaveGameCommand.Execute(null);
            var saved = SnapshotJson.Serialize(game.CreateSnapshot(0));
            vm.SelectedTableMode = vm.TableModes.Single(option => option.ModeId == "identity:classic-8");
            vm.LoadManualGameCommand.Execute(null);
            Program.Assert(!vm.HasSaveError && Program.Engine(vm).ModeId == "activity:wenhe-2019" &&
                vm.SelectedWenheAspiration.GoalRank == 4 && vm.IsWenheActivityTable &&
                SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0)) == saved,
                "A real manual restore must recover the exact live rule, private goal and survival view after another lobby mode is selected.");
            using var other = new MainViewModel(false, 17, true, store, contentRegistry: ComposedSkillContentRegistry.CreateShowcase())
                { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
            other.LoadManualGameCommand.Execute(null);
            Program.Assert(!other.HasSaveError && other.IsWenheActivityTable && other.Seats.Count == 8 &&
                other.SelectedTableMode.ModeId == "activity:wenhe-2019" && other.SelectedWenheAspiration.GoalRank == 4,
                "Package metadata must recover a survival save even when the new desktop configuration did not opt in to Wenhe.");
            other.StartNewGameFromLobbyCommand.Execute(null);
            other.ContinueFromIdentityRevealCommand.Execute(null);
            for (var step = 0; step < 80 && !other.IsGeneralSelectionPending; step++) other.StepAiCommand.Execute(null);
            Program.Assert(!other.HasSaveError && other.IsGeneralSelectionPending &&
                Program.Engine(other).ModeId == "activity:wenhe-2019" &&
                other.GeneralSelectionContextDetail.Contains("七名AI对手") && other.SelectedWenheAspiration.GoalRank == 4,
                "Rematching a cross-configuration save must preserve the recovered survival registry and the actual selected private goal.");
        }
        finally { window.Content = null; window.DataContext = null; window.Close(); }

        void Layout()
        {
            root.Measure(new Size(1440, 880)); root.Arrange(new Rect(0, 0, 1440, 880)); root.UpdateLayout();
            root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout();
        }
        void Until(Func<bool> ready)
        {
            for (var step = 0; step < 160; step++)
            {
                if (ready()) return;
                var boundary = Program.Engine(vm).CreateSnapshot(0);
                if (Program.SetupResponseDeclineCommand(vm) is { } decline &&
                    decline.CanExecute(null))
                {
                    decline.Execute(null);
                    continue;
                }
                Program.Assert(vm.StepAiCommand.CanExecute(null),
                    $"The real survival UI must advance to its bounded setup/rule boundary. Status={boundary.Status}; turn={boundary.TurnNumber}; phase={boundary.Phase}; seat={boundary.CurrentSeat}; pending={boundary.PendingDecision?.Kind}/{boundary.PendingDecision?.PlayerSeat}/{boundary.PendingDecision?.PromptId}; prompt={boundary.PendingDecision?.Prompt}; roster={string.Join(";", boundary.Players.Select(player => $"{player.Seat}:{player.GeneralId}"))}.");
                vm.StepAiCommand.Execute(null);
            }
            throw new InvalidOperationException("The Wenhe UI did not reach its bounded setup/rule boundary.");
        }
    }

    private sealed class RankUiFixture : IGameContentPackage
    {
        internal const string Mode = "fixture:wenhe-rank-ui", Driver = "fixture:wenhe-rank-driver",
            Owner = "fixture:wenhe-rank-owner", Quiet = "fixture:wenhe-rank-quiet";
        public PackageManifest Manifest { get; } = new("wenhe-rank-ui-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rules = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.wenhe-activity.rules.json")!);
            using var presentation = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.wenhe-activity.presentation.json")!);
            var activity = SkillProgramCatalog.Load(rules.ReadToEnd(), presentation.ReadToEnd());
            foreach (var (id, program) in activity.Programs)
                builder.AddSkill(new(id, activity.Presentations[id].Name, activity.Presentations[id].Description)
                { Program = program, ProgramPresentation = activity.Presentations[id], Tags = id == "wenhe:niepan" ? SkillTag.Limited : id == "wenhe:rules" ? SkillTag.Locked : SkillTag.None });
            var programs = SkillProgramCatalog.Load($$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
                {"id":"{{{Driver}}}","revision":1,"activations":[
                {"id":"defeat","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
                 "effects":[{"op":"loseHp","target":"selectedTarget","amount":20}]}]},
                {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"skip-ai-play","window":"afterNormalDraw","subject":"owner","optional":false,
                 "effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [Driver] = new { name = "Fixture elimination", description = "Real native HP-loss and death sequence for ranked UI." },
                    [Quiet] = new { name = "Quiet peer", description = "Skip ordinary AI play while retaining actual turn and activity-rule boundaries." } } }));
            foreach (var (id, program) in programs.Programs)
                builder.AddSkill(new(id, programs.Presentations[id].Name, programs.Presentations[id].Description)
                { Program = program, ProgramPresentation = programs.Presentations[id] });
            var ids = new[] { Owner }.Concat(Enumerable.Range(1, 7).Select(seat => $"fixture:wenhe-rank-{seat}")).ToArray();
            foreach (var id in ids) builder.AddGeneral(new(id, id, "", id == Owner ? Driver : Quiet, "wei", 4));
            builder.AddDeck(new("fixture:wenhe-rank-deck", "Quiet real physical Dodge pile", 4, 2, [new("standard:dodge", 160)]));
            builder.AddMode(new(Mode, "文和乱武 UI fixture", 8, 8, new Dictionary<string, int>(),
                "fixture:wenhe-rank-deck", 5, ids, ContentModeKind.Survival) { WenheActivity = new() });
        }
    }
}
