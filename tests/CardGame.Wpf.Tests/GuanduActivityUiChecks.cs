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
using CardGame.Wpf.ViewModels;

internal static class GuanduActivityUiChecks
{
    public static void TwoArmiesCommanderGeneralEventAndCrossConfigurationRestore(string output)
    {
        var registry = ComposedSkillContentRegistry.CreateShowcase(includeGuanduActivities: true);
        var mode = registry.Modes[StandardGuanduActivityPackage.ModeId];
        var activity = mode.GuanduActivity ?? throw new InvalidOperationException("The actual Guandu mode is missing its activity definition.");
        foreach (var (family, edition) in StandardGuanduActivityPackage.OrdinaryVersions)
        {
            var id = "guandu:" + family;
            var source = GeneralArt.GetSkin(edition);
            Program.Assert(source is not null && GeneralArt.NormalizeKey(id) == GeneralArt.NormalizeKey(edition) &&
                GeneralArt.GetSkin(id)?.LocalPath == source.LocalPath &&
                new GeneralPortraitViewModel(id).Image is System.Windows.Media.Imaging.BitmapSource { PixelWidth: > 0, PixelHeight: > 0 },
                $"The actual Guandu clone {id} must decode the exact {edition} local portrait rather than fall back to its name.");
        }
        Program.Assert(GeneralArt.GetSkin("guandu:cao-cao")?.LocalPath == GeneralArt.GetSkin("boundary:cao-cao")?.LocalPath &&
            GeneralArt.GetSkin("guandu:guan-yu")?.LocalPath == GeneralArt.GetSkin("sp:guan-yu")?.LocalPath &&
            new[] { "xu-you", "gao-lan", "shen-pei", "zhang-he", "xun-chen" }.All(family =>
                new GeneralPortraitViewModel("guandu:" + family).Image is System.Windows.Media.Imaging.BitmapSource { PixelWidth: > 0, PixelHeight: > 0 }),
            "Guandu must retain the Boundary Cao Cao and SP Guan Yu editions and reuse each available same-character activity portrait.");
        var store = new MemorySaveStore();
        using var vm = new MainViewModel(false, 17, true, store, useExpandedContent: true, contentRegistry: registry)
        { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false, ManualDiscardEnabled = false };
        var window = new MainWindow(vm); window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        try
        {
            vm.OpenLobbyCategoryCommand.Execute("activity");
            vm.SelectedTableMode = vm.VisibleTableModes.Single(option => option.ModeId == mode.Id);
            Layout(root);
            var teams = (ListBox)window.FindName("TeamChoices");
            var roles = (ListBox)window.FindName("GuanduRoleChoices");
            Program.Assert(vm.IsGuanduActivityModeSelection && vm.IsActivityModeSelection && vm.IsTeamModeSelection &&
                !vm.IsIdentityModeSelection && vm.SelectedGuanduRole.Role == GuanduSeatRole.General &&
                teams.Visibility == Visibility.Visible && teams.Items.Count == 2 && roles.Visibility == Visibility.Visible && roles.Items.Count == 2 &&
                vm.StartingTeams.Select(team => team.TeamId).SequenceEqual(["team:cao", "team:yuan"]),
                "The actual Guandu lobby must offer both armies and the default general/fixed commander role without identity choices.");
            teams.SelectedItem = vm.StartingTeams.Single(team => team.TeamId == "team:cao");
            roles.SelectedItem = vm.GuanduRoles.Single(role => role.Role == GuanduSeatRole.Commander);
            Layout(root);
            Program.Assert(vm.SelectedGuanduRole.Role == GuanduSeatRole.Commander && vm.TeamModeSetupText.Contains("曹操军") &&
                vm.TeamModeSetupText.Contains("三名人机") && vm.TeamModeSetupText.Contains("主帅") && vm.TeamModeSetupText.Contains("牌堆耗尽") &&
                vm.TeamModeSetupText.Contains("本地五阶") && vm.CurrentGuideTitle.Contains("官渡"),
                "Bound commander and army choices must explain three AI allies, commander victory and the actual deck-loss boundary.");
            Program.Render(root, 1440, 880, Path.Combine(output, "184-guandu-army-and-role.png"));
            vm.StartNewGameFromLobbyCommand.Execute(null);
            vm.ContinueFromIdentityRevealCommand.Execute(null);
            Until(vm, () => vm.IsGeneralSelectionPending);
            Program.Assert(vm.GeneralChoices is [{ GeneralId: var commander }] && commander == activity.Commanders["team:cao"],
                "Choosing commander must expose only the fixed army commander, with no ordinary substitute.");
            Click(root, (Button)window.FindName("ConfirmGeneralChoiceButton"));
            Until(vm, () => Program.Engine(vm).Events.Any(entry => entry.Payload is SetupCompletedEvent));
            var game = Program.Engine(vm); var snapshot = View(game); var state = snapshot.GuanduActivity!;
            var human = snapshot.Players.Single(player => player.IsHuman);
            Layout(root);
            Program.Assert(vm.Seats.Single(seat => seat.IsHuman).Portrait.Image is System.Windows.Media.Imaging.BitmapSource { PixelWidth: > 0, PixelHeight: > 0 },
                "The real bound Guandu commander seat must display its decoded portrait on the actual table.");
            Program.Assert(state is { HumanRole: GuanduSeatRole.Commander, PlayerTeamId: "team:cao", DeckExhausted: false } &&
                human.Seat == state.PlayerCommanderSeat && human.GeneralId == activity.Commanders["team:cao"] &&
                snapshot.Players[state.OpponentCommanderSeat].GeneralId == activity.Commanders["team:yuan"] &&
                snapshot.Players.Count == 8 && snapshot.Players.GroupBy(player => player.TeamId).All(team => team.Count() == 4) &&
                vm.Seats.Count(seat => seat.IsTeammate) == 3 && vm.Seats.Count(seat => !seat.IsHuman && !seat.IsTeammate) == 4 &&
                vm.Seats.Count(seat => seat.RoleLabel.EndsWith("主帅")) == 2 &&
                snapshot.Players.Where(player => !player.IsHuman).All(player => player.Hand.Count == 0) &&
                vm.TableArenaTitle == "官 渡 之 战" && vm.IdentityObjective.Contains("敌方主帅") &&
                vm.GuanduRoundText.Contains("轮") && vm.GuanduEventText.Length > 0 && vm.GuanduCommanderText.Contains("玩家方失败") &&
                ((FrameworkElement)window.FindName("GuanduEventBanner")).Visibility == Visibility.Visible &&
                vm.IsGuanduGuide && !vm.IsTeamGuide && !vm.IsIdentityGuide,
                "The real commander game must render eight public army seats, two fixed leaders, one of nine events and no teammate hands or Jiange labels.");
            Program.Render(root, 1440, 880, Path.Combine(output, "185-guandu-commanders-and-event.png"));
            vm.SaveGameCommand.Execute(null); var saved = SnapshotJson.Serialize(View(game));
            using var restored = new MainViewModel(false, 17, true, store, contentRegistry: ComposedSkillContentRegistry.CreateShowcase())
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
            restored.LoadManualGameCommand.Execute(null);
            Program.Assert(!restored.HasSaveError && restored.IsGuanduActivityTable && restored.SelectedStartingTeam.TeamId == "team:cao" &&
                restored.SelectedGuanduRole.Role == GuanduSeatRole.Commander && SnapshotJson.Serialize(View(Program.Engine(restored))) == saved,
                "Cross-configuration recovery must restore the actual Guandu package, army, commander role and fixed event without substituting another game.");
            restored.StartNewGameFromLobbyCommand.Execute(null); restored.ContinueFromIdentityRevealCommand.Execute(null);
            Until(restored, () => restored.IsGeneralSelectionPending);
            Program.Assert(restored.GeneralChoices is [{ GeneralId: var fixedId }] && fixedId == activity.Commanders["team:cao"],
                "A restored commander rematch must retain the same single fixed commander choice.");
            restored.ConfirmGeneralChoiceCommand.Execute(null);
            Until(restored, () => Program.Engine(restored).Events.Any(entry => entry.Payload is SetupCompletedEvent));
            Program.Assert(!restored.HasSaveError && View(Program.Engine(restored)).GuanduActivity is { HumanRole: GuanduSeatRole.Commander, PlayerTeamId: "team:cao" },
                "Rematching after recovery must retain the restored army and fixed commander role.");
            vm.SelectedStartingTeam = vm.StartingTeams.Single(team => team.TeamId == "team:yuan");
            vm.SelectedGuanduRole = vm.GuanduRoles.Single(role => role.Role == GuanduSeatRole.General);
            vm.StartNewGameFromLobbyCommand.Execute(null); vm.ContinueFromIdentityRevealCommand.Execute(null);
            Until(vm, () => vm.IsGeneralSelectionPending);
            game = Program.Engine(vm); snapshot = View(game);
            Program.Assert(snapshot.GuanduActivity is { HumanRole: GuanduSeatRole.General, PlayerTeamId: "team:yuan" } &&
                snapshot.Players.Single(player => player.IsHuman).Seat != snapshot.GuanduActivity.PlayerCommanderSeat &&
                vm.GeneralChoices.Count == activity.CandidateCount &&
                vm.GeneralChoices.All(choice => activity.GeneralPools["team:yuan"].Contains(choice.GeneralId) &&
                    choice.GeneralId != activity.Commanders["team:yuan"]) &&
                vm.GeneralSelectionSubtitle.Contains("三名AI队友") && vm.GeneralChoices.All(choice => choice.HealthText.Contains("五阶体力上限")),
                "A real Yuan general opening must expose its private faction pool rather than offer either fixed commander.");
            vm.SaveGameCommand.Execute(null); saved = SnapshotJson.Serialize(View(game));
            vm.LoadManualGameCommand.Execute(null);
            Program.Assert(!vm.HasSaveError && vm.IsGeneralSelectionPending && vm.SelectedGuanduRole.Role == GuanduSeatRole.General &&
                vm.SelectedStartingTeam.TeamId == "team:yuan" && SnapshotJson.Serialize(View(Program.Engine(vm))) == saved,
                "Saving during the actual general draft must retain its prompt, army, role and event exactly.");
            Program.AssertNoBindingErrors();
        }
        finally { window.Content = null; window.DataContext = null; window.Close(); }
    }

