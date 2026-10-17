using System.Text.Json.Serialization;

namespace CardGame.Core;

/// <summary>One completed rule instruction to discard its primary materials, independent of movement batching.</summary>
public sealed record NativeLogicalDiscardInvocationRecordedEvent(long InvocationId, int TurnNumber,
    int ActualTurnOwnerSeat, int InitiatorSeat, int ExecutorSeat, int? SubjectSeat,
    int ActualCount, int HandCount, int SequenceBefore, int SequenceAfter,
    long? ParentFrameId, CardMoveReason Reason) : IGameEvent;

public sealed record NativeLogicalDiscardMaterial(int CardId, CardLocation From);

public sealed record NativeLogicalDiscardInvocationDraft
{
    public long InvocationId { get; init; }
    public int TurnNumber { get; init; }
    public int ActualTurnOwnerSeat { get; init; }
    public int InitiatorSeat { get; init; }
    public int ExecutorSeat { get; init; }
    public int SequenceBefore { get; init; }
    public int InstructionIndex { get; init; }
    public long? ParentFrameId { get; init; }
    public CardMoveReason Reason { get; init; }
    private IReadOnlyList<NativeLogicalDiscardMaterial> _materials = Array.AsReadOnly(Array.Empty<NativeLogicalDiscardMaterial>());
    public IReadOnlyList<NativeLogicalDiscardMaterial> Materials
    { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NativeLogicalDiscardInvocationDraft? LogicalDiscardInvocation { get; init; }
}

public sealed partial class GameEngine
{
    private bool TracksNativeLogicalDiscards =>
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveBiguoDrawAndArmTargetPindian) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveZongluanPreparationSlashAndDiscard) ||
        HasMoTianenCapability || HasXieLingYuCapability || HasGuYinCapability ||
        _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveYouqueMinimumBaitPindian) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DiscardNianRuizhiRemainder) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.RecoverNianJingjueLoss) ||
        _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveXianbiHandAdjustmentAndRandomReturns) ||
        _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveHuanpeiHandAdjustmentAndBasicUse) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveDongxinDamageDiscardAndReclaim) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveFuchaoRoundEndingDrawAndDiscard) ||
        HasLeXiaoQiaoWeiwanCapability || HasLiYiCapability ||
        _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveWeijieRequiredBasicRequest) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveXiangxuIssuedTurnEndAdjustment) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveGoudeActualTurnEnd) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveQingyaBetweenHandDiscards) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveQiejianLastHandLoss);

    private NativeLogicalDiscardInvocationDraft? CaptureNativeLogicalDiscardInvocation(int initiatorSeat,
        int executorSeat, IReadOnlyList<int> cardIds, CardMoveReason reason, long? parentFrameId = null)
    {
        if (!TracksNativeLogicalDiscards || !_setupComplete || _turnNumber < 1 ||
            !IsValidPlayerSeat(_turnProgression.OwnerSeat) || !IsValidPlayerSeat(initiatorSeat) ||
            !IsValidPlayerSeat(executorSeat) || cardIds.Count == 0) return null;
        if (cardIds.Distinct().Count() != cardIds.Count)
            throw new InvalidOperationException("A logical discard cannot repeat a primary material.");
        var materials = cardIds.Select(id =>
        {
            var from = _cardZones.GetLocation(id);
            if (from == CardLocation.Processing)
            {
                var entry = _cardMovements.LastOrDefault(m => m.CardId == id);
                if (entry is not null && entry.To == CardLocation.Processing &&
                    IsOwnedDiscardSource(entry.From) && IsProgramDiscardOriginReason(entry.Reason)) from = entry.From;
            }
            if (!IsOwnedDiscardSource(from))
                throw new InvalidOperationException("A logical discard needs its original owned primary material.");
            return new NativeLogicalDiscardMaterial(id, from);
        }).ToArray();
        return new NativeLogicalDiscardInvocationDraft
        {
            InvocationId = ++_resolutionSequence, TurnNumber = _turnNumber,
            ActualTurnOwnerSeat = _turnProgression.OwnerSeat, InitiatorSeat = initiatorSeat,
            ExecutorSeat = executorSeat, SequenceBefore = _movementSequence,
            ParentFrameId = parentFrameId ?? _resolutionStack.LastOrDefault()?.Id, Reason = reason, Materials = materials
        };
    }

    private void CompleteNativeLogicalDiscardInvocation(NativeLogicalDiscardInvocationDraft? draft)
    {
        if (draft is null) return;
        if (ProgramEventHistory<NativeLogicalDiscardInvocationRecordedEvent>().Any(e => e.InvocationId == draft.InvocationId))
            throw new InvalidOperationException("A logical discard must finish exactly once.");
        var actual = draft.Materials.Where(material => _cardMovements.Any(move =>
            move.CardId == material.CardId && move.Sequence > draft.SequenceBefore && move.Sequence <= _movementSequence &&
            (move.From == material.From && move.To == CardLocation.DiscardPile && move.Reason == draft.Reason ||
                GetProgramDiscardSource(move) == material.From &&
                (move.Reason == draft.Reason || move.Reason.Value == draft.Reason.Value + "-finished" ||
                    move.Reason.Value == draft.Reason.Value + "-discard") ||
                move.From == material.From && move.To == CardLocation.OutsideGame &&
                material.From.Zone == CardZoneKind.Equipment && move.Reason == draft.Reason))).ToArray();
        if (actual.Length == 0) return;
        var owners = actual.Select(m => m.From.OwnerSeat).Distinct().ToArray();
        AdvanceEventRulesAndQueueFact(new NativeLogicalDiscardInvocationRecordedEvent(draft.InvocationId,
            draft.TurnNumber, draft.ActualTurnOwnerSeat, draft.InitiatorSeat, draft.ExecutorSeat,
            owners.Length == 1 ? owners[0] : null, actual.Length,
            actual.Count(m => m.From.Zone == CardZoneKind.Hand), draft.SequenceBefore, _movementSequence,
            draft.ParentFrameId, draft.Reason));
    }

    private void MoveProgramLogicalDiscard(ProgramSkillFrame frame, int executorSeat, IReadOnlyList<int> ids,
        CardMoveReason reason, Action<long, IReadOnlyList<CardMovementRecord>>? afterPhysicalRecords = null)
    {
        var draft = CaptureNativeLogicalDiscardInvocation(frame.OwnerSeat, executorSeat, ids, reason, frame.Id);
        MoveProgramCardsFromMultipleSources(ids, CardLocation.DiscardPile, reason, afterPhysicalRecords,
            disclosure: CardMovementDisclosure.FaceUp);
        CompleteNativeLogicalDiscardInvocation(draft);
    }

    private void MoveLogicalDiscard(int initiatorSeat, int executorSeat, Card card, CardLocation from,
        CardMoveReason reason, Action<CardMovementRecord>? beforeFact = null, long? parentFrameId = null)
    {
        var draft = CaptureNativeLogicalDiscardInvocation(initiatorSeat, executorSeat, [card.Id], reason, parentFrameId);
        MoveCard(card, from, CardLocation.DiscardPile, reason, beforeFact, disclosure: CardMovementDisclosure.FaceUp);
        CompleteNativeLogicalDiscardInvocation(draft);
    }

    private void MoveLogicalDiscardCards(int initiatorSeat, int executorSeat, IReadOnlyList<Card> cards,
        CardLocation from, CardMoveReason reason)
    {
        var draft = CaptureNativeLogicalDiscardInvocation(initiatorSeat, executorSeat,
            cards.Select(card => card.Id).ToArray(), reason);
        MoveCards(cards, from, CardLocation.DiscardPile, reason, disclosure: CardMovementDisclosure.FaceUp);
        CompleteNativeLogicalDiscardInvocation(draft);
    }

    private ProgramSkillFrame AppendFrameNativeLogicalDiscard(ProgramSkillFrame frame, int executorSeat,
        IReadOnlyList<int> ids, CardMoveReason reason)
    {
        frame = GetActiveProgramFrame(frame.Id);
        if (!TracksNativeLogicalDiscards) return frame;
        var draft = frame.LogicalDiscardInvocation;
        if (draft is null)
        {
            draft = CaptureNativeLogicalDiscardInvocation(frame.OwnerSeat, executorSeat, ids, reason, frame.Id);
            if (draft is not null) draft = draft with { InstructionIndex = frame.InstructionIndex };
        }
        else
        {
            if (draft.InitiatorSeat != frame.OwnerSeat || draft.ExecutorSeat != executorSeat || draft.Reason != reason ||
                draft.TurnNumber != _turnNumber || draft.ActualTurnOwnerSeat != _turnProgression.OwnerSeat ||
                draft.ParentFrameId != frame.Id || draft.InstructionIndex != frame.InstructionIndex || ids.Distinct().Count() != ids.Count ||
                ids.Any(id => draft.Materials.Any(m => m.CardId == id) || !IsOwnedDiscardSource(_cardZones.GetLocation(id))))
                throw new InvalidOperationException("A suspended logical discard changed its instruction or original primary materials.");
            draft = draft with { Materials = draft.Materials.Concat(ids.Select(id =>
                new NativeLogicalDiscardMaterial(id, _cardZones.GetLocation(id)))).ToArray() };
        }
        if (draft is not null) ReplaceRuntimeTop(frame = frame with { LogicalDiscardInvocation = draft });
        return frame;
    }

    private ProgramSkillFrame CompleteFrameNativeLogicalDiscard(ProgramSkillFrame frame)
    {
        if (!TracksNativeLogicalDiscards) return frame;
        frame = GetActiveProgramFrame(frame.Id);
        if (frame.LogicalDiscardInvocation is not { } draft) return frame;
        ReplaceRuntimeTop(frame = frame with { LogicalDiscardInvocation = null });
        CompleteNativeLogicalDiscardInvocation(draft);
        return GetActiveProgramFrame(frame.Id);
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 19000)]
    private void AssertNativeLogicalDiscardFrame(ProgramSkillFrame frame)
    {
        if (frame.LogicalDiscardInvocation is not { } draft) return;
        if (!TracksNativeLogicalDiscards || draft.InvocationId <= 0 || draft.InvocationId > _resolutionSequence ||
            draft.ParentFrameId != frame.Id || draft.InitiatorSeat != frame.OwnerSeat ||
            draft.InstructionIndex != frame.InstructionIndex || !IsValidPlayerSeat(draft.ExecutorSeat) ||
            draft.TurnNumber != _turnNumber || draft.ActualTurnOwnerSeat != _turnProgression.OwnerSeat ||
            draft.SequenceBefore < 0 || draft.SequenceBefore > _movementSequence ||
            string.IsNullOrWhiteSpace(draft.Reason.Value) || draft.Materials.Count == 0 ||
            draft.Materials.Select(m => m.CardId).Distinct().Count() != draft.Materials.Count ||
            draft.Materials.Any(m => !IsOwnedDiscardSource(m.From) || !IsValidPlayerSeat(m.From.OwnerSeat!.Value) ||
                _cardZones.GetLocation(m.CardId) != m.From && !_cardMovements.Any(move =>
                    move.CardId == m.CardId && move.Sequence > draft.SequenceBefore && move.From == m.From && move.Reason == draft.Reason)) ||
            ProgramEventHistory<NativeLogicalDiscardInvocationRecordedEvent>().Any(e => e.InvocationId == draft.InvocationId))
            throw new InvalidOperationException("A logical discard lost its owning instruction, frozen primary materials or original movement witness.");
    }
}
