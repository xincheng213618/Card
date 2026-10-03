using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryLingTongChecks
{
    private const string Skill = "boundary:xuanfeng-current", Classic = "classic:xuanfeng";
    private const string Driver = "fixture:lt-driver", Movement = "fixture:lt-movement", Hp = "fixture:lt-hp";
    private const string Mode = "identity:classic-lt-fixture";
    private const string ForeignReason = "skill-program.boundary:xuanfeng-current.ChooseOtherOwnedCardDiscard";
    private const string BoundReason = "skill-program.fixture:lt-driver.MoveBoundCards";

    public static void OwnerBatchEquipmentOrTwoCardsCreatesOneOpportunity()
    {
        foreach (var scenario in new[] { "two-hand", "one-equipment", "mixed", "gift" })
        {
            var (g, r) = Create(); Play(g);
            if (scenario is "one-equipment" or "mixed") { Use(g, "equip", targets: [0]); Play(g); }
            var hand = g.CreateSnapshot(0).Players[0].Hand.Select(c => c.Id).ToArray();
            var equipment = g.CreateSnapshot(0).Players[0].Equipment.Select(c => c.Id).ToArray();
            int[] paid = scenario switch
            {
                "one-equipment" => [equipment.Single()],
                "mixed" => [hand[0], equipment.Single()],
                _ => hand.Take(2).ToArray()
            };
            BoundMove(g, scenario == "gift" ? "gift-two" : paid.Length == 1 ? "discard-one" : "discard-two", paid);
            Reach(g, p => Activation(p, Skill));
            var window = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(f =>
                f.Candidates.Any(c => c.SkillId == Skill));
            var index = window.Candidates.Select((c, i) => (c, i)).Single(x => x.c.SkillId == Skill).i;
            var context = window.Contexts![index];
            Require(window.Candidates.Count(c => c.SkillId == Skill && c.OwnerSeat == 0) == 1 &&
                window.Batch.Movements.Count == paid.Length && window.Batch.Movements.Select(m => m.CardId).Order().SequenceEqual(paid.Order()) &&
                context.ParentFrameId == window.Batch.Id && context.MovementBatch == window.Batch &&
                context.Facts is { } facts && facts.MovedCardCount == paid.Length &&
                facts.MovedEquipmentCardCount == (scenario is "one-equipment" or "mixed" ? 1 : 0),
                "One frozen actual owner batch qualifies once: equipment OR total two does not create two candidates or consult a live zone count.");
            Require(paid.All(id => g.CardMovements.Count(m => m.CardId == id && m.Reason.Value == BoundReason) == 1),
                "Real selection pays each original entity once before the optional opportunity, including atomic mixed HE and a non-discard gift.");
            if (scenario == "gift") Require(window.Batch.AwaitingProgramFrameId is null &&
                window.Batch.Movements.All(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(2)),
                "The legal non-awaited activation commits one actual two-card gift batch before the normal post-event owner-loss window; it does not borrow the draw-ended-only awaited contract.");
            Cold(g, r); Reject(g); Activate(g, Skill); Reach(g, p => OtherDiscard(p)); Private(g); Cold(g, r);
            DiscardOther(g, 1); Reach(g, OtherDiscard); DiscardOther(g, 3); Play(g);
            Require(Started(g, Skill) == 1 && ForeignMoves(g).Length == 2 &&
                ForeignMoves(g).Select(m => m.From.OwnerSeat).Order().SequenceEqual(new int?[] { 1, 3 }) &&
                Facts<ProgramBindingResolvedEvent>(g).Count(e => e.SkillId == Skill && e.Activated && e.Completed) == 1 &&
                paid.All(id => g.CardMovements.Count(m => m.CardId == id && m.Reason.Value == BoundReason) == 1),
                "Both real optional discards and their child returns finish one binding without replaying the original paid owner batch.");
            if (scenario == "gift") Require(paid.All(id => g.CreateSnapshot(2).Players[2].Hand.Any(c => c.Id == id)),
                "The qualifying loss is a genuine gift; Xuanfeng does not turn those original paid entities into discards.");
            Cold(g, r);
        }
    }

    public static void SeparateLossesAndActualConvertedUseKeepPaymentAndCleanup()
    {
        var (single, sr) = Create(); Play(single);
        for (var i = 0; i < 2; i++)
        { BoundMove(single, "discard-one", [single.CreateSnapshot(0).Players[0].Hand[0].Id]); Play(single); }
        Require(Started(single, Skill) == 0 && ForeignMoves(single).Length == 0 &&
            single.CardMovements.Count(m => m.From == CardLocation.Hand(0) && m.Reason.Value == BoundReason) == 2,
            "Two distinct single-hand loss commands remain two actual batches and never manufacture one two-card opportunity."); Cold(single, sr);

        var (g, r) = Create(conversion: true); Play(g);
        var cards = g.CreateSnapshot(0).Players[0].Hand.Take(2).Select(c => c.Id).ToArray();
        Accept(g, new UseProgramSkillCommand(0, "classic:fuhun", "two-hand-cards-as-slash", cards, [1], g.Revision, P(g)!.PromptId));
        Reach(g, p => Activation(p, Skill)); var batch = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(f => f.Candidates.Any(c => c.SkillId == Skill));
        Require(batch.Batch.Movements.Count == 2 && batch.Batch.Movements.All(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) &&
            batch.Contexts!.Single(c => c.OwnerSeat == 0 && c.Facts?.MovedEquipmentCardCount is not null).Facts is { MovedCardCount: 2, MovedEquipmentCardCount: 0 },
            "The current skill also answers the exact two-material actual Use payment; it is not limited to discard reasons or the Discard phase.");
        Cold(g, r); Activate(g, Skill); Reach(g, OtherDiscard); Decline(g); Reach(g, OtherDiscard); Decline(g); Play(g);
        var use = Facts<CardActionAcceptedEvent>(g).Single(e => e.Action.Type == CardActionType.Use && e.Action.PhysicalCards.Select(c => c.CardId).Order().SequenceEqual(cards.Order())).Action;
        Require(use.EffectiveKind == CardKind.Slash && Started(g, Skill) == 1 && ForeignMoves(g).Length == 0 &&
            cards.All(id => g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
                g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1) &&
            Facts<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1),
            "Declining both follow-ups resumes the actual original converted Slash, including its real damage, payment and entity cleanup once; processing cleanup is not a second loss."); Cold(g, r);
    }

    public static void PrivateSequentialChoicesRecoveryChildrenDeclinesAndSourceLoss()
    {
        var (skip, sk) = Create(); Play(skip); BoundMove(skip, "discard-two", skip.CreateSnapshot(0).Players[0].Hand.Take(2).Select(c => c.Id).ToArray());
        Reach(skip, p => Activation(p, Skill)); Cold(skip, sk); Answer(skip, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); Play(skip);
        Require(Started(skip, Skill) == 0 && ForeignMoves(skip).Length == 0, "Refusing the optional opportunity preserves the already committed original cost and adds no foreign discard."); Cold(skip, sk);

        var (one, oneRegistry) = Create(); Play(one); BoundMove(one, "discard-two", one.CreateSnapshot(0).Players[0].Hand.Take(2).Select(c => c.Id).ToArray());
        Reach(one, p => Activation(p, Skill)); Activate(one, Skill); Reach(one, OtherDiscard); Decline(one); Reach(one, OtherDiscard); DiscardOther(one, 1); Play(one);
        Require(ForeignMoves(one).Length == 1, "The two independent optional steps permit a single discard after refusing the first step."); Cold(one, oneRegistry);

        var (g, r) = Create(observers: true, armor: true); Play(g); Use(g, "equip", targets: [1]); Play(g); Use(g, "hurt", targets: [1]); Play(g);
        var armor = g.CreateSnapshot(0).Players[1].Equipment.Single(c => c.Kind == CardKind.SilverLion).Id;
        var victimHp = g.State.Players[1].Hp;
        BoundMove(g, "discard-two", g.CreateSnapshot(0).Players[0].Hand.Take(2).Select(c => c.Id).ToArray());
        Reach(g, p => Activation(p, Skill)); Activate(g, Skill); Reach(g, OtherDiscard); Private(g); Reject(g);
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "choose-other-owned-card-discard" && c.Cards.SequenceEqual([armor]));
        Reach(g, p => p.SkillPrompt?.SkillId == Hp); var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Skill);
        Require(root.InstructionIndex == 1 && ForeignMoves(g).Length == 1 && g.State.Players[1].Hp == victimHp + 1 &&
            g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(f => f.Change.ParentFrameId == root.Id && f.Change.TargetSeat == 1),
            "The first genuine equipment discard pauses its exact producer beneath Silver Lion's real Recovery child before the second selection."); Cold(g, r); Reject(g);
        Accept(g, new AdvanceOneStepCommand(g.Revision)); Reach(g, p => p.SkillPrompt?.SkillId == Movement);
        root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Skill);
        Require(g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(f => f.ResumeProgramFrameId == root.Id && f.Batch.ParentFrameId == root.Id &&
            f.Batch.Movements.Single().CardId == armor), "The paid discard's subsequent real movement observer has one exact typed program return."); Cold(g, r);
        Accept(g, new AdvanceOneStepCommand(g.Revision)); Reach(g, OtherDiscard); Private(g); DiscardOther(g, 1); Play(g);
        Require(ForeignMoves(g).Length == 2 && ForeignMoves(g).All(m => m.From.OwnerSeat == 1) &&
            g.CardMovements.Count(m => m.CardId == armor && m.Reason.Value == ForeignReason) == 1 &&
            Facts<SilverLionRemovedRecoveryEvent>(g).Count(e => e.PlayerSeat == 1 && e.RecoveredAmount == 1) == 1,
            "Both discards may target the same person; recovery and movement children return without paying the first entity again."); Cold(g, r);

        var (lost, lr) = Create(observers: true, sourceLoss: true); Play(lost);
        BoundMove(lost, "discard-two", lost.CreateSnapshot(0).Players[0].Hand.Take(2).Select(c => c.Id).ToArray());
        Reach(lost, p => Activation(p, Skill)); Activate(lost, Skill); Reach(lost, OtherDiscard); DiscardOther(lost, 1);
        Reach(lost, p => p.SkillPrompt?.SkillId == Movement); Cold(lost, lr); Accept(lost, new AdvanceOneStepCommand(lost.Revision));
        Reach(lost, p => p.SkillPrompt?.SkillId == Movement && p.Choices.Any(c => c.Targets.SequenceEqual([0])));
        // Controlled real non-native actor command: the separate native check below
        // never manually answers an AI prompt or supplies hidden card information.
        Answer(lost, c => c.Targets.SequenceEqual([0])); Play(lost);
        Require(Facts<CurrentTurnNonLockedSkillSuppressionIssuedEvent>(lost).Any(e => e.Suppression.TargetSeat == 0) &&
            ForeignMoves(lost).Length == 1 && Started(lost, Skill) == 1 &&
            Facts<ProgramBindingResolvedEvent>(lost).Any(e => e.SkillId == Skill && e.Activated && !e.Completed),
            "A real movement child invalidates the current nonlocked source after its first paid discard: that cost stays committed and the unpaid second step cancels."); Cold(lost, lr);
    }

    public static void NativeOwnerBatchLossAndStrictValueContextPreserveClassic()
    {
        var (classic, cr) = Create(classicOnly: true); Play(classic);
        BoundMove(classic, "discard-two", classic.CreateSnapshot(0).Players[0].Hand.Take(2).Select(c => c.Id).ToArray()); Play(classic);
        Require(Started(classic, Classic) == 0 && Started(classic, Skill) == 0,
            "The historical classic skill retains its Discard-phase condition; a Play-phase two-card loss does not acquire the new current rule."); Cold(classic, cr);

        var (native, nr) = Create(native: true);
        var issued = Started(native, Skill) > 0;
        for (var i = 0; !issued && i < 100 && native.State.Status != EngineStatus.Completed; i++)
        { Accept(native, new AdvanceOneStepCommand(native.Revision)); issued = Started(native, Skill) > 0; }
        Require(issued && ForeignMoves(native).Length > 0 && native.AcceptedCommands.All(c => c is not AnswerPromptCommand),
            "The fixed native owner actually accepts a genuine equipment/two-card loss and pays a legal foreign discard through normal Advance commands; no AI prompt is manually answered."); Cold(native, nr);
        StrictValueContexts();
    }

    private static void StrictValueContexts()
    {
        const string condition = "\"condition\":{\"kind\":\"compare\",\"left\":{\"kind\":\"movedEquipmentCardCount\"},\"operator\":\"greaterThan\",\"right\":{\"kind\":\"integerConstant\",\"value\":0}}";
        var valid = "{\"id\":\"loss\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\",\"equipment\"],\"movementOccurrence\":\"perOwnerBatch\",\"optional\":true," + condition + ",\"effects\":[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}]}";
        var presentation = "{\"schemaVersion\":3,\"skills\":{\"fixture:lt-value\":{\"name\":\"批次事实\",\"description\":\"严格公开标量\"}}}";
        foreach (var trigger in new[] {
            valid.Replace("\"perOwnerBatch\"", "\"perBatch\"").Replace("[\"hand\",\"equipment\"]", "[\"equipment\"]"),
            valid.Replace("[\"hand\",\"equipment\"]", "[\"hand\"]"),
            valid.Replace("\"optional\":true", "\"optional\":true,\"movementDiscardOnly\":true"),
            "{\"id\":\"loss\",\"window\":\"playEnding\",\"subject\":\"owner\",\"optional\":true," + condition + ",\"effects\":[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}]}"
        })
        {
            var rejected = false;
            try { SkillProgramCatalog.Load("{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"fixture:lt-value\",\"revision\":1,\"triggers\":[" + trigger + "]}]}", presentation); }
            catch (InvalidOperationException e) when (e.Message.Contains("equipment-loss", StringComparison.OrdinalIgnoreCase)) { rejected = true; }
            Require(rejected, "The new value rejects non-owner-batch, absent equipment, synthetic discard-origin and unrelated windows without weakening the loader.");
        }
    }

    private static IEnumerable<T> Facts<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static int Started(GameEngine g, string skill) => Facts<ProgramBindingStartedEvent>(g).Count(e => e.SkillId == skill);
    private static CardMovementRecord[] ForeignMoves(GameEngine g) => g.CardMovements.Where(m => m.Reason.Value == ForeignReason).ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Activation(PendingDecision p, string skill) => p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool OtherDiscard(PendingDecision p) => p.SkillPrompt?.SkillId == Skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "choose-other-owned-card-discard");
    private static void Activate(GameEngine g, string skill) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == skill);
    private static void Decline(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "choose-other-owned-card-decline");
    private static void DiscardOther(GameEngine g, int seat) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "choose-other-owned-card-discard" && c.Targets.SequenceEqual([seat]) && c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
    private static void Play(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void Use(GameEngine g, string activation, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, activation, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void BoundMove(GameEngine g, string activation, IReadOnlyList<int> ids)
    {
        Use(g, activation, activation == "gift-two" ? [2] : []);
        foreach (var id in ids) { Reach(g, p => p.SkillPrompt?.SkillId == Driver && p.Choices.Any(c => c.Cards.SequenceEqual([id]))); Answer(g, c => c.Cards.SequenceEqual([id])); }
    }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    {
        for (var i = 0; i < 120; i++) { var p = P(g); if (p is not null && predicate(p)) return; Advance(g); }
        throw new InvalidOperationException("Fixed Ling Tong fixture did not reach its boundary: " + JsonSerializer.Serialize(P(g)));
    }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0 } && Activation(p, Skill)) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass")) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static void Cold(GameEngine g, ContentRegistry r) => Require(State(g) == State(GameReplay.Restore(
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)),
        "Four private views, frozen owner-batch scalars, original physical costs, current candidate/instance and typed child returns cold-restore exactly.");
    private static void Reject(GameEngine g)
    { var p = P(g)!; var state = State(g); Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("unpublished"), g.Revision)).Accepted && state == State(g), "Unpublished input changes neither original costs, candidate, follow-up discards, private views nor command history."); }
    private static void Private(GameEngine g)
    {
        var p = P(g)!;
        Require(p.PlayerSeat == 0 && p.IsPrivate && p.Choices.Where(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)).All(c => c.Cards.Count == 0),
            "Other hands are genuine opaque current slots; only public equipment identities enter the chooser's choices.");
        for (var seat = 1; seat < 4; seat++) Require(g.CreateSnapshot(seat).PendingDecision is null, "Unrelated views do not receive the chooser's private target-card choices.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Create(bool observers = false, bool armor = false, bool sourceLoss = false, bool conversion = false, bool native = false, bool classicOnly = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(observers, armor, sourceLoss, conversion, native, classicOnly));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, ModeId = Mode, HumanSeat = native ? -1 : 0,
            HumanRole = native ? null : Role.Lord, UseInteractiveSetup = true, UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false, MaxTurns = native ? 2 : 4 }, registry);
        Accept(game, new StartGameCommand());
        if (!native) { Reach(game, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.SelectGeneral); Accept(game, new SelectGeneralCommand(0, "fixture:lt-owner", game.Revision, P(game)!.PromptId)); }
        return (game, registry);
    }

    private sealed class Fixture(bool observers, bool armor, bool sourceLoss, bool conversion, bool native, bool classicOnly) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-ling-tong", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var terminal = sourceLoss ? ", {\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLiving\"},{\"op\":\"issueCurrentTurnNonLockedSkillSuppression\",\"target\":\"selectedTarget\"}" : "";
            string BoundActivation(string id, int count, bool gift = false) => $$"""
                {"id":"{{id}}","minCards":0,"maxCards":0,"minTargets":{{(gift ? 1 : 0)}},"maxTargets":{{(gift ? 1 : 0)}},"targetKind":"otherLiving","usesPerTurn":null,"effects":[
                {"op":"selectOwnedCards","target":"owner","minimumCards":{{count}},"maximumCards":{{count}},"zones":["hand","equipment"],"resultBind":"paid"},
                {"op":"moveBoundCards","target":"owner","sourceBind":"paid","destination":"{{(gift ? "selectedTargetHand" : "discardPile")}}","awaitMovementTriggers":{{(gift ? "false" : "true")}}}]}
                """;
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"{{Driver}}","revision":1,"activations":[{{BoundActivation("discard-one",1)}},{{BoundActivation("discard-two",2)}},{{BoundActivation("gift-two",2,true)}},
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]}]},
                {"id":"{{Movement}}","revision":1,"triggers":[{"id":"moved","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["{{ForeignReason}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}{{terminal}}]}]},
                {"id":"{{Hp}}","revision":1,"triggers":[{"id":"recovered","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}]}
                """;
            var labels = new Dictionary<string, object> {
                [Driver] = new { name = "真实付款", description = "固定实际区域选择、原子弃牌/赠牌及装备" },
                [Movement] = new { name = "移牌子窗口", description = "真实弃牌回返及可控来源失效", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Hp] = new { name = "回复子窗口", description = "实际白银狮子回复回返", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
            };
            var c = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = 3, skills = labels }));
            foreach (var id in c.Programs.Keys) b.AddSkill(new(id, id, "真实通用能力夹具") { Program = c.Programs[id], ProgramPresentation = c.Presentations[id] });
            foreach (var owner in new[] { true, false }) b.AddSkill(new(owner ? "fixture:lt-pick-owner" : "fixture:lt-pick-other", "固定选将", "正式角色评分")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => (role == Role.Lord) == owner ? 10000d : -10000d) });
            var ownerSkills = new List<string> { "fixture:lt-pick-owner" };
            if (!native) ownerSkills.Add(Driver); if (conversion) ownerSkills.Add("classic:fuhun");
            b.AddGeneral(new("fixture:lt-owner", "真实旋风来源", "supporter", classicOnly ? Classic : Skill, "wu", 4, ownerSkills.ToArray()));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:lt-other-{i}", "其他角色", "supporter", "fixture:lt-pick-other", "shu", 8,
                i == 1 && observers ? [Movement, Hp] : []));
            b.AddDeck(new("fixture:lt-deck", "固定实际装备实体", native ? 6 : 4, native ? 2 : 0, []) {
                PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard(armor ? "classic:silver-lion" : "standard:crossbow", Suit.Club, 7)).ToArray() });
            b.AddMode(new(Mode, "真实 owner batch 旋风", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:lt-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:lt-owner", "fixture:lt-other-1", "fixture:lt-other-2", "fixture:lt-other-3"]));
        }
    }
}
