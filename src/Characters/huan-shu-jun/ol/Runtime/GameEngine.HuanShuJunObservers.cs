namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static string HuanShuJunDecisionHash(PendingDecision p) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_huan_shu_jun_ol_HuanShuJunObservers.HuanShuJunDecisionHash(p);
    private bool HuanShuJunPaidOrPreparing(ProgramSkillFrame f) => f.HuanShuJun is not null && HuanShuJunComposition.Handles(f.HuanShuJun.Op) && f.HuanShuJun.Source == HuanShuJunSource(f) && f.HuanShuJun.GameplayHash == f.GameplayHash && ProgramEventHistory<HuanShuJunStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == f.HuanShuJun.Source && e.GameplayHash == f.GameplayHash && e.Op == f.HuanShuJun.Op && e.WindowId == f.HuanShuJun.WindowId) == 1;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 28100)]
    private void AssertHuanShuJun(ProgramSkillFrame f)
    {
        if (f.HuanShuJun is not { } r) return;
        if (!HuanShuJunPaidOrPreparing(f) || f.InstructionIndex is < 0 or > 1 || f.InstructionIndex == 1 && GetPausedPrivateOfferEffect(f)?.Op != r.Op || !Enum.IsDefined(r.Stage) || r.Source.OwnerSeat != f.OwnerSeat || r.Before < 0 || r.After < r.Before || r.After > _movementSequence || r.Selected.Distinct().Count() != r.Selected.Count || r.Materials.Select(m => m.CardId).Distinct().Count() != r.Materials.Count || r.Selected is not System.Collections.IList { IsReadOnly: true } || r.Targets is not System.Collections.IList { IsReadOnly: true } || r.Materials is not System.Collections.IList { IsReadOnly: true } || r.Sources is not System.Collections.IList { IsReadOnly: true } || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash)
            throw new InvalidOperationException("Huan Shu Jun lost her immutable source, owning operation or exact pending stage.");
        if (r.Op == SkillProgramEffectOp.ResolveHuanpeiHandAdjustmentAndBasicUse)
        {
            if (r.Request is not { } n || n.ActorSeat != f.OwnerSeat || n.Intent != HuanpeiIntent.Play && (!HuanpeiRequestParentCurrent(n) || r.OriginalDecision is not { } original || original.PromptId != n.OriginalPromptId || original.Revision != n.OriginalRevision || ProgramEventHistory<HuanpeiRequestCapturedEvent>().Count(e => e.FrameId == f.Id && e.Request == n && e.DecisionHash == HuanShuJunDecisionHash(original)) != 1)) throw new InvalidOperationException("Huanpei lost its exact original true-Use request and frozen native prompt.");
            var payments = ProgramEventHistory<HuanpeiAdjustmentAcceptedEvent>().Where(e => e.FrameId == f.Id).ToArray();
            if (r.Paid ? payments is not [var paid] || paid.OwnerSeat != f.OwnerSeat || paid.Round != r.Round || paid.HandCount != r.FrozenHandCount || paid.Hp != r.FrozenHp || paid.RequestedCount != Math.Abs(r.FrozenHandCount - r.FrozenHp) || paid.Before != r.Before || ProgramEventHistory<HuanpeiAdjustmentAcceptedEvent>().Count(e => e.OwnerSeat == f.OwnerSeat && e.Round == r.Round) != 1 : payments.Length != 0) throw new InvalidOperationException("Huanpei repeated or changed its one actual round adjustment.");
        }
        else if (r.Op == SkillProgramEffectOp.ResolveWenyiHealthOneAid)
        {
            if (!WenyiHpRoot(f) || r.WindowId != f.WindowContext!.ParentFrameId || r.TargetSeat != f.WindowContext.HpChange!.TargetSeat) throw new InvalidOperationException("Wenyi lost its actual HP-one parent and original beneficiary.");
            var payments = ProgramEventHistory<WenyiPaidEvent>().Where(e => e.FrameId == f.Id).ToArray();
            if (r.Paid ? payments is not [var paid] || paid.OwnerSeat != f.OwnerSeat || paid.TargetSeat != r.TargetSeat || paid.HealthWindowId != r.WindowId : payments.Length != 0) throw new InvalidOperationException("Wenyi repeated or changed her accepted aid.");
        }
        else if (f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied } c || c.OwnerSeat != f.OwnerSeat || c.TargetSeat != f.OwnerSeat || c.ParentFrameId != r.WindowId || _resolutionStack.SingleOrDefault(x => x.Id == r.WindowId) is not DamageTriggerWindowFrame window || window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count || window.Candidates[window.CandidateIndex].ToProgramCandidate() is not { } candidate || candidate.OwnerSeat != f.OwnerSeat || candidate.SkillId != f.SkillId || candidate.SkillInstanceId != f.SkillInstanceId || candidate.BindingId != f.TriggerId || candidate.OccurrenceIndex != c.OccurrenceIndex || ProgramEventHistory<MiaoxinBenefitIssuedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.DamageWindowId == r.WindowId && e.Occurrence == c.OccurrenceIndex && e.DrawCount == r.RequestedCount && e.RefreshWenyi == r.RefreshWenyi) != 1)
            throw new InvalidOperationException("Miaoxin lost its exact real damage-point occurrence.");
        if (f.PendingMovementContinuation is { } pending && !IsHuanShuJunMovement(f, GetPausedPrivateOfferEffect(f), pending)) throw new InvalidOperationException("Huan Shu Jun lost her typed movement return.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 28100)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 28100)]
    private bool ContinuePaidHuanShuJun(ProgramSkillFrame f)
    { if (f.HuanShuJun is not { Paid: true }) return false; AssertHuanShuJun(f); return true; }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 160)]
    private bool IsHuanShuJunMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) =>
        f.HuanShuJun is { Paid: true } r && effect?.Op == r.Op && p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null && r.Stage is HuanShuJunStage.AdjustmentChildren or HuanShuJunStage.DrawChildren or HuanShuJunStage.EquipmentGiftChildren or HuanShuJunStage.NativeChildren or HuanShuJunStage.FaceChildren or HuanShuJunStage.ChainChildren;
    private bool HuanShuJunPaidMovement(ProgramSkillFrame f, CardMovementRecord m)
    {
        var r = f.HuanShuJun!;
        if (!_cardMovements.Contains(m)) return false;
        if (ProgramEventHistory<HuanpeiCostEntityEvent>().Any(e => e.FrameId == f.Id && e.CardId == m.CardId && e.Kind == m.CardKind && e.From == m.From && e.Sequence == m.Sequence) && m.Reason.Value == HuanpeiDiscardReason) return true;
        if (ProgramEventHistory<WenyiEquipmentGivenEvent>().Any(e => e.FrameId == f.Id && e.CardId == m.CardId && e.Sequence == m.Sequence && m.To == CardLocation.Hand(e.RecipientSeat)) && m.Reason.Value == "skill-program.huan-shu-jun.wenyi.give") return true;
        if (r.DrawOutcome is { } o && m.Sequence > o.SequenceBefore && m.Sequence <= o.SequenceAfter) return o.ActualDrawIds.Contains(m.CardId) && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == (r.Op == SkillProgramEffectOp.ResolveMiaoxinDamageDrawAndRefresh ? MiaoxinDrawReason : HuanpeiDrawReason) || o.ReplacementGainedIds.Contains(m.CardId) && m.To == CardLocation.Hand(f.OwnerSeat) || m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle;
        return r.Stage == HuanShuJunStage.EquipmentGiftChildren && r.Materials.Any(c => c.Kind == CardKind.WoodenOx && c.From == CardLocation.Equipment(f.OwnerSeat)) && m.From == CardLocation.WoodenOxGrain(f.OwnerSeat) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard && m.Sequence > r.Before && m.Sequence <= r.After;
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 320)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 310)]
    private bool IsHuanShuJunRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery) =>
        f.HuanShuJun is { Paid: true, Stage: HuanShuJunStage.EquipmentGiftChildren } r && RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.SourceSeat == f.OwnerSeat && recovery.Attempt.TargetSeat == f.OwnerSeat && recovery.Attempt.Amount == 1 && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && r.Materials.Any(c => c.Kind == CardKind.SilverLion && c.From == CardLocation.Equipment(f.OwnerSeat)) && recovery.Attempt.Completion.MoveReason?.Value == "skill-program.huan-shu-jun.wenyi.give";
    private bool HuanShuJunFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.HuanShuJun is not { } r) return false;
        AssertHuanShuJun(f);
        if (child is ProgramSkillFrame draft && HuanpeiDraftRidesOn(draft, f)) return true;
        if (WenyiEquipmentSlotPaymentRidesOn(f, child)) return true;
        if (child is DrawFundedDistinctBasicFrame funded) return WenyiDrawFundedParentMatches(f, funded.Payment) && DrawFundedDistinctBasicFrameRidesOn(child, f);
        if (child is RequestedDeckBasicFrame view) return view.Receipt.Intent == RequestedDeckBasicIntent.WenyiPeach && IsWenyiPeachRequest(f) && RequestedDeckBasicFrameRidesOn(child, f);
        if (child is ProgramSkillFrame { Weijie: { } w } weijie) return r.Op == SkillProgramEffectOp.ResolveWenyiHealthOneAid && w.Need.OwnerFrameId == f.Id && w.Need.RequestFrameId == f.Id && w.Need.InstructionIndex == f.InstructionIndex && w.Need.RequestOp == r.Op && w.Need.ActorSeat == f.OwnerSeat && w.Need.ProviderSeat == f.OwnerSeat && w.Need.Targets.SequenceEqual([r.TargetSeat]) && WeijieParent(weijie);
        if (!r.Paid) return false;
        if (child is CardUseFrame use) return r.Stage == HuanShuJunStage.NativeUse && r.Native?.UseId == use.Id && IsIssuedWenyiUse(use);
        if (r.Stage == HuanShuJunStage.Drawing && f.NativeDraw is not null) return NativeDrawAttemptRidesOn(child, f);
        if (child is RecoveryReplacementFrame recovery) return IsHuanShuJunRecoveryParent(f, recovery);
        if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills) return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state) return state.ResumeProgramFrameId == f.Id && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id);
        if (child is not CardsMovedTriggerWindowFrame moved || !ProgramMovementReturnMatches(f, moved) || moved.Batch.OriginOwnerSeat != f.OwnerSeat || moved.Batch.OriginSkillId != f.SkillId || moved.Batch.OriginSkillInstanceId != f.SkillInstanceId || moved.Batch.Movements.Count == 0) return false;
        if (r.Stage == HuanShuJunStage.NativeChildren && r.NativeReturned && r.Native is { } native) return moved.Batch.ParentFrameId == native.UseId && ProgramEventHistory<WenyiNativeReturnedEvent>().Count(e => e.FrameId == f.Id && e.UseId == native.UseId && e.ActionId == native.ActionId) == 1;
        return moved.Batch.ParentFrameId == f.Id && moved.Batch.Movements.All(m => HuanShuJunPaidMovement(f, m));
    }
    private bool HuanShuJunReplacementObserverEdge(int index)
    {
        var parent = _resolutionStack[index - 1]; var child = _resolutionStack[index];
        return parent is ProgramSkillFrame { Zenrun: not null } replacement && ZenrunFirstChild(replacement, child) ||
            TypedDamagePostEventEdge(parent, child);
    }
    private bool HuanShuJunObserverSuffix(int i) => HasObserverSuffix(i, static (g, n) => g.IsMovementObserverEdge(n) || g.HuanShuJunReplacementObserverEdge(n) || g.IsSameNameHandObserverEdge(n) || g.IsYanPuDyingExitEdge(n) || g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) || NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]), static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d));
    private ProgramSkillFrame? HuanShuJunObserverRoot() => FindProgramObserverRoot(static (g, f, c) => g.HuanShuJunFirstChild(f, c), static (g, i) => g.HuanShuJunObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1090)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1200)]
    private bool HasHuanShuJunDamageObserver(long id)
    {
        if (HuanShuJunObserverRoot() is not { } root || _resolutionStack.SingleOrDefault(x => x.Id == id) is not DamageTriggerWindowFrame window) return false;
        if (_resolutionStack.FindIndex(x => x.Id == id) > _resolutionStack.FindIndex(x => x.Id == root.Id)) return true;
        return root.HuanShuJun is { Op: SkillProgramEffectOp.ResolveMiaoxinDamageDrawAndRefresh, Paid: true } r &&
            r.WindowId == window.Id && root.WindowContext?.ParentFrameId == window.Id && window.TriggerWindow == SkillProgramTriggerWindow.AfterDamageApplied && DamageProgramMatchesWindow(root, window);
    }
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 1230)]
    private bool HasHuanShuJunBeforeDamageObserver(long id) => HuanShuJunObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 1060)]
    private bool IsHuanShuJunDying()
    {
        if (ActiveDying is not { } d) return false;
        var position = _resolutionStack.FindIndex(x => x.Id == d.Id);
        if (HuanpeiActiveDraft() is { HuanShuJun.Request: { Intent: HuanpeiIntent.Dying } n } draft &&
            n.ParentFrameId == d.Id && n.RequestFrameId == d.Id && n.ActorSeat == d.ResponderSeat && n.Cursor == d.ResponderIndex && n.FixedTargetSeat == d.VictimSeat &&
            HuanShuJunPaidOrPreparing(draft) && HuanpeiDyingRescueRide(position, d)) return true;
        return HuanShuJunObserverRoot() is { } f && position > _resolutionStack.FindIndex(x => x.Id == f.Id);
    }
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1630)]
    private bool AllowsHuanShuJunNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || HuanShuJunObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature && target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 28100)]
    private bool AdvanceHuanShuJunChildren() => _pendingDecision is null && HuanShuJunObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
