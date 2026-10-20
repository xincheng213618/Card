namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidMouChenGongGift(ProgramSkillFrame f)
    {
        if (f.MouChenGongGift is not { } r || !MouChenGongGiftRoot(f) || !Enum.IsDefined(r.Stage) || r.Before < 0 || r.Before > r.After || r.After > _movementSequence) return false;
        var gifts = ProgramEventHistory<MouChenGongGiftPaidEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Gift is not { } gift) return r.Stage == MouChenGongGiftStage.GiftChoice && gifts.Length == 0 && r.Branch is null && r.Loss is null && r.Draw is null && !r.CeGained;
        if (gift.From.OwnerSeat != f.OwnerSeat || gift.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || gift.Sequence <= 0 ||
            gifts is not [var paid] || paid != new MouChenGongGiftPaidEvent(f.Id, r.RecipientSeat, gift.CardId, gift.PrintedKind, gift.From, gift.To, gift.Sequence) ||
            _cardMovements.Count(m => m.Sequence == gift.Sequence && m.CardId == gift.CardId && m.CardKind == gift.PrintedKind && m.From == gift.From && m.To == gift.To && m.Reason.Value == MouChenGongGiftReason) != 1) return false;
        var branches = ProgramEventHistory<MouChenGongBranchAcceptedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Branch is null) return r.Stage is MouChenGongGiftStage.GiftChildren or MouChenGongGiftStage.RecipientChoice && branches.Length == 0 && r.Loss is null && r.Draw is null && !r.CeGained;
        if (r.Branch is not ("lose-hp" or "draw") || branches is not [var branch] || branch != new MouChenGongBranchAcceptedEvent(f.Id, r.RecipientSeat, r.Branch)) return false;
        var issued = ProgramEventHistory<MouChenGongLossIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var losses = ProgramEventHistory<MouChenGongLossReturnedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Branch == "draw" && (r.Stage is not (MouChenGongGiftStage.Drawing or MouChenGongGiftStage.DrawChildren) || r.Loss is not null || losses.Length != 0 || issued.Length != 0 || r.CeGained)) return false;
        if (r.Branch == "lose-hp")
        {
            if (issued is not [var issue] || issue != new MouChenGongLossIssuedEvent(f.Id, r.RecipientSeat, 1)) return false;
            if (r.Loss is not { } loss) return r.Stage == MouChenGongGiftStage.LosingHp && losses.Length == 0 && r.Draw is null && !r.CeGained;
            if (loss.HpBefore < 0 || loss.HpAfter < 0 || loss.ActualAmount != loss.HpBefore - loss.HpAfter || loss.ActualAmount is < 0 or > 1 ||
                losses is not [var returned] || returned != new MouChenGongLossReturnedEvent(f.Id, r.RecipientSeat, loss.ChangeId, loss.HpBefore, loss.HpAfter, loss.ActualAmount, loss.IdentityPrevented)) return false;
            if (loss.IdentityPrevented ? loss.ChangeId != 0 || loss.ActualAmount != 0 || !ProgramEventHistory<IdentityGeneralAppearedEvent>().Any(e =>
                    e.Receipt.Entry == IdentityAppearanceEntry.ProgramHpLoss && e.Receipt.ParentFrameId == f.Id && e.Receipt.OwnerSeat == r.RecipientSeat &&
                    e.Receipt.RequestedDeduction == 1 && e.Receipt.OriginalInstructionIndex == f.InstructionIndex && e.Receipt.RestoredHp == loss.HpBefore) :
                loss.ChangeId <= 0 || ProgramEventHistory<ProgramSkillHpLostEvent>().Count(e => e.FrameId == f.Id && e.SkillId == f.SkillId &&
                    e.TargetSeat == r.RecipientSeat && e.Amount == loss.ActualAmount && e.RemainingHp == loss.HpAfter) != 1) return false;
        }
        var draws = ProgramEventHistory<MouChenGongDrawReturnedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Draw is not { } draw) return draws.Length == 0 && !r.CeGained && r.Stage is MouChenGongGiftStage.LossChildren or MouChenGongGiftStage.Drawing;
        var seat = r.Branch == "lose-hp" ? f.OwnerSeat : r.RecipientSeat; var count = r.Branch == "lose-hp" ? 2 : 1;
        return r.Stage == MouChenGongGiftStage.DrawChildren && draw.RecipientSeat == seat && draw.RequestedCount == count &&
            draws is [var returnedDraw] && returnedDraw == new MouChenGongDrawReturnedEvent(f.Id, seat, count, draw.ActualDrawIds.Count,
                draw.ReplacementGainedIds.Count, draw.SequenceBefore, draw.SequenceAfter, draw.AttemptFrameId) &&
            (!r.CeGained || r.Branch == "lose-hp");
    }
    private bool ValidMouChenGongRelease(ProgramSkillFrame f)
    {
        if (f.MouChenGongRelease is not { } r || !MouChenGongReleaseRoot(f) || !Enum.IsDefined(r.Stage) || r.Before > r.After || r.After > _movementSequence) return false;
        var accepted = ProgramEventHistory<MouChenGongReleaseAcceptedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var damage = ProgramEventHistory<MouChenGongReleaseDamageIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var cleared = ProgramEventHistory<MouChenGongCeClearedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.TargetSeat is not { } target || r.FrozenX is not { } x) return r.Stage == MouChenGongReleaseStage.TargetChoice && r.TargetSeat is null && r.FrozenX is null &&
            !r.Cleared && accepted.Length == 0 && damage.Length == 0 && cleared.Length == 0;
        return IsValidPlayerSeat(target) && target != f.OwnerSeat && x > 0 && accepted is [var accept] && accept == new MouChenGongReleaseAcceptedEvent(f.Id, target, x) &&
            damage is [var issue] && issue == new MouChenGongReleaseDamageIssuedEvent(f.Id, f.OwnerSeat, target, x) &&
            (r.Cleared ? r.Stage == MouChenGongReleaseStage.ClearChildren && cleared is [var clear] && clear.OwnerSeat == f.OwnerSeat && clear.FrozenX == x && clear.ClearedCount >= 0 :
                r.Stage == MouChenGongReleaseStage.Damage && cleared.Length == 0);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 29600)]
    private void AssertMouChenGong(ProgramSkillFrame f)
    {
        if (f.MouChenGongGift is not null && !ValidMouChenGongGift(f)) throw new InvalidOperationException("Mingce lost its exact actual Play, original paid HE entity or accepted native loss/draw invoice.");
        if (f.MouChenGongRelease is not null && !ValidMouChenGongRelease(f)) throw new InvalidOperationException("Mingce release lost its actual Play-start, frozen accepted X or damage-before-clear invoice.");
        if (f.WindowContext?.MouChenGongProtection is not null && !ExactMouChenGongBeforeFrame(f, allowPrevented: true))
            throw new InvalidOperationException("Zhichi's protection escaped its original native before-damage parent and actual-turn receipt.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 29600)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 29600)]
    private bool PermitAcceptedMouChenGong(ProgramSkillFrame f)
    {
        if (f.MouChenGongGift is { Gift: not null }) return ValidMouChenGongGift(f);
        return f.MouChenGongRelease is { FrozenX: > 0 } && ValidMouChenGongRelease(f);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 29601)]
    private bool PermitIssuedMouChenGong(ProgramSkillFrame f) => _players[f.OwnerSeat].IsAlive && PermitMouChenGongZhichiAfterLoss(f);
    private void SealMouChenGongMovements(long id, long before)
    {
        var f = GetActiveProgramFrame(id); var after = _movementSequence;
        ReplaceRuntimeTop(f = f with { MouChenGongGift = f.MouChenGongGift is { } gift ? gift with { After = after } : null });
        foreach (var m in _cardMovements.Where(m => m.Sequence > before && m.Sequence <= after))
        {
            var signed = new MouChenGongMovementSignedEvent(id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence);
            if (!ProgramEventHistory<MouChenGongMovementSignedEvent>().Contains(signed)) AdvanceEventRulesAndQueueFact(signed);
        }
    }
    private bool MouChenGongMovementSigned(ProgramSkillFrame f, CardMovementRecord m) => _cardMovements.Contains(m) &&
        ProgramEventHistory<MouChenGongMovementSignedEvent>().Count(e => e == new MouChenGongMovementSignedEvent(f.Id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence)) == 1;
    private bool IsMouChenGongMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        e?.Op == SkillProgramEffectOp.ResolveMouChenGongMingceGift && f.MouChenGongGift is { Stage: MouChenGongGiftStage.GiftChildren or MouChenGongGiftStage.LosingHp or MouChenGongGiftStage.LossChildren or MouChenGongGiftStage.DrawChildren } &&
        p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null && ValidMouChenGongGift(f);
    private bool DrainMouChenGongChildren(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private bool MouChenGongFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MouChenGongGift is { Gift: not null } r && ValidMouChenGongGift(f))
        {
            if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == MouChenGongGiftStage.Drawing && f.NativeDraw is not null;
            if (child is DyingFrame dying) return r.Stage == MouChenGongGiftStage.LossChildren && r.Loss is { ActualAmount: > 0, HpAfter: 0 } &&
                dying.ParentFrameId == f.Id && dying.VictimSeat == r.RecipientSeat && dying.KillerSeat is null && dying.Continuation == DyingContinuationKind.ProgramSkill;
            if (child is IdentityAppearanceFrame identity) return r.Stage == MouChenGongGiftStage.LosingHp && identity.Receipt.Entry == IdentityAppearanceEntry.ProgramHpLoss &&
                identity.Receipt.ParentFrameId == f.Id && identity.Receipt.OwnerSeat == r.RecipientSeat && identity.Receipt.RequestedDeduction == 1 && identity.Receipt.OriginalInstructionIndex == f.InstructionIndex;
            if (RecoveryReplacementFrameRidesOn(child, f)) return true;
            if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                (r.Stage == MouChenGongGiftStage.LossChildren && r.Loss?.ChangeId == hp.Change.Id && hp.Change.TargetSeat == r.RecipientSeat && hp.Change.Kind == HpChangeKind.Loss ||
                 r.Stage == MouChenGongGiftStage.GiftChildren && r.Gift is { PrintedKind: CardKind.SilverLion, From.Zone: CardZoneKind.Equipment } && hp.Change.TargetSeat == f.OwnerSeat && hp.Change.Kind == HpChangeKind.Recovery);
            if (f.PendingMovementContinuation is { } pending && IsMouChenGongMovement(f, GetPausedPrivateOfferEffect(f), pending) && child is CardsMovedTriggerWindowFrame moved)
                return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat &&
                    moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
                    moved.Batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After && MouChenGongMovementSigned(f, m));
        }
        else if (f.MouChenGongRelease is { Stage: MouChenGongReleaseStage.Damage, FrozenX: > 0, TargetSeat: not null } && ValidMouChenGongRelease(f) &&
                 f.AttackAttempt is { } a && f.AttackReturn is not null && a.SourceSeat == f.OwnerSeat && !a.SourceLess)
            return DyingSuitsStructuralEdge(f, child);
        else return false;
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouChenGongObserverSuffix(int index) =>
        _resolutionStack[index + 1] is DyingFrame original && index + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(index + 1, original) ||
        HasObserverSuffix(index, static (g, n) => g.IsMovementObserverEdge(n) || g.IsSameNameHandObserverEdge(n) || g.IsYanPuDyingExitEdge(n) ||
            g.TypedDamagePostEventEdge(g._resolutionStack[n - 1], g._resolutionStack[n]) || g._resolutionStack[n - 1] is ProgramSkillFrame f && g.MouChenGongFirstChild(f, g._resolutionStack[n]) ||
            NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) || g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]),
            static (g, n, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(n, dying));
    private ProgramSkillFrame? MouChenGongObserverRoot() => FindProgramObserverRoot(static (g, f, child) => g.MouChenGongFirstChild(f, child), static (g, n) => g.MouChenGongObserverSuffix(n));
    private bool HasMouChenGongDamageObserver(long id) => MouChenGongObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasMouChenGongBeforeDamageObserver(long id) => MouChenGongObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsMouChenGongDying() => ActiveDying is { } dying && MouChenGongObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsMouChenGongNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess) return false;
        if (f.MouChenGongRelease is { Stage: MouChenGongReleaseStage.Damage } r && ValidMouChenGongRelease(f)) return target == r.TargetSeat && amount == r.FrozenX && source is null && nature is null;
        if (MouChenGongObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature &&
            target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    private bool IsMouChenGongRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return index >= 0 && child > index + 1 && MouChenGongFirstChild(f, _resolutionStack[index + 1]) && MouChenGongObserverSuffix(index) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 29600)]
    private bool ResumeMouChenGong(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id) return false;
        if (f.MouChenGongGift is not null) { ContinueMouChenGongGift(id); return true; }
        if (f.MouChenGongRelease is not null) { ContinueMouChenGongRelease(id); return true; } return false;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 29600)]
    private bool ReturnMouChenGongMovement(ProgramSkillFrame f) => f.PendingMovementContinuation is not null && ResumeMouChenGong(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 29600)]
    private bool AdvanceMouChenGongChildren() => _pendingDecision is null && MouChenGongObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
