using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum GameStartingDrawChildKind { OutsidePhaseDraw, ProgramInstruction, ProgramOperation, NativeGainBenefit, NativeGainColorBenefit }

/// <summary>A paid gain observer derives one request from an earlier real startup draw.</summary>
public sealed record GameStartingOutsideDrawOrigin(long ProgramFrameId, int InstructionIndex, string GameplayHash,
    CardConversionSource Source, long WindowFrameId, long OriginalBatchId, long ParentInvocationId,
    int ParticipantSeat, long SequenceBefore)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public GameStartingDrawChildKind Kind { get; init; }
    public int RequestedCount { get; init; } = 1;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SkillProgramEffectOp? Operation { get; init; }
}
public sealed record GameStartingDrawChildRequestedEvent(long InvocationId, GameStartingOutsideDrawOrigin Child) : IGameEvent;

public sealed partial class GameEngine
{
    private bool TryGetGameStartingGainDrawParent(ProgramSkillFrame program, out CardsMovedTriggerWindowFrame? parent,
        out NativeDrawInvocationProof? incoming)
    {
        parent = null; incoming = null;
        if (!_setupComplete || _turnNumber != 0 || _phase != TurnPhase.NotStarted || program.TriggerId is null ||
            program.WindowContext is not { Window: SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved,
                MovementBatch: { NativeDrawInvocation: { } proof } batch } context || context.OwnerSeat != program.OwnerSeat ||
            proof.ActualTurnNumber != 0 || proof.GameStartingOrigin is null && proof.GameStartingMovementOrigin is null || proof.OwnerSeat != program.OwnerSeat ||
            proof.LastBatchId != batch.Id || proof.ParentFrameId != batch.ParentFrameId || !ValidNativeDrawInvocation(proof) ||
            _contentRegistry.Skills.GetValueOrDefault(program.SkillId)?.Program is not { } definition ||
            definition.GameplayHash != program.GameplayHash || program.InstructionIndex < 1) return false;
        var index = _resolutionStack.FindIndex(frame => frame.Id == program.Id);
        if (index < 1 || _resolutionStack[index - 1] is not CardsMovedTriggerWindowFrame window || window.Id != context.ParentFrameId ||
            window.Batch.Id != batch.Id || !window.Batch.Movements.SequenceEqual(batch.Movements) ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            !MountObserverCandidateMatches(program, window.Candidates[window.CandidateIndex])) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(program, definition);
        if (program.InstructionIndex > plan.Instructions.Count) return false;
        parent = window; incoming = proof; return true;
    }

    private bool IsExactGameStartingGainDrawProgram(ProgramSkillFrame program) =>
        TryGetGameStartingGainDrawParent(program, out _, out _) &&
        ProgramInstructionResolver.Default.Resolve(program, _contentRegistry.GetSkill(program.SkillId).Program!)
            .GetPausedInstruction(program.InstructionIndex).Effect.CompiledInstruction is DrawProgramInstruction;

