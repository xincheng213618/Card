namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static readonly CardMoveReason DamageTargetSkillRoundDrawReason = new("program.damage-target-skill.round-draw");
    private static ProgramSkillGrantIdentity RoundSkillIdentity(SkillGrant grant) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_DamageTargetSkillRoundDraw.RoundSkillIdentity(grant);
    private static bool SameRoundSkillIdentity(SkillGrant grant, ProgramSkillGrantIdentity identity) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_DamageTargetSkillRoundDraw.SameRoundSkillIdentity(grant,identity);
    private IEnumerable<SkillGrant> OwnedAndRestorableRoundSkillGrants(CharacterState owner, string skillId)
    {
        foreach (var grant in owner.SkillGrants.Grants.Where(g => g.SkillId == skillId)) yield return grant;
        foreach (var lease in _orderedPrintedSkillLossLeases.Where(l => l.Source.OwnerSeat == owner.Seat))
        {
            var general = lease.TemplateSourceId == CharacterState.SecondarySkillSource ? owner.SecondaryGeneral : owner.General;
            if (general?.Id != lease.GeneralId) continue;
            foreach (var grant in lease.RemovedGrants.Where(g => g.SkillId == skillId))
            {
                var loss = ProgramEventHistory<OrderedPrintedSkillGrantRemovedEvent>().Single(e => e.FrameId == lease.ProgramFrameId && e.Grant == grant);
                if (!WasGrantPermanentlyRevokedAfterLoss(owner.Seat, grant, loss) && !RandomDamageTargetSkillGrantExpired(grant)) yield return grant;
            }
        }
        foreach (var loss in HellSoulOutstandingLosses(owner.Seat))
            if (loss.Grant.SkillId == skillId && owner.General.Id == loss.GeneralId && owner.SecondaryGeneral?.Id == loss.SecondaryGeneralId &&
                !WasGrantPermanentlyRevokedAfterLoss(owner.Seat, loss.Grant, loss) && loss.Grant.PhaseExpiry is null &&
                !RandomDamageTargetSkillGrantExpired(loss.Grant)) yield return loss.Grant;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 1247)]
    private bool CanOfferDamageTargetRoundSkill(SkillProgramTrigger trigger, ProgramSkillWindowContext context, int ownerSeat) =>
        !GrantDamageTargetSkillAndDrawByRoundDescriptor.IsTrigger(trigger) ||
        context is { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0, SourceSeat: { } source, TargetSeat: { } target } &&
        source == ownerSeat && source != target && _players[source].TeamRevealed && _players[target].TeamRevealed && PublicTeamEnemy(source, _players[target]) &&
        _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is { } window && !GetDamageTriggerAttack(window).IsSourceLess;

    private SkillProgramStepOutcome GrantDamageTargetSkillAndDrawByRound(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.InstructionIndex != 1 || frame.DamageTargetSkillRoundDraw is not null ||
            !GrantDamageTargetSkillAndDrawByRoundDescriptor.IsTrigger(GetProgramTrigger(frame)) ||
            !ExactOrderedPrintedSkillLossParent(frame, out var window, out var damage, out var attack) ||
            attack.IsSourceLess || attack.SourceSeat != frame.OwnerSeat || attack.DamageAmount <= 0 ||
            !_players[frame.OwnerSeat].TeamRevealed || !_players[damage.TargetSeat].TeamRevealed || !PublicTeamEnemy(frame.OwnerSeat, _players[damage.TargetSeat]))
            throw new InvalidOperationException("A damage target skill grant requires its exact positive native source segment and public enemy.");
        var owner = _players[frame.OwnerSeat]; var target = _players[damage.TargetSeat];
        if (!owner.IsAlive || !HasRuntimeSkillInstance(owner, frame.SkillId, frame.SkillInstanceId)) return SkillProgramStepOutcome.Continue;
        var effect = GetProgramTrigger(frame).Effects.Single(); var skill = effect.SkillIds.Single();
        _ = _contentRegistry.GetSkill(skill);
        var existing = target.IsAlive ? OwnedAndRestorableRoundSkillGrants(target, skill).OrderBy(g => g.GrantId, StringComparer.Ordinal).FirstOrDefault() : null;
        var sourceId = $"acquired:damage-target-round:{frame.OwnerSeat}:{frame.SkillId}:{frame.SkillInstanceId}:{frame.Id}";
        var grant = existing is null && target.IsAlive ? new SkillGrant(sourceId + ":" + skill, skill, sourceId + ":" + skill, sourceId) : null;
        var receipt = new DamageTargetSkillRoundDrawReceipt(frame.Id, new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId),
            frame.GameplayHash, window.Id, damage.Id, target.Seat, damage.Amount, damage.Nature, _roundNumber, Math.Min(effect.Amount, Math.Max(0, _roundNumber)), skill,
            grant is null ? null : RoundSkillIdentity(grant), existing is null ? null : RoundSkillIdentity(existing))
            { TargetDeadAtGrant = !target.IsAlive };
        ReplaceRuntimeTop(frame = frame with { DamageTargetSkillRoundDraw = receipt });
        if (grant is not null)
        {
            foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
            target.SkillGrants.Grant(grant); RegisterTaggedConversionSkill(target, skill);
        }
        AdvanceEventRulesAndQueueFact(new DamageTargetSkillRoundGrantedEvent(frame.Id, receipt.Source, receipt.GameplayHash,
            receipt.DamageWindowId, receipt.DamageFrameId, receipt.TargetSeat, receipt.DamageAmount, receipt.Nature, receipt.RoundNumber,
            receipt.DrawCount, skill, receipt.GrantedGrant, receipt.ExistingGrant, receipt.TargetDeadAtGrant));
        if (grant is not null) AdvanceEventRulesAndQueueFact(new SkillsAcquiredEvent(target.Seat, frame.SkillId, Array.AsReadOnly(new[] { skill })));
        ResumeDamageTargetSkillRoundDraw(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 1247)]
    private bool ResumeDamageTargetSkillRoundDraw(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { DamageTargetSkillRoundDraw: { } r } frame || frame.Id != id) return false;
        RequireDamageTargetSkillRoundDraw(frame);
        if (r.Stage == DamageTargetSkillRoundDrawStage.DrawAttempt) throw new InvalidOperationException("A round draw must return through its owned native request.");
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return true;
        frame = GetActiveProgramFrame(id); r = frame.DamageTargetSkillRoundDraw!;
        if (r.Stage == DamageTargetSkillRoundDrawStage.GrantChildren && _players[frame.OwnerSeat].IsAlive && _winner == Winner.None && r.DrawCount > 0)
        {
            ReplaceRuntimeTop(frame = frame with { DamageTargetSkillRoundDraw = r with { Stage = DamageTargetSkillRoundDrawStage.DrawAttempt }, PendingMovementContinuation = null });
            AdvanceEventRulesAndQueueFact(new DamageTargetSkillRoundDrawRequestedEvent(id, frame.OwnerSeat, r.DrawCount));
            BeginOperationProgramDraw(frame, frame.OwnerSeat, r.DrawCount, DamageTargetSkillRoundDrawReason); return true;
        }
        AdvanceEventRulesAndQueueFact(new DamageTargetSkillRoundCompletedEvent(id, r.DrawOutcome is not null,
            r.Cancelled || r.Stage == DamageTargetSkillRoundDrawStage.GrantChildren && (!_players[frame.OwnerSeat].IsAlive || _winner != Winner.None)));
        ReplaceRuntimeTop(frame = frame with { PendingMovementContinuation = null });
        FinishProgramSkill(frame, true); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 1247)]
    private bool ReturnDamageTargetSkillRoundMovement(ProgramSkillFrame frame) => frame.DamageTargetSkillRoundDraw is not null && ResumeDamageTargetSkillRoundDraw(frame.Id);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.GrantDamageTargetSkillAndDrawByRound)]
    private void ReturnDamageTargetSkillRoundDraw(ProgramSkillFrame supplied, NativeDrawOutcome outcome)
    {
        var frame = GetActiveProgramFrame(supplied.Id); RequireDamageTargetSkillRoundDraw(frame); var r = frame.DamageTargetSkillRoundDraw!;
        if (r.Stage != DamageTargetSkillRoundDrawStage.DrawAttempt || frame.NativeDraw is not null ||
            outcome.RecipientSeat != frame.OwnerSeat || outcome.RequestedCount != r.DrawCount || outcome.SequenceAfter != _movementSequence)
            throw new InvalidOperationException("The damage-source round draw lost its exact native operation return.");
        var cancelled = !_players[frame.OwnerSeat].IsAlive || _winner != Winner.None;
        ReplaceRuntimeTop(frame = frame with { DamageTargetSkillRoundDraw = r with { Stage = DamageTargetSkillRoundDrawStage.DrawChildren,
            DrawOutcome = outcome, Cancelled = cancelled }, PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new DamageTargetSkillRoundDrawPaidEvent(frame.Id, frame.OwnerSeat, outcome.RequestedCount, outcome.ActualDrawIds.Count,
            outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId, outcome.Replacement is not null, cancelled));
        ResumeDamageTargetSkillRoundDraw(frame.Id);
    }
    private void RequireDamageTargetSkillRoundDraw(ProgramSkillFrame frame)
    {
        if (frame.DamageTargetSkillRoundDraw is not { } r || r.ProgramFrameId != frame.Id || frame.InstructionIndex != 1 ||
            r.Source != new CardConversionSource(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId) || r.GameplayHash != frame.GameplayHash ||
            !GrantDamageTargetSkillAndDrawByRoundDescriptor.IsTrigger(GetProgramTrigger(frame)) ||
            !ExactOrderedPrintedSkillLossParent(frame, out var window, out var damage, out var attack) || attack.IsSourceLess || attack.SourceSeat != frame.OwnerSeat ||
            r.DamageWindowId != window.Id || r.DamageFrameId != damage.Id || r.TargetSeat != damage.TargetSeat || r.DamageAmount != damage.Amount || r.Nature != damage.Nature ||
            r.DamageAmount <= 0 || r.RoundNumber < 0 || r.DrawCount != Math.Min(Math.Max(0, r.RoundNumber), GetProgramTrigger(frame).Effects[0].Amount) ||
            r.GrantedSkillId != GetProgramTrigger(frame).Effects[0].SkillIds.Single() || !Enum.IsDefined(r.Stage) ||
            r.GrantedGrant is { } grant && (grant.SkillId != r.GrantedSkillId || grant.SourceId != $"acquired:damage-target-round:{frame.OwnerSeat}:{frame.SkillId}:{frame.SkillInstanceId}:{frame.Id}" ||
                grant.GrantId != grant.SourceId + ":" + r.GrantedSkillId || grant.SkillInstanceId != grant.GrantId || r.ExistingGrant is not null) ||
            r.ExistingGrant is { } prior && prior.SkillId != r.GrantedSkillId ||
            (r.TargetDeadAtGrant ? r.GrantedGrant is not null || r.ExistingGrant is not null : (r.GrantedGrant is null) == (r.ExistingGrant is null)) ||
            ProgramEventHistory<DamageTargetSkillRoundGrantedEvent>().Count(e => e == new DamageTargetSkillRoundGrantedEvent(frame.Id, r.Source, r.GameplayHash,
                r.DamageWindowId, r.DamageFrameId, r.TargetSeat, r.DamageAmount, r.Nature, r.RoundNumber, r.DrawCount, r.GrantedSkillId, r.GrantedGrant, r.ExistingGrant, r.TargetDeadAtGrant)) != 1)
            throw new InvalidOperationException("A damage target skill/round draw lost its exact paid source, segment or grant identity.");
        var requests = ProgramEventHistory<DamageTargetSkillRoundDrawRequestedEvent>().Where(e => e.ProgramFrameId == frame.Id).ToArray();
        if (r.Stage == DamageTargetSkillRoundDrawStage.GrantChildren)
        { if (requests.Length != 0 || r.DrawOutcome is not null || frame.NativeDraw is not null) throw new InvalidOperationException("An unrequested round draw cannot have a native payment."); return; }
        if (requests is not [var request] || request.OwnerSeat != frame.OwnerSeat || request.RequestedCount != r.DrawCount || r.DrawCount <= 0)
            throw new InvalidOperationException("A round draw changed its once-frozen request.");
        if (r.Stage == DamageTargetSkillRoundDrawStage.DrawAttempt)
        {
            if (r.DrawOutcome is not null || frame.PendingMovementContinuation is not null || frame.NativeDraw is { } native &&
                (native.Operation != SkillProgramEffectOp.GrantDamageTargetSkillAndDrawByRound || native.ReturnKind != NativeDrawAttemptReturnKind.ProgramOperation ||
                 native.RequestedCount != r.DrawCount || !native.Recipients.SequenceEqual([frame.OwnerSeat]) || native.Reason != DamageTargetSkillRoundDrawReason))
                throw new InvalidOperationException("A round draw lost its exact outstanding native attempt.");
            return;
        }
        if (r.DrawOutcome is not { } outcome || frame.NativeDraw is not null || outcome.RecipientSeat != frame.OwnerSeat || outcome.RequestedCount != r.DrawCount ||
            outcome.SequenceBefore > outcome.SequenceAfter || outcome.SequenceAfter > _movementSequence || outcome.ActualDrawIds.Count > r.DrawCount ||
            outcome.ActualDrawIds.Distinct().Count() != outcome.ActualDrawIds.Count || (r.Cancelled || outcome.Replacement is not null) && outcome.ActualDrawIds.Count != 0 ||
            !_cardMovements.Where(m => m.Sequence > outcome.SequenceBefore && m.Sequence <= outcome.SequenceAfter && m.From == CardLocation.DrawPile &&
                m.To == CardLocation.Hand(frame.OwnerSeat) && m.Reason == DamageTargetSkillRoundDrawReason).OrderBy(m => m.Sequence).Select(m => m.CardId).SequenceEqual(outcome.ActualDrawIds) ||
            ProgramEventHistory<DamageTargetSkillRoundDrawPaidEvent>().Count(e => e == new DamageTargetSkillRoundDrawPaidEvent(frame.Id, frame.OwnerSeat, r.DrawCount,
                outcome.ActualDrawIds.Count, outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId, outcome.Replacement is not null, r.Cancelled)) != 1 ||
            outcome.AttemptFrameId is { } attempt && ProgramEventHistory<NativeDrawAttemptDecidedEvent>().Count(e => e.AttemptFrameId == attempt && e.ParentFrameId == frame.Id &&
                e.RecipientSeat == frame.OwnerSeat && e.RequestedCount == r.DrawCount && e.Replaced == (outcome.Replacement is not null)) != 1)
            throw new InvalidOperationException("A round draw lost its exact actual native materials or paid outcome.");
    }
    private bool ValidDamageTargetSkillRoundDraw(ProgramSkillFrame frame)
    { RequireDamageTargetSkillRoundDraw(frame); return true; }
    private ProgramSkillFrame? DamageTargetSkillRoundDamageCursorRoot(DamageTriggerWindowFrame window, out long? dyingId)
    {
        dyingId = null;
        var index = _resolutionStack.FindIndex(f => f.Id == window.Id);
        if (index < 0 || index + 1 >= _resolutionStack.Count ||
            _resolutionStack[index + 1] is not ProgramSkillFrame { DamageTargetSkillRoundDraw: { } r } root ||
            r.DamageWindowId != window.Id || !DamageProgramMatchesWindow(root, window) || !ValidDamageTargetSkillRoundDraw(root)) return null;
        if (index + 1 == _resolutionStack.Count - 1) return root;
        if (OrderedPrintedSkillLossObserverRoot(root.Id)?.Id != root.Id) return null;
        if (ActiveDying is { } dying && _resolutionStack.FindIndex(f => f.Id == dying.Id) > index + 1) dyingId = dying.Id;
        return root;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 1247)]
    private void AssertDamageTargetSkillRoundDraw(ProgramSkillFrame frame) { if (frame.DamageTargetSkillRoundDraw is not null) RequireDamageTargetSkillRoundDraw(frame); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 1247)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 1247)]
    private bool ContinuePaidDamageTargetRoundSkill(ProgramSkillFrame frame)
    { if (frame.DamageTargetSkillRoundDraw is null) return false; RequireDamageTargetSkillRoundDraw(frame); return true; }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 500)]
    private bool IsDamageTargetSkillRoundMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        e?.Op == SkillProgramEffectOp.GrantDamageTargetSkillAndDrawByRound && f.DamageTargetSkillRoundDraw is not null &&
        p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    [ProgramCapability]
    private sealed class DamageTargetSkillRoundCapability(GameEngine engine) : IDamageTargetSkillRoundDrawHost
    { public SkillProgramStepOutcome GrantDamageTargetSkillAndDrawByRound(ProgramSkillFrame frame) => engine.GrantDamageTargetSkillAndDrawByRound(frame); }
}
