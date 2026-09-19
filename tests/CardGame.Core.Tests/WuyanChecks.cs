using System.Collections;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class WuyanChecks
{
    public static void PreventsTrickDamageWithVersionBoundary()
    {
        Require(GameEngine.IsTrickCardDamage(CardKind.Duel) &&
                GameEngine.IsTrickCardDamage(CardKind.FireAttack) &&
                GameEngine.IsTrickCardDamage(CardKind.Lightning) &&
                !GameEngine.IsTrickCardDamage(CardKind.Slash),
            "Wuyan must recognize immediate and delayed trick damage without treating Slash as trick damage.");
        Require(GameEngine.CanWuyanPreventDamage(77, true, CardKind.Duel, true, false) &&
                GameEngine.CanWuyanPreventDamage(77, true, CardKind.FireAttack, false, true) &&
                !GameEngine.CanWuyanPreventDamage(76, true, CardKind.Duel, true, false) &&
                !GameEngine.CanWuyanPreventDamage(77, false, CardKind.Duel, true, false) &&
                !GameEngine.CanWuyanPreventDamage(77, true, CardKind.Slash, true, true),
            "Wuyan must be versioned to rules v77, classic identity, trick damage, and either participating owner.");

        var registry = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 62, 0));
        var sourceVerified = false;
        var targetVerified = false;
        for (var seed = 1; seed <= 4096 && (!sourceVerified || !targetVerified); seed++)
        {
            var game = Create(seed, registry);
            if (!SelectAndGrantWuyan(game)) continue;
            for (var step = 0; step < 900 && game.State.Winner == Winner.None; step++)
            {
                var command = BuildCommand(game);
                if (command is null) break;
                var hpBefore = game.CreateSnapshot(0, revealAll: true).Players.Select(player => player.Hp).ToArray();
                var eventCount = game.Events.Count;
                try
                {
                    if (!game.Submit(command).Accepted) break;
                }
                catch (InvalidOperationException exception) when (
                    exception.Message.StartsWith("Unknown or unsupported turn phase:", StringComparison.Ordinal) ||
                    exception.Message.Contains("is not a CardUse frame", StringComparison.Ordinal))
                {
                    break;
                }
                var prevented = game.Events.Skip(eventCount).Select(item => item.Payload)
                    .OfType<WuyanDamagePreventedEvent>().FirstOrDefault(item => item.SkillOwnerSeat == 0);
                if (prevented is null) continue;

                Require(prevented.PreventedAmount > 0 && GameEngine.IsTrickCardDamage(prevented.TrickCard) &&
                        game.CreateSnapshot(0, revealAll: true).Players[prevented.TargetSeat].Hp == hpBefore[prevented.TargetSeat],
                    "Wuyan must prevent the full trick-card damage amount before HP changes.");
                sourceVerified |= prevented.SourceSeat == 0;
                targetVerified |= prevented.TargetSeat == 0;
                break;
            }
        }
        Require(sourceVerified && targetVerified,
            "Bounded fixtures must verify Wuyan for Xu Shu as both trick-damage source and target.");
    }

    private static GameEngine Create(int seed, ContentRegistry registry) => GameEngine.CreateStandard(new GameOptions
    {
        Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 5,
        ModeId = "identity:classic-5", UseInteractiveSetup = true, UseInteractiveDiscard = true,
        AdvanceAfterHumanCommands = false, MaxTurns = 100
    }, registry);

    private static bool SelectAndGrantWuyan(GameEngine game)
    {
        if (!game.Submit(new StartGameCommand()).Accepted ||
            game.PendingDecision is not { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } setup)
            return false;
        var generalId = setup.ValidContentIds[0];
        if (!game.Submit(new SelectGeneralCommand(0, generalId, game.Revision, setup.PromptId)).Accepted)
            return false;
        GrantWuyan(game, 0);
        return true;
    }

    private static void GrantWuyan(GameEngine game, int seat)
    {
        var players = ((IEnumerable)typeof(GameEngine)
            .GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(game)!).Cast<object>().ToArray();
        var player = players[seat];
        player.GetType().GetProperty("General")!.SetValue(player, new GeneralDefinition(
            "test:xu-shu-wuyan", "徐庶", "xu_shu", SkillKind.Wuyan, "无言",
            "锁定技，当锦囊牌造成伤害时，若你为来源或受伤角色，防止此伤害。", "shu", BaseHp: 3));
    }

    private static GameCommand? BuildCommand(GameEngine game)
    {
        if (game.PendingDecision is not { } pending || pending.PlayerSeat != 0)
            return new AdvanceOneStepCommand(game.Revision);
        if (pending.Kind == DecisionKind.PlayCard)
        {
            var trick = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.CardId is not null && action.PlayedCardKind is
                    CardKind.Duel or CardKind.FireAttack or CardKind.BarbarianAssault or CardKind.ArrowBarrage);
            return trick is not null
                ? new PlayCardCommand(0, trick.CardId!.Value, trick.TargetSeats, game.Revision,
                    pending.PromptId, trick.PlayedCardKind)
                : new EndPlayPhaseCommand(0, game.Revision, pending.PromptId);
        }
        if (pending.Kind == DecisionKind.DiscardCards)
            return new DiscardCardsCommand(0, pending.ValidCardIds.Take(pending.RequiredCardCount).ToArray(),
                pending.PromptId, game.Revision);
        var choice = pending.Choices.FirstOrDefault(item => item.Parameters.Values.Any(value =>
            value.Contains("skip", StringComparison.Ordinal) ||
            value.Contains("decline", StringComparison.Ordinal) ||
            value == "take-damage")) ?? pending.Choices.LastOrDefault();
        return choice is null ? null : new AnswerPromptCommand(0, pending.PromptId, choice.Id, game.Revision);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
