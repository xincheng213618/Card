using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

// Real commands exercise the shared conversion and its native child returns.
internal static class BoundaryGuoHuangHouChecks
{
    private const string Jiaozhao = "boundary:jiaozhao-round-current", Danxin = "boundary:danxin-capped-current";
    private const string Driver = "fixture:ghh-driver", Gain = "fixture:ghh-gain", Hp = "fixture:ghh-hp", Completed = "fixture:ghh-completed";
    private const string TrickChild = "fixture:ghh-trick-child", FireDamage = "fixture:ghh-fire-damage", FireColor = "fixture:ghh-fire-color";
    private const string StateId = "boundary-jiaozhao-round-tier", Mode = "identity:classic-guo-huang-hou-fixture";
    private const string DrawReason = "skill-program." + Danxin + ".capped-conversion-draw";

    public static void TrueRoundNamesPhysicalConversionAndPrivateCold()
    {
        var (g, r) = Create(); Play(g);
        var action = g.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == Jiaozhao && a.PlayedCardKind == CardKind.DrawTwo);
        var material = action.CardId!.Value; var hand = V(g, 0).HandCount;
        Private(g); g = Cold(g, r); SubmitPlay(g, action); Play(g);
        var receipt = F<TieredRoundConversionUseIssuedEvent>(g).Single().Receipt;
        Require(receipt.FrozenTier == 0 && receipt.MaterialCount == 1 && receipt.RoundNumber == 1 && receipt.Source.OwnerSeat == 0 &&
            F<TrueRoundCardNameUsedEvent>(g).Any(e => e.EffectiveKind == CardKind.DrawTwo && e.ActorSeat == 0) &&
            g.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            V(g, 0).HandCount == hand + 1 && !g.GetHumanLegalActions().Any(a => a.ConversionSource?.SkillId == Jiaozhao),
            "A genuine single material DrawTwo use consumes only the actual Play quota, records the declared output name and preserves one real payment.");
        Use(g, "duel", [1]); Reach(g, p => p.Kind == DecisionKind.RespondSlash && p.PlayerSeat == 0);
        var count = F<TrueRoundCardNameUsedEvent>(g).Length;
        var responseCard = P(g)!.Choices.First(c => c.Cards.Count == 1 && c.Parameters.GetValueOrDefault("response") == "slash").Cards.Single();
        g = Cold(g, r);
        Answer(g, c => c.Cards.Count == 1 && c.Parameters.GetValueOrDefault("response") == "slash");
        Play(g);
        Require(F<TrueRoundCardNameUsedEvent>(g).Length == count && !F<TrueRoundCardNameUsedEvent>(g).Any(e => e.EffectiveKind == CardKind.Slash) &&
            g.CardMovements.Count(m => m.CardId == responseCard && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1,
            "A real entity Slash played in a Duel response does not become an all-player true-use name.");
        _ = Cold(g, r);
        NextOwnRound(g, 2);
        Require(g.GetHumanLegalActions().Any(a => a.ConversionSource?.SkillId == Jiaozhao && a.PlayedCardKind == CardKind.DrawTwo) &&
            F<RoundStartedEvent>(g).Length == 2, "A genuine new Round releases the prior Round name and a new actual Play allowance.");
        _ = Cold(g, r);

        // Bagua/native/conversion Dodge uses share the mature Slash-defense
        // classification. This branch pays a native entity Dodge, not Jiaozhao.
        (g, r) = Create(CardKind.Dodge, foreignSlash: true); Play(g); End(g);
        Reach(g, p => p.Kind == DecisionKind.RespondDodge && p.PlayerSeat == 0);
        var nativeParent = g.ResolutionStack.OfType<CardUseFrame>().Last().Id;
        Answer(g, c => c.Cards.Count == 1 && c.Parameters.GetValueOrDefault("response") == "dodge");
        Reach(g, p => p.SkillPrompt?.SkillId == Completed); g = Cold(g, r); Continue(g);
        Until(g, e => !e.ResolutionStack.Any(f => f.Id == nativeParent));
        Require(F<TrueRoundCardNameUsedEvent>(g).Any(e => e.EffectiveKind == CardKind.Dodge && e.ActorSeat == 0),
            "A different mature native Dodge use is included in the global Round ledger without the new conversion policy.");
        _ = Cold(g, r);
    }

