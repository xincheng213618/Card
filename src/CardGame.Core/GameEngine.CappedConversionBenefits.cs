namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static CardConversionSource CappedConversionSource(ProgramSkillFrame f) =>
        new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    private bool ExactCappedConversionParent(ProgramSkillFrame f, out DamageTriggerWindowFrame window)
    {
        window = null!;
        if (f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied } context ||
            _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } current ||
            current.TriggerWindow != context.Window || current.TargetSeat != f.OwnerSeat || context.TargetSeat != f.OwnerSeat ||
            context.OwnerSeat != f.OwnerSeat || context.DamageFrameId != current.ParentFrameId ||
            current.CandidateIndex < 0 || current.CandidateIndex >= current.Candidates.Count ||
            !MountObserverCandidateMatches(f, current.Candidates[current.CandidateIndex].ToProgramCandidate())) return false;
        window = current; return true;
    }

    private SkillProgramStepOutcome BeginCappedConversionBenefit(ProgramSkillFrame supplied, string state)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.CappedConversionBenefit is not null || f.InstructionIndex != 1 || !ExactCappedConversionParent(f, out var window) ||
            _roundNumber < 1 || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
            throw new InvalidOperationException("A capped conversion benefit lost its exact original damage recipient and current candidate.");
        var tier = TieredRoundTier(f.OwnerSeat, state);
        if (tier is < 0 or > 2) throw new InvalidOperationException("A conversion modification count cannot exceed its two defined versions.");
        var before = _cardMovements.LastOrDefault()?.Sequence ?? 0;
        var receipt = new ProgramCappedConversionBenefitReceipt(f.InstructionIndex, CappedConversionSource(f), f.GameplayHash,
            state, window.Id, window.ParentFrameId, f.WindowContext!.OccurrenceIndex, _roundNumber, tier,
            before, before, 0, tier > 0, tier > 0);
        ReplaceRuntimeTop(f with { CappedConversionBenefit = receipt,
            PendingMovementContinuation = tier > 0 ? new(f.OwnerSeat, 0, null) : null });
        // X=0 is a genuine zero benefit: no draw call, no movement or CardsDrawn
        // fact. Its accepted operation can still issue modification one.
        var count = tier == 0 ? 0 : DrawCards(_players[f.OwnerSeat], tier, true,
            new($"skill-program.{f.SkillId}.capped-conversion-draw")).Count;
        f = GetActiveProgramFrame(f.Id);
        receipt = receipt with { ActualDrawCount = count, SequenceAfter = _cardMovements.LastOrDefault()?.Sequence ?? 0 };
        ReplaceRuntimeTop(f with { CappedConversionBenefit = receipt });
        AdvanceEventRulesAndQueueFact(new CappedConversionBenefitIssuedEvent(f.Id, receipt));
        ContinueCappedConversionBenefit(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private void ContinueCappedConversionBenefit(long id)
    {
        var f = GetActiveProgramFrame(id); var r = f.CappedConversionBenefit!;
        if (!ValidCappedConversionBenefit(f)) throw new InvalidOperationException("An issued conversion benefit changed its exact damage parent, draw invoice or original source.");
        if (r.AwaitingMovement)
        {
            if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
                TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) ||
                TryBeginCardsMovedProgramWindow(id)) return;
            ReplaceRuntimeTop(f with { PendingMovementContinuation = null, CappedConversionBenefit = r with { AwaitingMovement = false } });
            f = GetActiveProgramFrame(id); r = f.CappedConversionBenefit!;
        }
        var current = TieredRoundTier(f.OwnerSeat, r.StateId);
        var qualified = _winner == Winner.None && _players[f.OwnerSeat].IsAlive &&
            HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId);
        var modified = qualified && current == r.FrozenSuccessfulModifications && current < 2;
        if (modified) UpgradeConfiguredConversionTier(f, r.StateId);
        AdvanceEventRulesAndQueueFact(new CappedConversionBenefitCompletedEvent(id, r.Source, r.StateId,
            r.FrozenSuccessfulModifications, r.ActualDrawCount, modified, TieredRoundTier(f.OwnerSeat, r.StateId)));
        ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { CappedConversionBenefit = null, PendingMovementContinuation = null });
        // The one-op plan has no unissued benefit after this exact completion.
        FinishProgramSkill(f, qualified);
    }

    private bool ResumeCappedConversionBenefit(long id)
    {
        if (GetActiveProgramFrame(id).CappedConversionBenefit is null) return false;
        ContinueCappedConversionBenefit(id); return true;
    }

    private bool ReturnCappedConversionBenefitMovement(ProgramSkillFrame frame)
    {
        if (frame.CappedConversionBenefit is null) return false;
        if (frame.PendingMovementContinuation is not { } pending || !IsCappedConversionAwaitedMovement(frame, pending))
            throw new InvalidOperationException("The conversion draw lost its exact awaited movement return.");
        ContinueCappedConversionBenefit(frame.Id); return true;
    }

    private bool ValidCappedConversionBenefit(ProgramSkillFrame f)
    {
        if (f.CappedConversionBenefit is not { } r || r.InstructionIndex != f.InstructionIndex || f.InstructionIndex != 1 ||
            r.Source != CappedConversionSource(f) || string.IsNullOrWhiteSpace(r.Source.SkillInstanceId) ||
            string.IsNullOrWhiteSpace(r.Source.BindingId) || r.GameplayHash != f.GameplayHash ||
            !ExactCappedConversionParent(f, out var window) || window.Id != r.DamageWindowFrameId ||
            window.ParentFrameId != r.DamageFrameId || f.WindowContext!.OccurrenceIndex != r.OccurrenceIndex ||
            r.RoundNumber != _roundNumber || r.FrozenSuccessfulModifications is < 0 or > 2 ||
            r.ActualDrawCount < 0 || r.ActualDrawCount > r.FrozenSuccessfulModifications ||
            r.ActualDrawAttempted != (r.FrozenSuccessfulModifications > 0) || r.SequenceBefore < 0 || r.SequenceAfter < r.SequenceBefore ||
            !r.ActualDrawAttempted && (r.SequenceAfter != r.SequenceBefore || r.ActualDrawCount != 0 || r.AwaitingMovement) ||
            r.AwaitingMovement && (f.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending || pending.SubjectSeat != f.OwnerSeat) ||
            ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).Instructions is not
                [{ Op: SkillProgramEffectOp.DrawBeforeCappedConversionTierUpgrade, StateId: var state }] || state != r.StateId) return false;
        if (CompleteProgramEventHistory().OfType<CappedConversionBenefitIssuedEvent>().Count(e => e.ProgramFrameId == f.Id &&
            e.Receipt == (r with { AwaitingMovement = r.ActualDrawAttempted })) != 1) return false;
        return _cardMovements.Count(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter &&
            m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) &&
            m.Reason.Value == $"skill-program.{f.SkillId}.capped-conversion-draw") == r.ActualDrawCount;
    }

    private sealed partial class ProgramSkillHost : ICappedConversionBenefitHost
    {
        public SkillProgramStepOutcome DrawBeforeCappedConversionTierUpgrade(ProgramSkillFrame f, string state) => engine.BeginCappedConversionBenefit(f, state);
    }
}
