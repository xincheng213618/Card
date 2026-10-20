namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int MouXuHuangProjectedPhaseUses(int owner, string skill, int fallback) => skill == MouXuHuangComposition.Duanliang &&
        CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase && IsRecordedActualDiscardRecoveryPhase(phase)
            ? ProgramEventHistory<MouXuHuangMouyiStartedEvent>().Count(e => e.Source.OwnerSeat == owner && e.Source.SkillId == skill && e.Phase == phase) : fallback;
    private bool ValidMouXuHuangShipo(ProgramSkillFrame f)
    {
        if (f.MouXuHuangShipo is not { } r || !MouXuHuangShipoRoot(f) || r.Source != MouXuHuangSource(f) || r.GameplayHash != f.GameplayHash ||
            r.EndingWindowId != f.WindowContext!.ParentFrameId || r.Phase.Kind != ActualDiscardRecoveryPhaseKind.Ending || r.Phase.ActorSeat != f.OwnerSeat ||
            !IsRecordedActualDiscardRecoveryPhase(r.Phase) || !Enum.IsDefined(r.Stage) || r.Cursor < 0 || r.Cursor > r.Targets.Count ||
            r.Targets.Any(t => !IsValidPlayerSeat(t) || t == f.OwnerSeat) || r.Targets.Distinct().Count() != r.Targets.Count ||
            r.SelectedCardIds.Distinct().Count() != r.SelectedCardIds.Count ||
            ProgramEventHistory<MouXuHuangShipoStartedEvent>().Count(e => e == new MouXuHuangShipoStartedEvent(f.Id, r.Source, r.GameplayHash, r.EndingWindowId, r.Phase, r.OwnerHp)) != 1) return false;
        if (!r.Accepted) return r.Stage == MouXuHuangShipoStage.ModeChoice && r.Mode is null && r.Targets.Count == 0 && r.Gifts.Count == 0;
        if (r.Mode is not ("mode-single" or "mode-all") || r.Targets.Count == 0 || r.Mode == "mode-single" && r.Targets.Count != 1 ||
            !r.Targets.SequenceEqual(r.Targets.OrderBy(t => (t - f.OwnerSeat + _players.Count) % _players.Count)) ||
            ProgramEventHistory<MouXuHuangShipoModeAcceptedEvent>().Count(e => e == new MouXuHuangShipoModeAcceptedEvent(f.Id, r.Mode, r.Targets.Count)) != 1) return false;
        var targets = ProgramEventHistory<MouXuHuangShipoTargetFrozenEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).ToArray();
        if (targets.Length != r.Targets.Count || targets.Where((e, i) => e.Index != i || e.TargetSeat != r.Targets[i]).Any()) return false;
        var completed = ProgramEventHistory<MouXuHuangShipoRecipientCompletedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (completed.Length != r.Cursor || completed.Select(e => e.Cursor).Distinct().Count() != r.Cursor ||
            completed.Any(e => e.Cursor < 0 || e.Cursor >= r.Cursor || e.TargetSeat != r.Targets[e.Cursor])) return false;
        if (r.Gifts.Select(g => g.Cursor).Distinct().Count() != r.Gifts.Count || r.Gifts.Any(g => g.Cursor < 0 || g.Cursor >= r.Targets.Count ||
            g.GiverSeat != r.Targets[g.Cursor] || g.From != CardLocation.Hand(g.GiverSeat) ||
            ProgramEventHistory<MouXuHuangShipoGiftPaidEvent>().Count(e => e == new MouXuHuangShipoGiftPaidEvent(f.Id, g.Cursor, g.GiverSeat, g.CardId, g.To, g.Sequence)) != 1 ||
            _cardMovements.Count(m => m.Sequence == g.Sequence && m.CardId == g.CardId && m.From == g.From && m.To == g.To && m.Reason.Value == MouXuHuangGiveReason) != 1)) return false;
        var damage = ProgramEventHistory<MouXuHuangShipoDamageIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (damage.Select(e => e.Cursor).Distinct().Count() != damage.Length || damage.Any(e => e.Cursor < 0 || e.Cursor >= r.Targets.Count ||
            e.SourceSeat != f.OwnerSeat || e.TargetSeat != r.Targets[e.Cursor] || e.Amount != 1 || r.Gifts.Any(g => g.Cursor == e.Cursor))) return false;
        if (r.Stage == MouXuHuangShipoStage.Damage && damage.Count(e => e.Cursor == r.Cursor) != 1) return false;
        var selected = ProgramEventHistory<MouXuHuangShipoTransferSelectedEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).ToArray();
        return selected.Length == r.SelectedCardIds.Count && selected.Select((e, i) => e.Index == i && e.CardId == r.SelectedCardIds[i] && r.Gifts.Any(g => g.CardId == e.CardId && g.To == CardLocation.Hand(f.OwnerSeat))).All(v => v);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 29500)]
    private void AssertMouXuHuang(ProgramSkillFrame f)
    {
        if (f.MouXuHuangShipo is not null && !ValidMouXuHuangShipo(f)) throw new InvalidOperationException("Shipo lost its exact accepted Ending roster, paid recipient invoice or original obtained subset.");
        if (f.MouXuHuangDuanliang is not { } r) return;
        if (!MouXuHuangDuanliangRoot(f) || !Enum.IsDefined(r.Stage) || r.Before > r.After || r.After > _movementSequence)
            throw new InvalidOperationException("Duanliang lost its source-bound actual Play, sealed commitments or paid native invoice.");
        var selectedTargets = ProgramEventHistory<MouXuHuangDuelTargetSelectedEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).ToArray();
        if (selectedTargets.Length != r.NativeTargets.Count || selectedTargets.Where((e, i) => e.Index != i || e.TargetSeat != r.NativeTargets[i]).Any() ||
            r.NativeTargets.Count > 0 && (r.NativeTargets[0] != r.TargetSeat || r.NativeTargets.Any(t => !IsValidPlayerSeat(t) || t == f.OwnerSeat) ||
                r.NativeTargets.Distinct().Count() != r.NativeTargets.Count || selectedTargets.Any(e => e.Adjusted != selectedTargets[0].Adjusted)))
            throw new InvalidOperationException("Duanliang's original native Duel target plan lost its exact scalar selections.");
        var commits = ProgramEventHistory<MouXuHuangStrategyCommittedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (commits.Count(e => e.Attacker && e.ParticipantSeat == f.OwnerSeat) != (r.Attack is null ? 0 : 1) ||
            commits.Count(e => !e.Attacker && e.ParticipantSeat == r.TargetSeat) != (r.Defense is null ? 0 : 1) || commits.Length != (r.Attack is null ? 0 : 1) + (r.Defense is null ? 0 : 1) ||
            r.Revealed != (r.Attack is not null && r.Defense is not null)) throw new InvalidOperationException("Mouyi cannot expose or repeat a participant's sealed commitment.");
        var reveals = ProgramEventHistory<MouXuHuangMouyiRevealedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Revealed ? reveals is not [var reveal] || reveal.Attack != r.Attack || reveal.Defense != r.Defense || reveal.Success != r.Success ||
            r.Success != (r.Attack == MouXuHuangAttack.Siege ? r.Defense == MouXuHuangDefense.BlockAdvance : r.Defense == MouXuHuangDefense.BlockSiege) : reveals.Length != 0)
            throw new InvalidOperationException("Mouyi must jointly reveal its exact two commitments and four-cell outcome once.");
        if (r.Native is { } o && (o.ProgramFrameId != f.Id || o.Source != r.Source || o.GameplayHash != r.GameplayHash ||
            ProgramEventHistory<MouXuHuangNativeIssuedEvent>().Count(e => e.Origin == o) != 1 ||
            r.Stage == MouXuHuangDuanliangStage.NativeUse && !IsMouXuHuangNativeUse(o.UseId) ||
            r.NativeReturned && (LifecycleCardUse(o.UseId) is not null || ProgramEventHistory<MouXuHuangNativeReturnedEvent>().Count(e => e == new MouXuHuangNativeReturnedEvent(f.Id, o.UseId, o.ActionId)) != 1)))
            throw new InvalidOperationException("Duanliang changed its signed physical top or genuine empty-material Duel use.");
        if (r.PaymentSequence is { } paid && (r.Native is not { CardId: > 0 } origin ||
            ProgramEventHistory<MouXuHuangTopPaidEvent>().Count(e => e == new MouXuHuangTopPaidEvent(f.Id, origin.UseId, origin.CardId, paid)) != 1 ||
            _cardMovements.Count(m => m.Sequence == paid && m.CardId == origin.CardId && m.From == CardLocation.DrawPile && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) != 1))
            throw new InvalidOperationException("Duanliang cannot replace or repay its original top entity.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 29500)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 29500)]
    private bool PermitAcceptedMouXuHuang(ProgramSkillFrame f)
    {
        if (f.MouXuHuangDuanliang is null && f.MouXuHuangShipo is not { Accepted: true }) return false;
        AssertMouXuHuang(f); return true;
    }
    private void SignMouXuHuangMovement(ProgramSkillFrame f, CardMovementRecord m)
    {
        if (!_cardMovements.Contains(m)) throw new InvalidOperationException("Xu Huang cannot sign a nonexistent native movement.");
        var fact = new MouXuHuangMovementSignedEvent(f.Id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence);
        if (!ProgramEventHistory<MouXuHuangMovementSignedEvent>().Contains(fact)) AdvanceEventRulesAndQueueFact(fact);
    }
    private bool MouXuHuangMovementSigned(ProgramSkillFrame f, CardMovementRecord m) => _cardMovements.Contains(m) &&
        ProgramEventHistory<MouXuHuangMovementSignedEvent>().Count(e => e == new MouXuHuangMovementSignedEvent(f.Id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence)) == 1;
    private void SealMouXuHuangMovements(long id, long before)
    {
        var f = GetActiveProgramFrame(id); var after = _movementSequence;
        ReplaceRuntimeTop(f = f with { MouXuHuangDuanliang = f.MouXuHuangDuanliang is { } d ? d with { After = after } : null,
            MouXuHuangShipo = f.MouXuHuangShipo is { } s ? s with { After = after } : null });
        foreach (var m in _cardMovements.Where(m => m.Sequence > before && m.Sequence <= after)) SignMouXuHuangMovement(f, m);
    }
    private bool IsMouXuHuangMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        e is not null && MouXuHuangComposition.Handles(e.Op) && p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null &&
        (f.MouXuHuangDuanliang is { Stage: MouXuHuangDuanliangStage.RefillChildren or MouXuHuangDuanliangStage.ObtainChildren or MouXuHuangDuanliangStage.NativeChildren } && MouXuHuangDuanliangRoot(f) ||
         f.MouXuHuangShipo is { Stage: MouXuHuangShipoStage.GiftChildren or MouXuHuangShipoStage.TransferChildren } && ValidMouXuHuangShipo(f));
    private bool DrainMouXuHuangChildren(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private bool MouXuHuangFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!PermitAcceptedMouXuHuang(f)) return false;
        if (child is CardUseFrame use) return f.MouXuHuangDuanliang is { Stage: MouXuHuangDuanliangStage.NativeUse, Native: { } o } && use.Id == o.UseId && IsMouXuHuangNativeUse(use.Id);
        if (f.MouXuHuangShipo is { Stage: MouXuHuangShipoStage.Damage } s && f.AttackAttempt is { } a && f.AttackReturn is not null)
            return s.Cursor < s.Targets.Count && a.SourceSeat == f.OwnerSeat && a.TargetSeat == s.Targets[s.Cursor] && !a.SourceLess && DyingSuitsStructuralEdge(f, child);
        if (f.PendingMovementContinuation is not { } p || !IsMouXuHuangMovement(f, GetPausedPrivateOfferEffect(f), p)) return false;
        if (child is CardsMovedTriggerWindowFrame moved)
        {
            if (IsMouXuHuangNativeSettlementBatch(f, moved.Batch)) return moved.ResumeProgramFrameId == f.Id;
            var before = f.MouXuHuangDuanliang?.Before ?? f.MouXuHuangShipo!.Before;
            var after = f.MouXuHuangDuanliang?.After ?? f.MouXuHuangShipo!.After;
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id &&
                moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.OriginOwnerSeat == f.OwnerSeat &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => m.Sequence > before && m.Sequence <= after && MouXuHuangMovementSigned(f, m));
        }
        if (RecoveryReplacementFrameRidesOn(child, f)) return true;
        if (child is HpChangedTriggerWindowFrame hp) return ProgramHpReturnMatches(f, hp);
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouXuHuangNativeWindowEdge(int index)
    {
        if (_resolutionStack[index - 1] is CardUseFrame use && IsMouXuHuangNativeUse(use.Id) && _resolutionStack[index] is ProgramCardTriggerWindowFrame w)
            return w.ParentFrameId == use.Id && w.Action.ActionId == use.Action!.ActionId && w.Action.ActorSeat == use.SourceSeat && w.Action.EffectiveKind == use.CardKind;
        if (_resolutionStack[index - 1] is ProgramCardTriggerWindowFrame parent && LifecycleCardUse(parent.ParentFrameId) is { } native && IsMouXuHuangNativeUse(native.Id) &&
            _resolutionStack[index] is ProgramSkillFrame child && parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count)
        { var c = parent.Candidates[parent.CandidateIndex]; return MountObserverCandidateMatches(child, ToSharedCandidate(c)) && child.WindowContext == CreateCardActionProgramContext(parent, c); }
        return false;
    }
    private bool MouXuHuangObserverSuffix(int index) => HasObserverSuffix(index, static (g, n) =>
        g.IsMovementObserverEdge(n) || g.IsSameNameHandObserverEdge(n) || g.IsYanPuDyingExitEdge(n) || g.TypedDamagePostEventEdge(g._resolutionStack[n - 1], g._resolutionStack[n]) ||
        g.MouXuHuangNativeWindowEdge(n) || g._resolutionStack[n - 1] is ProgramSkillFrame f && g.MouXuHuangFirstChild(f, g._resolutionStack[n]) ||
        NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) || g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]),
        static (g, n, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(n, dying));
    private ProgramSkillFrame? MouXuHuangObserverRoot() => FindProgramObserverRoot(static (g, f, child) => g.MouXuHuangFirstChild(f, child), static (g, index) => g.MouXuHuangObserverSuffix(index));
    private bool HasMouXuHuangDamageObserver(long id) => MouXuHuangObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasMouXuHuangBeforeDamageObserver(long id) => MouXuHuangObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsMouXuHuangDying() => ActiveDying is { } dying && MouXuHuangObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsMouXuHuangNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess) return false;
        if (f.MouXuHuangShipo is { Stage: MouXuHuangShipoStage.Damage } r && ValidMouXuHuangShipo(f))
            return r.Cursor < r.Targets.Count && target == r.Targets[r.Cursor] && amount == 1 && source is null && nature is null;
        if (MouXuHuangObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature &&
            target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    private bool IsMouXuHuangRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return index >= 0 && child > index + 1 && MouXuHuangFirstChild(f, _resolutionStack[index + 1]) && MouXuHuangObserverSuffix(index) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 29500)]
    private bool ResumeMouXuHuang(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id) return false;
        if (f.MouXuHuangDuanliang is not null) { ContinueMouXuHuangDuanliang(id); return true; }
        if (f.MouXuHuangShipo is not null) { ContinueMouXuHuangShipo(id); return true; } return false;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 29500)]
    private bool ReturnMouXuHuangMovement(ProgramSkillFrame f) => f.PendingMovementContinuation is not null && ResumeMouXuHuang(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 29500)]
    private bool AdvanceMouXuHuangChildren() => _pendingDecision is null && MouXuHuangObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