    public static void ZeroCounterspellDodgeAndDyingTypedReturns()
    {
        var (g, r) = Create(CardKind.Dismantlement); Play(g); UpgradeTwo(g);
        SubmitPlay(g, g.GetHumanLegalActions().First(a => a.ConversionSource is null && a.Kind == LegalActionKind.Dismantlement && a.TargetSeats.SequenceEqual([1])));
        Reach(g, p => p.Kind == DecisionKind.Nullification && p.PlayerSeat == 0 && p.Choices.Any(IsZero));
        var n = g.ResolutionStack.OfType<NullificationWindowFrame>().Last(); var chain = n.ChainDepth;
        g = Cold(g, r); Answer(g, c => IsZero(c) && c.Parameters.GetValueOrDefault("output-kind") == nameof(CardKind.Nullification));
        Reach(g, p => p.SkillPrompt?.SkillId == Completed);
        var held = g.ResolutionStack.OfType<NullificationWindowFrame>().Single(w => w.Id == n.Id);
        Require(held.TieredRoundResponseUse is { MaterialCount: 0, EffectiveKind: CardKind.Nullification } receipt && receipt.OwnerFrameId == n.Id &&
            held.ChainDepth == chain + 1 && F<TrueRoundCardNameUsedEvent>(g).Any(e => e.NullificationUse && e.EffectiveKind == CardKind.Nullification),
            "The actual zero Nullification node owns its frozen source/quota and real Response-as-Use chain.");
        Private(g); AuditPausedResponseReceipt(g, r, n.Id); g = Cold(g, r); Continue(g); Play(g);
        Require(F<NullificationResolvedEvent>(g).Single().ChainDepth == chain + 1 && !g.ResolutionStack.OfType<NullificationWindowFrame>().Any() &&
            g.CardMovements.All(m => m.CardId != 0), "Completed response children return once to the original trick without a fictitious payment entity.");
        _ = Cold(g, r);

        (g, r) = Create(CardKind.Dodge, foreignSlash: true); Play(g); UpgradeTwo(g); End(g);
        Reach(g, p => p.Kind == DecisionKind.RespondDodge && p.PlayerSeat == 0 && p.Choices.Any(IsZero));
        var original = g.ResolutionStack.OfType<CardUseFrame>().Last(); g = Cold(g, r); Answer(g, c => IsZero(c) && c.Parameters.GetValueOrDefault("output-kind") == nameof(CardKind.Dodge));
        Reach(g, p => p.SkillPrompt?.SkillId == Completed);
        Require(g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == original.Id).TieredRoundResponseUse is { EffectiveKind: CardKind.Dodge, MaterialCount: 0 } &&
            F<TrueRoundCardNameUsedEvent>(g).Any(e => e.EffectiveKind == CardKind.Dodge && e.ActorSeat == 0),
            "The genuine incoming Slash owns the exact zero Dodge use receipt, not a parallel pending action.");
        AuditPausedResponseReceipt(g, r, original.Id); g = Cold(g, r); Continue(g); Until(g, e => !e.ResolutionStack.Any(f => f.Id == original.Id)); _ = Cold(g, r);

