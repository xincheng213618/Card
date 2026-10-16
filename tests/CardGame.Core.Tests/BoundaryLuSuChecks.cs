using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryLuSuChecks
{
    private const string Haoshi = "boundary:haoshi-current", Dimeng = "boundary:dimeng-current", Driver = "fixture:ls-driver";
    private const string Extra = "fixture:ls-extra", Gift = "fixture:ls-gift", Exchange = "fixture:ls-exchange", Debt = "fixture:ls-debt", Support = "fixture:ls-support";
    private const string Gain = "fixture:ls-gain", Hp = "fixture:ls-hp", Mode = "identity:classic-lu-su-fixture";
    private const string InitialDimeng = "fixture:ls-initial-dimeng", PaidSuppressor = "fixture:ls-paid-suppressor";
    private const string DrawReason = "skill-program." + Haoshi + ".DrawExtraAndArmHalfHandSupport";
    private const string GiftReason = "skill-program." + Haoshi + ".GiveHalfHandAndIssueTargetSupport";

    // Staged real-command drafts only: no compilation, loader or test execution.
    public static void ExtraDrawAndHalfGiftOwnTheirGainChildren()
    {
        var (g, r) = Create(observers: true); Reach(g, IsExtra); ActivateExtra(g); Reach(g, p => p.SkillPrompt?.SkillId == Extra);
        var draw = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.HalfHandDraw is not null);
        var drawId = draw.Id; var receipt = draw.HalfHandDraw!;
        var extraWindow = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Batch.ParentFrameId == draw.Id);
        Require(receipt.RequestedCount == 2 && receipt.ActualCount == 2 && Moves(g, DrawReason).Length == 2 &&
            extraWindow.Batch.Movements.Count == 1 && (extraWindow.Batch.AwaitingProgramFrameId is null || extraWindow.Batch.AwaitingProgramFrameId == draw.Id) &&
            extraWindow.ResumeProgramFrameId is null && V(g, 0).HandCount == 6 && !F<HalfHandTargetSupportIssuedEvent>(g).Any(),
            "Real extra Draw2 is paid in two inherited atomic batches; its first gain child retains the exact draw owner before normal draw or recipient issuance.");
        g = RestoreAfterCold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Haoshi && p.Choices.Any(c => c.Targets.Count == 1));
        Require(V(g, 0).HandCount == 8 && P(g)!.Choices.Where(c => c.Targets.Count == 1).All(c => V(g, c.Targets[0]).HandCount == 4),
            "Only after both extra and normal draw children does the least-hand target query see the real current hands.");
        Answer(g, c => c.Targets.SequenceEqual([1])); Reach(g, IsOwned); Private(g, 0); Reject(g);
        var handBefore = V(g, 0).Hand.Select(c => c.Id).Order().ToArray(); var ids = P(g)!.Choices.Where(c => c.Cards.Count == 1).Take(4).Select(c => c.Cards[0]).ToArray();
        var before = g.CardMovements.Count;
        for (var i = 0; i < 3; i++) Answer(g, c => c.Cards.SequenceEqual([ids[i]]));
        Require(g.CardMovements.Count == before && !F<HalfHandTargetSupportIssuedEvent>(g).Any(), "Three private choices do not pay or issue a partial half-hand recipient.");
        g = RestoreAfterCold(g, r); Answer(g, c => c.Cards.SequenceEqual([ids[3]])); Reach(g, p => p.SkillPrompt?.SkillId == Gift);
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.HalfHandGift is not null); var rootId = root.Id;
        var gift = root.HalfHandGift!; var moved = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Batch.ParentFrameId == root.Id);
        Require(root.InstructionIndex == 3 && root.PendingMovementContinuation is { SubjectSeat: 0, BeforeCount: 0 } && gift.DrawProgramFrameId == drawId &&
            gift.HandCountBefore == 8 && gift.CardIds.SequenceEqual(ids) && moved.Batch.Movements.Count == 4 && moved.ResumeProgramFrameId is null &&
            moved.Batch.AwaitingProgramFrameId == root.Id && Moves(g, GiftReason).All(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(1)) &&
            F<HalfHandTargetSupportIssuedEvent>(g).Single().ActualDeliveredCount == 4 && V(g, 0).HandCount == 4 && V(g, 1).HandCount == 8,
            "All four frozen entities move atomically to the original recipient and issue one scalar qualification before their exact movement children return.");
        Frozen(gift); PrivateGift(g, ids); g = RestoreAfterCold(g, r); Frozen(g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == rootId).HalfHandGift!);
        Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Gain); g = RestoreAfterCold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Hp);
        Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == rootId && f.HalfHandGift is not null) && Moves(g, GiftReason).Length == 4,
            "The actual recipient gain→Recovery→HP observer remains beneath the paid half-hand root without reopening selection or re-paying.");
        g = RestoreAfterCold(g, r); Continue(g); Play(g);
        Require(V(g, 0).Hand.Select(c => c.Id).Order().SequenceEqual(handBefore.Except(ids)) &&
            F<HalfHandTargetSupportIssuedEvent>(g).Count(e => e.ProgramFrameId == rootId) == 1 && Moves(g, GiftReason).Length == 4,
            "Cold continuation completes the same whole gift once and returns the original owner's play."); Cold(g, r);
    }

    private static (GameEngine, ContentRegistry) Create(bool observers = false, bool removePaidSource = false, bool supportDamage = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(), new Fixture(observers, removePaidSource, supportDamage));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(g, new SelectGeneralCommand(0, "fixture:ls-owner", g.Revision, P(g)!.PromptId)); return (g, r);
    }
    private static void ActivateExtra(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("binding-id") == "extra-draw");
    private static bool IsExtra(PendingDecision p) => p.SkillPrompt?.SkillId == Haoshi && p.Choices.Any(c => c.Parameters.GetValueOrDefault("binding-id") == "extra-draw");
    private static bool IsOwned(PendingDecision p) => Action(p, "select-owned-cards");
    private static bool Action(PendingDecision p, string id) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == id);
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static T[] F<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static CardMovementRecord[] Moves(GameEngine g, string reason) => g.CardMovements.Where(m => m.Reason.Value == reason).ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Play(GameEngine g) => Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    { for (var i = 0; i < 220; i++) { var p = P(g); if (p is not null && predicate(p)) return; Advance(g); } throw new InvalidOperationException("Fixed Lu Su boundary absent: " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack, Recent = g.Events.TakeLast(4).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is not null && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
        else if (p is not null && IsOwned(p)) Answer(g, c => c.Cards.Count == 1);
        else if (p is not null && Action(p, "half-hand-support")) Answer(g, c => c.Parameters.GetValueOrDefault("support-option") == "pass");
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is not null && Action(p, "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }) Answer(g, c => c.Cards.Count == 0);
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack),
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine RestoreAfterCold(GameEngine g, ContentRegistry r)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Require(State(g) == State(restored), "All four prepared views, private choices, frozen receipts, typed parents and real ledger cold-restore identically."); return restored; }
    private static void Cold(GameEngine g, ContentRegistry r) => _ = RestoreAfterCold(g, r);
    private static void Private(GameEngine g, int chooser)
    {
        var decision = g.CreateSnapshot(chooser).PendingDecision!;
        Require(decision is { IsPrivate: true }, "The actual chooser owns the private selection.");
        ImmutableList(decision.Choices); foreach (var choice in decision.Choices) { ImmutableList(choice.Cards); ImmutableList(choice.Targets); }
        foreach (var seat in Enumerable.Range(0, 4).Where(s => s != chooser)) Require(g.CreateSnapshot(seat).PendingDecision is null, "Foreign views expose no private options, material IDs or selected entities.");
    }
    private static void PrivateGift(GameEngine g, int[] ids)
    { foreach (var seat in new[] { 2, 3 }) Require(g.CreateSnapshot(seat).Players[1].Hand.Count == 0 && g.CreateSnapshot(seat).Players[0].Hand.Count == 0, "Uninvolved prepared views expose only public hand counts after an actual hidden gift."); }
    private static void Reject(GameEngine g) { var before = State(g); var p = P(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && State(g) == before, "An unpublished private choice cannot pay or mutate the owning draft."); }
    private static void Frozen(ProgramHalfHandGiftPayment value)
    { Immutable(value.CardIds); var ids = value.CardIds.ToArray(); var copy = value with { CardIds = ids }; if (ids.Length > 0) ids[0] = -1; Require(copy.CardIds.SequenceEqual(value.CardIds), "with clones the half-hand invoice."); Immutable(JsonSerializer.Deserialize<ProgramHalfHandGiftPayment>(JsonSerializer.Serialize(value))!.CardIds); }
    private static void Frozen(ProgramPhaseHandExchangeReceipt value)
    { Immutable(value.FirstCardIds); Immutable(value.SecondCardIds); var first = value.FirstCardIds.ToArray(); var copy = value with { FirstCardIds = first }; if (first.Length > 0) first[0] = -1; Require(copy.FirstCardIds.SequenceEqual(value.FirstCardIds), "with clones the exact original exchange invoice."); var json = JsonSerializer.Deserialize<ProgramPhaseHandExchangeReceipt>(JsonSerializer.Serialize(value))!; Immutable(json.FirstCardIds); Immutable(json.SecondCardIds); }
    private static void Immutable(IReadOnlyList<int> values)
    { if (values.Count == 0) return; if (values is IList<int> list) { var rejected = false; try { list[0] = -1; } catch (NotSupportedException) { rejected = true; } Require(rejected, "Prepared/checkpoint receipt collections cannot be mutated through IList."); } else Require(values is not int[], "No mutable array is exposed."); }
    private static void ImmutableList<T>(IReadOnlyList<T> values)
    { if (values.Count == 0) return; if (values is IList<T> list) { var rejected = false; try { list[0] = values[0]; } catch (NotSupportedException) { rejected = true; } Require(rejected, "Prepared public or private choice collections cannot be mutated through IList."); } else Require(values is not T[], "Prepared choices expose no mutable array."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool observers, bool removePaidSource, bool supportDamage) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:lu-su", "1.0.0", "界鲁肃公共能力真实命令草稿");
        public void Register(IContentRegistryBuilder b)
        {
            var rules = JsonNode.Parse("""
                {"schemaVersion":0,"skills":[
                  {"id":"fixture:ls-driver","revision":1,"activations":[
                    {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipped"}]},
                    {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                    {"id":"clear","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"selectedTarget","numberExpression":"allOwnedZoneCards","zones":["hand","equipment"],"resultBind":"clear"},{"op":"moveBoundCards","target":"owner","sourceBind":"clear","destination":"discardPile","awaitMovementTriggers":true},{"op":"awaitBoundCardMovements","target":"owner"}]},
                    {"id":"draw","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":1}]},
                    {"id":"search","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"declareDeckCriterionAndGiveMatchingCard","target":"owner"}]},
                    {"id":"request","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"asked"}]},
                    {"id":"remove","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["boundary:dimeng-current"],"sourceBind":"fixture:ls-noop"}]}]},
                  {"id":"fixture:ls-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
                  {"id":"fixture:ls-extra","revision":1,"triggers":[{"id":"extra","window":"cardsGained","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:haoshi-current.DrawExtraAndArmHalfHandSupport"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                  {"id":"fixture:ls-gift","revision":1,"triggers":[{"id":"gift","window":"cardsMoved","subject":"owner","optional":false,"priority":100,"sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:haoshi-current.GiveHalfHandAndIssueTargetSupport"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                  {"id":"fixture:ls-exchange","revision":1,"triggers":[{"id":"exchange","window":"cardsMoved","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:dimeng-current.exchange"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                  {"id":"fixture:ls-debt","revision":1,"triggers":[{"id":"debt","window":"cardsMoved","subject":"owner","optional":false,"sourceZones":["hand","equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["skill-program.boundary:dimeng-current.MoveBoundCards"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                  {"id":"fixture:ls-support","revision":1,"triggers":[{"id":"support","window":"cardsGained","subject":"owner","optional":false,"priority":100,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:haoshi-current.OfferHalfHandRecipientSupport"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                  {"id":"fixture:ls-gain","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:haoshi-current.GiveHalfHandAndIssueTargetSupport","skill-program.boundary:haoshi-current.OfferHalfHandRecipientSupport"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]},{"op":"recover","target":"owner","amount":1}]}]},
                  {"id":"fixture:ls-hp","revision":1,"triggers":[{"id":"hp","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}
                ]}
                """)!;
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            ((JsonArray)rules["skills"]!).Add(JsonNode.Parse("""
                {"id":"fixture:ls-support-damage","revision":1,"triggers":[{"id":"paid-gain-damage","window":"cardsGained","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:haoshi-current.OfferHalfHandRecipientSupport"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]},{"op":"damage","target":"owner","amount":1}]}]}
                """));
            ((JsonArray)rules["skills"]!).Add(JsonNode.Parse("""
                {"id":"fixture:ls-source-pulse","revision":1,"triggers":[{"id":"generic-damage-dying-return","window":"selfDyingResponse","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]},{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":3,"clampToMaxHp":true}]}]}
                """));
            if (supportDamage) ((JsonArray)rules["skills"]!).Single(n => n!["id"]!.GetValue<string>() == Gain)!["triggers"]![0]!["movementReasons"] =
                JsonNode.Parse("""["skill-program.boundary:haoshi-current.GiveHalfHandAndIssueTargetSupport"]""");
            if (removePaidSource)
            {
                ((JsonArray)rules["skills"]!).Add(JsonNode.Parse($$"""{"id":"{{InitialDimeng}}","revision":1,"triggers":[{"id":"acquire-dimeng","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"grantSkills","target":"owner","skillIds":["{{Dimeng}}"]}]}]}"""));
                ((JsonArray)((JsonArray)rules["skills"]!).Single(n => n!["id"]!.GetValue<string>() == Hp)!["triggers"]![0]!["effects"]!).Add(
                    JsonNode.Parse($$"""{"op":"grantSkills","target":"owner","skillIds":["{{PaidSuppressor}}"]}"""));
            }
            var presentation = JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
                { [Driver] = new { name = "真实命令", description = "小固定夹具" }, ["fixture:ls-quiet"] = new { name = "安静回合", description = "实际跳过出牌" },
                  [Extra] = Pause("额外摸牌孩子"), [Gift] = Pause("半手付款孩子"), [Exchange] = Pause("实际换手孩子"), [Debt] = Pause("Ending成本孩子"),
                  [Support] = Pause("受赠者援助孩子"), [Gain] = Pause("真实得牌"), [Hp] = Pause("真实回复"),
                  ["fixture:ls-support-damage"] = Pause("已付援助得牌伤害"), ["fixture:ls-source-pulse"] = Pause("真实伤害濒死返回") } });
            if (removePaidSource)
            {
                var labels = JsonNode.Parse(presentation)!;
                labels["skills"]![InitialDimeng] = JsonSerializer.SerializeToNode(new { name = "真实取得缔盟", description = "游戏开始由owner实际取得唯一缔盟实例" });
                presentation = labels.ToJsonString();
            }
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), presentation);
            foreach (var id in new[] { Driver, "fixture:ls-quiet", Extra, Gift, Exchange, Debt, Support, Gain, Hp, "fixture:ls-support-damage", "fixture:ls-source-pulse" }) b.AddSkill(new(id, id, "真实程序夹具") { Program = catalog.Programs[id] });
            if (removePaidSource)
            {
                b.AddSkill(new(InitialDimeng, "真实取得缔盟", "仅付款失源夹具") { Program = catalog.Programs[InitialDimeng] });
                b.AddSkill(new(PaidSuppressor, "真实回复HP2资格抑制", "原生白银狮子回复后的owner资格") { SuppressionRule = new(2) });
            }
            b.AddSkill(new("fixture:ls-noop", "已替换来源", "无运行能力"));
            b.AddSkill(new("fixture:ls-selection", "固定选将", "无运行能力") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            var owner = new List<string> { removePaidSource ? InitialDimeng : Dimeng, Driver }; if (observers) owner.AddRange([Extra, Gift, Exchange, Debt, Support, Gain, Hp]);
            if (supportDamage) owner.AddRange(["fixture:ls-support-damage", "fixture:ls-source-pulse"]);
            b.AddGeneral(new("fixture:ls-owner", "界鲁肃机制", "supporter", Haoshi, "wu", 3, owner, Gender: GeneralGender.Male) { InitialHp = 1 });
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:ls-target-{i}", "固定目标", "supporter", "fixture:ls-selection", "wu", 6,
                observers ? ["fixture:ls-quiet", Exchange, Gain, Hp] : ["fixture:ls-quiet"], Gender: GeneralGender.Male) { InitialHp = 5 });
            b.AddDeck(new("fixture:ls-deck", "固定合法实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 60).Select(i =>
                new ContentDeckPhysicalCard(i % 3 == 0 ? "classic:silver-lion" : i % 3 == 1 ? "standard:draw_two" : "standard:slash", i % 3 == 0 ? Suit.Spade : Suit.Heart, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "真实界鲁肃", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:ls-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:ls-owner", "fixture:ls-target-1", "fixture:ls-target-2", "fixture:ls-target-3"]));
        }
        private static object Pause(string name) => new { name, description = "真实子结算暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
    }
}
