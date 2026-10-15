using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class BoundaryFuHuangHouChecks
{
    private const string Contest = "boundary:zhuikong-current", Aid = "boundary:qiuyuan-current",
        Driver = "fixture:fhh-driver", Gain = "fixture:fhh-gain", Entry = "fixture:fhh-entry";
    private const string Mode = "identity:classic-fhh-fixture", Claim = "program.foreign-turn-contest.claim",
        Gift = "program.same-type-actual-use-aid.gift";

    public static void ForeignActualTurnWinDeclineAndExpiry()
    {
        var (g, r) = Create(win: true); Play(g); End(g);
        Reach(g, p => Offer(p, Contest)); Reject(g); var before = g.CardMovements.Count;
        g = Restore(g, r); Skip(g);
        Reach(g, p => Offer(p, Contest));
        Require(g.CardMovements.Count(m => m.Reason == CardMoveReasons.PindianReveal) == 0 && g.CardMovements.Count >= before,
            "Declining a foreign actual-turn opportunity never selects or pays a Pindian card.");
        var window = g.ResolutionStack.OfType<ForeignActualTurnStartWindowFrame>().Single();
        Require(window.OwnerSeat == g.State.CurrentSeat && window.OwnerSeat != 0 &&
            window.Contexts.All(c => c.SourceSeat == window.OwnerSeat && c.TargetSeat == window.OwnerSeat),
            "A real foreign turn freezes its original actor instead of recomputing a later live seat.");
        var contexts = (IList<ProgramSkillWindowContext>)window.Contexts;
        var blocked = false; try { contexts[0] = contexts[0] with { SourceSeat = -1 }; } catch (NotSupportedException) { blocked = true; }
        Require(blocked, "New window context collections are frozen before diagnostics exposure.");
        ContestNow(g); Until(g, () => E<ForeignTurnContestRestrictedEvent>(g).Any());
        var restricted = E<ForeignTurnContestRestrictedEvent>(g).Single();
        var grant = E<CardTargetRestrictionGrantedEvent>(g).Single(e => e.Restriction.GrantSequence == restricted.GrantSequence).Restriction;
        Require(restricted.Origin.Result.SourceWon && restricted.Origin.Result.SourceRank == 13 &&
            grant.SubjectSeat == restricted.Origin.Result.OpponentSeat && grant.Restriction == SkillProgramCardTargetRestriction.SelfOnly &&
            grant.TurnNumber == restricted.Origin.ActualTurnNumber &&
            !E<ForeignTurnContestSlashIssuedEvent>(g).Any(), "A true winning result issues one opponent self-only actual-turn restriction.");
        g = Restore(g, r);
        Until(g, () => E<TurnCardUseEffectsExpiredEvent>(g).Any(e => e.GrantSequences.Contains(restricted.GrantSequence)));
        Require(E<ForeignTurnContestRestrictedEvent>(g).Count(e => e.Origin == restricted.Origin) == 1,
            "Cold continuation does not repeat the winning issuance and its grant expires with that same actual turn.");
    }

    public static void DamageTrickGroupPrivateGiftLegacyVirtualAndNativeContracts()
    {
        foreach (var kind in new[] { CardKind.FireAttack, CardKind.BarbarianAssault, CardKind.ArrowBarrage })
        {
            var (g, r) = Create(deck: "fireAttack", aid: true, ordinary: kind); Play(g); End(g);
            Reach(g, p => Offer(p, Aid)); Activate(g);
            Reach(g, p => p.SkillPrompt?.SkillId == Aid && p.Choices.Any(c => c.Parameters.GetValueOrDefault("aid-option") == "recipient"));
            Answer(g, c => c.Targets.SequenceEqual([2]));
            if (kind == CardKind.FireAttack)
            {
                Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.FireAttackReveal);
                var fact = E<SameTypeAidTargetAddedEvent>(g).Single();
                Require(fact.Use.EffectiveKind == kind && fact.RecipientSeat == 2, "The native recipient accepts a real FireAttack additional target.");
                g = Restore(g, r); Answer(g, c => c.Cards.Count == 1);
                Until(g, () => E<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == fact.Use.CardUseFrameId));
                Require(E<FireAttackResolvedEvent>(g).Where(e => e.ResolutionId == fact.Use.CardUseFrameId).Select(e => e.TargetSeat)
                    .Distinct().Order().SequenceEqual([0, 2]), "Each actual FireAttack target runs its own reveal/discard/damage chain.");
            }
            else
            {
                Reach(g, p => p.SkillPrompt?.SkillId == Gain);
                var paid = E<SameTypeAidGiftPaidEvent>(g).Single();
                Require(paid.Use.EffectiveKind == kind && !E<SameTypeAidTargetAddedEvent>(g).Any(),
                    "An already included AOE recipient gives a different-name Trick and never becomes a duplicate target.");
                g = Restore(g, r); Continue(g);
                Until(g, () => E<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == paid.Use.CardUseFrameId));
                Require(g.CardMovements.Count(m => m.Sequence == paid.MovementSequence && m.Reason.Value == Gift) == 1,
                    "The restored AOE gift does not bypass its normal target responses or repay a hidden card.");
            }
        }
        var (legacy, lr) = Create(deck: "peach", aid: true, legacy: true, fragileRecipients: true); Play(legacy); End(legacy);
        Reach(legacy, p => Offer(p, Aid)); Activate(legacy);
        Reach(legacy, p => p.SkillPrompt?.SkillId == Aid && p.Choices.Any(c => c.Parameters.GetValueOrDefault("aid-option") == "recipient"));
        Answer(legacy, c => c.Targets.SequenceEqual([2])); Reach(legacy, p => p.SkillPrompt?.SkillId == Gain);
        var old = E<SameTypeAidGiftPaidEvent>(legacy).Single();
        Require(old.Use.CardActionId is null && old.Use.LegacyProducerProgramId is not null &&
            !E<CardActionAcceptedEvent>(legacy).Any(e => e.Action.Type == CardActionType.Use && e.Action.ActorSeat == old.Use.ActorSeat &&
                e.Action.EffectiveKind == CardKind.Slash), "A real registered mandatory legacy producer retains its Action-null use and can request aid without a manufactured Action.");
        legacy = Restore(legacy, lr); Continue(legacy);
        Until(legacy, () => E<CardUseFinishedEvent>(legacy).Any(e => e.ResolutionId == old.Use.CardUseFrameId) ||
            !legacy.ResolutionStack.OfType<CardUseFrame>().Any(u => u.Id == old.Use.CardUseFrameId));

        var (native, _) = Create(win: true, native: true);
        for (var i = 0; i < 80 && native.State.Status != EngineStatus.Completed && !E<ForeignTurnContestRestrictedEvent>(native).Any(); i++)
            Accept(native, new AdvanceOneStepCommand(native.Revision));
        Require(E<ForeignTurnContestRestrictedEvent>(native).Any() && E<PindianResultDeterminedEvent>(native).Any(e => e.SkillId == Contest),
            "Native automatic play truly accepts and pays a foreign Pindian opportunity, without manually answering a bot.");
        foreach (var trigger in new[] {
            """{"id":"wrong","window":"otherActualTurnStarted","subject":"owner","optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"eventSource"},{"op":"startPindian","target":"owner","opponentRef":{"kind":"selectedTarget"},"resultBind":"x","visibility":"public"},{"op":"resolveForeignTurnPindian","target":"owner","sourceBind":"x"}]}""",
            """{"id":"wrong","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["slash"],"optional":true,"effects":[{"op":"offerSameTypeDifferentNameOrExtraTarget","target":"owner"}]}""" })
        {
            var failed = false; try { SkillProgramCatalog.Load("{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion +
                ",\"skills\":[{\"id\":\"fixture:fhh-invalid\",\"revision\":1,\"triggers\":[" + trigger + "]}]}",
                """{"schemaVersion":3,"skills":{"fixture:fhh-invalid":{"name":"精确合同","description":"无旁路"}}}"""); }
            catch (InvalidOperationException) { failed = true; }
            Require(failed, "Only the exact optional foreign contest and supported original-target aid contracts load.");
        }
    }

    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Offer(PendingDecision p, string skill) => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == skill &&
        p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void ContestNow(GameEngine g)
    {
        Activate(g); Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "pindian-source-card"));
        Answer(g, c => c.Parameters.GetValueOrDefault("action") == "pindian-source-card");
    }
    private static void Play(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string activation, IReadOnlyList<int> cards, IReadOnlyList<int> targets) =>
        Accept(g, new UseProgramSkillCommand(0, Driver, activation, cards, targets, g.Revision, P(g)!.PromptId));
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Skip(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static bool IsDecline(PendingDecision p, PromptChoice c) =>
        c.Parameters.GetValueOrDefault("response") == (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash ? "take-damage" : "pass");
    private static void Pass(GameEngine g) => Answer(g, c => IsDecline(P(g)!, c));
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> done)
    { Until(g, () => P(g) is { } p && done(p)); }
    private static void Until(GameEngine g, Func<bool> done)
    { for (var i = 0; i < 160; i++) { if (done()) return; Advance(g); } throw new InvalidOperationException("Fixed Fu Huang Hou boundary not reached: " + JsonSerializer.Serialize(P(g))); }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Skip(g);
        else if (p is { PlayerSeat: 0 } && p.Kind == DecisionKind.RescueDying && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "peach"))
            Answer(g, c => c.Parameters.GetValueOrDefault("response") == "peach");
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => IsDecline(p, c))) Pass(g);
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"))
            Answer(g, c => c.Targets.Count == 1);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Real command rejected."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Restore(GameEngine g, ContentRegistry registry)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(State(g) == State(restored), "Four private views, original entities, exact owning receipts and children survive real JSON restore."); return restored; }
    private static void Reject(GameEngine g)
    { var p = P(g)!; var state = State(g); Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && state == State(g),
        "Unpublished input leaves all private views, payments, facts and accepted commands unchanged."); }
    private static void Require(bool result, string text) { if (!result) throw new InvalidOperationException(text); }

    private static (GameEngine, ContentRegistry) Create(string deck = "dodge", bool win = false, bool aid = false, bool sourceLoss = false,
        bool claimDying = false, bool fragileRecipients = false, bool peerAid = false, bool gear = false, bool native = false,
        bool legacy = false, CardKind? ordinary = null, bool giftDying = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
            new Fixture(deck, win, aid, sourceLoss, claimDying, fragileRecipients, peerAid, gear, native, legacy, ordinary, giftDying));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, ModeId = Mode,
            HumanSeat = native ? -1 : 0, HumanRole = native ? null : Role.Lord, UseInteractiveSetup = true,
            UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(g, new StartGameCommand());
        if (!native) { Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.SelectGeneral);
            Accept(g, new SelectGeneralCommand(0, "fixture:fhh-owner", g.Revision, P(g)!.PromptId)); }
        return (g, registry);
    }
    private sealed class Fixture(string deck, bool win, bool aid, bool sourceLoss, bool claimDying, bool fragileRecipients,
        bool peerAid, bool gear, bool native, bool legacy, CardKind? ordinary, bool giftDying) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-fu-huang-hou", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var terminal = sourceLoss
                ? ",{\"op\":\"grantSkills\",\"target\":\"owner\",\"skillIds\":[\"fixture:fhh-source-suppression\"]}"
                : claimDying ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":6}" : "";
            var giftTerminal = giftDying ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":6}" : "";
            var peachCompleted = giftDying
                ? ",{\"id\":\"peach-completed\",\"window\":\"cardUseCompleted\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"peach\"],\"includeResponseUses\":true,\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"peach-seen\",\"options\":[{\"id\":\"continue\"}]}]}"
                : "";
            var aoe = ordinary is CardKind.BarbarianAssault or CardKind.ArrowBarrage
                ? "\"viewAs\":[{\"id\":\"actual-aoe\",\"inputKinds\":[\"fireAttack\"],\"inputSuits\":[],\"allowSameKind\":true,\"outputKind\":\"" +
                    (ordinary == CardKind.BarbarianAssault ? "barbarianAssault" : "arrowBarrage") + "\",\"forPlay\":true,\"forResponse\":false,\"singleCardTrickUse\":true}]," +
                    "\"triggers\":[{\"id\":\"actual-aoe-only\",\"window\":\"drawPhaseStarting\",\"subject\":\"owner\",\"optional\":false," +
                    "\"effects\":[{\"op\":\"grantTurnCardActionProhibition\",\"target\":\"owner\",\"cardKinds\":[\"fireAttack\"],\"actionTypes\":[\"use\"]}]}],"
                : "";
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
            {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":20,"priority":0}],
             "viewAs":[{"id":"two-slash","inputKinds":[],"inputSuits":[],"allowSameKind":true,"outputKind":"slash","forPlay":true,"forResponse":false,"inputCount":2,"extendedUse":true,"sourceZones":["hand","equipment"]}],
             "activations":[
               {"id":"opponent-duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
               {"id":"gear","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
               {"id":"double-slash","minCards":2,"maxCards":2,"sourceZones":["hand","equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useSelectedCardsAs","target":"selectedTarget","sourceBind":"two-slash","outputKind":"slash"}]}]},
            {"id":"{{Gain}}","revision":1,"triggers":[
              {"id":"original-claim","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementReasons":["{{Claim}}"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"claim-seen","options":[{"id":"continue"}]}{{terminal}}]},
              {"id":"private-gift","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementReasons":["{{Gift}}"],"movementOccurrence":"perSourceOwner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gift-seen","options":[{"id":"continue"}]}{{giftTerminal}}]}]},
            {"id":"{{Entry}}","revision":1,"triggers":[{"id":"entry","window":"dyingEntering","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"entry-seen","options":[{"id":"continue"}]}]}{{peachCompleted}}]},
            {"id":"fixture:fhh-legacy","revision":1,"triggers":[{"id":"mandatory-legacy-slash","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingVirtualSlashTarget"},{"op":"skipTurnPhases","target":"owner","phases":["judgment","draw"]},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget"}]}]},
            {"id":"fixture:fhh-completed","revision":1,"triggers":[{"id":"completed","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"completed-seen","options":[{"id":"continue"}]}]}]},
            {"id":"fixture:fhh-native-aoe","revision":1,{{aoe}}"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":20,"priority":0}]}
            ]}
            """;
            var label = new Dictionary<string, object> {
                [Driver] = new { name = "真实用牌驱动", description = "成熟零实体决斗与实际多材料杀" },
                [Gain] = new { name = "实际收益子链", description = "原实体收益", optionLabels = new Dictionary<string,string> { ["continue"] = "继续" } },
                [Entry] = new { name = "真实濒死入口", description = "真实救援", optionLabels = new Dictionary<string,string> { ["continue"] = "继续" } },
                ["fixture:fhh-legacy"] = new { name = "固定旧生产者", description = "注册的实际阶段替换及虚拟杀" },
                ["fixture:fhh-completed"] = new { name = "真实完成窗口", description = "全部目标完成", optionLabels = new Dictionary<string,string> { ["continue"] = "继续" } },
                ["fixture:fhh-native-aoe"] = new { name = "实际AOE转换", description = "真实实体转换" }
            };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = 3, skills = label }));
            foreach (var id in catalog.Programs.Keys) b.AddSkill(new(id, id, "真实通用夹具") { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:fhh-source-suppression", "已付来源资格抑制", "保留已领取实体并取消未发出的杀") { SuppressionRule = new(6), Tags = SkillTag.Locked });
            foreach (var owner in new[] { true, false }) b.AddSkill(new(owner ? "fixture:fhh-owner-weight" : "fixture:fhh-other-weight", "固定原生选将", "实际角色权重")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => (role == Role.Lord) == owner ? 10000d : -10000d) });
            var source = new List<string> { "fixture:fhh-owner-weight" };
            if (!native) source.AddRange([Driver, Gain]); if (win) source.Add("classic:tianbian");
            if (aid) source.Add(Aid); if (claimDying || giftDying) source.Add(Entry); if (peerAid) source.Add("fixture:fhh-completed");
            b.AddGeneral(new("fixture:fhh-owner", "当前伏皇后真实来源", "supporter", Contest, "qun", 6, source.ToArray()) { InitialHp = 5 });
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:fhh-peer-{i}", "实际对手", "supporter", "fixture:fhh-other-weight", "wei", 8,
                new[] { "fixture:fhh-native-aoe" }.Concat(peerAid ? [Aid] : Array.Empty<string>()).Concat(legacy ? ["fixture:fhh-legacy"] : Array.Empty<string>()).ToArray())
                { InitialHp = fragileRecipients ? 1 : null });
            b.AddDeck(new("fixture:fhh-deck", "固定真实实体", 4, 2, []) {
                PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard(
                    gear && i >= 64 ? "classic:qinglong-crescent-blade" : "standard:" + (deck == "fireAttack" ? "fire_attack" : deck), Suit.Heart, 7)).ToArray() });
            b.AddMode(new(Mode, "当前伏皇后实际付款", 4, 4, new Dictionary<string,int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:fhh-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:fhh-owner","fixture:fhh-peer-1","fixture:fhh-peer-2","fixture:fhh-peer-3"]));
        }
    }
}
