using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OrdinarySpMaChaoChecks
{
    private const string Mode = "identity:classic-sp-ma-chao-fixture", Driver = "fixture:smc-driver";
    private const string Hp = "fixture:smc-hp", Cost = "fixture:smc-cost", Gain = "fixture:smc-gain", Loss = "fixture:smc-loss";
    private const string Zhuiji = "ol:zhuiji", Shichou = "ol:shichou";
    private const string Discard = "skill-program.ol:zhuiji.RequireTargetDiscardOrEquipmentRecast";

    public static void HpOrderedDistanceAndLostHpExtraTargetsPayEachTargetOnce()
    {
        var (g, r) = Create(); Play(g);
        var a = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1, 2]));
        Require(g.State.Players[0].Hp == g.State.Players[0].MaxHp && a.TargetSeats.Count == 2,
            "Full HP still permits base one plus X+1, and the opposite lower-HP seat is truly at directed distance one.");
        var card = SubmitSlash(g, a); Reach(g, Penalty);
        var offered = E<SlashTargetPenaltyOfferedEvent>(g);
        Require(offered.Length == 2 && offered.Select(e => e.Identity.TargetSeat).SequenceEqual([1, 2]) &&
            E<DamageAppliedEvent>(g).Length == 0 && !E<CardActionAcceptedEvent>(g).Any(e => e.Action.Type == CardActionType.Response),
            "All actual distance-one targets are frozen before the first effect or response.");
        Private(g); g = Cold(g, r); Play(g);
        Require(E<SlashTargetPenaltyPaidEvent>(g).Length == 2 && E<SlashTargetPenaltyFinishedEvent>(g).Count(e => e.Completed) == 2 &&
            g.CardMovements.Count(m => m.Reason.Value == Discard) == 2 &&
            g.CardMovements.Count(m => m.CardId == card && m.Reason == CardMoveReasons.Use) == 1 &&
            g.CardMovements.Count(m => m.CardId == card && m.Reason == CardMoveReasons.UseFinished) == 1,
            "Each target pays one original entity once; the real original Slash pays and cleans once after both windows.");
        var (higher, hr) = Create(highHp: true); Play(higher);
        Require(!higher.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.Contains(2)),
            "An opposite strictly higher-HP target retains normal distance and is not made distance one.");
        Use(higher, "hurt"); Play(higher);
        Require(higher.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1, 3])),
            "Lost HP expands the quota without removing normal distance legality."); higher = Cold(higher, hr);
    }

    public static void EquipmentRecastDrainsSilverLionAndPaidSourceLossBeforeRealDraw()
    {
        foreach (var sourceLoss in new[] { false, true })
        {
            var (g, r) = Create(equipment: true, sourceLoss: sourceLoss); Play(g); Use(g, "equip", [1]); Play(g);
            var before = g.State.Players[1].Hp;
            var ids = SelectedSlash(g, [1]); Reach(g, Penalty);
            Require(P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("penalty-option") == "recast"),
                "A real equipped Silver Lion supplies the complete all-equipment branch.");
            g = Cold(g, r); Reach(g, p => p.SkillPrompt?.SkillId == Hp);
            var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SlashTargetPenaltyDraft is not null);
            var draft = root.SlashTargetPenaltyDraft!;
            Require(draft.Recast && draft.Stage == SlashTargetPenaltyStage.CostChildren && draft.PaidCardIds.Count == 1 &&
                g.State.Players[1].Hp == before + 1 && E<SlashTargetPenaltyDrawIssuedEvent>(g).Length == 0 &&
                g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(h => h.ResumeFrameId == root.Id && h.Change.ParentFrameId == root.Id &&
                    h.Continuation == PostEventContinuation.AwaitedProgramMovement),
                "Silver Lion cost recovery is a real exact awaited child before any recast reward.");
            Freeze(draft.PaidCardIds); Freeze(draft.PaidFrom); g = Cold(g, r);
            if (sourceLoss)
            {
                Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Loss && p.PlayerSeat == 0 && p.Choices.Any(c => c.Targets.SequenceEqual([2])));
                Require(g.State.Players[1].Hp == before + 1 && E<SlashTargetPenaltyDrawIssuedEvent>(g).Length == 0 &&
                    E<ProgramSkillHpLostEvent>(g).Any(e => e.SkillId == Hp && e.TargetSeat == 0 && e.Amount == 1),
                    "The paid recovery child causes a real issuer HP loss before its owner chooses a separate wounded victim; the payer remains alive before reward.");
                g = Cold(g, r); Answer(g, c => c.Targets.SequenceEqual([2]));
            }
            Reach(g, p => p.SkillPrompt?.SkillId == Cost);
            var movement = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Last();
            Require(movement.Batch.ParentFrameId == root.Id && (movement.Batch.AwaitingProgramFrameId is null || movement.Batch.AwaitingProgramFrameId == root.Id) &&
                root.PendingMovementContinuation is not null && movement.ResumeProgramFrameId is null &&
                movement.Batch.Movements.Select(m => m.CardId).SequenceEqual(draft.PaidCardIds),
                "The original atomic equipment discard retains its real exact program parent and material IDs.");
            g = Cold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Gain);
            Require(!sourceLoss || E<CharacterSkillsLostEvent>(g).Any(e => e.Seat == 0 && e.SourceSeat == 2 && e.SkillIds.Contains(Zhuiji)) &&
                E<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 0 && e.TargetSeat == 2 && e.Amount == 1) &&
                E<PlayerDiedEvent>(g).Any(e => e.VictimSeat == 2 && e.KillerSeat == 0) && g.State.Players[1].IsAlive &&
                !g.CreateSnapshot(0).Players[0].Skills!.Any(s => s.ContentId == Zhuiji),
                "The real nested victim death invokes Duanchang and disables the original issuer's grants after actual payment; the recast payer survives.");
            Require(E<SlashTargetPenaltyDrawIssuedEvent>(g).Single().ActualCount == 1,
                "Paid recast belongs to the target and draws even after its original attacker's source is lost.");
            g = Cold(g, r); Play(g);
            Require(E<CardRecastEvent>(g).Count(e => draft.PaidCardIds.Contains(e.CardId) && e.DrawCount == 1) == 1 &&
                g.CardMovements.Count(m => draft.PaidCardIds.Contains(m.CardId) && m.Reason == CardMoveReasons.RecastDiscard) == 1 &&
                g.CardMovements.Count(m => m.Reason == CardMoveReasons.RecastDraw && m.To == CardLocation.Hand(1)) == 1 &&
                ids.All(id => g.CardMovements.Count(m => m.CardId == id && m.Reason == CardMoveReasons.UseFinished) == 1) &&
                E<ProgramBindingResolvedEvent>(g).Single(e => e.FrameId == root.Id).Completed,
                "Cost, recovery, movement, real reward and original two-material Slash finish exactly once after cold resumes.");
        }
    }

    public static void SelectedAndSpearMaterialSlashesUseOwnTypedReturnAndFrozenMaximum()
    {
        foreach (var spear in new[] { false, true })
        {
            var (g, r) = Create(spear: spear); Play(g); if (spear) { Use(g, "equip", [0]); Play(g); }
            Use(g, "hurt"); Play(g);
            var cards = g.CreateSnapshot(0).Players[0].Hand.Take(2).Select(c => c.Id).ToArray();
            if (spear)
            {
                var action = g.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.UseEquipmentEffect && a.EquipmentKind == CardKind.ZhangbaSerpentSpear);
                Require(action.MaxTargetCount >= 3, "The mature two-material spear publishes the HP-based target maximum.");
                Accept(g, new UseEquipmentEffectCommand(0, CardKind.ZhangbaSerpentSpear, cards, [1, 2, 3], g.Revision, P(g)!.PromptId));
            }
            else SelectedSlash(g, [1, 2, 3], cards);
            Reach(g, Penalty); var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.HpLossMaterialSlashReturn is not null);
            var issued = use.HpLossMaterialSlashReturn!;
            Require(use.Action is { ActorSeat: 0, ProviderSeat: 0, Type: CardActionType.Use } && use.PhysicalCardIds!.SequenceEqual(cards) &&
                use.TargetSeats.SequenceEqual([1, 2, 3]) && issued.FrozenMaximum >= 3 && issued.IsZhangba == spear &&
                (spear ? issued.ParentProgramFrameId is null : g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == issued.ParentProgramFrameId &&
                    f.HpLossSlashSelection?.CardUseFrameId == use.Id && f.NextActualUseAdjustment is null)),
                "Two real materials create an independent issued return and never manufacture the old adjustment grant.");
            Require(E<DamageAppliedEvent>(g).Length == 0 && E<SlashTargetPenaltyOfferedEvent>(g).All(e => e.Identity.CardUseFrameId == use.Id),
                "All mandatory announced opportunities precede every multi-target effect.");
            g = Cold(g, r); Play(g);
            Require(cards.All(id => g.CardMovements.Count(m => m.CardId == id && m.Reason == CardMoveReasons.Use) == 1 &&
                g.CardMovements.Count(m => m.CardId == id && m.Reason == CardMoveReasons.UseFinished) == 1) &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1 &&
                (spear || E<ProgramSkillResolvedEvent>(g).Count(e => e.FrameId == issued.ParentProgramFrameId && e.Completed) == 1),
                "All three targets return, all physical costs clean once, and the exact selected parent resolves once."); g = Cold(g, r);
        }
    }

    public static void LegacySlashAndExistingTargetWindowsKeepOneVisitPerOriginalTarget()
    {
        foreach (var legacy in new[] { false, true })
        {
            var (g, r) = Create(legacy: legacy, coexist: true);
            if (legacy)
            {
                Reach(g, p => p.SkillPrompt?.SkillId == "boundary:shensu" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
                Reach(g, p => p.SkillPrompt?.SkillId == "boundary:shensu" && p.Choices.Any(c => c.Targets.SequenceEqual([1])));
                Answer(g, c => c.Targets.SequenceEqual([1]));
            }
            else { Play(g); SelectedSlash(g, [1, 2]); }
            Reach(g, Penalty); var original = g.ResolutionStack.OfType<CardUseFrame>().Last();
            if (legacy) Require(original.Action is null && original.CardId == 0 && original.PhysicalCardIds is { Count: 0 } &&
                E<SlashTargetPenaltyOfferedEvent>(g).Single().Identity.LegacyProducerProgramId is not null,
                "Mature Shensu keeps its exact real Action-null producer and creates no fabricated physical or accepted action.");
            g = Cold(g, r); Play(g);
            var offer = E<SlashTargetPenaltyOfferedEvent>(g).Where(e => e.Identity.CardUseFrameId == original.Id).ToArray();
            Require(offer.Select(e => e.Identity.TargetSeat).Distinct().Count() == offer.Length &&
                E<SlashTargetBenefitOfferedEvent>(g).Where(e => e.CardUseFrameId == original.Id).Select(e => e.TargetSeat).Distinct().Count() == offer.Length &&
                E<UniqueHpTargetAnnouncedEvent>(g).Where(e => e.Target.CardUseFrameId == original.Id).Select(e => e.Target.TargetSeat).Distinct().Count() == offer.Length,
                "Mandatory chase, Moukui and Tianming visit each real finalized target once without replaying the old windows.");
            Require(E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == original.Id) == 1,
                "Both entity and mature virtual producers finish exactly once after all dedicated target windows."); g = Cold(g, r);
        }
        var (native, nr) = Create(native: true);
        for (var i = 0; i < 100 && !E<SlashTargetPenaltyFinishedEvent>(native).Any(); i++) Accept(native, new AdvanceOneStepCommand(native.Revision));
        Require(E<SlashTargetPenaltyPaidEvent>(native).Any() && E<SlashTargetPenaltyFinishedEvent>(native).Any(e => e.Completed),
            "The same fixed small setup really selects, uses Slash and pays a mandatory target decision through native AI commands."); native = Cold(native, nr);
    }

    private static int SubmitSlash(GameEngine g, LegalAction a)
    { var id = a.CardId ?? throw new InvalidOperationException("Missing physical Slash."); Accept(g, new PlayCardCommand(0, id, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId)
        { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources }); return id; }
    private static int[] SelectedSlash(GameEngine g, int[] targets, int[]? cards = null)
    { var ids = cards ?? g.CreateSnapshot(0).Players[0].Hand.Take(2).Select(c => c.Id).ToArray(); Accept(g, new UseProgramSkillCommand(0, "classic:fuhun", "two-hand-cards-as-slash", ids, targets, g.Revision, P(g)!.PromptId)); return ids; }
    private static void Use(GameEngine g, string id, int[]? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Penalty(PendingDecision p) => p.SkillPrompt?.SkillId == Zhuiji && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "slash-target-penalty");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> test) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(test).Id, g.Revision)); }
    private static void Continue(GameEngine g) { if (P(g)!.PlayerSeat == 0) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
    private static void Play(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> test)
    { for (var i = 0; i < 220; i++) { if (P(g) is { } p && test(p)) return;
        if (P(g) is { PlayerSeat: 0 } human) {
            if (human.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (human.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
            else throw new InvalidOperationException("Unexpected human boundary: " + human.Kind + "/" + human.SkillPrompt?.SkillId);
        } else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
        throw new InvalidOperationException("The small fixed prefix did not reach its exact boundary."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
        Require(State(g) == State(restored), "Four viewer projections, exact owning frames, real entities and accepted-journal cold prefix match."); return restored; }
    private static void Private(GameEngine g) { var p = P(g)!; Require(p.IsPrivate, "Target cost choices are private to their actual owner.");
        for (var i = 0; i < 4; i++) if (i != p.PlayerSeat) Require(g.CreateSnapshot(i).PendingDecision is null, "Other viewers cannot read target-owned hand identities."); }
    private static void Freeze<T>(IReadOnlyList<T> values) { if (values.Count > 0 && values is IList<T> mutable) { try { mutable[0] = values[0]; throw new InvalidOperationException("Mutable exposed collection."); } catch (NotSupportedException) { } } }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected true command."); }
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static (GameEngine, ContentRegistry) Create(bool highHp = false, bool equipment = false, bool spear = false, bool sourceLoss = false, bool legacy = false, bool coexist = false, bool native = false)
    { var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new Fixture(highHp, equipment, spear, sourceLoss, legacy, coexist));
      var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, ModeId = Mode, HumanSeat = native ? -1 : 0, HumanRole = native ? null : Role.Lord,
          UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 3 }, r);
      Accept(g, new StartGameCommand()); if (!native) { Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
      Accept(g, new SelectGeneralCommand(0, "fixture:smc-owner", g.Revision, P(g)!.PromptId)); } return (g, r); }
    private sealed class Fixture(bool highHp, bool equipment, bool spear, bool sourceLoss, bool legacy, bool coexist) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-sp-ma-chao", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = JsonNode.Parse("""
            {"skills":[
              {"id":"fixture:smc-driver","revision":1,"activations":[
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipment"}]},
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"loseHp","target":"owner","amount":1}]}]},
              {"id":"fixture:smc-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"fixture:smc-hp","revision":1,"triggers":[{"id":"hp","window":"afterHpRecovered","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:smc-cost","revision":1,"triggers":[{"id":"cost","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementOccurrence":"perBatch","movementReasons":["card.recast.discard"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:smc-gain","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["card.recast.draw"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}
            ]}
            """)!;
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            if (sourceLoss)
            {
                rules["skills"]![2]!["triggers"]![0]!["effects"]!.AsArray().Add(JsonNode.Parse("""{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"}"""));
                rules["skills"]![2]!["triggers"]![0]!["effects"]!.AsArray().Add(JsonNode.Parse("""{"op":"loseHp","target":"selectedTarget","amount":1}"""));
                rules["skills"]!.AsArray().Add(JsonNode.Parse("""
                {"id":"fixture:smc-loss","revision":1,"triggers":[{"id":"loss","window":"afterHpLost","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,
                  "effects":[{"op":"selectTarget","target":"owner","targetKind":"otherWoundedMale"},{"op":"damage","target":"selectedTarget","amount":1}]}]}
                """));
            }
            var ids = rules["skills"]!.AsArray().Select(n => n!["id"]!.GetValue<string>()).ToArray();
            var descriptions = ids.ToDictionary(id => id, id => id is Hp or Cost or Gain ? (object)new { name = id, description = "真实付款子窗", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
                : new { name = id, description = "固定小型真实命令" });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = descriptions }));
            foreach (var id in ids) b.AddSkill(new(id, id, "真实夹具") { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:smc-noop", "真实失源替换", "No program"));
            b.AddSkill(new("fixture:smc-pick-owner", "固定真人候选", "Passive") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? 10000d : -10000d) });
            b.AddSkill(new("fixture:smc-pick-other", "固定其他候选", "Passive") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r != Role.Lord ? 10000d : -10000d) });
            var owner = new List<string> { Shichou, Driver, "classic:fuhun", "fixture:smc-pick-owner" };
            if (legacy) owner.Add("boundary:shensu"); if (coexist) owner.Add("ol:moukui"); if (sourceLoss) owner.Add(Loss);
            b.AddGeneral(new("fixture:smc-owner", "SP马超真实共享能力", "supporter", Zhuiji, "qun", 4, owner, GeneralGender.Male));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:smc-other-{i}", "其他角色", "supporter", "fixture:smc-pick-other", "wei", highHp ? 6 : 4,
                coexist ? ["fixture:smc-quiet", "ol:tianming"] : equipment ? sourceLoss ? ["fixture:smc-quiet", Hp, Cost, Gain, "classic:duanchang"] : ["fixture:smc-quiet", Hp, Cost, Gain] : ["fixture:smc-quiet"], GeneralGender.Male) { InitialHp = equipment ? 1 : null });
            b.AddDeck(new("fixture:smc-deck", "固定真实实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 72).Select(_ => new ContentDeckPhysicalCard(
                equipment ? "classic:silver-lion" : spear ? "classic:zhangba-serpent-spear" : "standard:slash", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "SP马超完整真实命令草稿", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:smc-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:smc-owner", "fixture:smc-other-1", "fixture:smc-other-2", "fixture:smc-other-3"]));
        }
    }
}
