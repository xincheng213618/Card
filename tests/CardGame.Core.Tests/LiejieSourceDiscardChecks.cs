using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class LiejieSourceDiscardChecks
{
    private const string Skill = "ol:liejie";
    private const string Driver = "fixture:liejie-source-driver";
    private const string Watch = "fixture:liejie-source-watch";
    private const string Red = "fixture:liejie-effective-red";
    private const string Protected = "fixture:liejie-protected";
    private const string Weapon = "fixture:liejie-general-weapon";
    private const string Owner = "fixture:liejie-source-owner";
    private const string Mode = "identity:classic-liejie-source";
    private const string CostReason = "skill-program.ol:liejie.MoveBoundCards";
    private const string DrawReason = "skill-program.ol:liejie.Draw";
    private const string SourceReason = "skill-program.ol:liejie.liejie-discard";

    public static void EffectiveRedQuotaKeepsMixedHePaymentAndNativeChildren()
    {
        var (g, registry) = Start("children");
        var source = Enumerable.Range(1, 3).Single(s => g.CreateSnapshot(0).Players[s].MaxHp == 8);
        var victim = Enumerable.Range(1, 3).First(s => s != source);
        Use(g, "equipment", 0); ReachPlay(g); Use(g, "equipment", source); ReachPlay(g);
        Use(g, "wound", source); ReachPlay(g);
        var ownArmor = g.CreateSnapshot(0).Players[0].Equipment.Single().Id;
        var sourceArmor = g.CreateSnapshot(0).Players[source].Equipment.Single().Id;
        var sourceHand = g.CreateSnapshot(source).Players[source].Hand.Select(c => c.Id).ToArray();
        Use(g, "incoming", source); Activate(g); Reach(g, p => HasAction(p, "select-owned-cards"));
        var id = Parent(g).Id;
        Answer(g, c => c.Cards.Contains(ownArmor));
        Answer(g, c => Action(c) == "select-owned-cards" && !c.Cards.Contains(ownArmor));
        Answer(g, c => Action(c) == "finish-owned-cards");
        Reach(g, p => IsWatch(p, "recovery"));
        Require(Parent(g).InstructionIndex == 2 && Parent(g).LiejieDiscardPending is null &&
                Cost(g, id).Count == 2 && Cost(g, id).RedCount == 2 &&
                g.CardMovements.Count(m => m.Reason.Value == CostReason) == 2 &&
                g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Count() == 0 &&
                !g.CardMovements.Any(m => m.Reason.Value == DrawReason) &&
                !Facts<ProgramLiejieSelectionStartedEvent>(g).Any(e => e.FrameId == id),
            "The mixed printed-black HE cost freezes two effective-red cards, then native own Silver Lion recovery pauses before any draw or source choice.");
        g = Cold(g, registry); Continue(g); Reach(g, p => IsWatch(p, "draw"));
        var costChild = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single();
        Require(Parent(g).InstructionIndex == 3 && Parent(g).LiejieDiscardPending is null &&
                costChild.Batch.ParentFrameId == id && costChild.ResumeProgramFrameId == id &&
                costChild.Batch.AwaitingProgramFrameId is null &&
                g.CardMovements.Count(m => m.Reason.Value == DrawReason) == 2,
            "The bound two-card native draw drains through its real gain child before publishing source HE selection.");
        g = Cold(g, registry); Continue(g);
        Reach(g, p => HasAction(p, "select-target")); Answer(g, c => c.Targets.SequenceEqual([victim]));
        ReachState(g, () => g.ResolutionStack.OfType<DyingFrame>().Any(f => f.VictimSeat == victim));
        Require(Parent(g).InstructionIndex == 3 && !Facts<ProgramLiejieSelectionStartedEvent>(g).Any(e => e.FrameId == id),
            "A real gain-child damage and dying settlement retain the original paid first-clause frame before its source choice.");
        g = Cold(g, registry); Reach(g, p => IsWatch(p, "draw-done"));
        Require(!g.CreateSnapshot(0).Players[victim].IsAlive &&
                Facts<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == victim && e.Amount == 1) == 1,
            "The native child damage and unrescued dying settle once without paying the original Liejie cost again.");
        g = Cold(g, registry); Continue(g); Reach(g, p => HasAction(p, "liejie-pick"));
        var receipt = Parent(g).LiejieDiscardPending!;
        Require(receipt.SourceSeat == source && receipt.FrozenRedCount == 2 && receipt.MaximumCount == 2 &&
                receipt.CandidateIds.Contains(sourceArmor) && sourceHand.All(receipt.CandidateIds.Contains) &&
                P(g)!.Choices.Where(c => c.Parameters.GetValueOrDefault("source-zone") == "Hand").All(c => c.Cards.Count == 0) &&
                P(g)!.Choices.Any(c => c.Cards.Contains(sourceArmor)),
            "The exact frozen red quota exposes opaque foreign Hand slots and public Equipment, with every actual HE candidate available.");
        AssertReadOnly(receipt.CandidateIds); AssertReadOnly(receipt.CandidateLocations); AssertReadOnly(receipt.SelectedIds);
        var retained = g.CreateSnapshot(0); var retainedJson = SnapshotJson.Serialize(retained);
        Require(P(g)!.IsPrivate && Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null) &&
                g.CreateSnapshot(0).Players[source].Hand.Count == 0,
            "Only the actual owner chooser sees this source prompt, and its foreign Hand remains hidden.");
        Reject(g, new AnswerPromptCommand(source, P(g)!.PromptId, P(g)!.Choices[0].Id, g.Revision));
        Reject(g, new AnswerPromptCommand(0, P(g)!.PromptId, new("liejie-unpublished"), g.Revision));
        g = Cold(g, registry); Answer(g, c => c.Cards.Contains(sourceArmor));
        g = Cold(g, registry); Answer(g, c => Action(c) == "liejie-pick" && c.Parameters.GetValueOrDefault("source-zone") == "Hand");
        ReachState(g, () => g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(f => f.Change.TargetSeat == source));
        receipt = Parent(g).LiejieDiscardPending!;
        Require(receipt.Paid && receipt.SelectedIds.Count == 2 && receipt.BatchId is not null &&
                receipt.SelectedIds.Contains(sourceArmor) && receipt.SelectedIds.Any(sourceHand.Contains) &&
                g.CardMovements.Count(m => m.Reason.Value == SourceReason) == 2 &&
                !Facts<ProgramBindingResolvedEvent>(g).Any(e => e.FrameId == id),
            "The selected source armor and Hand slot pay one native atomic HE invoice, retaining the owning frame through source Silver Lion recovery.");
        g = Cold(g, registry); Reach(g, p => IsWatch(p, "discard"));
        var movement = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single();
        Require(movement.Batch.Id == receipt.BatchId && movement.Batch.ParentFrameId == id &&
                movement.Batch.AwaitingProgramFrameId == id && movement.ResumeProgramFrameId is null &&
                movement.Batch.Movements.Select(m => m.CardId).ToHashSet().SetEquals(receipt.SelectedIds),
            "The paid source discard resumes from its exact native batch and typed parent rather than reissuing a selection or cost.");
        g = Cold(g, registry); Continue(g); ReachPlay(g);
        Require(Facts<ProgramLiejieSourceDiscardEvent>(g).Count(e => e.FrameId == id && e.DiscardCount == 2) == 1 &&
                Facts<ProgramLiejieSourceDiscardResolvedEvent>(g).Count(e => e.FrameId == id && e.Count == 2) == 1 &&
                Facts<ProgramBindingResolvedEvent>(g).Count(e => e.FrameId == id && e.Completed) == 1 &&
                g.CardMovements.Count(m => m.Reason.Value == SourceReason) == 2 &&
                g.CardMovements.Count(m => m.Reason.Value == CostReason) == 2 &&
                g.CardMovements.Count(m => m.Reason.Value == DrawReason) == 2 &&
                SnapshotJson.Serialize(retained) == retainedJson,
            "Both real costs, native draw and original completion occur once across every cold return; retained private views stay frozen.");
        AssertReadOnly(Facts<ProgramLiejieSourceDiscardEvent>(g).Single(e => e.FrameId == id).DiscardedCardIds);
        _ = Cold(g, registry);
    }

    public static void ZeroDeclineProtectionAndGeneralWeaponsKeepPhysicalBoundaries()
    {
        foreach (var variant in new[] { "zero", "decline", "protected", "source-less", "general-weapon" })
        {
            var (g, registry) = Start(variant); var source = 1;
            if (variant == "protected")
            {
                Use(g, "equipment", source); ReachPlay(g);
                for (var i = 0; i < 4; i++)
                {
                    Use(g, "drain", source); Reach(g, p => HasAction(p, "select-and-move-owned-card"));
                    Answer(g, c => Action(c) == "select-and-move-owned-card"); ReachPlay(g);
                }
                Require(g.CreateSnapshot(source).Players[source].HandCount == 0 && g.CreateSnapshot(0).Players[source].Equipment.Count == 1,
                    "The protection branch genuinely retains only its native protected equipment after four actual Hand discards.");
            }
            int? ownWeapon = null; int? foreignWeapon = null;
            if (variant == "general-weapon")
            {
                Use(g, "general-weapon"); Reach(g, p => HasAction(p, "advanced-lifecycle"));
                Answer(g, c => c.Parameters.GetValueOrDefault("advanced-value") != "finish");
                Answer(g, c => c.Parameters.GetValueOrDefault("advanced-value") == "finish"); ReachPlay(g);
                ownWeapon = g.CreateSnapshot(0).Players[0].Equipment.Single(e => e.Kind == CardKind.GeneralWeapon).Id;
                foreignWeapon = g.CreateSnapshot(0).Players[source].Equipment.Single(e => e.Kind == CardKind.GeneralWeapon).Id;
            }
            Use(g, variant == "source-less" ? "source-less" : "incoming", variant == "source-less" ? null : source);
            Activate(g); Reach(g, p => HasAction(p, "select-owned-cards")); var id = Parent(g).Id;
            if (ownWeapon is { } wid) Answer(g, c => c.Cards.Contains(wid));
            Answer(g, c => Action(c) == "select-owned-cards" && c.Cards.All(card => card != ownWeapon));
            Answer(g, c => Action(c) == "finish-owned-cards");
            if (variant is "decline" or "general-weapon")
            {
                Reach(g, p => HasAction(p, "liejie-pick")); g = Cold(g, registry);
                if (foreignWeapon is { } fw) Answer(g, c => c.Cards.Contains(fw));
                else Answer(g, c => Action(c) == "liejie-decline");
            }
            ReachPlay(g);
            Require(Facts<ProgramBindingResolvedEvent>(g).Count(e => e.FrameId == id && e.Completed) == 1 &&
                    Facts<ProgramLiejieCostColorFrozenEvent>(g).Count(e => e.FrameId == id) == 1 &&
                    g.CardMovements.Count(m => m.Reason.Value == DrawReason) == (ownWeapon is null ? 1 : 2),
                "Zero, decline, protection, source-less and generated-weapon cases finish their real original payment and bound draw exactly once: " + variant);
            if (foreignWeapon is { } fid)
                Require(g.CardMovements.Single(m => m.CardId == ownWeapon && m.Reason.Value == CostReason).To == CardLocation.OutsideGame &&
                        g.CardMovements.Single(m => m.CardId == fid && m.Reason.Value == SourceReason).To == CardLocation.OutsideGame &&
                        Facts<ProgramLiejieSourceDiscardEvent>(g).Single(e => e.FrameId == id).DiscardedCardIds.SequenceEqual([fid]),
                    "Both genuinely generated general weapons follow native equipment-to-OutsideGame normalization while counting their exact paid entity once.");
            else Require(!Facts<ProgramLiejieSourceDiscardEvent>(g).Any(e => e.FrameId == id) &&
                         !g.CardMovements.Any(m => m.Reason.Value == SourceReason),
                "Zero quota, explicit decline, protected-only HE and source-less damage create no invented source payment: " + variant);
            if (variant == "zero") Require(Cost(g, id).RedCount == 0, "Printed black cards without a suit policy freeze zero red quota.");
            if (variant == "protected") Require(g.CreateSnapshot(0).Players[source].Equipment.Count == 1, "Native foreign equipment protection remains effective.");
            _ = Cold(g, registry);
        }
        var invalid = JsonNode.Parse(FixtureRules("zero"))!;
        var liejie = ActualLiejieProgram();
        liejie["triggers"]![0]!["effects"]![0]!["allowDecline"] = true;
        invalid["skills"]!.AsArray().Add(liejie);
        try { _ = SkillProgramCatalog.Load(invalid.ToJsonString(), Presentation(includeLiejie: true)); }
        catch (InvalidOperationException e) when (e.Message.Contains("Liejie", StringComparison.OrdinalIgnoreCase)) { return; }
        throw new InvalidOperationException("The terminal Liejie contract must reject a zero-cost first-clause allowDecline override.");
    }

    private static JsonNode ActualLiejieProgram()
    {
        var assembly = typeof(StandardClassicGeneralPackage).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("ol-wei-zi.rules.json", StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return JsonNode.Parse(reader.ReadToEnd())!["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == Skill)!.DeepClone();
    }
    private static ProgramLiejieCostColorFrozenEvent Cost(GameEngine g, long id) => Facts<ProgramLiejieCostColorFrozenEvent>(g).Single(e => e.FrameId == id);
    private static ProgramSkillFrame Parent(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Skill);
    private static PendingDecision? P(GameEngine g) => g.CreateSnapshot(0).PendingDecision;
    private static T[] Facts<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static string? Action(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private static bool HasAction(PendingDecision p, string action) => p.Choices.Any(c => Action(c) == action);
    private static bool IsWatch(PendingDecision p, string bind) => p.SkillPrompt?.SkillId == Watch && p.Choices.Any(c => c.Parameters.GetValueOrDefault("result-bind") == bind);
    private static void Use(GameEngine g, string id, int? target = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], target is { } s ? [s] : [], g.Revision, P(g)!.PromptId));
    private static void Activate(GameEngine g) { Reach(g, p => p.SkillPrompt?.SkillId == Skill && HasAction(p, "activate")); Answer(g, c => Action(c) == "activate"); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) => Accept(g, new AnswerPromptCommand(0, P(g)!.PromptId, P(g)!.Choices.First(predicate).Id, g.Revision));
    private static void ReachPlay(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate) => ReachState(g, () => P(g) is { } p && predicate(p));
    private static void ReachState(GameEngine g, Func<bool> predicate)
    {
        for (var i = 0; i < 192; i++)
        {
            if (predicate()) return;
            if (P(g) is { } unexpected) throw new InvalidOperationException("Unexpected Liejie native boundary: " + JsonSerializer.Serialize(unexpected));
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixed Liejie fixture exceeded its bounded native stage.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack), g.CardMovements,
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Require(State(g) == State(cold), "Cold command replay preserves the exact paid HE invoices, private choices, native children and accepted prefix."); return cold; }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Require(result.Accepted, result.Error?.Message ?? "A real Liejie fixture command rejected."); }
    private static void Reject(GameEngine g, GameCommand c) { var before = State(g); var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Require(!result.Accepted && result.Error is not null && State(g) == before, "Wrong actor or unpublished HE choice rejects atomically."); }
    private static void AssertReadOnly<T>(IReadOnlyList<T> list)
    {
        if (list is not IList<T> mutable) return;
        try { mutable.Add(default!); }
        catch (NotSupportedException) { return; }
        throw new InvalidOperationException("An exposed Liejie collection remained mutable.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static (GameEngine, ContentRegistry) Start(string variant)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(variant));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 17, PlayerCount = 4, ModeId = Mode, HumanSeat = 0, HumanRole = Role.Lord, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 3 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); ReachPlay(g); return (g, r);
    }
    private sealed class Fixture(string variant) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-liejie-source", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var catalog = SkillProgramCatalog.Load(FixtureRules(variant), Presentation());
            foreach (var p in catalog.Programs.Values) b.AddSkill(new(p.Id, catalog.Presentations[p.Id].Name, catalog.Presentations[p.Id].Description) { Program = p });
            b.AddGeneral(new(Owner, "烈节原生观察", "supporter", Skill, "wei", BaseHp: 8,
                AdditionalSkillIds: new[] { Driver }.Concat(variant == "zero" ? [] : new[] { Red }).Concat(variant == "children" ? [Watch] : Array.Empty<string>()).ToArray()));
            var peers = Enumerable.Range(1, variant == "general-weapon" ? 7 : 3).Select(i => $"fixture:liejie-peer-{i}").ToArray();
            for (var i = 0; i < peers.Length; i++) b.AddGeneral(new(peers[i], "原生来源", "supporter", variant == "general-weapon" ? Weapon : variant == "protected" ? Protected : "standard:none", "wei", BaseHp: variant == "children" && i > 0 ? 1 : 8,
                AdditionalSkillIds: variant == "children" ? [Watch] : Array.Empty<string>()));
            b.AddDeck(new("fixture:liejie-deck", "固定实体牌堆", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("classic:silver-lion", Suit.Spade, 5)).ToArray() });
            b.AddMode(new(Mode, "烈节原生付款", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:liejie-deck", GeneralCandidateCount: peers.Length + 1, GeneralPoolIds: [Owner, .. peers]));
        }
    }
    private static string FixtureRules(string variant) => $$$"""
    {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
      {"id":"{{{Driver}}}","revision":1,"activations":[
        {"id":"equipment","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
        {"id":"wound","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
        {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
        {"id":"source-less","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"receiveOwnerDamage","target":"owner","amount":1}]},
        {"id":"general-weapon","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"equipSampledGenerals","target":"owner","amount":1}]},
        {"id":"drain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLivingWithHand","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]}]},
      {"id":"{{{Red}}}","revision":1,"cardPolicies":[{"id":"effective-red","kind":"rewriteSuit","inputSuit":"spade","outputSuit":"heart"}]},
      {"id":"{{{Protected}}}","revision":1,"cardPolicies":[{"id":"protected-equipment","kind":"preventForeignEquipmentDiscard","cardKinds":[]}]},
      {"id":"{{{Weapon}}}","revision":1,"triggers":[{"id":"native-general-weapon","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"equipSampledGenerals","target":"owner","amount":1}]}]},
      {"id":"{{{Watch}}}","revision":1,"triggers":[
        {"id":"recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"recovery","options":[{"id":"continue"}]}]},
        {"id":"draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{{DrawReason}}}"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"draw","options":[{"id":"continue"}]},{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"damage","target":"selectedTarget","amount":1},{"op":"chooseOption","target":"owner","resultBind":"draw-done","options":[{"id":"continue"}]}]},
        {"id":"discard","window":"discardPileReceived","subject":"owner","discardOwnerScope":"other","sourceZones":["hand","equipment"],"movementDiscardOnly":true,"movementOccurrence":"perBatch","movementReasons":["{{{SourceReason}}}"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"discard","options":[{"id":"continue"}]}]}]}
    ]}
    """;
    private static string Presentation(bool includeLiejie = false) => JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
        skills = new[] { Driver, Watch, Red, Protected, Weapon }.Concat(includeLiejie ? [Skill] : Array.Empty<string>()).ToDictionary(id => id,
            id => new { name = id, description = "真实实体付款和原生子结算", optionLabels = id == Watch ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() }) });
}
