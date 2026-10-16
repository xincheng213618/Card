using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

// Static command drafts. This batch deliberately has not loaded or run them.
internal static class BoundaryBuLianShiChecks
{
    private const string Anxu = "boundary:anxu-current", Zhuiyi = "boundary:zhuiyi-current";
    private const string Driver = "fixture:bls-driver", Gain = "fixture:bls-gain", Gift = "fixture:bls-gift", Hp = "fixture:bls-hp", Reward = "fixture:bls-reward";
    private const string NestedGain = "fixture:bls-nested-gain", NestedHp = "fixture:bls-nested-hp", NestedEntry = "fixture:bls-nested-entry", NestedRescue = "fixture:bls-nested-rescue", NestedCommitted = "fixture:bls-nested-committed";
    private const string Mode = "identity:classic-bu-lian-shi-fixture";
    private const string ObtainReason = "skill-program." + Anxu + ".ObtainOneFromEachSelectedTarget";
    private const string GiftReason = "skill-program." + Anxu + ".GiveShownCardToLeastOriginalTarget";

    public static void OrderedOpaquePairPaysBeforePublicGiftAndReward()
    {
        foreach (var suit in new[] { Suit.Spade, Suit.Heart })
        {
            var (g, r) = Create(suit: suit); Play(g);
            var firstBefore = V(g, 1).HandCount; var secondBefore = V(g, 2).HandCount;
            AnxuPair(g, 1, 2); Reach(g, p => Action(p, "pair-obtain"));
            var rootId = PairRoot(g).Id;
            Require(P(g)!.TargetSeat == 1 && P(g)!.ValidCardIds.Count == 0 && P(g)!.Choices.All(c => c.Cards.Count == 0 && c.Label.Contains("手牌", StringComparison.Ordinal)),
                "The original first foreign hand publishes opaque slots, without identities or a filtered card name.");
            Private(g, 0); Reject(g); g = RestoreAfterCold(g, r);
            Answer(g, c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
            Reach(g, p => p.SkillPrompt?.SkillId == Gain && p.PlayerSeat == 0);
            var root = PairRoot(g); var paid = root.PairObtain!.FirstPayment!;
            Require(root.Id == rootId && root.PairObtain is { Cursor: 0, AwaitingMovement: true, SecondPayment: null } &&
                paid.From == CardLocation.Hand(1) && paid.Delivered && !paid.SameHand && Moves(g, ObtainReason) is [var first] && first.CardId == paid.CardId &&
                first.To == CardLocation.Hand(0) && V(g, 1).HandCount == firstBefore - 1 && V(g, 2).HandCount == secondBefore &&
                g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(w => w.Batch.ParentFrameId == rootId &&
                    (w.Batch.AwaitingProgramFrameId is null || w.Batch.AwaitingProgramFrameId == rootId)),
                "One real first obtain owns the gain child before the second original slot is selected; no future target has paid.");
            g = RestoreAfterCold(g, r); Continue(g); Reach(g, p => Action(p, "pair-obtain"));
            Require(P(g)!.TargetSeat == 2 && PairRoot(g).PairObtain is { Cursor: 1, AwaitingMovement: false }, "The exact ordered cursor advances only after the first gain children return.");
            Private(g, 0); g = RestoreAfterCold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
            Reach(g, p => Action(p, "select-owned-cards"));
            var shown = P(g)!.Choices.First(c => c.Cards.Count == 1).Cards.Single();
            Private(g, 0); g = RestoreAfterCold(g, r); Answer(g, c => c.Cards.SequenceEqual([shown])); Reach(g, p => Action(p, "shown-pair-gift"));
            root = PairRoot(g); var gift = root.ShownPairGift!;
            Require(root.PairObtain is { Cursor: 2, AwaitingMovement: false } && Moves(g, ObtainReason).Length == 2 && gift.CardId == shown && gift.FrozenSuit == suit &&
                gift.FrozenFirstHandCount == V(g, 1).HandCount && gift.FrozenSecondHandCount == V(g, 2).HandCount &&
                P(g)!.Choices.All(c => c.Targets is [1] or [2]) &&
                root.CardSetBindings.Single(b => b.Name == "shown-gift").Visibility == SkillProgramCardSetVisibility.Public &&
                F<ProgramCardsRevealedEvent>(g).Any(e => e.FrameId == rootId),
                "The public single-card reveal freezes the effective suit and real post-gain hand counts of the original pair.");
            g = RestoreAfterCold(g, r); Answer(g, c => c.Targets.SequenceEqual([1])); Reach(g, p => p.SkillPrompt?.SkillId == Gift && p.PlayerSeat == 1);
            Require(Moves(g, GiftReason) is [var delivered] && delivered.CardId == shown && delivered.From == CardLocation.Hand(0) && delivered.To == CardLocation.Hand(1) &&
                !F<ShownPairGiftRewardIssuedEvent>(g).Any() && PairRoot(g).ShownPairGift is { Stage: ProgramShownPairGiftStage.AwaitingGift, Delivered: true },
                "The shown entity really reaches the original least-hand recipient before any non-Spade reward.");
            g = RestoreAfterCold(g, r); Continue(g);
            if (suit != Suit.Spade)
            {
                Reach(g, p => p.SkillPrompt?.SkillId == Reward && p.PlayerSeat == 0);
                Require(F<ShownPairGiftRewardIssuedEvent>(g).Single() is { RequestedCount: 1, ActualCount: 1 } && PairRoot(g).ShownPairGift is { Stage: ProgramShownPairGiftStage.AwaitingReward },
                    "The actual one-card reward owns its own gain child after the gift, using the frozen revealed suit.");
                g = RestoreAfterCold(g, r); Continue(g);
            }
            Play(g);
            Require(Moves(g, ObtainReason).Length == 2 && Moves(g, GiftReason).Length == 1 && F<ShownPairGiftCommittedEvent>(g).Length == 1 &&
                F<ShownPairGiftRewardIssuedEvent>(g).Length == (suit == Suit.Spade ? 0 : 1) && !g.GetHumanLegalActions().Any(a => a.SkillId == Anxu) &&
                !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.PairObtain is not null),
                "Both real payments, the shown gift and its optional reward complete once; the actual play-phase usage remains consumed."); Cold(g, r);
        }
    }

