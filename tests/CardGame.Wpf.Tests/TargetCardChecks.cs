using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Content.Standard;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class TargetCardChecks
{
    public static void Controls(string output)
    {
        var (viewModel, action) = FindFixture();
        using (viewModel)
        {
            var engine = Program.Engine(viewModel);
            var targetSeat = action.TargetSeat ??
                throw new InvalidOperationException("Target-card fixture lost its target seat.");
            var targetBefore = engine.CreateSnapshot(targetSeat, revealAll: true)
                .Players.Single(player => player.Seat == targetSeat);

            Program.Assert(engine.Submit(new PlayCardCommand(engine.State.HumanSeat, action.CardId!.Value,
                action.TargetSeats, engine.Revision, engine.PendingDecision!.PromptId)).Accepted,
                "The legacy native target-card prompt could not be opened.");
            Program.AdvanceToDecision(viewModel);

            Program.Assert(viewModel.IsTargetCardSelectionPending, "WPF did not expose the private target-card prompt.");
            var prompt = engine.PendingDecision ??
                throw new InvalidOperationException("Target-card fixture has no pending prompt.");
            Program.Assert(prompt.Kind == DecisionKind.SelectTargetCard, "Wrong WPF target-card prompt kind.");
            Program.Assert(prompt.IsPrivate && prompt.Choices.Count == targetBefore.HandCount,
                "Target-card prompt did not preserve its private count-only contract.");
            Program.Assert(viewModel.TargetCardChoices.Count == prompt.Choices.Count,
                "WPF target-card collection does not mirror the published choices.");
            Program.Assert(viewModel.TargetCardChoices.All(choice =>
                    choice.Cards.Count == 0 &&
                    choice.Targets.SequenceEqual([targetSeat]) &&
                    choice.Parameters.GetValueOrDefault("action") == "target-card-slot" &&
                    choice.Parameters.ContainsKey("slot-index") &&
                    !choice.Parameters.ContainsKey("card-id") &&
                    choice.Description.Contains("牌位", StringComparison.Ordinal)),
                "WPF exposed a target-card identity instead of an opaque slot.");

            var observerSeat = Enumerable.Range(0, engine.PlayerCount)
                .First(seat => seat != engine.State.HumanSeat);
            Program.Assert(engine.CreateSnapshot(observerSeat).PendingDecision is null,
                "An ordinary viewer received the source player's private target-card prompt.");

            var window = new MainWindow(viewModel);
            window.ApplyTemplate();
            var root = (FrameworkElement)window.Content;
            Program.Render(root, 1120, 740, Path.Combine(output, "target-card-selection.png"));
            var buttons = Program.Find<Button>(root)
                .Where(button => button.Command == viewModel.SelectTargetCardChoiceCommand &&
                                 button.Visibility == Visibility.Visible &&
                                 button.IsEnabled &&
                                 button.ActualWidth > 0 &&
                                 button.ActualHeight > 0)
                .ToArray();
            Program.Assert(buttons.Length == prompt.Choices.Count,
                "Every opaque target-card slot must be reachable as a visible button.");
            Program.Assert(buttons.All(button => Program.Find<ContentControl>(button)
                    .Any(content => content.Content is PromptCardChoicePresentation { IsCard: true, IsHidden: true, Face: null })),
                "Opaque target choices must display card backs without a card face or entity ID.");
            var hiddenChoice = prompt.Choices[0];
            foreach (var parameters in new[]
            {
                new Dictionary<string, string> { ["program-action"] = "choose-other-owned-card-discard", ["source-zone"] = "Hand", ["slot-index"] = "0" },
                new Dictionary<string, string> { ["program-action"] = "zhanyi-punish-card", ["hand-slot"] = "0" },
                new Dictionary<string, string> { ["program-action"] = "participant-discard", ["zone"] = "Hand", ["slot"] = "0" }
            })
                Program.Assert(PromptCardChoiceConverter.Present(hiddenChoice with { Parameters = parameters },
                        engine.CreateSnapshot(engine.State.HumanSeat, revealAll: true)) is { IsHidden: true, Face: null },
                    "Skill-owned opaque choices must stay card backs, including in a developer view.");
            var faceChoice = hiddenChoice with { Cards = new[] { targetBefore.Hand[0].Id } };
            Program.Assert(PromptCardChoiceConverter.Present(faceChoice, engine.CreateSnapshot(engine.State.HumanSeat)).Face is null &&
                           PromptCardChoiceConverter.Present(faceChoice, engine.CreateSnapshot(targetSeat)).Face?.Id == targetBefore.Hand[0].Id,
                "The shared card renderer must use only faces authorized in its supplied viewer snapshot.");

            var revision = engine.Revision;
            viewModel.SelectTargetCardChoiceCommand.Execute(viewModel.TargetCardChoices.Last());
            Program.Assert(engine.Revision > revision && !viewModel.IsTargetCardSelectionPending,
                "Selecting an opaque target-card slot did not commit through the command boundary.");
            Program.Assert(engine.State.ProcessingCardCount == 0 && engine.ResolutionStack.Count == 0,
                "Target-card selection left an in-flight resolution behind.");
            Program.Assert(engine.Events.Select(item => item.Payload).Any(payload =>
                    payload is TargetCardDiscardedEvent or TargetCardTakenEvent),
                "Target-card selection did not publish its typed result.");
            Program.Assert(!viewModel.PromptText.Contains("未执行", StringComparison.Ordinal), viewModel.PromptText);
            Program.Render(root, 1120, 740, Path.Combine(output, "target-card-finished.png"));
            window.Content = null;
            window.Close();
        }
        RegionChoices(output);
        SkillRegionChoices(output);
        NativeEquipmentRegionChoices(output);
        NativeEquipmentRegionChoices(output, equipmentOnly: true);
    }

    public static void OnlyHandCard(string output)
    {
        foreach (var effect in new[] { "standard:dismantlement", "standard:snatch" })
        foreach (var handCount in new[] { 1, 2 })
        {
            var (game, registry) = StalePromptChoiceChecks.CreatePublicTargetFixture(effect, "standard:bagua");
            while (game.CreateSnapshot(0).Players[1].HandCount > handCount)
            {
                Accept(new UseProgramSkillCommand(0, "fixture:published-play-choice-driver", "strip", [], [1],
                    game.Revision, game.PendingDecision!.PromptId));
                var choice = game.PendingDecision!.Choices.First(candidate => candidate.Cards.Count == 0);
                Accept(new AnswerPromptCommand(0, game.PendingDecision.PromptId, choice.Id, game.Revision));
                ReachPlay();
            }
            if (handCount == 2)
            {
                var armor = game.CreateSnapshot(0).Players[1].Equipment.Single().Id;
                Accept(new UseProgramSkillCommand(0, "fixture:published-play-choice-driver", "strip", [], [1],
                    game.Revision, game.PendingDecision!.PromptId));
                var choice = game.PendingDecision!.Choices.Single(candidate => candidate.Cards.SequenceEqual([armor]));
                Accept(new AnswerPromptCommand(0, game.PendingDecision.PromptId, choice.Id, game.Revision));
                ReachPlay();
            }
            var handCard = game.CreateSnapshot(0, revealAll: true).Players[1].Hand.Last();
            var equipmentIds = game.CreateSnapshot(0).Players[1].Equipment.Select(card => card.Id).ToArray();
            var store = new MemorySaveStore();
            store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
            using var model = new MainViewModel(false, game.Seed, true, store, contentRegistry: registry)
                { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
            model.LoadManualGameCommand.Execute(null);
            game = Program.Engine(model);
            var commandsBefore = game.AcceptedCommands.Count;
            var oldChoice = ChooseHand();
            Pump();
            Program.AdvanceToDecision(model);
            var newCommands = game.AcceptedCommands.Skip(commandsBefore).ToArray();
            Program.Assert(newCommands.Count(command => command is PlayCardCommand) == 1 &&
                newCommands.All(command => command is not AnswerPromptCommand) &&
                !model.IsTargetCardSelectionPending && !model.HasTargetCardPicker && !model.HasSelection &&
                game.CreateSnapshot(0).Players[1].HandCount == handCount - 1 &&
                game.CreateSnapshot(0).Players[1].Equipment.Select(card => card.Id).SequenceEqual(equipmentIds) &&
                game.CardMovements.Count(move => move.CardId == handCard.Id && move.From == CardLocation.Hand(1)) == 1 &&
                (effect == "standard:snatch"
                    ? game.CreateSnapshot(0).Players[0].Hand.Any(card => card.Id == handCard.Id)
                    : game.CardMovements.Any(move => move.CardId == handCard.Id && move.To == CardLocation.DiscardPile)),
                "Clicking any concrete hand back must finish one native use without an answer or second picker, preserving equipment.");
            var completedRevision = game.Revision;
            model.SelectPublicTargetChoiceCommand.Execute(oldChoice);
            Pump();
            Program.Assert(game.Revision == completedRevision,
                "A retired sole-card callback or choice must not repeat a completed target effect.");
            var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Program.Assert(SnapshotJson.Serialize(game.CreateSnapshot(0)) == SnapshotJson.Serialize(restored.CreateSnapshot(0)),
                "The chosen opaque slot must remain part of the replayable Play command.");
            var window = new MainWindow(model);
            try { Program.Render((FrameworkElement)window.Content, 1120, 740, Path.Combine(output, $"target-direct-hand-finished-{effect.Split(':')[1]}-{handCount}.png")); }
            finally { window.Content = null; window.Close(); }

            PromptChoice ChooseHand()
            {
                var action = game.GetHumanLegalActions().First(candidate => candidate.TargetSeat == 1 && candidate.TargetCardId is null &&
                    candidate.Kind is LegalActionKind.Dismantlement or LegalActionKind.Snatch);
                model.SelectCardCommand.Execute(model.Hand.Single(card => card.Id == action.CardId));
                model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == 1));
                var choices = model.TargetCardRegions.Single(region => region.Zone == CardZoneKind.Hand).Choices;
                Program.Assert(choices.Count == handCount && choices.All(choice => choice.Parameters.ContainsKey("target-hand-slot")),
                    "The hand region must expose one opaque selection per current hand card.");
                Program.Assert(model.TargetCardRegions.Select(region => region.Zone).SequenceEqual(equipmentIds.Length == 0
                    ? [CardZoneKind.Hand] : new[] { CardZoneKind.Hand, CardZoneKind.Equipment }),
                    "Empty equipment and judgment regions must be absent from the same concrete-card picker.");
                var selected = choices.Last();
                model.SelectPublicTargetChoiceCommand.Execute(selected);
                Program.AdvanceToDecision(model);
                Program.Assert(!model.IsTargetCardSelectionPending,
                    "A concrete hand selection must not reopen the native picker.");
                return selected;
            }
            void Accept(GameCommand command)
            {
                var result = game.Submit(command);
                Program.Assert(result.Accepted, result.Error?.Message ?? "The sole-hand fixture command failed.");
            }
            void ReachPlay()
            {
                for (var step = 0; step < 48 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
                    Accept(new AdvanceOneStepCommand(game.Revision));
                Program.Assert(game.PendingDecision?.Kind == DecisionKind.PlayCard, "The sole-hand fixture lost its Play boundary.");
            }
        }
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    public static void MultiTakeConfirmsOnce(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new MultiTakeFixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = "fixture:multi-take", UseInteractiveSetup = true, AdvanceAfterHumanCommands = false }, registry);
        Program.Assert(game.Submit(new StartGameCommand()).Accepted, "Could not start the multi-take fixture.");
        Program.Assert(game.Submit(new SelectGeneralCommand(0, "fixture:multi-take-owner", game.Revision, game.PendingDecision!.PromptId)).Accepted,
            "Could not select the multi-take owner.");
        for (var step = 0; step < 48 && game.PendingDecision?.SkillPrompt?.SkillId != "fixture:multi-take-driver"; step++)
            Program.Assert(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "Could not reach the actual draw-end take window.");
        var targetChoice = game.PendingDecision!.Choices.Single(choice => choice.Targets.SequenceEqual([1]));
        Program.Assert(game.Submit(new AnswerPromptCommand(0, game.PendingDecision.PromptId, targetChoice.Id, game.Revision)).Accepted,
            "Could not select the private hand owner.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, game.Seed, true, store, contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        game = Program.Engine(model);
        var targetBefore = game.CreateSnapshot(1).Players[1].HandCount;
        var sourceBefore = game.CreateSnapshot(0).Players[0].HandCount;
        var revision = game.Revision;
        var choices = model.TargetCardRegions.Single().Choices.Take(2).ToArray();
        Program.Assert(model.IsForeignCardTakeSelectionPending && choices.Length == 2 && !model.CanConfirmSelected &&
            model.TargetCardRegions.Single().Zone == CardZoneKind.Hand, "A multi-take must show only its hand region and wait for selection.");
        model.SelectSkillChoiceCommand.Execute(choices[0]);
        Program.Assert(game.Revision == revision && !model.CanConfirmSelected, "Selecting the first take card must not move it or confirm an incomplete set.");
        model.SelectSkillChoiceCommand.Execute(choices[1]);
        Program.Assert(game.Revision == revision && model.CanConfirmSelected && model.SelectedTargetCardIds.Count == 2,
            "Selecting the full take set must still wait for one explicit confirmation.");
        model.ClearSelectionCommand.Execute(null);
        Program.Assert(model.SelectedTargetCardIds.Count == 0 && !model.CanConfirmSelected && game.Revision == revision,
            "Clearing a multi-take must retire the local draft without taking any card.");
        model.SelectSkillChoiceCommand.Execute(choices[0]);
        model.SelectSkillChoiceCommand.Execute(choices[1]);
        var window = new MainWindow(model);
        try { Program.Render((FrameworkElement)window.Content, 1120, 740, Path.Combine(output, "target-multi-take-selected.png")); }
        finally { window.Content = null; window.Close(); }
        model.ConfirmSelectedCommand.Execute(null);
        Program.AdvanceToDecision(model);
        Program.Assert(game.CreateSnapshot(1).Players[1].HandCount == targetBefore - 2 &&
            game.CreateSnapshot(0).Players[0].HandCount == sourceBefore + 2 && !model.HasTargetCardPicker &&
            !model.IsForeignCardTakeSelectionPending, "One confirmation must obtain both selected cards and close the picker.");
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Program.Assert(SnapshotJson.Serialize(game.CreateSnapshot(0)) == SnapshotJson.Serialize(restored.CreateSnapshot(0)),
            "The shared multi-take confirmation must replay through the original selection commands.");
    }

    private sealed class MultiTakeFixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-multi-take", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:multi-take-driver","revision":1,"triggers":[
                  {"id":"take-two","window":"drawPhaseEnded","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[
                    {"op":"selectTarget","target":"owner","targetKind":"otherLiving"},
                    {"op":"viewAndTakeSelectedTargetHand","target":"selectedTarget","amount":2}]}]}]}
                """, System.Text.Json.JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new Dictionary<string, object> { ["fixture:multi-take-driver"] = new { name = "取两张牌", description = "实际结束摸牌后的观看与取得。" } } }));
            builder.AddSkill(new("fixture:multi-take-driver", "取两张牌", "实际取得两张目标手牌") { Program = catalog.Programs["fixture:multi-take-driver"] });
            builder.AddGeneral(new("fixture:multi-take-owner", "取牌者", "supporter", "fixture:multi-take-driver", "wei", 4));
            var peers = Enumerable.Range(1, 3).Select(seat => $"fixture:multi-take-peer-{seat}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, peer, "supporter", "standard:none", "wei", 4));
            builder.AddDeck(new("fixture:multi-take-deck", "取牌测试", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 56)
                .Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 7)).ToArray() });
            builder.AddMode(new("fixture:multi-take", "取牌测试", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:multi-take-deck",
                GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:multi-take-owner", .. peers]));
        }
    }

    private static void RegionChoices(string output)
    {
        foreach (var effect in new[] { "standard:dismantlement", "standard:snatch" })
        foreach (var chooseHand in new[] { false, true })
        {
            var (game, registry) = StalePromptChoiceChecks.CreatePublicTargetFixture(effect, "standard:bagua");
            var armor = game.CreateSnapshot(0).Players[1].Equipment.Single();
            var action = game.GetHumanLegalActions().First(candidate => candidate.TargetSeat == 1 &&
                candidate.TargetCardId == armor.Id);
            var store = new MemorySaveStore();
            store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
            using var model = new MainViewModel(false, game.Seed, true, store, contentRegistry: registry) { IsMotionEnabled = false };
            model.LoadManualGameCommand.Execute(null);
            var engine = Program.Engine(model);
            model.SelectCardCommand.Execute(model.Hand.Single(card => card.Id == action.CardId));
            var window = new MainWindow(model);
            try
            {
                var root = (FrameworkElement)window.Content;
                var targetSeat = model.Seats.Single(seat => seat.Seat == 1);
                Program.Render(root, 1120, 740, Path.Combine(output, $"target-region-unselected-{action.Kind}-{chooseHand}.png"));
                var panel = (Border)window.FindName("DecisionPanel");
                Program.Assert(model.PublicTargetChoices.Count == 0 && !model.HasPublicTargetChoices &&
                    panel.Visibility == Visibility.Collapsed && !model.CanConfirmSelected && targetSeat.IsLegalTarget,
                    "Selecting a trick card must highlight legal generals without showing their region choices before a target is selected.");
                model.SelectTargetCommand.Execute(targetSeat);
                var retired = model.PublicTargetChoices.First();
                model.SelectTargetCommand.Execute(targetSeat);
                Program.Render(root, 1120, 740, Path.Combine(output, $"target-region-deselected-{action.Kind}-{chooseHand}.png"));
                var revision = engine.Revision;
                model.SelectPublicTargetChoiceCommand.Execute(retired);
                Program.Assert(model.PublicTargetChoices.Count == 0 && !model.HasPublicTargetChoices &&
                    panel.Visibility == Visibility.Collapsed && engine.Revision == revision,
                    "Deselecting the target must close its region choices and retire their commands.");
                model.SelectTargetCommand.Execute(targetSeat);
                var handChoice = model.PublicTargetChoices.Last(choice => !choice.Parameters.ContainsKey("target-card-id"));
                var armorChoice = model.PublicTargetChoices.Single(choice => choice.Parameters.GetValueOrDefault("target-card-id") == armor.Id.ToString());
                Program.Assert(model.PublicTargetChoices.All(choice => choice.Targets.SequenceEqual([targetSeat.Seat])) &&
                    !model.CanConfirmSelected && !model.CanPlaySelected && !model.CanPlaySelectedAsSlash,
                    "Only the selected general's region choices may be shown, and they must require an explicit choice.");
                model.ConfirmSelectedCommand.Execute(null);
                model.PlaySelectedCardCommand.Execute(null);
                model.SelectPublicTargetChoiceCommand.Execute(handChoice with { });
                Program.Assert(engine.Revision == revision && model.HasSelection,
                    "Confirm, direct play or a copied choice silently committed the hidden-hand branch.");
                Program.Render(root, 1120, 740, Path.Combine(output, $"target-region-{action.Kind}-{chooseHand}.png"));
                var buttons = Program.Find<Button>(root).Where(button => button.Command == model.SelectPublicTargetChoiceCommand &&
                    button.Visibility == Visibility.Visible && button.IsEnabled && button.ActualHeight > 0).ToArray();
                Program.Assert(buttons.Any(button => ReferenceEquals(button.CommandParameter, armorChoice) &&
                        Program.Find<ContentControl>(button).Any(content => content.Content is
                            CardViewModel face && face.Id == armor.Id && content.Visibility == Visibility.Visible && content.ActualHeight > 0)) &&
                    buttons.Any(button => ReferenceEquals(button.CommandParameter, handChoice) &&
                        Program.Find<ContentControl>(button).Any(content => content.Content is
                            PromptCardChoicePresentation { IsCard: true, IsHidden: true, Face: null })),
                    "The equipment region must show its actual card face while the hand entry remains a card back.");
                var picker = Program.Find<TargetCardPicker>(root).Single();
                var viewport = (ScrollViewer)window.FindName("ResponseViewport");
                var equipmentButton = buttons.Single(button => ReferenceEquals(button.CommandParameter, armorChoice));
                Program.Assert(equipmentButton.TransformToAncestor(viewport).Transform(new Point(0, equipmentButton.ActualHeight)).Y <= viewport.ActualHeight,
                    "The actual equipment face must fit in the visible two-region picker at the minimum table size.");
                Program.Assert(((FrameworkElement)window.FindName("SupplementalActionBar")).Visibility == Visibility.Collapsed,
                    "Supplemental actions must not overlap the card picker.");
                Program.Assert(model.TargetCardRegions.Select(region => region.Zone).SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) &&
                    model.TargetCardRegions.Single(region => region.Zone == CardZoneKind.Equipment).Choices.SequenceEqual([armorChoice]) &&
                    ((ItemsControl)picker.FindName("TargetRegions")).Items.Count == 2 &&
                    TargetRegionCardChoiceConverter.Present(handChoice, engine.CreateSnapshot(0, revealAll: true)) is
                        { IsHidden: true, Face: null } &&
                    TargetRegionCardChoiceConverter.Present(armorChoice with
                        { Parameters = new Dictionary<string, string> { ["target-card-id"] = action.CardId.ToString()! } },
                        engine.CreateSnapshot(0)).Face is null,
                    "Region grouping must preserve the published choices and never render the played card or a foreign hand face as equipment.");
                var handBefore = engine.CreateSnapshot(0).Players[1].HandCount;
                model.SelectPublicTargetChoiceCommand.Execute(chooseHand ? handChoice : armorChoice);
                Program.AdvanceToDecision(model);
                Program.Assert(engine.AcceptedCommands.Last(command => command is PlayCardCommand) is PlayCardCommand paid &&
                    paid.CardId == action.CardId && paid.TargetSeats.SequenceEqual([1]) &&
                    paid.TargetCardId == (chooseHand ? (int?)null : armor.Id), "The region choice submitted a different physical target.");
                Program.Assert(!model.IsTargetCardSelectionPending &&
                    engine.AcceptedCommands.Last(command => command is PlayCardCommand) is PlayCardCommand selectedPlay &&
                    selectedPlay.TargetHandSlot == (chooseHand ? handBefore - 1 : (int?)null),
                    "A direct region selection must carry its opaque slot in the Play command and never reopen a picker.");
                var target = engine.CreateSnapshot(0).Players[1];
                Program.Assert(target.HandCount == handBefore - (chooseHand ? 1 : 0) &&
                    target.Equipment.Any(card => card.Id == armor.Id) == chooseHand && engine.State.ProcessingCardCount == 0,
                    "The chosen region did not resolve exactly once while preserving the other region.");
                var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(engine.CreateCheckpoint())), registry);
                Program.Assert(SnapshotJson.Serialize(engine.CreateSnapshot(0)) == SnapshotJson.Serialize(restored.CreateSnapshot(0)),
                    "The explicit hand/armor choice must survive native replay.");
            }
            finally { window.Content = null; window.Close(); }
        }
    }

    private static void SkillRegionChoices(string output)
    {
        var (game, registry) = StalePromptChoiceChecks.CreatePublicTargetFixture("standard:supply_shortage", "standard:bagua");
        var delay = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.SupplyShortage && action.TargetSeat == 1);
        Program.Assert(game.Submit(new PlayCardCommand(0, delay.CardId!.Value, [1], game.Revision,
            game.PendingDecision!.PromptId)).Accepted, "Could not place a real judgment card for the region fixture.");
        for (var step = 0; step < 48 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Program.Assert(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "Could not resume the real Play boundary.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, game.Seed, true, store, contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        game = Program.Engine(model);
        var window = new MainWindow(model);
        try
        {
            var root = (FrameworkElement)window.Content;
            foreach (var zone in new[] { CardZoneKind.Equipment, CardZoneKind.Judgment, CardZoneKind.Hand })
            {
                var target = game.CreateSnapshot(0).Players[1];
                var expectedZones = new List<CardZoneKind> { CardZoneKind.Hand };
                if (target.Equipment.Count > 0) expectedZones.Add(CardZoneKind.Equipment);
                if (target.Judgment.Count > 0) expectedZones.Add(CardZoneKind.Judgment);
                var take = zone == CardZoneKind.Judgment;
                Program.Assert(game.Submit(new UseProgramSkillCommand(0, "fixture:published-play-choice-driver",
                    take ? "take" : "strip", [], [1], game.Revision, game.PendingDecision!.PromptId)).Accepted,
                    "Could not open the native skill-owned card picker.");
                model.IsDeveloperView = !model.IsDeveloperView;
                Program.Render(root, 1440, 920, Path.Combine(output, $"target-skill-regions-{zone}.png"));
                Program.Assert(model.TargetCardRegions.Select(region => region.Zone).SequenceEqual(expectedZones) &&
                    model.TargetCardRegions.All(region => !region.IsPublicPlay && region.Choices.Count > 0 &&
                        region.Command == model.SelectSkillChoiceCommand) && model.UngroupedCenterSkillChoices.Count == 0,
                    "Skills must share the region picker, and an empty equipment or judgment zone must leave no row.");
                var picker = Program.Find<TargetCardPicker>(root).Single();
                var choice = model.TargetCardRegions.Single(region => region.Zone == zone).Choices[0];
                var button = Program.Find<Button>(picker).Single(button => ReferenceEquals(button.CommandParameter, choice));
                Program.Assert(button.IsEnabled && button.ActualHeight > 0 && button.Command == model.SelectSkillChoiceCommand,
                    "The grouped skill card must keep its original enabled published command.");
                if (zone == CardZoneKind.Hand)
                    Program.Assert(Program.Find<ContentControl>(button).Any(content => content.Content is
                        PromptCardChoicePresentation { IsHidden: true, Face: null }) && !model.IsSkillTargetSelectionPending,
                        "Opaque skill cards must remain backs and must not become portrait target drafts.");
                else
                    Program.Assert(Program.Find<ContentControl>(button).Any(content => content.Content is
                        CardViewModel face && face.Id == choice.Cards[0] && content.ActualHeight > 0),
                        "A skill's equipment and judgment choices must show the actual authorized card face.");
                var revision = game.Revision;
                button.Command!.Execute(button.CommandParameter);
                Program.Assert(game.Revision == revision + 1 && game.AcceptedCommands.Last() is AnswerPromptCommand answer &&
                    answer.Choice == choice.Id, "The grouped native skill choice must commit exactly once.");
                Program.AdvanceToDecision(model);
                var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
                Program.Assert(SnapshotJson.Serialize(game.CreateSnapshot(0)) == SnapshotJson.Serialize(restored.CreateSnapshot(0)),
                    "Grouped skill card selection must retain the original replay boundary.");
            }
            // A single opaque slot must remain a card choice, not a portrait-selection prompt.
            for (var step = 0; step < 4 && game.CreateSnapshot(0).Players[1].HandCount > 0; step++)
            {
                Program.Assert(game.Submit(new UseProgramSkillCommand(0, "fixture:published-play-choice-driver",
                    "strip", [], [1], game.Revision, game.PendingDecision!.PromptId)).Accepted,
                    "Could not reopen the native hand-only picker.");
                model.IsDeveloperView = !model.IsDeveloperView;
                Program.Assert(model.TargetCardRegions is [{ Zone: CardZoneKind.Hand } hand] &&
                    hand.Choices.Count == game.CreateSnapshot(0).Players[1].HandCount && !model.IsSkillTargetSelectionPending,
                    "A hand-only picker must keep every opaque slot, including the last single slot, without empty rows.");
                model.SelectSkillChoiceCommand.Execute(model.TargetCardRegions.Single().Choices[0]);
                Program.AdvanceToDecision(model);
            }
            Program.Assert(game.CreateSnapshot(0).Players[1].HandCount == 0 && !model.HasTargetCardPicker &&
                model.TargetCardRegions.Count == 0, "Completing the last choice must remove the whole empty picker.");
        }
        finally { window.Content = null; window.Close(); }
    }

    private static void NativeEquipmentRegionChoices(string output, bool equipmentOnly = false)
    {
        var (game, registry) = StalePromptChoiceChecks.CreatePublicTargetFixture("standard:slash", "classic:ice-sword", true,
            new StandardActiveSkillExpansionPackage(includeJijiu: true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage());
        void Play(LegalAction action) => Program.Assert(game.Submit(new PlayCardCommand(0, action.CardId!.Value,
            action.TargetSeats, game.Revision, game.PendingDecision!.PromptId)).Accepted, "Could not submit the real equipment fixture play.");
        void Reach(DecisionKind kind)
        {
            for (var step = 0; step < 48 && game.PendingDecision?.Kind != kind; step++)
                Program.Assert(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "Could not advance the native equipment fixture.");
            Program.Assert(game.PendingDecision?.Kind == kind, $"The native equipment fixture did not reach {kind}.");
        }
        if (equipmentOnly)
        {
            while (game.CreateSnapshot(0).Players[1].HandCount > 0)
            {
                Program.Assert(game.Submit(new UseProgramSkillCommand(0, "fixture:published-play-choice-driver",
                    "strip", [], [1], game.Revision, game.PendingDecision!.PromptId)).Accepted,
                    "Could not empty the Ice Sword target's hand through the fixture's native discard skill.");
                var handChoice = game.PendingDecision!.Choices.First(choice => choice.Cards.Count == 0);
                Program.Assert(game.Submit(new AnswerPromptCommand(0, game.PendingDecision.PromptId,
                    handChoice.Id, game.Revision)).Accepted, "Could not discard the fixture target's hand card.");
                Reach(DecisionKind.PlayCard);
            }
        }
        var stage = equipmentOnly ? "equipment-only" : "mixed";
        var weapon = game.CreateSnapshot(0).Players[0].Hand.First(card => card.Kind == CardKind.IceSword);
        Play(game.GetHumanLegalActions().First(action => action.CardId == weapon.Id));
        Reach(DecisionKind.PlayCard);
        Play(game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Slash && action.TargetSeat == 1));
        Reach(DecisionKind.IceSword);
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, game.Seed, true, store, contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        game = Program.Engine(model);
        var window = new MainWindow(model);
        try
        {
            var root = (FrameworkElement)window.Content;
            Program.Render(root, 1440, 920, Path.Combine(output, $"target-native-equipment-{stage}-activation.png"));
            var activation = model.DockSkillChoices.Single(choice => choice.Parameters.GetValueOrDefault("action") == "ice-sword-activate");
            var decline = model.DockSkillChoices.Single(choice => choice.Parameters.GetValueOrDefault("action") == "ice-sword-damage");
            var dock = (ItemsControl)window.FindName("DockSkillChoices");
            var activationButton = Program.Find<Button>(dock).Single(button => ReferenceEquals(button.CommandParameter, activation));
            Program.Assert(model.HasDockSkillChoices && model.DockSkillChoices.Count == 2 && !model.HasCenterChoices &&
                !model.HasTargetCardPicker && model.TargetCardRegions.Count == 0 &&
                ((FrameworkElement)window.FindName("DecisionPanel")).Visibility == Visibility.Collapsed &&
                activationButton.IsEnabled && activationButton.ActualHeight > 0 &&
                Program.Find<Button>(dock).Any(button => ReferenceEquals(button.CommandParameter, decline) && button.IsEnabled),
                "Ice Sword must first show activation and decline in the lower action dock, with no central card picker.");
            var activationRevision = game.Revision;
            var movementCount = game.CardMovements.Count;
            typeof(System.Windows.Controls.Primitives.ButtonBase).GetMethod("OnClick",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(activationButton, null);
            Program.Assert(game.Revision == activationRevision + 1 && game.CardMovements.Count == movementCount &&
                game.AcceptedCommands.Last() is AnswerPromptCommand activationAnswer && activationAnswer.Choice == activation.Id &&
                !model.HasDockSkillChoices && model.HasCenterChoices && model.HasTargetCardPicker,
                "The actual activation button must open the first-card picker once, before any card is discarded.");
            CardZoneKind[] expectedRegions = equipmentOnly ? [CardZoneKind.Equipment] : [CardZoneKind.Hand, CardZoneKind.Equipment];
            foreach (var zone in expectedRegions)
            {
                Program.Render(root, 1440, 920, Path.Combine(output, $"target-native-equipment-{stage}-{zone}.png"));
                Program.Assert(model.ChoiceSnapshot.PendingDecision is { Kind: DecisionKind.IceSword, SkillPrompt: null } &&
                    model.TargetCardRegions.Select(region => region.Zone).SequenceEqual(expectedRegions) &&
                    model.UngroupedCenterSkillChoices.Count == 0 && !model.HasDockSkillChoices,
                    "An activated weapon must show only its mandatory card regions, without activation or decline buttons.");
                var choice = model.TargetCardRegions.Single(region => region.Zone == zone).Choices[0];
                var button = Program.Find<Button>(Program.Find<TargetCardPicker>(root).Single())
                    .Single(button => ReferenceEquals(button.CommandParameter, choice));
                var revision = game.Revision;
                button.Command!.Execute(button.CommandParameter);
                Program.Assert(game.Revision == revision + 1 && game.AcceptedCommands.Last() is AnswerPromptCommand answer &&
                    answer.Choice == choice.Id, "The native equipment picker must submit its exact original choice once.");
            }
            Program.AdvanceToDecision(model);
            Program.Assert(game.CreateSnapshot(0).Players[1].Equipment.Count == 0 &&
                game.CardMovements.Count(move => move.Reason == CardMoveReasons.IceSwordDiscard) == expectedRegions.Length,
                "Ice Sword must finish after exactly two cards, or its only remaining equipment card.");
            var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Program.Assert(SnapshotJson.Serialize(game.CreateSnapshot(0)) == SnapshotJson.Serialize(restored.CreateSnapshot(0)),
                "Native equipment selection must retain the original replay boundary.");
        }
        finally { window.Content = null; window.Close(); }
    }

    private static (MainViewModel ViewModel, LegalAction Action) FindFixture()
    {
        for (var seed = 1; seed <= 2_048; seed++)
        {
            var viewModel = new MainViewModel(
                autoAdvance: false,
                seed: seed,
                showSetup: false,
                saveStore: new MemorySaveStore())
            {
                IsMotionEnabled = false
            };
            viewModel.SelectGeneralChoiceCommand.Execute(viewModel.GeneralChoices[0]);
            Program.AdvanceToDecision(viewModel);
            var engine = Program.Engine(viewModel);
            var full = engine.CreateSnapshot(engine.State.HumanSeat, revealAll: true);
            var action = engine.GetHumanLegalActions().FirstOrDefault(candidate =>
                candidate.Kind is LegalActionKind.Dismantlement or LegalActionKind.Snatch &&
                candidate.CardId is not null &&
                candidate.TargetSeat is not null &&
                candidate.TargetCardId is null &&
                full.Players.Single(player => player.Seat == candidate.TargetSeat).HandCount > 0 &&
                full.Players.All(player => player.Hand.All(card => card.Kind != CardKind.Nullification)));
            if (action is not null)
            {
                return (viewModel, action);
            }

            viewModel.Dispose();
        }

        throw new InvalidOperationException("Could not find a deterministic WPF target-card fixture.");
    }
}
