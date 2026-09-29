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

}
