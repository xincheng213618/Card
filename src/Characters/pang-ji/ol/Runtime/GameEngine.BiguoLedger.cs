namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedBeforeActivity, 10)]
    private bool BiguoFinalizedBeforeActivityTargetWindow(ProgramTargetCardWindowQuery query) =>
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveBiguoTargetedPindianAndCategoryBan);


    private bool ValidBiguoArm(BiguoArmedEvent arm)
    {
        if (arm.Source.SkillId != "ol:biguo" || arm.Source.BindingId != "draw-end-category-gain" || _contentRegistry.GetSkill(arm.Source.SkillId).Program?.GameplayHash != arm.GameplayHash ||
            ProgramEventHistory<PangJiStartedEvent>().Count(e => e.FrameId == arm.FrameId && e.Source == arm.Source && e.GameplayHash == arm.GameplayHash && e.Op == SkillProgramEffectOp.ResolveBiguoDrawAndArmTargetPindian && e.ActualTurn == arm.ActualTurn && e.TurnOwner == arm.TurnOwner) != 1 ||
            ProgramEventHistory<BiguoArmedEvent>().Count(e => e.FrameId == arm.FrameId) != 1 || ProgramEventHistory<BiguoCostCommittedEvent>().Where(e => e.FrameId == arm.FrameId).ToArray() is not [var cost] ||
            ProgramEventHistory<BiguoCostPaidEvent>().Where(e => e.FrameId == arm.FrameId).ToArray() is not [var paid] || paid.CardId != cost.CardId || paid.PrintedKind != cost.PrintedKind || paid.From != cost.From || paid.Sequence <= cost.Before ||
            cost.From.OwnerSeat != arm.Source.OwnerSeat || cost.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || !_cardMovements.Any(m => m.Sequence == paid.Sequence && m.CardId == paid.CardId && m.CardKind == paid.PrintedKind && m.From == paid.From && m.To == paid.To && m.Reason == paid.Reason)) return false;
        var issued = ProgramEventHistory<BiguoObtainEntityIssuedEvent>().Where(e => e.FrameId == arm.FrameId).OrderBy(e => e.Index).ToArray();
        return issued.Select(e => GetProgramCardCategory(e.PrintedKind)).Distinct().Count() == issued.Length && issued.Where((e, i) => e.Index != i).Any() == false && issued.All(e => ProgramEventHistory<BiguoObtainEntityPaidEvent>().Where(p => p.FrameId == e.FrameId && p.Index == e.Index).ToArray() is [var p] && p.CardId == e.CardId && p.PrintedKind == e.PrintedKind && p.From == CardLocation.DrawPile && p.To == CardLocation.Hand(arm.Source.OwnerSeat) && _cardMovements.Any(m => m.Sequence == p.Sequence && m.CardId == p.CardId && m.CardKind == p.PrintedKind && m.From == p.From && m.To == p.To && m.Reason.Value == BiguoObtainReason));
    }
    private IReadOnlyList<int> BiguoDesignatedTargets(CardActionContext action) => action.EffectiveKind == CardKind.BorrowedSword
        ? action.TargetSeats.Where((_, i) => i % 2 == 0).ToArray()
        : GetImplicitSelfCardUseTargets(action.EffectiveKind, action.ActorSeat, action.EffectiveDesignatedTargetSeats);
    [ProgramPipelineHook(ProgramPipelinePhase.CollectIssuedCardTrigger, 21)]
    private void CollectIssuedBiguoCandidates(CardActionContext action, SkillProgramTriggerWindow window, List<ProgramCardTriggerCandidate> candidates)
    {
        if (window != SkillProgramTriggerWindow.CardUseTargetsFinalized) return;
        candidates.RemoveAll(c => c.SkillId == "ol:biguo" && c.TriggerId == "armed-target-pindian");
        if (action.Type != CardActionType.Use || !BiguoTurnOpen(_turnNumber, _turnProgression.OwnerSeat) || _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u => u.Action?.ActionId == action.ActionId) is not { } use) return;
        var targets = BiguoDesignatedTargets(action);
        foreach (var arm in ProgramEventHistory<BiguoArmedEvent>().Where(a => a.ActualTurn == _turnNumber && a.TurnOwner == _turnProgression.OwnerSeat && ValidBiguoArm(a)).OrderBy(a => a.FrameId).DistinctBy(a => a.Source.OwnerSeat))
        {
            var owner = arm.Source.OwnerSeat;
            if (!targets.Contains(owner) || !CanBiguoPindian(owner) || ProgramEventHistory<BiguoTargetResolvedEvent>().Any(e => e.UseId == use.Id && e.OwnerSeat == owner) || ProgramEventHistory<BiguoTargetAcceptedEvent>().Any(e => e.Origin.UseId == use.Id && e.Origin.Source.OwnerSeat == owner)) continue;
            var t = _contentRegistry.GetSkill(arm.Source.SkillId).Program!.Triggers.Single(t => t.Id == "armed-target-pindian");
            var origin = new BiguoTargetOrigin(arm.FrameId, arm.Source, arm.GameplayHash, use.Id, action.ActionId, arm.ActualTurn, arm.TurnOwner);
            var c = CreateCardActionProgramContext(action, window, 0, owner, owner, CaptureProgramTriggerFacts(_players[owner], action)) with { BiguoTarget = origin };
            candidates.Add(new(owner, owner, arm.Source.SkillId, t.Id, arm.GameplayHash, arm.Source.SkillInstanceId, t.Priority, c));
        }
    }
    private ProgramSkillWindowContext CreateBiguoTargetContext(ProgramCardTriggerWindowFrame window, ProgramCardTriggerCandidate candidate, ProgramSkillWindowContext ordinary)
    {
        if (candidate.SkillId != "ol:biguo" || candidate.TriggerId != "armed-target-pindian") return ordinary;
        if (candidate.FrozenContext?.BiguoTarget is not { } origin || origin.UseId != window.ParentFrameId || origin.ActionId != window.Action.ActionId) throw new InvalidOperationException("Biguo lost his frozen target-use entitlement.");
        return ordinary with { BiguoTarget = origin, SourceSeat = window.Action.ActorSeat, TargetSeat = candidate.OwnerSeat };
    }
    private bool BiguoTargetRoot(ProgramTriggerCandidate candidate, ProgramSkillWindowContext c, bool requireUnpaid)
    {
        if (candidate.SkillId != "ol:biguo" || candidate.BindingId != "armed-target-pindian" || c.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || c.OwnerSeat != candidate.OwnerSeat || c.TargetSeat != candidate.OwnerSeat || c.BiguoTarget is not { } origin || origin.Source.OwnerSeat != candidate.OwnerSeat || origin.Source.SkillId != candidate.SkillId || origin.Source.SkillInstanceId != candidate.SkillInstanceId || origin.GameplayHash != candidate.GameplayHash ||
            c.CardUse?.CardActionId != origin.ActionId || c.CardUse.ParentCardUseFrameId != origin.UseId ||
            ProgramEventHistory<BiguoArmedEvent>().SingleOrDefault(a => a.FrameId == origin.ArmFrameId) is not { } arm || arm.Source != origin.Source || arm.GameplayHash != origin.GameplayHash || arm.ActualTurn != origin.ActualTurn || arm.TurnOwner != origin.TurnOwner || !ValidBiguoArm(arm) ||
            _resolutionStack.SingleOrDefault(x => x.Id == c.ParentFrameId) is not ProgramCardTriggerWindowFrame window || window.ParentFrameId != origin.UseId || window.Action.ActionId != origin.ActionId || GetCardActionWindow(window) != c.Window || window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count || ToSharedCandidate(window.Candidates[window.CandidateIndex]) != candidate || window.Candidates[window.CandidateIndex].FrozenContext?.BiguoTarget != origin ||
            LifecycleCardUse(origin.UseId) is not { Action.Type: CardActionType.Use } use || use.Action!.ActionId != origin.ActionId || !BiguoDesignatedTargets(window.Action).Contains(candidate.OwnerSeat)) return false;
        return !requireUnpaid || BiguoTurnOpen(origin.ActualTurn, origin.TurnOwner) && CanBiguoPindian(candidate.OwnerSeat) && !ProgramEventHistory<BiguoTargetAcceptedEvent>().Any(e => e.Origin.UseId == origin.UseId && e.Origin.Source.OwnerSeat == candidate.OwnerSeat);
    }
    private bool IsIssuedBiguoCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context) => BiguoTargetRoot(c, context, true);
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 28130)]
    private void CaptureBiguoNativeFacts(IGameEvent payload)
    {
        if (payload is ProgramCardTriggerResolvedEvent { SkillId: "ol:biguo", TriggerId: "armed-target-pindian" } resolved && _resolutionStack.LastOrDefault() is ProgramCardTriggerWindowFrame w && w.Id == resolved.FrameId && w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count && w.Candidates[w.CandidateIndex] is { FrozenContext.BiguoTarget: { } o } candidate && candidate.OwnerSeat == resolved.OwnerSeat)
        {
            if (ProgramEventHistory<BiguoTargetResolvedEvent>().Any(e => e.UseId == o.UseId && e.OwnerSeat == resolved.OwnerSeat)) throw new InvalidOperationException("Biguo cannot finish the same target occurrence twice.");
            AdvanceEventRulesAndQueueFact(new BiguoTargetResolvedEvent(o.UseId, o.ActionId, resolved.OwnerSeat, resolved.Activated)); return;
        }
        if (payload is not PindianResultDeterminedEvent e || _resolutionStack.LastOrDefault() is not PindianFrame p || p.Id != e.FrameId || p.SkillId != "ol:biguo" || !IsBiguoPindianFrame(p) || _resolutionStack.SingleOrDefault(x => x.Id == p.ParentFrameId) is not ProgramSkillFrame { PangJi: { } r } f) return;
        var result = e.Result;
        if (result.SourceSeat != p.SourceSeat || result.OpponentSeat != p.OpponentSeat || (result.SourceCardId != p.SourceCardId && !IsDouyuPairAssignment(p, result) && !IsWuLuKangAssignedPair(p, result)) || result.SourceCardId == result.OpponentCardId || ProgramEventHistory<BiguoPindianCardCategoryEvent>().Any(x => x.PindianId == p.Id)) throw new InvalidOperationException("Biguo cannot replace or classify a native Pindian pair twice.");
        var cards = new[] { (result.SourceSeat, result.SourceCardId), (result.OpponentSeat, result.OpponentCardId) };
        var categories = new List<SkillProgramCardCategory>();
        foreach (var (seat, id) in cards)
        {
            var card = GetAdvancedCard(id); var category = GetProgramCardCategory(card.Kind); categories.Add(category);
            AdvanceEventRulesAndQueueFact(new BiguoPindianCardCategoryEvent(f.Id, p.Id, seat, id, card.Kind, category));
        }
        foreach (var seat in new[] { result.SourceSeat, result.OpponentSeat }.Where(seat => !result.WonBy(seat)))
        foreach (var category in categories.Distinct()) AdvanceEventRulesAndQueueFact(new BiguoCategoryBanEvent(f.Id, p.Id, seat, category, r.ActualTurn, r.TurnOwner));
    }
    private bool IsBiguoCategoryForbidden(int seat, CardKind effectiveKind)
    {
        if (!BiguoTurnOpen(_turnNumber, _turnProgression.OwnerSeat)) return false;
        var category = GetProgramCardCategory(effectiveKind);
        return ProgramEventHistory<BiguoCategoryBanEvent>().Any(e => e.Seat == seat && e.Category == category && e.ActualTurn == _turnNumber && e.TurnOwner == _turnProgression.OwnerSeat && ValidBiguoBan(e));
    }
    private bool ValidBiguoBan(BiguoCategoryBanEvent ban)
    {
        if (ProgramEventHistory<PangJiStartedEvent>().SingleOrDefault(e => e.FrameId == ban.FrameId) is not { Op: SkillProgramEffectOp.ResolveBiguoTargetedPindianAndCategoryBan } start || start.ActualTurn != ban.ActualTurn || start.TurnOwner != ban.TurnOwner ||
            ProgramEventHistory<BiguoPindianIssuedEvent>().Count(e => e.FrameId == ban.FrameId && e.PindianId == ban.PindianId && e.OwnerSeat == start.Source.OwnerSeat) != 1 || ProgramEventHistory<PindianResultDeterminedEvent>().Where(e => e.FrameId == ban.PindianId && e.SkillId == start.Source.SkillId).ToArray() is not [var determined] || determined.Result.WonBy(ban.Seat) || ban.Seat != determined.Result.SourceSeat && ban.Seat != determined.Result.OpponentSeat) return false;
        var result = determined.Result; var cards = ProgramEventHistory<BiguoPindianCardCategoryEvent>().Where(e => e.FrameId == ban.FrameId && e.PindianId == ban.PindianId).ToArray();
        return cards.Length == 2 && cards.Any(e => e.Seat == result.SourceSeat && e.CardId == result.SourceCardId) && cards.Any(e => e.Seat == result.OpponentSeat && e.CardId == result.OpponentCardId) && cards.All(e => GetAdvancedCard(e.CardId).Kind == e.PrintedKind && GetProgramCardCategory(e.PrintedKind) == e.Category) && cards.Any(e => e.Category == ban.Category) && ProgramEventHistory<BiguoCategoryBanEvent>().Count(e => e == ban) == 1;
    }
    private bool IsBiguoPindianFrame(PindianFrame child) => _resolutionStack.SingleOrDefault(x => x.Id == child.ParentFrameId) is ProgramSkillFrame { PangJi: { Paid: true, Stage: PangJiStage.Pindian } r } f && r.Op == SkillProgramEffectOp.ResolveBiguoTargetedPindianAndCategoryBan && r.PindianId == child.Id && child.SkillId == f.SkillId && child.SourceSeat == f.OwnerSeat && child.ProgramResultBind == BiguoResultBind && child.ProgramResultVisibility == SkillProgramCardSetVisibility.Public && ProgramEventHistory<BiguoPindianIssuedEvent>().Count(e => e.FrameId == f.Id && e.PindianId == child.Id && e.ArmFrameId == r.Target!.ArmFrameId && e.UseId == r.Target.UseId && e.ActionId == r.Target.ActionId && e.OwnerSeat == f.OwnerSeat && e.Before == r.Before) == 1;
    private bool HasBiguoPindianContinuation()
    {
        var p = _resolutionStack.OfType<PindianFrame>().SingleOrDefault();
        if (p is null || !IsBiguoPindianFrame(p) || _resolutionStack.SingleOrDefault(x => x.Id == p.ParentFrameId) is not ProgramSkillFrame { PangJi: { Target: { } origin } } f || f.WindowContext is not { } c || LifecycleCardUse(origin.UseId) is not { } use || c.ParentFrameId != f.PangJi.WindowId) return false;
        var candidate = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, c.OccurrenceIndex);
        return BiguoTargetRoot(candidate, c, false) && use.Action?.ActionId == origin.ActionId;
    }

    private bool IsAcceptedBiguoUse(int actorSeat, CardKind kind, CardActionType actionType, long? useId) =>
        actionType == CardActionType.Use && useId is { } id && LifecycleCardUse(id) is { Action: { Type: CardActionType.Use } action } use &&
        use.SourceSeat == actorSeat && use.CardKind == kind && action.ActorSeat == actorSeat && action.EffectiveKind == kind &&
        ProgramEventHistory<CardUseDeclaredEvent>().Count(e => e.ResolutionId == id && e.SourceSeat == actorSeat &&
            e.CardKind == kind && e.CardId == use.CardId) == 1;

    [ProgramCardLegality(ProgramCardLegalityStage.BeforeIntrinsicUseBan, 10)]
    private bool BiguoCardUseForbidden(ProgramCardUseQuery query) =>
        IsBiguoCategoryForbidden(query.ActorSeat, query.Kind) &&
        !IsAcceptedBiguoUse(query.ActorSeat, query.Kind, query.ActionType, query.ExistingUseFrameId);
}
