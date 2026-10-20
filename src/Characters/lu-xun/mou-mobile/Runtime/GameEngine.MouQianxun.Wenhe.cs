namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TryBeginMouLuXunWenheTarget(ProgramSkillFrame frame, int actorSeat, int hpAmount)
    {
        if (!HasMouLuXun || frame.WenheChaos is not { CardActionId: not null } chaos ||
            !IsExactWenheChaos(frame) || LifecycleCardUse(chaos.ParentFrameId)?.Action is not { } action ||
            IsCardEffectIneffective(chaos.ParentFrameId, actorSeat) || !CanMouLuXunRecordNativeTarget(actorSeat, action)) return false;
        var cursor = frame.NumberBindings.Single(b => b.Name == "participant-cursor-" + (frame.InstructionIndex - 1)).Value;
        return BeginCardEffectPrograms(action, [actorSeat], null, null,
            new(chaos.ParentFrameId, actorSeat, cursor, WenheProgramFrameId: frame.Id, HpAmount: hpAmount), perTargetOnly: true);
    }

    private bool ValidMouLuXunWenheTarget(NativeTrickTargetEffectReturn receipt, CardActionContext action) =>
        receipt.GroupEffect is null && receipt.IronChainReturn is null && receipt.HpAmount == 1 &&
        _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == receipt.WenheProgramFrameId) is { } frame &&
        IsExactWenheChaos(frame) && frame.WenheChaos is { CardActionId: not null } chaos &&
        chaos.ParentFrameId == receipt.UseId && chaos.CardActionId == action.ActionId &&
        receipt.TargetIndex >= 1 && receipt.TargetIndex <= chaos.ActorSeats.Count &&
        chaos.ActorSeats[receipt.TargetIndex - 1] == receipt.TargetSeat && frame.SelectedTargetSeats.SequenceEqual([receipt.TargetSeat]) &&
        frame.NearestLegalSlashRequest is null && frame.ReexecuteParticipantInstruction &&
        frame.NumberBindings.SingleOrDefault(b => b.Name == "participant-cursor-" + (frame.InstructionIndex - 1))?.Value == receipt.TargetIndex;

    private void ReturnMouLuXunWenheTarget(NativeTrickTargetEffectReturn receipt)
    {
        var frame = GetActiveProgramFrame(receipt.WenheProgramFrameId!.Value);
        if (!_players[receipt.TargetSeat].IsAlive || _winner != Winner.None || IsCardEffectIneffective(receipt.UseId, receipt.TargetSeat))
        { AdvanceRuntimeProgram(frame.Id); return; }
        var outcome = RequestLegalSlashByNearestActor(frame, receipt.TargetSeat, receipt.HpAmount, trickEffectReturned: true);
        if (outcome == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(frame.Id);
    }
}
