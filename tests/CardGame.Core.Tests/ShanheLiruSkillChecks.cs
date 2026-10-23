using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShanheLiruSkillChecks
{
    private const string Mieji = StandardShanheS5LiruPackage.MiejiId, Fencheng = StandardShanheS5LiruPackage.FenchengId;
    private const string Driver = "fixture:shanhe-liru-driver", Watch = "fixture:shanhe-liru-watch", Damage = "fixture:shanhe-liru-damage";
    private const string Mode = "identity:classic-shanhe-liru-fixture";

    public static void NoCostTrickOrWholeHandKeepsPaidMovementChild()
    {
        var (g, r) = Create(); Play(g); DriverUse(g, "equip", 1); Play(g);
        var ownerHand = V(g, 0).Hand.Select(c => c.Id).ToArray(); var hand = V(g, 1).Hand.Select(c => c.Id).ToArray();
        var equipment = V(g, 1).Equipment.Select(c => c.Id).ToArray();
        Activate(g, Mieji, "trick-or-all-hand", 1); Reach(g, IsChallenge);
        Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Mieji).SequentialDiscardTopPayment is null &&
            V(g, 0).Hand.Select(c => c.Id).SequenceEqual(ownerHand), "S5 Mieji opens without the boundary version's hand-trick top cost.");
        g = Cold(g, r); Branch(g, "all-hand"); Reach(g, p => p.SkillPrompt?.SkillId == Watch);
        var root = Root(g); var frameId = root.Id;
        Require(equipment.Length > 0 && root.SequentialDiscard is { Kind: ProgramSequentialDiscardKind.CategoryOrAllHand, Stage: ProgramSequentialDiscardStage.AwaitingMovement, Payment: { } paid } &&
            paid.CardIds.SequenceEqual(hand) && paid.SourceLocations.All(l => l == CardLocation.Hand(1)) && paid.ActualCount == hand.Length &&
            V(g, 1).HandCount == 0 && V(g, 1).Equipment.Select(c => c.Id).SequenceEqual(equipment),
            "All current hand entities pay in one original batch while equipment and source hand stay untouched.");
        g = Cold(g, r); Continue(g); Play(g);
        Require(V(g, 1).HandCount == 1 && F<ProgramSequentialDiscardPaidEvent>(g).Count(e => e.FrameId == frameId) == 1 &&
            V(g, 0).Hand.Select(c => c.Id).SequenceEqual(ownerHand) && !g.GetHumanLegalActions().Any(a => a.SkillId == Mieji),
            "A real movement observer replenishes one hand card after the whole-hand payment; the child returns once without discarding it or reopening this Play-phase use.");
        _ = Cold(g, r);

        (g, r) = Create(); Play(g); GiveTrick(g, 1);
        var trick = V(g, 1).Hand.First(c => c.Kind == CardKind.Dismantlement).Id;
        hand = V(g, 1).Hand.Select(c => c.Id).ToArray(); ownerHand = V(g, 0).Hand.Select(c => c.Id).ToArray();
        Activate(g, Mieji, "trick-or-all-hand", 1); Reach(g, IsChallenge); Branch(g, "primary");
        Require(P(g)!.Choices.All(c => c.Cards.Count == 1 && V(g, 1).Hand.Any(card => card.Id == c.Cards[0] && card.Kind == CardKind.Dismantlement)),
            "The one-trick alternative selects an actual private hand trick.");
        g = Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([trick])); Reach(g, p => p.SkillPrompt?.SkillId == Watch);
        Require(Root(g).SequentialDiscard!.Payment!.CardIds.SequenceEqual([trick]) &&
            V(g, 1).Hand.Select(c => c.Id).Order().SequenceEqual(hand.Where(id => id != trick).Order()),
            "The primary branch pays exactly its selected trick, never the target's other hand cards.");
        g = Cold(g, r); Continue(g); Play(g);
        Require(V(g, 0).Hand.Select(c => c.Id).SequenceEqual(ownerHand), "No Mieji branch pays or topdecks the source's hand."); _ = Cold(g, r);
    }

    public static void StartingSeatDifferenceFireEmptyHandAndTwoRoundQuota()
    {
        var (g, r) = Create(); Play(g); Require(Round(g) == 1, "The first actual round opens Fencheng.");
        DriverUse(g, "clear", 2); Play(g); DriverUse(g, "draw-many", 3); Play(g);
        var expectedDamage = Math.Abs(V(g, 0).HandCount - V(g, 3).HandCount); var hp = V(g, 3).Hp;
        Require(V(g, 3).HandCount > V(g, 0).HandCount && expectedDamage > 0, "The fixed command fixture covers an absolute difference when the target has more hand cards.");
        Activate(g, Fencheng, "chosen-start-hand-difference-fire", 2); Reach(g, IsChallenge);
        var root = Root(g); var frameId = root.Id;
        Require(root.SequentialDiscard!.Order.SequenceEqual([2, 3, 1]) && P(g)!.PlayerSeat == 2 &&
            F<ProgramAllHandChallengeQuotaPaidEvent>(g) is [{ RoundNumber: 1 }], "The chosen other seat begins the actual ring and pays the two-round allowance once.");
        g = Cold(g, r); Branch(g, "all-hand"); Reach(g, p => IsChallenge(p) && p.PlayerSeat == 3);
        Require(!F<ProgramSequentialDiscardPaidEvent>(g).Any(e => e.FrameId == frameId && e.ChooserSeat == 2) &&
            !F<ProgramSequentialDiscardDamageChosenEvent>(g).Any(e => e.FrameId == frameId && e.TargetSeat == 2),
            "An empty hand can choose zero-card all-hand discard without a fictitious movement or fire child.");
        Branch(g, "damage"); Reach(g, p => p.SkillPrompt?.SkillId == Damage);
        Require(Root(g).Id == frameId && Root(g).SequentialDiscard is { ChooserSeat: 3, Stage: ProgramSequentialDiscardStage.AwaitingDamage } &&
            F<ProgramSequentialDiscardDamageChosenEvent>(g).Single(e => e.FrameId == frameId).Amount == expectedDamage && V(g, 3).Hp == hp - expectedDamage,
            "The current absolute hand difference freezes into a real Fire damage attempt owned by the original ring frame.");
        g = Cold(g, r); Continue(g); Reach(g, p => IsChallenge(p) && p.PlayerSeat == 1);
        Branch(g, "all-hand"); Reach(g, p => p.SkillPrompt?.SkillId == Watch); g = Cold(g, r); Continue(g); Play(g);
        Require(F<ProgramSequentialDiscardFinishedEvent>(g).Single(e => e.FrameId == frameId).ProcessedSeats == 3 &&
            F<ProgramAllHandChallengeQuotaPaidEvent>(g).Length == 1 && !g.GetHumanLegalActions().Any(a => a.SkillId == Fencheng),
            "All other seats resolve once and the source cannot reactivate in its paid round.");
        g = Cold(g, r); NextPlay(g);
        Require(Round(g) == 2 && !g.GetHumanLegalActions().Any(a => a.SkillId == Fencheng), "The immediately following round remains on cooldown after cold recovery.");
        NextPlay(g); Require(Round(g) == 3 && g.GetHumanLegalActions().Any(a => a.SkillId == Fencheng), "Two actual rounds later the shared allowance reopens.");
        foreach (var seat in new[] { 0, 1, 2, 3 }) { DriverUse(g, "clear", seat); Play(g); }
        var damageCount = F<ProgramSequentialDiscardDamageChosenEvent>(g).Length;
        Activate(g, Fencheng, "chosen-start-hand-difference-fire", 1); Reach(g, IsChallenge); Branch(g, "damage");
        Reach(g, p => IsChallenge(p) && p.PlayerSeat == 2); Branch(g, "all-hand");
        Reach(g, p => IsChallenge(p) && p.PlayerSeat == 3); Branch(g, "all-hand"); Play(g);
        Require(F<ProgramSequentialDiscardDamageChosenEvent>(g).Length == damageCount + 1 &&
            F<ProgramSequentialDiscardDamageChosenEvent>(g).Last().Amount == 0 && F<ProgramAllHandChallengeQuotaPaidEvent>(g).Select(e => e.RoundNumber).SequenceEqual([1, 3]),
            "An equal zero-hand damage choice advances without a zero-amount native attack and still pays precisely one new allowance."); _ = Cold(g, r);
    }

    private static (GameEngine, ContentRegistry) Create()
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new StandardShanheS5LiruPackage(), new Fixture());
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(g, new SelectGeneralCommand(0, "fixture:shanhe-liru-owner", g.Revision, P(g)!.PromptId)); return (g, r);
    }
    private static void GiveTrick(GameEngine g, int target)
    {
        DriverUse(g, "search"); Reach(g, p => Action(p, "declared-deck-criterion")); Answer(g, c => c.Parameters.GetValueOrDefault("criterion") == "red");
        Reach(g, p => Action(p, "declared-deck-recipient")); Answer(g, c => c.Targets.SequenceEqual([target])); Play(g);
    }
    private static int Round(GameEngine g) => F<RoundStartedEvent>(g).Last().RoundNumber;
    private static ProgramSkillFrame Root(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SequentialDiscard is not null);
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static T[] F<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Action(PendingDecision p, string action) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action);
    private static bool IsChallenge(PendingDecision p) => Action(p, "sequential-discard");
    private static void Activate(GameEngine g, string skill, string activation, int target) => Accept(g, new UseProgramSkillCommand(0, skill, activation, [], [target], g.Revision, P(g)!.PromptId));
    private static void DriverUse(GameEngine g, string activation, int? target = null) => Accept(g, new UseProgramSkillCommand(0, Driver, activation, [], target is { } seat ? [seat] : [], g.Revision, P(g)!.PromptId));
    private static void Branch(GameEngine g, string branch) => Answer(g, c => c.Parameters.GetValueOrDefault("branch") == branch);
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Play(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void NextPlay(GameEngine g) { Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Play(g); }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    {
        for (var i = 0; i < 220; i++)
        {
            var p = P(g); if (p is not null && predicate(p)) return;
            if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue") == true) Continue(g);
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
            else if (p is not null && Action(p, "select-owned-cards")) Answer(g, c => c.Cards.Count == 1);
            else if (p is { PlayerSeat: 0 } && Action(p, "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("S5 Li Ru command boundary absent: " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack }));
    }
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
        string State(GameEngine engine) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(engine.CreateSnapshot(s))).ToArray(),
            Frames = JsonSerializer.Serialize(engine.ResolutionStack), Events = engine.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), engine.CardMovements,
            Commands = CommandJson.Serialize(engine.AcceptedCommands), Zones = engine.CreateCardZoneDiagnostics() });
        Require(State(g) == State(restored), "All current views, owning frames, accepted commands and actual ledgers cold-restore identically."); return restored;
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-liru", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = JsonNode.Parse("""
                {"schemaVersion":0,"skills":[
                  {"id":"fixture:shanhe-liru-driver","revision":1,"activations":[
                    {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipped"}]},
                    {"id":"clear","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"selectedTarget","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"clear"},{"op":"moveBoundCards","target":"owner","sourceBind":"clear","destination":"discardPile","awaitMovementTriggers":true}]},
                    {"id":"draw-many","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":8}]},
                    {"id":"search","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"declareDeckCriterionAndGiveMatchingCard","target":"owner"}]}]},
                  {"id":"fixture:shanhe-liru-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
                  {"id":"fixture:shanhe-liru-watch","revision":1,"triggers":[{"id":"moved","window":"cardsMoved","subject":"owner","optional":false,"sourceZones":["hand"],"movementDiscardOnly":true,"movementOccurrence":"perOwnerBatch","movementReasons":["skill-program.shanhe:s5-mieji-14175.ChooseTrickOrAllHandDiscard","skill-program.shanhe:s5-fencheng-14176.AllHandDiscardOrDifferenceDamageFromSelected"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]},{"op":"draw","target":"owner","amount":1}]}]},
                  {"id":"fixture:shanhe-liru-damage","revision":1,"triggers":[{"id":"fire","window":"afterDamageApplied","subject":"owner","optional":false,"damageOccurrence":"perDamage","effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}
                ]}
                """)!;
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            var presentation = JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
                { [Driver] = new { name = "真实命令", description = "固定场景" }, ["fixture:shanhe-liru-quiet"] = new { name = "安静回合", description = "真实跳过出牌" },
                  [Watch] = new { name = "真实失牌观察", description = "先暂停后摸一张牌", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                  [Damage] = new { name = "真实火伤观察", description = "火伤孩子暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), presentation);
            foreach (var id in new[] { Driver, "fixture:shanhe-liru-quiet", Watch, Damage }) b.AddSkill(new(id, id, "真实程序夹具") { Program = catalog.Programs[id] });
            b.AddSkill(new("fixture:shanhe-liru-selection", "固定选将", "无运行能力") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            b.AddGeneral(new("fixture:shanhe-liru-owner", "山河李儒技能", "supporter", Mieji, "qun", 20, [Fencheng, Driver], Gender: GeneralGender.Male));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:shanhe-liru-target-{i}", "固定目标", "supporter", "fixture:shanhe-liru-selection", "qun", 20,
                ["fixture:shanhe-liru-quiet", Watch, Damage], Gender: GeneralGender.Male));
            b.AddDeck(new("fixture:shanhe-liru-deck", "固定合法实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 120).Select(i =>
                new ContentDeckPhysicalCard(i % 4 == 0 ? "classic:silver-lion" : "standard:dismantlement", i % 4 == 0 ? Suit.Spade : Suit.Heart, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "山河李儒专版技能", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:shanhe-liru-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:shanhe-liru-owner", "fixture:shanhe-liru-target-1", "fixture:shanhe-liru-target-2", "fixture:shanhe-liru-target-3"]));
        }
    }
}
