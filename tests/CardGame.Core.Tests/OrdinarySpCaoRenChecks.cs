using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OrdinarySpCaoRenChecks
{
    private const string Skill = "ol:weikui", EndingSkill = "ol:lizhan", Binding = "pay-hp-inspect-hand";
    private const string Driver = "fixture:scr-driver", Hp = "fixture:scr-hp", Move = "fixture:scr-move", Entry = "fixture:scr-entry", Loss = "fixture:scr-loss";
    private const string Mode = "identity:classic-ordinary-sp-cao-ren-fixture", DiscardReason = "program.hp-hand-inspection.discard";

    public static void PrivateWholeHandAfterHpChildrenAndRealDiscard()
    {
        var (g, r) = Create(); Play(g); var oldHp = g.State.Players[0].Hp; Begin(g, 2);
        Reach(g, p => p.SkillPrompt?.SkillId == Hp);
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.InspectedHandSlash is not null);
        Require(root.InspectedHandSlash is { Stage: InspectedHandSlashStage.HpChildren, ViewedCardIds.Count: 0 } &&
            g.State.Players[0].Hp == oldHp - 1 && E<InspectedHandViewedEvent>(g).Length == 0 &&
            Enumerable.Range(0, 4).All(s => g.CreateSnapshot(s).PrivateRevealedCards?.Count is null or 0),
            "Real HP payment pauses its actual child before any opponent hand inspection is exposed.");
        g = Cold(g, r); Continue(g); Reach(g, View); PrivateWholeHand(g, 2); Reject(g); g = Cold(g, r);
        var viewed = g.CreateSnapshot(0).PrivateRevealedCards!.Select(c => c.Id).ToArray();
        var card = P(g)!.Choices.First().Cards.Single(); Answer(g, c => c.Cards.SequenceEqual([card]));
        Reach(g, p => p.SkillPrompt?.SkillId == Move); g = Cold(g, r); Continue(g); Play(g);
        Require(E<InspectedHandHpPaidEvent>(g).Single().ProgramFrameId == root.Id &&
            E<ProgramSkillHpLostEvent>(g).Count(e => e.FrameId == root.Id && e.Amount == 1) == 1 &&
            g.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Hand(2) && m.To == CardLocation.DiscardPile && m.Reason.Value == DiscardReason) == 1 &&
            !g.CardMovements.Any(m => m.CardId == card && m.To == CardLocation.Processing) &&
            E<InspectedHandSlashIssuedEvent>(g).Length == 0 && E<InspectedHandFinishedEvent>(g).Single().IssuedSlash == false &&
            !g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Skill),
            "The authorized named hand choice really discards exactly one original entity and the paid once-per-Play activation returns through movement observers.");
        Require(viewed.Contains(card) && Enumerable.Range(0, 4).All(s => g.CreateSnapshot(s).PrivateRevealedCards?.Count is null or 0),
            "The paid view does not remain visible after its original program retires."); g = Cold(g, r);
    }

    public static void ForcedSlashAfterFiniteQuotaAndDirectedDistanceExpiry()
    {
        var (g, r) = Create(dodge: true); Play(g);
        Require(g.GetCombatDistance(0, 2) == 2, "The fixed living four-seat primary target is genuinely outside normal range before payment.");
        var normal = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]) && a.ConversionSource?.SkillId == "classic:longdan");
        Accept(g, new PlayCardCommand(0, normal.CardId!.Value, normal.TargetSeats, g.Revision, P(g)!.PromptId, normal.PlayedCardKind, normal.TargetCardId)
        { ConversionSource = normal.ConversionSource, AdditionalConversionSources = normal.AdditionalConversionSources }); Play(g);
        Require(E<CardUseDebitRecordedEvent>(g).Length == 1 && !g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "A genuine ordinary converted Slash has already consumed the finite Play quota.");
        Begin(g, 2); Reach(g, p => p.SkillPrompt?.SkillId == Hp); g = Cold(g, r); Continue(g); Reach(g, View); PrivateWholeHand(g, 2);
        Require(P(g)!.Choices is [var mandatory] && mandatory.Parameters.GetValueOrDefault("option") == "slash" && mandatory.Cards.Count == 0,
            "Printed physical Dodge fixes the compulsory skill-use branch without offering a second decline.");
        g = Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("option") == "slash");
        Reach(g, p => p.Kind == DecisionKind.RespondDodge && p.PlayerSeat == 2);
        var issued = E<InspectedHandSlashIssuedEvent>(g).Single().Return;
        Require(g.GetCombatDistance(0, 2) == 1 && g.GetCombatDistance(2, 0) == 2 &&
            E<CardActionAcceptedEvent>(g).Any(e => e.Action.ActionId == issued.ActionId && e.Action.Type == CardActionType.Use &&
                e.Action.ActorSeat == 0 && e.Action.ProviderSeat == 0 && e.Action.EffectiveKind == CardKind.Slash && e.Action.PhysicalCards.Count == 0) &&
            E<CardUseDeclaredEvent>(g).Count(e => e.ResolutionId == issued.CardUseFrameId && e.CardId == 0 && e.CardKind == CardKind.Slash) == 1 &&
            E<CardUseDebitRecordedEvent>(g).All(e => e.Debit.CardActionId != issued.ActionId),
            "The mature forced-skill contract creates a real zero-material Use and exact directed distance1 while ignoring, without refunding or debiting, ordinary quota.");
        g = Cold(g, r); Accept(g, new AdvanceOneStepCommand(g.Revision));
        Reach(g, p => p.SkillPrompt?.SkillId == Entry && g.ResolutionStack.OfType<ProgramSkillFrame>().Last().WindowContext?.Window == SkillProgramTriggerWindow.CardUseCompleted);
        g = Cold(g, r); Continue(g); Play(g);
        Require(E<InspectedHandFinishedEvent>(g).Single().IssuedSlash && E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == issued.CardUseFrameId) == 1 &&
            E<CardUseDebitRecordedEvent>(g).Length == 1 && !g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash) &&
            !g.CardMovements.Any(m => m.CardId == 0), "The real skill Slash completes its response/Completed children once and leaves ordinary quota exhausted without fabricated material cleanup.");
        var turn = g.State.TurnNumber; Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Until(g, () => g.State.TurnNumber > turn); Require(g.GetCombatDistance(0, 2) == 2,
            "The issued distance right expires with the original actual turn, rather than carrying into the next actor's turn."); g = Cold(g, r);
    }

    public static void PaidDyingAndSourceLossContinueOnce()
    {
        var (dying, dr) = Create(dying: true); Play(dying); Use(dying, "hurt", [0]); Play(dying);
        Require(dying.State.Players[0].Hp == 1, "A real preparation HP loss leaves exactly one HP; no player state is edited.");
        Begin(dying, 2); Reach(dying, p => p.SkillPrompt?.SkillId == Entry);
        var root = dying.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.InspectedHandSlash is not null);
        Require(dying.ResolutionStack.OfType<DyingFrame>().Any(d => d.ParentFrameId == root.Id && d.VictimSeat == 0 && d.Continuation == DyingContinuationKind.ProgramSkill) &&
            E<InspectedHandViewedEvent>(dying).Length == 0, "The actual one-HP cost creates a real direct Dying child and still has not inspected the target hand.");
        dying = Cold(dying, dr); Continue(dying); Reach(dying, p => p.PlayerSeat == 0 && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "peach"));
        var peach = P(dying)!.Choices.First(c => c.Parameters.GetValueOrDefault("response") == "peach").Cards.Single();
        Answer(dying, c => c.Parameters.GetValueOrDefault("response") == "peach");
        Reach(dying, p => p.SkillPrompt?.SkillId == Entry && dying.ResolutionStack.OfType<ProgramSkillFrame>().Last().WindowContext?.Window == SkillProgramTriggerWindow.CardUseCompleted);
        dying = Cold(dying, dr); Continue(dying); Reach(dying, View); PrivateWholeHand(dying, 2); dying = Cold(dying, dr);
        Answer(dying, c => c.Parameters.GetValueOrDefault("option") == "discard"); Reach(dying, p => p.SkillPrompt?.SkillId == Move); dying = Cold(dying, dr); Continue(dying); Play(dying);
        Require(E<ProgramSkillHpLostEvent>(dying).Count(e => e.FrameId == root.Id && e.Amount == 1) == 1 &&
            E<InspectedHandViewedEvent>(dying).Length == 1 && E<InspectedHandDiscardPaidEvent>(dying).Length == 1 &&
            E<CardActionAcceptedEvent>(dying).Any(e => e.Action.EffectiveKind == CardKind.Peach && e.Action.PhysicalCards.Any(c => c.CardId == peach)),
            "Actual JSON-journal restored engines continue Peach/Completed and later view/discard without repeating the real one-HP cost.");

        foreach (var issuedLoss in new[] { false, true })
        {
            var (g, r) = Create(dodge: issuedLoss, paidLoss: !issuedLoss, issuedLoss: issuedLoss); Play(g); Begin(g, 2);
            if (issuedLoss) { Reach(g, p => p.SkillPrompt?.SkillId == Hp); Continue(g); Reach(g, View); Answer(g, c => c.Parameters.GetValueOrDefault("option") == "slash"); }
            Reach(g, p => p.SkillPrompt?.SkillId == Loss && p.Choices.Any(c => c.Parameters.GetValueOrDefault("choice") == Skill)); g = Cold(g, r);
            Answer(g, c => c.Parameters.GetValueOrDefault("choice") == Skill);
            if (!issuedLoss) { Reach(g, View); PrivateWholeHand(g, 2); g = Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("option") == "discard"); }
            else { Reach(g, p => p.Kind == DecisionKind.RespondDodge && p.PlayerSeat == 2);
                Require(g.GetCombatDistance(0, 2) == 1, "An already issued directed right survives real source disabling before the original Slash response.");
                g = Cold(g, r); Accept(g, new AdvanceOneStepCommand(g.Revision)); }
            Until(g, () => E<InspectedHandFinishedEvent>(g).Length == 1);
            Require(E<ProgramSkillSuppressedEvent>(g).Any(e => e.SkillId == Skill && e.TargetSeat == 0 && e.Suppressed) &&
                E<InspectedHandHpPaidEvent>(g).Length == 1 && E<InspectedHandViewedEvent>(g).Length == 1 &&
                E<InspectedHandSlashIssuedEvent>(g).Length == (issuedLoss ? 1 : 0) && E<InspectedHandFinishedEvent>(g).Single().IssuedSlash == issuedLoss,
                "Real general-skill disabling after payment retains the earned private branch; after actual issuance it retains the exact typed Slash return and never repays HP."); g = Cold(g, r);
        }
    }

    public static void LizhanCurrentWoundedDrawNativeAndStrictComposition()
    {
        var (g, r) = Create(); Play(g); Use(g, "hurt", [2]); Play(g); Begin(g, 2); Reach(g, p => p.SkillPrompt?.SkillId == Hp); Continue(g);
        Reach(g, View); Answer(g, c => c.Parameters.GetValueOrDefault("option") == "discard"); Play(g);
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == EndingSkill));
        g = Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == EndingSkill);
        Reach(g, p => p.SkillPrompt?.SkillId == EndingSkill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-targets"));
        Require(P(g)!.Choices.Any(c => c.Targets.SequenceEqual([0, 2])) && P(g)!.Choices.All(c => c.Targets.All(s => g.State.Players[s].Hp < g.State.Players[s].MaxHp)),
            "Actual Ending allows owner and any chosen subset of genuinely wounded living characters.");
        var before = g.CardMovements.Count; g = Cold(g, r); Answer(g, c => c.Targets.SequenceEqual([0, 2]));
        Until(g, () => !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == EndingSkill));
        Require(new[] { 0, 2 }.All(s => g.CardMovements.Skip(before).Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(s)) == 1),
            "Lizhan performs exactly one real Draw per selected target with ordinary movement children and no artificial grant or reward."); g = Cold(g, r);

        var (native, nr) = Create(dodge: true, native: true);
        for (var i = 0; i < 220 && native.State.Status != EngineStatus.Completed && E<InspectedHandFinishedEvent>(native).Length == 0; i++) Accept(native, new AdvanceOneStepCommand(native.Revision));
        Require(E<InspectedHandHpPaidEvent>(native).Length > 0 && E<InspectedHandViewedEvent>(native).Length > 0 &&
            E<InspectedHandSlashIssuedEvent>(native).Length > 0 && native.AcceptedCommands.All(c => c is not AnswerPromptCommand),
            "The fixed native source uses public activation scoring and its actually authorized private chooser; no AI prompt is answered manually or seed searched."); native = Cold(native, nr);

        var presentation = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
            skills = new Dictionary<string, object> { ["fixture:scr-bad"] = new { name = "失血看牌严格合同", description = "仅新节点的组合门" } } });
        foreach (var part in new[] {
            "\"minCards\":1,\"maxCards\":1,\"targetKind\":\"otherLivingWithHand\",\"usesPerTurn\":null,\"usesPerPhase\":1",
            "\"minCards\":0,\"maxCards\":0,\"targetKind\":\"otherLiving\",\"usesPerTurn\":null,\"usesPerPhase\":1",
            "\"minCards\":0,\"maxCards\":0,\"targetKind\":\"otherLivingWithHand\",\"usesPerTurn\":1,\"usesPerPhase\":1" })
        {
            var rejected = false;
            try { SkillProgramCatalog.Load("{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"fixture:scr-bad\",\"revision\":1,\"activations\":[{\"id\":\"bad\",\"minTargets\":1,\"maxTargets\":1," + part + ",\"effects\":[{\"op\":\"payHpInspectHandThenDiscardOrSlash\",\"target\":\"owner\"}]}]}]}", presentation); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Only the exact new zero-card once-per-phase one-hand-target HP-inspection composition is admitted; historical loader defaults remain unchanged.");
        }
    }

    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool View(PendingDecision p) => p.SkillPrompt?.SkillId == Skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "inspected-hand-slash");
    private static void PrivateWholeHand(GameEngine g, int target)
    {
        var cards = g.CreateSnapshot(target).Players[target].Hand.Select(c => c.Id).ToArray(); var view = g.CreateSnapshot(0);
        Require(P(g) is { IsPrivate: true, PlayerSeat: 0 } && view.PrivateRevealedCards!.Select(c => c.Id).SequenceEqual(cards) &&
            Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PrivateRevealedCards?.Count is null or 0) &&
            Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null),
            "Only the paid owner snapshot exposes the complete actual opponent hand and its exact private decision; other observers receive neither.");
    }
    private static void Begin(GameEngine g, int target) => Accept(g, new UseProgramSkillCommand(0, Skill, Binding, [], [target], g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string id, int[] targets) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets, g.Revision, P(g)!.PromptId));
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> match) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(match).Id, g.Revision)); }
    private static void Play(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> match)
    { for (var i = 0; i < 160; i++) { if (P(g) is { } p && match(p)) return; Advance(g); } throw new InvalidOperationException("Fixed Cao Ren fixture missed boundary: " + JsonSerializer.Serialize(P(g))); }
    private static void Until(GameEngine g, Func<bool> match)
    { for (var i = 0; i < 180; i++) { if (match()) return; Advance(g); } throw new InvalidOperationException("Fixed Cao Ren fixture did not retire its exact paid child/root."); }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue") == true) Continue(g);
        else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass") == true) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements,
        Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Require(State(g) == State(restored), "All four private views and exact HP/view/payment/issued-return prefixes journal-restore identically."); return restored; }
    private static void Reject(GameEngine g)
    { var before = State(g); var p = P(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && before == State(g), "Rejected private input changes no cost, hand, journal or view."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static (GameEngine, ContentRegistry) Create(bool dodge = false, bool dying = false, bool paidLoss = false, bool issuedLoss = false, bool native = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
            new Fixture(dodge, dying, paidLoss, issuedLoss, native));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, ModeId = Mode, HumanSeat = native ? -1 : 0, HumanRole = native ? null : Role.Lord,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 3 }, r);
        Accept(g, new StartGameCommand()); if (!native) { Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.SelectGeneral);
            Accept(g, new SelectGeneralCommand(0, "fixture:scr-owner", g.Revision, P(g)!.PromptId)); } return (g, r);
    }
    private sealed class Fixture(bool dodge, bool dying, bool paidLoss, bool issuedLoss, bool native) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ordinary-sp-cao-ren", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
            {"id":"{{Driver}}","revision":1,"activations":[{"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"usesPerPhase":1,"effects":[{"op":"loseHp","target":"selectedTarget","amount":{{(dying ? 2 : 1)}}}]}]},
            {"id":"{{Hp}}","revision":1,"triggers":[{"id":"paid-hp","window":"afterHpLost","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"held","options":[{"id":"continue"}]}]}]},
            {"id":"{{Move}}","revision":1,"triggers":[{"id":"original-discard","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perOwnerBatch","movementReasons":["{{DiscardReason}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"held","options":[{"id":"continue"}]}]}]},
            {"id":"{{Entry}}","revision":1,"triggers":[{"id":"enter","window":"dyingEntering","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"held","options":[{"id":"continue"}]}]},{"id":"completed","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["slash","peach"],"includeResponseUses":true,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"held","options":[{"id":"continue"}]}]}]},
            {"id":"{{Loss}}","revision":1,"triggers":[{"id":"disable",{{(issuedLoss ? "\"window\":\"cardUseBeforeTargetEffects\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"slash\"]" : "\"window\":\"afterHpLost\",\"subject\":\"owner\"")}},"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"suppressGeneralSkill","target":"owner"}]}]}
            ]}
            """;
            var labels = new Dictionary<string, object> { [Driver] = new { name = "实际前置失血", description = "固定命令" }, [Loss] = new { name = "实际技能失效", description = "原生抑制选择" } };
            foreach (var id in new[] { Hp, Move, Entry }) labels[id] = new { name = "真实子窗暂停", description = "继续原父链", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var id in catalog.Programs.Keys) b.AddSkill(new(id, id, "真实命令夹具") { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id], Tags = id == Driver ? SkillTag.None : SkillTag.Locked });
            foreach (var lord in new[] { true, false }) b.AddSkill(new(lord ? "fixture:scr-pick-owner" : "fixture:scr-pick-other", "正式选将", "角色评分")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => (role == Role.Lord) == lord ? 10000d : -10000d) });
            var own = new List<string> { "fixture:scr-pick-owner", EndingSkill }; if (dodge) own.Add("classic:longdan"); if (!native) own.AddRange([Driver, Hp, Entry]); if (paidLoss || issuedLoss) own.Add(Loss);
            b.AddGeneral(new("fixture:scr-owner", "伪溃真实来源", "supporter", Skill, "wei", dying ? 2 : 6, own.ToArray()));
             for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:scr-other-{i}", "其他角色", "supporter", "fixture:scr-pick-other", "qun", 8, !native ? [Move] : []));
            b.AddDeck(new("fixture:scr-deck", "固定同类真实材料", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 72).Select(i =>
                new ContentDeckPhysicalCard(dodge ? "standard:dodge" : "standard:peach", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(Mode, "SP曹仁当前真实流程", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:scr-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:scr-owner", "fixture:scr-other-1", "fixture:scr-other-2", "fixture:scr-other-3"]));
        }
    }
}
