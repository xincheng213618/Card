namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShiHqShanxi = "mobile:shi-shanxi", ShiHqQizhou = "mobile:shi-qizhou", ShiHqView = "yingji-duel";
    private const string ShiHqDrawReason = "skill.mobile-shi-he-qi.yingji-draw", ShiHqCleanupReason = "skill.mobile-shi-he-qi.yingji-cleanup";
    private bool HasShiHqCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMobileShiHeQiQizhouMark);
    private static CardConversionSource ShiHqSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private ShiHqYingjiMarkedEvent? ShiHqMark(int id) => !HasShiHqCapability ? null : ProgramEventHistory<ShiHqYingjiMarkedEvent>()
        .LastOrDefault(m => m.CardId == id && !ProgramEventHistory<ShiHqYingjiClearedEvent>().Any(c => c.MarkId == m.MarkId));
    private bool HasShiHqYingji(int id) => ShiHqMark(id) is not null;
    [ProgramPipelineHook(ProgramPipelinePhase.CardEntitySkillLabel, 49000)]
    private string? ShiHqYingjiLabel(int id) => HasShiHqYingji(id) ? "应机" : null;
    private bool ShiHqTargetUsed(int owner, int target) => ProgramEventHistory<ShiHqShanxiUsedEvent>().Any(e => e.Source.OwnerSeat == owner &&
        e.Turn == _turnNumber && e.PhaseInstance == _cardUseDebitPhaseInstanceId && e.Target == target);

    [ProgramCardLegality(ProgramCardLegalityStage.ConversionSourceBan, 49000)]
    private bool BanShiHqUnmarkedConversion(ProgramViewAsSourceQuery q) => q.Source.SkillId == ShiHqShanxi &&
        (q.Source.BindingId != ShiHqView || q.ForResponse || q.DyingUse || q.Source.OwnerSeat != q.Owner.Seat ||
         _phase != TurnPhase.Play || _currentSeat != q.Owner.Seat ||
         _cardZones.GetLocation(q.Card.Id) != CardLocation.Hand(q.Owner.Seat) && !IsShiLsHandAccess(q.Owner.Seat, q.Card.Id) || !HasShiHqYingji(q.Card.Id));
    [ProgramCardLegality(ProgramCardLegalityStage.AfterLegalActionKind, 49000)]
    private bool BanShiHqRepeatedTarget(ProgramLegalActionQuery q) => q.Action.ConversionSource is { SkillId: ShiHqShanxi } &&
        (q.Action.TargetSeats.Count == 0 || q.Action.TargetSeats.Any(s => s == q.Actor.Seat || ShiHqTargetUsed(q.Actor.Seat, s)));
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 49000)]
    private void ObserveShiHqDuel(IGameEvent payload)
    {
        if (!HasShiHqCapability || payload is not CardUseDeclaredEvent d || LifecycleCardUse(d.ResolutionId) is not { Action: { } a } use ||
            !a.ConversionChain.Any(s => s.SkillId == ShiHqShanxi)) return;
        var source = a.ConversionChain.Single(s => s.SkillId == ShiHqShanxi);
        if (source.BindingId != ShiHqView || a.Type != CardActionType.Use || a.EffectiveKind != CardKind.Duel || use.CardKind != CardKind.Duel ||
            a.ActorSeat != source.OwnerSeat || a.PhysicalCards is not [{ CardId: var id, From: var from }] ||
            !IsShiHqOriginalHandPayment(use, a, source.OwnerSeat, id, from) || !HasShiHqYingji(id) || _phase != TurnPhase.Play || _currentSeat != source.OwnerSeat ||
            use.TargetSeats.Count == 0 || use.TargetSeats.Any(s => s == source.OwnerSeat || ShiHqTargetUsed(source.OwnerSeat, s)))
            throw new InvalidOperationException("Shanxi requires one original authorized marked hand entity and unused actual-Play targets on its native Duel.");
        foreach (var target in use.TargetSeats.Distinct()) AdvanceEventRulesAndQueueFact(new ShiHqShanxiUsedEvent(use.Id, a.ActionId,
            source, _turnNumber, _cardUseDebitPhaseInstanceId, target, id));
    }

    private bool IsShiHqOriginalHandPayment(CardUseFrame use, CardActionContext action, int actor, int cardId, CardLocation from) =>
        from == CardLocation.Hand(actor) && action.ProviderSeat == actor ||
        from.Zone == CardZoneKind.Hand && from.OwnerSeat == action.ProviderSeat &&
        ProgramEventHistory<ShiLsMaterialAuthorizedEvent>().Any(e => e.UseId == use.Id && e.ActionId == action.ActionId &&
            e.Actor == actor && e.CardId == cardId && e.From == from);

    private static bool IsShiHqMarkTrigger(SkillProgramTrigger t) => t.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveMobileShiHeQiQizhouMark);
    private static bool ShiHqEntersField(CardMovementRecord m, int owner) => m.To.OwnerSeat == owner &&
        m.To.Zone is CardZoneKind.Equipment or CardZoneKind.Judgment && m.From != m.To;
    private IEnumerable<ProgramTriggerCandidate> CollectShiHqMovementCandidates(CardMovementBatchContext batch)
    {
        if (!HasShiHqCapability) yield break;
        foreach (var owner in _players.Where(p => p.IsAlive && batch.Movements.Any(m => ShiHqEntersField(m, p.Seat))))
        foreach (var c in CollectProgramTriggerCandidates(owner, SkillProgramTriggerWindow.CardsMoved).Where(c => c.SkillId == ShiHqQizhou && c.BindingId == "field-entry-mark"))
            yield return c;
    }
    private ProgramSkillWindowContext CreateShiHqMovementContext(CardsMovedTriggerWindowFrame w, ProgramTriggerCandidate c) =>
        new(SkillProgramTriggerWindow.CardsMoved, w.Id, c.OwnerSeat, SourceSeat: w.Batch.OriginOwnerSeat,
            TargetSeat: c.OwnerSeat, OccurrenceIndex: c.OccurrenceIndex, Facts: CaptureProgramTriggerFacts(_players[c.OwnerSeat]), MovementBatch: w.Batch);
    private bool IsCurrentShiHqFieldEntry(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (c.SkillId != ShiHqQizhou || c.BindingId != "field-entry-mark" || context.Window != SkillProgramTriggerWindow.CardsMoved ||
            context.OwnerSeat != c.OwnerSeat || context.TargetSeat != c.OwnerSeat || context.OccurrenceIndex != c.OccurrenceIndex ||
            !IsValidPlayerSeat(c.OwnerSeat) || !_players[c.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[c.OwnerSeat], c.SkillId, c.SkillInstanceId) ||
            _contentRegistry.GetSkill(c.SkillId).Program?.GameplayHash != c.GameplayHash || context.MovementBatch is not { } batch ||
            _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().LastOrDefault() is not { } window || window.Id != context.ParentFrameId ||
            window.Batch.Id != batch.Id || window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count || window.Candidates[window.CandidateIndex] != c ||
            context.SourceSeat != window.Batch.OriginOwnerSeat || !window.Batch.Movements.SequenceEqual(batch.Movements)) return false;
        // Native equipment and delayed-trick entry often comes from ownerless
        // Processing. Qualification belongs to the actual destination batch,
        // never to an unrelated card loss by the observing skill holder.
        return batch.Movements.Any(m => ShiHqEntersField(m, c.OwnerSeat) && _cardMovements.Contains(m)) &&
            _players.Any(p => p.IsAlive && GetHand(p).Count > 0);
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 49000)]
    private bool CanOfferShiHq(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w)
    {
        if (c.SkillId != ShiHqQizhou) return true;
        if (t.Id == "field-entry-mark") return IsCurrentShiHqFieldEntry(c, w);
        if (t.Id == "global-yingji-cleanup") return IsIssuedShiHqCleanupCandidate(c, w);
        return t.Id == "draw-extra-yingji" && w.Window == SkillProgramTriggerWindow.DrawPhaseStarting && _currentSeat == c.OwnerSeat;
    }

    private ShiHqYingjiMarkedEvent? ShiHqCleanupSource() => _players.SelectMany(p => GetHand(p)).Select(c => ShiHqMark(c.Id))
        .Where(m => m is not null).Cast<ShiHqYingjiMarkedEvent>().OrderBy(m => m.MarkId).FirstOrDefault();
    private IReadOnlyList<TurnEndingBoundaryItem> IncludeShiHqAfterTurnEndedItems(DeferredTurnEndFrame parent, IReadOnlyList<TurnEndingBoundaryItem> ordinary)
    {
        if (!HasShiHqCapability) return ordinary;
        var items = parent.AfterTurnEnded is null ? ordinary.Where(i => i.Candidate is not { SkillId: ShiHqQizhou, BindingId: "global-yingji-cleanup" }).ToList() : ordinary.ToList();
        if (!items.Any(i => i.Candidate is { SkillId: ShiHqQizhou, BindingId: "global-yingji-cleanup" }) && ShiHqCleanupSource() is { } mark &&
            !ProgramEventHistory<ShiHqCleanupIssuedEvent>().Any(e => e.Turn == parent.TurnNumber && e.TurnOwner == parent.OwnerSeat))
        {
            var trigger = _contentRegistry.GetSkill(ShiHqQizhou).Program!.Triggers.Single(t => t.Id == "global-yingji-cleanup");
            var c = new ProgramTriggerCandidate(mark.Source.OwnerSeat, ShiHqQizhou, trigger.Id, mark.Source.SkillInstanceId, mark.Hash, trigger.Priority);
            items.Add(new(TurnEndingBoundaryItemKind.Program, trigger.Priority, $"shi-he-qi.cleanup:{parent.TurnNumber}:{parent.OwnerSeat}", c, CaptureProgramTriggerFacts(_players[c.OwnerSeat])));
        }
        return items.SequenceEqual(ordinary) ? ordinary : Array.AsReadOnly(items.ToArray());
    }
    private bool IsIssuedShiHqCleanupCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext w)
    {
        if (!HasShiHqCapability || c.SkillId != ShiHqQizhou || c.BindingId != "global-yingji-cleanup" || w.Window != SkillProgramTriggerWindow.AfterTurnEnded ||
            _resolutionStack.SingleOrDefault(f => f.Id == w.ParentFrameId) is not DeferredTurnEndFrame p || !IsActualAfterTurnEndedParent(p) ||
            AfterTurnEndedCandidate(p) != c || w.OwnerSeat != c.OwnerSeat || w.SourceSeat != p.OwnerSeat || w.TargetSeat != p.OwnerSeat ||
            w.OccurrenceIndex != c.OccurrenceIndex || w.Facts != p.AfterTurnEnded!.Items[p.AfterTurnEnded.ItemIndex].Facts ||
            _contentRegistry.GetSkill(c.SkillId).Program?.GameplayHash != c.GameplayHash) return false;
        if (_resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.ShiHeQi is { Operation: SkillProgramEffectOp.ResolveMobileShiHeQiYingjiCleanup } r && r.ParentId == p.Id &&
            r.Source.OwnerSeat == c.OwnerSeat && r.Source.SkillInstanceId == c.SkillInstanceId && r.Hash == c.GameplayHash && f.WindowContext == w)) return true;
        return ShiHqCleanupSource() is { } mark && mark.Source.OwnerSeat == c.OwnerSeat && mark.Source.SkillInstanceId == c.SkillInstanceId && mark.Hash == c.GameplayHash &&
            !ProgramEventHistory<ShiHqCleanupIssuedEvent>().Any(e => e.Turn == p.TurnNumber && e.TurnOwner == p.OwnerSeat);
    }
    private IReadOnlyList<ProgramPublicRuleStateSnapshot> MobileShiHeQiPublicStates(CharacterState owner, string skillId)
    {
        if (skillId != ShiHqShanxi) return [];
        var used = _phase == TurnPhase.Play && _currentSeat == owner.Seat ? _players.Count(p => p.Seat != owner.Seat && ShiHqTargetUsed(owner.Seat, p.Seat)) : 0;
        var remaining = _phase == TurnPhase.Play && _currentSeat == owner.Seat ? _players.Count(p => p.IsAlive && p.Seat != owner.Seat && !ShiHqTargetUsed(owner.Seat, p.Seat)) : 0;
        return GetSkillBindingShard(owner).ProgramInstances.Where(i => i.SkillId == skillId).SelectMany(i => new[] {
            new ProgramPublicRuleStateSnapshot(i.SkillInstanceId, "shanxi-used-targets", ProgramPublicRuleStateKind.GameActivationProgress, used),
            new ProgramPublicRuleStateSnapshot(i.SkillInstanceId, "shanxi-remaining-targets", ProgramPublicRuleStateKind.GameActivationProgress, remaining) }).ToArray();
    }
}
