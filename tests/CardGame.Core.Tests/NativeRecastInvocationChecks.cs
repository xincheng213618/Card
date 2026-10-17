using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class NativeRecastInvocationChecks
{
    private const string Owner = "fixture:nri-owner", Mode = "identity:classic-native-recast";
    private const string Driver = "fixture:nri-driver", Pair = "fixture:nri-pair", Match = "fixture:nri-match";
    private const string Slash = "fixture:nri-slash", Watch = "fixture:nri-watch", Quiet = "fixture:nri-quiet";
    private const string Conditional = "fixture:nri-conditional", Convert = "fixture:nri-convert";
    private static string Scene = "";

    public static void NativeSingleAndWholeGroupAreDistinct()
    {
        foreach (var enabled in new[] { false, true })
        {
            var (g, registry) = Start("whole", enabled);
            var action = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Recast);
            Accept(g, new RecastCardCommand(0, action.CardId!.Value, g.Revision, P(g)!.PromptId) { ConversionSource = action.ConversionSource });
            Play(g);
            Need(E<NativeRecastInvocationRecordedEvent>(g).Length == (enabled ? 1 : 0), "Only the new capability opts native recasts into semantic history.");
            if (enabled)
            {
                var single = E<NativeRecastInvocationRecordedEvent>(g).Single();
                Need(single is { InitiatorSeat: 0, ExecutorSeat: 0, PrimaryCount: 1, ActualRecastCount: 1, ActualDrawCount: 1, ParentFrameId: null }, "One physical Iron Chain is one complete one-card native recast.");
            }
            var cards = V(g, 0).Hand.Take(2).Select(c => c.Id).ToArray();
            Need(V(g, 0).HandCount == 2 && cards.Length == 2, "The negative representative genuinely recasts its complete two-card hand as one original group.");
            Use(g, Match, "match", cards, [1]);
            Play(g);
            var begun = E<MatchingRecastStartedEvent>(g).Single();
            var ownerFacts = E<NativeRecastInvocationRecordedEvent>(g).Where(e => e.ParentFrameId == begun.FrameId && e.ExecutorSeat == 0).ToArray();
            Need(ownerFacts.Length == (enabled ? 1 : 0) && E<CardRecastEvent>(g).Count(e => cards.Contains(e.CardId)) == 2,
                "One matching group keeps its two legacy per-card facts without turning them into two semantic recasts.");
            if (enabled) Need(ownerFacts is [{ PrimaryCount: 2, ActualRecastCount: 2, ActualDrawCount: 2, ParticipantCursor: 0 }],
                "The whole two-card recast cannot satisfy an exactly-one-card historical category.");
            Need(g.ResolutionStack.OfType<ProgramSkillFrame>().All(f => f.NativeRecastInvocation is null), "Completed programs retain no pending semantic recast draft.");
            _ = Cold(g, registry);
        }
    }

    public static void TwoParticipantsOwnIndependentFrozenInvocationsCold()
    {
        var (g, registry) = Start("pair");
        var original = V(g, 0).Hand[0].Id;
        Use(g, Pair, "pair", [original], [1]);
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.PairedHandRecast is not null);
        var started = E<PairedHandRecastStartedEvent>(g).Single();
        var draft = root.NativeRecastInvocation!;
        Need(draft is { ExecutorSeat: 0, ParticipantCursor: 0 } && draft.Materials is [{ CardId: var paid }] && paid == original &&
            draft.Materials is System.Collections.IList { IsReadOnly: true } && E<NativeRecastInvocationRecordedEvent>(g).Length == 0 &&
            root.PairedHandRecast!.Owner.CostIssued && !root.PairedHandRecast.Owner.DrawIssued && !root.PairedHandRecast.Partner.CostIssued,
            "Before its native cost child returns, the first participant owns one frozen primary group and has not issued either reward or the peer's recast.");
        Need(Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null && g.CreateSnapshot(s).Players[0].Hand.Count == 0),
            "The actual participant's private observer prompt and remaining hand identities are absent from other views.");
        g = Cold(g, registry); Continue(g); Play(g);
        var facts = E<NativeRecastInvocationRecordedEvent>(g).Where(e => e.ParentFrameId == started.FrameId).ToArray();
        Need(facts.Length == 2 && facts.Select(e => e.InvocationId).Distinct().Count() == 2 &&
            facts.Select(e => e.ExecutorSeat).SequenceEqual([0, 1]) && facts.Select(e => e.ParticipantCursor).SequenceEqual(new int?[] { 0, 1 }) &&
            facts.All(e => e is { InitiatorSeat: 0, PrimaryCount: 1, ActualRecastCount: 1, ActualDrawCount: 1 }) &&
            facts[1].SequenceBefore >= facts[0].SequenceAfter && E<PairedHandRecastCompletedEvent>(g).Single().PartnerPaid,
            "A real paired program emits one independent whole invocation for each actual payer, in native participant order, after cold recovery exactly once.");
        _ = Cold(g, registry);
    }

    public static void PaidLossAndDeathKeepZeroDrawCompletionCold()
    {
        foreach (var scene in new[] { "loss", "death" })
        {
            var (g, registry) = Start(scene);
            Need(V(g, 0).Skills!.Any(s => s.Id == Slash), "The paid recast's genuine source is present before activation.");
            var card = V(g, 0).Hand.First(c => c.Kind == CardKind.Slash).Id;
            Use(g, Slash, "slash", [card]);
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
            var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.PlaySlashRecast is not null);
            var frameId = root.Id;
            Need(root.NativeRecastInvocation is { Materials.Count: 1 } && E<NativeRecastInvocationRecordedEvent>(g).Length == 0,
                "The paid source owns its semantic invoice through the actual last-cost child, without prematurely completing it.");
            g = Cold(g, registry); Continue(g);
            Reach(g, () => E<NativeRecastInvocationRecordedEvent>(g).Any(e => e.ParentFrameId == frameId) && !g.ResolutionStack.Any(f => f.Id == frameId));
            var fact = E<NativeRecastInvocationRecordedEvent>(g).Single(e => e.ParentFrameId == frameId);
            Need(fact is { ExecutorSeat: 0, PrimaryCount: 1, ActualRecastCount: 1, ActualDrawCount: 0 } &&
                E<CardRecastEvent>(g).Single(e => e.CardId == card).DrawCount == 0 &&
                g.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Hand(0) && m.Reason == CardMoveReasons.RecastDiscard) == 1 &&
                (scene == "death" ? !V(g, 0).IsAlive : V(g, 0).IsAlive && !V(g, 0).Skills!.Any(s => s.Id == Slash)),
                "A genuine already-paid recast remains a one-card recast when its source is lost or its participant dies before the reward; zero draw neither cancels nor repays the cost.");
            _ = Cold(g, registry);
        }
    }

    public static void EquipmentRecoveryAndOxSpillAreNotExtraRecastsCold()
    {
        foreach (var scene in new[] { "lion-native", "lion-program", "ox" })
        {
            var (g, registry) = Start(scene);
            var card = V(g, 0).Hand[0].Id;
            Accept(g, new PlayCardCommand(0, card, [], g.Revision, P(g)!.PromptId)); Play(g);
            Need(V(g, 0).Equipment.Any(c => c.Id == card), "The recast material is a genuinely equipped native entity.");
            int[] grain = [];
            if (scene == "ox")
            {
                grain = V(g, 0).Hand.Take(2).Select(c => c.Id).ToArray();
                foreach (var id in grain) { Use(g, Driver, "grain", [id]); Play(g); }
            }
            else { Use(g, Driver, "wound"); Play(g); }
            var handBeforeRecast = V(g, 0).HandCount;
            if (scene is "lion-native" or "ox")
            {
                var recast = g.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.Recast && a.CardId == card);
                Accept(g, new RecastCardCommand(0, card, g.Revision, P(g)!.PromptId) { ConversionSource = recast.ConversionSource });
            }
            else Use(g, Driver, "equipment", [card]);
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
            var drafts = g.ResolutionStack.OfType<EquipmentRecastFrame>().Select(f => f.NativeRecastInvocation)
                .Concat(g.ResolutionStack.OfType<ProgramSkillFrame>().Select(f => f.NativeRecastInvocation))
                .OfType<NativeRecastInvocationDraft>().ToArray();
            if (scene == "lion-program")
            {
                var parent = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Driver && f.ActivationId == "equipment");
                var invoices = E<NativeRecastInvocationRecordedEvent>(g);
                var paid = g.CardMovements.Single(m => m.CardId == card && m.From == CardLocation.Equipment(0) && m.Reason == CardMoveReasons.RecastDiscard);
                Need(drafts.Length == 0 && invoices is [{ InitiatorSeat: 0, ExecutorSeat: 0, PrimaryCount: 1, ActualRecastCount: 1, ActualDrawCount: 1 }] &&
                    invoices[0].ParentFrameId == parent.Id && invoices[0].InstructionIndex == parent.InstructionIndex &&
                    invoices[0].SequenceBefore < paid.Sequence && paid.Sequence <= invoices[0].SequenceAfter &&
                    E<CardRecastEvent>(g) is [{ DrawCount: 1 }] && E<CardRecastEvent>(g)[0].CardId == card &&
                    V(g, 0).HandCount == handBeforeRecast + 1 && V(g, 0).Hp == V(g, 0).MaxHp,
                    "The existing synchronous equipment program completes its exact one-primary payment and Draw1 before the genuine Lion recovery observer, retaining one completed whole invoice and its original instruction.");
            }
            else
            {
                Need(drafts.Length == 1, "The native cost child must retain exactly one frame-owned original recast invoice: " + Diagnostic(g) +
                    " Invoices=" + JsonSerializer.Serialize(E<NativeRecastInvocationRecordedEvent>(g)));
                var draft = drafts.Single();
                Need(draft is { Materials.Count: 1 } && draft.Materials[0].CardId == card && draft.Materials[0].From == CardLocation.Equipment(0) &&
                    E<NativeRecastInvocationRecordedEvent>(g).Length == 0 && V(g, 0).HandCount == handBeforeRecast &&
                    (scene == "ox" ? grain.All(id => g.CardMovements.Any(m => m.CardId == id && m.From.Zone == CardZoneKind.WoodenOxGrain && m.To == CardLocation.DiscardPile)) : V(g, 0).Hp == V(g, 0).MaxHp),
                    "Native Lion recovery or Ox spill pauses under the original frozen equipment invoice before its reward and whole completion.");
            }
            g = Cold(g, registry); Continue(g); Play(g);
            Need(E<NativeRecastInvocationRecordedEvent>(g) is [{ PrimaryCount: 1, ActualRecastCount: 1, ActualDrawCount: 1 }] &&
                E<CardRecastEvent>(g).Length == 1 && E<CardRecastEvent>(g).Single().CardId == card &&
                E<NativeLogicalDiscardInvocationRecordedEvent>(g).All(e => e.Reason != CardMoveReasons.RecastDiscard) &&
                g.CardMovements.Count(m => m.CardId == card && m.Reason == CardMoveReasons.RecastDiscard) == 1 && V(g, 0).HandCount == handBeforeRecast + 1,
                "Cold child return preserves exactly one reward and whole completion in the producer's native order; Ox grain never becomes an extra primary or logical discard.");
            _ = Cold(g, registry);
        }
    }

    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(Scene + ": " + message); }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Native recast command rejected."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> select) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(select).Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Use(GameEngine g, string skill, string id, int[]? cards = null, int[]? targets = null) => Accept(g, new UseProgramSkillCommand(0, skill, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<bool> done, [CallerLineNumber] int line = 0)
    {
        for (var step = 0; step < 180; step++)
        {
            if (done()) return;
            var p = P(g); Need(g.State.Winner == Winner.None, "Unexpected terminal boundary at " + line + ": " + Diagnostic(g));
            if (p is null || p.PlayerSeat != 0) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (p.Kind == DecisionKind.RescueDying) Answer(g, c => c.Parameters.GetValueOrDefault("response") is "let-die" or "pass");
            else if (p.SkillPrompt?.SkillId == Watch) Continue(g);
            else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else throw new InvalidOperationException(Scene + " unexpected boundary at " + line + ": " + Diagnostic(g));
        }
        throw new InvalidOperationException(Scene + " missing boundary at " + line + ": " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.CurrentSeat, g.State.Phase,
        Prompt = P(g) is { } p ? new { p.PlayerSeat, p.Kind, Skill = p.SkillPrompt?.SkillId, Choices = p.Choices.Take(3).Select(c => c.Parameters) } : null,
        Frames = g.ResolutionStack.TakeLast(4).Select(f => new { f.Id, Type = f.GetType().Name }), Facts = g.Events.TakeLast(3).Select(e => e.Payload.ToString()) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands),
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    { var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(copy), "Cold replay preserves private views, frozen whole primary group, original movements and once-only semantic completion."); return copy; }
    private static (GameEngine, ContentRegistry) Start(string scene, bool capability = true)
    {
        Scene = scene; var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene, capability));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0,
            HumanRole = scene == "death" ? Role.Renegade : Role.Lord, ModeId = Mode, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, registry);
    }
    private sealed class Fixture(string scene, bool capability) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-native-recast-invocations", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            if (capability) CharacterFixtureContent.Register(b, "ol:li-yi");
            var json = """
            {"schemaVersion":@schema@,"skills":[
             {"id":"fixture:nri-driver","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":30,"priority":0}],"activations":[
              {"id":"wound","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
              {"id":"equipment","minCards":1,"maxCards":1,"sourceZones":["hand","equipment"],"cardCategories":["equipment"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recastSelectedEquipment","target":"owner"}]},
              {"id":"grain","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"grain"},{"op":"moveBoundCards","target":"owner","sourceBind":"grain","destination":"ownerPersistentZone","destinationZone":"woodenOxGrain"}]}]},
             {"id":"fixture:nri-pair","revision":1,"activations":[{"id":"pair","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLivingWithHand","usesPerPhase":1,"usesPerTurn":null,"usesPerGame":null,"effects":[{"op":"pairedHandRecast","target":"selectedTarget"}]}]},
             {"id":"fixture:nri-match","revision":1,"activations":[{"id":"match","minCards":2,"maxCards":null,"sourceZones":["hand","equipment"],"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"targetPhaseLedgerId":"match","effects":[{"op":"recastMatchingCardsThenPeerChoice","target":"selectedTarget","stateId":"match"}]}]},
             {"id":"fixture:nri-slash","revision":1,"activations":[{"id":"slash","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardKinds":["slash","fireSlash","thunderSlash"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recastSelectedPhysicalSlash","target":"owner"}]}]},
             {"id":"fixture:nri-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"fixture:nri-convert","revision":1,"viewAs":[{"id":"chain","inputKinds":[],"inputSuits":[],"sourceZones":["hand","equipment"],"outputKind":"ironChain","forPlay":true,"forResponse":false}]}
            ]}
            """.Replace("@schema@", SkillProgramCatalog.RulesSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var rules = JsonNode.Parse(json)!; var defs = rules["skills"]!.AsArray();
            if (scene == "loss") defs.Add(JsonNode.Parse("""{"id":"fixture:nri-conditional","revision":1,"hpSkillGrants":[{"id":"healthy","skillId":"fixture:nri-slash","minimumHp":9}]}"""));
            if (scene != "whole")
            {
                var trigger = JsonNode.Parse(scene.StartsWith("lion", StringComparison.Ordinal)
                    ? """{"id":"cost","window":"afterHpRecovered","subject":"owner","hpChangeOccurrence":"perEvent","optional":false,"effects":[]}"""
                    : scene == "ox"
                    ? """{"id":"cost","window":"cardsMoved","subject":"owner","sourceZones":["woodenOxGrain"],"movementOccurrence":"perBatch","movementReasons":["equipment.wooden-ox.grain-discard"],"optional":false,"effects":[]}"""
                    : """{"id":"cost","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["card.recast.discard"],"optional":false,"effects":[]}""")!;
                trigger["effects"]!.AsArray().Add(JsonNode.Parse("""{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}"""));
                if (scene is "loss" or "death") trigger["effects"]!.AsArray().Add(JsonNode.Parse("{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + (scene == "death" ? 20 : 1) + "}"));
                defs.Add(new JsonObject { ["id"] = Watch, ["revision"] = 1, ["triggers"] = new JsonArray(trigger) });
            }
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "真实整次重铸机制夹具", optionLabels = n!["id"]!.GetValue<string>() == Watch ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "原生重铸凭据行为") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:nri-pick", "固定本人", "公开选将排序") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000d) });
            var skills = new List<string> { Driver, Pair, Match, Convert, scene == "loss" ? Conditional : Slash }; if (scene != "whole") skills.Add(Watch);
            b.AddGeneral(new(Owner, "原生重铸本人", "supporter", "fixture:nri-pick", "wei", 8, skills));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:nri-peer-" + i).ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "真实其他角色", "supporter", Quiet, "qun", 12, []));
            var kind = scene is "loss" or "death" ? CardKind.Slash : scene.StartsWith("lion", StringComparison.Ordinal) ? CardKind.SilverLion : scene == "ox" ? CardKind.WoodenOx : CardKind.IronChain;
            b.AddCard(new("fixture:nri-card", "原生实体", CardCatalog.Get(kind).CategoryName, "固定真实实体", kind));
            b.AddDeck(new("fixture:nri-deck", "固定原生重铸材料", scene == "whole" ? 2 : 4, scene == "whole" ? 0 : 2, []) { PhysicalCards = Enumerable.Range(0, 64).Select(i => new ContentDeckPhysicalCard("fixture:nri-card", Suit.Club, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "原生整次重铸行为", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:nri-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