    public static void PhysicalCardsSelectBoundedRecipientsWholeArmyAndRecast(string output)
    {
        foreach (var kind in new[] { CardKind.StoreGrain, CardKind.Reinforcements, CardKind.Intoxication, CardKind.UnitedEffort })
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new CardUiFixture(kind));
            var original = GameEngine.CreateStandard(new GameOptions
            {
                Seed = 7, PlayerCount = 8, HumanSeat = 0, HumanRole = null, HumanTeamId = "team:cao",
                HumanGuanduRole = GuanduSeatRole.Commander, ModeId = CardUiFixture.Mode, UseInteractiveSetup = true,
                AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false, MaxTurns = 8
            }, registry);
            Accept(original, new StartGameCommand());
            NativeUntil(() => original.PendingDecision is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
            Accept(original, new SelectGeneralCommand(0, CardUiFixture.Commander, original.Revision, original.PendingDecision!.PromptId));
            NativeUntil(() => original.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
            if (kind == CardKind.Reinforcements)
                foreach (var target in new[] { 0, 1, 4 })
                {
                    Accept(original, new UseProgramSkillCommand(0, CardUiFixture.Driver, "hurt", [], [target], original.Revision,
                        original.PendingDecision!.PromptId));
                    NativeUntil(() => original.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
                }
            var store = new MemorySaveStore(); var history = new MemoryMatchHistoryStore();
            store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, original.CreateCheckpoint()));
            using var vm = new MainViewModel(false, 7, true, store, historyStore: history, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
            vm.LoadManualGameCommand.Execute(null);
            var window = new MainWindow(vm); window.ApplyTemplate();
            var root = (FrameworkElement)window.Content;
            try
            {
                Layout(root);
                var game = Program.Engine(vm); var actionKind = (LegalActionKind)(int)kind;
                var action = game.GetHumanLegalActions().First(action => action.Kind == actionKind && action.ConversionSource is null);
                var card = vm.Hand.Single(card => card.Id == action.CardId); var commands = game.AcceptedCommands.Count;
                Click(root, Program.Find<Button>(root).Single(button => ReferenceEquals(button.Command, vm.SelectCardCommand) &&
                    ReferenceEquals(button.CommandParameter, card)));
                Program.Assert(!vm.HasSaveError && card.IsSelected && !vm.CanConfirmSelected &&
                    vm.IsMultiTargetCardSelected == (kind != CardKind.Intoxication),
                    "Each real Guandu hand card must request its published recipients before confirmation.");
                IReadOnlyList<int> expected;
                if (kind == CardKind.UnitedEffort)
                {
                    var teams = game.GetHumanLegalActions().Where(candidate => candidate.CardId == card.Id && candidate.Kind == actionKind).ToArray();
                    var own = teams.Single(candidate => candidate.TargetSeats.Contains(0));
                    var enemy = teams.Single(candidate => !candidate.TargetSeats.Contains(0));
                    Program.Assert(vm.CanRecastSelected && vm.ShowRecastAction && teams.Length == 2,
                        "Physical United Effort must expose both complete armies and an independent targetless recast.");
                    Program.Assert(View(game).DrawPileCount == 1,
                        "The bounded 44-card pile must leave one real card after eight five-tier hands and the commander's first normal draw.");
                    vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == own.TargetSeats[0]));
                    Program.Assert(Selected(vm).SetEquals(own.TargetSeats) && vm.CanConfirmSelected && !vm.CanRecastSelected &&
                        enemy.TargetSeats.All(seat => vm.Seats.Single(view => view.Seat == seat).IsLegalTarget),
                        "One allied click must select the exact whole army while the other army remains visibly selectable.");
                    vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == enemy.TargetSeats[0]));
                    Program.Assert(Selected(vm).SetEquals(enemy.TargetSeats) && vm.CanConfirmSelected,
                        "Clicking the other army must replace the whole selection rather than append a mixed subset.");
                    expected = enemy.TargetSeats;
                    Program.Render(root, 1440, 880, Path.Combine(output, "186-guandu-whole-army-card.png"));
                }
                else
                {
                    expected = kind == CardKind.StoreGrain ? [0, 1, 4] : kind == CardKind.Reinforcements ? [1, 4] : [4];
                    foreach (var target in expected) vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == target));
                    Program.Assert(Selected(vm).SetEquals(expected) && vm.CanConfirmSelected &&
                        (kind != CardKind.Reinforcements || !vm.Seats.Single(seat => seat.IsHuman).IsLegalTarget),
                        "Store Grain may include self and three recipients; Reinforcements must exclude self; Intoxication must target one enemy.");
                }
                Program.Assert(game.AcceptedCommands.Count == commands, "Target preview must not pay cards or submit rule commands.");
                Click(root, (Button)window.FindName("PlayCardButton"));
                Until(vm, () => game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
                var played = game.AcceptedCommands.Skip(commands).OfType<PlayCardCommand>().Single();
                Program.Assert(played.CardId == card.Id && played.TargetSeats!.SequenceEqual(expected) &&
                    game.CardMovements.Count(move => move.CardId == card.Id && move.To == CardLocation.DiscardPile) == 1,
                    "The actual confirm button must submit the canonical full legal targets and retire the one paid physical entity.");
                if (kind == CardKind.UnitedEffort)
                {
                    Program.Assert(expected.All(seat => View(game).Players[seat].IsChained), "The selected enemy army must become chained through the actual card effect.");
                    var recast = game.GetHumanLegalActions().First(candidate => candidate.Kind == LegalActionKind.Recast);
                    card = vm.Hand.Single(candidate => candidate.Id == recast.CardId);
                    Click(root, Program.Find<Button>(root).Single(button => ReferenceEquals(button.Command, vm.SelectCardCommand) &&
                        ReferenceEquals(button.CommandParameter, card)));
                    var uses = game.Events.Count(entry => entry.Payload is CardUseDeclaredEvent);
                    Click(root, (Button)window.FindName("RecastButton"));
                    Until(vm, () => game.State.Status == EngineStatus.Completed);
                    Program.Assert(game.AcceptedCommands.OfType<RecastCardCommand>().Count(command => command.CardId == card.Id) == 1 &&
                        game.Events.Any(entry => entry.Payload is CardRecastEvent fact && fact.CardId == card.Id && fact.CardKind == kind && fact.DrawCount == 1) &&
                        game.Events.Count(entry => entry.Payload is CardUseDeclaredEvent) == uses,
                        "The visible recast button must execute one native recast and draw without inventing a card use or recipients.");
                    Program.Assert(View(game).GuanduActivity is { DeckExhausted: true, PlayerTeamId: "team:cao" } &&
                        View(game).WinnerTeamId == "team:yuan" && vm.HasGameOver && vm.GuanduCommanderText.Contains("牌堆已耗尽") &&
                        vm.GameOverText.Contains("玩家方败北") && vm.GameOutcomeTitle == "败 北" &&
                        GameSoundRules.Outcome(View(game)) == GameSound.Defeat &&
                        vm.CompletedMatch is { Players.Count: 8 } summary && summary.Players.All(player => player.Camp != "未知") &&
                        summary.Players.Count(player => player.Camp.EndsWith("主帅")) == 2 &&
                        history.Read().Entries.Single().Outcome == MatchOutcome.Loss,
                        "The real final draw must render deck exhaustion as the player's defeat in banner, outcome, audio and the public eight-seat history.");
                }
                Program.AssertNoBindingErrors();
            }
            finally { window.Content = null; window.DataContext = null; window.Close(); }

            void NativeUntil(Func<bool> done)
            {
                for (var step = 0; step < 180; step++) { if (done()) return; Accept(original, new AdvanceOneStepCommand(original.Revision)); }
                throw new InvalidOperationException("The small Guandu card UI fixture missed its real human boundary.");
            }
        }
    }

    private static HashSet<int> Selected(MainViewModel vm) => vm.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).ToHashSet();
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Program.Assert(result.Accepted, result.Error?.Message ?? "Native Guandu UI command rejected."); }
    private static void Click(FrameworkElement root, Button button)
    {
        Layout(root);
        Program.Assert(button.IsEnabled && button.Command!.CanExecute(button.CommandParameter), "The real Guandu button must be enabled before its routed click.");
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
        Layout(root);
    }
    private static GameSnapshot View(GameEngine game) => game.CreateSnapshot(game.State.HumanSeat);
    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(1440, 880)); root.Arrange(new Rect(0, 0, 1440, 880)); root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout();
    }
    private static void Until(MainViewModel vm, Func<bool> done)
    {
        for (var step = 0; step < 240; step++)
        {
            if (done()) return;
            Program.Assert(vm.StepAiCommand.CanExecute(null), "The real Guandu setup must advance to its exact native human boundary.");
            vm.StepAiCommand.Execute(null);
        }
        throw new InvalidOperationException("The bounded Guandu UI missed its native setup boundary.");
    }

    private sealed class CardUiFixture(CardKind kind) : IGameContentPackage
    {
        internal const string Mode = "fixture:guandu-card-ui", Driver = "fixture:guandu-card-ui-driver",
            Quiet = "fixture:guandu-card-ui-quiet", Commander = "fixture:guandu-ui-cao-0";
        public PackageManifest Manifest { get; } = new("guandu-card-ui-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            foreach (var bundle in new[] { "guandu-events", "guandu-activity" })
            {
                using var rules = new StreamReader(assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.{bundle}.rules.json")!);
                using var presentation = new StreamReader(assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.{bundle}.presentation.json")!);
                var catalog = SkillProgramCatalog.Load(rules.ReadToEnd(), presentation.ReadToEnd());
                foreach (var id in catalog.Programs.Keys.Where(id => id is "guandu:battle-event" or "guandu:niepan"))
                    builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                    { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id], Tags = id == "guandu:niepan" ? SkillTag.Limited : SkillTag.Locked });
            }
            var fixture = SkillProgramCatalog.Load($$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
                  {"id":"{{{Driver}}}","revision":1,"activations":[{"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,
                    "effects":[{"op":"loseHp","target":"selectedTarget","amount":2}]}]},
                  {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,
                    "effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new Dictionary<string, object> { [Driver] = new { name = "Wound fixture", description = "Pay a real bounded health loss before native healing target selection." },
                        [Quiet] = new { name = "Quiet native turn", description = "Preserve draws and actual turns while skipping AI Play." } } }));
            foreach (var (id, program) in fixture.Programs) builder.AddSkill(new(id, fixture.Presentations[id].Name, fixture.Presentations[id].Description)
            { Program = program, ProgramPresentation = fixture.Presentations[id] });
            var pools = new Dictionary<string, IReadOnlyList<string>>();
            foreach (var army in new[] { "cao", "yuan" })
            {
                var ids = Enumerable.Range(0, 9).Select(index => $"fixture:guandu-ui-{army}-{index}").ToArray();
                foreach (var id in ids) builder.AddGeneral(new(id, id, "", id == Commander ? Driver : Quiet, army == "cao" ? "wei" : "qun", 8) { CharacterId = id });
                pools["team:" + army] = ids;
            }
            const string cardId = "fixture:guandu-ui-card", deck = "fixture:guandu-ui-deck";
            var card = CardCatalog.Get(kind);
            builder.AddCard(new(cardId, card.DisplayName, card.CategoryName, card.Description, kind));
            builder.AddDeck(new(deck, "Small physical Guandu UI pile", 4, 2, [new(cardId, kind == CardKind.UnitedEffort ? 44 : 180)]));
            IReadOnlyList<string> layout = ["team:cao", "team:cao", "team:cao", "team:cao", "team:yuan", "team:yuan", "team:yuan", "team:yuan"];
            builder.AddMode(new(Mode, "官渡真实目标 UI fixture", 8, 8, new Dictionary<string, int>(), deck, 6,
                pools.Values.SelectMany(ids => ids).ToArray(), ContentModeKind.Team,
                new Dictionary<string, int> { ["team:cao"] = 4, ["team:yuan"] = 4 })
            { GuanduActivity = new(pools, new Dictionary<string, string> { ["team:cao"] = Commander, ["team:yuan"] = pools["team:yuan"][0] },
                [GuanduBattleEvent.Underdog], Enumerable.Range(0, 5).Select(_ => layout).ToArray()) });
        }
    }
}