    private static (GameEngine, ContentRegistry) Create(Suit suit = Suit.Spade, bool equipment = false, bool deathOwner = false, bool removePaidSource = false, string? deathNesting = null)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(suit, equipment, deathOwner, removePaidSource, deathNesting));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = deathOwner ? Role.Renegade : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.SelectGeneral);
        Require(P(g)!.ValidContentIds.Contains("fixture:bls-owner"),
            "The fixed published general selection actually retains the owner.");
        Accept(g, new SelectGeneralCommand(0, "fixture:bls-owner", g.Revision, P(g)!.PromptId));
        if (removePaidSource)
        {
            Play(g);
            Require(!V(g, 0).Skills!.Any(s => s.Id == Anxu), "The source-loss fixture does not print the skill whose acquired qualification will be suppressed.");
            Use(g, "grant-anxu", []); Play(g);
            Require(V(g, 0).Skills!.Any(s => s.Id == Anxu) &&
                V(g, 0).SkillRuntimeStates!.Single(s => s.SkillId == Anxu).IsAcquired &&
                F<SkillsAcquiredEvent>(g).Count(e => e.PlayerSeat == 0 && e.SourceSkillId == Driver && e.SkillIds.Contains(Anxu)) == 1,
                "One actual activation independently grants Anxu before its real pair payment and later qualification suppression.");
        }
        return (g, r);
    }
    private static void AnxuPair(GameEngine g, int a, int b) => Accept(g, new UseProgramSkillCommand(0, Anxu, "pair-obtain-show-give", [], [a, b], g.Revision, P(g)!.PromptId));
    private static ProgramSkillFrame PairRoot(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.PairObtain is not null);
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static T[] F<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static CardMovementRecord[] Moves(GameEngine g, string reason) => g.CardMovements.Where(m => m.Reason.Value == reason).ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Action(PendingDecision p, string id) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == id);
    private static void Use(GameEngine g, string id, IReadOnlyList<int> targets) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets, g.Revision, P(g)!.PromptId));
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Continue(GameEngine g)
    { var p = P(g)!; Require(p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"), "The actual child publishes its continue option.");
        if (p.PlayerSeat == 0) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
    private static void Play(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> expected) => Until(g, engine => P(engine) is { } p && expected(p));
    private static void Until(GameEngine g, Func<GameEngine, bool> expected)
    { for (var i = 0; i < 240; i++) { if (expected(g)) return; Advance(g); }
        throw new InvalidOperationException("Fixed Bu Lian Shi command boundary absent: " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack, Events = g.Events.TakeLast(5).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0 } && p.SkillPrompt?.SkillId is Gain or Gift or Hp or Reward) Continue(g);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0 } && Action(p, "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die")) Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected actual command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack),
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static void Cold(GameEngine g, ContentRegistry r) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)),
        "All four prepared views, exact typed frames, scalar facts, real movement ledger and accepted-command history cold-restore identically.");
    private static GameEngine RestoreAfterCold(GameEngine g, ContentRegistry r)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Require(State(g) == State(restored), "Cold JSON restoration preserves the paid parent and resumes this actual restored instance."); return restored; }
    private static void Reject(GameEngine g) { var before = State(g); var p = P(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("unpublished"), g.Revision)).Accepted && State(g) == before, "An unpublished private answer cannot pay or alter the original cursor."); }
    private static void Private(GameEngine g, int chooser)
    { foreach (var seat in Enumerable.Range(0, 4).Where(s => s != chooser)) Require(g.CreateSnapshot(seat).PendingDecision is null, "Only the original chooser sees its private card choices or opaque slots.");
        var p = g.CreateSnapshot(chooser).PendingDecision!; Require(p.Choices is System.Collections.IList { IsReadOnly: true } && p.ValidCardIds is System.Collections.IList { IsReadOnly: true } &&
            p.Choices.All(c => c.Cards is System.Collections.IList { IsReadOnly: true } && c.Targets is System.Collections.IList { IsReadOnly: true }), "Prepared outer and nested choice lists expose only frozen collections."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(Suit suit, bool equipment, bool deathOwner, bool removePaidSource, string? deathNesting) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:boundary-bu-lian-shi", "1.0.0", "当前OL机制真实命令草稿");
        public void Register(IContentRegistryBuilder b)
        {
            var rules = JsonNode.Parse("""
                {"schemaVersion":0,"skills":[
                  {"id":"fixture:bls-driver","revision":1,"activations":[
                    {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipped"}]},
                    {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]}]},
                  {"id":"fixture:bls-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
                  {"id":"fixture:bls-gain","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","optional":false,"usageScope":"turn","usageLimit":1,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:anxu-current.ObtainOneFromEachSelectedTarget","skill-program.boundary:zhuiyi-current.Draw"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                  {"id":"fixture:bls-gift","revision":1,"triggers":[{"id":"gift","window":"cardsGained","subject":"owner","optional":false,"usageScope":"turn","usageLimit":1,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:anxu-current.GiveShownCardToLeastOriginalTarget"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                  {"id":"fixture:bls-hp","revision":1,"triggers":[{"id":"hp","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                  {"id":"fixture:bls-reward","revision":1,"triggers":[{"id":"reward","window":"cardsGained","subject":"owner","optional":false,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:anxu-current.GiveShownCardToLeastOriginalTarget.reward"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}
                ]}
                """)!;
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            if (removePaidSource)
            {
                ((JsonArray)rules["skills"]![0]!["activations"]!).Add(JsonNode.Parse("""{"id":"grant-anxu","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"grantSkills","target":"owner","skillIds":["boundary:anxu-current"]}]}"""));
                ((JsonArray)rules["skills"]![2]!["triggers"]![0]!["effects"]!).Add(JsonNode.Parse("""{"op":"grantSkills","target":"owner","skillIds":["fixture:bls-noop"]}"""));
            }

            if (deathNesting is not null)
            {
                ((JsonArray)rules["skills"]![0]!["activations"]!).Add(JsonNode.Parse("""{"id":"kill-damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}"""));
                // This is a real first-Ending HP observer's skill grant. The new
                // candidates are absent from that already-frozen HP window.
                ((JsonArray)rules["skills"]![4]!["triggers"]![0]!["effects"]!).Add(JsonSerializer.SerializeToNode(new
                    { op = "grantSkills", target = "owner", skillIds = new[] { deathNesting == "damage" ? NestedGain : NestedHp } }));
                var nested = JsonNode.Parse("""
                    [
                     {"id":"fixture:bls-nested-gain","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","optional":false,"priority":20,"usageScope":"game","usageLimit":1,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:zhuiyi-current.Draw"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"nested-seen","options":[{"id":"continue"}]},{"op":"grantSkills","target":"owner","skillIds":["fixture:bls-nested-entry"]},{"op":"damage","target":"owner","amount":1}]}]},
                     {"id":"fixture:bls-nested-hp","revision":1,"triggers":[{"id":"recovered","window":"afterHpRecovered","subject":"owner","optional":false,"priority":20,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"nested-seen","options":[{"id":"continue"}]},{"op":"grantSkills","target":"owner","skillIds":["fixture:bls-nested-entry","fixture:bls-nested-rescue","fixture:bls-nested-committed"]},{"op":"loseHp","target":"owner","amount":2}]}]},
                     {"id":"fixture:bls-nested-entry","revision":1,"triggers":[{"id":"entering","window":"dyingEntering","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"entry-seen","options":[{"id":"continue"}]}]}]},
                     {"id":"fixture:bls-nested-rescue","revision":1,"viewAs":[{"id":"two-spades","inputKinds":["slash"],"inputSuits":["spade"],"outputKind":"peach","forPlay":false,"forResponse":true,"inputCount":2,"sameSuit":true,"sourceZones":["hand","equipment"],"extendedUse":true}]},
                     {"id":"fixture:bls-nested-committed","revision":1,"triggers":[{"id":"committed","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["peach"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"rescue-seen","options":[{"id":"continue"}]}]}]}
                    ]
                    """)!.AsArray();
                if (deathNesting == "damage")
                    ((JsonArray)nested[2]!["triggers"]![0]!["effects"]!).Add(JsonNode.Parse("""{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":3,"clampToMaxHp":true}"""));
                foreach (var definition in nested.ToArray()) ((JsonArray)rules["skills"]!).Add(definition!.DeepClone());
            }

            var presentation = JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
                { [Driver] = new { name = "真实命令", description = "固定实体与真实HP" }, ["fixture:bls-quiet"] = new { name = "安静回合", description = "真实跳过出牌" },
                  [Gain] = Pause("真实获得"), [Gift] = Pause("展示后赠牌"), [Hp] = Pause("真实回复"), [Reward] = Pause("非黑桃奖励") } });
            if (deathNesting is not null)
            {
                var p = JsonNode.Parse(presentation)!;
                foreach (var id in new[] { NestedGain, NestedHp, NestedEntry, NestedCommitted }) p["skills"]![id] = JsonSerializer.SerializeToNode(Pause("死亡收益实际子链"));
                p["skills"]![NestedRescue] = JsonSerializer.SerializeToNode(new { name = "两实体真实救援", description = "全黑实体真实转换桃" });
                presentation = p.ToJsonString();
            }

            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), presentation);
            foreach (var id in new[] { Driver, "fixture:bls-quiet", Gain, Gift, Hp, Reward }) b.AddSkill(new(id, id, "真实程序夹具") { Program = catalog.Programs[id] });
            if (deathNesting is not null) foreach (var id in new[] { NestedGain, NestedHp, NestedEntry, NestedRescue, NestedCommitted }) b.AddSkill(new(id, id, "死亡收益实际子链") { Program = catalog.Programs[id] });
            b.AddSkill(new("fixture:bls-noop", "已付来源资格抑制", "真实Lord的2HP使原来源失去资格") { SuppressionRule = new(2), Tags = SkillTag.Locked });
            b.AddSkill(new("fixture:bls-selection", "固定选将", "无运行能力") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            b.AddGeneral(new("fixture:bls-owner", "界步练师机制", "supporter", removePaidSource ? "fixture:bls-selection" : Anxu, "wu", 3, [Zhuiyi, Driver, Gain, Gift, Hp, Reward], Gender: GeneralGender.Female) { InitialHp = 1 });
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:bls-target-{i}", "固定受益者", "supporter", "fixture:bls-selection", "qun", 6,
                ["fixture:bls-quiet", Gain, Gift, Hp], Gender: GeneralGender.Male) { InitialHp = 5 });
            b.AddDeck(new("fixture:bls-deck", "固定合法实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 64).Select(i =>
                new ContentDeckPhysicalCard(equipment ? "classic:silver-lion" : "standard:slash", suit, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "真实界步练师", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:bls-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:bls-owner", "fixture:bls-target-1", "fixture:bls-target-2", "fixture:bls-target-3"]));
        }
        private static object Pause(string name) => new { name, description = "真实子结算暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
    }
}
