namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome SelectIssuedFixedRecipientWithDeathReturn(ProgramSkillFrame supplied, SkillProgramEffect effect)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.OwnedDeathBenefitReturn is not null || !ExactFixedRecipientDeath(f, out var window))
            throw new InvalidOperationException("An owned death return requires the exact original OwnerDied producer.");
        var index = _resolutionStack.FindIndex(frame => frame.Id == f.Id);
        var death = (DeathFrame)_resolutionStack[index - 2];
        var dying = death.ReturnKind == DeathReturnKind.Dying && index >= 3 && _resolutionStack[index - 3] is DyingFrame d && d.Id == death.ParentFrameId ? d : null;
        if (death.ReturnKind == DeathReturnKind.Dying && dying is null || death.ReturnKind is null ||
            death.ReturnKind == DeathReturnKind.ProgramSkill && (index < 3 || _resolutionStack[index - 3] is not ProgramSkillFrame parent || parent.Id != death.ParentFrameId))
            throw new InvalidOperationException("The original death lost its typed parent.");
        var outcome = SelectIssuedFixedRecipient(f, effect);
        if (outcome != SkillProgramStepOutcome.Continue) return outcome;
        f = GetActiveProgramFrame(f.Id);
        var originalDamage = dying?.Continuation == DyingContinuationKind.Damage
            ? _resolutionStack.OfType<DamageFrame>().SingleOrDefault(d => d.Id == dying!.ParentFrameId) : null;
        if (dying?.Continuation == DyingContinuationKind.Damage && originalDamage is null)
            throw new InvalidOperationException("The original damage Dying lost its actual damage frame.");
        var receipt = new ProgramOwnedDeathBenefitReturn(f.Id, window.Id, window.CandidateIndex, window.Step,
            window.Candidates[window.CandidateIndex], new(death.Id, death.ParentFrameId, death.VictimSeat, death.KillerSeat,
                death.ReturnKind, death.Step, death.OwnerDiedProgramsResolved, death.KillerProgramsResolved, death.CleanedUpCardIds),
            dying is null ? null : new(dying.Id, dying.ParentFrameId, dying.VictimSeat, dying.KillerSeat, dying.Continuation, dying.Step,
                dying.ResponderIndex, dying.ResponderSeats, dying.AttemptedSelfDyingBindings),
            originalDamage is null ? null : new(originalDamage.Id, originalDamage.ParentFrameId, originalDamage.SourceSeat,
                originalDamage.TargetSeat, originalDamage.Amount, originalDamage.Nature, originalDamage.Step),
            CurrentDamageAttempt?.ResolutionId, _turnNumber, _currentSeat);
        ReplaceRuntimeTop(f with { OwnedDeathBenefitReturn = receipt });
        AdvanceEventRulesAndQueueFact(new OwnedDeathBenefitReturnIssuedEvent(f.Id, window.Id, death.Id, dying?.Id,
            f.OwnerSeat, f.FixedRecipient!.RecipientSeat, PairBenefitSource(f), f.GameplayHash, OwnedDeathBenefitReturnHash(receipt)));
        return SkillProgramStepOutcome.Continue;
    }

    // Structural only: calling ActiveDying or a paid-observer helper here would recurse through its filter.
    private bool ExactOwnedDeathBenefitReturn(ProgramSkillFrame f)
    {
        if (f.OwnedDeathBenefitReturn is not { } r || f.FixedRecipient is not { DeathReplay: true } fixedRecipient ||
            r.ProgramFrameId != f.Id || r.ActualTurnNumber != _turnNumber || r.ActualTurnOwnerSeat != _currentSeat ||
            fixedRecipient.OwnerDeathWindowFrameId != r.OwnerDeathWindowFrameId || f.OwnerSeat != r.OriginalDeath.VictimSeat ||
            !IsValidPlayerSeat(f.OwnerSeat) || _players[f.OwnerSeat].IsAlive || f.InstructionIndex is < 1 or > 3 ||
            f.SelectedCardIds.Count != 0 || fixedRecipient.InstructionIndex != 0 ||
            fixedRecipient.ActualTurnNumber != r.ActualTurnNumber || fixedRecipient.ActualTurnOwnerSeat != r.ActualTurnOwnerSeat ||
            fixedRecipient.Source.SkillId != f.SkillId || fixedRecipient.Source.OwnerSeat != f.OwnerSeat ||
            fixedRecipient.Source.SkillInstanceId != f.SkillInstanceId || fixedRecipient.GameplayHash != f.GameplayHash ||
            !f.SelectedTargetSeats.SequenceEqual([fixedRecipient.RecipientSeat]) || !IsValidPlayerSeat(fixedRecipient.RecipientSeat) ||
            OriginalFixedRecipient(f.OwnerSeat, f.SkillId, f.GameplayHash, fixedRecipient.StateId) is not { } issuance ||
            issuance.ProgramFrameId != fixedRecipient.IssuanceProgramFrameId || issuance.Source != fixedRecipient.Source ||
            issuance.RecipientSeat != fixedRecipient.RecipientSeat ||
            !ExactFixedRecipientDeath(f, out var window) || window.Id != r.OwnerDeathWindowFrameId ||
            window.CandidateIndex != r.CandidateIndex || window.Step != r.WindowStep || window.Candidates[r.CandidateIndex] != r.Candidate ||
            r.Candidate.OwnerSeat != f.OwnerSeat || r.Candidate.SkillId != f.SkillId || r.Candidate.BindingId != f.TriggerId ||
            r.Candidate.SkillInstanceId != f.SkillInstanceId || r.Candidate.GameplayHash != f.GameplayHash ||
            _contentRegistry.Skills.GetValueOrDefault(f.SkillId)?.Program is not { } program || program.GameplayHash != f.GameplayHash ||
            ProgramInstructionResolver.Default.Resolve(f, program).Instructions is not
                [{ Op: SkillProgramEffectOp.SelectIssuedFixedRecipientWithDeathReturn, StateId: var state },
                 { Op: SkillProgramEffectOp.Draw, Target: SkillProgramEffectTarget.SelectedTarget, Amount: 3 },
                 { Op: SkillProgramEffectOp.Recover, Target: SkillProgramEffectTarget.SelectedTarget, Amount: 1 }] || state != fixedRecipient.StateId) return false;
        var index = _resolutionStack.FindIndex(frame => frame.Id == f.Id);
        var death = (DeathFrame)_resolutionStack[index - 2]; var old = r.OriginalDeath;
        if (death.Id != old.FrameId || death.ParentFrameId != old.ParentFrameId || death.VictimSeat != old.VictimSeat || death.KillerSeat != old.KillerSeat ||
            death.ReturnKind != old.ReturnKind || death.Step != old.Step || death.OwnerDiedProgramsResolved != old.OwnerDiedProgramsResolved ||
            death.KillerProgramsResolved != old.KillerProgramsResolved || !death.CleanedUpCardIds.SequenceEqual(old.CleanedUpCardIds) ||
            death.PendingRecoveryAttempts.Count != 0 || death.PaidFactionRequestCostRecovery is not null) return false;
        if (r.OriginalDying is { } frozen)
        {
            if (death.ReturnKind != DeathReturnKind.Dying || index < 3 || _resolutionStack[index - 3] is not DyingFrame dying ||
                dying.Id != frozen.FrameId || dying.Id != death.ParentFrameId || dying.ParentFrameId != frozen.ParentFrameId ||
                dying.VictimSeat != frozen.VictimSeat || dying.VictimSeat != death.VictimSeat || dying.KillerSeat != frozen.KillerSeat ||
                dying.Continuation != frozen.Continuation || dying.Step != frozen.Step || dying.ResponderIndex != frozen.ResponderIndex ||
                !dying.ResponderSeats.SequenceEqual(frozen.ResponderSeats) || !dying.AttemptedSelfDyingBindings.SequenceEqual(frozen.AttemptedSelfDyingBindings) ||
                dying.PendingRecoveryAttempts.Count != 0 || dying.PaidFactionRequestCostRecovery is not null ||
                !CompleteProgramEventHistory().OfType<PlayerDyingEvent>().Any(e => e.ResolutionId == dying.Id && e.VictimSeat == dying.VictimSeat && e.KillerSeat == dying.KillerSeat)) return false;
        }
        else if (death.ReturnKind != DeathReturnKind.ProgramSkill || index < 3 || _resolutionStack[index - 3] is not ProgramSkillFrame parent || parent.Id != death.ParentFrameId) return false;
        if (r.OriginalDamage is { } damageCursor)
        {
            if (r.OriginalDying is not { Continuation: DyingContinuationKind.Damage } damageDying || damageDying.ParentFrameId != damageCursor.FrameId ||
                _resolutionStack.Take(index - 2).OfType<DamageFrame>().SingleOrDefault(d => d.Id == damageCursor.FrameId) is not { } damage ||
                damage.ParentFrameId != damageCursor.ParentFrameId || damage.SourceSeat != damageCursor.SourceSeat || damage.TargetSeat != damageCursor.TargetSeat ||
                damage.Amount != damageCursor.Amount || damage.Nature != damageCursor.Nature || damage.Step != damageCursor.Step ||
                damage.ParentFrameId != r.OriginalAttackOwnerFrameId || damage.TargetSeat != death.VictimSeat) return false;
        }
        else if (r.OriginalDying?.Continuation == DyingContinuationKind.Damage) return false;
        if (r.OriginalAttackOwnerFrameId is { } attackId && !_resolutionStack.Take(index - 2).Any(frame => frame.Id == attackId &&
            (frame is ProgramSkillFrame { AttackAttempt: not null, AttackReturn: not null } or ProgramSkillFrame { CardAttack: not null } or
                CardUseFrame { CardAttack: not null } or JudgmentFrame { CardAttack: not null }))) return false;
        return CompleteProgramEventHistory().OfType<OwnedDeathBenefitReturnIssuedEvent>().Count(e => e.ProgramFrameId == f.Id &&
            e.OwnerDeathWindowFrameId == window.Id && e.OriginalDeathFrameId == death.Id && e.OriginalDyingFrameId == r.OriginalDying?.FrameId &&
            e.OwnerSeat == f.OwnerSeat && e.RecipientSeat == fixedRecipient.RecipientSeat && e.Source == PairBenefitSource(f) && e.GameplayHash == f.GameplayHash &&
            e.FrozenReturnHash == OwnedDeathBenefitReturnHash(r)) == 1;
    }

    private static string OwnedDeathBenefitReturnHash(ProgramOwnedDeathBenefitReturn receipt) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(receipt))));

    private bool IsOriginalDyingSuspendedByOwnedDeathBenefit(DyingFrame dying) =>
        IsValidPlayerSeat(dying.VictimSeat) && !_players[dying.VictimSeat].IsAlive && _resolutionStack.OfType<ProgramSkillFrame>().Any(f =>
            f.OwnedDeathBenefitReturn?.OriginalDying?.FrameId == dying.Id && ExactOwnedDeathBenefitReturn(f));

    private void FinishOwnedDeathBenefitReturn(ProgramSkillFrame f, bool completed)
    {
        if (f.OwnedDeathBenefitReturn is not { } r) return;
        if (_resolutionStack.LastOrDefault()?.Id != f.Id || !ExactOwnedDeathBenefitReturn(f) || !ValidFixedRecipientReceipt(f) ||
            f.AttackAttempt is not null || f.AttackReturn is not null || f.PendingMovementContinuation is not null ||
            f.PendingRecoveryAttempts.Count != 0 || f.PaidFactionRequestCostRecovery is not null ||
            CurrentDamageAttempt?.ResolutionId != r.OriginalAttackOwnerFrameId ||
            CompleteProgramEventHistory().OfType<OwnedDeathBenefitReturnedEvent>().Any(e => e.ProgramFrameId == f.Id))
            throw new InvalidOperationException("An owned death benefit cannot return before its exact paid children finish.");
        AdvanceEventRulesAndQueueFact(new OwnedDeathBenefitReturnedEvent(f.Id, r.OwnerDeathWindowFrameId,
            r.OriginalDeath.FrameId, r.OriginalDying?.FrameId, completed));
        // The owning frame is popped immediately by CompleteRuntimeProgramBinding.
        // Do not clear the receipt early and accidentally expose the original dying while the root remains active.
    }

    private sealed partial class ProgramSkillHost : IOwnedDeathBenefitProgramHost
    {
        public SkillProgramStepOutcome SelectIssuedFixedRecipientWithDeathReturn(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.SelectIssuedFixedRecipientWithDeathReturn(frame, effect);
    }
}