        (g, r) = Create(rescueHpChild: true); Play(g); UpgradeTwo(g);
        var remainingHp = V(g, 0).Hp; Use(g, "die-" + remainingHp); Reach(g, p => p.Kind == DecisionKind.RescueDying && p.PlayerSeat == 0 && p.Choices.Any(IsZero));
        var dying = g.ResolutionStack.OfType<DyingFrame>().Last(); var driver = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Driver);
        Require(dying.ParentFrameId == driver.Id && dying.VictimSeat == 0 && V(g, 0).Hp == 0, "The real LoseHp command enters the exact victim's owning Dying parent.");
        Private(g); g = Cold(g, r); Answer(g, c => IsZero(c) && c.Parameters.GetValueOrDefault("output-kind") == nameof(CardKind.Alcohol));
        Reach(g, p => p.SkillPrompt?.SkillId == Hp);
        var rescue = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.DyingResponse?.ResolutionId == dying.Id);
        Require(rescue.CardId == 0 && rescue.PhysicalCardIds is { Count: 0 } && rescue.Action is { Type: CardActionType.Use, PhysicalCards.Count: 0 } &&
            rescue.TieredRoundConversionUse?.DyingFrameId == dying.Id && rescue.DyingResponse is { UsedAlcohol: true, AlcoholCardId: 0 } &&
            g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(h => h.Change.TargetSeat == 0 && h.Change.ParentFrameId == rescue.Id &&
                h.ResumeFrameId == rescue.Id && h.Continuation == PostEventContinuation.CardUse),
            "A genuine zero Alcohol rescue retains the exact original responder/victim token and pauses in its actual CardUse HP child.");
        g = Cold(g, r); Continue(g); Play(g);
        Require(V(g, 0).Hp == 1 && F<DyingResolvedEvent>(g).Single(e => e.ResolutionId == dying.Id).Survived &&
            F<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == rescue.Id && e.CardId == 0) == 1 &&
            F<TieredRoundConversionUseIssuedEvent>(g).Count(e => e.Receipt.OwnerFrameId == rescue.Id) == 1 && g.CardMovements.All(m => m.CardId != 0),
            "Cold-restored HP returns complete the original Dying and paid Round use once, with no fake entity or duplicate recovery.");
        _ = Cold(g, r);
    }

    private static void AuditPausedResponseReceipt(GameEngine g, ContentRegistry r, long ownerId)
    {
        var owner = g.ResolutionStack.Single(f => f.Id == ownerId);
        var receipt = owner switch { NullificationWindowFrame n => n.TieredRoundResponseUse!, CardUseFrame u => u.TieredRoundResponseUse!, _ => throw new InvalidOperationException("No typed response owner.") };
        foreach (var bad in new[] { receipt with { MaterialCount = receipt.MaterialCount + 1 },
            receipt with { OwnerFrameId = receipt.OwnerFrameId + 1 },
            receipt with { Source = receipt.Source with { BindingId = "unissued-response-binding" } } })
        {
            // A trusted-host invariant audit on a journal-restored clone only.
            // Checkpoints store accepted commands, not pending frame objects.
            // The actual command engine and its original accepted facts stay intact.
            var invalid = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var stack = (FrameStore)typeof(GameEngine).GetField("_resolutionStack", flags)!.GetValue(invalid)!;
            var current = stack.Single(f => f.Id == ownerId);
            stack.Replace(current switch { NullificationWindowFrame n => n with { TieredRoundResponseUse = bad },
                CardUseFrame u => u with { TieredRoundResponseUse = bad }, _ => throw new InvalidOperationException("Response owner changed.") });
            var rejected = false;
            try { typeof(GameEngine).GetMethod("AssertCoreInvariants", flags)!.Invoke(invalid, null); }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is InvalidOperationException { Message: var message } &&
                message.Contains("tiered response", StringComparison.Ordinal)) { rejected = true; }
            Require(rejected, "The paused response invariant rejects altered material/source/owner scalars on the real replay-restored prefix.");
        }
    }

    private static void UpgradeTwo(GameEngine g) { HurtAndActivate(g); Play(g); HurtAndActivate(g); Play(g); Require(Tier(g) == 2, "Two actual damage choices establish tier2."); }
    private static void HurtAndActivate(GameEngine g) { Use(g, "hurt"); Reach(g, p => p.SkillPrompt?.SkillId == Danxin && HasAction(p, "activate")); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); }
    private static int Tier(GameEngine g) => V(g, 0).ConfiguredConversionTiers?.GetValueOrDefault(StateId) ?? 0;
    private static bool IsZero(PromptChoice c) => c.Parameters.GetValueOrDefault("response") == "tiered-round-zero-use";
    private static void SubmitPlay(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId)
        { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void NextOwnRound(GameEngine g, int round) { End(g); Until(g, e => F<RoundStartedEvent>(e).Length >= round && P(e) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }); }
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static T[] F<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool HasAction(PendingDecision p, string id) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == id);
    private static void Play(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate) => Until(g, e => P(e) is { } p && predicate(p));
    private static void Until(GameEngine g, Func<GameEngine, bool> predicate)
    { for (var i = 0; i < 220; i++) { if (predicate(g)) return; Advance(g); } throw new InvalidOperationException("Fixed郭皇后边界 absent: " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack, Facts = g.Events.TakeLast(4).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0 } && p.SkillPrompt?.SkillId is Gain or Hp or Completed or TrickChild or FireDamage) Continue(g);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0 } && HasAction(p, "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.SelectTargetCard }) Answer(g, _ => true);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack),
        Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Require(State(restored) == State(g), "Actual four-view cold reconstruction preserves original frames, private choices, facts, costs and command journal before resumed commands."); return restored; }
    private static void Private(GameEngine g) { var p = P(g)!; foreach (var s in Enumerable.Range(0, 4).Where(s => s != p.PlayerSeat)) Require(g.CreateSnapshot(s).PendingDecision is null, "Only the true chooser receives its private decision.");
        Require(p.Choices is System.Collections.IList { IsReadOnly: true } && p.ValidCardIds is System.Collections.IList { IsReadOnly: true } && p.Choices.All(c => c.Cards is System.Collections.IList { IsReadOnly: true } && c.Targets is System.Collections.IList { IsReadOnly: true }), "Prepared outer/nested choices resist mutation."); }
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Create(CardKind material = CardKind.Slash, bool benefitChildren = false, bool foreignSlash = false, bool rescueHpChild = false, bool trickChild = false, bool benxi = false, bool fireAttackChildren = false, bool colorFireAttack = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(material, benefitChildren, foreignSlash, rescueHpChild, trickChild, benxi, fireAttackChildren, colorFireAttack));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Require(P(g)!.ValidContentIds.Contains("fixture:ghh-owner"), "The fixed four-general published choice contains the actual owner.");
        Accept(g, new SelectGeneralCommand(0, "fixture:ghh-owner", g.Revision, P(g)!.PromptId)); return (g, r);
    }

    private sealed class Fixture(CardKind material, bool benefitChildren, bool foreignSlash, bool rescueHpChild, bool trickChild, bool benxi, bool fireAttackChildren, bool colorFireAttack) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:boundary-guo-huang-hou", "1.0.0", "当前OL矫诏/殚心真实命令草稿");
        public void Register(IContentRegistryBuilder b)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rs = assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.boundary-guo-huang-hou.rules.json") ?? throw new InvalidOperationException("Missing production rules.");
            using var ps = assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.boundary-guo-huang-hou.presentation.json") ?? throw new InvalidOperationException("Missing production presentation.");
            using var rr = new StreamReader(rs); using var pr = new StreamReader(ps);
            var production = SkillProgramCatalog.Load(rr.ReadToEnd(), pr.ReadToEnd());
            foreach (var id in new[] { Jiaozhao, Danxin }) b.AddSkill(new(id, id, "完整当前共享能力") { Program = production.Programs[id] });
            if (benxi)
            {
                using var brs = assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.classic-wu-yi.rules.json") ?? throw new InvalidOperationException("Missing genuine Benxi rules.");
                using var bps = assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.classic-wu-yi.presentation.json") ?? throw new InvalidOperationException("Missing genuine Benxi presentation.");
                using var brr = new StreamReader(brs); using var bpr = new StreamReader(bps);
                var mature = SkillProgramCatalog.Load(brr.ReadToEnd(), bpr.ReadToEnd());
                b.AddSkill(new("classic:benxi", "奔袭", "成熟真实目标修改producer") { Program = mature.Programs["classic:benxi"] });
            }
            var root = JsonNode.Parse("""
            {"skills":[
              {"id":"fixture:ghh-driver","revision":1,"activations":[
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
                {"id":"duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
                {"id":"extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"pendExtraTurn","target":"owner"}]},
                {"id":"remove","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["boundary:jiaozhao-round-current"],"sourceBind":"fixture:ghh-noop"}]},
                {"id":"regain","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"grantSkills","target":"owner","skillIds":["boundary:jiaozhao-round-current"]}]}]},
              {"id":"fixture:ghh-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"fixture:ghh-gain","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","optional":false,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:danxin-capped-current.capped-conversion-draw"],"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]},{"op":"recover","target":"owner","amount":1}]}]},
              {"id":"fixture:ghh-hp","revision":1,"triggers":[{"id":"hp","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:ghh-completed","revision":1,"triggers":[{"id":"response","window":"cardUseCompleted","optional":false,"ownerRelation":"actor","includeResponseUses":true,"cardKinds":["nullification","dodge","fireAttack"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:ghh-trick-child","revision":1,"triggers":[{"id":"committed","window":"cardUseCommitted","optional":false,"ownerRelation":"actor","cardKinds":["drawTwo"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:ghh-fire-damage","revision":1,"triggers":[{"id":"damage","window":"afterDamageApplied","subject":"damageSource","optional":false,"damageOccurrence":"perDamage","effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:ghh-fire-color","revision":1,"cardPolicies":[{"id":"color-payment","kind":"randomRevealColorFireAttack","cardKinds":["fireAttack"]}]},
              {"id":"fixture:ghh-foreign-slash","revision":1,"viewAs":[{"id":"real-slash","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false,"useOnly":true}],"triggers":[{"id":"slash","window":"turnEnding","subject":"owner","turnOwnerScope":"otherLiving","optional":false,"effects":[{"op":"useOwnerSlashAgainstTurnOwner","target":"owner","ignoreDistance":true}]}]}
            ]}
            """)!;
            root["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            var activations = (JsonArray)root["skills"]![0]!["activations"]!;
            for (var amount = 1; amount <= 20; amount++) activations.Add(JsonNode.Parse(JsonSerializer.Serialize(new {
                id = "die-" + amount, minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null,
                effects = new[] { new { op = "loseHp", target = "owner", amount } } })));
            var catalog = SkillProgramCatalog.Load(root.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object> {
                [Driver] = new { name = "实际规则命令", description = "真实伤害、死亡入口、额外回合和来源重新获得" }, ["fixture:ghh-quiet"] = new { name = "安静回合", description = "真实跳过出牌" },
                [Gain] = Pause("真实gain"), [Hp] = Pause("真实HP"), [Completed] = Pause("真实完成响应"), [TrickChild] = Pause("真实锦囊提交"), [FireDamage] = Pause("真实火攻伤害"), [FireColor] = new { name = "同颜色弃置", description = "成熟火攻付款政策" }, ["fixture:ghh-foreign-slash"] = new { name = "真实外部杀", description = "固定真实实体Slash producer" } } }));
            foreach (var id in catalog.Programs.Keys) b.AddSkill(new(id, id, "实际小夹具") { Program = catalog.Programs[id] });
            b.AddSkill(new("fixture:ghh-noop", "旧来源替换", "无运行能力"));
            b.AddSkill(new("fixture:ghh-selection", "固定候选", "无运行能力") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            var skills = new List<string> { Danxin, Driver, Completed };
            if (trickChild) skills.Add(TrickChild);
            if (fireAttackChildren) skills.Add(FireDamage);
            if (colorFireAttack) skills.Add(FireColor);
            if (benxi) skills.Add("classic:benxi");
            if (benefitChildren) { skills.Add(Gain); skills.Add(Hp); } else if (rescueHpChild) skills.Add(Hp);
            b.AddGeneral(new("fixture:ghh-owner", "当前郭皇后机制", "supporter", Jiaozhao, "wei", 8, skills, GeneralGender.Female));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:ghh-target-{i}", "固定原生目标", "supporter", "fixture:ghh-selection", "qun", 8,
                foreignSlash ? ["fixture:ghh-quiet", "fixture:ghh-foreign-slash"] : ["fixture:ghh-quiet"], GeneralGender.Male));
            var materialId = "standard:" + (material == CardKind.Dismantlement ? "dismantlement" : material == CardKind.Dodge ? "dodge" : "slash");
            b.AddDeck(new("fixture:ghh-deck", "固定单实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard(materialId, Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "当前OL共享能力", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:ghh-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:ghh-owner", "fixture:ghh-target-1", "fixture:ghh-target-2", "fixture:ghh-target-3"]));
        }
        private static object Pause(string name) => new { name, description = "真正暂停继续", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
    }
}
