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

    public static void KujinDyingContinuation()
    {
        var registry = StandardContentRegistry.CreateWithActiveSkills();
        GameEngine? selected = null;
        for (var seed = 1; seed <= 512 && selected is null; seed++)
        {
            var game = Create(registry, seed);
            if (!HasSkill(game, "standard:kujin") || !game.Submit(new StartGameCommand()).Accepted)
                continue;
            var reachedOneHp = true;
            for (var use = 0; use < 4; use++)
                if (game.PendingDecision?.Kind != DecisionKind.PlayCard ||
                    !Activate(game, "standard:kujin", "lose-hp-and-draw", [], []).Accepted)
                {
                    reachedOneHp = false;
                    break;
                }
            var owner = game.CreateSnapshot(0, revealAll: true).Players[0];
            if (reachedOneHp && owner.Hp == 1 && owner.Hand.Any(card => card.Kind == CardKind.Peach))
                selected = game;
        }
        var fixture = selected ?? throw new InvalidOperationException("No replayable Kujin rescue fixture found.");
        var before = fixture.CreateSnapshot(0, revealAll: true).Players[0];
        var used = Activate(fixture, "standard:kujin", "lose-hp-and-draw", [], []);
        Require(used.Accepted, used.Error?.Message ?? "Lethal Kujin activation failed.");
        var dying = fixture.PendingDecision ?? throw new InvalidOperationException("Kujin did not pause for rescue.");
        Require(dying.Kind == DecisionKind.RescueDying &&
                fixture.ResolutionStack.OfType<ProgramSkillFrame>().Any() &&
                fixture.ResolutionStack.OfType<DyingFrame>().Any(),
            "The Program frame must remain beneath the rescue window.");
        ReplayEquals(fixture, registry);
        var peach = dying.Choices.First(choice => choice.Parameters.GetValueOrDefault("response") == "peach");
        var rescue = fixture.Submit(new AnswerPromptCommand(0, dying.PromptId, peach.Id, fixture.Revision));
        Require(rescue.Accepted, rescue.Error?.Message ?? "Kujin Peach rescue failed.");
        var after = fixture.CreateSnapshot(0, revealAll: true).Players[0];
        Require(after.Hp == 1 && after.HandCount == before.HandCount + 1 &&
                fixture.ResolutionStack.Count == 0,
            "Rescued Kujin must resume its two-card draw and release every frame.");
        ReplayEquals(fixture, registry);
    }

    public static void QingnangFlow()
    {
        var registry = StandardContentRegistry.CreateWithActiveSkills();
        var game = FindWoundedPlay(registry, "standard:qingnang", 1);
        var before = game.CreateSnapshot(0, revealAll: true);
        var wounded = before.Players.First(player => player.IsAlive && player.Hp < player.MaxHp);
        var full = before.Players.First(player => player.IsAlive && player.Hp == player.MaxHp);
        var card = before.Players[0].Hand[0].Id;
        Require(!Activate(game, "standard:qingnang", "discard-and-heal", [card], [full.Seat]).Accepted,
            "Qingnang accepted an unwounded target.");
        var accepted = Activate(game, "standard:qingnang", "discard-and-heal", [card], [wounded.Seat]);
        Require(accepted.Accepted, accepted.Error?.Message ?? "Qingnang activation failed.");
        var after = game.CreateSnapshot(0, revealAll: true);
        Require(after.Players[wounded.Seat].Hp == wounded.Hp + 1 &&
                after.Players[0].HandCount == before.Players[0].HandCount - 1 &&
                game.CardMovements.Any(move => move.CardId == card && move.To == CardLocation.DiscardPile),
            "Qingnang must spend its selected hand card and heal exactly one wounded target.");
        ReplayEquals(game, registry);
    }

    public static void HuichunFlow()
    {
        var registry = StandardContentRegistry.CreateWithActiveSkills();
        var game = FindWoundedPlay(registry, "standard:huichun", 2);
        var before = game.CreateSnapshot(0, revealAll: true);
        var cards = before.Players[0].Hand.Take(2).Select(card => card.Id).ToArray();
        var targets = before.Players.Where(player => player.IsAlive && player.Hp < player.MaxHp)
            .Take(2).Select(player => player.Seat).ToArray();
        Require(!Activate(game, "standard:huichun", "discard-two-heal-many", cards,
                [targets[0]]).Accepted,
            "Huichun accepted fewer than two wounded targets.");
        var accepted = Activate(game, "standard:huichun", "discard-two-heal-many", cards, targets);
        Require(accepted.Accepted, accepted.Error?.Message ?? "Huichun activation failed.");
        var after = game.CreateSnapshot(0, revealAll: true);
        Require(targets.All(seat => after.Players[seat].Hp == before.Players[seat].Hp + 1) &&
                after.Players[0].HandCount == before.Players[0].HandCount - 2 &&
                cards.All(cardId => game.CardMovements.Any(move =>
                    move.CardId == cardId && move.To == CardLocation.DiscardPile)),
            "Huichun must discard two physical cards and heal each selected target.");
        ReplayEquals(game, registry);
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

    private static GameEngine FindWoundedPlay(ContentRegistry registry, string skillId, int requiredWounded)
    {
        for (var seed = 1; seed <= 8192; seed++)
        {
            var game = Create(registry, seed);
            if (!HasSkill(game, skillId) || !game.Submit(new StartGameCommand()).Accepted)
                continue;
            for (var turn = 0; turn < 24 && game.State.Status != EngineStatus.Completed; turn++)
            {
                if (game.PendingDecision?.Kind != DecisionKind.PlayCard) break;
                var snapshot = game.CreateSnapshot(0, revealAll: true);
                if (snapshot.Players.Count(player => player.IsAlive && player.Hp < player.MaxHp) >= requiredWounded &&
                    game.GetHumanLegalActions().Any(action =>
                        action.Kind == LegalActionKind.UseProgramSkill && action.ProgramSkillId == skillId))
                    return game;
                var slash = game.GetHumanLegalActions().FirstOrDefault(action =>
                    action.Kind == LegalActionKind.Slash && action.CardId is not null &&
                    action.ConversionSource is null && action.TargetSeats.Count == 1 &&
                    snapshot.Players[action.TargetSeats[0]].Hp == snapshot.Players[action.TargetSeats[0]].MaxHp);
                GameCommand command = slash is null
                    ? new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision.PromptId)
                    : new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats,
                        game.Revision, game.PendingDecision.PromptId, slash.PlayedCardKind, slash.TargetCardId);
                if (!game.Submit(command).Accepted) break;
            }
        }
        throw new InvalidOperationException($"No deterministic {skillId} wounded-target fixture found.");
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
