using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class GhostRuleChecks
{
    private const string Driver = "fixture:ghost-rule-driver", Mode = "fixture:ghost-rule-mode";

    public static void EnyuanRequiresHeartsPerDamageAndRewardsActualRecoveryPoints()
    {
        foreach (var suit in new[] { Suit.Heart, Suit.Spade })
        {
            var (g, registry) = Start("ghost:enyuan", suit);
            var actorBefore = V(g, 0).HandCount;
            var targetBefore = V(g, 1).HandCount;
            Use(g, "damage", 1);
            Until(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "hand-repayment") == true);
            Cold(g, registry);
            var prompt = P(g)!;
            Require(prompt.PlayerSeat == 0, "The actual damage source owns the private Enyuan repayment choice.");
            var gifts = prompt.Choices.Where(c => c.Parameters.GetValueOrDefault("repayment") == "give").ToArray();
            Require((gifts.Length > 0) == (suit == Suit.Heart) && gifts.All(c => V(g, 0).Hand.Single(card => card.Id == c.Cards.Single()).Suit == Suit.Heart),
                "Only actual effective-heart hand cards can repay the old ghost Enyuan.");
            Answer(g, c => c.Parameters.GetValueOrDefault("repayment") == (suit == Suit.Heart ? "give" : "lose-hp"));
            Play(g);
            Require(V(g, 1).Hp == 2 && (suit == Suit.Heart
                ? V(g, 0).HandCount == actorBefore - 1 && V(g, 1).HandCount == targetBefore + 1
                : V(g, 0).Hp == 3 && V(g, 0).HandCount == actorBefore),
                "Two damage points form one old Enyuan obligation: give one heart or lose exactly one HP.");
            Require(E<ProgramHandRepaymentResolvedEvent>(g).Count() == 1, "The damage instance must not repay once per damage point.");
            var beforeRecovery = V(g, 0).HandCount;
            Use(g, "recover", 1); Play(g);
            Require(V(g, 1).Hp == 4 && V(g, 0).HandCount == beforeRecovery + 2,
                "Two actually recovered HP reward the other recovery source with two actual cards.");
            Cold(g, registry);
        }
    }

    public static void GhostDeathDiscardsTheKillerThroughNativeZones()
    {
        foreach (var skill in new[] { "ghost:beiming", "ghost:huilei" })
        {
            var (g, registry) = Start(skill, Suit.Heart);
            Use(g, "equip", 0); Play(g);
            UseCard(g, CardKind.Lightning, 0); Play(g);
            var actor = V(g, 0);
            Require(actor.Equipment.Count == 1 && actor.Judgment.Count == 1 && actor.Hand.Count > 0,
                "The killer owns real hand, equipment and judgment entities before the lethal command.");
            var hand = actor.Hand.Select(c => c.Id).ToArray();
            var equipment = actor.Equipment.Single().Id;
            var judgment = actor.Judgment.Single().Id;
            Use(g, "kill", 1); Play(g);
            Require(!V(g, 1).IsAlive && V(g, 0).HandCount == 0, "Both death skills discard the living killer's entire hand.");
            var all = skill == "ghost:huilei";
            Require((V(g, 0).Equipment.Count == 0) == all && V(g, 0).Judgment.Single().Id == judgment,
                "Huilei also discards equipment; judgment cards are unowned and remain after both death skills.");
            var expected = hand.Concat(all ? [equipment] : Array.Empty<int>()).ToArray();
            Require(expected.All(id => g.CardMovements.Any(move => move.CardId == id && move.From.OwnerSeat == 0 &&
                move.To == CardLocation.DiscardPile && move.Reason.Value.Contains("DiscardDeathSourceCards", StringComparison.Ordinal))),
                "Death cleanup uses native, attributable movements of the original physical entities.");
            var receipt = E<DeathSourceCardsDiscardedEvent>(g).Single();
            Require(receipt.OwnerSeat == 1 && receipt.SourceSeat == 0 && receipt.Count == expected.Length,
                "The receipt retains the dead skill owner and actual killer as separate participants.");
            Cold(g, registry);
        }
    }

    public static void GhostJudgmentDiscardAndRedBlackConsequencesUseActualCards()
    {
        {
            var (g, registry) = Start("ghost:guiji", Suit.Heart);
            var delayed = UseCard(g, CardKind.Indulgence, 1); Play(g);
            Require(V(g, 1).Judgment.Single().Id == delayed, "The target receives the actual delayed-card material.");
            Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
            Until(g, () => E<RandomJudgmentDiscardedEvent>(g).Any());
            Require(E<RandomJudgmentDiscardedEvent>(g).Single() is { OwnerSeat: 1 } fact && fact.CardId == delayed &&
                V(g, 1).Judgment.Count == 0 && g.CardMovements.Any(move => move.CardId == delayed &&
                    move.From == CardLocation.Judgment(1) && move.To == CardLocation.DiscardPile),
                "Guiji discards a real judgment-area entity before the delayed trick can resolve.");
            Cold(g, registry);
        }
        {
            var (g, registry) = Start("ghost:xixing", Suit.Heart);
            Use(g, "damage", 1); Play(g);
            Require(V(g, 1).Hp == 2 && g.CreateSnapshot(0).Players.All(player => !player.IsChained),
                "The real Xixing owner is wounded and every potential target is unchained.");
            var turn = g.State.TurnNumber;
            Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
            Play(g);
            Require(E<TurnStartedEvent>(g).Any(e => e.ActorSeat == 1 && e.TurnNumber > turn) && V(g, 1).Hp == 2 &&
                !E<RecoveryAppliedEvent>(g).Any(e => e.SourceSeat == 1 && e.TargetSeat == 1),
                "Mandatory Xixing safely skips its entire sequence without a chained target, so its actual preparation cannot heal the owner.");
            Cold(g, registry);
        }
        foreach (var suit in new[] { Suit.Heart, Suit.Spade })
        {
            var (g, registry) = Start("ghost:moyan", suit);
            var before = V(g, 0).Hp;
            Use(g, "equip", 1); Play(g);
            var equipment = V(g, 1).Equipment.Single().Id;
            Use(g, "discard-mixed", 1);
            Until(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards") == true);
            Answer(g, c => c.Cards.SequenceEqual([equipment]));
            Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards");
            Until(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "red-judgment-fire-target") == true ||
                P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
            if (suit == Suit.Heart)
            {
                Require(P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "red-judgment-fire-target") == true,
                    "A red actual judgment opens target selection after the judgment result is known.");
                Cold(g, registry);
                Answer(g, c => c.Targets.SequenceEqual([0]));
            }
            Play(g);
            Require(V(g, 0).Hp == before - (suit == Suit.Heart ? 1 : 0) &&
                E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 1 && e.TargetSeat == 0 && e.Nature == DamageNature.Fire) == (suit == Suit.Heart ? 1 : 0),
                "A red judgment deals exactly one real fire damage, while a black judgment opens no target and deals none.");
            Require(E<ProgramBindingStartedEvent>(g).Count(e => e.SkillId == "ghost:moyan") == 1,
                "One native mixed hand/equipment loss batch offers Moyan once for that owner, rather than once per source zone.");
            Cold(g, registry);
        }
    }

    private static (GameEngine, ContentRegistry) Start(string skill, Suit suit)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new StandardGhostActivityPackage(), new Fixture(skill, suit));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 17, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
            HumanTeamId = "team:blue", ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 5 }, registry);
        Accept(g, new StartGameCommand()); Until(g, () => P(g)?.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, "fixture:ghost-rule-actor", g.Revision, P(g)!.PromptId)); Play(g);
        return (g, registry);
    }

    private static int UseCard(GameEngine g, CardKind kind, int target)
    {
        var action = g.GetHumanLegalActions().First(a => a.CardId is { } cardId &&
            (a.PlayedCardKind ?? V(g, 0).Hand.FirstOrDefault(c => c.Id == cardId)?.Kind) == kind && a.ConversionSource is null &&
            (kind == CardKind.Lightning || a.TargetSeats.SequenceEqual([target])));
        Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, P(g)!.PromptId,
            action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
        return action.CardId.Value;
    }
    private static void Use(GameEngine g, string id, int target) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], [target], g.Revision, P(g)!.PromptId));
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4)
        .Select(seat => g.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Play(GameEngine g) => Until(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Until(GameEngine g, Func<bool> condition)
    {
        for (var i = 0; i < 120; i++)
        {
            if (condition()) return;
            var p = P(g);
            if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass") == true)
                Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true)
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card");
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Ghost rule fixture did not reach its actual boundary: " + JsonSerializer.Serialize(P(g)));
    }
    private static void Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(Enumerable.Range(0, 4).All(seat => SnapshotJson.Serialize(g.CreateSnapshot(seat)) == SnapshotJson.Serialize(restored.CreateSnapshot(seat))) &&
            JsonSerializer.Serialize(g.CardMovements) == JsonSerializer.Serialize(restored.CardMovements), "The actual private prompt and original physical movements restore from accepted commands.");
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(command); Require(result.Accepted, result.Error?.Message ?? "Ghost rule command rejected."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(string skill, Suit suit) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("ghost-rule-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},
                {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]},
                {"id":"recover","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"selectedTarget","amount":2}]},
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipment"}]},
                {"id":"discard-mixed","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"selectedTarget","amount":2,"zones":["hand","equipment"],"resultBind":"mixed"},{"op":"moveBoundCards","target":"owner","sourceBind":"mixed","destination":"discardPile","awaitMovementTriggers":true}]},
                {"id":"discard-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]}]},
              {"id":"fixture:ghost-rule-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Driver, "fixture:ghost-rule-quiet" }.ToDictionary(id => id, id => new { name = id, description = "Small actual ghost rule fixture." }) }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "Small actual ghost rule fixture.") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new("fixture:ghost-rule-actor", "Actual ghost rule actor", "", Driver));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:ghost-rule-peer-{i}", $"Ghost rule peer {i}", "", skill,
                AdditionalSkillIds: ["fixture:ghost-rule-quiet"]));
            var mainCard = skill is "ghost:beiming" or "ghost:huilei" ? "standard:lightning" :
                skill == "ghost:guiji" ? "standard:indulgence" : "standard:dodge";
            b.AddDeck(new("fixture:ghost-rule-deck", "Uniform judgment colors and real equipment", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 140).Select(i => new ContentDeckPhysicalCard(i < 120 ? mainCard : "standard:crossbow", suit, 7)).ToArray() });
            b.AddMode(new(Mode, "Actual ghost rule fixture", 4, 4, new Dictionary<string, int>(), "fixture:ghost-rule-deck", 4,
                ["fixture:ghost-rule-actor", "fixture:ghost-rule-peer-1", "fixture:ghost-rule-peer-2", "fixture:ghost-rule-peer-3"], ContentModeKind.Team,
                new Dictionary<string, int> { ["team:blue"] = 2, ["team:red"] = 2 }));
        }
    }
}
