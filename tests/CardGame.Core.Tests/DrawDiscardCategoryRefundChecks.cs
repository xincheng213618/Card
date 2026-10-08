using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DrawDiscardCategoryRefundChecks
{
    private const string Skill = "fixture:category-refund";
    private const string Activation = "three-real-categories";
    private const string StateId = "fixture:category-refund-targets";
    private const string Driver = "fixture:category-refund-driver";
    private const string Watch = "fixture:category-refund-watch";
    private const string EquipmentChild = "fixture:category-refund-equipment-child";
    private const string Suppressor = "fixture:category-refund-suppressor";
    private const string Restrict = "fixture:category-refund-restrict";
    private const string Mode = "fixture:draw-discard-category-refund";
    private static string Reason(string part) => $"skill-program.{Skill}.draw-discard-category.{part}";

    public static void DistinctPaymentRefundsPhaseOnlyAndBansOriginalActualTurnTarget()
    {
        var registry = Registry(); var g = Start(registry); CollectInitialHands(g);
        var offered = g.GetHumanLegalActions().Single(a => a.ProgramSkillId == Skill);
        Require(offered.MinCardCount == 0 && offered.MaxCardCount == 0 && offered.SelectableTargetSeats.Contains(0),
            "The real phase-limited activation requires no material and includes its own living owner.");
        Activate(g, 0); Reach(g, p => p.SkillPrompt?.SkillId == Watch);
        var frameId = Root(g).Id; var source = Root(g).DrawDiscardCategoryRefund!.Source;
        Require(Root(g).DrawDiscardCategoryRefund is { Stage: DrawDiscardCategoryStage.Drawing, DrawIssued: true, DrawActual: 3, DiscardIssued: false },
            "The accepted owner frame pays Draw3 once and waits for its real gained-card child before freezing the discard pool.");
        PrivateAndFrozen(g); g = Cold(g, registry); Continue(g); Reach(g, IsCategoryChoice);
        var draft = Root(g).DrawDiscardCategoryRefund!;
        Require(draft.Source == source && draft.TargetSeat == 0 && draft.RequiredCount == 3 &&
            draft.EligibleMaterials.Select(m => m.Category).Distinct().Count() == 3 && !draft.DiscardIssued,
            "The same accepted source freezes actual legal HE entities only after its draw children return.");
        Frozen(draft.EligibleMaterials); PrivateAndFrozen(g);
        var ids = new[] { SkillProgramCardCategory.Basic, SkillProgramCardCategory.Trick, SkillProgramCardCategory.Equipment }
            .Select(category => draft.EligibleMaterials.First(m => m.Category == category).CardId).ToArray();
        var oldPrompt = P(g)!; var oldChoice = oldPrompt.Choices.Single(c => c.Cards.SequenceEqual([ids[0]]));
        Select(g, ids[0]);
        Require(Root(g).DrawDiscardCategoryRefund!.SelectedCardIds.SequenceEqual([ids[0]]) &&
            !E<DrawDiscardCategoryDiscardPaidEvent>(g).Any(), "Partial mandatory selection moves no entity and issues no payment receipt.");
        Frozen(Root(g).DrawDiscardCategoryRefund!.SelectedCardIds);
        Reject(g, new AnswerPromptCommand(0, oldPrompt.PromptId, oldChoice.Id, g.Revision));
        g = Cold(g, registry); Select(g, ids[1]); Select(g, ids[2]); Reach(g, p => p.SkillPrompt?.SkillId == Watch);
        var paid = Root(g).DrawDiscardCategoryRefund!;
        Require(paid.Stage == DrawDiscardCategoryStage.DiscardChildren && paid.DistinctNonEmpty && !paid.BonusIssued &&
            !paid.QuotaRefunded && !paid.TargetBanned, "The complete distinct payment waits for its actual movement child before bonus or quota refund.");
        AssertPayment(g, frameId, ids, true); Frozen(paid.ActualDiscards);
        g = Cold(g, registry); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Watch);
        Require(Root(g).DrawDiscardCategoryRefund is { Stage: DrawDiscardCategoryStage.BonusChildren, BonusIssued: true, BonusActual: 1, QuotaRefunded: false },
            "Exactly one real bonus card is paid before its own gain child, while the original phase debit remains outstanding.");
        PrivateAndFrozen(g); g = Cold(g, registry); Continue(g); ReachPlay(g);
        AssertDraw(g, frameId, false, 3); AssertDraw(g, frameId, true, 1); AssertSuccess(g, frameId, 0);
        var refund = E<DrawDiscardCategoryRefundedEvent>(g).Single(e => e.FrameId == frameId);
        Require(refund.BeforeUsage == 1 && refund.AfterUsage == 0 && refund.ActivationId == Activation &&
            E<ProgramSkillStartedEvent>(g).Count(e => e.SkillId == Skill) == 1,
            "Refund removes one phase quota debit without erasing the same actual activation from the game's started-skill history.");
        var remaining = g.GetHumanLegalActions().Single(a => a.ProgramSkillId == Skill);
        Require(!remaining.SelectableTargetSeats.Contains(0) && remaining.SelectableTargetSeats.Contains(1),
            "The refunded phase can activate again, but its exact original target is banned for the current actual turn.");
        Reject(g, new UseProgramSkillCommand(0, Skill, Activation, [], [0], g.Revision, P(g)!.PromptId));
        Activate(g, 1); ReachPlay(g);
        Require(E<ProgramSkillStartedEvent>(g).Count(e => e.SkillId == Skill) == 2 &&
            E<DrawDiscardCategoryStartedEvent>(g).Count(e => e.Source.SkillId == Skill) == 2 &&
            E<DrawDiscardCategoryCompletedEvent>(g).Length == 2 &&
            g.AcceptedCommands.OfType<AnswerPromptCommand>().All(c => c.ActorSeat == 0),
            "A second genuine activation targets another living participant and the native AI pays its own mandatory private choices; both real activations remain counted.");
        var turn = g.State.TurnNumber;
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && g.State.TurnNumber > turn);
        Require(g.GetHumanLegalActions().Single(a => a.ProgramSkillId == Skill).SelectableTargetSeats.Contains(0),
            "The exact target ban expires on a later real turn without replacing or reacquiring the skill.");
        _ = Cold(g, registry);
    }

    public static void RepeatedCategoriesAndSmallActualPayments()
    {
        var repeatedRegistry = Registry(singleCategory: true); var repeated = Start(repeatedRegistry);
        Activate(repeated, 0); Reach(repeated, IsCategoryChoice);
        var repeatedFrame = Root(repeated).Id;
        var repeatedIds = Root(repeated).DrawDiscardCategoryRefund!.EligibleMaterials.Take(3).Select(m => m.CardId).ToArray();
        foreach (var id in repeatedIds) Select(repeated, id);
        ReachPlay(repeated); AssertPayment(repeated, repeatedFrame, repeatedIds, false);
        Require(E<DrawDiscardCategoryCompletedEvent>(repeated).Single(e => e.FrameId == repeatedFrame) is
                { DistinctNonEmpty: false, BonusIssued: false, QuotaRefunded: false, TargetBanned: false } &&
            !E<DrawDiscardCategoryDrawIssuedEvent>(repeated).Any(e => e.Bonus) &&
            !E<DrawDiscardCategoryRefundedEvent>(repeated).Any() && !E<DrawDiscardCategoryTargetBannedEvent>(repeated).Any() &&
            !repeated.GetHumanLegalActions().Any(a => a.ProgramSkillId == Skill),
            "Three genuine Basic discards pay once but earn neither bonus nor refund nor target ban; the original phase debit stays spent.");
        _ = Cold(repeated, repeatedRegistry);

        var tinyRegistry = Registry(tiny: true);
        foreach (var count in new[] { 0, 1, 2 })
        {
            var g = Start(tinyRegistry); CollectInitialHands(g);
            Require(g.CreateSnapshot(0).Players[0].Hand.Count == 16 && g.State.DrawPileCount == 0 && g.State.DiscardPileCount == 0,
                "The fixed sixteen-card deck is genuinely dealt and collected, with no recycled discard material available to draw.");
            var keep = new[] { SkillProgramCardCategory.Basic, SkillProgramCardCategory.Trick }.Take(count)
                .Select(category => g.CreateSnapshot(0).Players[0].Hand.First(c => Category(c.Kind) == category).Id).ToArray();
            var transfer = g.CreateSnapshot(0).Players[0].Hand.Select(c => c.Id).Except(keep).ToArray();
            UseDriver(g, "stash", [1]); Reach(g, p => p.SkillPrompt?.SkillId == Driver && Action(p, "select-owned-cards"));
            foreach (var id in transfer) Answer(g, c => c.Cards.SequenceEqual([id]));
            if (count > 0) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards");
            ReachPlay(g);
            Require(g.CreateSnapshot(0).Players[0].Hand.Select(c => c.Id).Order().SequenceEqual(keep.Order()) &&
                g.State.DrawPileCount == 0 && g.State.DiscardPileCount == 0,
                "Actual selection and a real non-discard gift retain exactly the intended zero, one or two original entities.");
            Activate(g, 0);
            if (count > 0)
            {
                Reach(g, IsCategoryChoice);
                Require(Root(g).DrawDiscardCategoryRefund is { DrawActual: 0 } r && r.RequiredCount == count &&
                    r.EligibleMaterials.Select(m => m.CardId).Order().SequenceEqual(keep.Order()),
                    "A real exhausted draw does not invent cards; mandatory payment shrinks to the actual remaining legal HE count.");
                foreach (var id in keep) Select(g, id);
            }
            ReachPlay(g);
            var frame = E<DrawDiscardCategoryStartedEvent>(g).Single().FrameId;
            AssertDraw(g, frame, false, 0); AssertPayment(g, frame, keep, count > 0);
            if (count > 0)
            {
                AssertDraw(g, frame, true, 1); AssertSuccess(g, frame, 0);
                Require(E<DrawDiscardCategoryRefundedEvent>(g).Single().BeforeUsage == 1 &&
                    E<DrawDiscardCategoryRefundedEvent>(g).Single().AfterUsage == 0,
                    "One or two actual nonempty, pairwise different categories earn the original bonus and one quota refund.");
            }
            else Require(E<DrawDiscardCategoryCompletedEvent>(g).Single() is
                    { DistinctNonEmpty: false, BonusIssued: false, QuotaRefunded: false, TargetBanned: false } &&
                    !E<DrawDiscardCategoryDrawIssuedEvent>(g).Any(e => e.Bonus) &&
                    !g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Skill),
                    "An empty actual discard is not vacuously distinct and earns no bonus, refund or target restriction.");
            _ = Cold(g, tinyRegistry);
        }
    }

    public static void EquipmentBatchSettlesAfterAcquiredSourceQualificationLoss()
    {
        var registry = Registry(sourceLoss: true); var g = Start(registry); CollectInitialHands(g); EquipArmor(g);
        var originalOwner = g.CreateSnapshot(0).Players[0];
        var armor = originalOwner.Equipment.Single().Id;
        Require(originalOwner.Hp == 5 && originalOwner.Skills!.Any(s => s.Id == Skill),
            $"The small identity Lord remains at HP5 after installing Bagua, and the acquired source is qualified before payment. HP={originalOwner.Hp}/{originalOwner.MaxHp}, skills={string.Join(',', originalOwner.Skills!.Select(s => s.Id))}.");
        Activate(g, 0); Reach(g, IsCategoryChoice);
        var frameId = Root(g).Id; var receipt = Root(g).DrawDiscardCategoryRefund!;
        var basic = receipt.EligibleMaterials.First(m => m.Category == SkillProgramCardCategory.Basic).CardId;
        var trick = receipt.EligibleMaterials.First(m => m.Category == SkillProgramCardCategory.Trick).CardId;
        Require(receipt.EligibleMaterials.Any(m => m.CardId == armor && m.From == CardLocation.Equipment(0)),
            "A genuinely installed armor remains a legal self-discard even under the existing foreign-equipment protection policy.");
        Select(g, basic); Select(g, armor); Select(g, trick);
        Reach(g, p => p.SkillPrompt?.SkillId == EquipmentChild);
        var paid = Root(g).DrawDiscardCategoryRefund!; var observer = g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.SkillId == EquipmentChild);
        var batch = observer.WindowContext!.MovementBatch!;
        var paymentOwner = g.CreateSnapshot(0).Players[0];
        var sourceQualified = paymentOwner.Skills!.Any(s => s.Id == Skill);
        var suppressorQualified = paymentOwner.Skills!.Any(s => s.Id == Suppressor);
        Require(paid.Stage == DrawDiscardCategoryStage.DiscardChildren && paid.Source == receipt.Source &&
            batch.Id == paid.PaymentBatchId && batch.ParentFrameId == frameId && batch.Movements.Count == 3 &&
            batch.Movements.Select(m => m.CardId).SequenceEqual([basic, armor, trick]) &&
            batch.SourceCounts.Any(c => c.Location == CardLocation.Equipment(0) && c.CountBefore == 1 && c.CountAfter == 0) &&
            batch.SourceCounts.Any(c => c.Location == CardLocation.Hand(0) && c.CountBefore - c.CountAfter == 2) &&
            !E<SkillsAcquiredEvent>(g).Any(e => e.PlayerSeat == 0 && e.SourceSkillId == EquipmentChild && e.SkillIds.Contains(Suppressor)) &&
            paymentOwner.Hp == 5 && sourceQualified && !suppressorQualified &&
            !E<ProgramOwnerSkillsReplacedEvent>(g).Any(e => e.LostSkillIds.Contains(Skill)),
            $"One real mixed Hand/Equipment batch owns the exact observer and pauses before its source-suppressing grant; no grant or physical RemoveGrant is claimed before the real answer. HP={paymentOwner.Hp}/{paymentOwner.MaxHp}, sourceQualified={sourceQualified}, suppressorQualified={suppressorQualified}, skills={string.Join(',', paymentOwner.Skills!.Select(s => s.Id))}, stage={paid.Stage}, batch={batch.Id}/{paid.PaymentBatchId}.");
        AssertPayment(g, frameId, [basic, armor, trick], true); Frozen(batch.Movements); Frozen(batch.SourceCounts); PrivateAndFrozen(g);
        g = Cold(g, registry); Continue(g); ReachPlay(g);
        AssertSuccess(g, frameId, 0); AssertDraw(g, frameId, false, 3); AssertDraw(g, frameId, true, 1);
        var settledOwner = g.CreateSnapshot(0).Players[0];
        var sourceActions = g.GetHumanLegalActions().Count(a => a.ProgramSkillId == Skill);
        var suppressorGrants = g.Events.Where(e => e.Payload is SkillsAcquiredEvent a && a.PlayerSeat == 0 &&
            a.SourceSkillId == EquipmentChild && a.SkillIds.Contains(Suppressor)).ToArray();
        var paymentSequence = g.Events.Single(e => e.Payload is DrawDiscardCategoryDiscardPaidEvent p && p.FrameId == frameId).Sequence;
        var bonusSequence = g.Events.Single(e => e.Payload is DrawDiscardCategoryDrawIssuedEvent d && d.FrameId == frameId && d.Bonus).Sequence;
        Require(E<ProgramSkillResolvedEvent>(g).Any(e => e.FrameId == frameId && e.SkillId == Skill && e.Completed) &&
            E<DrawDiscardCategoryRefundedEvent>(g).Single().AfterUsage == 0 &&
            E<DrawDiscardCategoryTargetBannedEvent>(g).All(e => e.TargetSeat == 0) &&
            sourceActions == 0 && !settledOwner.Skills!.Any(s => s.Id == Skill) && settledOwner.Skills!.Any(s => s.Id == Suppressor) &&
            suppressorGrants is [var suppression] && paymentSequence < suppression.Sequence && suppression.Sequence < bonusSequence &&
            !E<ProgramOwnerSkillsReplacedEvent>(g).Any(e => e.LostSkillIds.Contains(Skill)) &&
            settledOwner.Hp == 5 && !g.ResolutionStack.Any(f => f.Id == frameId),
            $"After the equipment child actually grants its suppressor, the original paid source settles bonus/refund exactly once; other targets still lack source qualification despite free phase quota. HP={settledOwner.Hp}/{settledOwner.MaxHp}, sourceActions={sourceActions}, skills={string.Join(',', settledOwner.Skills!.Select(s => s.Id))}, grantSequences={string.Join(',', suppressorGrants.Select(e => e.Sequence))}, paymentSequence={paymentSequence}, bonusSequence={bonusSequence}, refunds={string.Join(',', E<DrawDiscardCategoryRefundedEvent>(g).Select(e => e.AfterUsage))}, sourceFramePresent={g.ResolutionStack.Any(f => f.Id == frameId)}.");
        _ = Cold(g, registry);
    }

    public static void ProtectedHandCategoryIsExcludedButOwnArmorRemainsPayable()
    {
        var registry = Registry(protection: true); var g = Start(registry); CollectInitialHands(g); EquipArmor(g);
        var armor = g.CreateSnapshot(0).Players[0].Equipment.Single().Id;
        UseDriver(g, "damage-self", []); Reach(g, p => p.SkillPrompt?.SkillId == Restrict && Action(p, "activate"));
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, p => p.SkillPrompt?.SkillId == Restrict && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "trick"));
        Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "trick"); ReachPlay(g);
        var protectedIds = g.CreateSnapshot(0).Players[0].Hand.Where(c => Category(c.Kind) == SkillProgramCardCategory.Trick).Select(c => c.Id).ToArray();
        Require(protectedIds.Length > 0 && E<HandCategoryRestrictionGrantedEvent>(g).Single().Restriction is
                { AffectedSeat: 0, Category: SkillProgramCardCategory.Trick } &&
            E<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 1),
            "A real damage-source declaration protects the actual owner's Trick hand materials during this actual turn.");
        Activate(g, 0); Reach(g, IsCategoryChoice);
        var receipt = Root(g).DrawDiscardCategoryRefund!; var frameId = Root(g).Id;
        Require(receipt.EligibleMaterials.All(m => m.Category != SkillProgramCardCategory.Trick) &&
            receipt.EligibleMaterials.Any(m => m.CardId == armor && m.From == CardLocation.Equipment(0)) &&
            P(g)!.Choices.All(c => !c.Cards.Any(protectedIds.Contains)),
            "Mandatory HE selection omits protected self-discard hand categories while retaining its own protected armor for genuine self-payment.");
        PrivateAndFrozen(g); g = Cold(g, registry);
        Reject(g, new AnswerPromptCommand(0, P(g)!.PromptId, new ChoiceId("unpublished-protected-hand"), g.Revision));
        var basics = receipt.EligibleMaterials.Where(m => m.Category == SkillProgramCardCategory.Basic).Take(2).Select(m => m.CardId).ToArray();
        Require(basics.Length == 2, "The fixed real mixed deck supplies two legal basic alternatives to the protected hand cards.");
        Select(g, armor); foreach (var id in basics) Select(g, id); ReachPlay(g);
        AssertPayment(g, frameId, [armor, .. basics], false);
        Require(protectedIds.All(id => g.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == id)) &&
            g.CardMovements.Count(m => m.CardId == armor && m.From == CardLocation.Equipment(0) &&
                m.To == CardLocation.DiscardPile && m.Reason.Value == Reason("discard")) == 1 &&
            E<DrawDiscardCategoryCompletedEvent>(g).Single() is { DistinctNonEmpty: false, QuotaRefunded: false },
            "The exact installed armor is paid once, protected original hand entities stay untouched, and repeated actual Basic categories do not earn refund.");
        _ = Cold(g, registry);
    }

    private static void AssertDraw(GameEngine g, long frame, bool bonus, int actual)
    {
        var fact = E<DrawDiscardCategoryDrawIssuedEvent>(g).Single(e => e.FrameId == frame && e.Bonus == bonus);
        var moves = g.CardMovements.Where(m => m.Sequence > fact.SequenceBefore && m.Sequence <= fact.SequenceAfter &&
            m.Reason.Value == Reason(bonus ? "bonus" : "draw")).ToArray();
        Require(fact.RequestedCount == (bonus ? 1 : 3) && fact.ActualCount == actual && moves.Length == actual &&
            moves.All(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(fact.TargetSeat)),
            "Each original or bonus draw invoice matches its exact real movement interval and target, with no replayed draw.");
    }
    private static void AssertPayment(GameEngine g, long frame, IReadOnlyList<int> ids, bool distinct)
    {
        var fact = E<DrawDiscardCategoryDiscardPaidEvent>(g).Single(e => e.FrameId == frame);
        var entities = E<DrawDiscardCategoryEntityDiscardedEvent>(g).Where(e => e.FrameId == frame).ToArray();
        Require(fact.RequiredCount == ids.Count && fact.ActualDiscardCount == ids.Count && fact.DistinctNonEmpty == distinct &&
            (fact.BatchId is not null) == (ids.Count > 0) && entities.Select(e => e.CardId).SequenceEqual(ids) &&
            entities.All(e => g.CardMovements.Count(m => m.Sequence == e.MovementSequence && m.CardId == e.CardId &&
                m.From == e.From && m.To == CardLocation.DiscardPile && m.Reason.Value == Reason("discard") &&
                m.Sequence > fact.SequenceBefore && m.Sequence <= fact.SequenceAfter && Category(m.CardKind) == e.Category) == 1),
            "One complete atomic payment exposes only the actual ordered public discards and their real movement sequences, never an uncommitted selected set.");
    }
    private static void AssertSuccess(GameEngine g, long frame, int target) => Require(
        E<DrawDiscardCategoryCompletedEvent>(g).Single(e => e.FrameId == frame) is
            { DistinctNonEmpty: true, BonusIssued: true, QuotaRefunded: true, TargetBanned: true, TargetAlive: true } &&
        E<DrawDiscardCategoryRefundedEvent>(g).Count(e => e.FrameId == frame && e.TargetSeat == target) == 1 &&
        E<DrawDiscardCategoryTargetBannedEvent>(g).Count(e => e.FrameId == frame && e.TargetSeat == target) == 1 &&
        E<ProgramSkillStartedEvent>(g).Count(e => e.FrameId == frame && e.SkillId == Skill) == 1,
        "A nonempty pairwise distinct actual discard awards one bonus, one exact phase refund and one original-target actual-turn ban without erasing the real activation.");
    private static SkillProgramCardCategory Category(CardKind kind) => EquipmentCatalog.IsEquipment(kind)
        ? SkillProgramCardCategory.Equipment : kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.Dodge or CardKind.Peach or CardKind.Alcohol
            ? SkillProgramCardCategory.Basic : SkillProgramCardCategory.Trick;
    private static ProgramSkillFrame Root(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.DrawDiscardCategoryRefund is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Action(PendingDecision p, string action) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action);
    private static bool IsCategoryChoice(PendingDecision p) => Action(p, "draw-discard-category");
    private static void Select(GameEngine g, int id) => Answer(g, c => c.Cards.SequenceEqual([id]));
    private static void Activate(GameEngine g, int target) => Accept(g, new UseProgramSkillCommand(0, Skill, Activation, [], [target], g.Revision, P(g)!.PromptId));
    private static void UseDriver(GameEngine g, string activation, IReadOnlyList<int> targets) => Accept(g, new UseProgramSkillCommand(0, Driver, activation, [], targets, g.Revision, P(g)!.PromptId));
    private static void CollectInitialHands(GameEngine g) { foreach (var seat in new[] { 1, 2, 3 }) { UseDriver(g, "collect", [seat]); ReachPlay(g); } }
    private static void EquipArmor(GameEngine g)
    {
        var armor = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip &&
            g.CreateSnapshot(0).Players[0].Hand.Single(c => c.Id == a.CardId).Kind == CardKind.BaguaFormation);
        Accept(g, new PlayCardCommand(0, armor.CardId!.Value, armor.TargetSeats, g.Revision, P(g)!.PromptId)); ReachPlay(g);
    }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> choose)
    {
        var p = P(g)!; Require(p.PlayerSeat == 0, "Only the actual human owner answers fixture choices; AI participants use native Advance.");
        Accept(g, new AnswerPromptCommand(0, p.PromptId, p.Choices.First(choose).Id, g.Revision));
    }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void ReachPlay(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine g, Func<PendingDecision, bool> stop)
    {
        for (var i = 0; i < 128; i++)
        {
            var p = P(g); if (p is not null && stop(p)) return;
            if (p is { PlayerSeat: 0 } && p.SkillPrompt?.SkillId is Watch or EquipmentChild) Continue(g);
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
            else
            {
                Require(p is not { PlayerSeat: 0 }, $"Unexpected human boundary: {p?.Kind}/{p?.SkillPrompt?.SkillId}.");
                Accept(g, new AdvanceOneStepCommand(g.Revision));
            }
        }
        throw new InvalidOperationException("The bounded fixed-seed draw/discard fixture did not reach its intended boundary.");
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "The actual command was rejected."); }
    private static void Reject(GameEngine g, GameCommand command)
    { var before = State(g); var result = g.Submit(command); Require(!result.Accepted && result.Error is not null && before == State(g), "An unpublished or stale command changes neither the original payment, accepted prefix, exact frames nor any private view."); }
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(State(copy) == State(g), "Cold accepted-command restoration preserves every private view, typed receipt, actual invoice, frame, fact and command."); return copy;
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack),
        Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static void PrivateAndFrozen(GameEngine g)
    {
        var before = State(g); var actor = P(g)!.PlayerSeat;
        foreach (var viewer in Enumerable.Range(0, 4))
        {
            var view = g.CreateSnapshot(viewer);
            Require(view.Players.Where(p => p.Seat != viewer).All(p => p.Hand.Count == 0) &&
                (viewer == actor ? view.PendingDecision is { IsPrivate: true } : view.PendingDecision is null),
                "Every viewer retains hand privacy and only the exact actual chooser receives its private decision.");
            if (viewer != actor) continue;
            Frozen(view.PendingDecision!.Choices); Frozen(view.PendingDecision.ValidCardIds); Frozen(view.PendingDecision.ValidTargetSeats);
            foreach (var choice in view.PendingDecision.Choices)
            {
                Frozen(choice.Cards); Frozen(choice.Targets); Frozen(choice.ContentIds);
                Require(choice.Parameters is IDictionary<string, string> { IsReadOnly: true }, "Prepared choice parameters are readonly.");
                try { ((IDictionary<string, string>)choice.Parameters).Add("mutation", "probe"); throw new InvalidOperationException("Prepared choice parameters allowed observer mutation."); }
                catch (NotSupportedException) { }
            }
        }
        Require(State(g) == before, "Prepared four-view mutation probes preserve the live accepted state.");
    }
    private static void Frozen<T>(IReadOnlyList<T> values)
    {
        Require(values is IList<T> { IsReadOnly: true }, "An exposed collection is detached and readonly.");
        try { ((IList<T>)values).Add(default!); } catch (NotSupportedException) { return; }
        throw new InvalidOperationException("A prepared nested collection allowed observer mutation.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static ContentRegistry Registry(bool singleCategory = false, bool tiny = false, bool sourceLoss = false, bool protection = false) =>
        ContentRegistry.Build(new StandardContentPackage(), new Fixture(singleCategory, tiny, sourceLoss, protection));
    private static GameEngine Start(ContentRegistry registry)
    {
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 6 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, "fixture:category-refund-owner", g.Revision, P(g)!.PromptId)); ReachPlay(g);
        UseDriver(g, "acquire", []); ReachPlay(g);
        Require(E<SkillsAcquiredEvent>(g).Any(e => e.PlayerSeat == 0 && e.SourceSkillId == Driver && e.SkillIds.Contains(Skill)),
            "The tested source is a real runtime acquisition, so a later actual suppressor can invalidate its qualification.");
        return g;
    }

    private sealed class Fixture(bool singleCategory, bool tiny, bool sourceLoss, bool protection) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("draw-discard-category-refund-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = JsonNode.Parse($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Skill}}}","revision":1,"activations":[{"id":"{{{Activation}}}","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"usesPerPhase":1,"usesPerGame":null,"effects":[{"op":"drawThenDiscardDistinctCategories","target":"selectedTarget","amount":3,"stateId":"{{{StateId}}}","condition":{"kind":"always"}}]}]},
              {"id":"{{{Driver}}}","revision":1,"activations":[
                {"id":"acquire","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["{{{Skill}}}"]}]},
                {"id":"collect","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelectedTargetHand","target":"selectedTarget"}]},
                {"id":"stash","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","minimumCards":0,"maximumCards":16,"zones":["hand"],"resultBind":"transfer"},{"op":"moveBoundCards","target":"owner","sourceBind":"transfer","destination":"selectedTargetHand"}]},
                {"id":"damage-self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"owner"},"amount":1}]}]},
              {"id":"{{{Watch}}}","revision":1,"triggers":[
                {"id":"draw","window":"cardsGained","subject":"owner","movementOccurrence":"perBatch","destinationZones":["hand"],"movementReasons":["{{{Reason("draw")}}}"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"draw","options":[{"id":"continue"}]}]},
                {"id":"discard","window":"cardsMoved","subject":"owner","movementOccurrence":"perOwnerBatch","sourceZones":["hand","equipment"],"movementReasons":["{{{Reason("discard")}}}"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"discard","options":[{"id":"continue"}]}]},
                {"id":"bonus","window":"cardsGained","subject":"owner","movementOccurrence":"perBatch","destinationZones":["hand"],"movementReasons":["{{{Reason("bonus")}}}"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"bonus","options":[{"id":"continue"}]}]}]},
              {"id":"{{{EquipmentChild}}}","revision":1,"triggers":[{"id":"actual-equipment-loss","window":"cardsMoved","subject":"owner","movementOccurrence":"perOwnerBatch","sourceZones":["equipment"],"movementReasons":["{{{Reason("discard")}}}"],"priority":100,"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"equipment","options":[{"id":"continue"}]},{"op":"grantSkills","target":"owner","skillIds":["{{{Suppressor}}}"]}]}]},
              {"id":"fixture:category-refund-protection","revision":1,"cardPolicies":[{"id":"real-foreign-armor-protection","kind":"preventForeignEquipmentDiscard","cardKinds":[]}]},
              {"id":"{{{Restrict}}}","revision":1,"triggers":[{"id":"actual-damage-category","window":"afterDamageApplied","subject":"owner","requireDamageSource":true,"damageOccurrence":"perDamage","optional":true,"effects":[
                {"op":"chooseOption","target":"owner","resultBind":"category","options":[{"id":"basic"},{"id":"trick"},{"id":"equipment"}]},
                {"op":"restrictDamageSourceHandCategory","target":"owner","cardCategories":["basic"],"condition":{"kind":"choiceIs","sourceBind":"category","optionId":"basic"}},
                {"op":"restrictDamageSourceHandCategory","target":"owner","cardCategories":["trick"],"condition":{"kind":"choiceIs","sourceBind":"category","optionId":"trick"}},
                {"op":"restrictDamageSourceHandCategory","target":"owner","cardCategories":["equipment"],"condition":{"kind":"choiceIs","sourceBind":"category","optionId":"equipment"}}]}]},
              {"id":"fixture:category-refund-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """)!;
            var presentations = ((JsonArray)rules["skills"]!).Select(n => n!["id"]!.GetValue<string>()).ToDictionary(id => id,
                id => (object)new { name = id, description = "正式命令类别弃牌回归" });
            foreach (var id in new[] { Watch, EquipmentChild }) presentations[id] = new
                { name = id, description = "实际支付子窗口", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            presentations[Restrict] = new { name = Restrict, description = "真实伤害来源手牌保护", optionLabels =
                new Dictionary<string, string> { ["basic"] = "基本", ["trick"] = "锦囊", ["equipment"] = "装备" } };
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentations }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "实际支付与精确返回")
                { Program = program, ProgramPresentation = catalog.Presentations[id], Tags = id == "fixture:category-refund-protection" ? SkillTag.Locked : SkillTag.None });
            b.AddSkill(new(Suppressor, "实际HP5资格抑制", "仅抑制取得来源资格，保留实体技能实例") { SuppressionRule = new(5) });
            b.AddSkill(new("fixture:category-refund-pick", "固定原生AI选将", "小四人实际模式") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            var extras = new[] { Watch, "fixture:category-refund-protection" }
                .Concat(sourceLoss ? new[] { EquipmentChild } : Array.Empty<string>()).Concat(protection ? new[] { Restrict } : Array.Empty<string>()).ToArray();
            b.AddGeneral(new("fixture:category-refund-owner", "类别退款拥有者", "supporter", Driver, "wei", 3, extras));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:category-refund-peer-{i}").ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "原生其他角色", "supporter", "fixture:category-refund-pick", "shu", 6, ["fixture:category-refund-quiet"]));
            var kinds = new[] { "standard:slash", "standard:duel", "standard:bagua" };
            b.AddDeck(new("fixture:category-refund-deck", "固定原生实体", 4, 0, [])
                { PhysicalCards = Enumerable.Range(0, tiny ? 16 : 96).Select(i => new ContentDeckPhysicalCard(singleCategory ? kinds[0] : kinds[i % 3], Suit.Heart, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "小四人类别支付", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:category-refund-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:category-refund-owner", .. peers]));
        }
    }
}
