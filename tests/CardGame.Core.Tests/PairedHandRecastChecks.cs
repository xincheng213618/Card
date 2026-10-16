using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static partial class PairedHandRecastChecks
{
    private const string Recast = "fixture:paired-hand-recast", Driver = "fixture:paired-recast-driver";
    private const string Cost = "fixture:paired-recast-cost", Gain = "fixture:paired-recast-gain";
    private const string Material = "fixture:paired-recast-material", Suppress = "fixture:paired-recast-suppress";
    private const string DiscardWitness = "fixture:paired-recast-discard-witness", Mode = "identity:paired-hand-recast";
    private enum Scenario { Normal, SourceLoss, MoveMaterial, KillPartner }

    private static PairedHandRecastStartedEvent Begin(GameEngine g, int ownerCard, int partnerCard)
    {
        Accept(g, new UseProgramSkillCommand(0, Recast, "pair", [ownerCard], [1], g.Revision, P(g)!.PromptId));
        Reach(g, p => p.PlayerSeat == 1 && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "paired-hand-recast"));
        var issued = F<PairedHandRecastStartedEvent>(g).Single();
        Require(P(g)!.Choices.Any(c => c.Cards.SequenceEqual([partnerCard])) && issued.PartnerSeat == 1 &&
            V(g, 0).Hand.Any(c => c.Id == ownerCard) && V(g, 1).Hand.Any(c => c.Id == partnerCard),
            "The genuine partner prompt appears while both original materials are still held and no cost has been paid.");
        return issued;
    }
    private static void SelectPartner(GameEngine g, int card) => Answer(g, c => c.Cards.SequenceEqual([card]));
    private static void CostBoundary(GameEngine g, PairedHandRecastStartedEvent issued, int cursor, int ownerCard, int partnerCard)
    {
        var seat = cursor;
        Reach(g, p => p.PlayerSeat == seat && p.SkillPrompt?.SkillId == Cost && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
        var root = Root(g, issued.FrameId); var receipt = root.PairedHandRecast!;
        var paid = cursor == 0 ? receipt.Owner : receipt.Partner;
        var movement = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(f => f.Batch.Id == paid.CostBatchId);
        var observer = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Cost);
        Require(receipt.Stage == PairedHandRecastStage.CostChildren && receipt.Cursor == cursor && paid.CostIssued && !paid.DrawIssued &&
            receipt.Source == issued.Source && receipt.Owner.Material?.CardId == ownerCard && receipt.Partner.Material?.CardId == partnerCard &&
            root.PendingMovementContinuation?.SubjectSeat == seat && movement.Batch.ParentFrameId == root.Id &&
            movement.Batch.AwaitingProgramFrameId == root.Id && movement.ResumeProgramFrameId is null &&
            observer.WindowContext?.Window == SkillProgramTriggerWindow.CardsMoved && observer.WindowContext.ParentFrameId == movement.Id &&
            F<PairedHandRecastPaidEvent>(g).Single(e => e.FrameId == root.Id && e.Cursor == cursor) is var fact &&
            fact.ParticipantSeat == seat && fact.CardId == paid.Material!.CardId && fact.From == CardLocation.Hand(seat) &&
            fact.PrintedKind == CardKind.Crossbow && fact.BatchId == movement.Batch.Id && fact.SequenceBefore == paid.CostBefore &&
            fact.SequenceAfter == paid.CostAfter && fact.SequenceAfter > fact.SequenceBefore &&
            movement.Batch.Movements is [var move] && move.CardId == paid.Material.CardId && move.From == paid.Material.From &&
            move.To == CardLocation.DiscardPile && move.Reason == CardMoveReasons.RecastDiscard &&
            (cursor != 0 || !receipt.Partner.CostIssued),
            "The exact current participant's one physical recast payment owns its native movement child, frozen pair and typed return before its owed draw or the later participant's payment.");
        PrivateAndFrozen(g);
    }
    private static void ReachGain(GameEngine g, int seat) => Reach(g, p => p.PlayerSeat == seat && p.SkillPrompt?.SkillId == Gain &&
        p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
    private static void GainBoundary(GameEngine g, PairedHandRecastStartedEvent issued, int cursor, int ownerCard, int partnerCard)
    {
        var root = Root(g, issued.FrameId); var receipt = root.PairedHandRecast!;
        var participant = cursor == 0 ? receipt.Owner : receipt.Partner;
        var movement = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(f => f.Batch.ParentFrameId == root.Id &&
            f.Batch.Movements.Any(m => m.Reason == CardMoveReasons.RecastDraw));
        Require(receipt.Stage == PairedHandRecastStage.DrawChildren && receipt.Cursor == cursor &&
            participant is { CostIssued: true, DrawIssued: true, ActualDrawCount: 1 } &&
            receipt.Source == issued.Source && receipt.Owner.Material?.CardId == ownerCard && receipt.Partner.Material?.CardId == partnerCard &&
            root.PendingMovementContinuation?.SubjectSeat == cursor && movement.Batch.AwaitingProgramFrameId == root.Id &&
            movement.Batch.Movements is [var draw] && draw.From == CardLocation.DrawPile && draw.To == CardLocation.Hand(cursor) &&
            draw.Reason == CardMoveReasons.RecastDraw && draw.Sequence > participant.DrawBefore && draw.Sequence <= participant.DrawAfter &&
            F<PairedHandRecastDrawIssuedEvent>(g).Count(e => e.FrameId == root.Id && e.Cursor == cursor && e.ActualCount == 1) == 1 &&
            !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == Gain && f.OwnerSeat == cursor),
            "One actual recast draw is already issued under the same receipt when its genuine optional gain candidate pauses before binding; the later payer cannot run ahead of this child.");
        PrivateAndFrozen(g);
    }
    private static void FinishGain(GameEngine g)
    {
        var seat = P(g)!.PlayerSeat;
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, p => p.PlayerSeat == seat && p.SkillPrompt?.SkillId == Gain && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
        Continue(g);
    }
    private static void Completed(GameEngine g, PairedHandRecastStartedEvent issued, int ownerCard, int partnerCard, bool partnerPaid)
    {
        var paid = F<PairedHandRecastPaidEvent>(g).Where(e => e.FrameId == issued.FrameId).ToArray();
        var draws = F<PairedHandRecastDrawIssuedEvent>(g).Where(e => e.FrameId == issued.FrameId).ToArray();
        Require(F<PairedHandRecastCompletedEvent>(g).Single(e => e.FrameId == issued.FrameId) is var done &&
            done.OwnerPaid && done.PartnerPaid == partnerPaid && done.OwnerDrawCount == 1 && done.PartnerDrawCount == (partnerPaid ? 1 : 0) &&
            paid.Length == (partnerPaid ? 2 : 1) && draws.Length == paid.Length &&
            paid[0] is { Cursor: 0, ParticipantSeat: 0 } && paid[0].CardId == ownerCard &&
            draws[0] is { Cursor: 0, ParticipantSeat: 0, ActualCount: 1 } &&
            (!partnerPaid || paid[1] is { Cursor: 1, ParticipantSeat: 1 } && paid[1].CardId == partnerCard &&
                draws[1] is { Cursor: 1, ParticipantSeat: 1, ActualCount: 1 } && paid[1].SequenceBefore >= draws[0].SequenceAfter) &&
            g.CardMovements.Count(m => m.CardId == ownerCard && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.RecastDiscard) == 1 &&
            g.CardMovements.Count(m => m.CardId == partnerCard && m.Reason == CardMoveReasons.RecastDiscard) == (partnerPaid ? 1 : 0) &&
            F<CardRecastEvent>(g).Count(e => e.ActorSeat == 0 && e.CardId == ownerCard && e.DrawCount == 1) == 1 &&
            F<CardRecastEvent>(g).Count(e => e.ActorSeat == 1 && e.CardId == partnerCard && e.DrawCount == 1) == (partnerPaid ? 1 : 0) &&
            !g.ResolutionStack.Any(f => f.Id == issued.FrameId) && g.State.ProcessingCardCount == 0 &&
            g.CreateCardZoneDiagnostics().Count == 48 && g.CreateCardZoneDiagnostics().Select(c => c.CardId).Distinct().Count() == 48,
            "The ordered pair completes once with one payment, draw attempt and native recast fact for each actual payer, exact original entity provenance and conserved card inventory.");
    }

    private static ProgramSkillFrame Root(GameEngine g, long id) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == id);
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] F<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Real paired recast command rejected."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> choose) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(choose).Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine g, Func<PendingDecision, bool> stop)
    {
        for (var step = 0; step < 100; step++)
        {
            var p = P(g); if (p is not null && stop(p)) return;
            if (p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) break;
            if (p is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 })
                Answer(g, c => c.Cards.Count == 0 && (c.Parameters.GetValueOrDefault("response") is "let-die" or "pass" || c.Parameters.GetValueOrDefault("action") == "pass"));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Fixed paired recast boundary not reached: " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack, RecentFacts = g.Events.TakeLast(8).Select(e => e.Payload) }));
    }
    private static void PrivateAndFrozen(GameEngine g)
    {
        var p = P(g)!; Require(p.IsPrivate, "The real participant or movement observer decision is private.");
        foreach (var viewer in Enumerable.Range(0, 4).Where(s => s != p.PlayerSeat))
            Require(g.CreateSnapshot(viewer).PendingDecision is null && g.CreateSnapshot(viewer).Players[p.PlayerSeat].Hand.Count == 0,
                "Other prepared views expose neither the participant choice nor its held hand identities.");
        Require(p.ValidCardIds is System.Collections.IList { IsReadOnly: true } && p.ValidTargetSeats is System.Collections.IList { IsReadOnly: true } &&
            p.ValidContentIds is System.Collections.IList { IsReadOnly: true } && p.Choices is System.Collections.IList { IsReadOnly: true } &&
            p.Choices.All(c => c.Cards is System.Collections.IList { IsReadOnly: true } && c.Targets is System.Collections.IList { IsReadOnly: true } &&
                c.ContentIds is System.Collections.IList { IsReadOnly: true } && c.Parameters is System.Collections.IDictionary { IsReadOnly: true }) &&
            g.ResolutionStack.OfType<ProgramSkillFrame>().Where(f => f.PairedHandRecast is not null).All(f =>
                f.PairedHandRecast!.EligiblePartnerMaterials is System.Collections.IList { IsReadOnly: true }),
            "Every prepared prompt collection and nested parameter map, plus the owning receipt's private eligible materials, is frozen.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Require(State(copy) == State(g), "Cold replay preserves four private views, frozen pair, exact payment/draw intervals, native child return, events, inventory and accepted commands."); return copy; }
    private static (GameEngine, ContentRegistry) Create(Scenario scenario = Scenario.Normal)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Require(P(g)!.ValidContentIds.Contains("fixture:paired-recast-owner"), "The genuine published general selection includes the fixed synthetic owner.");
        Accept(g, new SelectGeneralCommand(0, "fixture:paired-recast-owner", g.Revision, P(g)!.PromptId));
        Play(g);
        Accept(g, new UseProgramSkillCommand(0, Driver, "acquire", [], [], g.Revision, P(g)!.PromptId));
        Play(g);
        Require(V(g, 0).Skills!.Any(s => s.Id == Recast) && F<SkillsAcquiredEvent>(g).Count(e => e.PlayerSeat == 0 && e.SourceSkillId == Driver && e.SkillIds.Contains(Recast)) == 1,
            "The operation is genuinely acquired through a legal driver so later qualification loss can be observed without relying on a primary skill label.");
        if (scenario == Scenario.MoveMaterial)
        {
            Require(V(g, 1).GeneralId == "fixture:paired-recast-peer-1" &&
                V(g, 1).Skills!.Any(s => s.Id == Material) &&
                Enumerable.Range(0, 4).Where(seat => seat != 1).All(seat => !V(g, seat).Skills!.Any(s => s.Id == Material)),
                "The first real AI selection chooses the uniquely weighted material observer; only the original partner owns that printed skill.");
        }
        return (g, registry);
    }
    private static JsonNode Rules(Scenario scenario)
    {
        var rules = JsonNode.Parse($$$"""
        {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
          {"id":"{{{Recast}}}","revision":1,"activations":[{"id":"pair","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLivingWithHand","usesPerPhase":1,"usesPerTurn":null,"usesPerGame":null,"effects":[{"op":"pairedHandRecast","target":"selectedTarget"}]}]},
          {"id":"{{{Driver}}}","revision":1,"activations":[
            {"id":"acquire","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["{{{Recast}}}"]}]}]},
          {"id":"{{{Cost}}}","revision":1,"triggers":[{"id":"cost","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perCard","movementReasons":["card.recast.discard"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"cost-seen","options":[{"id":"continue"}]}]}]},
          {"id":"{{{Gain}}}","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["card.recast.draw"],"optional":true,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain-seen","options":[{"id":"continue"}]}]}]},
          {"id":"{{{Material}}}","revision":1,"triggers":[{"id":"remove-original","window":"discardPileReceived","subject":"owner","discardOwnerScope":"other","sourceZones":["hand"],"movementOccurrence":"perCard","movementReasons":["card.recast.discard"],"optional":false,"effects":[{"op":"selectOwnedCards","target":"owner","amount":1,"zones":["hand"],"resultBind":"removed"},{"op":"moveBoundCards","target":"owner","sourceBind":"removed","destination":"discardPile","awaitMovementTriggers":true},{"op":"chooseOption","target":"owner","resultBind":"material-seen","options":[{"id":"continue"}]}]}]},
          {"id":"{{{DiscardWitness}}}","revision":1,"triggers":[{"id":"discard-only","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perCard","movementDiscardOnly":true,"movementReasons":["card.recast.discard"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"discard-seen","options":[{"id":"continue"}]}]}]}
        ]}
        """)!;
        var costEffects = rules["skills"]![2]!["triggers"]![0]!["effects"]!.AsArray();
        if (scenario == Scenario.SourceLoss)
            costEffects.Add(JsonNode.Parse($$$"""{"op":"grantSkills","target":"owner","skillIds":["{{{Suppress}}}"]}"""));
        if (scenario == Scenario.KillPartner)
        {
            costEffects.Add(JsonNode.Parse("""{"op":"selectTarget","target":"owner","targetKind":"otherLiving"}"""));
            costEffects.Add(JsonNode.Parse("""{"op":"loseHp","target":"selectedTarget","amount":4}"""));
        }
        return rules;
    }
    private static string Presentation(JsonNode rules) => JsonSerializer.Serialize(new { schemaVersion = 3,
        skills = rules["skills"]!.AsArray().ToDictionary(n => n!["id"]!.GetValue<string>(), n => (object)new
        { name = n!["id"]!.GetValue<string>(), description = "真实独立双人手牌重铸机制", optionLabels =
            n!["id"]!.GetValue<string>() is Cost or Gain or Material or DiscardWitness ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() }) });
    private sealed class Fixture(Scenario scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-paired-hand-recast", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = Rules(scenario); var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), Presentation(rules));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "共享原生重铸夹具")
            {
                Program = program,
                SelectionWeights = scenario == Scenario.MoveMaterial && id == Material
                    ? Enum.GetValues<Role>().ToDictionary(role => role, _ => 10000d) : null
            });
            b.AddSkill(new(Suppress, "真实资格抑制", "固定真实HP触发") { SuppressionRule = new(5) });
            b.AddSkill(new("fixture:paired-recast-selection", "固定其他候选", "无运行技能")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            b.AddGeneral(new("fixture:paired-recast-owner", "真实双人重铸拥有者", "supporter", Driver, "jin", 4,
                [Cost, Gain, DiscardWitness], GeneralGender.Male));
            // Identity selection runs Lord first, then ascending seat; four
            // candidates expose the whole remaining pool. A unique non-active
            // primary skill weight makes seat1 choose the observer, independent
            // of the tiny AI tie breaker and without replacing any live grant.
            for (var seat = 1; seat < 4; seat++) b.AddGeneral(new($"fixture:paired-recast-peer-{seat}", "真实其他参与者", "supporter",
                scenario == Scenario.MoveMaterial && seat == 1 ? Material : "fixture:paired-recast-selection",
                "qun", 4, [Cost, Gain, DiscardWitness], GeneralGender.Male));
            b.AddDeck(new("fixture:paired-recast-deck", "同质真实手牌", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 48).Select(i => new ContentDeckPhysicalCard("standard:crossbow", Suit.Club, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "独立双人手牌重铸", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:paired-recast-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:paired-recast-owner", "fixture:paired-recast-peer-1", "fixture:paired-recast-peer-2", "fixture:paired-recast-peer-3"]));
        }
    }
}
