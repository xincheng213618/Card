namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static CardConversionSource RandomAreaSource(ProgramSkillFrame frame) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_RandomAreaAcquisition.RandomAreaSource(frame);
    private static CardMoveReason RandomAreaReason(ProgramSkillFrame frame, int index) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_RandomAreaAcquisition.RandomAreaReason(frame,index);
    private bool ExactRandomAreaParent(ProgramSkillFrame frame, out DamageTriggerWindowFrame window, out DamageFrame damage)
    {
        window = null!; damage = null!;
        if (frame.InstructionIndex != 1 || frame.WindowContext is not { TargetSeat: { } target, OccurrenceIndex: >= 0 } context || target != frame.OwnerSeat ||
            !ObtainRandomAreaFromEachOtherThenTurnOverDescriptor.IsTrigger(GetProgramTrigger(frame)) ||
            !ExactOrderedPrintedSkillLossParent(frame, out var actualWindow, out var actualDamage, out _) ||
            actualWindow.Candidates[actualWindow.CandidateIndex].OccurrenceIndex != context.OccurrenceIndex || context.OccurrenceIndex >= actualDamage.Amount)
            return false;
        window = actualWindow; damage = actualDamage; return true;
    }
    private SkillProgramStepOutcome ObtainRandomAreaFromEachOtherThenTurnOver(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (_resolutionStack.LastOrDefault()?.Id != frame.Id || frame.RandomAreaAcquisition is not null || frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats.Count != 0 ||
            !ExactRandomAreaParent(frame, out var window, out var damage))
            throw new InvalidOperationException("Random area acquisition needs its exact positive native damage-point owner candidate.");
        var owner = _players[frame.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive || !HasRuntimeSkillInstance(owner, frame.SkillId, frame.SkillInstanceId)) return SkillProgramStepOutcome.Continue;
        var receipt = new ProgramRandomAreaAcquisitionReceipt(frame.Id, frame.InstructionIndex, RandomAreaSource(frame), frame.GameplayHash,
            window.Id, damage.Id, frame.WindowContext!.OccurrenceIndex, damage.Amount)
        { OtherSeats = _players.Where(p => p.Seat != frame.OwnerSeat).OrderBy(p => (p.Seat - frame.OwnerSeat + _playerCount) % _playerCount).Select(p => p.Seat).ToArray() };
        ReplaceRuntimeTop(frame with { RandomAreaAcquisition = receipt });
        AdvanceEventRulesAndQueueFact(new RandomAreaAcquisitionStartedEvent(receipt));
        ResumeRandomAreaAcquisition(frame.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 1245)]
    private bool ResumeRandomAreaAcquisition(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { RandomAreaAcquisition: not null } frame || frame.Id != id) return false;
        while (true)
        {
            frame = GetActiveProgramFrame(id); RequireRandomAreaAcquisition(frame); var receipt = frame.RandomAreaAcquisition!;
            if (receipt.Stage is RandomAreaAcquisitionStage.MovementChildren or RandomAreaAcquisitionStage.TurnoverChildren)
            {
                if (DrainRandomAreaChildren(frame)) return true;
                if (receipt.Stage == RandomAreaAcquisitionStage.TurnoverChildren) { FinishRandomAreaAcquisition(frame, false); return true; }
                ReplaceRuntimeTop(frame = frame with { PendingMovementContinuation = null,
                    RandomAreaAcquisition = receipt = receipt with { Stage = RandomAreaAcquisitionStage.Visiting, NextSeatIndex = receipt.NextSeatIndex + 1 } });
            }
            var owner = _players[frame.OwnerSeat];
            if (_winner != Winner.None || !owner.IsAlive || receipt.Steps.Count == 0 && !HasRuntimeSkillInstance(owner, frame.SkillId, frame.SkillInstanceId))
            { FinishRandomAreaAcquisition(frame, true); return true; }
            if (receipt.NextSeatIndex == receipt.OtherSeats.Count)
            {
                var before = owner.IsFaceDown; var prevented = ShenwuCannotTurnOver(owner);
                ReplaceRuntimeTop(frame = frame with { RandomAreaAcquisition = receipt with { Stage = RandomAreaAcquisitionStage.TurnoverChildren,
                    TurnoverIssued = true, TurnoverPrevented = prevented, FaceDownBefore = before, FaceDownAfter = prevented ? before : !before } });
                TurnOverProgramTarget(frame.Id, frame.OwnerSeat, frame.OwnerSeat);
                AdvanceEventRulesAndQueueFact(new RandomAreaAcquisitionTurnoverIssuedEvent(frame.Id, before, owner.IsFaceDown, prevented));
                continue;
            }
            var target = receipt.OtherSeats[receipt.NextSeatIndex];
            var eligible = _players[target].IsAlive ? ObtainRandomAreaFromEachOtherThenTurnOverDescriptor.Areas
                .Where(z => RandomAreaCards(target, z).Count > 0).ToArray() : [];
            if (eligible.Length == 0)
            {
                ReplaceRuntimeTop(frame with { RandomAreaAcquisition = receipt with { NextSeatIndex = receipt.NextSeatIndex + 1 } });
                AdvanceEventRulesAndQueueFact(new RandomAreaAcquisitionSeatSkippedEvent(frame.Id, receipt.NextSeatIndex, target));
                continue;
            }
            var randomBefore = _random.State; var zone = eligible[_random.Next(eligible.Length)];
            var materials = RandomAreaCards(target, zone).Select(c => new DamageTargetRegionMaterial(c.Id, c.Kind)).ToArray();
            var step = new RandomAreaAcquisitionStepReceipt(receipt.NextSeatIndex, target, zone, randomBefore, _random.State, 0, _movementSequence, _movementSequence)
            { EligibleZones = eligible, Materials = materials };
            ReplaceRuntimeTop(frame with { PendingMovementContinuation = new(frame.OwnerSeat, 0, null),
                RandomAreaAcquisition = receipt with { Stage = RandomAreaAcquisitionStage.MovementChildren } });
            MoveProgramCardsFromMultipleSources(materials.Select(m => m.CardId).ToArray(), CardLocation.Hand(frame.OwnerSeat), RandomAreaReason(frame, step.SeatIndex),
                (batch, records) =>
                {
                    var active = GetActiveProgramFrame(id);
                    var paid = step with { MovementBatchId = batch, SequenceAfter = _movementSequence, Movements = records };
                    ReplaceRuntimeTop(active with { RandomAreaAcquisition = active.RandomAreaAcquisition! with
                    { Steps = active.RandomAreaAcquisition.Steps.Append(paid).ToArray() } });
                    AdvanceEventRulesAndQueueFact(new RandomAreaAcquisitionRegionPaidEvent(id, paid));
                });
        }
    }
    private IReadOnlyList<Card> RandomAreaCards(int seat, CardZoneKind zone) => zone switch
    {
        CardZoneKind.Hand => GetHand(_players[seat]), CardZoneKind.Equipment => GetEquipment(_players[seat]),
        // A pending native judgment result is not a card in this character's area.
        CardZoneKind.Judgment => GetJudgment(_players[seat]).Where(c => !_resolutionStack.OfType<JudgmentFrame>().Any(j => j.TargetSeat == seat && j.CardId == c.Id)).ToArray(),
        _ => throw new InvalidOperationException("Random acquisition requires an owned HEJ area.")
    };
    private bool DrainRandomAreaChildren(ProgramSkillFrame frame) =>
        TryBeginQueuedRecoveryReplacement(frame.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(frame.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(frame.Id) || TryBeginAdvancedSkillsChanged(frame.Id);
    private void FinishRandomAreaAcquisition(ProgramSkillFrame frame, bool cancelled)
    {
        var r = frame.RandomAreaAcquisition!;
        if (frame.PendingMovementContinuation is not null || r.Stage == RandomAreaAcquisitionStage.MovementChildren)
            throw new InvalidOperationException("Random acquisition cannot retire a paid region before its children return.");
        ReplaceRuntimeTop(frame = frame with { RandomAreaAcquisition = r with { Stage = RandomAreaAcquisitionStage.Finished, Cancelled = cancelled } });
        AdvanceEventRulesAndQueueFact(new RandomAreaAcquisitionCompletedEvent(frame.Id, r.Steps.Count, r.Steps.Sum(s => s.ActualObtained), r.TurnoverIssued, cancelled));
        FinishProgramSkill(frame, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 1245)]
    private bool ReturnRandomAreaMovement(ProgramSkillFrame frame) => frame.RandomAreaAcquisition is not null && ResumeRandomAreaAcquisition(frame.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 1245)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 1245)]
    private bool CanContinueRandomAreaAcquisition(ProgramSkillFrame frame) => frame.RandomAreaAcquisition is { } r &&
        (r.Steps.Count > 0 || r.TurnoverIssued) && ValidRandomAreaAcquisition(frame);
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 480)]
    private bool IsRandomAreaAcquisitionMovement(ProgramSkillFrame frame, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        effect?.Op == SkillProgramEffectOp.ObtainRandomAreaFromEachOtherThenTurnOver && frame.RandomAreaAcquisition is { Stage: RandomAreaAcquisitionStage.MovementChildren, Steps.Count: > 0 } &&
        pending.SubjectSeat == frame.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null && ValidRandomAreaAcquisition(frame);
    [ProgramCapability]
    private sealed class RandomAreaAcquisitionCapability(GameEngine engine) : IRandomAreaAcquisitionHost
    { public SkillProgramStepOutcome ObtainRandomAreaFromEachOtherThenTurnOver(ProgramSkillFrame frame) => engine.ObtainRandomAreaFromEachOtherThenTurnOver(frame); }
}
