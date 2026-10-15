using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OrdinaryFuWanChecks
{
    private const string Skill = "ol:moukui", Driver = "fixture:fw-driver", Gain = "fixture:fw-gain", Entry = "fixture:fw-entry";
    private const string Enhance = "fixture:fw-enhance", Prevent = "fixture:fw-prevent", Loss = "fixture:fw-loss";
    private const string Mode = "identity:classic-fu-wan-fixture";
    private const string DrawReason = "skill-program.slash-target-benefit.draw", DiscardReason = "skill-program.slash-target-benefit.discard";
    private const string SettleReason = "skill-program.slash-target-benefit.settlement";

    public static void RealBenefitAndDodgeCancellationPayOriginalEntitiesOnce()
    {
        foreach (var choice in new[] { "draw", "discard", "decline" })
        {
            var (g, r) = Create(); Play(g); var hp = g.State.Players[1].Hp;
            var paid = Slash(g, 1); Reach(g, Offer); g = Cold(g, r); Reject(g);
            Choose(g, choice);
            if (choice == "discard") { Reach(g, CardChoice); Private(g); g = Cold(g, r); Answer(g, c => HandSlot(c)); }
            Play(g);
            var offers = E<SlashTargetBenefitOfferedEvent>(g); var benefits = E<SlashTargetBenefitPaidEvent>(g);
            var settled = E<SlashTargetBenefitSettledEvent>(g);
            Require(offers.Length == 1 && benefits.Length == (choice == "decline" ? 0 : 1) &&
                E<SlashTargetBenefitCancellationEvent>(g).Length == (choice == "decline" ? 0 : 1) &&
                settled.Length == (choice == "decline" ? 0 : 1) && settled.All(e => e.Paid && e.ActorSeat == 0 && e.TargetSeat == 1) &&
                g.State.Players[1].Hp == hp,
                "A benefit creates one same-use target debt only after actual payment, and only full genuine Jink cancellation settles it.");
            Require(g.CardMovements.Count(m => m.Reason.Value == DrawReason) == (choice == "draw" ? 1 : 0) &&
                g.CardMovements.Count(m => m.Reason.Value == DiscardReason) == (choice == "discard" ? 1 : 0) &&
                g.CardMovements.Count(m => m.Reason.Value == SettleReason && m.From.OwnerSeat == 0) == (choice == "decline" ? 0 : 1),
                "Draw, foreign HE discard and reverse HE discard are real original ledger movements, never hidden fake payments.");
            AssertOriginalUse(g, paid); g = Cold(g, r);
        }
    }

    public static void LegacyVirtualSlashAndBaguaUseExactCancellationProducer()
    {
        var (g, r) = Create(legacy: true);
        Reach(g, p => p.SkillPrompt?.SkillId == "boundary:shensu" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, p => p.SkillPrompt?.SkillId == "boundary:shensu" && p.Choices.Any(c => c.Targets.SequenceEqual([1])));
        Answer(g, c => c.Targets.SequenceEqual([1])); Reach(g, Offer);
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single();
        Require(use.CardId == 0 && use.Action is null && use.PhysicalCardIds is { Count: 0 } &&
            use.SlashTargetBenefits is [{ Use.ActionId: null, Use.LegacyProducerProgramId: not null }],
            "The mature actual Action-null Shensu Slash keeps its real paused producer and creates no fabricated AcceptedAction or physical material.");
        g = Cold(g, r); Choose(g, "draw"); Play(g);
        Require(E<SlashTargetBenefitCancellationEvent>(g).Length == 1 && E<SlashTargetBenefitSettledEvent>(g).Single().Paid &&
            !E<CardActionAcceptedEvent>(g).Any(e => e.Action.ActorSeat == 0 && e.Action.EffectiveKind == CardKind.Slash),
            "The legacy parent resumes once after genuine cancellation; ordinary actual-target windows remain in its original return order."); g = Cold(g, r);

        var (bagua, br) = Create(bagua: true); Play(bagua);
        Use(bagua, "equip", [1]); Play(bagua); var physical = Slash(bagua, 1); Reach(bagua, Offer); Choose(bagua, "draw"); Play(bagua);
        var slash = E<CardActionAcceptedEvent>(bagua).Single(e => e.Action.Type == CardActionType.Use && e.Action.EffectiveKind == CardKind.Slash).Action;
        Require(E<JudgmentResolvedEvent>(bagua).Any(e => e.TargetSeat == 1 && e.Reason == JudgmentReasons.BaguaDefense && e.Succeeded) &&
            E<CardActionAcceptedEvent>(bagua).Any(e => e.Action.Type == CardActionType.Response && e.Action.EffectiveKind == CardKind.Dodge &&
                e.Action.ActorSeat == 1 && e.Action.PhysicalCards.Count == 0 && e.Action.ParentActionId == slash.ActionId) &&
            E<SlashTargetBenefitSettledEvent>(bagua).Single().Paid,
            "Actual successful Bagua provides a zero-material Dodge cancellation; physical CardResponded events are not invented for armor.");
        AssertOriginalUse(bagua, physical); bagua = Cold(bagua, br);
    }
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Offer(PendingDecision p) => p.SkillPrompt?.SkillId == Skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("slash-benefit-step") == "draw");
    private static bool CardChoice(PendingDecision p) => p.SkillPrompt?.SkillId == Skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("slash-benefit-step") == "card");
    private static bool HandSlot(PromptChoice c) => c.Parameters.GetValueOrDefault("slash-benefit-step") == "card" && c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand);
    private static void Choose(GameEngine g, string step) => Answer(g, c => c.Parameters.GetValueOrDefault("slash-benefit-step") == step);
    private static void Play(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static int[] Slash(GameEngine g, int target)
    { var ids = g.CreateSnapshot(0).Players[0].Hand.Take(2).Select(c => c.Id).ToArray(); Accept(g, new UseProgramSkillCommand(0, "classic:fuhun", "two-hand-cards-as-slash", ids, [target], g.Revision, P(g)!.PromptId)); return ids; }
    private static void Use(GameEngine g, string binding, int[] targets) => Accept(g, new UseProgramSkillCommand(0, Driver, binding, [], targets, g.Revision, P(g)!.PromptId));
    private static void Answer(GameEngine g, Func<PromptChoice, bool> match)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(match).Id, g.Revision)); }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> match)
    { for (var i = 0; i < 150; i++) { var p = P(g); if (p is not null && match(p)) return; Advance(g); }
      throw new InvalidOperationException("Fixed Fu Wan fixture did not reach a real boundary: " + JsonSerializer.Serialize(P(g))); }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0 } && p.SkillPrompt?.SkillId == "boundary:shensu" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass")) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements,
        Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    { var clone = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
      Require(State(g) == State(clone), "Four private views, exact scalar receipt/ledger, pending child and typed return journal-restore identically."); return clone; }
    private static void Reject(GameEngine g)
    { var p = P(g)!; var old = State(g); Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("unpublished"), g.Revision)).Accepted && old == State(g), "Unpublished input pays nothing and changes no views, receipt or history."); }
    private static void Private(GameEngine g)
    { var p = P(g)!; Require(p.IsPrivate && p.PlayerSeat == 0 && p.Choices.Where(HandSlot).All(c => c.Cards.Count == 0), "Foreign hands are opaque actual slots.");
      for (var s = 1; s < 4; s++) Require(g.CreateSnapshot(s).PendingDecision is null, "Unrelated viewers do not receive private slot choices."); }
    private static void AssertOriginalUse(GameEngine g, int[] ids) => Require(ids.All(id =>
        g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
        g.CardMovements.Count(m => m.CardId == id && m.Reason == CardMoveReasons.UseFinished) == 1), "The actual multi-material Slash pays and cleans every original entity once after all children.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Create(bool multiple = false, bool prevent = false, bool legacy = false, bool bagua = false, bool gainDying = false, bool sourceLoss = false, bool native = false, bool gainDamage = false)
    {
        IGameContentPackage classicSkills = legacy ? new LegacySlashSkillFixture() : new StandardClassicGeneralPackage();
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), classicSkills,
            new Fixture(multiple, prevent, legacy, bagua, gainDying, sourceLoss, native, gainDamage));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, ModeId = Mode, HumanSeat = native ? -1 : 0, HumanRole = native ? null : Role.Lord,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = native ? 2 : 4 }, r);
        Accept(g, new StartGameCommand()); if (!native) { Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.SelectGeneral); Accept(g, new SelectGeneralCommand(0, "fixture:fw-owner", g.Revision, P(g)!.PromptId)); }
        return (g, r);
    }
    private sealed class LegacySlashSkillFixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:fw-legacy-skills", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            // This branch checks the old actionless completion contract. The full
            // current catalog opts completion into category and ordinal tracking.
            var source = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
                new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage());
            foreach (var id in new[] { Skill, "boundary:shensu", "classic:fuhun", "classic:mashu", "classic:wusheng", "classic:paoxiao" })
                builder.AddSkill(source.GetSkill(id));
        }
    }
    private sealed class Fixture(bool multiple, bool prevent, bool legacy, bool bagua, bool gainDying, bool sourceLoss, bool native, bool gainDamage) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ordinary-fu-wan", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
            {"id":"{{Driver}}","revision":1,"activations":[{"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]}]},
            {"id":"{{Gain}}","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{DrawReason}}"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{{(gainDying ? "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":7}," : gainDamage ? "{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}," : "")}}{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
            {"id":"{{Entry}}","revision":1,"triggers":[{"id":"entry","window":"dyingEntering","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]},{"id":"nested-damage","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]},{"id":"peach-completed","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["peach"],"includeResponseUses":true,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
            {"id":"{{Enhance}}","revision":1,"triggers":[{"id":"extra","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"effects":[{"op":"applyCurrentCardEnhancements","target":"owner","amount":1}]}]},
            {"id":"{{Prevent}}","revision":1,"triggers":[{"id":"prevent","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"effects":[{"op":"preventCurrentTargetSlashCancellationByRule","target":"owner"}]}]},
            {"id":"{{Loss}}","revision":1,"triggers":[{"id":"paid-loss","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["{{DiscardReason}}"],"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"issueCurrentTurnNonLockedSkillSuppression","target":"selectedTarget"}]}]}]}
            """;
            var descriptions = new Dictionary<string, object>();
            foreach (var id in new[] { Driver, Gain, Entry, Enhance, Prevent, Loss }) descriptions[id] = id is Gain or Entry
                ? new { name = id, description = "真实子帧", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
                : (object)new { name = id, description = "真实成熟能力" };
            var c = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = descriptions }));
            foreach (var id in c.Programs.Keys) b.AddSkill(new(id, id, "固定真实命令能力") { Program = c.Programs[id], ProgramPresentation = c.Presentations[id] });
            foreach (var owner in new[] { true, false }) b.AddSkill(new(owner ? "fixture:fw-pick-owner" : "fixture:fw-pick-other", "固定选择", "正式身份选将评分")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => (role == Role.Lord) == owner ? 10000d : -10000d) });
            var skills = new List<string> { "fixture:fw-pick-owner", "classic:fuhun", "classic:mashu" };
            if (!native) skills.Add(Driver); if (native) skills.Add("classic:longdan"); if (multiple) skills.Add(Enhance); if (prevent) skills.Add(Prevent);
            if (legacy) skills.Add("boundary:shensu"); if (gainDying || gainDamage) skills.AddRange([Gain, Entry]);
            b.AddGeneral(new("fixture:fw-owner", "真实谋溃来源", "supporter", Skill, "qun", 6, skills.ToArray()));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:fw-other-{i}", "其他角色", "supporter", "fixture:fw-pick-other", "wei", 8, i == 1 && sourceLoss ? [Loss] : []));
            b.AddDeck(new("fixture:fw-deck", "固定真实材料", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 64).Select(_ =>
                new ContentDeckPhysicalCard(bagua ? "standard:bagua" : gainDying ? "standard:peach" : "standard:dodge", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(Mode, "谋溃实际同杀", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:fw-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:fw-owner", "fixture:fw-other-1", "fixture:fw-other-2", "fixture:fw-other-3"]));
        }
    }
}
