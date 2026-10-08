using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class KuangfuChecks
{
    private const string Mode = "identity:classic-kuangfu-fixture", Driver = "fixture:kf-driver", Clone = "fixture:kf-clone";
    private const string Hp = "fixture:kf-hp", Cost = "fixture:kf-cost", Changed = "fixture:kf-changed", Pulse = "fixture:kf-pulse";
    private const string Gain = "fixture:kf-gain", Completed = "fixture:kf-completed", Skill = "ol:kuangfu";
    private const string GearReason = "skill-program.kuangfu.equipment-cost", HandReason = "skill-program.kuangfu.no-damage-hand-cost";

    public static void KuangfuOwnEquipmentRecoveryAndWholeUseCompletionReturnOnce()
    {
        var (g, r) = Create(); Play(g); Equip(g, 0); var gear = g.CreateSnapshot(0).Players[0].Equipment.Single().Id;
        var before = g.State.Players[0].HandCount; Start(g); var initial = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Kuangfu is not null);
        var initialState = State(g); var revision = g.Revision;
        Require(!g.Submit(new AnswerPromptCommand(0, P(g)!.PromptId, new("kuangfu.forged"), g.Revision)).Accepted &&
            revision == g.Revision && State(g) == initialState, "A forged public payment choice changes no frame, view, movement or accepted journal.");
        g = Cold(g, r); Pick(g, c => c.Cards.SequenceEqual([gear]) && c.Targets.SequenceEqual([0])); Reach(g, p => p.SkillPrompt?.SkillId == Hp);
        var paid = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == initial.Id);
        Require(paid.Kuangfu is { Stage: KuangfuStage.EquipmentChildren, EquipmentOwnerSeat: 0, EquipmentKind: CardKind.SilverLion } &&
            paid.Kuangfu.EquipmentCardId == gear && E<KuangfuSlashIssuedEvent>(g).Length == 0 &&
            g.CardMovements.Count(m => m.CardId == gear && m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile && m.Reason.Value == GearReason) == 1 &&
            g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(h => h.ResumeFrameId == paid.Id && h.Continuation == PostEventContinuation.AwaitedProgramMovement),
            "The original Silver Lion is really discarded once; its real recovery child precedes Slash issuance under the exact payment frame.");
        g = Cold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Skill && Has(p, "kuangfu")); Pick(g, c => c.Targets.SequenceEqual([2]));
        Reach(g, p => p.SkillPrompt?.SkillId == Completed); var issued = E<KuangfuSlashIssuedEvent>(g).Single().Return;
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == issued.CardUseFrameId);
        Require(use.KuangfuSlashReturn == issued && use.CardId == 0 && use.PhysicalCardIds?.Count == 0 && use.Action is { Type: CardActionType.Use } action &&
            action.ActionId == issued.CardActionId && action.EffectiveKind == CardKind.Slash &&
            E<CardUseDebitRecordedEvent>(g).Count(e => e.Debit.CardActionId == action.ActionId) == 1 &&
            E<KuangfuActualDamageObservedEvent>(g).Any(e => e.CardUseFrameId == use.Id && e.Amount > 0) && E<KuangfuDrawIssuedEvent>(g).Length == 0,
            "A real distant virtual Slash debits normal quota and damages through its exact Use; its completed-use observer finishes before any ownership reward.");
        g = Cold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Gain);
        var outcome = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == initial.Id);
        Require(outcome.Kuangfu is { Stage: KuangfuStage.OutcomeChildren, CausedDamage: true, ActualDraw: 2 } &&
            E<KuangfuSlashResolvedEvent>(g).Single().CardActionId == issued.CardActionId && E<KuangfuDrawIssuedEvent>(g).Single().Actual == 2 &&
            g.State.Players[0].HandCount == before + 2 && E<KuangfuHandDiscardPaidEvent>(g).Length == 0,
            "Own-equipment success draws exactly two after the whole use, with no foreign penalty or guessed turn damage counter.");
        g = Cold(g, r); Continue(g); Play(g);
        Require(E<KuangfuFinishedEvent>(g).Count(e => e.ProgramFrameId == initial.Id) == 1 && !g.ResolutionStack.Any(f => f.Id == initial.Id) &&
            !g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Skill), "Paid children return once and the once-per-Play activation remains spent.");
        g = Cold(g, r);
    }

    public static void KuangfuForeignPreventionIgnoresPaidSkillsChangedDyingDamage()
    {
        var (g, r) = Create(prevent: true, nested: true); Play(g); Equip(g, 1);
        var gear = g.CreateSnapshot(0).Players[1].Equipment.Single().Id; var hand = g.CreateSnapshot(0).Players[0].Hand.Select(c => c.Id).ToArray();
        Start(g); Pick(g, c => c.Cards.SequenceEqual([gear])); Reach(g, p => p.SkillPrompt?.SkillId == Cost); g = Cold(g, r); Continue(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Cost && Has(p, "advanced-lifecycle"));
        Pick(g, c => c.Parameters.GetValueOrDefault("advanced-value") == Skill); g = Cold(g, r); Pick(g, c => c.Parameters.GetValueOrDefault("advanced-value") == "finish");
        Reach(g, p => p.SkillPrompt?.SkillId == Changed); var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Kuangfu is not null);
        Require(root.Kuangfu is { Stage: KuangfuStage.EquipmentChildren, EquipmentOwnerSeat: 1 } &&
            g.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(w => w.Window == SkillProgramTriggerWindow.SkillsChanged) &&
            !g.CreateSnapshot(0).Players[0].Skills!.Any(s => s.ContentId == Skill), "The paid foreign entity persists while a real source-removal SkillsChanged child is suspended.");
        g = Cold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Pulse);
        Require(g.ResolutionStack.OfType<DyingFrame>().Any(d => d.VictimSeat == 0 && d.Continuation == DyingContinuationKind.Damage &&
            g.ResolutionStack.OfType<DamageFrame>().Any(damage => damage.Id == d.ParentFrameId && damage.TargetSeat == 0 && damage.Amount == 2 &&
                g.ResolutionStack.OfType<ProgramSkillFrame>().Any(observer => observer.Id == damage.ParentFrameId && observer.SkillId == Changed))) &&
            E<KuangfuSlashIssuedEvent>(g).Length == 0 && E<KuangfuActualDamageObservedEvent>(g).Length == 0,
            "Collateral paid observer damage enters actual Dying without becoming damage of an unissued Slash.");
        g = Cold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Hp); g = Cold(g, r); Continue(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Skill && Has(p, "kuangfu")); Pick(g, c => c.Targets.SequenceEqual([2]));
        Reach(g, p => p.SkillPrompt?.SkillId == Completed); g = Cold(g, r); Continue(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Skill && p.IsPrivate); var penalty = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == root.Id);
        Require(penalty.Kuangfu is { Stage: KuangfuStage.HandDiscardChoice, CausedDamage: false, DiscardRequired: 2 } &&
            E<KuangfuSlashResolvedEvent>(g).Single().CausedDamage == false && E<KuangfuActualDamageObservedEvent>(g).Length == 0 &&
            E<DamageAppliedEvent>(g).Any(e => e.Amount > 0) && E<KuangfuDrawIssuedEvent>(g).Length == 0,
            "Prevented exact-use damage is false despite real earlier collateral damage and source loss; the original foreign payment selects only own Hand.");
        Require(Enumerable.Range(1, 3).All(v => g.CreateSnapshot(v).PendingDecision?.Choices.Count is null or 0), "Foreign viewers do not see private hand identities or choices.");
        g = Cold(g, r); Pick(g, c => c.Cards.SequenceEqual([hand[0]]));
        Require(!g.CardMovements.Any(m => m.CardId == hand[0] && m.Reason.Value == HandReason), "The first choice freezes material and never partially pays the two-Hand invoice.");
        g = Cold(g, r); Pick(g, c => c.Cards.SequenceEqual([hand[1]])); Play(g);
        var invoice = E<KuangfuHandDiscardPaidEvent>(g).Single(); Freeze(invoice.CardIds);
        Require(invoice.CardIds.SequenceEqual(hand.Take(2)) && invoice.ProgramFrameId == root.Id && E<KuangfuSlashIssuedEvent>(g).Length == 1 &&
            hand.Take(2).All(id => g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile && m.Reason.Value == HandReason) == 1) &&
            g.CardMovements.Count(m => m.CardId == gear && m.Reason.Value == GearReason) == 1,
            "Cold recovery drains nested SkillsChanged/Dying and pays the original equipment, exact virtual Use and Hand penalty once each.");
        var input = new[] { 7 }; var frozen = invoice with { CardIds = input }; input[0] = 8;
        Require(frozen.CardIds[0] == 7, "Collection-bearing new facts detach init/with inputs as well as committed projections.");
        g = Cold(g, r);
    }

    public static void KuangfuOwnershipDamageMatrixAndPartialHandPenalty()
    {
        foreach (var own in new[] { false, true }) foreach (var prevent in new[] { false, true })
        {
            var (g, r) = Create(prevent: prevent); Play(g); Equip(g, own ? 0 : 1);
            if (!own && prevent) Trim(g);
            var gear = g.CreateSnapshot(0).Players[own ? 0 : 1].Equipment.Single().Id; var before = g.State.Players[0].HandCount;
            Start(g); Pick(g, c => c.Cards.SequenceEqual([gear]));
            if (own) { Reach(g, p => p.SkillPrompt?.SkillId == Hp); Continue(g); }
            Reach(g, p => p.SkillPrompt?.SkillId == Skill && Has(p, "kuangfu")); Pick(g, c => c.Targets.SequenceEqual([2]));
            Reach(g, p => p.SkillPrompt?.SkillId == Completed); g = Cold(g, r); Continue(g);
            if (own && !prevent) { Reach(g, p => p.SkillPrompt?.SkillId == Gain); Continue(g); }
            if (!own && prevent)
            {
                Reach(g, p => p.SkillPrompt?.SkillId == Skill && p.IsPrivate);
                var receipt = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Kuangfu is not null).Kuangfu!;
                Require(before == 1 && receipt.DiscardRequired == 1, "Fewer than two Hand means one actual remaining Hand entity, not invented cards or equipment.");
                g = Cold(g, r); Pick(g, c => c.Cards.Count == 1);
            }
            Play(g); var result = E<KuangfuSlashResolvedEvent>(g).Single();
            Require(result.CausedDamage == !prevent && E<KuangfuDrawIssuedEvent>(g).Length == (own && !prevent ? 1 : 0) &&
                E<KuangfuHandDiscardPaidEvent>(g).Length == (!own && prevent ? 1 : 0) &&
                g.State.Players[0].HandCount == before + (own && !prevent ? 2 : !own && prevent ? -1 : 0),
                "All four original-equipment ownership/accurate-use damage combinations use their exact prescribed successor.");
            g = Cold(g, r);
        }
    }

    public static void KuangfuSpentQuotaRejectsDiscardingItsOwnCrossbow()
    {
        var (g, r) = Create(crossbow: true); Play(g); Equip(g, 0); Equip(g, 1);
        var crossbow = g.CreateSnapshot(0).Players[0].Equipment.Single().Id; var foreign = g.CreateSnapshot(0).Players[1].Equipment.Single().Id;
        Start(g, Clone); Pick(g, c => c.Cards.SequenceEqual([foreign])); Reach(g, p => p.SkillPrompt?.SkillId == Clone && Has(p, "kuangfu"));
        Pick(g, c => c.Targets.SequenceEqual([2])); Reach(g, p => p.SkillPrompt?.SkillId == Completed); Continue(g); Play(g);
        Require(E<CardUseDebitRecordedEvent>(g).Length == 1 && g.CreateSnapshot(0).Players[0].Equipment.Any(c => c.Id == crossbow) &&
            !g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Skill),
            "After an actual normally counted Slash, the sole own Crossbow cannot finance an ordinary Slash whose quota becomes illegal when that exact equipment leaves.");
        var state = State(g); var revision = g.Revision;
        Require(!g.Submit(new UseProgramSkillCommand(0, Skill, "equipment-slash", [], [], g.Revision, P(g)!.PromptId)).Accepted &&
            revision == g.Revision && State(g) == state && !g.CardMovements.Any(m => m.CardId == crossbow && m.Reason.Value == GearReason),
            "The cost is rejected before real equipment payment, without creating an unused Slash or accepting a command.");
        g = Cold(g, r);
    }

    private static PendingDecision? P(GameEngine g) => g.PendingDecision;
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static bool Has(PendingDecision p, string action) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected fixed command."); }
    private static void Pick(GameEngine g, Func<PromptChoice, bool> predicate) => Accept(g, new AnswerPromptCommand(0, P(g)!.PromptId, P(g)!.Choices.Single(predicate).Id, g.Revision));
    private static void Continue(GameEngine g) => Pick(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Use(GameEngine g, string skill, string activation, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, skill, activation, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Equip(GameEngine g, int seat) { Use(g, Driver, "equip", [seat]); Play(g); }
    private static void Start(GameEngine g, string skill = Skill) { Use(g, skill, "equipment-slash"); Reach(g, p => p.SkillPrompt?.SkillId == skill && Has(p, "kuangfu")); }
    private static void Trim(GameEngine g)
    {
        var remaining = g.State.Players[0].HandCount;
        for (var step = 0; step < remaining - 1; step++)
        {
            var id = g.CreateSnapshot(0).Players[0].Hand[0].Id;
            Accept(g, new UseProgramSkillCommand(0, Driver, "trim-hand", [id], [], g.Revision, P(g)!.PromptId)); Play(g);
            Require(g.State.Players[0].HandCount == remaining - step - 1,
                "Each actual trim command discards exactly one Hand entity and returns to the original Play boundary.");
        }
        Require(g.State.Players[0].HandCount == 1, "The bounded trim leaves exactly one actual Hand entity.");
    }
    private static void Play(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    {
        for (var i = 0; i < 160; i++)
        {
            if (P(g) is { } p && predicate(p)) return;
            if (P(g) is { } other) throw new InvalidOperationException("Unexpected fixed boundary " + other.Kind + "/" + other.SkillPrompt?.SkillId);
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Bounded real command prefix did not reach its expected boundary.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(g.CreateSnapshot(v))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements,
        Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Require(State(g) == State(restored), "Cold accepted-journal replay retains all views, exact typed parents, private choices, facts and movement invoices."); return restored; }
    private static void Freeze<T>(IReadOnlyList<T> list)
    { if (list is IList<T> mutable && list.Count > 0) { try { mutable[0] = mutable[0]; } catch (NotSupportedException) { return; } throw new InvalidOperationException("A committed collection is writable."); } }
    private static (GameEngine, ContentRegistry) Create(bool prevent = false, bool nested = false, bool crossbow = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(prevent, nested, crossbow));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, "fixture:kf-owner", g.Revision, P(g)!.PromptId)); return (g, r);
    }
    private sealed class Fixture(bool prevent, bool nested, bool crossbow) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-kuangfu", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = JsonNode.Parse(FixtureRules)!; rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            if (nested) rules["skills"]![3]!["triggers"]![0]!["effects"]!.AsArray().Add(JsonNode.Parse("""{"op":"replaceSkillsOnAwakening","target":"owner","skillIds":["fixture:kf-noop"]}"""));
            else rules["skills"]!.AsArray().RemoveAt(4);
            var ids = rules["skills"]!.AsArray().Select(n => n!["id"]!.GetValue<string>()).ToArray();
            var presentation = ids.ToDictionary(id => id, id => (object)new
            {
                name = id, description = "真实子选择固定夹具",
                optionLabels = id is Hp or Cost or Changed or Pulse or Gain or Completed
                    ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>()
            });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentation }));
            foreach (var id in ids) b.AddSkill(new(id, id, "实际命令夹具") { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:kf-noop", "替换后的实际来源", "No program"));
            b.AddSkill(new("fixture:kf-pick-owner", "固定主人", "Passive") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == Role.Lord ? 10000d : -10000d) });
            b.AddSkill(new("fixture:kf-pick-peer", "固定他人", "Passive") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role != Role.Lord ? 10000d : -10000d) });
            var ownerSkills = new List<string> { Skill, Clone, Hp, Gain, Completed, Pulse, "fixture:kf-pick-owner" };
            if (nested) ownerSkills.AddRange([Cost, Changed]);
            b.AddGeneral(new("fixture:kf-owner", "狂斧实际付款者", "supporter", Driver, "qun", 6, ownerSkills, GeneralGender.Male) { InitialHp = crossbow ? null : 1 });
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:kf-peer-{i}", "固定原参与者", "supporter", "fixture:kf-pick-peer", "wei", 8,
                prevent ? ["fixture:kf-quiet", "fixture:kf-prevent"] : ["fixture:kf-quiet"], GeneralGender.Male));
            b.AddDeck(new("fixture:kf-deck", "小固定装备实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard(crossbow ? "standard:crossbow" : "classic:silver-lion", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(Mode, "狂斧实际父子返回", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:kf-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:kf-owner", "fixture:kf-peer-1", "fixture:kf-peer-2", "fixture:kf-peer-3"]));
        }
    }
    private const string FixtureRules = """
    {"skills":[
      {"id":"fixture:kf-driver","revision":1,"activations":[
        {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
        {"id":"trim-hand","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]}]},
      {"id":"fixture:kf-clone","revision":1,"activations":[{"id":"equipment-slash","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerPhase":1,"effects":[{"op":"discardEquipmentThenSlashAndOwnershipOutcome","target":"owner"}]}]},
      {"id":"fixture:kf-hp","revision":1,"triggers":[{"id":"hp","window":"afterHpRecovered","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
      {"id":"fixture:kf-cost","revision":1,"triggers":[{"id":"cost","window":"discardPileReceived","subject":"owner","sourceZones":["equipment"],"discardOwnerScope":"other","movementOccurrence":"perBatch","movementReasons":["skill-program.kuangfu.equipment-cost"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
      {"id":"fixture:kf-changed","revision":1,"triggers":[{"id":"changed","window":"skillsChanged","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]},{"op":"damage","target":"owner","amount":2}]}]},
      {"id":"fixture:kf-pulse","revision":1,"triggers":[{"id":"rescue","window":"selfDyingResponse","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]},{"op":"recover","target":"owner","amount":1}]}]},
      {"id":"fixture:kf-gain","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.kuangfu.own-equipment-draw"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
      {"id":"fixture:kf-completed","revision":1,"triggers":[{"id":"completed","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
      {"id":"fixture:kf-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
      {"id":"fixture:kf-prevent","revision":1,"triggers":[{"id":"prevent","window":"beforeDamageApplied","subject":"damageTarget","optional":false,"effects":[{"op":"preventCurrentDamage","target":"owner"}]}]}
    ]}
    """;
}
