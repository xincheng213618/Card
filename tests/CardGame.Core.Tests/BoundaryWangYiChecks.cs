using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryWangYiChecks
{
    private const string Zhen = "boundary:zhenlie-current", Miji = "boundary:miji-current";
    private const string Driver = "fixture:wy-driver", Entry = "fixture:wy-entry", Hp = "fixture:wy-hp", Loss = "fixture:wy-loss";
    private const string SourceSuppression = "fixture:wy-source-suppression";
    private const string Mode = "identity:classic-wy-fixture";

    public static void PaidOtherUseNullificationOpaqueObtainAndActualSlash()
    {
        var (g, r) = Create(); Play(g); var before = g.State.Players[0].Hp;
        Use(g, "enemy-duel", [1]); Reach(g, p => Activation(p, Zhen)); Reject(g); g = Restore(g, r);
        Activate(g, Zhen); Reach(g, p => p.SkillPrompt?.SkillId == Hp);
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Zhen);
        var paid = root.PaidOwnTarget!;
        Require(paid.Use is { ActorSeat: 1, ProviderSeat: 1, TargetSeat: 0, EffectiveKind: CardKind.Duel, ActionId: not null } &&
            paid.ActualLost == 1 && !paid.Applied && g.State.Players[0].Hp == before - 1 &&
            E<PaidOwnTargetAppliedEvent>(g).Count() == 0,
            "The actual selected actor's zero-entity ordinary Duel freezes actor/provider/target before paying one real HP; nullification waits for the HP child.");
        g = Restore(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Zhen && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "obtain"));
        Require(E<PaidOwnTargetAppliedEvent>(g).Single().CardUseFrameId == paid.Use.CardUseFrameId &&
            E<ProgramSkillHpLostEvent>(g).Count(e => e.FrameId == root.Id) == 1,
            "The restored HP child applies to the same use once and never repays the cost.");
        Option(g, "obtain"); Reach(g, p => p.SkillPrompt?.SkillId == Zhen && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card"));
        var prompt = P(g)!;
        Require(prompt.IsPrivate && prompt.Choices.Where(c => c.Parameters.GetValueOrDefault("source-zone") == "Hand").All(c => c.Cards.Count == 0) &&
            Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null),
            "Choosing the original actor's hidden HE hand uses opaque current slots and exposes no identity in other views.");
        Reject(g); g = Restore(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("source-zone") == "Hand"); Play(g);
        var obtainReason = $"skill-program.{Zhen}.{SkillProgramEffectOp.SelectAndMoveOwnedCard}";
        var claim = g.CardMovements.Last(m => m.From == CardLocation.Hand(1) && m.To == CardLocation.Hand(0) && m.Reason.Value == obtainReason);
        Require(g.CardMovements.Count(m => m.CardId == claim.CardId && m.From == CardLocation.Hand(1) && m.To == CardLocation.Hand(0)) == 1 &&
            E<PaidOwnTargetAppliedEvent>(g).Count() == 1 && g.State.Players[0].Hp == before - 1 &&
            E<EarnedActualEndingBenefitIssuedEvent>(g).Count() == 0,
            "The original actor's entity is obtained once; the actual Duel completes without damage and without issuing the unchosen Ending promise.");
        g = Restore(g, r);

        var (slash, sr) = Create("standard:slash", observers: false); Play(slash);
        Accept(slash, new EndPlayPhaseCommand(0, slash.Revision, P(slash)!.PromptId));
        Reach(slash, p => Activation(p, Zhen));
        var window = slash.ResolutionStack.OfType<ActualUseTargetWindowFrame>().Single();
        var actual = slash.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == window.ParentFrameId);
        Require(actual.Action is { Type: CardActionType.Use, PhysicalCards.Count: 1 } && actual.SourceSeat != 0 &&
            actual.CardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash,
            "A native opponent's true paid Slash reaches the exact target window before Dodge or damage.");
        var hp = slash.State.Players[0].Hp; slash = Restore(slash, sr); Activate(slash, Zhen); Reach(slash, p => p.SkillPrompt?.SkillId == Zhen);
        Option(slash, "skip"); Until(slash, () => E<ProgramBindingResolvedEvent>(slash).Any(e => e.SkillId == Zhen && e.Completed));
        Require(slash.State.Players[0].Hp == hp - 1 && actual.Action.PhysicalCards.All(c =>
            slash.CardMovements.Count(m => m.CardId == c.CardId && m.To == CardLocation.Processing) == 1) &&
            E<PaidOwnTargetAppliedEvent>(slash).Single().CardUseFrameId == actual.Id,
            "The same native physical Slash is paid once and suppressed only for the actual protected target.");
        slash = Restore(slash, sr);

        var (legacy, legacyRegistry) = CreateLegacySource();
        ReachLegacyHuman(legacy, p => Activation(p, "boundary:shensu"));
        Require(legacy.CreateSnapshot(0).Players[0] is { Role: Role.Rebel, GeneralId: "fixture:wy-owner" } &&
            legacy.CreateSnapshot(1).Players[0].Role is null &&
            legacy.CreateSnapshot(1).Players[1] is { Role: Role.Lord, GeneralId: "fixture:wy-other-1" } &&
            E<GeneralSelectedEvent>(legacy).Count(e => e.ActorSeat == 0 && e.GeneralId == "fixture:wy-owner") == 1 &&
            E<GeneralSelectedEvent>(legacy).Count(e => e.ActorSeat == 1 && e.GeneralId == "fixture:wy-other-1") == 1,
            "The Lord first selects the exact published Shensu producer, then the first Rebel AI selects the exact Zhenlie target through public role weights.");
        var legacyHp = legacy.State.Players[0].Hp;
        Activate(legacy, "boundary:shensu");
        ReachLegacyHuman(legacy, p => p.SkillPrompt?.SkillId == "boundary:shensu" && p.Choices.Any(c => c.Targets.SequenceEqual([0])));
        Answer(legacy, c => c.Parameters.GetValueOrDefault("program-action") == "select-target" && c.Targets.SequenceEqual([0]));
        for (var i = 0; i < 40 && !E<PaidOwnTargetAppliedEvent>(legacy).Any(); i++)
            Accept(legacy, new AdvanceOneStepCommand(legacy.Revision));
        var legacyPaid = E<PaidOwnTargetHpEvent>(legacy).Single();
        Require(legacyPaid.Receipt.Use is { ActionId: null, LegacyProducerProgramId: not null, ActorSeat: 1, ProviderSeat: 1, TargetSeat: 0, EffectiveKind: CardKind.Slash } &&
            legacy.State.Players[0].Hp == legacyHp - 1 && E<PaidOwnTargetAppliedEvent>(legacy).Single().ActionId is null,
            "The registered legacy Shensu's real zero-entity Slash is protected using its exact original typed producer; it is not given a new Action or physical payment.");
        legacy = Restore(legacy, legacyRegistry);
        for (var i = 0; i < 40 && !E<ProgramBindingResolvedEvent>(legacy).Any(e => e.SkillId == Zhen && e.Completed); i++)
            Accept(legacy, new AdvanceOneStepCommand(legacy.Revision));
        Require(E<ProgramBindingResolvedEvent>(legacy).Any(e => e.SkillId == Zhen && e.Completed) &&
            E<ProgramSkillHpLostEvent>(legacy).Count(e => e.FrameId == legacyPaid.ProgramFrameId) == 1 &&
            !legacy.CardMovements.Any(m => m.CardId == 0),
            "The restored native protected target finishes one paid cost and the original legacy program without inventing a zero-ID entity.");
    }

    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Activation(PendingDecision p, string skill) => p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Play(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Use(GameEngine g, string binding, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, binding, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Activate(GameEngine g, string skill) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == skill);
    private static void Option(GameEngine g, string option) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == option);
    private static void Continue(GameEngine g) => Option(g, "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; var c = p.Choices.First(predicate); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c.Id, g.Revision)); }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate) { for (var i = 0; i < 240; i++) { if (P(g) is { } p && predicate(p)) return; Advance(g); } throw new InvalidOperationException("Fixed actual command fixture did not reach its expected boundary."); }
    private static void ReachLegacyHuman(GameEngine g, Func<PendingDecision, bool> predicate)
    {
        for (var i = 0; i < 160; i++)
        {
            if (P(g) is { PlayerSeat: 1 } p)
            {
                if (predicate(p)) return;
                if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass"))
                { Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass"); continue; }
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The real human legacy producer did not reach its expected boundary.");
    }
    private static void Until(GameEngine g, Func<bool> predicate) { for (var i = 0; i < 240; i++) { if (predicate()) return; Advance(g); } throw new InvalidOperationException("Fixed actual command fixture did not finish its typed return."); }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p?.SkillPrompt?.SkillId is Hp or Entry) Continue(g);
        else if (p is not null && (Activation(p, Zhen) || Activation(p, Miji))) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is not null && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass")) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected actual command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Restore(GameEngine g, ContentRegistry r) { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Require(State(g) == State(cold), "Four private views, exact original use, paid HP, gifts and scalar earned Ending promises survive real JSON restore."); return cold; }
    private static void Reject(GameEngine g) { var p = P(g)!; var before = State(g); Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("not-published"), g.Revision)).Accepted && before == State(g), "Rejected choices do not advance cost, promise, gift or any private view."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static (GameEngine, ContentRegistry) Create(string card = "standard:peach", bool observers = true, bool sourceLoss = false, bool gainDying = false, bool native = false, bool shortDeck = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(card, observers, sourceLoss, gainDying, native, shortDeck));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, ModeId = Mode, HumanSeat = native ? -1 : 0, HumanRole = native ? null : Role.Lord,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 6 }, r);
        Accept(g, new StartGameCommand()); if (!native) { Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0); Accept(g, new SelectGeneralCommand(0, "fixture:wy-owner", g.Revision, P(g)!.PromptId)); }
        if (sourceLoss)
        {
            Play(g);
            Require(!g.CreateSnapshot(0).Players[0].Skills!.Any(s => s.Id == Zhen),
                "The source-loss fixture initially has no printed Zhenlie grant.");
            Use(g, "grant-zhenlie"); Play(g);
            var owner = g.CreateSnapshot(0).Players[0];
            Require(owner.Skills!.Any(s => s.Id == Zhen) && owner.SkillRuntimeStates!.Single(s => s.SkillId == Zhen).IsAcquired &&
                E<SkillsAcquiredEvent>(g).Count(e => e.PlayerSeat == 0 && e.SourceSkillId == Driver && e.SkillIds.Contains(Zhen)) == 1,
                "One actual activation independently grants Zhenlie before its irreversible HP payment and later qualification suppression.");
        }
        return (g, r);
    }
    private static (GameEngine, ContentRegistry) CreateLegacySource()
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(), new Fixture("standard:slash", false, false, false, true, false, legacySource: true));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, ModeId = Mode, HumanSeat = 1, HumanRole = Role.Lord,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 6 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 1);
        Require(P(g)!.ValidContentIds.Contains("fixture:wy-other-1") && !E<GeneralSelectedEvent>(g).Any(),
            "The exact registered Shensu general is published to the human Lord before any AI consumes the shared candidate pool.");
        Accept(g, new SelectGeneralCommand(1, "fixture:wy-other-1", g.Revision, P(g)!.PromptId)); return (g, r);
    }
    private sealed class Fixture(string card, bool observers, bool sourceLoss, bool gainDying, bool native, bool shortDeck, bool legacySource = false) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-wang-yi", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
            {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":30,"priority":0}],"activations":[
            {"id":"enemy-duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
            {"id":"hurt-three","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":3}]},
            {"id":"hurt-one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
            {"id":"drain","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]}]},
            {"id":"{{Hp}}","revision":1,"triggers":[{"id":"hp-child","window":"afterHealthChanged","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
            {"id":"{{Entry}}","revision":1,"triggers":[{"id":"entry","window":"dyingEntering","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
            {"id":"{{Loss}}","revision":1,"triggers":[{"id":"loss","window":"afterHpLost","subject":"owner","optional":false,"effects":[{"op":"grantSkills","target":"owner","skillIds":["{{SourceSuppression}}"]}]}]},
            {"id":"fixture:wy-gain","revision":1,"triggers":[{"id":"gift-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.lost-hp-owned-gift.give"],"optional":false,"effects":[{"op":"loseHp","target":"owner","amount":6}]}]}]}
            """;
            if (sourceLoss)
            {
                var sourceRules = JsonNode.Parse(rules)!;
                ((JsonArray)sourceRules["skills"]![0]!["activations"]!).Add(JsonNode.Parse("""{"id":"grant-zhenlie","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"grantSkills","target":"owner","skillIds":["boundary:zhenlie-current"]}]}"""));
                rules = sourceRules.ToJsonString();
            }
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实能力驱动", description = "实际对方决斗及体力状态" },
                [Hp] = new { name = "HP真实子窗", description = "确切体力变化", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Entry] = new { name = "真实濒死入口", description = "实付后的 typed Dying", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Loss] = new { name = "真实来源资格抑制", description = "已付后的技能资格失效" }, ["fixture:wy-gain"] = new { name = "真实收牌濒死", description = "一次真实收牌" } };
            var c = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = 3, skills = labels }));
            foreach (var id in c.Programs.Keys) b.AddSkill(new(id, id, "正式通用能力") { Program = c.Programs[id], ProgramPresentation = c.Presentations[id] });
            b.AddSkill(new(SourceSuppression, "已付来源资格抑制", "真实Lord由4HP支付至3HP时抑制其他武将技能") { SuppressionRule = new(3), Tags = SkillTag.Locked });
            var ownerRole = legacySource ? Role.Rebel : Role.Lord;
            foreach (var owner in new[] { true, false }) b.AddSkill(new(owner ? "fixture:wy-pick-owner" : "fixture:wy-pick-other", "固定公开选将", "正式角色权重")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => (role == ownerRole) == owner ? 10000d : -10000d) });
            var extras = new List<string> { Miji }; if (!sourceLoss) extras.Add("fixture:wy-pick-owner"); if (!native) extras.Add(Driver); if (observers) extras.Add(Hp); if (observers || gainDying) extras.Add(Entry); if (sourceLoss) extras.Add(Loss);
            b.AddGeneral(new("fixture:wy-owner", "真实界王异来源", "supporter", sourceLoss ? "fixture:wy-pick-owner" : Zhen, "wei", 3, extras));
            for (var i = 1; i < 4; i++)
            {
                var otherSkills = new List<string>();
                if (i == 1 && gainDying) otherSkills.AddRange(["fixture:wy-gain", Entry]);
                if (i == 1 && legacySource) otherSkills.Add("boundary:shensu");
                b.AddGeneral(new($"fixture:wy-other-{i}", "其他角色", "supporter", "fixture:wy-pick-other", "shu", 6, otherSkills));
            }
            b.AddDeck(new("fixture:wy-deck", "固定真实实体", 4, 0, []) { PhysicalCards = Enumerable.Range(0, shortDeck ? 20 : 64)
                .Select(i => new ContentDeckPhysicalCard(native ? i % 4 == 0 ? "standard:peach" : "standard:slash" : card, Suit.Club, 7)).ToArray() });
            b.AddMode(new(Mode, "真实贞烈秘计", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:wy-deck",
                GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:wy-owner", "fixture:wy-other-1", "fixture:wy-other-2", "fixture:wy-other-3"]));
        }
    }
}
