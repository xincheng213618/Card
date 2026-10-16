using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class HuoJunChecks
{
    private const string Qiong = "ol:qiongshou", Fen = "ol:fenrui", Driver = "fixture:hj-driver", Quiet = "fixture:hj-quiet";
    private const string Watch = "fixture:hj-paid", UseWatch = "fixture:hj-use", DamageWatch = "fixture:hj-damage", Owner = "fixture:hj-owner", Mode = "identity:classic-huo-jun-fixture";
    public static void ActualInitialSlotsHandLimitPaidSourceLossAndNativeEquipmentCold()
    {
        foreach (var loss in new[] { false, true })
        {
            var (g, registry) = Start(loss ? "loss" : "normal");
            Need(V(g, 0).HandCount == 6 && V(g, 0).IsEquipmentAreaAbolished && V(g, 0).Equipment.Count == 0 &&
                E<PlayerAreasAbolishedEvent>(g).Count(e => e.Seat == 0 && e.Equipment) == 1,
                "Actual mandatory game start abolishes all five native slots and adds exactly four actual cards to the two-card initial hand.");
            Use(g, "draw-six"); Play(g); Need(V(g, 0).HandCount == 12, "The fixture produces twelve actual hand entities before the real discard phase.");
            End(g); Reach(g, () => Optional(P(g), Fen));
            Need(V(g, 0).HandCount == V(g, 0).Hp + 4 && V(g, 0).HandCount == 9 &&
                g.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Any(),
                "The real discard phase retains HP plus four cards, and Fenrui opens only in the later actual ending phase.");
            Activate(g); Reach(g, () => Branch(P(g), "cost"));
            Need(P(g) is { IsPrivate: true, PlayerSeat: 0 } && g.CreateSnapshot(1).PendingDecision is null,
                "Only the owner sees and pays the actual hand/equipment discard selection.");
            var cost = P(g)!.Choices.First().Cards.Single(); g = Cold(g, registry); Answer(g, c => c.Cards.SequenceEqual([cost]));
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
            Need(E<HuoJunFenruiCostPaidEvent>(g).Single() is { CardId: var paid } && paid == cost &&
                g.CardMovements.Count(m => m.CardId == cost && m.Reason.Value == "skill-program.huo-jun.fenrui.discard") == 1 &&
                !E<HuoJunFenruiSlotRestoredEvent>(g).Any() && !E<HuoJunFenruiEquipmentIssuedEvent>(g).Any() &&
                V(g, 0).Skills!.Any(s => s.Id == Fen) != loss,
                "One actual entity is discarded before restoration or use; the paid frame survives a real source-skill loss in its native movement children.");
            g = Cold(g, registry); Continue(g); Reach(g, () => Branch(P(g), "restore"));
            Need(P(g)!.Choices.Count == 5, "Whole-area abolition offers exactly five actual native restoration categories.");
            g = Cold(g, registry); Slot(g, EquipmentSlot.Weapon); Reach(g, () => P(g)?.SkillPrompt?.SkillId == UseWatch);
            var issue = E<HuoJunFenruiEquipmentIssuedEvent>(g).Single();
            var native = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == issue.NativeUseId);
            Need(issue.Slot == EquipmentSlot.Weapon && issue.CardKind == CardKind.Crossbow && native.Action is { Type: CardActionType.Use } action &&
                action.ActionId == issue.NativeActionId && action.ActorSeat == 0 && action.PhysicalCards is [var material] &&
                material.CardId == issue.CardId && material.From == CardLocation.DrawPile && action.ConversionChain.Contains(issue.Source) &&
                V(g, 0).Equipment.Count == 0 && !E<HuoJunFenruiEquipmentReturnedEvent>(g).Any(),
                "The random matching physical weapon enters a genuine native Use and finalized-target child before it is equipped; it is never directly inserted into Equipment.");
            g = Cold(g, registry); Continue(g); Reach(g, () => Branch(P(g), "damage"));
            Need(V(g, 0).Equipment is [var equipped] && equipped.Id == issue.CardId && !V(g, 0).IsEquipmentAreaAbolished &&
                E<HuoJunFenruiEquipmentReturnedEvent>(g).Single().NativeUseId == issue.NativeUseId &&
                E<CardUseFinishedEvent>(g).Single(e => e.ResolutionId == issue.NativeUseId).CardId == issue.CardId &&
                Enum.GetValues<EquipmentSlot>().All(s => V(g, 0).EquipmentSlotCapacities!.GetValueOrDefault(s, 1) == (s == EquipmentSlot.Weapon ? 1 : 0)),
                "The exact native equipment use finishes before the optional damage choice, and only the restored category becomes available.");
            var hp = V(g, 1).Hp; g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "damage" && c.Targets.SequenceEqual([1]));
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == DamageWatch);
            Need(E<HuoJunFenruiDamageCommittedEvent>(g).Single() is { DamageAmount: 1, OwnEquipmentCount: 1, TargetEquipmentCount: 0, TargetSeat: 1 } && V(g, 1).Hp == hp,
                "Accepting the current one-equipment difference commits the permanent damage branch before the actual native before-damage child.");
            g = Cold(g, registry); Continue(g); Play(g);
            Need(V(g, 1).Hp == hp - 1 && E<HuoJunFenruiCostPaidEvent>(g).Count() == 1 && E<HuoJunFenruiEquipmentIssuedEvent>(g).Count() == 1 &&
                E<HuoJunFenruiEquipmentReturnedEvent>(g).Count() == 1 && E<HuoJunFenruiDamageCommittedEvent>(g).Count() == 1,
                "Cold paid movement, restored-slot choice, actual native use and damage children neither repay the cost nor repeat the equipment or one-time damage, even after source loss.");
            _ = Cold(g, registry);
        }
    }
    private static PlayerSnapshot V(GameEngine g, int s) => g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static bool Optional(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool Branch(PendingDecision? p, string branch) => p?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == branch) == true;
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Slot(GameEngine g, EquipmentSlot slot) => Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "restore" && c.Parameters.GetValueOrDefault("value") == ((int)slot).ToString(System.Globalization.CultureInfo.InvariantCulture));
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string id) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], [], g.Revision, P(g)!.PromptId));
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 400; step++)
        {
            if (done()) return;
            if (P(g) is { } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
            else if (P(g) is { PlayerSeat: 0 } human)
            {
                if (human.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else throw new InvalidOperationException("Unexpected actual Huo Jun boundary: " + JsonSerializer.Serialize(human));
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The actual Huo Jun boundary was not reached: " + JsonSerializer.Serialize(P(g)));
    }
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected actual Huo Jun command."); }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(cold), "Every private view, paid entity, restored slot, random native use, one-time branch and actual child restores identically."); return cold; }
    private static string Embedded(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.ol-huo-jun.{suffix}")!; using var r = new StreamReader(stream); return r.ReadToEnd(); }
    private static (GameEngine, ContentRegistry) Start(string scenario)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = scenario == "recovery" ? Role.Renegade : Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 18 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, registry);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-huo-jun", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            void Add(SkillProgramCatalog c) { foreach (var (id, p) in c.Programs) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = p, ProgramPresentation = c.Presentations[id] }); }
            Add(SkillProgramCatalog.Load(Embedded("rules.json"), Embedded("presentation.json")));
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[{"id":"draw-six","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":6}]},{"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-discard-child","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["skill-program.huo-jun.fenrui.discard"],"optional":false,"effects":[{{(scenario == "loss" ? "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}," : "")}}{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
              {"id":"{{UseWatch}}","revision":1,"triggers":[{"id":"actual-native-equipment-use","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardCategories":["equipment"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"native","options":[{"id":"continue"}]}]}]},
              {"id":"{{DamageWatch}}","revision":1,"triggers":[{"id":"actual-damage-child","window":"beforeDamageApplied","subject":"damageTarget","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"damage","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:hj-loss","revision":1,"hpSkillGrants":[{"id":"healthy-fenrui","skillId":"{{Fen}}","minimumHp":5}]},
              {"id":"fixture:hj-prevent","revision":1,"triggers":[{"id":"prevent-attempt","window":"beforeDamageApplied","subject":"damageTarget","priority":20,"optional":false,"effects":[{"op":"preventCurrentDamageAndDrawMultiple","target":"owner","amount":1}]}]},
              {"id":"fixture:hj-hp","revision":1,"triggers":[{"id":"actual-cost-removal-heal","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:hj-redirect","revision":1,"cardPolicies":[{"id":"actual-replacement","kind":"redirectOwnTurnFactionRecovery","factionId":"shu","ownerRole":"lord","value":1,"providerDrawCount":1}]}]}
            """;
            var ids = new[] { Driver, Quiet, Watch, UseWatch, DamageWatch, "fixture:hj-loss", "fixture:hj-prevent", "fixture:hj-hp", "fixture:hj-redirect" };
            Add(SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = ids.ToDictionary(id => id, id => new { name = id, description = "实际霍峻原生窗口", optionLabels = id is Watch or UseWatch or DamageWatch or "fixture:hj-hp" ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() }) })));
            var humanRole = scenario == "recovery" ? Role.Renegade : Role.Lord;
            b.AddSkill(new("fixture:hj-owner-pick", "固定霍峻", "公开评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == humanRole ? 100000d : -100000d) });
            b.AddSkill(new("fixture:hj-peer-pick", "固定其他", "公开评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == humanRole ? -100000d : 100000d) });
            var own = new List<string> { Qiong, scenario == "loss" ? "fixture:hj-loss" : Fen, Driver, Watch, UseWatch, "fixture:hj-hp" };
            b.AddGeneral(new(Owner, "实际霍峻", "ol-huo-jun", "fixture:hj-owner-pick", "shu", 4, own));
            var peerSkills = new List<string> { Quiet, DamageWatch }; if (scenario == "prevent") peerSkills.Add("fixture:hj-prevent"); if (scenario == "recovery") peerSkills.Add("fixture:hj-redirect");
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:hj-peer-{i}").ToArray();
            foreach (var id in peers) b.AddGeneral(new(id, "固定其他", "supporter", "fixture:hj-peer-pick", "shu", 6, peerSkills) { InitialHp = scenario == "recovery" ? 1 : 6 });
            b.AddCard(new("fixture:hj-weapon", "诸葛连弩", "装备牌", "真实随机武器", CardKind.Crossbow));
            b.AddCard(new("fixture:hj-armor", "白银狮子", "装备牌", "真实随机防具", CardKind.SilverLion));
            b.AddDeck(new("fixture:hj-deck", "固定两个副类别", 2, 0, []) { PhysicalCards = Enumerable.Range(0, 160).Select(i => new ContentDeckPhysicalCard(i % 2 == 0 ? "fixture:hj-weapon" : "fixture:hj-armor", Suit.Club, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "霍峻真实命令", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:hj-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
