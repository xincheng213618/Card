using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OrdinaryCaoHongChecks
{
    private const string Skill = "ol:yuanhu", Driver = "fixture:ch-driver", Hp = "fixture:ch-hp", Gain = "fixture:ch-gain";
    private const string Entry = "fixture:ch-entry", Pulse = "fixture:ch-pulse", Loss = "fixture:ch-loss";
    private const string Mode = "identity:classic-ordinary-cao-hong-fixture";
    private const string DrawReason = "skill-program.placed-equipment-benefit.draw", DiscardReason = "skill-program.placed-equipment-benefit.discard";
    public static void RealPlacementReplacementAndArmorDraw()
    {
        var (g, r) = Create("classic:silver-lion"); Play(g); Use(g, "gear", [0]); Play(g); Use(g, "gear", [1]); Play(g);
        Use(g, "hurt", [0]); Play(g); var ownerHp = g.State.Players[0].Hp; var recipientHp = g.State.Players[1].Hp;
        var acceptedBefore = E<CardActionAcceptedEvent>(g).Length;
        Ending(g); Activate(g); Reach(g, p => Step(p, "equipment")); Reject(g); Private(g); g = Cold(g, r);
        var paidCard = P(g)!.Choices.First(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Equipment)).Cards.Single();
        Answer(g, c => c.Cards.SequenceEqual([paidCard])); Reach(g, p => Step(p, "recipient")); g = Cold(g, r);
        Answer(g, c => c.Targets.SequenceEqual([1]));
        var rootId = E<PlacedEquipmentBenefitPaidEvent>(g).Single().ProgramFrameId;
        var payments = E<PlacedEquipmentBenefitPaidEvent>(g).Single().Payment;
        Require(payments.From == CardLocation.Equipment(0) && payments.ReplacedCardId is not null && payments.Slot == EquipmentSlot.Armor,
            "The chosen actual equipped armor is transferred and replaces the recipient's actual armor.");
        var holds = 0;
        for (var step = 0; step < 140 && !E<PlacedEquipmentBenefitFinishedEvent>(g).Any(e => e.ProgramFrameId == rootId); step++)
        {
            var prompt = P(g);
            if (prompt?.SkillPrompt?.SkillId is Hp or Gain) { holds++; g = Cold(g, r); Continue(g); }
            else Advance(g);
        }
        Require(E<PlacedEquipmentBenefitFinishedEvent>(g).Any(e => e.ProgramFrameId == rootId) && holds >= 2 && g.State.Players[0].Hp == ownerHp + 1 && g.State.Players[1].Hp == recipientHp + 1 &&
            E<PlacedEquipmentBenefitDrawnEvent>(g).Single().Invoice is { RecipientSeat: 1, ActualCount: 1 },
            "Both actual Silver Lion removal recovery children return before the single armor draw; replay-restored engines really continue them.");
        Require(g.CardMovements.Count(m => m.CardId == paidCard && m.From == CardLocation.Equipment(0) && m.To == CardLocation.Equipment(1)) == 1 &&
            g.CardMovements.Count(m => m.CardId == payments.ReplacedCardId && m.Reason == CardMoveReasons.EquipmentReplace) == 1 &&
            !E<CardActionAcceptedEvent>(g).Skip(acceptedBefore).Any(e => e.Action.PhysicalCards.Any(c => c.CardId == paidCard)),
            "Placement pays the original entity once without emitting a second equipment Use/action or putting it into Processing.");
        g = Cold(g, r);
    }
    public static void DirectedRecipientDistanceOpaqueHejAndEquipmentSource()
    {
        var (g, r) = Create("standard:qinggang_sword", recipientMashu: true, withJudgment: true); Prepare(g);
        var indulgence = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Indulgence && a.TargetSeats.Contains(3));
        Accept(g, new PlayCardCommand(0, indulgence.CardId!.Value, indulgence.TargetSeats, g.Revision, P(g)!.PromptId, indulgence.PlayedCardKind, indulgence.TargetCardId)); Play(g);
        Ending(g); Activate(g); ChooseEquipment(g, CardZoneKind.Hand); Reach(g, p => Step(p, "recipient")); Answer(g, c => c.Targets.SequenceEqual([1]));
        Reach(g, p => Step(p, "weapon-target"));
        Require(g.GetSeatDistance(1, 3) == 2 && g.GetCombatDistance(1, 3) == 1 && P(g)!.Choices.Any(c => c.Targets.SequenceEqual([3])) &&
            !P(g)!.Choices.Any(c => c.Targets.SequenceEqual([1])), "Weapon benefit uses the recipient's final directed distance, including its real Mashu, and self-distance stays zero.");
        g = Cold(g, r); Answer(g, c => c.Targets.SequenceEqual([3])); Reach(g, p => Step(p, "weapon-card")); Reject(g); Private(g);
        Require(P(g)!.Choices.Where(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)).All(c => c.Cards.Count == 0),
            "The owner chooses a foreign hand slot without receiving its private identity.");
        var judgment = P(g)!.Choices.Single(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Judgment));
        var judgmentCard = judgment.Cards.Single(); g = Cold(g, r); Answer(g, c => c.Id == judgment.Id); Finish(g);
        Require(g.CardMovements.Count(m => m.CardId == judgmentCard && m.From == CardLocation.Judgment(3) && m.To == CardLocation.DiscardPile && m.Reason.Value == DiscardReason) == 1,
            "A real weapon placement may discard a real entity from the qualifying character's Judgment area, exactly once.");

        var (hand, hr) = Create("standard:qinggang_sword", recipientMashu: true); Prepare(hand); Ending(hand); Activate(hand); ChooseEquipment(hand, CardZoneKind.Hand);
        Reach(hand, p => Step(p, "recipient")); Answer(hand, c => c.Targets.SequenceEqual([1])); Reach(hand, p => Step(p, "weapon-target"));
        Answer(hand, c => c.Targets.SequenceEqual([3])); Reach(hand, p => Step(p, "weapon-card")); hand = Cold(hand, hr);
        Answer(hand, c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)); Finish(hand);
        var discard = E<PlacedEquipmentBenefitDiscardedEvent>(hand).Single().Payment;
        Require(discard.TargetSeat == 3 && discard.From == CardLocation.Hand(3) &&
            hand.CardMovements.Count(m => m.CardId == discard.CardId && m.From == discard.From && m.Reason.Value == DiscardReason) == 1,
            "The hidden slot is resolved only at actual accepted payment, and the original entity is discarded once after restoration.");
    }
    public static void MountRecoveryPaidSourceLossAndGainDying()
    {
        foreach (var horse in new[] { "standard:offensive_horse", "standard:defensive_horse" })
        {
            var (g, r) = Create(horse); Prepare(g); Ending(g); Activate(g); ChooseEquipment(g, CardZoneKind.Hand);
            Reach(g, p => Step(p, "recipient")); var hp = g.State.Players[1].Hp; Answer(g, c => c.Targets.SequenceEqual([1]));
            Reach(g, p => p.SkillPrompt?.SkillId == Hp); g = Cold(g, r); Continue(g); Finish(g);
            Require(g.State.Players[1].Hp == hp + 1 && E<PlacedEquipmentBenefitRecoveryIssuedEvent>(g).Single().RequestedAmount == 1 &&
                E<PlacedEquipmentBenefitDrawnEvent>(g).Length == 0, "Each real mount slot issues one recipient recovery through a restored HP child, without armor draw.");
        }
        var (loss, lr) = Create("standard:bagua", sourceLoss: true); Prepare(loss); Ending(loss); Activate(loss); ChooseEquipment(loss, CardZoneKind.Hand);
        Reach(loss, p => Step(p, "recipient")); Answer(loss, c => c.Targets.SequenceEqual([1])); Reach(loss, p => p.SkillPrompt?.SkillId == Loss && p.Choices.Any(c => c.Targets.Count == 1));
        Answer(loss, c => c.Targets.SequenceEqual([0])); Reach(loss, p => p.SkillPrompt?.SkillId == Loss); loss = Cold(loss, lr); Continue(loss);
        DrainHolds(loss); Finish(loss);
        Require(E<PlacedEquipmentBenefitPaidEvent>(loss).Length == 1 && E<PlacedEquipmentBenefitDrawnEvent>(loss).Single().Invoice.ActualCount == 1,
            "Actual source suppression after placement does not revoke the already paid armor benefit or repeat its equipment payment."); loss = Cold(loss, lr);

        var (dying, dr) = Create("standard:bagua", gainDying: true); Prepare(dying); Ending(dying); Activate(dying); ChooseEquipment(dying, CardZoneKind.Hand);
        Reach(dying, p => Step(p, "recipient")); Answer(dying, c => c.Targets.SequenceEqual([0])); Reach(dying, p => p.SkillPrompt?.SkillId == Entry);
        Require(dying.ResolutionStack.OfType<DyingFrame>().Any(d => d.VictimSeat == 0) &&
            dying.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.PlacedEquipmentBenefit is { Stage: PlacedEquipmentBenefitStage.DrawChildren }),
            "Actual armor Draw1 creates a real CardsGained→HP-loss→DyingEntering child beneath the original paid Ending frame.");
        dying = Cold(dying, dr); Continue(dying); Reach(dying, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Pulse));
        Answer(dying, c => c.Parameters.GetValueOrDefault("skill-id") == Pulse); Reach(dying, p => p.SkillPrompt?.SkillId == Hp); dying = Cold(dying, dr); Continue(dying);
        Reach(dying, p => p.SkillPrompt?.SkillId == Gain); dying = Cold(dying, dr); Continue(dying); Finish(dying);
        Require(E<PlacedEquipmentBenefitPaidEvent>(dying).Length == 1 && E<PlacedEquipmentBenefitDrawnEvent>(dying).Single().Invoice.ActualCount == 1 &&
            E<CardUseDeclaredEvent>(dying).Any(e => e.CardId == 0 && e.CardKind == CardKind.Alcohol && e.SourceSeat == 0) &&
            !dying.CardMovements.Any(m => m.CardId == 0), "The restored self-dying zero-entity Alcohol returns through its actual HP observer without repeating the paid draw or equipment transfer.");
    }
    public static void NativeOptionalEndingAndStrictComposition()
    {
        var (cancel, cr) = Create("standard:bagua"); Prepare(cancel); Ending(cancel); Activate(cancel); Reach(cancel, p => Step(p, "equipment"));
        var before = cancel.CardMovements.Count; cancel = Cold(cancel, cr); Answer(cancel, c => c.Parameters.GetValueOrDefault("step") == "cancel");
        Require(cancel.CardMovements.Count == before && E<PlacedEquipmentBenefitPaidEvent>(cancel).Length == 0,
            "Cancellation before actual placement moves no card and issues no reward.");
        var (native, nr) = Create("standard:bagua", native: true);
        for (var i = 0; i < 100 && native.State.Status != EngineStatus.Completed && E<PlacedEquipmentBenefitPaidEvent>(native).Length == 0; i++) Advance(native);
        Require(E<PlacedEquipmentBenefitPaidEvent>(native).Length > 0 && E<PlacedEquipmentBenefitFinishedEvent>(native).Any(e => e.Placed),
            "The sole real native source accepts the optional Ending benefit and pays actual equipment; this is a bounded draft, with no seed search or forced AI answer.");
        native = Cold(native, nr);
        var presentation = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
            skills = new Dictionary<string,object> { ["fixture:invalid-place"] = new { name = "无效测试", description = "拒绝错误上下文" } } });
        foreach (var window in new[] { "playEnding", "afterTurnEnded", "afterDamageApplied" })
        {
            var rules = $$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:invalid-place","revision":1,"triggers":[{"id":"bad","window":"{{window}}","subject":"owner","optional":true,"effects":[{"op":"placeOwnedEquipmentThenResolveSlotBenefit","target":"owner"}]}]}]}""";
            var failed = false; try { SkillProgramCatalog.Load(rules, presentation); } catch (InvalidOperationException) { failed = true; }
            Require(failed, "The new standalone capability cannot be placed in another lifecycle or damage window.");
        }
    }
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Step(PendingDecision p, string step) => p.SkillPrompt?.SkillId == Skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("step") == step);
    private static void Private(GameEngine g)
    { var p = P(g)!; Require(p.IsPrivate && Enumerable.Range(0, 4).Where(s => s != p.PlayerSeat).All(s => g.CreateSnapshot(s).PendingDecision is null), "Only the original chooser sees this private material/opaque foreign slot prompt."); }
    private static void ChooseEquipment(GameEngine g, CardZoneKind zone)
    { Reach(g, p => Step(p, "equipment")); Answer(g, c => c.Parameters.GetValueOrDefault("source-zone") == zone.ToString()); }
    private static void Play(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void Use(GameEngine g, string id, int[] targets) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets, g.Revision, P(g)!.PromptId));
    private static void Prepare(GameEngine g) { Play(g); Use(g, "draw", []); Play(g); }
    private static void Ending(GameEngine g) { Play(g); Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Skill && c.Parameters.GetValueOrDefault("program-action") == "activate")); }
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("skill-id") == Skill && c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Finish(GameEngine g) { Until(g, () => E<PlacedEquipmentBenefitFinishedEvent>(g).Length > 0); }
    private static void DrainHolds(GameEngine g) { Until(g, () => E<PlacedEquipmentBenefitFinishedEvent>(g).Length > 0, true); }
    private static void Until(GameEngine g, Func<bool> done, bool continueHolds = false)
    { for (var i = 0; i < 140; i++) { if (done()) return; if (continueHolds && P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue") == true) Continue(g); else Advance(g); } throw new InvalidOperationException("Fixed Ending did not finish its original paid root."); }
    private static void Reach(GameEngine g, Func<PendingDecision,bool> match)
    { for (var i = 0; i < 160; i++) { if (P(g) is { } p && match(p)) return; Advance(g); } throw new InvalidOperationException("Fixed Cao Hong command fixture missed boundary: " + JsonSerializer.Serialize(P(g))); }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards })
        {
            // Retain one actual equipment for the impending Ending; discard other true cards through the standard command.
            var equipment = g.CreateSnapshot(0).Players[0].Hand.FirstOrDefault(c => EquipmentCatalog.IsEquipment(c.Kind));
            var discarded = p.ValidCardIds.Where(id => id != equipment?.Id).Take(p.RequiredCardCount).ToArray();
            if (discarded.Length != p.RequiredCardCount) discarded = p.ValidCardIds.Take(p.RequiredCardCount).ToArray();
            Accept(g, new DiscardCardsCommand(0, discarded, p.PromptId, g.Revision));
        }
        else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass") == true) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Answer(GameEngine g, Func<PromptChoice,bool> match) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(match).Id, g.Revision)); }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Real command rejected."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0,4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Require(State(g) == State(restored), "Four-view journal replay retains exact original payment, private choices and child parent facts."); return restored; }
    private static void Reject(GameEngine g)
    { var p = P(g)!; var before = State(g); Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && before == State(g), "Rejected input cannot change payment, history or private views."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static (GameEngine, ContentRegistry) Create(string equipment, bool recipientMashu = false, bool withJudgment = false, bool sourceLoss = false, bool gainDying = false, bool native = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(equipment, recipientMashu, withJudgment, sourceLoss, gainDying, native));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, ModeId = Mode, HumanSeat = native ? -1 : 0, HumanRole = native ? null : Role.Lord,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, r);
        Accept(g, new StartGameCommand()); if (!native) { Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0); Accept(g, new SelectGeneralCommand(0, "fixture:ch-owner", g.Revision, P(g)!.PromptId)); } return (g, r);
    }
    private sealed class Fixture(string equipment, bool recipientMashu, bool withJudgment, bool sourceLoss, bool gainDying, bool native) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ordinary-cao-hong", new(1,0,0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
            {"id":"{{Driver}}","revision":1,"activations":[{"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerPhase":1,"effects":[{"op":"draw","target":"owner","amount":20}]},{"id":"gear","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},{"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]}]},
            {"id":"{{Hp}}","revision":1,"triggers":[{"id":"hp","window":"afterHpRecovered","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
            {"id":"{{Gain}}","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perSourceOwner","movementReasons":["{{DrawReason}}"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{{(gainDying ? "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":7}," : string.Empty)}}{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
            {"id":"{{Entry}}","revision":1,"triggers":[{"id":"entry","window":"dyingEntering","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
            {"id":"{{Pulse}}","revision":1,"triggers":[{"id":"wine","window":"selfDyingResponse","subject":"owner","optional":true,"usageScope":"game","usageLimit":1,"effects":[{"op":"useVirtualDyingAlcohol","target":"owner"},{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":3,"clampToMaxHp":true}]}]},
            {"id":"{{Loss}}","revision":1,"triggers":[{"id":"loss","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["{{CardMoveReasons.EquipmentEnter.Value}}"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"anyLiving"},{"op":"issueCurrentTurnNonLockedSkillSuppression","target":"selectedTarget"},{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}]}
            """;
            var labels = new Dictionary<string,object>(); foreach (var id in new[] { Driver, Hp, Gain, Entry, Pulse, Loss }) labels[id] = id is Hp or Gain or Entry or Loss
                ? new { name = id, description = "真实子窗", optionLabels = new Dictionary<string,string> { ["continue"] = "继续" } } : (object)new { name = id, description = "固定真实命令驱动" };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var id in catalog.Programs.Keys) b.AddSkill(new(id, id, "固定能力") { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id], Tags = id == Loss ? SkillTag.Locked : SkillTag.None });
            foreach (var owner in new[] { true, false }) b.AddSkill(new(owner ? "fixture:ch-owner-weight" : "fixture:ch-peer-weight", "公开固定选将", "角色权重")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => (role == Role.Lord) == owner ? 10000d : -10000d) });
            var own = new List<string> { "fixture:ch-owner-weight" }; if (!native) own.AddRange([Driver, Hp, Gain, Entry, Pulse]); if (sourceLoss) own.Add(Loss);
            b.AddGeneral(new("fixture:ch-owner", "实际援护来源", "supporter", Skill, "wei", 6, own));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:ch-peer-{i}", "实际受赠角色", "supporter", "fixture:ch-peer-weight", "qun", 6,
                (native ? Array.Empty<string>() : new[] { Hp, Gain }).Concat(i == 1 && recipientMashu ? new[] { "classic:mashu" } : Array.Empty<string>()).ToArray()) { InitialHp = 3 });
            b.AddDeck(new("fixture:ch-deck", "小固定装备及区域材料", 4, 0, []) { PhysicalCards = Enumerable.Range(0,64).Select(i =>
                new ContentDeckPhysicalCard(withJudgment && i % 4 == 0 ? "standard:indulgence" : equipment, Suit.Club, 7)).ToArray() });
            b.AddMode(new(Mode, "实际援护", 4, 4, new Dictionary<string,int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:ch-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:ch-owner", "fixture:ch-peer-1", "fixture:ch-peer-2", "fixture:ch-peer-3"]));
        }
    }
}
