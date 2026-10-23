using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class YangYiChecks
{
    private const string Juanxia = "ol:juanxia", Dingcuo = "ol:dingcuo";
    private const string Mode = "identity:classic-juanxia-regression", Deck = "fixture:juanxia-deck";

    public static void VirtualDuelDamageBothDirectionsAndColdReturn()
    {
        foreach (var responds in new[] { false, true })
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(responds));
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
                UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false
            }, registry);
            Accept(game, new StartGameCommand());
            Reach(game, p => p.Kind == DecisionKind.SelectGeneral);
            Accept(game, new SelectGeneralCommand(0, "ol:yang-yi", game.Revision, P(game)!.PromptId));
            Reach(game, p => p.Kind == DecisionKind.PlayCard);
            var hp = game.State.Players.Select(p => p.Hp).ToArray();
            Accept(game, new EndPlayPhaseCommand(0, game.Revision, P(game)!.PromptId));
            Reach(game, p => p.SkillPrompt?.SkillId == Juanxia && Has(p, "activate"));
            Answer(game, c => Action(c) == "activate");
            Reach(game, p => Has(p, "select-target"));
            Answer(game, c => c.Targets.SequenceEqual([1]));
            Reach(game, p => Has(p, "juanxia-trick"));
            Answer(game, c => c.Parameters.GetValueOrDefault("trick") == nameof(CardKind.Duel));
            if (responds)
            {
                Reach(game, p => p is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0 });
                Require(Facts<DuelResponseEvent>(game).Any(e => e.ResponderSeat == 1 && e.UsedSlash),
                    "The target really pays a physical Slash before the original user declines its own response.");
                game = Cold(game, registry);
                Answer(game, c => c.Cards.Count == 0);
            }
            Reach(game, p => p.SkillPrompt?.SkillId == Dingcuo && Has(p, "activate"));
            var owner = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.JuanxiaLaunch is not null);
            var victim = responds ? 0 : 1;
            Require(owner.JuanxiaLaunch is { Stage: JuanxiaStage.Child, UsedCount: 1 } &&
                owner.CardAttack is { Active: true, PhysicalCardIds.Count: 0, CardId: null } attack &&
                attack.ProgramUseInstructionIndex == owner.InstructionIndex &&
                Facts<DamageAppliedEvent>(game).Count(e => e.SourceSeat == (responds ? 1 : 0) && e.TargetSeat == victim && e.Amount == 1) == 1 &&
                game.State.Players[victim].Hp == hp[victim] - 1,
                "The cardless Juanxia Duel retains its exact parent across the real damage aftermath in both directions.");
            var hand = game.State.Players[0].HandCount;
            game = Cold(game, registry);
            Answer(game, c => Action(c) == "activate");
            Reach(game, p => Has(p, "juanxia-trick"));
            Require(game.State.Players[0].HandCount == hand + 2 && game.State.Players[victim].Hp == hp[victim] - 1,
                "Dingcuo draws exactly twice and returns to the suspended Juanxia chooser without repeating damage.");
            game = Cold(game, registry);
            Answer(game, c => c.Parameters.GetValueOrDefault("trick") == "stop");
            Require(Facts<ProgramJuanxiaDebtEvent>(game).Single() is { OwnerSeat: 0, TargetSeat: 1, TrickCount: 1 } &&
                Facts<ProgramJuanxiaTrickUsedEvent>(game).Count(e => e.TrickKind == nameof(CardKind.Duel)) == 1 &&
                game.ResolutionStack.OfType<ProgramSkillFrame>().All(f => f.JuanxiaLaunch is null),
                "The returned launch records its single real Duel and exactly one retaliation debt, then completes.");
            _ = Cold(game, registry);
        }
    }

    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 4)
        .Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] Facts<T>(GameEngine game) where T : IGameEvent => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static string? Action(PromptChoice choice) => choice.Parameters.GetValueOrDefault("program-action");
    private static bool Has(PendingDecision p, string action) => p.Choices.Any(c => Action(c) == action);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Require(result.Accepted, result.Error?.Message ?? "The real Juanxia command was rejected.");
    }
    private static void Answer(GameEngine game, Func<PromptChoice, bool> select)
    {
        var p = P(game)!;
        Accept(game, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(select).Id, game.Revision));
    }
    private static void Reach(GameEngine game, Func<PendingDecision, bool> stop)
    {
        for (var step = 0; step < 80; step++)
        {
            var p = P(game);
            if (p is not null && stop(p)) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Missing Juanxia boundary: " + JsonSerializer.Serialize(P(game)));
    }
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(SnapshotJson.Serialize(copy.CreateSnapshot(0)) == SnapshotJson.Serialize(game.CreateSnapshot(0)) &&
            JsonSerializer.Serialize(copy.ResolutionStack) == JsonSerializer.Serialize(game.ResolutionStack),
            "Cold replay preserves the real Duel, damage child and suspended ending program.");
        return copy;
    }

    private sealed class Fixture(bool responds) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-juanxia-regression", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "ol:yang-yi");
            builder.AddSkill(new("fixture:juanxia-peer", "无技能角色", "固定受击角色"));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:juanxia-peer-{i}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "其他角色", "", "fixture:juanxia-peer", "qun", responds ? 1 : 4));
            builder.AddDeck(new(Deck, "固定实体牌堆", 2, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard(
                    responds ? "standard:slash" : "standard:crossbow", Suit.Club, 7)).ToArray()
            });
            builder.AddMode(new(Mode, "狷狭回归", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                Deck, GeneralCandidateCount: 4, GeneralPoolIds: ["ol:yang-yi", .. peers]));
        }
    }
}
