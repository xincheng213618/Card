using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class PublicPileSkillUiChecks
{
    public static void ForeignPublicPileCostsUseSharedDraft(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        var game = ReachForeignPileAction(registry);
        // The fixture grants the public adjustment directly; this checks UI contract binding,
        // while Core behavior checks cover Qiaoshui's real Pindian win.
        Accept(game.Submit(new UseProgramSkillCommand(0, "fixture:ui-adjustment", "grant", [], [], game.Revision, game.PendingDecision!.PromptId)));
        for (var step = 0; step < 64 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(autoAdvance: false, seed: game.Seed, showSetup: true,
            saveStore: store, useExpandedContent: true, contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        game = Program.Engine(model);
        var action = model.HumanActiveSkillActions.First(item => item.ProgramSkillOwnerSeat is not null && item.ProgramSkillId == "classic:xiansi" && item.TargetSeats.Count == 2);
        var ownerSeat = action.ProgramSkillOwnerSeat!.Value;
        Program.Assert(model.HumanActiveSkillActions.Any(item => item.ProgramSkillId == action.ProgramSkillId &&
            item.ProgramActivationId == action.ProgramActivationId && item.ProgramSkillOwnerSeat == ownerSeat &&
            item.TargetSeats.SequenceEqual([ownerSeat])),
            "The adjusted action must coexist with its identical-identity base action for this regression.");
        var publicOwner = game.CreateSnapshot(0).Players[ownerSeat];
        Program.Assert(publicOwner.AuthorityName == "逆" &&
            model.Seats.Single(seat => seat.Seat == ownerSeat).AuthorityText == $"逆 ×{publicOwner.AuthorityCount}",
            "A public pile must use its content presentation name in the seat summary.");
        var retiredAction = action;
        var actionWindow = new MainWindow(model);
        actionWindow.ApplyTemplate();
        var actionCommand = model.SelectActiveSkillCommand;
        try
        {
            var actionRoot = (FrameworkElement)actionWindow.Content;
            Program.Render(actionRoot, 1120, 740, Path.Combine(output, "233-public-pile-action-before-reload.png"));
            var actionButton = Program.Find<Button>(actionRoot).Single(button =>
                ReferenceEquals(button.Command, model.SelectActiveSkillCommand) && ReferenceEquals(button.CommandParameter, action));
            actionCommand = actionButton.Command!;
            retiredAction = (LegalAction)actionButton.CommandParameter;
        }
        finally
        {
            actionWindow.Content = null;
            actionWindow.Close();
        }
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError && !ReferenceEquals(game, Program.Engine(model)) && !model.IsActiveSkillSelectionPending,
            "Manual reload must replace the public-pile game and clear its active draft.");
        game = Program.Engine(model);
        action = model.HumanActiveSkillActions.Single(item => item.ProgramSkillId == retiredAction.ProgramSkillId &&
            item.ProgramActivationId == retiredAction.ProgramActivationId && item.ProgramSkillOwnerSeat == ownerSeat &&
            item.TargetSeats.SequenceEqual(retiredAction.TargetSeats));
        Program.Assert(!ReferenceEquals(action, retiredAction), "Reload must publish a fresh equivalent public-pile action.");
        Program.Assert(model.SupplementalActiveSkillActions.Count > 0 && model.SupplementalActiveSkillActions.All(item =>
            model.HumanActiveSkillActions.Any(current => ReferenceEquals(current, item))),
            "Every restored supplemental button must retain a currently published action parameter.");
        var loadedState = SnapshotJson.Serialize(game.CreateSnapshot(0));
        var loadedText = model.SelectedCardText;
        actionCommand.Execute(retiredAction);
        Program.Assert(!model.IsActiveSkillSelectionPending && model.ActiveSkillEquipmentChoices.Count == 0 && model.SelectedCardText == loadedText &&
            SnapshotJson.Serialize(game.CreateSnapshot(0)) == loadedState,
            "A retired public-pile action reopened its draft against the loaded game.");
        model.SelectActiveSkillCommand.Execute(action with { });
        Program.Assert(!model.IsActiveSkillSelectionPending && model.ActiveSkillEquipmentChoices.Count == 0 && model.SelectedCardText == loadedText &&
            SnapshotJson.Serialize(game.CreateSnapshot(0)) == loadedState,
            "An equal copied active action must not choose the loaded public-pile owner or target contract.");
        var before = game.CreateSnapshot(0, revealAll: true);
        var moves = game.CardMovements.Count;
        var revision = game.Revision;
        var commandCount = game.AcceptedCommands.Count;
        var foreignActions = model.HumanActiveSkillActions.Where(item => item.ProgramSkillId == action.ProgramSkillId &&
            item.ProgramActivationId == action.ProgramActivationId && item.ProgramSkillOwnerSeat is not null).ToArray();
        Program.Assert(foreignActions.Select(item => item.ProgramSkillOwnerSeat).Distinct().Count() > 1 &&
            model.HumanActiveSkillActions.Where(item => item.ProgramSkillId == action.ProgramSkillId &&
                item.ProgramActivationId == action.ProgramActivationId).Select(item => item.ProgramSkillOwnerSeat).ToHashSet()
                .SetEquals(foreignActions.Select(item => item.ProgramSkillOwnerSeat)),
            "Actions with the same skill and activation must preserve every public pile owner.");
        var otherAction = foreignActions.First(item => item.ProgramSkillOwnerSeat != ownerSeat);
        model.SelectActiveSkillCommand.Execute(otherAction);
        Program.Assert(model.ActiveSkillEquipmentChoices.SelectMany(choice => choice.Cards).ToHashSet().SetEquals(otherAction.SelectableCardIds),
            "Selecting another owner must display that owner's legal public costs.");
        Program.Assert(!otherAction.SelectableCardIds.ToHashSet().SetEquals(model.HumanActiveSkillActions.First().SelectableCardIds),
            "The other-owner fixture must distinguish its costs from the default first action.");
        model.UseActiveSkillCommand.Execute(null);
        Program.Assert(!model.IsActiveSkillSelectionPending && !model.HasSelection && model.ActiveSkillEquipmentChoices.Count == 0 &&
            game.Revision == revision && game.AcceptedCommands.Count == commandCount && game.CardMovements.Count == moves &&
            SnapshotJson.Serialize(game.CreateSnapshot(0)) == loadedState,
            "Cancelling an empty other-owner draft must exit selection without submitting or moving cards.");
        model.UseActiveSkillCommand.Execute(null);
        Program.Assert(model.IsActiveSkillSelectionPending && !model.CanConfirmActiveSkill &&
            model.ActiveSkillEquipmentChoices.SelectMany(choice => choice.Cards).ToHashSet().SetEquals(otherAction.SelectableCardIds) &&
            model.Seats.All(seat => !seat.IsSelectedTarget) && game.Revision == revision &&
            game.AcceptedCommands.Count == commandCount && game.CardMovements.Count == moves &&
            SnapshotJson.Serialize(game.CreateSnapshot(0)) == loadedState,
            "Re-entering the empty draft must retain the selected public-pile owner instead of reverting to the first action.");
        model.SelectActiveSkillEquipmentChoiceCommand.Execute(model.ActiveSkillEquipmentChoices[0]);
        model.ClearSelectionCommand.Execute(null);
        Program.Assert(!model.IsActiveSkillSelectionPending && model.ActiveSkillEquipmentChoices.Count == 0 &&
            game.Revision == revision && game.CardMovements.Count == moves,
            "Cancelling another owner's draft must clear its costs without submitting a command.");
        model.SelectActiveSkillCommand.Execute(action);
        Program.Assert(model.IsActiveSkillSelectionPending && model.ActiveSkillEquipmentChoices.Count == action.SelectableCardIds.Count &&
            model.ActiveSkillEquipmentChoices.SelectMany(choice => choice.Cards).ToHashSet().SetEquals(action.SelectableCardIds) &&
            model.ActiveSkillEquipmentChoices.All(choice => choice.Description.Contains("“逆”", StringComparison.Ordinal) && choice.Description.Contains(publicOwner.GeneralName, StringComparison.Ordinal)) &&
            model.Hand.All(card => !card.IsPlayable),
            "The shared draft must expose only the specified owner's selectable public pile, with its owner and pile labels.");
        var window = new MainWindow(model);
        window.ApplyTemplate();
        try
        {
            var root = (FrameworkElement)window.Content;
            Program.Render(root, 1120, 740, Path.Combine(output, "233-foreign-public-pile-before-cost.png"));
            var first = model.ActiveSkillEquipmentChoices[0];
            var firstId = first.Cards.Single();
            var firstButton = Program.Find<Button>(root).Single(button =>
                ReferenceEquals(button.Command, model.SelectActiveSkillEquipmentChoiceCommand) && ReferenceEquals(button.CommandParameter, first));
            var costCommand = firstButton.Command!;
            var costParameter = firstButton.CommandParameter;
            costCommand.Execute(costParameter);
            Program.Assert(!model.CanConfirmActiveSkill && game.Revision == revision && game.CardMovements.Count == moves &&
                model.ActiveSkillEquipmentChoices.Single(choice => choice.Cards.Contains(firstId)).Description.StartsWith("✓", StringComparison.Ordinal),
                "Selecting one public cost must update the existing draft without submitting or moving cards.");
            RejectCost(costParameter, "A retired public-pile button toggled the current selected cost off.");
            RejectCost(model.ActiveSkillEquipmentChoices.Single(choice => choice.Cards.Contains(firstId)) with { },
                "An equal copied cost record changed the public-pile draft.");
            model.SelectActiveSkillEquipmentChoiceCommand.Execute(model.ActiveSkillEquipmentChoices.Single(choice => choice.Cards.Contains(firstId)));
            Program.Assert(model.ActiveSkillEquipmentChoices.All(choice => !choice.Description.StartsWith("✓", StringComparison.Ordinal)),
                "The shared public pile choice must support toggling a selected cost off.");
            var payment = action.SelectableCardIds.Take(2).ToArray();
            foreach (var id in payment)
                model.SelectActiveSkillEquipmentChoiceCommand.Execute(model.ActiveSkillEquipmentChoices.Single(choice => choice.Cards.Contains(id)));
            foreach (var target in action.TargetSeats.Reverse())
                model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == target));
            Program.Assert(model.CanConfirmActiveSkill && model.HasSelection && game.Revision == revision && game.CardMovements.Count == moves,
                "Two public cards and the pile owner must enable the ordinary confirmation dock without paying early.");
            Program.Assert(model.SupplementalActiveSkillActions.Count == 0,
                "The prepared public-pile draft must withdraw supplemental action buttons until confirmation or cancellation.");
            RejectActiveAction(action, "An old supplemental action must not submit the prepared public costs outside confirmation.");
            RejectActiveAction(model.HumanActiveSkillActions.Single(item => item.ProgramSkillId == action.ProgramSkillId &&
                item.ProgramActivationId == action.ProgramActivationId && item.ProgramSkillOwnerSeat == ownerSeat &&
                item.TargetSeats.SequenceEqual([ownerSeat])),
                "A still-legal base action must not replace the prepared adjusted target contract.");
            RejectActiveAction(model.HumanActiveSkillActions.First(item => item.ProgramSkillId == action.ProgramSkillId &&
                item.ProgramActivationId == action.ProgramActivationId && item.ProgramSkillOwnerSeat != ownerSeat),
                "A still-legal other-owner action must not replace the prepared pile owner or costs.");
            var beforeTutorial = model.ActiveSkillEquipmentChoices.Single(choice => choice.Cards.Contains(payment[0]));
            model.StartTutorialCommand.Execute(null);
            Program.Assert(model.IsTutorialActive, "The tutorial must suspend the prepared two-target draft.");
            model.ExitTutorialCommand.Execute(null);
            Program.Assert(!model.IsTutorialActive && ReferenceEquals(Program.Engine(model), game) && model.CanConfirmActiveSkill &&
                model.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).ToHashSet().SetEquals(action.TargetSeats) &&
                model.ActiveSkillEquipmentChoices.Where(choice => choice.Description.StartsWith("✓", StringComparison.Ordinal))
                    .SelectMany(choice => choice.Cards).ToHashSet().SetEquals(payment) &&
                game.AcceptedCommands.Count == commandCount && game.CardMovements.Count == moves,
                "Returning from the tutorial must preserve the adjusted contract, two selected costs and targets without paying.");
            RejectCost(beforeTutorial, "A retired pre-tutorial cost parameter changed the resumed costs or targets.");
            Program.Render(root, 1120, 740, Path.Combine(output, "233-foreign-public-pile-cost.png"));
            model.UseActiveSkillCommand.Execute(null);
            var submitted = game.AcceptedCommands.Last() as UseProgramSkillCommand;
            Program.Assert(game.AcceptedCommands.Count == commandCount + 1 && submitted is { SkillId: "classic:xiansi", SkillOwnerSeat: not null } && submitted.SkillOwnerSeat == ownerSeat &&
                submitted.CardIds.ToHashSet().SetEquals(payment) && submitted.TargetSeats.SequenceEqual(action.TargetSeats),
                "Confirmation must retain the public pile owner's identity and the exact selected physical costs.");
            Program.Assert(game.CardMovements.Skip(moves).Count(move => payment.Contains(move.CardId) &&
                move.From == CardLocation.Authority(ownerSeat) && move.To == CardLocation.DiscardPile) == 2 &&
                game.CreateSnapshot(0, revealAll: true).Players[0].Hand!.Select(card => card.Id).SequenceEqual(before.Players[0].Hand!.Select(card => card.Id)),
                "The shared UI must spend only the two foreign public cards and preserve the actor's private hand.");
            var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Program.Assert(SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) &&
                JsonSerializer.Serialize(game.AcceptedCommands) == JsonSerializer.Serialize(restored.AcceptedCommands) &&
                JsonSerializer.Serialize(game.Events) == JsonSerializer.Serialize(restored.Events) && game.CardMovements.SequenceEqual(restored.CardMovements),
                "The UI-submitted foreign public-pile Slash must replay its state, commands, events and movements.");

            void RejectCost(object? input, string message)
            {
                var selected = model.ActiveSkillEquipmentChoices.Where(choice => choice.Description.StartsWith("✓", StringComparison.Ordinal))
                    .SelectMany(choice => choice.Cards).ToArray();
                var targets = model.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).ToArray();
                var text = model.SelectedCardText;
                var canConfirm = model.CanConfirmActiveSkill;
                var state = SnapshotJson.Serialize(game.CreateSnapshot(0));
                costCommand.Execute(input);
                Program.Assert(model.ActiveSkillEquipmentChoices.Where(choice => choice.Description.StartsWith("✓", StringComparison.Ordinal))
                        .SelectMany(choice => choice.Cards).SequenceEqual(selected) && model.SelectedCardText == text &&
                    model.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).SequenceEqual(targets) &&
                    model.CanConfirmActiveSkill == canConfirm && model.IsActiveSkillSelectionPending &&
                    game.Revision == revision && game.AcceptedCommands.Count == commandCount && game.CardMovements.Count == moves &&
                    SnapshotJson.Serialize(game.CreateSnapshot(0)) == state, message);
            }

            void RejectActiveAction(LegalAction input, string message)
            {
                var fields = new[] { "_selectedActiveSkillIdentity", "_isSelectingActiveSkillCards" }
                    .Select(name => typeof(MainViewModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!).ToArray();
                var values = fields.Select(field => field.GetValue(model)).ToArray();
                var selected = model.ActiveSkillEquipmentChoices.Where(choice => choice.Description.StartsWith("✓", StringComparison.Ordinal))
                    .SelectMany(choice => choice.Cards).ToArray();
                var targets = model.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).ToArray();
                var text = model.SelectedCardText;
                var prompt = model.PromptText;
                var state = SnapshotJson.Serialize(game.CreateSnapshot(0));
                model.SelectActiveSkillCommand.Execute(input);
                Program.Assert(fields.Select(field => field.GetValue(model)).SequenceEqual(values) && model.CanConfirmActiveSkill &&
                    model.ActiveSkillEquipmentChoices.Where(choice => choice.Description.StartsWith("✓", StringComparison.Ordinal))
                        .SelectMany(choice => choice.Cards).SequenceEqual(selected) && model.SelectedCardText == text && model.PromptText == prompt &&
                    model.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).SequenceEqual(targets) &&
                    game.Revision == revision && game.AcceptedCommands.Count == commandCount && game.CardMovements.Count == moves &&
                    SnapshotJson.Serialize(game.CreateSnapshot(0)) == state, message);
            }
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static GameEngine ReachForeignPileAction(ContentRegistry registry)
    {
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, HumanSeat = 0, HumanRole = Role.Lord,
            PlayerCount = 4, ModeId = "fixture:ui-public-pile", UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 80 }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, "fixture:ui-public-pile-actor", game.Revision, game.PendingDecision!.PromptId)));
        for (var step = 0; step < 768; step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } prompt)
            {
                if (game.GetHumanLegalActions().Any(item => item.ProgramSkillOwnerSeat is not null && item.ProgramSkillId == "classic:xiansi")) return game;
                Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)));
            }
            else Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        }
        throw new InvalidOperationException("The public-pile UI fixture did not expose a foreign-pile cost action.");
    }
    private static void Accept(CommandResult result) => Program.Assert(result.Accepted, result.Error?.Message ?? "Fixture command rejected.");

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ui-public-pile", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var rules = $$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:ui-adjustment","revision":1,"activations":[{"id":"grant","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":1,"effects":[{"op":"grantNextCardTargetAdjustment","target":"owner"}]}]}]}""";
            var catalog = SkillProgramCatalog.Load(rules, """{"schemaVersion":3,"skills":{"fixture:ui-adjustment":{"name":"测试目标调整","description":"界面契约检查"}}}""");
            builder.AddSkill(new ContentSkillDefinition("fixture:ui-adjustment", "测试目标调整", "界面契约检查")
                { Program = catalog.Programs["fixture:ui-adjustment"], ProgramPresentation = catalog.Presentations["fixture:ui-adjustment"] });
            builder.AddGeneral(new("fixture:ui-public-pile-actor", "借杀者", "supporter", "fixture:ui-adjustment", "shu", BaseHp: 10));
            for (var index = 1; index < 4; index++) builder.AddGeneral(new($"fixture:ui-public-pile-owner-{index}", $"牌堆者{index}", "supporter", "classic:xiansi", "shu", BaseHp: 10));
            builder.AddDeck(new("fixture:ui-public-pile-deck", "公共牌堆界面测试", 8, 0, [new("standard:dodge", 160)]));
            builder.AddMode(new("fixture:ui-public-pile", "公共牌堆界面测试", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:ui-public-pile-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:ui-public-pile-actor", "fixture:ui-public-pile-owner-1", "fixture:ui-public-pile-owner-2", "fixture:ui-public-pile-owner-3"]));
        }
    }
}
