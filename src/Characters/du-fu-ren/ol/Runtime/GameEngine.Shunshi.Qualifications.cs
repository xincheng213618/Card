namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasDuFuRenShunshi => _contentRegistry?.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GiveForNextActualPhases) == true;
    private bool IsDuFuRenGiftContext(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (context.OwnerSeat != c.OwnerSeat || context.TargetSeat != c.OwnerSeat || !_players[c.OwnerSeat].IsAlive) return false;
        int? excluded;
        if (context.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)
        {
            if (c.OwnerSeat != _turnProgression.OwnerSeat || _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not
                { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow } parent || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || parent.Candidates[parent.CandidateIndex] != c) return false;
            excluded = null;
        }
        else
        {
            if (context.Window != SkillProgramTriggerWindow.AfterDamageApplied || c.OwnerSeat == _turnProgression.OwnerSeat || context.Amount <= 0 ||
                context.DamageFrameId is not { } damageId || _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w => w.ParentFrameId == damageId && w.Id == context.ParentFrameId) is not { } parent ||
                parent.TriggerWindow != SkillProgramTriggerWindow.AfterDamageApplied || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || parent.Candidates[parent.CandidateIndex].ToProgramCandidate() != c) return false;
            excluded = GetDamageTriggerAttack(parent).IsSourceLess ? null : context.SourceSeat;
        }
        return _players.Any(p => p.IsAlive && p.Seat != c.OwnerSeat && p.Seat != excluded) && (GetHand(_players[c.OwnerSeat]).Count > 0 ||
            GetEquipment(_players[c.OwnerSeat]).Any(card => !card.IsGeneralWeapon && !IsActiveProgramSourceEquipmentCard(c.OwnerSeat,c.SkillId,c.SkillInstanceId,card) &&
                !IsForeignEquipmentDiscardPrevented(c.OwnerSeat,card,CardLocation.Equipment(c.OwnerSeat),OwnedCardMoveIntent.Transfer)));
    }
}
