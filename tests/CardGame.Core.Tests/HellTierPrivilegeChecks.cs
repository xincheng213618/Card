using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class HellTierPrivilegeChecks
{
    private const string Mode = "fixture:hell-tier-privileges", Owner = "fixture:hell-tier-owner";
    private const string Driver = "fixture:hell-tier-driver", Quiet = "fixture:hell-tier-quiet";
    private const string Limited = "fixture:hell-tier-limited", DrawWatch = "fixture:hell-tier-native-draw";
    private const string ArrivalWatch = "fixture:hell-tier-native-arrival";

    public static void ChongshengPreservesHandEquipmentAndLimitedLedgerAcrossColdDraw()
    {
        var (g, registry) = Start(tier: 1, rebirth: true);
        Need(V(g, 0).MaxHp == 6 && V(g, 0).Equipment.Count == 1 &&
            Facts<SkillUsageConsumedEvent>(g).Count(e => e.SkillOwnerSeat == 0 && e.SkillId == Limited) == 1,
            "The real Hell metadata supplies the tier-five player's health and physical arrival equipment, with a previously consumed independent limited skill.");
        var lightning = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Lightning);
        Accept(g, new PlayCardCommand(0, lightning.CardId!.Value, lightning.TargetSeats, g.Revision, P(g)!.PromptId)); Play(g);
        var judgment = V(g, 0).Judgment.Single().Id;
        var discard = V(g, 0).Hand.Skip(2).Select(c => c.Id).ToArray();
        Use(g, "trim", discard); Play(g);
        Use(g, "back"); Play(g);
        var before = V(g, 0);
        var hand = before.Hand.Select(c => c.Id).ToArray();
        var equipment = before.Equipment.Select(c => c.Id).ToArray();
        Need(before is { Hp: 6, HandCount: 2, IsFaceDown: true, IsChained: true } && before.Judgment.Count == 1,
            "Real card use and native face/chain effects prepare two retained hand cards, actual equipment and a real delayed trick before dying.");
        var movementStart = g.CardMovements.Count;
        Use(g, "empty-hp");
        Until(g, () => P(g) is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 } prompt &&
            prompt.Choices.Any(IsChongsheng));
        Cold(g, registry);
        Answer(g, IsChongsheng);
        Until(g, () => P(g) is { PlayerSeat: 0, SkillPrompt.SkillId: DrawWatch } &&
            g.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any(f => f.Context.Reason.Value == "hell.chongsheng.refill"));
        Need(V(g, 0) is { Hp: 0, IsFaceDown: false, IsChained: false, HandCount: 2 } &&
            V(g, 0).Judgment.Count == 0 && V(g, 0).Equipment.Select(c => c.Id).SequenceEqual(equipment) &&
            V(g, 0).Hand.Select(c => c.Id).SequenceEqual(hand) &&
            Facts<HellChongshengRefillIssuedEvent>(g).Single() is { OwnerSeat: 0, RequestedCount: 3 } &&
            g.CardMovements.Skip(movementStart).Count(m => m.CardId == judgment &&
                m.From == CardLocation.Judgment(0) && m.To == CardLocation.DiscardPile) == 1 &&
            !Facts<RecoveryAppliedEvent>(g).Any(e => e.TargetSeat == 0),
            "Rebirth first discards only the real judgment card and restores face/chain; its exact three-card native draw child pauses before any refill or full recovery.");
        Cold(g, registry); Play(g);
        var after = V(g, 0);
        Need(after.Hp == after.MaxHp && after.MaxHp == 6 && after.HandCount == 5 &&
            after.Equipment.Select(c => c.Id).SequenceEqual(equipment) && hand.All(id => after.Hand.Any(c => c.Id == id)) &&
            !g.CardMovements.Skip(movementStart).Any(m => hand.Concat(equipment).Contains(m.CardId)) &&
            g.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.Reason.Value == "hell.chongsheng.refill") == 3 &&
            Facts<RecoveryAppliedEvent>(g).Single(e => e.TargetSeat == 0) is { Amount: 6, RemainingHp: 6 } &&
            Facts<SkillUsageConsumedEvent>(g).Count(e => e.SkillOwnerSeat == 0 && e.SkillId == "hell:chongsheng") == 1 &&
            Facts<SkillUsageConsumedEvent>(g).Count(e => e.SkillOwnerSeat == 0 && e.SkillId == Limited) == 1,
            "The returned native draw fills to min(maximum HP, five) exactly once before recovering to full, while every retained HE entity and both original limited ledgers survive.");
        Cold(g, registry);
        Use(g, "empty-hp");
        Until(g, () => !V(g, 0).IsAlive);
        Need(Facts<PlayerDyingEvent>(g).Count(e => e.VictimSeat == 0) == 2 &&
            Facts<PlayerDiedEvent>(g).Count(e => e.VictimSeat == 0) == 1 &&
            Facts<HellChongshengRefillIssuedEvent>(g).Length == 1 &&
            Facts<SkillUsageConsumedEvent>(g).Count(e => e.SkillOwnerSeat == 0 && e.SkillId == "hell:chongsheng") == 1 &&
            Facts<SkillUsageConsumedEvent>(g).Count(e => e.SkillOwnerSeat == 0 && e.SkillId == Limited) == 1,
            "The second genuine dying window completes a real death without refreshing either limited ledger or issuing a second rebirth refill.");
        Cold(g, registry);
    }

    public static void TierEquipmentAndLateFifthTierAssaultUseRealNativeCardsOnce()
    {
        foreach (var tier in Enumerable.Range(2, 4))
        {
            var (g, registry) = Start(tier, rebirth: false);
            Need(Enumerable.Range(0, 4).All(seat => V(g, seat).Equipment.Count == 1 &&
                    Facts<CardUseDeclaredEvent>(g).Count(e => e.SourceSeat == seat && e.CardKind == CardKind.Crossbow) == 1) &&
                Enumerable.Range(0, 3).All(seat => Facts<ProgramBindingResolvedEvent>(g).Count(e =>
                    e.OwnerSeat == seat && e.SkillId == "hell:player-tier" && e.BindingId == "arrival-equipment" && e.Completed) == 1) &&
                Facts<ProgramBindingResolvedEvent>(g).Count(e => e.OwnerSeat == 3 && e.SkillId == "hell:boss-tier" &&
                    e.BindingId == "arrival-equipment" && e.Completed) == 1 &&
                Facts<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 3 && e.TargetSeat < 3) == (tier == 5 ? 3 : 0) &&
                Facts<DamageAppliedEvent>(g).Where(e => e.SourceSeat == 3 && e.TargetSeat < 3).All(e => e.Amount == 2),
                "Enemy tiers two through five and the three real player-tier entry programs equip one actual deck entity; only tier five issues the real two-damage native Barbarian Assault.");
            Cold(g, registry);
            if (tier != 5) continue;
            var alliedEquipment = Enumerable.Range(0, 3).Select(seat => V(g, seat).Equipment.Single().Id).ToArray();
            var initialAssaults = Facts<CardUseDeclaredEvent>(g).Where(e => e.CardKind == CardKind.BarbarianAssault).ToArray();
            Need(initialAssaults is [{ SourceSeat: 3, CardId: 0 }] && Enumerable.Range(0, 3).All(seat => V(g, seat).Hp == 4),
                "The opening fifth-tier attack uses a genuine zero-material ordinary trick and applies its scoped plus-one damage separately to all three coalition recipients.");
            Use(g, "kill", targets: [3]);
            Until(g, () => P(g) is { PlayerSeat: 0, SkillPrompt.SkillId: ArrivalWatch } &&
                Facts<BossActivityStageStartedEvent>(g).Any(e => e.StageIndex == 1) &&
                g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.CardKind == CardKind.BarbarianAssault &&
                    f.VirtualOrdinaryTrickOrigin is { SkillId: "hell:boss-tier" }));
            var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.BarbarianAssault);
            Need(use.CardId == 0 && use.Action!.PhysicalCards.Count == 0 && use.SourceSeat == 3 &&
                use.VirtualOrdinaryTrickOrigin is { BindingId: "arrival-assault" } &&
                Facts<CardUseDeclaredEvent>(g).Count(e => e.CardKind == CardKind.BarbarianAssault) == 2,
                "A genuinely replaced later boss owns its new native arrival assault rather than re-running the original global game-start window.");
            Cold(g, registry); Play(g);
            Need(Facts<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1 &&
                Facts<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 3 && e.TargetSeat < 3 && e.Amount == 2) == 6 &&
                Enumerable.Range(0, 3).All(seat => V(g, seat).Hp == 2 && V(g, seat).Equipment.Single().Id == alliedEquipment[seat] &&
                    Facts<ProgramBindingResolvedEvent>(g).Count(e => e.OwnerSeat == seat && e.SkillId == "hell:player-tier" &&
                        e.BindingId == "arrival-equipment" && e.Completed) == 1) &&
                Facts<ProgramBindingResolvedEvent>(g).Count(e => e.OwnerSeat == 3 && e.SkillId == "hell:boss-tier" &&
                    e.BindingId == "arrival-equipment" && e.Completed) == 2 &&
                Facts<ProgramBindingResolvedEvent>(g).Count(e => e.OwnerSeat == 3 && e.SkillId == "hell:boss-tier" &&
                    e.BindingId == "arrival-assault" && e.Completed) == 2 &&
                Facts<HellNpcAppearedEvent>(g).Where(e => e.Seat == 3).All(e =>
                    Facts<HellAppearanceCompletedEvent>(g).Count(done => done.AppearanceId == e.AppearanceId) == 1),
                "The cold-resumed later arrival settles once with two damage per target; existing players retain their equipment and never repeat entry skills, while only the new NPC receives its second actual entry.");
            Cold(g, registry);
        }
    }

    private static bool IsChongsheng(PromptChoice choice) =>
        choice.Parameters.GetValueOrDefault("response") == "program-trigger" &&
        choice.Parameters.GetValueOrDefault("skill-id") == "hell:chongsheng" &&
        choice.Parameters.GetValueOrDefault("binding-id") == "limited-rebirth";
    private static (GameEngine, ContentRegistry) Start(int tier, bool rebirth)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(tier, rebirth));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 17, PlayerCount = 5, HumanSeat = 0,
            HumanRole = null, HumanTeamId = "team:allies", ModeId = Mode, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(g, new StartGameCommand()); Until(g, () => P(g)?.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g);
        return (g, registry);
    }

    private sealed class Fixture(int tier, bool rebirth) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-hell-tier-privileges", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rules = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.hell-activity.rules.json")!);
            using var presentation = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.hell-activity.presentation.json")!);
            var actual = SkillProgramCatalog.Load(rules.ReadToEnd(), presentation.ReadToEnd());
            foreach (var (id, program) in actual.Programs) b.AddSkill(new(id, actual.Presentations[id].Name, actual.Presentations[id].Description)
            { Program = program, ProgramPresentation = actual.Presentations[id], Tags = id == "hell:chongsheng" ? SkillTag.Limited : SkillTag.Locked });
            var definitions = new List<object>
            {
                new { id = Driver, revision = 1,
                    modifiers = rebirth ? new object[] { new { id = "opening-materials", query = "initialHandSize", operation = "add", value = 20, priority = 0 } } : [],
                    activations = new object[] {
                        Activation("kill", [new { op = "damage", target = "selectedTarget", amount = 20 }], targets: 1),
                        Activation("empty-hp", [new { op = "loseHp", target = "owner", amount = 6 }]),
                        Activation("back", [new { op = "setFaceState", target = "owner", faceDown = true }, new { op = "setChainedState", target = "owner", chained = true }]),
                        Activation("trim", [new { op = "captureSelectedCards", target = "owner", resultBind = "discarded" },
                            new { op = "moveBoundCards", target = "owner", sourceBind = "discarded", destination = "discardPile", awaitMovementTriggers = true }], maxCards: 32) } },
                new { id = Quiet, revision = 1, modifiers = new[] { new { id = "retain", query = "handLimit", operation = "add", value = 100, priority = 0 } } },
                new { id = Limited, revision = 1,
                    states = new[] { new { id = "paid", initialValue = false, visibility = "private", resetScope = "game", reacquirePolicy = "preserveUntilGameEnd" } },
                    triggers = new[] { new { id = "once", window = "gameStarting", subject = "owner", optional = false, usageScope = "game", usageLimit = 1,
                        effects = new[] { new { op = "setBooleanState", target = "owner", stateId = "paid", value = true } } } } },
                new { id = DrawWatch, revision = 1, triggers = new[] { new { id = "real-draw", window = "nativeDrawStarting", subject = "owner", optional = false,
                    effects = new[] { new { op = "chooseOption", target = "owner", resultBind = "continued", options = new[] { new { id = "continue" } } } } } } },
                new { id = ArrivalWatch, revision = 1, triggers = new[] { new { id = "real-arrival-damage", window = "beforeDamageApplied", subject = "damageTarget", optional = false,
                    effects = new[] { new { op = "chooseOption", target = "owner", resultBind = "continued", options = new[] { new { id = "continue" } } } } } } }
            };
            var fixture = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new[] { Driver, Quiet, Limited, DrawWatch, ArrivalWatch }.ToDictionary(id => id, id => new { name = id, description = "Native Hell tier fixture.",
                        optionLabels = id is DrawWatch or ArrivalWatch ? new Dictionary<string, string> { ["continue"] = "Continue" } : [] }) }));
            foreach (var (id, program) in fixture.Programs) b.AddSkill(new(id, id, "Native Hell tier fixture.")
            { Program = program, ProgramPresentation = fixture.Presentations[id], Tags = id == Limited ? SkillTag.Limited : SkillTag.None });
            string[] players = [Owner, "fixture:hell-tier-peer-1", "fixture:hell-tier-peer-2"];
            foreach (var id in players) b.AddGeneral(new(id, id, "", id == Owner ? Driver : Quiet, "wei", 4,
                id == Owner ? rebirth ? [Limited, DrawWatch] : [ArrivalWatch] : []));
            var stages = Enumerable.Range(0, 4).Select(i => "fixture:hell-tier-boss-" + i).ToArray();
            const string auxiliary = "fixture:hell-tier-auxiliary", hidden = "fixture:hell-tier-hidden";
            foreach (var id in stages.Append(hidden)) b.AddGeneral(new(id, id, "", Quiet, "qun", 4, ["hell:boss-tier"]));
            b.AddGeneral(new(auxiliary, auxiliary, "", Quiet, "qun", 4));
            const string deck = "fixture:hell-tier-deck";
            b.AddDeck(new(deck, "Physical arrival equipment and delayed tricks", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 160).Select(i => new ContentDeckPhysicalCard(
                rebirth && i % 2 == 0 ? "standard:lightning" : "standard:crossbow", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "Native Hell tier fixture", 5, 5, new Dictionary<string, int>(), deck, 3,
                players.Concat(stages).Append(auxiliary).Append(hidden).ToArray(), ContentModeKind.Team,
                new Dictionary<string, int> { ["team:allies"] = 3, ["team:hell"] = 2 })
            { BossActivity = new(players, stages.Select((id, i) => new BossActivityStage("Stage " + i, i == 3 ? [id, hidden] : [id])).ToArray(),
                    5, ["hell:player-tier", "hell:chongsheng"], EnemyTeamId: "team:hell"),
                HellActivity = new(tier, auxiliary, hidden) });
        }
        private static object Activation(string id, object[] effects, int targets = 0, int maxCards = 0) => new
        { id, minCards = maxCards == 0 ? 0 : 1, maxCards, sourceZones = new[] { "hand" }, minTargets = targets, maxTargets = targets,
            targetKind = "anyLiving", usesPerTurn = (int?)null, effects };
    }

    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 5).Select(seat => g.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] Facts<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) =>
        Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Answer(GameEngine g, Func<PromptChoice, bool> choose)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(choose).Id, g.Revision)); }
    private static void Play(GameEngine g) => Until(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Until(GameEngine g, Func<bool> ready)
    {
        for (var i = 0; i < 180; i++)
        {
            if (ready()) return;
            if (P(g) is { PlayerSeat: 0, SkillPrompt.SkillId: DrawWatch or ArrivalWatch }) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            else if (P(g) is { PlayerSeat: 0 } prompt && prompt.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass"))
                Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException($"Hell tier boundary missing: {g.State.Status}/{g.State.Phase}; prompt={P(g)?.Kind}/{P(g)?.SkillPrompt?.SkillId}; " +
            string.Join(',', g.ResolutionStack.Select(f => f.GetType().Name + ':' + f.Id)));
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    { Views = Enumerable.Range(0, 5).Select(seat => SnapshotJson.Serialize(g.CreateSnapshot(seat))),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), g.CardMovements,
        Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
    private static void Cold(GameEngine g, ContentRegistry registry) => Need(State(g) == State(GameReplay.Restore(
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry)),
        "Cold replay preserves true Hell entry uses, native rebirth draw children, physical zones and original limited usage.");
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(command); Need(result.Accepted, result.Error?.Message ?? "Hell tier command rejected."); }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
