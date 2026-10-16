using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ActualHandGainAndCategoryGiftChecks
{
    private const string Driver = "fixture:actual-gain-driver", Watch = "fixture:actual-gain-watch", Mode = "identity:classic-actual-hand-gains-4";
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void ActualHandGainsPreserveBatchIdentityAndExcludeSelfRecursion()
    {
        var (g, registry) = Start(holdInitialDraw: true);
        var f = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "ol:zishu");
        Require(f.ActualHandGain is { Operation: SkillProgramEffectOp.DrawAfterActualOwnHandGain, PaidIds.Count: 1, Gains.Count: 1 } &&
            E<ActualHandGainDrawPaidEvent>(g).Length == 1 && E<ForeignTurnHandGainsRecordedEvent>(g).Length == 0,
            "The original one-card normal-Draw batch issues one real bonus, with original entity identity on its paid frame.");
        Reject(g); Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); ReachPlay(g);
        var before = E<ActualHandGainDrawPaidEvent>(g).Length;
        Use(g, "gain-two", [], [1]); Reach(g, p => p.SkillPrompt?.SkillId == Watch);
        Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Single(p => p.SkillId == "ol:zishu").ActualHandGain is
            { Operation: SkillProgramEffectOp.DrawAfterActualOwnHandGain, PaidIds.Count: 1, Gains.Count: 2 } &&
            E<ActualHandGainDrawPaidEvent>(g).Length == before + 1,
            "One real atomic two-card acquisition issues one bonus and preserves both original acquisition identities.");
        Cold(g, registry); Continue(g); ReachPlay(g);
        Use(g, "two-singles", [], []); Reach(g, p => p.SkillPrompt?.SkillId == Watch); Continue(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Watch); Continue(g); ReachPlay(g);
        var draws = E<ActualHandGainDrawPaidEvent>(g).Skip(before).ToArray();
        Require(draws.Length == 3 && draws.Select(e => e.OriginalBatchId).Distinct().Count() == 3 && draws.All(e => e.ActualCount == 1),
            "One two-card gain and two independent single gains have three identities; each bonus excludes its own subsequent gain.");
        Cold(g, registry);
        // A source-boundary audit is intentionally synthetic: no hand entity changes during this read-only provenance query.
        var id = V(g).Hand[0].Id; var turn = g.CreateSnapshot(0).TurnNumber;
        var batch = new CardMovementBatchContext(999999, null, null, turn,
            [new(900000, turn, id, CardKind.SilverLion, CardLocation.Hand(0), CardLocation.Processing, new("fixture.hop")),
             new(900001, turn, id, CardKind.SilverLion, CardLocation.Processing, CardLocation.Hand(0), new("fixture.hop"))], []);
        var indexes = (int[])typeof(GameEngine).GetMethod("MatchingActualHandGainIndexes", Flags)!.Invoke(g, [batch, 0, CardLocation.Hand(0)])!;
        Require(indexes.Length == 0, "Moving an existing hand entity through Processing and back is not a new hand acquisition.");
        var reacquired = batch with { Movements =
            [new(900002, turn, id, CardKind.SilverLion, CardLocation.Hand(0), CardLocation.Equipment(0), new("fixture.leave")),
             new(900003, turn, id, CardKind.SilverLion, CardLocation.Equipment(0), CardLocation.Hand(0), new("fixture.reacquire"))] };
        var entities = (ActualHandGainEntity[])typeof(GameEngine).GetMethod("ActualHandGainEntities", Flags)!.Invoke(g, [reacquired, 0])!;
        Require(entities is [var regained] && regained.GainSequence == 900003, "An entity that genuinely leaves Hand and returns from Equipment has the new real acquisition identity.");
    }
    private static (GameEngine, ContentRegistry) Start(bool holdInitialDraw = false, bool foreign = false, bool killEndedActor = false, bool completedTargets = false, bool compoundTargets = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(foreign, killEndedActor, completedTargets, compoundTargets));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(g, new SelectGeneralCommand(0, "fixture:actual-gain-owner", g.Revision, P(g)!.PromptId));
        Reach(g, p => p.SkillPrompt?.SkillId == Watch); if (holdInitialDraw) return (g, registry);
        Continue(g); ReachPlay(g); return (g, registry);
    }
    private static PlayerSnapshot V(GameEngine g) => g.CreateSnapshot(0).Players[0];
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Use(GameEngine g, string id, IReadOnlyList<int> cards, IReadOnlyList<int> targets) => Accept(g, new UseProgramSkillCommand(0, Driver, id, cards, targets, g.Revision, P(g)!.PromptId));
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Continue(GameEngine g)
    {
        var prompt = P(g)!;
        if (prompt.PlayerSeat == 0) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        else
        {
            Require(prompt.Kind == DecisionKind.ProgramTrigger && prompt.SkillPrompt?.SkillId == Watch &&
                prompt.Choices.Count == 1 && prompt.Choices[0].Parameters.GetValueOrDefault("option-id") == "continue",
                "Only the recipient's exact single-choice movement child advances through its AI command boundary.");
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
    }
    private static void ReachPlay(GameEngine g, bool completeHandGainChildren = false) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0, completeHandGainChildren);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate, bool completeHandGainChildren = false)
    {
        for (var i = 0; i < 128; i++)
        {
            var p = P(g); if (p is not null && predicate(p)) return;
            if (completeHandGainChildren && p is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } && p.SkillPrompt?.SkillId == Watch &&
                p.Choices.Count == 1 && p.Choices[0].Parameters.GetValueOrDefault("option-id") == "continue") Continue(g);
            else Advance(g);
        }
        var prompt = P(g);
        throw new InvalidOperationException($"Fixed actual-gain fixture did not reach its boundary: turn={g.State.TurnNumber}, phase={g.State.Phase}, status={g.State.Status}, prompt={prompt?.Kind}/{prompt?.PlayerSeat}/{prompt?.SkillPrompt?.SkillId}, frames={string.Join(',', g.ResolutionStack.Select(f => f.GetType().Name + ':' + f.Id))}.");
    }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected fixed actual-gain command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static void Cold(GameEngine g, ContentRegistry registry) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry)), "Cold command replay preserves original gains, paid entities, native parents and private prompts.");
    private static void Reject(GameEngine g)
    { var before = State(g); var p = P(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && State(g) == before, "An unpublished choice changes neither the paid frame nor any viewer."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Fixture(bool foreign, bool killEndedActor, bool completedTargets, bool compoundTargets) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-actual-hand-gains", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var foreignTrigger = foreign ? $$"""
            ,"triggers":[{"id":"actual-foreign-gain","window":"playPhaseStarting","subject":"owner","turnOwnerScope":"otherLiving","usageScope":"game","usageLimit":1,"optional":false,"effects":[
              {"op":"draw","target":"owner","amount":2},{"op":"chooseOption","target":"owner","resultBind":"gains-usable","options":[{"id":"continue"}]},
              {"op":"selectOwnedCards","target":"owner","minimumCards":1,"maximumCards":1,"zones":["hand"],"resultBind":"spent"},
              {"op":"moveBoundCards","target":"owner","sourceBind":"spent","destination":"discardPile","awaitMovementTriggers":true}
              {{(killEndedActor ? ",{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"eventSource\"},{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":20}" : "")}}]}]
            """ : "";
            var extraPrograms = completedTargets ? """
            ,{"id":"fixture:gain-short","revision":1,"triggers":[{"id":"issued-tail","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["slash","fireSlash","thunderSlash"],"optional":false,"effects":[{"op":"offerShortRangeSlashTarget","target":"owner"}]}]}
            ,{"id":"fixture:gain-fire","revision":1,"triggers":[{"id":"committed-change","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash","fireSlash","thunderSlash"],"optional":false,"effects":[{"op":"offerCurrentSlashFireAndExtraTarget","target":"owner"}]}]}
            """ : "";
            if (compoundTargets) extraPrograms += """
            ,{"id":"fixture:gain-compound","revision":1,"triggers":[{"id":"new-pair","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["borrowedSword"],"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"addCurrentCardUseTarget","target":"selectedTarget"}]}]}
            """;
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"gain-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"selectedTarget","minimumCards":2,"maximumCards":2,"zones":["hand"],"resultBind":"acquired"},{"op":"moveBoundCards","target":"owner","sourceBind":"acquired","destination":"ownerHand","awaitMovementTriggers":true}]},
                {"id":"two-singles","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":1},{"op":"draw","target":"owner","amount":1}]},
                {"id":"fixture-many","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":3}]},
                {"id":"fixture-equipment","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
                {"id":"virtual","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useVirtualSlash","target":"selectedTarget"}]}]{{foreignTrigger}}},
              {"id":"{{Watch}}","revision":1,"triggers":[
                {"id":"paid-hand-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.actual-hand-gain.draw","program.same-category-deck.gift"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain-child","options":[{"id":"continue"}]}]},
                {"id":"paid-foreign-cleanup","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.foreign-turn-hand-gain.cleanup"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"cleanup-child","options":[{"id":"continue"}]}]}]}{{extraPrograms}}]}
            """;
            var names = new[] { Driver, Watch }.Concat(completedTargets ? ["fixture:gain-short", "fixture:gain-fire"] : Array.Empty<string>())
                .Concat(compoundTargets ? ["fixture:gain-compound"] : Array.Empty<string>())
                .ToDictionary(id => id, id => (object)new { name = id, description = "真实取得与原生使用完结", optionLabels =
                    id == Watch || id == Driver && foreign ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() });
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var pair in catalog.Programs) b.AddSkill(new(pair.Key, pair.Key, "固定共享边界") { Program = pair.Value });
            b.AddSkill(new("fixture:actual-gain-idle", "无技能", "固定对照"));
            var skills = new List<string> { "ol:yingyuan", Driver, Watch };
            if (completedTargets) skills.AddRange(["fixture:gain-short", "fixture:gain-fire"]);
            if (compoundTargets) skills.Add("fixture:gain-compound");
            b.AddGeneral(new("fixture:actual-gain-owner", "固定拥有者", "supporter", "ol:zishu", "shu", 4, skills, GeneralGender.Male));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:actual-gain-peer-{i}", "固定其他角色", "supporter", "fixture:actual-gain-idle", "wei", 4, [Watch], GeneralGender.Male));
            b.AddDeck(new("fixture:actual-gain-deck", "固定装备实体", 4, 1, []) { PhysicalCards = compoundTargets
                ? Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard(i % 3 == 0 ? "standard:crossbow" : i % 3 == 1 ? "classic:borrowed-sword" : "standard:nullification", Suit.Spade, 7)).ToArray()
                : Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("classic:silver-lion", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "固定取得与赠牌边界", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:actual-gain-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:actual-gain-owner", "fixture:actual-gain-peer-1", "fixture:actual-gain-peer-2", "fixture:actual-gain-peer-3"]));
        }
    }
}
