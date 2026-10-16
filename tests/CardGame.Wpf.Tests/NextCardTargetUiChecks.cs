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

internal static class NextCardTargetUiChecks
{
    public static void PortraitDraftAndPublicCardChoices(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, HumanSeat = 0, HumanRole = Role.Lord,
            PlayerCount = 4, ModeId = "fixture:next-target-ui", UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, "fixture:next-target-owner", game.Revision, game.PendingDecision!.PromptId));
        ReachPlay(game);
        Accept(game, new UseProgramSkillCommand(0, "fixture:next-target-gear", "equip", [], [1], game.Revision, game.PendingDecision!.PromptId));
        ReachPlay(game);
        Program.Assert(game.CreateSnapshot(0).Players[1] is { HandCount: 0, Equipment.Count: 1 },
            "The primary Snatch target must have only public equipment, matching the exact-card regression.");
        var paid = game.CreateSnapshot(0).Players[0].Hand.First().Id;
        Accept(game, new UseProgramSkillCommand(0, "boundary:qiaoshui-current", "contest", [paid], [2], game.Revision, game.PendingDecision!.PromptId));
        ReachPlay(game);
        Program.Assert(game.Events.Any(item => item.Payload is NextActualUseTargetAdjustmentGrantedEvent),
            "A real paid winning contest must grant the next actual-use target adjustment.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, game.Seed, true, store, contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        var window = new MainWindow(model);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        try
        {
            foreach (var kind in new[] { CardKind.Slash, CardKind.Snatch, CardKind.ArrowBarrage, CardKind.DrawTwo })
            {
                model.LoadManualGameCommand.Execute(null);
                game = Program.Engine(model);
                var action = game.GetHumanLegalActions().First(item => item.CardId is > 0 &&
                    game.CreateSnapshot(0).Players[0].Hand.Any(card => card.Id == item.CardId && card.Kind == kind) &&
                    item.TargetSeats.Count == 2 && item.TargetCardId is null &&
                    (kind != CardKind.Snatch || !item.TargetSeats.Contains(1)));
                var card = model.Hand.Single(item => item.Id == action.CardId);
                model.SelectCardCommand.Execute(card);
                Program.Assert(model.IsMultiTargetCardSelected && !model.HasTargetCombinationChoices && !model.HasPublicTargetChoices &&
                    model.ActionHint.Contains(card.Name) && !model.ShowRecastAction,
                    "Adjusted cards must use their own portrait draft without duplicate combination buttons or Iron Chain labels.");
                var revision = game.Revision;
                var commands = game.AcceptedCommands.Count;
                var state = SnapshotJson.Serialize(game.CreateSnapshot(0));
                foreach (var seat in action.TargetSeats.Reverse()) Press(seat);
                Program.Assert(model.CanConfirmSelected && model.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).ToHashSet().SetEquals(action.TargetSeats) &&
                    game.Revision == revision && game.AcceptedCommands.Count == commands && SnapshotJson.Serialize(game.CreateSnapshot(0)) == state,
                    "Reverse portrait clicks must prepare the exact published target set without paying or consuming the grant.");
                if (kind == CardKind.Slash)
                {
                    Press(action.TargetSeats[1]);
                    Program.Assert(model.CanConfirmSelected && model.Seats.Count(seat => seat.IsSelectedTarget) == 1,
                        "Cancelling the extra target must restore the ordinary single-target use.");
                    Press(action.TargetSeats[1]);
                    model.ClearSelectionCommand.Execute(null);
                    Program.Assert(!model.HasSelection && model.Seats.All(seat => !seat.IsSelectedTarget) && game.Revision == revision,
                        "Cancelling the draft must clear all targets without consuming the winning contest.");
                    model.SelectCardCommand.Execute(card);
                    foreach (var seat in action.TargetSeats) Press(seat);
                }
                if (kind == CardKind.ArrowBarrage)
                    Program.Assert(action.TargetSeats.Count == game.PlayerCount - 2,
                        "The group-card draft must also allow one fewer target after the actual win.");
                Program.Render(root, 1440, 860, Path.Combine(output, $"next-target-{kind}.png"));
                Click((Button)window.FindName("PlayCardButton"));
                var command = game.AcceptedCommands.Skip(commands).OfType<PlayCardCommand>().Single();
                Program.Assert(command.CardId == card.Id && command.TargetSeats.SequenceEqual(action.TargetSeats) && command.TargetCardId is null,
                    "Confirmation must submit one original entity and Core's canonical target order.");
            }

            model.LoadManualGameCommand.Execute(null);
            game = Program.Engine(model);
            var snatch = model.Hand.First(card => card.Kind == CardKind.Snatch);
            var equipment = game.CreateSnapshot(0).Players[1].Equipment.Single().Id;
            var otherHand = game.CreateSnapshot(0).Players[3].HandCount;
            model.SelectCardCommand.Execute(snatch);
            Press(1);
            var retired = model.PublicTargetChoices.Single();
            Program.Assert(retired.Targets.SequenceEqual([1]) && !model.CanConfirmSelected,
                "An equipment-only primary target must still offer its exact public card.");
            Press(3);
            Program.Assert(!model.CanConfirmSelected && model.PublicTargetChoices.Count == 1 &&
                model.PublicTargetChoices.Single().Targets.SequenceEqual([1, 3]) &&
                !model.PublicTargetChoices.Any(choice => ReferenceEquals(choice, retired)),
                "Adding a portrait must replace the one-target equipment choice with the exact two-target choice.");
            var before = game.Revision;
            model.SelectPublicTargetChoiceCommand.Execute(retired);
            Program.Assert(game.Revision == before && model.Seats.Count(seat => seat.IsSelectedTarget) == 2,
                "A retired single-target equipment button must not submit or destroy the new multi-target draft.");
            var exact = model.PublicTargetChoices.Single();
            var count = game.AcceptedCommands.Count;
            Program.Render(root, 1440, 860, Path.Combine(output, "next-target-Snatch-public-equipment.png"));
            var button = Program.Find<Button>(root).Single(item => ReferenceEquals(item.Command, model.SelectPublicTargetChoiceCommand) && ReferenceEquals(item.CommandParameter, exact));
            Click(button);
            var submitted = game.AcceptedCommands.Skip(count).OfType<PlayCardCommand>().Single();
            Program.Assert(submitted.TargetSeats.SequenceEqual([1, 3]) && submitted.TargetCardId == equipment,
                "The public equipment button must preserve both portraits and the exact original equipment entity.");
            for (var step = 0; step < 80 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            {
                if (game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.SelectTargetCard } prompt)
                {
                    Program.Assert(prompt.TargetSeat == 3 && prompt.Choices.All(choice => choice.Cards.Count == 0),
                        "The extra target's hand selection must retain the opaque private slot contract.");
                    model.SelectTargetCardChoiceCommand.Execute(model.TargetCardChoices.First());
                }
                else model.StepAiCommand.Execute(null);
            }
            Program.Assert(game.PendingDecision?.Kind == DecisionKind.PlayCard &&
                game.CreateSnapshot(0).Players[0].Hand.Any(card => card.Id == equipment) &&
                game.CreateSnapshot(0).Players[3].HandCount == otherHand - 1 &&
                game.Events.Count(item => item.Payload is NextActualUseTargetAdjustmentConsumedEvent) == 1 &&
                game.CardMovements.Count(move => move.CardId == snatch.Id && move.Reason == CardMoveReasons.Use) == 1,
                "One Snatch must obtain the selected equipment and one extra hand card, paying and consuming the grant exactly once.");
        }
        finally { window.Content = null; window.Close(); }

        void Press(int target)
        {
            root.Measure(new Size(1440, 860));
            root.Arrange(new Rect(0, 0, 1440, 860));
            root.UpdateLayout();
            var seat = model.Seats.Single(item => item.Seat == target);
            var button = Program.Find<Button>(root).Single(item => ReferenceEquals(item.Command, model.SelectTargetCommand) && ReferenceEquals(item.CommandParameter, seat));
            var press = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent };
            button.RaiseEvent(press);
            Program.Assert(press.Handled, "The actual portrait press must reach the target-selection route.");
            button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            root.UpdateLayout();
        }
    }

    private static void Click(Button button) => typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "Next target UI fixture rejected a command.");
    }
    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 80; step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Next target UI fixture did not reach human play.");
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-next-target-ui", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "classic:jian-yong", "boundary:jian-yong", "classic:qin-mi");
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"fixture:next-target-gear","revision":1,"activations":[{"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]}]},
                {"id":"fixture:next-target-peer","revision":1,"triggers":[{"id":"empty-hand","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["crossbow"],"optional":false,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:next-target-gear":{"name":"固定装备","description":"界面回归夹具"},"fixture:next-target-peer":{"name":"清空手牌","description":"只保留公开装备"}}}""");
            foreach (var id in catalog.Programs.Keys) builder.AddSkill(new(id, id, "真实实体界面夹具") { Program = catalog.Programs[id] });
            builder.AddGeneral(new("fixture:next-target-owner", "界简雍目标夹具", "supporter", "boundary:qiaoshui-current", "shu", 12,
                ["classic:tianbian", "fixture:next-target-gear"]));
            var peers = Enumerable.Range(1, 3).Select(seat => $"fixture:next-target-peer-{seat}").ToArray();
            foreach (var id in peers) builder.AddGeneral(new(id, id, "supporter", "fixture:next-target-peer", "shu", 12));
            string[] cards = ["standard:snatch", "standard:slash", "standard:arrow_barrage", "standard:draw_two", "standard:crossbow"];
            builder.AddDeck(new("fixture:next-target-deck", "目标交互实体牌", 12, 0, []) {
                PhysicalCards = Enumerable.Range(0, 160).Select(index => new ContentDeckPhysicalCard(cards[index % cards.Length], Suit.Heart, 7)).ToArray() });
            builder.AddMode(new("fixture:next-target-ui", "实际巧说目标交互", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:next-target-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:next-target-owner", .. peers]));
        }
    }
}
