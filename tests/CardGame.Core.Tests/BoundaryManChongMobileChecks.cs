using CardGame.Core;
using static BoundaryLiDianChecks;

internal static class BoundaryManChongMobileChecks
{
    private const string Skill = BoundaryManChongScenario.Skill;

    public static void EqualHePaymentRetainsOriginalCostAndNativeHpLossCold()
    {
        var scene = BoundaryManChongScenario.CreateScene(); var game = scene.Game;
        var equipment = game.CreateSnapshot(0).Players[0].Hand[0].Id;
        Use(game, BoundaryManChongScenario.Driver, "equip-target", [], [1]);
        Reach(game, prompt => prompt.Choices.Any(choice => choice.Cards.SequenceEqual([equipment])));
        Answer(game, choice => choice.Cards.SequenceEqual([equipment])); Settle(game);
        var original = game.CreateSnapshot(0).Players[0].Hand.Take(2).Select(card => card.Id).ToArray();
        Use(game, Skill, "equal-discard-or-turn", original, [1]);
        Reach(game, prompt => IsJunxing(prompt));
        Require(original.All(id => game.CardMovements.Any(move => move.CardId == id && move.From == CardLocation.Hand(0) && move.To == CardLocation.DiscardPile)),
            "The original two hand cards are actually paid before the target's private choice.");
        var hand = game.CreateSnapshot(1).Players[1].Hand[0].Id;
        Answer(game, choice => choice.Cards.SequenceEqual([hand]) && choice.Parameters.GetValueOrDefault("branch") == "card");
        ReplayFourViews(game, scene.Registry);
        Answer(game, choice => choice.Cards.SequenceEqual([equipment]) && choice.Parameters.GetValueOrDefault("branch") == "card");
        var hp = game.CreateSnapshot(1).Players[1].Hp;
        Answer(game, choice => choice.Parameters.GetValueOrDefault("branch") == "discard");
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == BoundaryManChongScenario.Watch);
        Require(game.CreateSnapshot(1).Players[1].Hp == hp &&
            new[] { hand, equipment }.All(id => game.CardMovements.Count(move => move.CardId == id && move.Reason.Value == "skill-program.boundary-junxing-mobile.target-discard") == 1),
            "The target pays exactly one hand and one equipment entity before the movement child returns and HP loss starts.");
        ReplayFourViews(game, scene.Registry);
        Answer(game, choice => choice.Parameters.GetValueOrDefault("option-id") == "continue"); Settle(game);
        Require(game.CreateSnapshot(1).Players[1].Hp == hp - 1 && !game.CreateSnapshot(1).Players[1].IsFaceDown &&
            original.All(id => game.CardMovements.Count(move => move.CardId == id && move.From == CardLocation.Hand(0) && move.To == CardLocation.DiscardPile) == 1) &&
            game.Events.Select(record => record.Payload).OfType<BoundaryManChongChoiceCommittedEvent>().Count() == 1,
            "The original paid frame loses one HP after its child, never repays the owner and does not take the turn-over branch.");
        Replay(game, scene.Registry);
    }

    public static void TurnOverDrawUsesOriginalCountAndSharedYuceIsPerDamage()
    {
        var scene = BoundaryManChongScenario.CreateScene(); var game = scene.Game;
        var original = game.CreateSnapshot(0).Players[0].Hand.Take(2).Select(card => card.Id).ToArray();
        var before = game.CreateSnapshot(1).Players[1];
        Use(game, Skill, "equal-discard-or-turn", original, [1]); Reach(game, IsJunxing);
        Answer(game, choice => choice.Parameters.GetValueOrDefault("branch") == "turn"); Settle(game);
        Require(game.CreateSnapshot(1).Players[1].IsFaceDown && game.CreateSnapshot(1).Players[1].Hp == before.Hp &&
            game.CreateSnapshot(1).Players[1].HandCount == before.HandCount + 2,
            "The alternative flips the actual target and draws the original two-card count without losing HP.");
        var retry = game.Submit(new UseProgramSkillCommand(0, Skill, "equal-discard-or-turn",
            [game.CreateSnapshot(0).Players[0].Hand[0].Id], [1], game.Revision, P(game)!.PromptId));
        Require(!retry.Accepted, "Junxing remains once per actual play phase after completing either branch.");
        var hp = game.CreateSnapshot(0).Players[0].Hp;
        Use(game, BoundaryManChongScenario.Driver, "damage-owner", [], [1]);
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:yuce" && prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        Activate(game);
        Reach(game, prompt => prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"));
        Answer(game, choice => choice.Cards.Count == 1); Settle(game);
        Require(game.CreateSnapshot(0).Players[0].Hp == hp - 1,
            "One two-point native damage offers shared Yuce once; an all-equipment source cannot discard a different category, so it recovers exactly one HP.");
        Replay(game, scene.Registry);
    }

    private static bool IsJunxing(PendingDecision prompt) => prompt.SkillPrompt?.SkillId == Skill &&
        prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "boundary-man-chong-junxing");
    private static void Use(GameEngine game, string skill, string binding, int[] cards, int[] targets) =>
        Accept(game, new UseProgramSkillCommand(0, skill, binding, cards, targets, game.Revision, P(game)!.PromptId));
}
