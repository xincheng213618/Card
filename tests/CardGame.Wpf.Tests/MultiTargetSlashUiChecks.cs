using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class MultiTargetSlashUiChecks
{
    public static void PortraitDraftsUseExactNativeTargets(string output)
    {
        foreach (var scenario in new[]
        {
            new Scenario("classic:tianyi", "standard:slash"),
            new Scenario("boundary:tianyi", "standard:thunder_slash"),
            new Scenario("classic:tianyi", "standard:dodge", "standard:wusheng"),
            new Scenario("classic:lihuo", "standard:fire_slash"),
            new Scenario("classic:lihuo", "standard:slash", "classic:lihuo"),
            new Scenario("standard:longdan", "halberd", "standard:longdan")
        })
        {
            var (prepared, registry) = Create(scenario);
            var store = new MemorySaveStore();
            store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow,
                false, prepared.CreateCheckpoint()));
            using var model = new MainViewModel(false, prepared.Seed, true, store, contentRegistry: registry)
                { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
            var window = new MainWindow(model);
            var root = (FrameworkElement)window.Content;
            try
            {
                // A one-target use remains optional even when the same card can hit more people.
                foreach (var targetCount in new[] { 1, scenario.Material == "halberd" ? 3 : 2 })
                {
                    model.LoadManualGameCommand.Execute(null);
                    Program.Assert(!model.HasSaveError, model.SaveStatus);
                    var game = Program.Engine(model);
                    var action = game.GetHumanLegalActions().First(item => item.Kind == LegalActionKind.Slash &&
                        item.TargetSeats.Count == targetCount && item.ConversionSource?.SkillId == scenario.Conversion);
                    var commands = game.AcceptedCommands.Count;
                    var moves = game.CardMovements.Count;
                    var revision = game.Revision;
                    var views = Views(game);
                    if (scenario.Conversion is { } source)
                        model.ActivateHumanSkillCommand.Execute(model.HumanSkillCards.Single(skill => skill.ContentId == source));
                    Layout(root);
                    Click(Button(root, model.SelectCardCommand, model.Hand.Single(card => card.Id == action.CardId)));
                    Layout(root);
                    Console.WriteLine($"Slash draft {scenario}: multi={model.IsMultiTargetCardSelected}, combinations={model.TargetCombinationChoices.Count}, confirm={model.CanConfirmSelected}, selected={model.SelectedCardText}; native sizes={string.Join(',', game.GetHumanLegalActions().Where(item => item.CardId == action.CardId && item.ConversionSource == action.ConversionSource).Select(item => item.TargetSeats.Count).Distinct())}.");
                    Program.Assert(model.IsMultiTargetCardSelected && !model.HasTargetCombinationChoices &&
                        model.TargetCombinationChoices.Count == 0 && !model.CanConfirmSelected && !model.ShowRecastAction,
                        $"{scenario} must select Slash recipients through portraits without a combination button or early confirmation.");
                    model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == 0));
                    Program.Assert(model.Seats.All(seat => !seat.IsSelectedTarget), "Slash must reject its own source as a recipient.");
                    foreach (var target in action.TargetSeats.Reverse())
                    {
                        Click(Button(root, model.SelectTargetCommand, model.Seats.Single(seat => seat.Seat == target)));
                        Layout(root);
                    }
                    Program.Assert(model.CanConfirmSelected && Selected(model).SetEquals(action.TargetSeats),
                        "Reverse portrait clicks must retain every selected target and enable native confirmation.");
                    if (targetCount > 1)
                    {
                        var removable = action.TargetSeats[0];
                        Click(Button(root, model.SelectTargetCommand, model.Seats.Single(seat => seat.Seat == removable)));
                        Program.Assert(Selected(model).SetEquals(action.TargetSeats.Skip(1)),
                            "Clicking a selected portrait must remove only that target.");
                        Click(Button(root, model.SelectTargetCommand, model.Seats.Single(seat => seat.Seat == removable)));
                        var extra = model.Seats.FirstOrDefault(seat => seat.Seat != 0 && !action.TargetSeats.Contains(seat.Seat));
                        if (extra is not null)
                        {
                            model.SelectTargetCommand.Execute(extra);
                            Program.Assert(Selected(model).SetEquals(action.TargetSeats), "The draft must reject targets beyond its native limit.");
                        }
                    }
                    model.OpenContextGuideCommand.Execute(null);
                    Program.Assert(model.CurrentGuideTitle.Contains("目标", StringComparison.Ordinal) &&
                        model.CurrentGuideSteps.Any(step => step.Text.Contains("再次点选取消", StringComparison.Ordinal)) &&
                        !model.CurrentGuideSteps.Any(step => step.Text.Contains("点击组合", StringComparison.Ordinal)),
                        "The current guide must explain reversible portrait selection for all multi-target Slash sources.");
                    model.ToggleHelpCommand.Execute(null);
                    Program.Assert(game.AcceptedCommands.Count == commands && game.Revision == revision &&
                        game.CardMovements.Count == moves && Views(game).SequenceEqual(views),
                        "Card selection, portrait toggles and help must not submit a command, move a card or alter any viewer's state.");
                    model.ClearSelectionCommand.Execute(null);
                    Program.Assert(!model.HasSelection && Selected(model).Count == 0, "Cancel must clear the complete multi-target draft.");
                    if (scenario.Conversion is { } conversion)
                        model.ActivateHumanSkillCommand.Execute(model.HumanSkillCards.Single(skill => skill.ContentId == conversion));
                    model.SelectCardCommand.Execute(model.Hand.Single(card => card.Id == action.CardId));
                    foreach (var target in action.TargetSeats.Reverse())
                        model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == target));
                    Layout(root);
                    if (scenario.Skill == "classic:tianyi" && scenario.Conversion is not null && targetCount == 2)
                        Program.Render(root, 1440, 860, Path.Combine(output, "tianyi-wusheng-portrait-targets.png"));
                    var expected = new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, revision,
                        game.CreateSnapshot(0).PendingDecision!.PromptId, action.PlayedCardKind, action.TargetCardId)
                    { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources };
                    var reference = GameReplay.Restore(game.CreateCheckpoint(), registry);
                    Accept(reference, expected);
                    if (targetCount == 1) Click((Button)window.FindName("PlayCardButton"));
                    else Program.Assert(TableShortcutChecks.Input(window, Key.Enter), "Enter must confirm the selected multi-target Slash.");
                    Program.Assert(game.Revision == revision + 1 && game.AcceptedCommands.Count == commands + 1 &&
                        CommandJson.Serialize([game.AcceptedCommands.Last()]) == CommandJson.Serialize([expected]) &&
                        game.CardMovements.Skip(moves).Count(move => move.CardId == action.CardId &&
                            move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing) == 1 &&
                        Views(game).SequenceEqual(Views(reference)) && game.CardMovements.SequenceEqual(reference.CardMovements) &&
                        !model.HasSelection && Selected(model).Count == 0,
                        "Confirmation must submit the exact native source and canonical target order once, paying one physical card.");
                    var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
                    Program.Assert(Views(game).SequenceEqual(Views(restored)), "The accepted portrait-selected Slash must cold-replay exactly.");
                    Console.WriteLine($"Slash portraits: {scenario.Skill}, {scenario.Conversion ?? scenario.Material}, {targetCount} target(s), exact native commit and cold replay.");
                }
            }
            finally { window.Content = null; window.Close(); }
        }
    }

    private static (GameEngine, ContentRegistry) Create(Scenario scenario)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(scenario));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = scenario.Material == "halberd" ? 7 : 31,
            PlayerCount = scenario.Material == "halberd" ? 5 : 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Fixture.Mode, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 3 }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, Fixture.Owner, game.Revision, game.CreateSnapshot(0).PendingDecision!.PromptId));
        ReachPlay(game);
        if (scenario.Skill.EndsWith(":tianyi", StringComparison.Ordinal))
        {
            var source = game.CreateSnapshot(0).Players[0].Hand.MaxBy(card => card.Rank)!;
            var opponent = game.CreateSnapshot(1).Players[1].Hand.MinBy(card => card.Rank)!;
            Program.Assert(source.Rank > opponent.Rank, "The fixed native Tianyi fixture must have a winning pair.");
            Accept(game, new UseProgramSkillCommand(0, scenario.Skill, "contest", [source.Id], [1], game.Revision,
                game.CreateSnapshot(0).PendingDecision!.PromptId));
            var prompt = game.CreateSnapshot(1).PendingDecision!;
            var choice = prompt.Choices.Single(item => item.Cards.SequenceEqual([opponent.Id]));
            Accept(game, new AnswerPromptCommand(1, prompt.PromptId, choice.Id, game.Revision));
            ReachPlay(game);
        }
        if (scenario.Material == "halberd")
        {
            var hand = game.CreateSnapshot(0).Players[0].Hand;
            Program.Assert(hand.Count == 2 && hand.Any(card => card.Kind == CardKind.Dodge), "The fixed Fangtian hand must contain the weapon and one Dodge.");
            var weapon = hand.Single(card => card.Kind == CardKind.FangtianHalberd);
            Accept(game, new PlayCardCommand(0, weapon.Id, [], game.Revision, game.CreateSnapshot(0).PendingDecision!.PromptId));
            ReachPlay(game);
        }
        return (game, registry);
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 40 && game.CreateSnapshot(0).PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Program.Assert(game.CreateSnapshot(0).PendingDecision?.Kind == DecisionKind.PlayCard, "The native fixture must reach human play.");
    }
    private static HashSet<int> Selected(MainViewModel model) => model.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).ToHashSet();
    private static string[] Views(GameEngine game) => Enumerable.Range(0, game.CreateSnapshot(0).Players.Count)
        .Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
    private static void Accept(GameEngine game, GameCommand command) { var result = game.Submit(command); Program.Assert(result.Accepted, result.Error?.Message ?? "Native command rejected."); }
    private static Button Button(FrameworkElement root, ICommand command, object value) => Program.Find<Button>(root)
        .Single(button => ReferenceEquals(button.Command, command) && ReferenceEquals(button.CommandParameter, value));
    private static void Click(Button button) { Program.Assert(button.IsEnabled && button.Visibility == Visibility.Visible, "The actual bound target/card/confirm button must be enabled.");
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null); }
    private static void Layout(FrameworkElement root) { root.Measure(new Size(1120, 740)); root.Arrange(new Rect(0, 0, 1120, 740));
        root.UpdateLayout(); root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout(); }

    private sealed record Scenario(string Skill, string Material, string? Conversion = null);
    private sealed class Fixture(Scenario scenario) : IGameContentPackage
    {
        internal const string Mode = "identity:classic-multi-target-slash-ui";
        internal const string Owner = "fixture:multi-target-slash-owner";
        public PackageManifest Manifest { get; } = new("fixture-multi-target-slash-ui", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new(Owner, "多目标杀", "supporter", scenario.Skill, "qun", 6,
                AdditionalSkillIds: scenario.Conversion is "standard:wusheng" ? ["standard:wusheng"] : []));
            var count = scenario.Material == "halberd" ? 5 : 4;
            var peers = Enumerable.Range(1, count - 1).Select(seat => $"fixture:multi-target-slash-peer-{seat}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "合法目标", "supporter", "standard:none", "wei", 6));
            builder.AddDeck(new("fixture:multi-target-slash-deck", "真实杀实体", scenario.Material == "halberd" ? 2 : 8, 0,
                scenario.Material == "halberd" ? [new("classic:fangtian-halberd", 8), new("standard:dodge", 24), new("standard:peach", 28)] : [])
            {
                PhysicalCards = scenario.Material == "halberd" ? [] : Enumerable.Range(0, 80)
                    .Select(index => new ContentDeckPhysicalCard(scenario.Material, Suit.Heart, index % 13 + 1)).ToArray()
            });
            var roles = new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 };
            if (count == 5) roles[nameof(Role.Renegade)] = 1;
            builder.AddMode(new(Mode, "多目标杀输入", count, count, roles, "fixture:multi-target-slash-deck",
                GeneralCandidateCount: count, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
