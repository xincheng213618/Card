using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class PangTongChecks
{
    public static void LianhuanNiepanAndReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        VerifyLianhuan(registry);
    }

    private static void VerifyLianhuan(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = Create(seed, registry);
            if (!SelectPangTong(game)) continue;
            var play = Reach(game, DecisionKind.PlayCard, 64);
            if (play is null) continue;
            var snapshot = game.CreateSnapshot(0);
            var actions = game.GetHumanLegalActions();
            var converted = actions.FirstOrDefault(action =>
                action.Kind == LegalActionKind.Recast && action.PlayedCardKind == CardKind.IronChain &&
                action.CardId is { } id && snapshot.Players[0].Hand.Any(card =>
                    card.Id == id && card.Kind != CardKind.IronChain && card.Suit == Suit.Club));
            if (converted is null) continue;

            var physicalId = converted.CardId!.Value;
            var branch = game.CreateCheckpoint();
            Require(converted.ConversionSource is
                    { SkillId: "classic:lianhuan", BindingId: "club-hand-as-iron-chain" },
                "Lianhuan recast must publish its configured conversion source.");
            var rejected = game.Submit(new RecastCardCommand(0, physicalId, game.Revision, play.PromptId));
            Require(!rejected.Accepted && game.Revision == branch.Revision,
                "A converted recast without its published source must be rejected.");
            var recast = game.Submit(new RecastCardCommand(0, physicalId, game.Revision, play.PromptId)
            { ConversionSource = converted.ConversionSource });
            Require(recast.Accepted && game.Events.Any(item => item.Payload is CardRecastEvent evt &&
                    evt.CardId == physicalId && evt.CardKind == CardKind.IronChain),
                recast.Error?.Message ?? "Lianhuan recast was rejected.");
            var restoredRecast = GameReplay.Restore(game.CreateCheckpoint(), registry);
            Require(SnapshotJson.Serialize(restoredRecast.CreateSnapshot(0, revealAll: true)) ==
                    SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
                "Lianhuan recast must replay exactly.");

            var use = GameReplay.Restore(branch, registry);
            var ironChain = use.GetHumanLegalActions().First(action =>
                action.Kind == LegalActionKind.IronChain && action.CardId == physicalId &&
                action.PlayedCardKind == CardKind.IronChain);
            var used = use.Submit(new PlayCardCommand(0, physicalId, ironChain.TargetSeats, use.Revision,
                use.PendingDecision!.PromptId, CardKind.IronChain)
            { ConversionSource = ironChain.ConversionSource });
            Require(used.Accepted && use.Events.Any(item => item.Payload is CardUseDeclaredEvent evt &&
                    evt.CardId == physicalId && evt.CardKind == CardKind.IronChain),
                used.Error?.Message ?? "Lianhuan use was rejected.");
            var lianhuanFrames = use.ResolutionStack.OfType<CardUseFrame>().ToArray();
            if (lianhuanFrames.Length == 0)
            {
                // Nobody paused the resolved Iron Chain (no nullification holder), so
                // there is no pending card action to inspect; try the next fixture seed.
                continue;
            }
            Require(lianhuanFrames.Any(frame =>
                    frame.Action?.ConversionChain.SequenceEqual([ironChain.ConversionSource!]) == true),
                "Lianhuan use must retain the selected skill instance in its pending card action.");
            var restoredUse = GameReplay.Restore(use.CreateCheckpoint(), registry);
            Require(SnapshotJson.Serialize(restoredUse.CreateSnapshot(0, revealAll: true)) ==
                    SnapshotJson.Serialize(use.CreateSnapshot(0, revealAll: true)),
                "Lianhuan use must replay exactly.");
            return;
        }
        throw new InvalidOperationException("No bounded Pang Tong fixture exposed a club Lianhuan conversion.");
    }

    private static GameEngine Create(int seed, ContentRegistry registry) => GameEngine.CreateStandard(new GameOptions
    {
        Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 5,
        ModeId = "identity:classic-5", UseInteractiveSetup = true,
        UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 100
    }, registry);

    private static bool SelectPangTong(GameEngine game)
    {
        if (!game.Submit(new StartGameCommand()).Accepted) return false;
        var selection = game.PendingDecision;
        return selection is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } &&
               selection.ValidContentIds.Contains("classic:pang-tong") &&
               game.Submit(new SelectGeneralCommand(0, "classic:pang-tong", game.Revision, selection.PromptId)).Accepted;
    }

    private static PendingDecision? Reach(GameEngine game, DecisionKind kind, int limit)
    {
        for (var step = 0; step < limit; step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0 } pending && pending.Kind == kind) return pending;
            if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted) return null;
        }
        return null;
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