    private GameStartingDrawOrigin? CaptureGameStartingGainDrawOrigin(int recipient, int requested, CardMoveReason reason,
        CardConversionSource? directProducer, out GameStartingMovementDrawOrigin? movementOrigin)
    {
        movementOrigin = null;
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame program ||
            !TryGetGameStartingGainDrawParent(program, out var parent, out var incoming)) return null;
        var source = new CardConversionSource(program.SkillId, GetProgramBindingId(program), program.OwnerSeat, program.SkillInstanceId);
        var before = OutsidePhaseMovementSequence;
        GameStartingDrawChildKind kind;
        SkillProgramEffectOp? operation = null;
        if (program.NativeDraw is { } paid && paid.InstructionIndex == program.InstructionIndex &&
            paid.RequestedCount == requested && paid.Reason == reason && !paid.RecipientApplied && paid.Replacement is null &&
            paid.RecipientIndex >= 0 && paid.RecipientIndex < paid.Recipients.Count && paid.Recipients[paid.RecipientIndex] == recipient &&
            directProducer is null)
        {
            kind = paid.ReturnKind == NativeDrawAttemptReturnKind.ProgramInstruction
                ? GameStartingDrawChildKind.ProgramInstruction : GameStartingDrawChildKind.ProgramOperation;
            operation = paid.Operation;
        }
        else if (program.OutsidePhaseDrawDiscard is { Op: SkillProgramEffectOp.DrawAfterActualOutsideDraw,
                Stage: OutsidePhaseDrawDiscardStage.MovementChildren, MovementIssued: true, ActualCount: 0,
                PaidMaterial: null, BatchId: null } outside &&
            outside.InstructionIndex == program.InstructionIndex && outside.Source == source && outside.GameplayHash == program.GameplayHash &&
            outside.WindowFrameId == parent!.Id && outside.OriginalBatchId == parent.Batch.Id &&
            outside.OriginalDraw?.InvocationId == incoming!.InvocationId && outside.OriginalDiscards.Count == 0 &&
            outside.EligibleMaterials.Count == 0 && outside.EligibleTargets.Contains(recipient) && outside.ParticipantSeat == recipient &&
            outside.SequenceBefore == before && outside.SequenceAfter == before && requested == 1 && directProducer == source &&
            reason.Value == OutsidePhaseMovementReason(program, true) &&
            program.PendingMovementContinuation == new ProgramMovementContinuation(recipient, 0, null) &&
            MatchingOutsideDrawIndexes(parent.Batch, parent.Candidates[parent.CandidateIndex]) is [var matching] &&
            matching == outside.OriginalMovementIndex)
            kind = GameStartingDrawChildKind.OutsidePhaseDraw;
        else if (program.BuZhi is { Stage: "movement", ReturnStage: "complete", Actual: 0, PaidIds.Count: 0 } benefit &&
            benefit.InstructionIndex == program.InstructionIndex && benefit.Source == source && benefit.GameplayHash == program.GameplayHash &&
            benefit.ParentFrameId == parent!.Id && benefit.BatchId == parent.Batch.Id && benefit.Target == recipient &&
            benefit.Before == before && benefit.After == before && requested == 1 && directProducer is null &&
            reason.Value == "program.bu-zhi.draw" &&
            program.PendingMovementContinuation == new ProgramMovementContinuation(program.OwnerSeat, 0, null))
            kind = GameStartingDrawChildKind.NativeGainBenefit;
        else if (IsExactGameStartingColorGainDraw(program, parent!, incoming!, recipient, requested, reason, directProducer, before))
            kind = GameStartingDrawChildKind.NativeGainColorBenefit;
        else return null;
        var child = new GameStartingOutsideDrawOrigin(program.Id, program.InstructionIndex, program.GameplayHash, source,
            parent!.Id, parent.Batch.Id, incoming!.InvocationId, recipient, before)
            { Kind = kind, RequestedCount = requested, Operation = operation };
        if (!GameStartingDrawChildProgramMatches(child) || !GameStartingDrawChildStartedMatches(child)) return null;
        if (incoming.GameStartingMovementOrigin is { } nativeMovement)
        { movementOrigin = nativeMovement with { DrawChild = child }; return null; }
        return incoming.GameStartingOrigin! with { OutsideDrawChild = child };
    }

    private bool GameStartingDrawChildProgramMatches(GameStartingOutsideDrawOrigin child, bool movementRoot = false)
    {
        if (child.ProgramFrameId <= child.WindowFrameId || child.WindowFrameId <= 0 || child.OriginalBatchId <= 0 ||
            (movementRoot ? child.ParentInvocationId != 0 : child.ParentInvocationId <= 0) ||
            child.InstructionIndex < 1 || child.RequestedCount < 1 || child.SequenceBefore < 0 ||
            !IsValidPlayerSeat(child.Source.OwnerSeat) || !IsValidPlayerSeat(child.ParticipantSeat) ||
            _contentRegistry.Skills.GetValueOrDefault(child.Source.SkillId)?.Program is not { } definition ||
            definition.GameplayHash != child.GameplayHash) return false;
        var trigger = definition.Triggers.SingleOrDefault(item => item.Id == child.Source.BindingId);
        if (trigger?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved)) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(definition, ProgramInstructionSourceKind.Trigger, trigger.Id);
        if (child.InstructionIndex > plan.Instructions.Count) return false;
        var instruction = plan.Instructions[child.InstructionIndex - 1];
        return child.Kind switch
        {
            GameStartingDrawChildKind.ProgramInstruction => child.Operation is null && instruction.CompiledInstruction is DrawProgramInstruction,
            GameStartingDrawChildKind.ProgramOperation => child.Operation == instruction.Op && child.Operation is { } op && ProgramNativeDrawReturns.Value.ContainsKey(op),
            GameStartingDrawChildKind.OutsidePhaseDraw => child.Operation is null && child.RequestedCount == 1 &&
                trigger.Window == SkillProgramTriggerWindow.CardsGained && instruction.Op == SkillProgramEffectOp.DrawAfterActualOutsideDraw,
            GameStartingDrawChildKind.NativeGainBenefit => child.Operation is null && child.RequestedCount == 1 &&
                trigger.Window == SkillProgramTriggerWindow.CardsMoved && instruction.Op == SkillProgramEffectOp.OfferNativeBatchHandGainOrCardLossBenefit,
            GameStartingDrawChildKind.NativeGainColorBenefit => child.Operation is null && child.RequestedCount == 1 &&
                trigger.Window == SkillProgramTriggerWindow.CardsMoved && instruction.Op == SkillProgramEffectOp.ResolveZhiyiGainColorBenefit,
            _ => false
        };
    }

    private bool GameStartingDrawChildStartedMatches(GameStartingOutsideDrawOrigin child)
    {
        var history = CompleteProgramEventHistory().ToArray();
        var trigger = _contentRegistry.GetSkill(child.Source.SkillId).Program!.Triggers.Single(item => item.Id == child.Source.BindingId);
        if (history.OfType<ProgramBindingStartedEvent>().Count(started => started.FrameId == child.ProgramFrameId &&
            started.SkillId == child.Source.SkillId && started.BindingId == child.Source.BindingId &&
            started.SkillInstanceId == child.Source.SkillInstanceId && started.OwnerSeat == child.Source.OwnerSeat &&
            started.Window == trigger.Window) != 1) return false;
        if (child.Kind == GameStartingDrawChildKind.OutsidePhaseDraw)
            return history.OfType<OutsidePhaseDrawDiscardStartedEvent>().Where(started => started.FrameId == child.ProgramFrameId).ToArray() is [var outside] &&
                outside.Source == child.Source && outside.GameplayHash == child.GameplayHash &&
                outside.Op == SkillProgramEffectOp.DrawAfterActualOutsideDraw && outside.WindowFrameId == child.WindowFrameId &&
                outside.OriginalBatchId == child.OriginalBatchId && outside.NativeDrawInvocationId == child.ParentInvocationId;
        if (child.Kind == GameStartingDrawChildKind.NativeGainColorBenefit)
            return GameStartingColorGainDrawStartedMatches(child, history);
        if (child.Kind != GameStartingDrawChildKind.NativeGainBenefit) return true;
        return history.OfType<BuZhiStartedEvent>().Where(started => started.FrameId == child.ProgramFrameId).ToArray() is [var benefit] &&
            benefit.Source == child.Source && benefit.GameplayHash == child.GameplayHash && benefit.ParentFrameId == child.WindowFrameId &&
            benefit.BatchId == child.OriginalBatchId && benefit.Turn == 0 && benefit.Target is null &&
            history.OfType<BuZhiBranchSelectedEvent>().Where(selected => selected.FrameId == child.ProgramFrameId).ToArray() is [var choice] &&
            choice.ChooserSeat == child.Source.OwnerSeat && choice.Branch == "benefit";
    }

    private bool ValidGameStartingGainDrawChain(NativeDrawInvocationProof proof)
    {
        if (proof.ActualTurnNumber != 0 || proof.Phase is not null || proof.GameStartingOrigin is not { } origin ||
            !GameStartingDrawOriginProgramMatches(origin)) return false;
        var root = origin with { OutsideDrawChild = null };
        var history = CompleteProgramEventHistory().ToArray();
        var records = history.OfType<NativeDrawInvocationRecordedEvent>().GroupBy(record => record.InvocationId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var id = proof.InvocationId;
        while (records.TryGetValue(id, out var matching) && matching is [var record])
        {
            if (record.GameStartingOrigin is not { } issued || (issued with { OutsideDrawChild = null }) != root ||
                record.TurnNumber != 0 || record.Phase is not null || record.Requested < record.ActualCount || record.ActualCount < 1 ||
                !IsValidPlayerSeat(record.OwnerSeat) || record.Reason == CardMoveReasons.InitialDeal || record.LastBatchId <= 0 ||
                record.SequenceBefore < 0 || record.SequenceAfter <= record.SequenceBefore || record.SequenceAfter > OutsidePhaseMovementSequence) return false;
            var materials = _cardMovements.Where(m => m.Sequence > record.SequenceBefore && m.Sequence <= record.SequenceAfter &&
                m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(record.OwnerSeat) && m.Reason == record.Reason).ToArray();
            if (materials.Length != record.ActualCount || materials.Select(m => m.CardId).Distinct().Count() != materials.Length) return false;
            if (issued.OutsideDrawChild is not { } child)
                return record.ParentFrameId == root.ProgramFrameId && id > root.ProgramFrameId &&
                    history.OfType<ProgramBindingStartedEvent>().Count(started => started.FrameId == root.ProgramFrameId &&
                        started.SkillId == root.Source.SkillId && started.BindingId == root.Source.BindingId &&
                        started.SkillInstanceId == root.Source.SkillInstanceId && started.OwnerSeat == root.Source.OwnerSeat &&
                        started.Window == SkillProgramTriggerWindow.GameStarting) == 1;
            if (record.ParentFrameId != child.ProgramFrameId || child.ProgramFrameId >= id || child.ParentInvocationId >= id ||
                record.OwnerSeat != child.ParticipantSeat || record.Requested != child.RequestedCount || record.SequenceBefore != child.SequenceBefore ||
                !GameStartingDrawChildProgramMatches(child) || !GameStartingDrawChildStartedMatches(child) ||
                history.OfType<GameStartingDrawChildRequestedEvent>().Count(request => request.InvocationId == id && request.Child == child) != 1 ||
                !records.TryGetValue(child.ParentInvocationId, out var parents) || parents is not [var parent] ||
                parent.LastBatchId != child.OriginalBatchId || parent.OwnerSeat != child.Source.OwnerSeat ||
                parent.SequenceAfter > child.SequenceBefore || parent.ActualTurnOwnerSeat != record.ActualTurnOwnerSeat ||
                parent.PhaseActorSeat != record.PhaseActorSeat || !GameStartingDrawChildPaymentMatches(record, child, history)) return false;
            id = child.ParentInvocationId;
        }
        return false;
    }

    private bool GameStartingDrawChildPaymentMatches(NativeDrawInvocationRecordedEvent record, GameStartingOutsideDrawOrigin child,
        IReadOnlyList<IGameEvent> history)
    {
        if (child.Kind == GameStartingDrawChildKind.OutsidePhaseDraw)
        {
            if (record.DirectProducer != child.Source || record.Reason.Value != $"skill-program.{child.Source.SkillId}.outside-phase-draw") return false;
            var issued = history.OfType<OutsidePhaseDrawDiscardMovementIssuedEvent>().Where(issue => issue.FrameId == child.ProgramFrameId).ToArray();
            if (issued is [var payment]) return payment.Op == SkillProgramEffectOp.DrawAfterActualOutsideDraw &&
                payment.ParticipantSeat == record.OwnerSeat && payment.ActualCount == record.ActualCount &&
                payment.SequenceBefore == record.SequenceBefore && payment.SequenceAfter == record.SequenceAfter && payment.BatchId is null && payment.PaidMaterial is null;
            return issued.Length == 0 && _resolutionStack.OfType<ProgramSkillFrame>().Any(program => program.Id == child.ProgramFrameId &&
                program.OutsidePhaseDrawDiscard is { Stage: OutsidePhaseDrawDiscardStage.MovementChildren, MovementIssued: true,
                    ActualCount: 0, PaidMaterial: null } receipt && receipt.Source == child.Source && receipt.ParticipantSeat == record.OwnerSeat &&
                receipt.SequenceBefore == record.SequenceBefore && receipt.SequenceAfter == record.SequenceBefore);
        }
        if (record.DirectProducer is not null) return false;
        if (child.Kind == GameStartingDrawChildKind.NativeGainColorBenefit)
            return GameStartingColorGainDrawPaymentMatches(record, child, history);
        if (child.Kind != GameStartingDrawChildKind.NativeGainBenefit) return true;
        if (record.Reason.Value != "program.bu-zhi.draw") return false;
        var paid = history.OfType<BuZhiMovementPaidEvent>().Where(payment => payment.FrameId == child.ProgramFrameId).ToArray();
        if (paid is [var benefit]) return benefit.ReturnStage == "complete" && benefit.Target == record.OwnerSeat && benefit.Actual == record.ActualCount &&
            benefit.Before == record.SequenceBefore && benefit.After == record.SequenceAfter && benefit.CardIds.Count == 0;
        return paid.Length == 0 && _resolutionStack.OfType<ProgramSkillFrame>().Any(program => program.Id == child.ProgramFrameId &&
            program.BuZhi is { Stage: "movement", ReturnStage: "complete", Actual: 0, PaidIds.Count: 0 } receipt &&
            receipt.Source == child.Source && receipt.Target == record.OwnerSeat && receipt.Before == record.SequenceBefore && receipt.After == record.SequenceBefore);
    }
}
