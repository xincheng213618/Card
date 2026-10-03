using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryPangDeChecks
{
    private const string Skill = "boundary:jianchu";
    private const string Driver = "fixture:pd-driver";
    private const string Cost = "fixture:pd-cost";
    private const string Gain = "fixture:pd-gain";
    private const string Mode = "identity:classic-boundary-pang-de-fixture";

    public static void BasicDiscardClaimsExactEntitiesAndStillResolvesOriginalSlash()
    {
        var (game, registry) = Create(observers: true); var hp = game.State.Players[1].Hp;
        var slash = Play(game, [1]); Activate(game); var choice = P(game)!.Choices.First(c => c.Parameters.GetValueOrDefault("source-zone") == "Hand");
        Require(choice.Cards.Count == 0 && choice.Parameters.ContainsKey("slot-index"),
            "The acting source chooses opaque hand slots without reading a target's hidden card kind or identity.");
        Private(game); Cold(game, registry); Reject(game); Answer(game, c => c.Id == choice.Id);
        Reach(game, p => p.SkillPrompt?.SkillId == Cost);
        Require(Claims(game).Length == 0 && game.State.Players[1].Hp == hp,
            "The exact target discard and its movement child complete before the original-use entity is obtained or damage applies.");
        Cold(game, registry); Continue(game); Reach(game, p => p.SkillPrompt?.SkillId == Gain);
        var owner = game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CurrentUsePhysicalClaims is not null);
        var receipt = owner.CurrentUsePhysicalClaims!.Single();
        Require(receipt.CardIds.SequenceEqual([slash]) && receipt.ActionId == owner.Action!.ActionId &&
            receipt.ActorSeat == 0 && receipt.RecipientSeat == 1 && receipt.CardUseFrameId == owner.Id &&
            game.CreateSnapshot(1).Players[1].Hand.Any(c => c.Id == slash) && game.State.Players[1].Hp == hp,
            "The still-resolving use owns the exact physical Slash receipt before the recipient's gain observer and original damage.");
        Frozen(receipt.CardIds); Frozen(Claims(game).Single().Claim.CardIds); FreezeDetached(receipt);
        Cold(game, registry); Continue(game); Finish(game);
        Require(game.State.Players[1].Hp == hp - 1 && Claims(game).Length == 1 &&
            game.CardMovements.Count(m => m.CardId == slash && m.To == CardLocation.Hand(1)) == 1 &&
            !game.CardMovements.Any(m => m.CardId == slash && m.Reason == CardMoveReasons.UseFinished),
            "Obtaining the actual Slash never cancels it or repays its final cleanup; the original target still takes one damage.");
        Cold(game, registry);

        var (two, tr) = Create(observers: true); var materials = two.CreateSnapshot(0).Players[0].Hand.Take(2).Select(c => c.Id).ToArray();
        Accept(two, new UseProgramSkillCommand(0, "classic:fuhun", "two-hand-cards-as-slash", materials, [1], two.Revision, P(two)!.PromptId));
        Activate(two); DiscardHand(two); Reach(two, p => p.SkillPrompt?.SkillId == Cost); Continue(two);
        Reach(two, p => p.SkillPrompt?.SkillId == Gain);
        Require(Claims(two).Single().Claim.CardIds.Order().SequenceEqual(materials.Order()) &&
            two.CreateSnapshot(1).Players[1].Hand.Where(c => materials.Contains(c.Id)).Count() == 2,
            "A real two-material conversion obtains both frozen actual costs rather than a fabricated Slash entity.");
        Cold(two, tr); Continue(two); Finish(two);
        Require(two.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 1) &&
            materials.All(id => !two.CardMovements.Any(m => m.CardId == id && m.Reason == CardMoveReasons.UseFinished)),
            "The original converted use survives the gain child and skips already claimed material cleanup."); Cold(two, tr);
    }

    public static void NonBasicDiscardUsesOneTargetReceiptAndAddsRealTurnAllowance()
    {
        var (game, registry) = Create(tricks: true, armor: "standard:bagua");
        Use(game, "equip", targets: [1]); Finish(game); var hp = game.State.Players[1].Hp;
        var slash = Play(game, [1]); Activate(game); DiscardHand(game); Finish(game);
        Require(Claims(game).Length == 0 && Receipts(game).Single() is { PreventCancellation: true, TargetSeat: 1 } &&
            game.State.Players[1].Hp == hp - 1 &&
            game.Events.Select(e => e.Payload).OfType<TurnRuleModifierGrantedEvent>().Single(e => e.Modifier.Source.SkillId == Skill).Modifier.Amount == 1 &&
            game.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "A real non-basic target discard retains the original entity, prevents only this target cancelling this Slash and grants one actual extra use.");
        var slashAction = game.Events.Select(e => e.Payload).OfType<CardActionAcceptedEvent>()
            .Single(e => e.Action.Type == CardActionType.Use && e.Action.ActorSeat == 0 &&
                e.Action.PhysicalCards.Any(c => c.CardId == slash)).Action;
        Require(game.Events.Select(e => e.Payload).OfType<JudgmentResolvedEvent>().Any(e =>
                e.TargetSeat == 1 && e.Reason == JudgmentReasons.BaguaDefense && e.Succeeded &&
                e.Suit is Suit.Heart or Suit.Diamond && e.ParentResolutionId == Receipts(game).Single().CardUseFrameId) &&
            game.Events.Select(e => e.Payload).OfType<CardActionAcceptedEvent>().Any(e =>
                e.Action.Type == CardActionType.Response && e.Action.ActorSeat == 1 &&
                e.Action.EffectiveKind == CardKind.Dodge && e.Action.PhysicalCards.Count == 0 &&
                e.Action.ParentActionId == slashAction.ActionId),
            "The real red Bagua judgment succeeds and produces an exact zero-entity Dodge response to this Slash, which still cannot cancel its original damage.");
        Cold(game, registry); Play(game, [2]); Reach(game, p => IsActivation(p)); Skip(game); Finish(game);
        Require(Receipts(game).Length == 1 && !game.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "The independently granted +1 permits a second use, and no prohibition is issued to the later target when this trigger is declined.");
        Cold(game, registry);

        var (armorGame, ar) = Create(tricks: true, armor: "classic:tengjia"); Use(armorGame, "equip", targets: [1]); Finish(armorGame);
        var before = armorGame.State.Players[1].Hp; Play(armorGame, [1]); Activate(armorGame); DiscardHand(armorGame); Finish(armorGame);
        Require(Receipts(armorGame).Length == 1 && armorGame.State.Players[1].Hp == before,
            "Preventing Dodge cancellation does not bypass Tengjia's independent normal-Slash nullification."); Cold(armorGame, ar);

        var (lion, lr) = Create(tricks: true, armor: "classic:silver-lion", observers: true);
        Use(lion, "hurt", targets: [1]); Finish(lion); Use(lion, "equip", targets: [1]); Finish(lion);
        var armor = lion.CreateSnapshot(0).Players[1].Equipment.Single(c => c.Kind == CardKind.SilverLion).Id;
        var injured = lion.State.Players[1].Hp; Play(lion, [1]); Activate(lion);
        Answer(lion, c => c.Cards.SequenceEqual([armor])); Reach(lion, p => p.SkillPrompt?.SkillId == Cost);
        Require(lion.State.Players[1].Hp == injured + 1 && Receipts(lion).Length == 0 &&
            lion.Events.Select(e => e.Payload).OfType<SilverLionRemovedRecoveryEvent>().Count(e => e.PlayerSeat == 1) == 1,
            "The selected equipped Silver Lion is discarded once and its actual recovery finishes before the post-payment target receipt.");
        Cold(lion, lr); Continue(lion); Finish(lion);
        Require(Receipts(lion).Length == 1 && lion.CardMovements.Count(m => m.CardId == armor && m.From == CardLocation.Equipment(1)) == 1,
            "The paid equipment child returns to one frozen category branch without repaying the public cost."); Cold(lion, lr);
    }

    public static void MultipleTargetsVirtualUseAndSourceLossKeepTheirOwningUses()
    {
        var (multi, mr) = Create(); Use(multi, "two-targets"); Finish(multi); var hp1 = multi.State.Players[1].Hp; var hp2 = multi.State.Players[2].Hp;
        var slash = Play(multi, [1, 2]); Activate(multi); DiscardHand(multi);
        Reach(multi, IsActivation); Require(Claims(multi).Single().Claim.CardIds.SequenceEqual([slash]),
            "The first final target's basic-card branch claims the original physical entity while the second target offer is still pending.");
        Cold(multi, mr); Activate(multi); DiscardHand(multi); Finish(multi);
        Require(Claims(multi).Length == 1 && multi.State.Players[1].Hp == hp1 - 1 && multi.State.Players[2].Hp == hp2 - 1 &&
            multi.CardMovements.Count(m => m.CardId == slash && m.Reason.Value == "skill-program.current-use-physical-claim") == 1,
            "Each final target independently pays a discard, while the later target cannot obtain the already claimed use entity again."); Cold(multi, mr);

        var (virtualGame, vr) = Create();
        Accept(virtualGame, new UseProgramSkillCommand(0, "boundary:rende", "give", virtualGame.CreateSnapshot(0).Players[0].Hand.Take(2).Select(c => c.Id).ToArray(), [1], virtualGame.Revision, P(virtualGame)!.PromptId));
        Reach(virtualGame, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "virtual-basic"));
        Answer(virtualGame, c => c.Parameters.GetValueOrDefault("basic-option")?.StartsWith("Slash:", StringComparison.Ordinal) == true && c.Targets.SequenceEqual([1]));
        Activate(virtualGame); DiscardHand(virtualGame); Finish(virtualGame);
        Require(Claims(virtualGame).Length == 0 && virtualGame.Events.Select(e => e.Payload).OfType<CardActionAcceptedEvent>()
            .Any(e => e.Action.EffectiveKind == CardKind.Slash && e.Action.PhysicalCards.Count == 0) &&
            virtualGame.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 1),
            "A true zero-entity Slash still offers and pays the target discard, obtains no fabricated card and resolves its original damage."); Cold(virtualGame, vr);

        var (lost, rr) = Create(loseSource: true); var card = Play(lost, [1]); Activate(lost); DiscardHand(lost);
        Reach(lost, p => p.SkillPrompt?.SkillId == "fixture:pd-loss" && p.Choices.Any(c => c.Targets.SequenceEqual([0])));
        Require(Claims(lost).Length == 0 && Receipts(lost).Length == 0 &&
            lost.CardMovements.Count(m => m.Reason.Value == $"skill-program.{Skill}.SelectAndMoveOwnedCard") == 1,
            "The real paid target discard is frozen before its participant chooses to suppress the original issuing source.");
        Cold(lost, rr); Answer(lost, c => c.Targets.SequenceEqual([0]));
        for (var step = 0; step < 150 && !lost.CardMovements.Any(m => m.CardId == card && m.Reason == CardMoveReasons.UseFinished); step++) Advance(lost);
        Require(Claims(lost).Length == 0 && Receipts(lost).Length == 0 &&
            lost.Events.Select(e => e.Payload).OfType<CurrentTurnNonLockedSkillSuppressionIssuedEvent>()
                .Single().Suppression is { TargetSeat: 0, Source: { OwnerSeat: 1 } } &&
            !lost.Events.Select(e => e.Payload).OfType<TurnRuleModifierGrantedEvent>().Any(e => e.Modifier.Source.SkillId == Skill) &&
            lost.CardMovements.Count(m => m.Reason.Value == $"skill-program.{Skill}.SelectAndMoveOwnedCard") == 1 &&
            lost.CardMovements.Any(m => m.CardId == card && m.Reason == CardMoveReasons.UseFinished),
            "A real movement child makes the issuing nonlocked instance unqualified after one paid discard; remaining benefits cancel and the original use cleans up normally."); Cold(lost, rr);

        var (borrowed, br) = Create(armor: "classic:zhangba-serpent-spear", observers: true, borrowed: true);
        Use(borrowed, "equip", targets: [0]); Finish(borrowed);
        Accept(borrowed, new EndPlayPhaseCommand(0, borrowed.Revision, P(borrowed)!.PromptId));
        Reach(borrowed, p => p.Kind == DecisionKind.RespondSlash && p.PlayerSeat == 0 &&
            p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "borrowed-sword-give-weapon"));
        var outerCard = borrowed.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.BorrowedSword).CardId;
        var forced = P(borrowed)!.Choices.First(c => c.Parameters.GetValueOrDefault("response") == "zhangba-slash");
        var costs = forced.Cards.ToArray(); Cold(borrowed, br); Answer(borrowed, c => c.Id == forced.Id); Activate(borrowed);
        var target = borrowed.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Skill).WindowContext!.TargetSeat!.Value;
        var basicSlot = borrowed.CreateSnapshot(target).Players[target].Hand.Select((c, i) => (Card: c, Index: i))
            .First(item => CardCatalog.Get(item.Card.Kind).CategoryName == "基本牌").Index;
        Answer(borrowed, c => c.Parameters.GetValueOrDefault("source-zone") == "Hand" &&
            c.Parameters.GetValueOrDefault("slot-index") == basicSlot.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Reach(borrowed, p => p.SkillPrompt?.SkillId == Cost); Continue(borrowed); Reach(borrowed, p => p.SkillPrompt?.SkillId == Gain);
        Require(Claims(borrowed).Single().Claim.CardIds.Order().SequenceEqual(costs.Order()) &&
            !Claims(borrowed).Single().Claim.CardIds.Contains(outerCard) &&
            borrowed.ResolutionStack.OfType<CardUseFrame>().Any(f => f.CardId == outerCard && f.CardKind == CardKind.BorrowedSword) &&
            borrowed.CardMovements.Last(m => m.CardId == outerCard).To == CardLocation.Processing,
            "The inner forced two-material Slash claims only its own costs while the outer Borrowed Sword remains in its separate live Processing owner.");
        Cold(borrowed, br); Continue(borrowed);
        for (var step = 0; step < 100 && borrowed.ResolutionStack.OfType<CardUseFrame>().Any(f => f.CardId == outerCard); step++) Advance(borrowed);
        Require(!borrowed.ResolutionStack.OfType<CardUseFrame>().Any(f => f.CardId == outerCard) &&
            borrowed.CardMovements.Any(m => m.CardId == outerCard && m.Reason == CardMoveReasons.UseFinished) &&
            costs.All(id => !borrowed.CardMovements.Any(m => m.CardId == id && m.Reason == CardMoveReasons.UseFinished)),
            "The exact typed inner return preserves claimed entities and allows the original outer Borrowed Sword to perform its own single final cleanup."); Cold(borrowed, br);
    }

    public static void NativeAiUsesPublicCategoryPriorAndPaysRealCosts()
    {
        var (game, registry) = Create(native: true, tricks: true);
        for (var step = 0; step < 180 && game.State.Status != EngineStatus.Completed &&
            (Receipts(game).Length == 0 || !game.Events.Select(e => e.Payload).OfType<TurnRuleModifierGrantedEvent>().Any(e => e.Modifier.Source.SkillId == Skill)); step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Require(Receipts(game).Length > 0 && game.CardMovements.Any(m => m.Reason.Value == $"skill-program.{Skill}.SelectAndMoveOwnedCard") &&
            game.Events.Select(e => e.Payload).OfType<TurnRuleModifierGrantedEvent>().Any(e => e.Modifier.Source.SkillId == Skill),
            "Native unattended AI activates the publicly useful category branch, chooses a real opaque cost and issues legal current-target and actual-turn receipts."); Cold(game, registry);
    }

    private static ProgramCurrentUsePhysicalCardsClaimedEvent[] Claims(GameEngine g) => g.Events.Select(e => e.Payload).OfType<ProgramCurrentUsePhysicalCardsClaimedEvent>().ToArray();
    private static ProgramTargetSlashReceipt[] Receipts(GameEngine g) => g.Events.Select(e => e.Payload).OfType<ProgramTargetSlashReceiptIssuedEvent>().Where(e => e.Receipt.SkillId == Skill).Select(e => e.Receipt).ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool IsActivation(PendingDecision p) => p.SkillPrompt?.SkillId == Skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Activate(GameEngine g) { Reach(g, IsActivation); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); }
    private static void Skip(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void DiscardHand(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("source-zone") == "Hand");
    private static int Play(GameEngine g, IReadOnlyList<int> targets)
    {
        Finish(g); var action = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual(targets));
        Accept(g, new PlayCardCommand(0, action.CardId!.Value, targets, g.Revision, P(g)!.PromptId, action.PlayedCardKind) { ConversionSource = action.ConversionSource });
        return action.CardId.Value;
    }
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Finish(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 150; step++) { var p = P(g); if (p is not null && predicate(p)) return; Advance(g); }
        throw new InvalidOperationException("Fixed Pang De command prefix did not reach its boundary: " + JsonSerializer.Serialize(P(g)));
    }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p?.SkillPrompt?.SkillId is Cost or Gain) Continue(g);
        else if (p is { Kind: DecisionKind.ProgramTrigger } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Skip(g);
        else if (p is { Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(p.PlayerSeat, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Require(r.Accepted, r.Error?.Message ?? "Rejected real command."); }
    private static void Private(GameEngine g) { Require(P(g)!.PlayerSeat == 0 && P(g)!.IsPrivate, "The exact opponent-zone payment belongs to its actual chooser."); for (var v = 1; v < 4; v++) Require(g.CreateSnapshot(v).PendingDecision is null, "Other views receive no opaque hand-slot choice."); }
    private static void Reject(GameEngine g) { var before = State(g); var p = P(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("unpublished"), g.Revision)).Accepted && State(g) == before, "An unpublished payment is atomically rejected."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static void Cold(GameEngine g, ContentRegistry r) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)), "All four private views, owning use receipts, exact paid entities, journal and commands cold-restore identically.");
    private static void Frozen(IReadOnlyList<int> values) { Require(values is IList<int> { IsReadOnly: true }, "The public exact-entity collection is frozen."); try { ((IList<int>)values).Add(-1); throw new InvalidOperationException("An exposed collection allowed mutation."); } catch (NotSupportedException) { } }
    private static void FreezeDetached(ProgramCurrentUsePhysicalClaim receipt)
    {
        var mutable = receipt.CardIds.ToArray(); var original = new ProgramCurrentUsePhysicalCardsClaimedEvent(receipt with { CardIds = mutable });
        var frozen = (ProgramCurrentUsePhysicalCardsClaimedEvent)typeof(GameEngine).Assembly.GetType("CardGame.Core.CommittedEventProjection")!.GetMethod("Freeze", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [original])!;
        mutable[0] = -1; Require(frozen.Claim.CardIds[0] == receipt.CardIds[0], "Commit preparation detaches the exact nested claim entity list from its producer."); Frozen(frozen.Claim.CardIds);
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Create(bool tricks = false, string? armor = null, bool observers = false, bool loseSource = false, bool native = false, bool borrowed = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(tricks, armor, observers, loseSource, native, borrowed));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = native ? -1 : 0, HumanRole = native ? null : Role.Lord, ModeId = Mode, UseInteractiveSetup = !native, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        Accept(game, new StartGameCommand());
        if (!native) { Reach(game, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0); Accept(game, new SelectGeneralCommand(0, "fixture:pd-owner", game.Revision, P(game)!.PromptId)); Finish(game); }
        return (game, registry);
    }
    private sealed class Fixture(bool tricks, string? armor, bool observers, bool loseSource, bool native, bool borrowed) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-pd", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
             {"id":"{{Driver}}","revision":1,"activations":[
              {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipment"}]},
              {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
              {"id":"two-targets","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"cardTargetCount","ruleOperation":"add","amount":1,"cardKinds":["slash","fireSlash","thunderSlash"]}]}]},
             {"id":"fixture:pd-quiet","revision":1,"triggers":[{"id":"quiet","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":-20}]}]},
             {"id":"{{Cost}}","revision":1,"triggers":[{"id":"paid","window":"discardPileReceived","subject":"owner","optional":false,"movementReasons":["skill-program.{{Skill}}.SelectAndMoveOwnedCard"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"cost-seen","options":[{"id":"continue"}]}]}]},
             {"id":"{{Gain}}","revision":1,"triggers":[{"id":"claimed","window":"cardsGained","subject":"owner","optional":false,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.current-use-physical-claim"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain-seen","options":[{"id":"continue"}]}]}]},
             {"id":"fixture:pd-loss","revision":1,"triggers":[{"id":"suppress-source","window":"discardPileReceived","subject":"owner","discardOwnerScope":"own","optional":false,"movementReasons":["skill-program.{{Skill}}.SelectAndMoveOwnedCard"],"effects":[{"op":"selectTarget","target":"owner","targetKind":"anyLiving"},{"op":"issueCurrentTurnNonLockedSkillSuppression","target":"selectedTarget"}]}]}]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object> {
                [Driver] = new { name = "真实准备", description = "真实装备与目标上限准备" }, ["fixture:pd-quiet"] = new { name = "固定目标", description = "固定轻量回合" },
                [Cost] = new { name = "实际弃牌子链", description = "支付后的冻结窗口", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Gain] = new { name = "实体取得子链", description = "领取后原杀继续", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                ["fixture:pd-loss"] = new { name = "实际来源失效", description = "支付后的参与者抑制来源非锁定技" } } }));
            foreach (var id in catalog.Programs.Keys) b.AddSkill(new(id, id, "真实准备程序") { Program = catalog.Programs[id] });
            b.AddSkill(new("fixture:pd-selection", "固定选将", "无运行技能") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            b.AddGeneral(new("fixture:pd-owner", "鞬出真实命令", "supporter", Skill, "qun", 6,
                (native ? new[] { "classic:mashu" } : new[] { "classic:mashu", "classic:wusheng", "classic:fuhun", "boundary:rende", Driver })
                    .Concat(observers ? [Cost] : Array.Empty<string>()).ToArray()));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:pd-target-{i}", "固定目标", "supporter", native ? Skill : "fixture:pd-selection", "wei", 8,
                (native ? new[] { "classic:mashu" } : observers ? new[] { "fixture:pd-quiet", Gain } : new[] { "fixture:pd-quiet" })
                    .Concat(loseSource && i == 1 ? ["fixture:pd-loss"] : Array.Empty<string>()).ToArray()));
            b.AddDeck(new("fixture:pd-deck", "固定实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, native ? 32 : 80).Select(i => new ContentDeckPhysicalCard(native ? i % 3 == 0 ? "standard:slash" : "standard:duel" : borrowed && i % 4 == 0 ? "classic:borrowed-sword" : tricks ? "standard:duel" : "standard:slash", Suit.Heart, i % 13 + 1))
                .Concat(armor is null ? [] : Enumerable.Range(0, 4).Select(i => new ContentDeckPhysicalCard(armor, Suit.Heart, 6))).ToArray() });
            b.AddMode(new(Mode, "真实鞬出", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Loyalist)] = 1 }, "fixture:pd-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:pd-owner", "fixture:pd-target-1", "fixture:pd-target-2", "fixture:pd-target-3"]));
        }
    }
}
