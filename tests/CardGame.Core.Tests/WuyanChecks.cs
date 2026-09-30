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
        Require(GameEngine.CanWuyanPreventDamage(true, CardKind.Duel, true, false) &&
                GameEngine.CanWuyanPreventDamage(true, CardKind.FireAttack, false, true) &&
                !GameEngine.CanWuyanPreventDamage(false, CardKind.Duel, true, false) &&
                !GameEngine.CanWuyanPreventDamage(true, CardKind.Slash, true, true),
            "Wuyan must be versioned to rules v77, classic identity, trick damage, and either participating owner.");

        var registry = StandardContentRegistry.CreateWithClassicGenerals();
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

    public static void QianxiFrozenDiscardColorSurvivesLuoying()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
            new QianxiLuoyingScenario());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 1, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 5,
            ModeId = "identity:classic-qianxi-luoying", UseInteractiveSetup = true,
            UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 100
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "The fixed fixture setup must start.");
        Require(game.Submit(new SelectGeneralCommand(0, "fixture:qianxi-bank-0", game.Revision, game.PendingDecision!.PromptId)).Accepted, "The fixed fixture must select its registered human general.");
        var baseline = game.CreateCheckpoint();
        for (var step = 0; step < 600; step++)
        {
            var frame = game.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.SkillId == "classic:qianxi");
            var binding = frame?.CardSetBindings.SingleOrDefault(b => b.Name == "discarded");
            if (binding?.FrozenRevealedSuit is { } suit && game.CardMovements.Any(m => m.CardId == binding.CardIds.Single() && m.Reason.Value == "skill-program.classic:luoying.ClaimMovedCards"))
            {
                var replay = GameReplay.Restore(game.CreateCheckpoint(), registry);
                var command = BuildCommand(game)!;
                foreach (var branch in new[] { game, replay })
                {
                    Require(branch.Submit(command).Accepted, "Target selection after Luoying must resume Qianxi.");
                    var granted = branch.Events.Select(e => e.Payload).OfType<HandCardColorRestrictionGrantedEvent>().Last();
                    Require(granted.Restriction.IsRed == (suit is Suit.Heart or Suit.Diamond), "Qianxi must use its discarded card's captured color.");
                }
                var journalReplay = GameReplay.Restore(baseline, registry);
                foreach (var recorded in game.CreateCheckpoint().Commands.Skip(baseline.Commands.Count))
                    Require(journalReplay.Submit(recorded).Accepted, "Recorded Qianxi commands must replay.");
                foreach (var branch in new[] { replay, journalReplay })
                {
                    Require(SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(branch.CreateSnapshot(0, true)), "Qianxi frozen-color checkpoints must match.");
                    Require(game.Events.Select(e => System.Text.Json.JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).SequenceEqual(branch.Events.Select(e => System.Text.Json.JsonSerializer.Serialize(e.Payload, e.Payload.GetType()))), "Qianxi typed events must replay.");
                    Require(System.Text.Json.JsonSerializer.Serialize(game.CardMovements) == System.Text.Json.JsonSerializer.Serialize(branch.CardMovements), "Qianxi physical card movement must replay.");
                }
                return;
            }
            Require(game.Submit(BuildCommand(game)!).Accepted, "The fixed fixture must reach the Qianxi/Luoying interaction.");
        }
        throw new InvalidOperationException("The fixed Qianxi/Luoying fixture did not reach the actual discarded-card claim.");
    }

    private sealed class QianxiLuoyingScenario : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("qianxi-luoying-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

        public void Register(IContentRegistryBuilder builder)
        {
            var pool = new List<string> { "classic:ma-dai", "classic:cao-zhi" };
            for (var index = 0; index < 3; index++)
            {
                var id = "fixture:qianxi-bank-" + index;
                pool.Add(id);
                builder.AddGeneral(new(id, "测试对手", "supporter", "standard:none", "qun"));
            }
            builder.AddDeck(new("fixture:qianxi-luoying-deck", "潜袭落英测试牌堆", 4, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 160)
                    .Select(index => new ContentDeckPhysicalCard("standard:slash", Suit.Club, index % 13 + 1)).ToArray()
            });
            builder.AddMode(new("identity:classic-qianxi-luoying", "潜袭落英测试", 5, 5,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 },
                "fixture:qianxi-luoying-deck", GeneralCandidateCount: 5, GeneralPoolIds: pool));
        }
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
            "test:xu-shu-wuyan", "徐庶", "xu_shu",
            [new GeneralSkillDefinition("无言",
                "锁定技，当锦囊牌造成伤害时，若你为来源或受伤角色，防止此伤害。")
            { ContentId = "classic:wuyan" }], "shu", BaseHp: 3));
        Require(((CharacterState)player).SkillGrants.EffectiveSkillIds.Contains("classic:wuyan"),
            "The Wuyan fixture must grant the registered skill identity, not only legacy template metadata.");
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
