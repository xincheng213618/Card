namespace CardGame.Core;

public sealed record ProgramEquipmentRecastReceipt(int InstructionIndex, int CardId, CardKind CardKind,
    bool DrawApplied = false, int DrawCount = 0);

public sealed partial class GameEngine
{
    private bool TryPauseProgramEquipmentRecast(ProgramSkillFrame paidFrame, Card card)
    {
        var frame = GetActiveProgramFrame(paidFrame.Id);
        if (frame.PendingRecoveryAttempts is not { Count: > 0 }) return false;
        if (frame.EquipmentRecast is not null)
            throw new InvalidOperationException("A program equipment recast already has a paid receipt.");
        ReplaceRuntimeTop(frame with { EquipmentRecast = new(frame.InstructionIndex, card.Id, card.Kind) });
        return true;
    }

    private bool ResumeProgramEquipmentRecast(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != frameId ||
            frame.EquipmentRecast is not { } receipt) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
        if (frame.InstructionIndex != receipt.InstructionIndex || receipt.InstructionIndex < 1 ||
            receipt.InstructionIndex > plan.Instructions.Count || !EquipmentCatalog.IsEquipment(receipt.CardKind) ||
            plan.Instructions[receipt.InstructionIndex - 1].Op != SkillProgramEffectOp.RecastSelectedEquipment)
            throw new InvalidOperationException("A paid equipment recast lost its exact owning instruction.");
        if (TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.Program) ||
            TryBeginCardsMovedProgramWindow(frame.Id)) return true;
        if (!receipt.DrawApplied)
        {
            ReplaceRuntimeTop(frame with { EquipmentRecast = receipt with { DrawApplied = true } });
            var owner = _players[frame.OwnerSeat];
            var drawn = _winner == Winner.None && owner.IsAlive
                ? DrawCards(owner, 1, true, CardMoveReasons.RecastDraw) : Array.Empty<int>();
            frame = (ProgramSkillFrame)_resolutionStack.Last();
            ReplaceRuntimeTop(frame with { EquipmentRecast = frame.EquipmentRecast! with { DrawCount = drawn.Count } });
            AdvanceEventRulesAndQueueFact(new CardRecastEvent(owner.Seat, receipt.CardId, receipt.CardKind, drawn.Count));
            return ResumeProgramEquipmentRecast(frame.Id);
        }
        ReplaceRuntimeTop(frame with { EquipmentRecast = null });
        return false;
    }
}
