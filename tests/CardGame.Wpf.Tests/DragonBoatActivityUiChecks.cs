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

internal static class DragonBoatActivityUiChecks
{
    public static void FourTeamLobbyPrivateDraftAndCrossConfigurationRestore(string output)
    {
        var registry = ComposedSkillContentRegistry.CreateShowcase(includeDragonBoatActivities: true);
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
            vm.SelectedTableMode = vm.VisibleTableModes.Single(mode => mode.ModeId == "activity:dragon-boat-2018");
            Layout(root);
            var teams = (ListBox)window.FindName("TeamChoices");
            Program.Assert(vm.IsActivityModeSelection && vm.IsDragonBoatActivityModeSelection && vm.IsTeamModeSelection &&
                !vm.IsIdentityModeSelection && vm.SelectedTableMode.ModeBadge == "五阶竞舟" && teams.Visibility == Visibility.Visible &&
                teams.Items.Count == 4 && vm.StartingTeams.Select(team => team.TeamId).SequenceEqual(["team:wei", "team:shu", "team:wu", "team:qun"]),
                "The actual dragon-boat lobby must expose four two-player teams rather than a two-camp PVE or identity selector.");
            teams.SelectedItem = vm.StartingTeams.Single(team => team.TeamId == "team:qun");
            Layout(root);
            Program.Assert(vm.SelectedStartingTeam.TeamId == "team:qun" && vm.TeamModeSetupText.Contains("群船") &&
                vm.TeamModeSetupText.Contains("一名AI队友") && vm.TeamModeSetupText.Contains("六名AI对手") &&
                vm.TeamModeSetupText.Contains("第二个实际轮次") && vm.TeamModeSetupText.Contains("每次濒死") &&
                vm.CurrentGuideTitle.Contains("龙舟争渡") && !vm.CurrentGuideBody.Contains("英灵"),
                "A real bound team change must update the distinct two-round race, permanent guard and one-ally/six-opponent instructions.");
            Program.Render(root, 1440, 880, Path.Combine(output, "180-dragon-boat-four-teams.png"));
            vm.StartNewGameFromLobbyCommand.Execute(null);
            Program.Assert(vm.IdentityRevealRoster.Contains("魏蜀吴群") && vm.IdentityRevealRoster.Contains("六名AI对手"),
                "The actual opening must describe four boats without borrowing the Jiange NPC roster.");
            vm.ContinueFromIdentityRevealCommand.Execute(null);
            Until(vm, () => vm.IsGeneralSelectionPending);
            var game = Program.Engine(vm);
            var draft = game.CreateSnapshot(0).DragonBoatDraft!;
            Layout(root);
            var mode = registry.Modes["activity:dragon-boat-2018"].DragonBoatActivity!;
            Program.Assert(game.CreateSnapshot(0).Players[0].TeamId == "team:qun" && draft is { GeneralIds.Count: 6 } &&
                game.CreateSnapshot(0).Players[draft.TeammateSeat].TeamId == "team:qun" && draft.TeammateSeat != 0 &&
                vm.GeneralChoices.Count == 6 && vm.GeneralChoices.All(choice =>
                    mode.PlayerPoolIds["team:qun"].Contains(choice.GeneralId) &&
                    choice.HealthText == $"五阶体力上限 {registry.Generals[choice.GeneralId].BaseHp + 2}") &&
                vm.DragonBoatTeammateChoices.Select(choice => choice.GeneralId).SequenceEqual(draft.GeneralIds) &&
                ((ItemsControl)window.FindName("DragonBoatTeammateCandidates")).Items.Count == 6 &&
                ((FrameworkElement)window.FindName("DragonBoatTeammateDraft")).Visibility == Visibility.Visible &&
                ((Button)window.FindName("ToggleGeneralRosterButton")).Visibility == Visibility.Collapsed &&
                !vm.ToggleGeneralRosterCommand.CanExecute(null) && !vm.CanExpandGeneralRoster,
                "The true six-candidate draft must render exactly the projected same-team candidates and hide the inapplicable expanded-roster action.");
            Program.Render(root, 1440, 880, Path.Combine(output, "181-dragon-boat-shared-candidates.png"));
            vm.SaveGameCommand.Execute(null);
            var draftJson = SnapshotJson.Serialize(game.CreateSnapshot(0));
            vm.LoadManualGameCommand.Execute(null);
            game = Program.Engine(vm);
            Program.Assert(!vm.HasSaveError && SnapshotJson.Serialize(game.CreateSnapshot(0)) == draftJson &&
                vm.DragonBoatTeammateChoices.Select(choice => choice.GeneralId).SequenceEqual(draft.GeneralIds),
                "Saving and loading during the real draft must preserve fixed candidates, teammate projection and the exact live prompt.");
            vm.PreviewGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
            Click(root, (Button)window.FindName("ConfirmGeneralChoiceButton"));
            Until(vm, () => game.Events.Any(entry => entry.Payload is SetupCompletedEvent));
            var snapshot = game.CreateSnapshot(0);
            Layout(root);
            Program.Assert(snapshot.Players.Count == 8 && vm.Seats.Count(seat => seat.IsTeammate) == 1 &&
                vm.Seats.Count(seat => !seat.IsHuman && !seat.IsTeammate) == 6 &&
                snapshot.Players.GroupBy(player => player.TeamId).All(team => team.Count() == 2) &&
                snapshot.Players.All(player => player.MaxHp == registry.Generals[player.GeneralId].BaseHp + 2 && player.HandCount == 5) &&
                snapshot.Players.Skip(1).All(player => player.Hand.Count == 0) &&
                snapshot.DragonBoatStandings is { Count: 4 } && vm.DragonBoatStandings.Select(team => team.TeamId)
                    .SequenceEqual(["team:wei", "team:shu", "team:wu", "team:qun"]) &&
                ((ItemsControl)window.FindName("DragonBoatScoreRows")).Items.Count == 4 && !vm.HasDragonBoatTeammateDraft &&
                vm.TableArenaTitle == "龙 舟 争 渡" && vm.IdentityObjective.Contains("第二个实际轮次") &&
                vm.AliveText.Contains("六名AI对手") && vm.IsDragonBoatGuide && !vm.IsTeamGuide && !vm.IsIdentityGuide,
                "The live eight-seat race must keep one AI ally, six opponents, five-tier health/hands and private teammate hands while rendering the real four-team standings.");
            Program.Render(root, 1440, 880, Path.Combine(output, "182-dragon-boat-eight-seats.png"));
            vm.SaveGameCommand.Execute(null);
            var saved = SnapshotJson.Serialize(game.CreateSnapshot(0));
            using var restored = new MainViewModel(false, 17, true, store, contentRegistry:
                ComposedSkillContentRegistry.CreateShowcase())
                { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
            restored.LoadManualGameCommand.Execute(null);
            Program.Assert(!restored.HasSaveError && restored.IsDragonBoatActivityTable &&
                restored.SelectedStartingTeam.TeamId == "team:qun" && restored.StartingTeams.Count == 4 &&
                restored.Seats.Count == 8 && SnapshotJson.Serialize(Program.Engine(restored).CreateSnapshot(0)) == saved,
                "A desktop configured without dragon-boat content must cold-restore the exact package, four-team selector and eight-seat public state.");
            restored.StartNewGameFromLobbyCommand.Execute(null);
            restored.ContinueFromIdentityRevealCommand.Execute(null);
            Until(restored, () => restored.IsGeneralSelectionPending);
            Program.Assert(!restored.HasSaveError && Program.Engine(restored).ModeId == "activity:dragon-boat-2018" &&
                Program.Engine(restored).CreateCheckpoint().Options.HumanTeamId == "team:qun" && restored.GeneralChoices.Count == 6,
                "Rematching after cross-configuration recovery must start the restored dragon-boat package and selected boat's real six-candidate draft.");
        }
        finally { window.Content = null; window.DataContext = null; window.Close(); }
    }

