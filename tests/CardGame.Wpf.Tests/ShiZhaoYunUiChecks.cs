using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ShiZhaoYunUiChecks
{
    private const string Hero = "mobile:shi-zhao-yun", Wuyi = "mobile:shi-wuyi",
        Peer = "fixture:shi-zhao-yun-ui-peer", Deck = "fixture:shi-zhao-yun-ui-deck";

    public static void CardlessSkillRailDraftCancelTargetsAndRoundLimit(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var prepared = PrepareUnlocked(registry);
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow,
            false, prepared.CreateCheckpoint()));
        using var model = new MainViewModel(false, 83, true, store, contentRegistry: registry,
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        var game = Program.Engine(model);
        Program.Assert(Boundary(game) == Boundary(prepared), "Loading the real unlocked scene must preserve its native committed boundary.");
        var actions = game.GetHumanLegalActions().Where(IsWuyiSlash).ToArray();
        var targets = actions.SelectMany(action => action.TargetSeats).Distinct().Order().ToArray();
        Program.Assert(targets.Length >= 2, "The native zero-card Slash must offer more than one actual recipient.");
        var chosen = targets[^1];
        var window = new MainWindow(model);
        var root = (FrameworkElement)window.Content;
        try
        {
            Layout(root);
            var skill = WuyiChip();
            Program.Assert(skill.IsAvailable && !skill.IsDisabled && skill.ProgressText == "1/4" &&
                skill.StateText.Contains("1/4", StringComparison.Ordinal) && skill.StateText.Contains("已用 0/1", StringComparison.Ordinal) &&
                model.HumanSkillCards.Single(item => item.ContentId == "mobile:shi-cuifeng").ProgressText == "0/2" &&
                Bound(root, model.ActivateHumanSkillCommand, skill).IsEnabled,
                "A real first hand basic must expose an enabled Wuyi rail button and its public one-of-four progress.");
            Program.Assert(model.SupplementalActiveSkillActions.All(action => !IsWuyiSlash(action)),
                "The skill rail must own the cardless use without duplicating one supplemental banner for every target.");

            var unchanged = Boundary(game);
            Click(Bound(root, model.ActivateHumanSkillCommand, skill));
            AssertEmptyDraft(unchanged);
            SelectChosenTarget();
            Program.Assert(model.CanConfirmSelected && model.CanConfirmActiveSkill &&
                model.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).SequenceEqual([chosen]) &&
                Boundary(game) == unchanged,
                "Selecting a nonfirst legal portrait must prepare that exact target without submitting any card or skill command.");
            Click((Button)window.FindName("CancelActionButton"));
            Layout(root);
            Program.Assert(!model.HasSelection && !model.IsActiveSkillSelectionPending &&
                model.Seats.All(seat => !seat.IsSelectedTarget) && WuyiChip().IsAvailable && Boundary(game) == unchanged,
                "The actual cancel button must clear the local draft and preserve the cardless round allowance and native journal.");

            Click(Bound(root, model.ActivateHumanSkillCommand, WuyiChip()));
            AssertEmptyDraft(unchanged);
            SelectChosenTarget();
            Layout(root);
            Program.Assert(model.CanConfirmSelected && ((Button)window.FindName("PlayCardButton")).IsEnabled,
                "The second explicit target selection must enable the common card confirmation button.");
            Program.Render(root, 1120, 780, Path.Combine(output, "shi-zhao-yun-wuyi-target-selected.png"));
            var action = game.GetHumanLegalActions().Single(candidate => IsWuyiSlash(candidate) && candidate.TargetSeats.SequenceEqual([chosen]));
            var revision = game.Revision;
            var commands = game.AcceptedCommands.Count;
            var movements = game.CardMovements.Count;
            var expected = new PlayCardCommand(0, 0, action.TargetSeats, revision, game.CreateSnapshot(0).PendingDecision!.PromptId,
                action.PlayedCardKind, action.TargetCardId)
            { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources };
            var reference = GameReplay.Restore(game.CreateCheckpoint(), registry);
            Accept(reference, expected);
            Click((Button)window.FindName("PlayCardButton"));
            Program.Assert(game.AcceptedCommands.Count == commands + 1 && game.Revision == revision + 1 &&
                CommandJson.Serialize([game.AcceptedCommands.Last()]) == CommandJson.Serialize([expected]) &&
                game.AcceptedCommands.Last() is PlayCardCommand { CardId: 0, ConversionSource.SkillId: Wuyi } &&
                game.CardMovements.Count == movements && Boundary(game) == Boundary(reference),
                "Only the real card confirm button may submit one exact native CardId-zero Slash with its published conversion source and chosen target, without any physical payment.");

            for (var step = 0; step < 80 && game.CreateSnapshot(0).PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            {
                Program.Assert(model.CanStepAi, "The paid zero-card use must retain a step-able native continuation until Play resumes.");
                model.StepAiCommand.Execute(null);
            }
            Layout(root);
            skill = WuyiChip();
            Program.Assert(game.CreateSnapshot(0).PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } &&
                !skill.IsAvailable && !Bound(root, model.ActivateHumanSkillCommand, skill).IsEnabled &&
                skill.ProgressText == "1/4" && skill.StateText.Contains("已用 1/1", StringComparison.Ordinal) &&
                game.GetHumanLegalActions().All(action => !IsWuyiSlash(action)) && !model.HasSelection,
                "After native resolution, the same real round's spent cardless allowance must disable the Wuyi rail button.");
            var afterUse = Boundary(game);
            model.ActivateHumanSkillCommand.Execute(skill);
            model.ConfirmSelectedCommand.Execute(null);
            Program.Assert(Boundary(game) == afterUse && !model.IsActiveSkillSelectionPending,
                "Invoking the unavailable rail entry or confirmation again cannot reopen or repay the already-used cardless action.");

            HumanSkillViewModel WuyiChip() => model.HumanSkillCards.Single(item => item.ContentId == Wuyi);
            void AssertEmptyDraft(string boundary)
            {
                Layout(root);
                Program.Assert(model.HasSelection && model.IsActiveSkillSelectionPending && !model.CanConfirmSelected &&
                    model.Hand.All(card => !card.IsSelected) && model.Seats.All(seat => !seat.IsSelectedTarget) &&
                    model.Seats.Where(seat => seat.IsLegalTarget).Select(seat => seat.Seat).Order().SequenceEqual(targets) &&
                    Boundary(game) == boundary,
                    "Clicking Wuyi must only open a zero-material target draft containing every published legal portrait, with explicit confirmation still required.");
            }
            void SelectChosenTarget()
            {
                Layout(root);
                Click(Bound(root, model.SelectTargetCommand, model.Seats.Single(seat => seat.Seat == chosen)));
            }
        }
        finally { window.Content = null; window.Close(); }
    }

    private static bool IsWuyiSlash(LegalAction action) => action is { CardId: 0, PlayedCardKind: CardKind.Slash,
        ConversionSource.SkillId: Wuyi, RoundOnceCardlessBasicUse: not null };

    private static GameEngine PrepareUnlocked(ContentRegistry registry)
    {
        var setup = new ShanheBattleSetup(83, 1, "shi-zhao-yun-ui", Hero, 4, 0, 0, 0, 0, [],
            [new(CardKind.Alcohol, Suit.Heart, 7)], [],
            Enumerable.Range(0, 3).Select(_ => new ShanheCombatantDefinition(Peer) { StartingHand = [] }).ToArray(), [], new string('d', 64));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4,
            HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
            ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 6 }, registry);
        Accept(game, new StartGameCommand());
        ReachPlay(game);
        var alcohol = game.GetHumanLegalActions().Single(action => action.Kind == LegalActionKind.Alcohol && action.CardId is > 0 && action.ConversionSource is null);
        Accept(game, new PlayCardCommand(0, alcohol.CardId!.Value, alcohol.TargetSeats, game.Revision,
            game.CreateSnapshot(0).PendingDecision!.PromptId, alcohol.PlayedCardKind, alcohol.TargetCardId));
        ReachPlay(game);
        Program.Assert(game.GetHumanLegalActions().Any(IsWuyiSlash), "The original actual hand Alcohol must unlock the first Wuyi ability before opening the UI.");
        return game;
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 80; step++)
        {
            if (game.CreateSnapshot(0).PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) return;
            Program.Assert(game.CreateSnapshot(0).PendingDecision is null, "The bounded unlocked fixture encountered an unexpected human prompt.");
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The bounded Shi Zhao Yun UI fixture did not reach native human Play.");
    }

    private static string Boundary(GameEngine game) => JsonSerializer.Serialize(new { game.Revision,
        Commands = CommandJson.Serialize(game.AcceptedCommands), game.CardMovements,
        Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray() });
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Program.Assert(result.Accepted, result.Error?.Message ?? "Rejected native Shi Zhao Yun UI command."); }
    private static Button Bound(FrameworkElement root, ICommand command, object parameter) => Program.Find<Button>(root)
        .Single(button => ReferenceEquals(button.Command, command) && ReferenceEquals(button.CommandParameter, parameter));
    private static void Click(Button button)
    {
        Program.Assert(button.IsEnabled && button.Visibility == Visibility.Visible && button.Command!.CanExecute(button.CommandParameter),
            "The actual bound WPF control must be enabled and visible before its click.");
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    }
    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(1120, 780)); root.Arrange(new Rect(0, 0, 1120, 780)); root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout();
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-zhao-yun-ui", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, Hero);
            builder.AddGeneral(new(Peer, "零牌杀合法目标", "", "standard:none", "wei", 8));
            builder.AddDeck(new(Deck, "零牌主动技固定小牌堆", 0, 0, [])
            { PhysicalCards = Enumerable.Range(0, 40).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "势赵云零牌主动技能界面", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
