using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class WushengResponseChecks
{
    public static void ControlsAndSavedRules(string output)
    {
        foreach (var incoming in new[] { CardKind.Duel, CardKind.BarbarianAssault })
        {
            var engine = WushengResponseScenario.Find(incoming);
            var checkpoint = engine.CreateCheckpoint();
            var hand = engine.CreateSnapshot(0).Players[0].Hand;
            var choice = engine.PendingDecision!.Choices.First(candidate => candidate.Cards.Count == 1 &&
                hand.Single(card => card.Id == candidate.Cards[0]).Kind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash));
            var id = choice.Cards[0];
            var store = new FileGameSaveStore(Path.Combine(output, "wusheng-saves", Guid.NewGuid().ToString("N")));
            using var vm = new MainViewModel(false, 1, true, store) { IsMotionEnabled = false };
            // One stable Duel fixture is sufficient to verify the Wusheng rule
            // boundary. A current-rules command prefix that already crossed a
            // delayed judgment cannot be relabelled as v8 after rules 11.
            if (incoming == CardKind.Duel)
            {
                store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, checkpoint with { RulesVersion = 8 }));
                vm.LoadManualGameCommand.Execute(null);
                Program.Assert(!vm.HasSaveError && Program.Engine(vm).RulesVersion == 8 && !vm.Hand.Single(card => card.Id == id).IsPlayable,
                    "Old-rule UI enabled the new conversion or could not restore the old prompt.");
                var oldState = GameCheckpointJson.Serialize(Program.Engine(vm).CreateCheckpoint());
                vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == id));
                vm.ConfirmSelectedCommand.Execute(null);
                Program.Assert(!vm.HasSelection && GameCheckpointJson.Serialize(Program.Engine(vm).CreateCheckpoint()) == oldState, "Disabled legacy response changed the game.");
            }
            store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, checkpoint));
            vm.LoadManualGameCommand.Execute(null);
            Program.Assert(!vm.HasSaveError && vm.Hand.Single(card => card.Id == id).IsPlayable, "New-rule response did not become available after load.");
            vm.SaveGameCommand.Execute(null);
            vm.LoadManualGameCommand.Execute(null);
            Program.Assert(!vm.HasSaveError && GameCheckpointJson.Serialize(Program.Engine(vm).CreateCheckpoint()) == GameCheckpointJson.Serialize(checkpoint),
                "Saving the pending response changed its exact command history.");
            HandResponseChecks.VerifyBoundary(vm, output, $"Wusheng-{incoming}", id, "当作杀打出");
            Program.Assert(Program.Engine(vm).Events.Any(item => item.Payload is CardRespondedEvent response && response.CardId == id && response.EffectiveCardKind == CardKind.Slash),
                "WPF did not commit the converted Slash event.");
            // VerifyBoundary closes its window and disposes that window's VM.
            // Continue the committed response in a fresh UI session.
            store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, Program.Engine(vm).CreateCheckpoint()));
            using var resumed = new MainViewModel(false, 2, true, store);
            resumed.LoadManualGameCommand.Execute(null);
            Program.Assert(!resumed.HasSaveError, resumed.SaveStatus);
            for (var step = 0; step < 12000 && !resumed.HasGameOver; step++) PersistenceChecks.Step(resumed);
            Program.Assert(resumed.HasGameOver, "WPF response failed to continue to a completed match.");
            MatchSummaryChecks.VerifyCompleted(resumed);
        }
        var longdan = WushengResponseScenario.FindLongdanDodge();
        var responseHand = longdan.CreateSnapshot(0).Players[0].Hand;
        var slashChoice = longdan.PendingDecision!.Choices.First(choice => choice.Cards.Count == 1 && responseHand.Single(card => card.Id == choice.Cards[0]).Kind != CardKind.Dodge);
        var memory = new MemorySaveStore();
        memory.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, longdan.CreateCheckpoint()));
        using var longdanVm = new MainViewModel(false, 1, true, memory);
        longdanVm.LoadManualGameCommand.Execute(null);
        Program.Assert(!longdanVm.HasSaveError, longdanVm.SaveStatus);
        HandResponseChecks.VerifyBoundary(longdanVm, output, "Longdan-ConvertedDodge", slashChoice.Cards[0], "当作闪打出");

        var equippedWusheng = WushengResponseScenario.FindClassicWushengEquipmentResponse();
        var equippedOwner = equippedWusheng.CreateSnapshot(0, revealAll: true).Players[0];
        var equipmentChoice = equippedWusheng.PendingDecision!.Choices.Single(choice =>
            choice.Cards.Count == 1 &&
            equippedOwner.Equipment.Any(card => card.Id == choice.Cards[0]) &&
            choice.Parameters.GetValueOrDefault("response-card-kind") == nameof(CardKind.Slash));
        var equipmentId = equipmentChoice.Cards[0];
        var equipmentStore = new MemorySaveStore();
        equipmentStore.Write(GameSaveSlot.Manual, new(
            1,
            DateTimeOffset.UtcNow,
            false,
            equippedWusheng.CreateCheckpoint()));
        using var equipmentVm = new MainViewModel(
            false,
            equippedWusheng.Seed,
            true,
            equipmentStore,
            useExpandedContent: true)
        {
            IsMotionEnabled = false
        };
        var equipmentWindow = new MainWindow(equipmentVm);
        equipmentVm.LoadManualGameCommand.Execute(null);
        Program.Assert(!equipmentVm.HasSaveError &&
                       equipmentVm.Hand.All(card => card.Id != equipmentId) &&
                       equipmentVm.ResponseChoices.Any(choice => choice.Id == equipmentChoice.Id),
            "The WPF must expose an equipped Wusheng response as a central private choice, not a hand card.");
        Program.Render(
            (FrameworkElement)equipmentWindow.Content,
            1120,
            740,
            Path.Combine(output, "108-classic-wusheng-equipment-response.png"));
        equipmentVm.SelectResponseChoiceCommand.Execute(equipmentChoice);
        Program.Assert(Program.Engine(equipmentVm).Events.Any(item =>
                           item.Payload is CardRespondedEvent response &&
                           response.CardId == equipmentId &&
                           response.EffectiveCardKind == CardKind.Slash) &&
                       Program.Engine(equipmentVm).CardMovements.Any(move =>
                           move.CardId == equipmentId &&
                           move.From == CardLocation.Equipment(0) &&
                           move.To == CardLocation.Processing &&
                           move.Reason == CardMoveReasons.Respond),
            "The WPF central response did not commit the equipped Wusheng physical card.");
        equipmentWindow.Content = null;
        equipmentWindow.Close();
    }

    public static void NationalRevealDuringResponse(string output)
    {
        var engine = CreateNationalResponseFixture();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, engine.CreateCheckpoint()));
        using var vm = new MainViewModel(false, engine.Seed, true, store, useExpandedContent: true)
        {
            IsMotionEnabled = false
        };
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        vm.LoadManualGameCommand.Execute(null);
        Program.Render(root, 1120, 740, Path.Combine(output, "68-national-response-reveal.png"));

        var before = Program.Engine(vm).PendingDecision ?? throw new InvalidOperationException("National response prompt did not restore.");
        var primary = vm.NationalRevealChoices.Single(choice => choice.Slot == GeneralSelectionSlot.Primary);
        var revealButtons = (ItemsControl)window.FindName("NationalRevealButtons");
        var revealButton = Program.Find<Button>(revealButtons).Single(button => Equals(button.CommandParameter, primary));
        Program.Assert(revealButton.IsEnabled && revealButton.ActualHeight > 0, "Response-time national reveal is not rendered as an actionable button.");
        revealButton.Command.Execute(revealButton.CommandParameter);

        var after = Program.Engine(vm).PendingDecision ?? throw new InvalidOperationException("National response prompt disappeared after WPF reveal.");
        Program.Assert(after.Kind == DecisionKind.RespondSlash && after.PromptId != before.PromptId &&
            vm.NationalRevealChoices.All(choice => choice.Slot != GeneralSelectionSlot.Primary),
            "WPF national reveal did not preserve and refresh the response prompt.");
        var physical = vm.Hand.First(card => card.SuitGlyph is "♥" or "♦" &&
            card.Name is not ("杀" or "火杀" or "雷杀"));
        var converted = vm.ResponseChoices.Single(choice => choice.Cards.SequenceEqual([physical.Id]));
        vm.SelectCardCommand.Execute(physical);
        Program.Assert(vm.CanConfirmSelected && vm.PlayButtonText == "当作杀打出",
            $"WPF did not expose the refreshed national conversion through hand confirmation: can={vm.CanConfirmSelected}, text={vm.PlayButtonText}, playable={physical.IsPlayable}, responseCount={vm.ResponseChoices.Count}.");
        vm.ConfirmSelectedCommand.Execute(null);
        Program.Assert(Program.Engine(vm).Events.Any(item => item.Payload is CardRespondedEvent response &&
            response.ResponderSeat == 0 && response.CardId == physical.Id && response.EffectiveCardKind == CardKind.Slash),
            "WPF did not submit the refreshed national response conversion.");
        window.Content = null;
        window.Close();
    }

    private static GameEngine CreateNationalResponseFixture()
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 721008,
            HumanSeat = 0,
            HumanRole = null,
            PlayerCount = 4,
            ModeId = "national:lite-4",
            UseInteractiveSetup = true,
            UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 160
        }, StandardContentRegistry.CreateWithNationalWarLite());
        Program.Assert(game.Submit(new StartGameCommand()).Accepted, "National WPF response fixture did not start.");
        var selected = 0;
        for (var step = 0; step < 100 && game.State.Status != EngineStatus.AwaitingHumanPlay; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.SelectGeneral } prompt)
            {
                var id = selected++ == 0 ? "national:shu-guan-yu" : "national:shu-zhang-fei";
                Program.Assert(prompt.ValidContentIds.Contains(id), "National WPF response fixture could not select the expected general.");
                Program.Assert(game.Submit(new SelectGeneralCommand(0, id, game.Revision, prompt.PromptId)).Accepted,
                    "National WPF response fixture could not select a general.");
            }
            else
            {
                Program.Assert(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                    "National WPF response fixture setup stalled.");
            }
        }

        var play = game.PendingDecision;
        Program.Assert(play is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
            "National WPF response fixture did not reach human play.");
        Program.Assert(game.Submit(new EndPlayPhaseCommand(0, game.Revision, play!.PromptId)).Accepted,
            "National WPF response fixture could not end the human play phase.");
        for (var step = 0; step < 5000; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0 }) return game;
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } nextPlay)
            {
                Program.Assert(game.Submit(new EndPlayPhaseCommand(0, game.Revision, nextPlay.PromptId)).Accepted,
                    "National WPF response fixture could not end a later human play phase.");
            }
            else if (game.PendingDecision is { Kind: DecisionKind.DiscardCards, PlayerSeat: 0 } discard)
            {
                Program.Assert(game.Submit(new DiscardCardsCommand(0,
                    discard.ValidCardIds.Take(discard.RequiredCardCount).ToArray(), discard.PromptId, game.Revision)).Accepted,
                    "National WPF response fixture could not resolve a human discard.");
            }
            else if (game.PendingDecision is { Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash, PlayerSeat: 0 } response)
            {
                var choice = response.Choices.Single(option => option.Parameters.GetValueOrDefault("response") == "take-damage");
                Program.Assert(game.Submit(new AnswerPromptCommand(0, response.PromptId, choice.Id, game.Revision)).Accepted,
                    "National WPF response fixture could not decline an unrelated response.");
            }
            else if (game.PendingDecision?.PlayerSeat == 0)
            {
                break;
            }
            else
            {
                Program.Assert(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                    "National WPF response fixture could not advance the AI.");
            }
        }

        throw new InvalidOperationException("National WPF response fixture never reached a human Slash response.");
    }
}
