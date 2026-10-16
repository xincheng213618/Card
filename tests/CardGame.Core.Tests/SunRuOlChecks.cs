using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class SunRuOlChecks
{
    private const string Chi = "ol:chishi", Wei = "ol:weimian", Driver = "fixture:sr-driver", Quiet = "fixture:sr-quiet";
    private const string Watch = "fixture:sr-watch", Gain = "fixture:sr-gain", Hp = "fixture:sr-hp", Owner = "fixture:sr-owner", Mode = "identity:classic-sunru-ol-fixture";

    public static void ActualCurrentActorLastZoneLossOncePerTurnAndColdReturn()
    {
        var (g, registry) = Start("loss");
        for (var i = 0; i < 2; i++)
        { Use(g, "foreign-discard", [1]); Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("source-zone") == "Hand") == true); Answer(g, _ => true); Play(g); }
        Need(V(g, 1).HandCount == 0 && !E<SunRuOlChishiAppliedEvent>(g).Any() && !E<ProgramBindingStartedEvent>(g).Any(e => e.SkillId == Chi),
            "Losing another actor's last hand entity during Sun Ru's actual turn never offers Chishi.");
        var lastDiscarded = V(g, 0).Hand.Last().Id;
        Use(g, "empty-hand", []); Reach(g, () => Optional(P(g), Chi));
        var boundary = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Last();
        Need(boundary.Batch.SunRuOlLastZoneLoss is { OwnerSeat: 0 } token && token.TurnNumber == g.State.TurnNumber &&
            boundary.Batch.SourceCounts.Any(c => c.Location == CardLocation.Hand(0) && c.CountBefore == 1 && c.CountAfter == 0) &&
            boundary.Batch.Movements is [var last] && last.CardId == lastDiscarded,
            "The real native atomic batch freezes the actual current-turn owner and positive-before/zero-after region loss.");
        g = Cold(g, registry); Activate(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Gain);
        Need(V(g, 0).HandCount == 2 && E<SunRuOlChishiAppliedEvent>(g).Count() == 1 &&
            E<TurnRuleModifierGrantedEvent>(g).Single(e => e.Modifier.Source.SkillId == Chi).Modifier is { AffectedSeat: 0, Amount: 2, Query: SkillRuleQuery.HandLimit },
            "An actual accepted Chishi draws two and issues one native current-turn +2 hand limit to the losing actor before its gain child.");
        g = Cold(g, registry); Continue(g); Play(g); Use(g, "empty-hand", []); Play(g);
        Need(V(g, 0).HandCount == 0 && E<SunRuOlChishiAppliedEvent>(g).Count() == 1 &&
            E<ProgramBindingStartedEvent>(g).Count(e => e.SkillId == Chi) == 1,
            "A second real last-zone loss in the same actual turn cannot activate again, including after cold child return.");
        var first = E<SunRuOlChishiAppliedEvent>(g).Single(); End(g); Play(g);
        Use(g, "draw", []); Play(g); Use(g, "empty-hand", []); Reach(g, () => Optional(P(g), Chi));
        Activate(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Gain); Continue(g); Play(g);
        Need(E<SunRuOlChishiAppliedEvent>(g).Count() == 2 && E<SunRuOlChishiAppliedEvent>(g).Last().Turn.TurnNumber > first.Turn.TurnNumber &&
            E<TurnCardUseEffectsExpiredEvent>(g).Any(), "The next actual game turn has a fresh quota and native current-turn grants expire normally.");
        _ = Cold(g, registry);
    }

    public static void ActualWeimianPaidSlotsRecipientProjectsAndNativeRecoveryCold()
    {
        var (g, registry) = Start("silver"); var lion = V(g, 0).Hand.First();
        Use(g, "equip", [0], [lion.Id]); Play(g);
        var ownHp = V(g, 0).Hp; var targetHp = V(g, 1).Hp; var targetHand = V(g, 1).Hand.Select(c => c.Id).ToArray();
        Weimian(g, 1); Reach(g, () => Branch(P(g), "slots"));
        Need(P(g) is { PlayerSeat: 0, IsPrivate: true } && g.CreateSnapshot(1).PendingDecision is null,
            "Sun Ru alone chooses her actual native available slot combination before payment.");
        g = Cold(g, registry); Pick(g, "slots", 7); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Hp);
        Need(V(g, 0).Hp == ownHp + 1 && V(g, 0).Equipment.Count == 0 && E<SunRuOlWeimianPaidEvent>(g).Single().SlotsMask == 7 &&
            g.CardMovements.Count(m => m.CardId == lion.Id && m.Reason.Value == "equipment.slot-abolished") == 1,
            "Three distinct slots are paid once; actual Silver Lion removal heals its wounded wearer before the paid cost's HP child.");
        g = Cold(g, registry); Continue(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
        var costWindow = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Last();
        Need(costWindow.Batch.SunRuOlLastZoneLoss is { OwnerSeat: 0 } && costWindow.Candidates.Any(c => c.SkillId == Chi),
            "Actual equipment loss retains a Chishi candidate: " + JsonSerializer.Serialize(costWindow));
        g = Cold(g, registry); Continue(g); Reach(g, () => Optional(P(g), Chi) || P(g)?.Kind == DecisionKind.PlayCard || Branch(P(g), "benefits"));
        Need(Optional(P(g), Chi), "Chishi after actual equipment watcher: " + JsonSerializer.Serialize(new { costWindow, Facts = E<SunRuOlChishiAppliedEvent>(g).ToArray(), Bindings = E<ProgramBindingStartedEvent>(g).Where(e => e.SkillId == Chi).ToArray(), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Prompt = P(g) }));
        Activate(g);
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == Gain); g = Cold(g, registry); Continue(g);
        Reach(g, () => Branch(P(g), "benefits"));
        Need(P(g) is { PlayerSeat: 1, IsPrivate: true } p && p.Choices.Count == 1 && g.CreateSnapshot(0).PendingDecision is null &&
            E<SunRuOlWeimianBenefitIssuedEvent>(g).Count() == 0,
            "After every real equipment child returns, the recipient privately chooses exactly three distinct projects and the source cannot see the prompt.");
        g = Cold(g, registry); Pick(g, "benefits", 7); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Hp);
        Need(V(g, 1).Hp == targetHp + 1 && !g.ResolutionStack.OfType<RecoveryReplacementFrame>().Any(),
            "The selected one-HP recovery actually applies to the non-current recipient before its HP child; own-turn recovery redirection is correctly ineligible for that recipient.");
        g = Cold(g, registry); Continue(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Gain);
        Need(V(g, 1).HandCount == 4 && targetHand.All(id => g.CardMovements.Count(m => m.CardId == id && m.Reason.Value == "skill-program.sun-ru.ol.weimian.discard") == 1),
            "The refresh discards every actual recipient hand entity before drawing four, without touching the source's Chishi draws.");
        g = Cold(g, registry); Continue(g); Play(g);
        Need(E<SunRuOlWeimianPaidEvent>(g).Count() == 1 && E<SunRuOlWeimianBenefitsChosenEvent>(g).Single().BenefitsMask == 7 &&
            E<SunRuOlWeimianBenefitIssuedEvent>(g).Select(e => e.Benefit).SequenceEqual([1, 2, 3]) &&
            E<SunRuOlChishiAppliedEvent>(g).Count() == 1 && E<EquipmentSlotCapacityChangedEvent>(g).Count(e => e.Seat == 0 && e.Capacity == 0) == 3,
            "Cold cost, HP, movement, recipient and recovery returns never repeat the three paid slots, optional Chishi or any chosen project.");
        _ = Cold(g, registry);

        var (replacement, replacementRegistry) = Start("replacement");
        var beforeRecovery = V(replacement, 0).Hp;
        Weimian(replacement, 0); Reach(replacement, () => Branch(P(replacement), "slots")); Pick(replacement, "slots", 7);
        Reach(replacement, () => Branch(P(replacement), "benefits")); Pick(replacement, "benefits", 7);
        Reach(replacement, () => Branch(P(replacement), "restore")); Pick(replacement, "restore", (int)EquipmentSlot.Armor);
        Reach(replacement, () => P(replacement)?.Kind == DecisionKind.RecoveryReplacement);
        var recovery = replacement.ResolutionStack.OfType<RecoveryReplacementFrame>().Single();
        Need(recovery.Attempt is { TargetSeat: 0, Amount: 1 } && recovery.Attempt.Candidates.Any(c => c.OwnerSeat != 0) &&
            replacement.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == recovery.ParentFrameId && f.SunRuOlWeimian is { Stage: SunRuOlWeimianStage.RecoveryChildren }) &&
            V(replacement, 0).Hp == beforeRecovery,
            "With a distinct actual lord, the current actor's own Weimian heal opens a real native replacement under the exact custom recovery receipt.");
        replacement = Cold(replacement, replacementRegistry); Answer(replacement, c => c.Parameters.GetValueOrDefault("recovery-action") == "keep");
        Reach(replacement, () => P(replacement)?.SkillPrompt?.SkillId == Hp);
        Need(V(replacement, 0).Hp == beforeRecovery + 1, "The actual chosen replacement keeps and applies the one-point request exactly once.");
        replacement = Cold(replacement, replacementRegistry); Continue(replacement);
        Reach(replacement, () => P(replacement)?.SkillPrompt?.SkillId == Gain); replacement = Cold(replacement, replacementRegistry); Continue(replacement); Play(replacement);
        Need(E<SunRuOlWeimianPaidEvent>(replacement).Count() == 1 && E<RecoveryAppliedEvent>(replacement).Count(e => e.TargetSeat == 0) == 1 &&
            E<SunRuOlWeimianBenefitIssuedEvent>(replacement).Select(e => e.Benefit).SequenceEqual([1, 2, 3]),
            "Cold native recovery replacement and downstream hand-refresh children return without repeating any paid cost or project.");
        _ = Cold(replacement, replacementRegistry);
    }

    public static void ActualOtherHolderRewardsCurrentActorAndHandLimitCold()
    {
        var (g, registry) = Start("cross");
        var holder = Enumerable.Range(1, 3).Single(s => V(g, s).Skills!.Any(skill => skill.Id == Chi));
        var holderHand = V(g, holder).HandCount;
        Use(g, "empty-hand", []); Reach(g, () => Optional(P(g), Chi));
        Need(P(g)!.PlayerSeat == holder && g.CreateSnapshot(0).PendingDecision is null,
            "The separate Sun Ru holder privately decides whether to reward the actual turn owner's native last-region loss.");
        g = Cold(g, registry); Activate(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Gain);
        var fact = E<SunRuOlChishiAppliedEvent>(g).Single();
        var grant = E<TurnRuleModifierGrantedEvent>(g).Single(e => e.Modifier.Source.SkillId == Chi).Modifier;
        Need(fact.Source.OwnerSeat == holder && fact.Turn.OwnerSeat == 0 && V(g, 0).HandCount == 2 && V(g, holder).HandCount == holderHand &&
            grant.Source.OwnerSeat == holder && grant.AffectedSeat == 0 && grant.Amount == 2 &&
            E<TurnRuleModifierGrantedEvent>(g).All(e => e.Modifier.AffectedSeat != holder),
            "Cross-owner Chishi draws only for the actual actor and explicitly grants that actor, never the holder, its +2 native hand limit.");
        g = Cold(g, registry); Continue(g); Play(g);
        Need(E<SunRuOlChishiAppliedEvent>(g).Count() == 1 && V(g, 0).HandCount == 2,
            "The actual other-holder movement child cold-returns without repeating its recipient draw or quota.");
        _ = Cold(g, registry);
    }

    public static void ActualWholeEquipmentAreaSingleSlotRestorationAndEmptyRefresh()
    {
        {
            var (g, registry) = Start("whole");
            Need(V(g, 1).IsEquipmentAreaAbolished && !E<SunRuOlChishiAppliedEvent>(g).Any(), "The fixture abolishes a real whole equipment region at setup without inventing an actual current turn.");
            Weimian(g, 1); Reach(g, () => Branch(P(g), "slots")); Pick(g, "slots", 1);
            Reach(g, () => Branch(P(g), "benefits")); g = Cold(g, registry); Pick(g, "benefits", 1);
            Reach(g, () => Branch(P(g), "restore"));
            Need(P(g) is { PlayerSeat: 1, IsPrivate: true } p && p.Choices.Count == 5 && g.CreateSnapshot(0).PendingDecision is null,
                "A whole-area abolition presents exactly five native categories to the private recipient, allowing only one restoration.");
            g = Cold(g, registry); Pick(g, "restore", (int)EquipmentSlot.Armor); Play(g);
            var restored = V(g, 1);
            Need(!restored.IsEquipmentAreaAbolished && Enum.GetValues<EquipmentSlot>().All(s => restored.EquipmentSlotCapacities?.GetValueOrDefault(s, 1) == (s == EquipmentSlot.Armor ? 1 : 0)) &&
                E<SunRuOlWeimianBenefitIssuedEvent>(g).Single().RestoredSlot == EquipmentSlot.Armor,
                "Native single-slot restoration clears whole-area abolition but keeps the four unchosen categories unavailable.");
            _ = Cold(g, registry);
        }
        {
            var (g, registry) = Start("empty");
            for (var i = 0; i < 2; i++)
            { Use(g, "foreign-discard", [1]); Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("source-zone") == "Hand") == true); Answer(g, _ => true); Play(g); }
            var hp = V(g, 1).Hp;
            Need(hp == V(g, 1).MaxHp && V(g, 1).HandCount == 0 && !V(g, 1).IsEquipmentAreaAbolished,
                "The real recipient is full HP, has no abolished category, and already has no hand cards.");
            Weimian(g, 1); Reach(g, () => Branch(P(g), "slots")); Pick(g, "slots", 7);
            Reach(g, () => Branch(P(g), "benefits")); Pick(g, "benefits", 7); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Gain);
            Need(V(g, 1).HandCount == 4 && V(g, 1).Hp == hp && !E<RecoveryAppliedEvent>(g).Any() &&
                !g.CardMovements.Any(m => m.Reason.Value == "skill-program.sun-ru.ol.weimian.discard") && E<SunRuOlWeimianBenefitIssuedEvent>(g).Count() == 3,
                "All three projects remain selectable: unavailable restoration/healing naturally have no effect and discarding zero cards still draws four.");
            g = Cold(g, registry); Continue(g); Play(g); _ = Cold(g, registry);
        }
    }

    private static PlayerSnapshot V(GameEngine g, int s) => g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static bool Optional(PendingDecision? p, string id) => p?.SkillPrompt?.SkillId == id && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool Branch(PendingDecision? p, string branch) => p?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == branch) == true;
    private static void Pick(GameEngine g, string branch, int value) => Answer(g, c => c.Parameters.GetValueOrDefault("branch") == branch && c.Parameters.GetValueOrDefault("value") == value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> choose) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(choose).Id, g.Revision)); }
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string id, IReadOnlyList<int> targets, IReadOnlyList<int>? cards = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets, g.Revision, P(g)!.PromptId));
    private static void Weimian(GameEngine g, int target) => Accept(g, new UseProgramSkillCommand(0, Wei, "abolish-for-distinct-benefits", [], [target], g.Revision, P(g)!.PromptId));
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var i = 0; i < 400; i++)
        {
            if (done()) return;
            if (P(g) is { } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
            else if (P(g) is { PlayerSeat: 0 } human)
            {
                if (human.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else throw new InvalidOperationException("Unexpected Sun Ru OL boundary: " + JsonSerializer.Serialize(human));
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Sun Ru OL bounded native boundary was not reached: " + JsonSerializer.Serialize(P(g)));
    }
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected actual Sun Ru OL command."); }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(cold), "All private player views, paid slot receipts, native children, events, physical entities and accepted commands restore identically."); return cold; }
    private static string Embedded(string s) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.ol-sun-ru.{s}")!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
    private static (GameEngine, ContentRegistry) Start(string scenario)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = scenario == "replacement" ? Role.Renegade : Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 18 }, r);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, r);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-sunru-ol", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            void Add(SkillProgramCatalog c) { foreach (var (id, p) in c.Programs) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = p, ProgramPresentation = c.Presentations[id] }); }
            Add(SkillProgramCatalog.Load(Embedded("rules.json"), Embedded("presentation.json")));
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"empty-hand","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]},
                {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":1}]},
                {"id":"foreign-discard","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]},
                {"id":"equip","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-slot-child","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["equipment.slot-abolished"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
              {"id":"{{Gain}}","revision":1,"triggers":[{"id":"native-gain-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.sun-ru.ol.chishi.draw","skill-program.sun-ru.ol.weimian.draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]}]}]},
              {"id":"{{Hp}}","revision":1,"triggers":[{"id":"native-heal-child","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:sr-whole","revision":1,"triggers":[{"id":"whole-equipment-area","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"abolishOwnerAreas","target":"owner","zones":["equipment"]}]}]},
              {"id":"fixture:sr-recovery","revision":1,"cardPolicies":[{"id":"real-lord-recovery","kind":"redirectOwnTurnFactionRecovery","factionId":"wei","ownerRole":"lord","value":1,"providerDrawCount":1}]}]}
            """;
            var ids = new[] { Driver, Quiet, Watch, Gain, Hp, "fixture:sr-whole", "fixture:sr-recovery" };
            var labels = ids.ToDictionary(id => id, id => new { name = id, description = "孙茹真实命令与原生区域/子窗口", optionLabels = id is Watch or Gain or Hp ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() });
            Add(SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels })));
            var humanRole = scenario == "replacement" ? Role.Renegade : Role.Lord;
            b.AddSkill(new("fixture:sr-owner-pick", "固定孙茹", "公开评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == humanRole ? 100000d : -100000d) });
            b.AddSkill(new("fixture:sr-peer-pick", "固定其他", "公开评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == humanRole ? -100000d : 100000d) });
            var ownerSkills = new List<string> { Wei, Driver, Watch, Gain, Hp };
            if (scenario != "replacement") ownerSkills.Add("fixture:sr-recovery");
            if (scenario != "cross") ownerSkills.Add(Chi);
            b.AddGeneral(new(Owner, "孙茹真实规则", "ol-sun-ru", "fixture:sr-owner-pick", "wei", 6, ownerSkills) { InitialHp = 2 });
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:sr-peer-{i}").ToArray();
            foreach (var id in peers)
            {
                var peerSkills = new List<string> { Quiet, Gain, Hp };
                if (scenario == "whole") peerSkills.Add("fixture:sr-whole");
                if (scenario == "cross" && id == peers[0]) peerSkills.Add(Chi);
                if (scenario == "replacement") peerSkills.Add("fixture:sr-recovery");
                b.AddGeneral(new(id, "固定其他", "supporter", "fixture:sr-peer-pick", "wei", 6, peerSkills) { InitialHp = scenario == "replacement" ? 1 : scenario == "silver" ? 2 : 6 });
            }
            b.AddCard(new("fixture:sr-lion", "白银狮子", "装备牌", "实际移除回复", CardKind.SilverLion));
            b.AddDeck(new("fixture:sr-deck", "固定实际实体", 2, 0, []) { PhysicalCards = Enumerable.Range(0, 120).Select(i => new ContentDeckPhysicalCard(scenario == "silver" ? "fixture:sr-lion" : "standard:dodge", Suit.Club, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "孙茹OL真实命令", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:sr-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
