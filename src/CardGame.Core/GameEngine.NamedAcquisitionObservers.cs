namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool NamedAcquisitionParentMatches(ProgramSkillFrame root) =>
        root.WindowContext is { Window: SkillProgramTriggerWindow.TurnEnding } c &&
        _resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is { } parent &&
        parent.OwnerSeat == root.OwnerSeat && parent.TurnNumber == _turnNumber &&
        parent.ItemIndex >= 0 && parent.ItemIndex < parent.Items.Count &&
        parent.Items[parent.ItemIndex].Candidate is { } candidate && MountObserverCandidateMatches(root, candidate);

    private ProgramSkillFrame? NamedAcquisitionObserverRoot(long? damageWindowId = null)
    {
        for (var index = 1; index + 1 < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not ProgramSkillFrame { NamedCardAcquisition: { Stage: NamedCardAcquisitionStage.MovementChildren } d } root ||
                !ValidNamedCardAcquisition(root) || !NamedAcquisitionParentMatches(root) ||
                root.PendingMovementContinuation is not { CoverageResultBind: null } pending || pending.SubjectSeat != root.OwnerSeat ||
                damageWindowId is { } requested && !_resolutionStack.Skip(index + 1).Any(f => f.Id == requested)) continue;
            var paid = _cardMovements.Single(m => m.Sequence > d.SequenceBefore && m.Sequence <= d.SequenceAfter &&
                m.CardId == d.CardId && m.From == d.From && m.To == CardLocation.Hand(root.OwnerSeat) && m.Reason.Value == NamedAcquisitionReason(root));
            var first = _resolutionStack[index + 1];
            if (first is CardsMovedTriggerWindowFrame moved)
            {
                if (moved.Batch.ParentFrameId != root.Id || moved.Batch.AwaitingProgramFrameId is { } awaiting && awaiting != root.Id ||
                    moved.Batch.OriginOwnerSeat != root.OwnerSeat || moved.Batch.OriginSkillId != root.SkillId ||
                    moved.Batch.OriginSkillInstanceId != root.SkillInstanceId || moved.Batch.Movements is not [var one] || one != paid) continue;
            }
            else if (first is HpChangedTriggerWindowFrame hp)
            {
                if (paid.CardKind != CardKind.SilverLion || paid.From.Zone != CardZoneKind.Equipment ||
                    hp.ResumeFrameId != root.Id || hp.Continuation != PostEventContinuation.AwaitedProgramMovement || hp.Change.ParentFrameId != root.Id ||
                    hp.Change.Kind != HpChangeKind.Recovery || hp.Change.Amount != 1 || hp.Change.SourceSeat != paid.From.OwnerSeat || hp.Change.TargetSeat != paid.From.OwnerSeat) continue;
            }
            else if (first is RecoveryReplacementFrame recovery)
            {
                if (paid.CardKind != CardKind.SilverLion || paid.From.Zone != CardZoneKind.Equipment ||
                    !RecoveryReplacementFrameRidesOn(recovery, root) || recovery.Return.Continuation != PostEventContinuation.AwaitedProgramMovement ||
                    recovery.Attempt.SourceSeat != paid.From.OwnerSeat || recovery.Attempt.TargetSeat != paid.From.OwnerSeat || recovery.Attempt.Amount != 1 ||
                    recovery.Attempt.Completion.Producer != RecoveryAttemptProducer.SilverLion ||
                    recovery.Attempt.Completion.MoveReason?.Value != NamedAcquisitionReason(root)) continue;
            }
            else continue;
            var aligned = true;
            for (var child = index + 2; child < _resolutionStack.Count; child++)
            {
                if (!HalfHandPaidDamageObserverEdge(child)) { aligned = false; break; }
                if (_resolutionStack[child] is DyingFrame dying &&
                    (IsPaidHandRepaymentRescueRide(child, dying) || IsPaidHandRepaymentProgramAlcoholRide(child, dying) ||
                     PaidObserverDamageVirtualAlcoholRide(child, dying) || TieredRoundZeroDyingRescueRide(child, dying))) break;
            }
            if (aligned) return root;
        }
        return null;
    }
    private bool HasNamedAcquisitionDamageObserver(long id) => NamedAcquisitionObserverRoot(id) is not null;
    private bool IsNamedAcquisitionProgramDying() => ActiveDying is not null && NamedAcquisitionObserverRoot() is not null;
    private bool AllowsFireOrNamedNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (_resolutionStack.LastOrDefault()?.Id != observer.Id || observer.AttackAttempt is not null || observer.InstructionIndex < 1 ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.AfterHpRecovered or
                SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.AfterHpLost) ||
            FireTargetBenefitObserverRoot() is null && NamedAcquisitionObserverRoot() is null) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!)
            .GetPausedInstruction(observer.InstructionIndex).Effect;
        return effect.Op == SkillProgramEffectOp.Damage && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature &&
            !sourceLess && target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, effect.Target));
    }

    private bool CanStartLostHpTargetSelection(CharacterState owner, SkillProgramActivation activation)
    {
        if (!activation.Effects.Any(e => e.NumberExpression == SkillProgramNumberExpression.OwnerLostHpAtLeastOne)) return true;
        var selection = activation.Effects.FirstOrDefault(e => e.Op == SkillProgramEffectOp.SelectOwnedCards);
        if (selection is null || selection.Target != SkillProgramEffectTarget.Owner) return false;
        return selection.Zones.SelectMany(zone => _cardZones.CardsAt(new(zone, owner.Seat)))
            .Any(card => (selection.Suits.Count == 0 || selection.Suits.Contains(GetProgramEffectiveSuit(owner, card))) &&
                !IsSelfHandCategoryDiscardForbidden(owner.Seat, card, _cardZones.GetLocation(card.Id), OwnedCardMoveIntent.Discard));
    }
    private bool HasLostHpTargetSelection(ProgramSkillFrame frame) => ProgramInstructionResolver.Default
        .Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).Instructions
        .Any(e => e.NumberExpression == SkillProgramNumberExpression.OwnerLostHpAtLeastOne);
}
