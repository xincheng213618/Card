using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CaoXiChecks
{
    private const string Gang = "ol:gangshu", Jian = "ol:jianxuan", Owner = "fixture:cx-owner", Driver = "fixture:cx-driver",
        Quiet = "fixture:cx-quiet", Response = "fixture:cx-response", Source = "fixture:cx-source", Phase = "fixture:cx-phase",
        Watch = "fixture:cx-watch", Health = "fixture:cx-health", Mode = "identity:classic-cao-xi-fixture";
    public static void NonbasicValuesCapWeaponsAndNativeSlashQuotaCold()
    {
        var (g, registry) = Start();
        foreach (var (query, times) in new[] { (SkillRuleQuery.AttackRange, 4), (SkillRuleQuery.DrawCount, 3), (SkillRuleQuery.SlashLimit, 4) })
            for (var i = 0; i < times; i++) Grow(ref g, registry, query);
        Need(E<CaoXiGangshuIncreasedEvent>(g).Length == 11 && E<CaoXiGangshuIncreasedEvent>(g).All(e => e.AfterValue == e.BeforeValue + 1 && e.AfterValue <= 5) && g.GetAttackRange(0) == 5,
            "Eleven genuine completed nonbasic uses independently grow 1/2/1 to 5/5/5 without changing any other numerical source.");
        var starts = E<CaoXiGangshuStartedEvent>(g).Length; Use(g, "study"); Play(g);
        Need(E<CaoXiGangshuStartedEvent>(g).Length == starts, "All three capped numbers suppress the optional trigger rather than offering an illegal sixth point.");
        for (var i = 0; i < 5; i++)
        {
            var action = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]));
            Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, P(g)!.PromptId, action.PlayedCardKind)); Play(g);
        }
        Need(!g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash) && E<CardActionAcceptedEvent>(g).Count(e => e.Action.ActorSeat == 0 && e.Action.EffectiveKind == CardKind.Slash) == 5,
            "The actual native phase quota permits exactly five physical Slashes, rather than only displaying an increased number.");
        _ = Cold(g, registry);
        foreach (var scenario in new[] { "bow", "crossbow" })
        {
            (g, registry) = Start(scenario); Use(g, "equip", [0]); Activate(g, Gang); Reach(g, () => GangChoice(g));
            Need(P(g)!.Choices.All(c => c.Parameters.GetValueOrDefault("query") != (scenario == "bow" ? "AttackRange" : "SlashLimit")) &&
                (scenario != "bow" || g.GetAttackRange(0) == 9), "Real equipment and a separate native range modifier remain authoritative; a value above five or infinite cannot be incremented.");
            g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("query") == "DrawCount"); Play(g); _ = Cold(g, registry);
        }
    }
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static bool Optional(GameEngine g, string skill) => P(g)?.SkillPrompt?.SkillId == skill && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool GangChoice(GameEngine g) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "cao-xi" && c.Parameters.ContainsKey("query")) == true;
    private static bool Option(GameEngine g, string option) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == option) == true;
    private static void Need(bool yes, string why) { if (!yes) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand command) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected actual Cao Xi command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick) { var p = P(g)!; Need(V(g, p.PlayerSeat).IsHuman, "Never answer another player's AI prompt."); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(pick).Id, g.Revision)); }
    private static void Activate(GameEngine g, string skill) { Reach(g, () => Optional(g, skill)); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); }
    private static void Continue(GameEngine g) { if (V(g, P(g)!.PlayerSeat).IsHuman) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
    private static void Use(GameEngine g, string id, int[]? targets = null, int[]? cards = null)
    {
        if (id == "study")
        {
            var a = g.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == Driver && a.PlayedCardKind == CardKind.DrawTwo);
            Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource });
        }
        else Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Grow(ref GameEngine g, ContentRegistry r, SkillRuleQuery query, string activation = "study")
    {
        var current = g; Use(current, activation, activation == "equip" ? [0] : []); Activate(current, Gang); Reach(current, () => GangChoice(current));
        Need(P(current) is { PlayerSeat: 0, IsPrivate: true } && Enumerable.Range(1, 3).All(s => current.CreateSnapshot(s).PendingDecision is null), "Only the source sees its frozen private numerical decision.");
        var view = State(g); var p = P(g)!; Need(!g.Submit(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, g.Revision)).Accepted && view == State(g), "A foreign seat cannot answer or mutate the private frozen choice.");
        g = Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("query") == query.ToString()); Play(g);
    }
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 440; step++)
        {
            if (done()) return; Need(g.State.Winner == Winner.None, "Cao Xi ended before its real boundary: " + Diagnostic(g)); var p = P(g);
            if (p is not null && !V(g, p.PlayerSeat).IsHuman) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (p is { Kind: DecisionKind.PlayCard }) End(g);
            else if (p is { Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification }) Answer(g, c => c.Cards.Count == 0);
            else if (p is { Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
            else if (Option(g, "continue")) Continue(g);
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip") == true) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is not null) throw new InvalidOperationException("Unexpected Cao Xi private prompt: " + Diagnostic(g));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing Cao Xi actual boundary: " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.Winner,
        Prompt = P(g) is { } p ? new { p.Kind, p.PlayerSeat, Skill = p.SkillPrompt?.SkillId, Choices = p.Choices.Select(c => c.Parameters) } : null,
        Players = Enumerable.Range(0, 4).Select(s => new { Seat = s, V(g, s).Hp, V(g, s).IsAlive, V(g, s).GeneralId, V(g, s).HandCount }),
        Frames = g.ResolutionStack.Select(f => new { f.Id, Type = f.GetType().Name }),
        Counters = E<NullificationRespondedEvent>(g), Offsets = E<CaoXiGangshuActualOffsetEvent>(g), Tail = g.Events.TakeLast(7).Select(e => e.Payload.ToString()) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(copy), "Cold replay preserves exact private numerical choices, native offset stage, per-unit Draw reservation and the already paid recursive movement cursor."); return copy; }
    private static (GameEngine, ContentRegistry) Start(string scenario = "plain")
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario)); var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0,
            HumanRole = scenario is "source-death" or "recipient-death" ? Role.Renegade : Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, AiPolicyVersion = scenario == "ambush" ? 2 : 1, MaxTurns = 12 }, r);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, r);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-cao-xi", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:cao-xi"); if (scenario == "uncancelable") CharacterFixtureContent.Register(b, "ol:yan-rou");
            if (scenario == "ambush") CharacterFixtureContent.Register(b, "ol:cheng-yu");
            if (scenario == "guess") CharacterFixtureContent.Register(b, "ol:wang-rong");
            if (scenario == "nested-draw") CharacterFixtureContent.Register(b, "ol:zhang-shi-ping");
            var rules = JsonNode.Parse($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
             {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":70,"priority":0}],"viewAs":[{"id":"study","inputKinds":[],"inputSuits":[],"outputKind":"drawTwo","forPlay":true,"forResponse":false,"singleCardTrickUse":true}],"activations":[
              {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
              {"id":"duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
              {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"offerVirtualSlashOrDraw","target":"selectedTarget"}]},
              {"id":"hurt-one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
              {"id":"hurt-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]},
              {"id":"arm","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
              {"id":"wound-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":2}]},
              {"id":"trim","minCards":1,"maxCards":60,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","sourceZones":["hand"],"usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"trimmed"},{"op":"moveBoundCards","target":"owner","sourceBind":"trimmed","destination":"discardPile","awaitMovementTriggers":true}]},
              {"id":"trim-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]}]},
             {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"{{{Response}}}","revision":1,"triggers":[{"id":"actual-response","window":"cardResponseAccepted","ownerRelation":"actor","cardKinds":["slash","dodge","nullification"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"response","options":[{"id":"continue"}]}]}]}
            ]}
            """)!;
            var labels = new Dictionary<string, object> { [Driver] = new { name = "实际命令准备", description = "只用真实原生卡牌、伤害和移动建立边界" }, [Quiet] = new { name = "实际安静回合", description = "跳过其他角色出牌" },
                [Response] = new { name = "真实响应已支付", description = "在原生响应子帧暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            void Add(string id, string body, Dictionary<string, string>? options = null)
            { rules["skills"]!.AsArray().Add(JsonNode.Parse("{\"id\":\"" + id + "\",\"revision\":1," + body + "}")); labels[id] = new { name = id, description = "真实阶段和支付子树", optionLabels = options ?? new() }; }
            var extras = new List<string> { Driver, Gang, Jian, Response };
            if (scenario == "nested-draw") extras.Add("ol:hongji");
            if (scenario == "ambush")
            {
                extras.Add("ol:shefu");
                Add(Phase, "\"triggers\":[{\"id\":\"real-ai-trick\",\"window\":\"playEnding\",\"subject\":\"owner\",\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"useVirtualOrdinaryTrick\",\"target\":\"owner\",\"actorRef\":{\"kind\":\"owner\"},\"targetRef\":{\"kind\":\"owner\"},\"outputKind\":\"drawTwo\"}]}]");
                Add(Source, "\"triggers\":[{\"id\":\"empty-other-ai-opening\",\"window\":\"gameStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"discardOwnedZoneCards\",\"target\":\"owner\",\"zones\":[\"hand\"]}]}]");
            }
            if (scenario == "counterspell") Add(Phase, "\"triggers\":[{\"id\":\"empty-real-foreign-hands\",\"window\":\"gameStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"discardOwnedZoneCards\",\"target\":\"owner\",\"zones\":[\"hand\"]}]}]");
            if (scenario == "bow") { Add(Source, "\"modifiers\":[{\"id\":\"independent-range\",\"query\":\"attackRange\",\"operation\":\"add\",\"value\":4,\"priority\":0}]"); extras.Add(Source); }
            if (scenario is "skip" or "during-draw" or "guess" or "nested-draw")
            {
                Add(Source, "\"hpSkillGrants\":[{\"id\":\"exact-twelve\",\"skillId\":\"" + (scenario == "guess" ? "ol:jizhan" : Phase) + "\",\"minimumHp\":12,\"maximumHp\":12}]"); extras.Add(Source);
                if (scenario != "guess") Add(Phase, "\"triggers\":[{\"id\":\"actual-phase\",\"window\":\"" + (scenario == "skip" ? "turnStartBeforeNormalFlow" : "drawPhaseStarting") + "\",\"subject\":\"owner\",\"optional\":" + (scenario == "nested-draw" ? "true" : "false") + ",\"effects\":[" +
                    (scenario == "skip" ? "{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"draw\"]}" : "{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"currentTurnPlayer\"},{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"resultBind\":\"gear\"}") + ",{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1}]}]");
            }
            if (scenario == "uncancelable") extras.Add("ol:choutao");
            if (scenario is "paid-loss" or "source-death" or "recipient-death")
            {
                if (scenario == "paid-loss") { extras.Remove(Jian); extras.Add(Source); Add(Source, "\"hpSkillGrants\":[{\"id\":\"healthy-jianxuan\",\"skillId\":\"" + Jian + "\",\"minimumHp\":12}]"); }
                var child = scenario == "recipient-death" ? "{\"op\":\"damage\",\"target\":\"owner\",\"amount\":20}" : "{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"currentTurnPlayer\"},{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":" + (scenario == "source-death" ? "20" : "1") + "}";
                Add(Watch, "\"triggers\":[{\"id\":\"paid-draw-child\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"skill-program.cao-xi.jianxuan.draw\"],\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"pause\",\"options\":[{\"id\":\"continue\"}]},{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1}," + child + ",{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"after\",\"options\":[{\"id\":\"after-child\"}]}]}]", new() { ["continue"] = "继续", ["after-child"] = "继续已付谏旋" });
                Add(Health, "\"triggers\":[{\"id\":\"real-recovery-child\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"hpChangeOccurrence\":\"perEvent\",\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"pause\",\"options\":[{\"id\":\"continue\"}]}]}]", new() { ["continue"] = "继续" });
            }
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:cx-owner-pick", "本人公开评分", "固定选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) });
            b.AddGeneral(new(Owner, "曹羲本人", "ol-cao-xi", "fixture:cx-owner-pick", "wei", scenario is "source-death" or "recipient-death" ? 13 : 12, extras));
            var peers = Enumerable.Range(0, 3).Select(i => "fixture:cx-peer-" + i).ToArray();
            foreach (var (peer, index) in peers.Select((peer, index) => (peer, index)))
            { var pick = "fixture:cx-pick-" + index; b.AddSkill(new(pick, "公开选将次序", "确定性其他角色") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (3 - index)) });
                b.AddGeneral(new(peer, "真实其他角色", "supporter", pick, "wei", 14, scenario is "paid-loss" or "source-death" or "recipient-death" ? [Quiet, Watch, Health] : scenario == "counterspell" ? [Quiet, Phase] : scenario == "ambush" ? index == 0 ? [Phase, Gang] : [Quiet, Source] : [Quiet])); }
            var equipment = scenario == "crossbow" ? CardKind.Crossbow : scenario == "bow" ? CardKind.QilinBow : CardKind.SilverLion;
            b.AddCard(new("fixture:cx-gear", "实际装备", "装备牌", "原生装备槽及能力", equipment));
            b.AddDeck(new("fixture:cx-deck", "小固定实体牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 160).Select(i => new ContentDeckPhysicalCard(
                scenario is "bow" or "crossbow" ? "fixture:cx-gear" : scenario is "counterspell" or "ambush" ? i % 5 == 0 ? "fixture:cx-gear" : "standard:nullification" : i % 10 == 0 ? "fixture:cx-gear" : scenario is "dodge" or "uncancelable" ? "standard:dodge" : "standard:slash", Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "曹羲实际原生边界", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:cx-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