    public static void NativeTreasureArrivalOrderAndTwoRoundTeamReport(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new ScoreUiFixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 17, PlayerCount = 8, HumanSeat = 0,
            HumanRole = null, HumanTeamId = "team:wei", ModeId = ScoreUiFixture.Mode, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 30 }, registry);
        Accept(game, new StartGameCommand());
        for (var step = 0; step < 80 && game.PendingDecision is not { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 }; step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Program.Assert(game.PendingDecision!.Choices.Any(choice => choice.ContentIds.SequenceEqual([ScoreUiFixture.Owner])),
            "The fixed seed must offer the verified human-only driver among its real six candidates without expanding or changing the draft.");
        Accept(game, new SelectGeneralCommand(0, ScoreUiFixture.Owner, game.Revision, game.PendingDecision.PromptId));
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        var history = new MemoryMatchHistoryStore();
        using var vm = new MainViewModel(false, 17, true, store, historyStore: history, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        vm.LoadManualGameCommand.Execute(null);
        var window = new MainWindow(vm);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        try
        {
            game = Program.Engine(vm);
            Until(vm, () => game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
            Program.Assert(vm.DragonBoatStandings.All(team => team.Treasures == 0),
                "The bounded physical-Dodge fixture must reach its real human turn without any synthetic score or AI self-damage.");
            CreditOpponent(1);
            var first = vm.DragonBoatStandings.Single(team => team.TeamId == "team:shu");
            CreditOpponent(2);
            var second = vm.DragonBoatStandings.Single(team => team.TeamId == "team:wu");
            Program.Assert(first is { Treasures: 1, Rank: 1 } && second is { Treasures: 1, Rank: 2 } &&
                first.ReachedSequence < second.ReachedSequence &&
                vm.DragonBoatStandings.Single(team => team.IsHumanTeam) is { TeamId: "team:wei", Treasures: 0, Rank: 3 } &&
                vm.DragonBoatStandings.Single(team => team.TeamId == "team:shu").ArrivalText.Contains("第1位") &&
                second.ArrivalText.Contains("第2位") && game.Events.Count(entry => entry.Payload is DragonBoatTreasureChangedEvent) == 2,
                "Two real native damage sources must update public treasure counts and preserve their true first-arrival ordering at the same score.");
            vm.SaveGameCommand.Execute(null);
            var live = SnapshotJson.Serialize(game.CreateSnapshot(0));
            vm.LoadManualGameCommand.Execute(null); game = Program.Engine(vm);
            Program.Assert(!vm.HasSaveError && SnapshotJson.Serialize(game.CreateSnapshot(0)) == live &&
                vm.DragonBoatStandings.Select(team => team.Rank).SequenceEqual([1, 2, 3, 4]),
                "Cold recovery between actual turns must preserve live four-team scores and arrival ranks.");
            Until(vm, () => vm.HasGameOver, endPlay: true, root: root, window: window);
            var snapshot = game.CreateSnapshot(0);
            var summary = vm.CompletedMatch!;
            Layout(root);
            Program.Assert(snapshot is { Winner: Winner.RankedTeams, WinnerTeamId: "team:shu" } &&
                game.Events.Select(entry => entry.Payload).OfType<RoundEndedEvent>().Select(round => round.RoundNumber).SequenceEqual([1, 2]) &&
                game.Events.Count(entry => entry.Payload is DragonBoatTeamRankedEvent) == 4 &&
                vm.GameOutcomeTitle == "同船第 3 名" && vm.GameOverText.Contains("蜀船获得第一名") &&
                GameSoundRules.Outcome(snapshot) == GameSound.Defeat &&
                summary is { IsDragonBoat: true, IsSurvival: false, DragonBoatRounds: 2, DragonBoatTeams.Count: 4 } &&
                summary.Players.Select(player => player.TeamRank).SequenceEqual(new int?[] { 1, 1, 2, 2, 3, 3, 4, 4 }) &&
                summary.Players.All(player => player.SurvivalRank is null && player.IsAlive && player.Camp != "未知") &&
                summary.HumanPerformance.Contains("同船第3名") && summary.DurationText.Contains("2 个实际轮次") &&
                ((ItemsControl)window.FindName("DragonBoatResultRows")).Items.Count == 4 &&
                history.Read().Entries.Single() is { Outcome: MatchOutcome.Loss, OutcomeLabel: "同船第3名" },
                "The second actual round must finish a ranked-team match, display all four boats and group both teammates under their shared rank without claiming personal deaths.");
            Program.Assert(GameSoundRules.Outcome(snapshot with { WinnerTeamId = "team:wei" }) == GameSound.Victory &&
                JsonSerializer.Deserialize<MatchSummary>(JsonSerializer.Serialize(summary))!.DragonBoatRankingText == summary.DragonBoatRankingText,
                "Ranked-team audio must compare the actual human team, and the public four-boat report must survive history JSON without private cards.");
            var validate = typeof(FileMatchHistoryStore).GetMethod("Validate", BindingFlags.Static | BindingFlags.NonPublic)!;
            var entry = history.Read().Entries.Single();
            validate.Invoke(null, [entry]);
            var rejected = false;
            try { validate.Invoke(null, [entry with { Summary = summary with {
                DragonBoatTeams = summary.DragonBoatTeams!.Select(team => team with { Rank = 1 }).ToArray() } }]); }
            catch (TargetInvocationException error) when (error.InnerException is InvalidDataException) { rejected = true; }
            Program.Assert(rejected, "The persisted public team report must reject corrupt duplicate ranks rather than display a fabricated four-way first place.");
            Program.Render(root, 1440, 880, Path.Combine(output, "183-dragon-boat-ranked-report.png"));
            vm.SaveGameCommand.Execute(null); vm.LoadManualGameCommand.Execute(null);
            Program.Assert(!vm.HasSaveError && vm.GameOutcomeTitle == "同船第 3 名" && vm.MatchHistory.Count == 1 &&
                vm.CompletedMatch!.DragonBoatRankingText == summary.DragonBoatRankingText,
                "Loading a completed two-round race must restore exact team standings without recording another match.");
        }
        finally { window.Content = null; window.DataContext = null; window.Close(); }

        void CreditOpponent(int seat)
        {
            vm.SelectActiveSkillCommand.Execute(vm.HumanActiveSkillActions.Single(action => action.ProgramSkillId == ScoreUiFixture.Driver));
            vm.SelectTargetCommand.Execute(vm.Seats.Single(target => target.Seat == seat));
            Click(root, (Button)window.FindName("PlayCardButton"));
            Until(vm, () => game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        }
    }

    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Program.Assert(result.Accepted, result.Error?.Message ?? "Native dragon-boat UI command rejected."); }
    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(1440, 880)); root.Arrange(new Rect(0, 0, 1440, 880)); root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout();
    }
    private static void Click(FrameworkElement root, Button button)
    {
        Layout(root);
        Program.Assert(button.IsEnabled && button.Command!.CanExecute(button.CommandParameter), "The actual dragon-boat button must be enabled before its routed click.");
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
        Layout(root);
    }
    private static void Until(MainViewModel vm, Func<bool> done, bool endPlay = false, FrameworkElement? root = null, MainWindow? window = null)
    {
        for (var step = 0; step < 400; step++)
        {
            if (done()) return;
            if (endPlay && Program.Engine(vm).PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 })
                Click(root!, (Button)window!.FindName("EndTurnButton"));
            else
            {
                Program.Assert(vm.StepAiCommand.CanExecute(null), "The bounded dragon-boat GUI must advance through its actual native setup/turn boundary.");
                vm.StepAiCommand.Execute(null);
            }
        }
        throw new InvalidOperationException("The dragon-boat UI missed its bounded native boundary: " + SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0)));
    }

    private sealed class ScoreUiFixture : IGameContentPackage
    {
        internal const string Mode = "fixture:dragon-boat-score-ui", Driver = "fixture:dragon-boat-score-driver",
            Quiet = "fixture:dragon-boat-score-quiet", Owner = "fixture:dragon-boat-wei-10";
        public PackageManifest Manifest { get; } = new("dragon-boat-score-ui-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rules = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.dragonboat-activity.rules.json")!);
            using var presentation = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.dragonboat-activity.presentation.json")!);
            var activity = SkillProgramCatalog.Load(rules.ReadToEnd(), presentation.ReadToEnd());
            foreach (var (id, program) in activity.Programs) builder.AddSkill(new(id, activity.Presentations[id].Name, activity.Presentations[id].Description)
            { Program = program, ProgramPresentation = activity.Presentations[id], Tags = id == "dragonboat:niepan" ? SkillTag.Limited : SkillTag.Locked });
            var fixtureRules = $$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"{{{Driver}}}","revision":1,
                 "activations":[{"id":"credit-opponent","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
                  "effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]}]},
                 {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"skip-ai-play","window":"afterNormalDraw","subject":"owner","optional":false,
                  "effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """;
            var fixture = SkillProgramCatalog.Load(fixtureRules, JsonSerializer.Serialize(new {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> {
                    [Driver] = new { name = "Native score fixture", description = "Let the selected opponent cause one real damage to this character; no synthetic score events." },
                    [Quiet] = new { name = "Quiet actual turn", description = "Skip ordinary AI Play while preserving native draws, actual turns, mode grants and round boundaries." } } }));
            foreach (var (id, program) in fixture.Programs) builder.AddSkill(new(id, fixture.Presentations[id].Name, fixture.Presentations[id].Description)
            { Program = program, ProgramPresentation = fixture.Presentations[id] });
            var pools = new Dictionary<string, IReadOnlyList<string>>();
            foreach (var faction in new[] { "wei", "shu", "wu", "qun" })
            {
                var ids = Enumerable.Range(0, 12).Select(index => $"fixture:dragon-boat-{faction}-{index}").ToArray();
                foreach (var id in ids) builder.AddGeneral(new(id, id, "", id == Owner ? Driver : Quiet, faction, 20) { CharacterId = id });
                pools["team:" + faction] = ids;
            }
            foreach (var name in new[] { "tang-zi", "huang-quan", "su-fei", "liu-qi" })
                builder.AddGeneral(new("dragonboat:" + name, name, "", Quiet, "wei", 20) { CharacterId = "character:" + name });
            pools["team:wei"] = [.. pools["team:wei"], "dragonboat:tang-zi", "dragonboat:huang-quan"];
            pools["team:shu"] = [.. pools["team:shu"], "dragonboat:huang-quan", "dragonboat:liu-qi"];
            pools["team:wu"] = [.. pools["team:wu"], "dragonboat:tang-zi", "dragonboat:su-fei"];
            pools["team:qun"] = [.. pools["team:qun"], "dragonboat:su-fei", "dragonboat:liu-qi"];
            builder.AddDeck(new("fixture:dragon-boat-score-deck", "Real physical Dodge scoring pile", 4, 2, [new("standard:dodge", 200)]));
            builder.AddMode(new(Mode, "龙舟争渡 UI fixture", 8, 8, new Dictionary<string, int>(),
                "fixture:dragon-boat-score-deck", 6, pools.Values.SelectMany(ids => ids).Distinct().ToArray(), ContentModeKind.Team,
                TeamCounts: pools.ToDictionary(pair => pair.Key, _ => 2))
            { DragonBoatActivity = new(pools) });
        }
    }
}
