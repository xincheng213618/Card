using CardGame.Content.Standard;
using CardGame.Core;

internal static class ActiveSkillChecks
{
    public static void KujinFlow()
    {
        var registry = StandardContentRegistry.CreateWithActiveSkills();
        var game = FindAtPlay(registry, "standard:kujin");
        var before = game.CreateSnapshot(0, revealAll: true).Players[0];
        var invalid = Activate(game, "standard:kujin", "lose-hp-and-draw", [before.Hand[0].Id], []);
        Require(!invalid.Accepted, "Cardless Kujin accepted an extraneous card.");
        var used = Activate(game, "standard:kujin", "lose-hp-and-draw", [], []);
        Require(used.Accepted, used.Error?.Message ?? "Kujin activation failed.");
        var after = game.CreateSnapshot(0, revealAll: true).Players[0];
        Require(after.Hp == before.Hp - 1 && after.HandCount == before.HandCount + 2,
            "Kujin must lose one HP and draw two cards.");
        Require(game.Events.Select(item => item.Payload).OfType<ProgramSkillHpLostEvent>()
                .Any(item => item.SkillId == "standard:kujin" && item.Amount == 1) &&
                game.Events.Select(item => item.Payload).OfType<ProgramSkillResolvedEvent>()
                    .Any(item => item.SkillId == "standard:kujin" && item.Completed),
            "Kujin must publish its Program HP and completion events.");
        ReplayEquals(game, registry);
        ZhihengAndRendeFlow(registry);
    }

    private static void ZhihengAndRendeFlow(ContentRegistry registry)
    {
        var zhiheng = FindAtPlay(registry, "standard:zhiheng");
        var beforeExchange = zhiheng.CreateSnapshot(0, revealAll: true).Players[0];
        var cards = beforeExchange.Hand.Take(2).Select(card => card.Id).ToArray();
        Require(!Activate(zhiheng, "standard:zhiheng", "discard-and-draw",
                [cards[0], cards[0]], []).Accepted, "Zhiheng accepted duplicate cards.");
        var exchanged = Activate(zhiheng, "standard:zhiheng", "discard-and-draw", cards, []);
        Require(exchanged.Accepted, exchanged.Error?.Message ?? "Zhiheng activation failed.");
        Require(zhiheng.CreateSnapshot(0, revealAll: true).Players[0].HandCount == beforeExchange.HandCount &&
                cards.All(id => zhiheng.CardMovements.Any(move =>
                    move.CardId == id && move.To == CardLocation.DiscardPile)),
            "Zhiheng must exchange exactly its two selected cards.");
        ReplayEquals(zhiheng, registry);

        var rende = FindAtPlay(registry, "standard:rende");
        var beforeGift = rende.CreateSnapshot(0, revealAll: true);
        var gift = beforeGift.Players[0].Hand.Take(2).Select(card => card.Id).ToArray();
        var target = beforeGift.Players.First(player => player.IsAlive && player.Seat != 0).Seat;
        Require(!Activate(rende, "standard:rende", "give-and-heal-recipient", gift, [0]).Accepted,
            "Rende accepted its owner as gift target.");
        var given = Activate(rende, "standard:rende", "give-and-heal-recipient", gift, [target]);
        Require(given.Accepted, given.Error?.Message ?? "Rende activation failed.");
        var afterGift = rende.CreateSnapshot(0, revealAll: true);
        Require(afterGift.Players[0].HandCount == beforeGift.Players[0].HandCount - 2 &&
                afterGift.Players[target].HandCount == beforeGift.Players[target].HandCount + 2 &&
                gift.All(id => afterGift.Players[target].Hand.Any(card => card.Id == id)),
            "Rende must transfer the exact two selected physical cards.");
        ReplayEquals(rende, registry);
    }

    private static GameEngine FindAtPlay(ContentRegistry registry, string skillId)
    {
        for (var seed = 1; seed <= 4096; seed++)
        {
            var game = Create(registry, seed);
            if (HasSkill(game, skillId) && game.Submit(new StartGameCommand()).Accepted &&
                game.PendingDecision?.Kind == DecisionKind.PlayCard &&
                game.GetHumanLegalActions().Any(action =>
                    action.Kind == LegalActionKind.UseProgramSkill && action.ProgramSkillId == skillId))
                return game;
        }
        throw new InvalidOperationException($"No deterministic {skillId} play fixture found.");
    }

    private static bool HasSkill(GameEngine game, string skillId) =>
        game.CreateSnapshot(0, revealAll: true).Players[0].Skills?.Any(skill =>
            skill.ContentId == skillId) == true;

    private static GameEngine Create(ContentRegistry registry, int seed) => GameEngine.CreateStandard(
        new GameOptions
        {
            Seed = seed, PlayerCount = 5, ModeId = "identity:active-skills-5",
            HumanSeat = 0, HumanRole = Role.Lord, UseInteractiveSetup = false,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = true,
            AiPolicyVersion = 2
        }, registry);

    private static CommandResult Activate(GameEngine game, string skillId, string activationId,
        IReadOnlyList<int> cards, IReadOnlyList<int> targets)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("No activation prompt.");
        return game.Submit(new UseProgramSkillCommand(0, skillId, activationId,
            cards, targets, game.Revision, prompt.PromptId));
    }

    private static void ReplayEquals(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
            "Program active flow did not replay exact state.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
