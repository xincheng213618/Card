using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class WushengResponseChecks
{
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static bool IsConverted(CardSnapshot card) => card.Kind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash);

    public static void CommandsAndReplay()
    {
        foreach (var incoming in new[] { CardKind.Duel, CardKind.BarbarianAssault })
        {
            var game = WushengResponseScenario.Find(incoming);
            var prompt = game.PendingDecision!;
            var own = game.CreateSnapshot(0).Players[0];
            var choice = prompt.Choices.First(candidate => candidate.Cards.Count == 1 && IsConverted(own.Hand.Single(card => card.Id == candidate.Cards[0])));
            var physical = own.Hand.Single(card => card.Id == choice.Cards[0]);
            Require(physical.Suit is Suit.Heart or Suit.Diamond && choice.Parameters["response-card-kind"] == "Slash" && choice.Description.Contains("当作【杀】"), "Response did not publish an explicit red-card Slash conversion.");
            Require(game.CreateSnapshot(1).PendingDecision is null, "Private red-card options leaked to another viewer.");
            var checkpoint = game.CreateCheckpoint();
            var registry = StandardContentRegistry.Create();
            var legacyBase = GameEngine.CreateStandard(checkpoint.Options, registry);
            var legacy = GameReplay.Restore(legacyBase.CreateCheckpoint() with { RulesVersion = 8 }, registry);
            var legacyPlayers = ((System.Collections.IEnumerable)typeof(GameEngine)
                    .GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(legacy)!)
                .Cast<object>()
                .ToArray();
            var responseCards = (IReadOnlyList<Card>)typeof(GameEngine)
                .GetMethod("GetResponseCards", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(legacy, [legacyPlayers[0], CardKind.Slash])!;
            Require(responseCards.All(card => card.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash),
                "Rules 8 gained the new response conversion.");
            var legacyBefore = GameCheckpointJson.Serialize(legacy.CreateCheckpoint());
            Require(!legacy.Submit(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, legacy.Revision)).Accepted &&
                GameCheckpointJson.Serialize(legacy.CreateCheckpoint()) == legacyBefore, "Rejected old-rule conversion paid a cost or changed the journal.");
            var response = new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision);
            Require(game.Submit(response).Accepted, "Published Wusheng response was rejected.");
            Require(game.Events.Count(item => item.Payload is CardRespondedEvent responded && responded.CardId == physical.Id && responded.ResponderSeat == 0 && responded.EffectiveCardKind == CardKind.Slash) == 1,
                "Response did not record exactly one effective Slash.");
            Require(game.CardMovements.Any(move => move.CardId == physical.Id && move.CardKind == physical.Kind && move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing && move.Reason == CardMoveReasons.Respond),
                "Response changed the physical card identity or bypassed the response movement.");
            Require(!game.CreateSnapshot(0).Players[0].Hand.Any(card => card.Id == physical.Id), "Converted physical card remained in hand.");
            var after = GameCheckpointJson.Serialize(game.CreateCheckpoint());
            Require(!game.Submit(response).Accepted && GameCheckpointJson.Serialize(game.CreateCheckpoint()) == after, "A repeated response spent the card twice.");
            for (var step = 0; step < 10000 && game.State.Status != EngineStatus.Completed; step++) WushengResponseScenario.Step(game);
            Require(game.State.Status == EngineStatus.Completed, "The group/duel continuation stalled after a converted response.");
            var replay = GameReplay.Restore(game.CreateCheckpoint(), StandardContentRegistry.Create());
            Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, true)) == SnapshotJson.Serialize(game.CreateSnapshot(0, true)), "Completed converted-response match did not replay.");
            Console.WriteLine($"  Wusheng {incoming}: seed {checkpoint.Options.Seed}, physical {physical.Kind}, complete and replayed.");
        }
    }

    public static void NationalAndScope()
    {
        foreach (var slot in new[] { GeneralSelectionSlot.Primary, GeneralSelectionSlot.Secondary })
        {
            var game = NationalWarChecks.SkillFixture("national:shu-guan-yu", "national:shu-zhang-fei", slot, requireRed: true);
            var runtime = ((System.Collections.IEnumerable)typeof(GameEngine).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game)!).Cast<object>().First();
            var method = typeof(GameEngine).GetMethod("GetResponseCards", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Card[] Cards(CardKind required) => ((IReadOnlyList<Card>)method.Invoke(game, [runtime, required])!).ToArray();
            Require(Cards(CardKind.Slash).All(card => card.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash), "Hidden national Wusheng enabled response conversion.");
            Require(game.Submit(new RevealGeneralCommand(0, slot, game.Revision, game.PendingDecision!.PromptId)).Accepted, "National Wusheng reveal failed.");
            var hand = game.CreateSnapshot(0).Players[0].Hand;
            var expected = hand.Where(card => !IsConverted(card) || card.Suit is Suit.Heart or Suit.Diamond).Select(card => card.Id).ToHashSet();
            Require(expected.SetEquals(Cards(CardKind.Slash).Select(card => card.Id)) && Cards(CardKind.Slash).Length == expected.Count, "Revealed national response cards omit red cards, include black cards, or duplicate a physical card.");
            Require(Cards(CardKind.Dodge).All(card => card.Kind == CardKind.Dodge), "Wusheng incorrectly answered a Dodge prompt.");
        }
    }

    public static void NationalRevealDuringResponse()
    {
        for (var seed = 721000; seed < 721200; seed++)
        {
            GameEngine game;
            try
            {
                game = NationalWarChecks.SkillFixture(
                    "national:shu-guan-yu",
                    "national:shu-zhang-fei",
                    GeneralSelectionSlot.Primary,
                    requireRed: true,
                    exactSeed: seed);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            var play = game.PendingDecision ?? throw new InvalidOperationException("National skill fixture lost its play prompt.");
            Require(game.Submit(new EndPlayPhaseCommand(0, game.Revision, play.PromptId)).Accepted, "National response fixture could not end the human play phase.");

            for (var step = 0; step < 5000 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0 } response)
                {
                    var ownBefore = game.CreateSnapshot(0).Players[0];
                    var publicBefore = game.CreateSnapshot(1).Players[0];
                    Require(response.Choices.All(choice =>
                        !choice.Cards.Any(cardId => ownBefore.Hand.Single(card => card.Id == cardId).Kind is not
                            (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash))),
                        "Hidden national Wusheng appeared in a Slash response before reveal.");
                    Require(game.CreateSnapshot(1).PendingDecision is null && !publicBefore.IsGeneralPublic,
                        "National response prompt or hidden general leaked before reveal.");
                    var legacy = GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = 8 },
                        StandardContentRegistry.CreateWithNationalWarLite());
                    Require(legacy.GetHumanLegalActions().Count == 0,
                        "Rules 8 gained the new response-time national reveal action.");

                    var revealResult = game.Submit(new RevealGeneralCommand(
                            0,
                            GeneralSelectionSlot.Primary,
                            game.Revision,
                            response.PromptId));
                    Require(revealResult.Accepted, "National general reveal was rejected during Slash response.");
                    var refreshed = game.PendingDecision ?? throw new InvalidOperationException("National response prompt disappeared after reveal.");
                    Require(refreshed.Kind == DecisionKind.RespondSlash && refreshed.PromptId != response.PromptId,
                        "National reveal did not publish a refreshed Slash response prompt.");
                    Require(game.GetHumanLegalActions().All(action => action.GeneralSlot != GeneralSelectionSlot.Primary),
                        "The revealed national slot remained actionable during the response.");
                    var physical = ownBefore.Hand.First(card =>
                        card.Suit is Suit.Heart or Suit.Diamond &&
                        card.Kind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash));
                    var converted = refreshed.Choices.Single(choice => choice.Cards.SequenceEqual([physical.Id]));
                    Require(converted.Parameters["response-card-kind"] == "Slash" && converted.Description.Contains("当作【杀】"),
                        "Revealing national Wusheng did not refresh the red-card Slash response candidate.");
                    Require(game.CreateSnapshot(1).PendingDecision is null && game.CreateSnapshot(1).Players[0].IsGeneralPublic,
                        "Refreshing a private national response leaked its prompt to another viewer.");

                    var restored = GameReplay.Restore(game.CreateCheckpoint(), StandardContentRegistry.CreateWithNationalWarLite());
                    Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, true)) == SnapshotJson.Serialize(game.CreateSnapshot(0, true)),
                        "National response reveal did not replay exactly.");
                    Require(game.Submit(new AnswerPromptCommand(0, refreshed.PromptId, converted.Id, game.Revision)).Accepted,
                        "Refreshed national Wusheng response was rejected.");
                    Require(game.Events.Any(item => item.Payload is NationalGeneralRevealedEvent revealed &&
                        revealed.Seat == 0 && revealed.Slot == GeneralSelectionSlot.Primary) &&
                        game.Events.Any(item => item.Payload is CardRespondedEvent responded &&
                            responded.ResponderSeat == 0 && responded.CardId == physical.Id &&
                            responded.EffectiveCardKind == CardKind.Slash),
                        "National response reveal did not retain typed reveal and response events.");
                    Console.WriteLine($"  National Wusheng response reveal: seed {game.Seed}, physical {physical.Kind}, refreshed and replayed.");
                    return;
                }

                if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } nextPlay)
                {
                    Require(game.Submit(new EndPlayPhaseCommand(0, game.Revision, nextPlay.PromptId)).Accepted,
                        "National response fixture could not advance past a later human play phase.");
                }
                else if (game.PendingDecision is { Kind: DecisionKind.DiscardCards, PlayerSeat: 0 } discard)
                {
                    Require(game.Submit(new DiscardCardsCommand(
                        0,
                        discard.ValidCardIds.Take(discard.RequiredCardCount).ToArray(),
                        discard.PromptId,
                        game.Revision)).Accepted,
                        "National response fixture could not advance past a human discard prompt.");
                }
                else if (game.PendingDecision is { Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash, PlayerSeat: 0 } otherResponse)
                {
                    Require(game.Submit(new AnswerPromptCommand(
                        0,
                        otherResponse.PromptId,
                        otherResponse.Choices.Single(choice => choice.Parameters.GetValueOrDefault("response") == "take-damage").Id,
                        game.Revision)).Accepted,
                        "National response fixture could not answer an unrelated human response.");
                }
                else if (game.PendingDecision?.PlayerSeat == 0)
                {
                    break;
                }
                else
                {
                    Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                        "National response fixture could not advance the AI.");
                }
            }
        }

        throw new InvalidOperationException("Bounded national matches never produced a human Slash response with hidden Wusheng.");
    }

    public static void AiResponseAndReplay()
    {
        for (var seed = 1; seed <= 32; seed++)
        {
            var registry = StandardContentRegistry.Create();
            var game = GameEngine.CreateStandard(new GameOptions { Seed = seed, HumanSeat = -1, HumanRole = null, UseInteractiveSetup = false, MaxTurns = 100, AiPolicyVersion = 2, AdvanceAfterHumanCommands = false }, registry);
            if (!game.CreateSnapshot(-1, true).Players.Any(player => player.Skill == SkillKind.Wusheng)) continue;
            Require(game.Submit(new StartGameCommand()).Accepted, "AI Wusheng fixture failed to start.");
            for (var step = 0; step < 12000 && game.State.Status != EngineStatus.Completed; step++)
                Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "AI Wusheng response stalled.");
            Require(game.State.Status == EngineStatus.Completed, "AI Wusheng match did not finish.");
            var generals = game.CreateSnapshot(-1, true).Players;
            var converted = game.Events.Select(item => item.Payload).OfType<CardRespondedEvent>().Count(response =>
                generals.Single(player => player.Seat == response.ResponderSeat).Skill == SkillKind.Wusheng && response.EffectiveCardKind == CardKind.Slash &&
                game.CardMovements.Last(move => move.CardId == response.CardId && move.Reason == CardMoveReasons.Respond).CardKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash));
            if (converted == 0) continue;
            var replay = GameReplay.Restore(game.CreateCheckpoint(), registry);
            Require(SnapshotJson.Serialize(replay.CreateSnapshot(-1, true)) == SnapshotJson.Serialize(game.CreateSnapshot(-1, true)), "AI response choices did not replay deterministically.");
            Console.WriteLine($"  AI Wusheng: seed {seed}, {converted} converted Slash responses, completed and replayed.");
            return;
        }
        throw new InvalidOperationException("Bounded AI matches never used a Wusheng response.");
    }

}
