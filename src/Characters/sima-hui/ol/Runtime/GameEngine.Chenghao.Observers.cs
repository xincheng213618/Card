using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 580, SkillProgramEffectOp.DistributeTopCardsAfterElementalChainOrigin)]
    private bool AllowsDistributeTopCardsAfterElementalChainOriginTrigger(ProgramTriggerLegalityContext gate)
    {
        var features = gate.Features;
        var candidate = gate.Candidate;
        var trigger = gate.Trigger;
        var context = gate.Context;
        return !(features.HasOperation(SkillProgramEffectOp.DistributeTopCardsAfterElementalChainOrigin) &&
            !TrySimaHuiChainOrigin(candidate.OwnerSeat, trigger, context, out _));
    }
    private bool CanOfferSimaHuiDamage(int owner,SkillProgramTrigger trigger,IDamageAttempt attack)
    {
        if(!ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.DistributeTopCardsAfterElementalChainOrigin))return true;
        return TrySimaHuiOriginalChainMembers(attack,out _);
    }
    private bool TrySimaHuiOriginalChainMembers(IDamageAttempt attack,out int[] members)
    {
        members=[];
        if(!attack.DamageWasApplied || attack.DamageAmount<=0 || attack.IsChainPropagation || GetDamageNature(attack)==DamageNature.Normal || !IsValidPlayerSeat(attack.TargetSeat))return false;
        IReadOnlyList<int> propagated;int index;
        if(attack is ProgramAttackHandle)
        {var state=GetProgramAttackState(attack.ResolutionId);if(state.ChainedTargetSeats is null)return false;propagated=state.ChainedTargetSeats;index=state.ChainedTargetIndex;}
        else if(attack is CardAttackHandle)
        {var state=GetCardAttackState(attack.ResolutionId);propagated=state.ChainedTargetSeats;index=state.ChainedTargetIndex;}
        else return false;
        if(index!=0 || propagated.Any(s=>!IsValidPlayerSeat(s) || s==attack.TargetSeat) || propagated.Distinct().Count()!=propagated.Count)return false;
        var original=new[]{attack.TargetSeat}.Concat(propagated).ToArray();
        var damage=_resolutionStack.OfType<DamageFrame>().LastOrDefault(d=>d.ParentFrameId==attack.ResolutionId && d.TargetSeat==attack.TargetSeat && d.SourceSeat==attack.SourceSeat);
        if(damage is null || damage.Amount!=attack.DamageAmount || damage.Nature!=GetDamageNature(attack))return false;
        var events=CompleteProgramEventHistory().ToArray();var requestIndex=Array.FindLastIndex(events,e=>e is DamageRequestedEvent requested && requested.ResolutionId==damage.Id && requested.SourceSeat==attack.SourceSeat &&
            requested.TargetSeat==attack.TargetSeat && requested.Amount==damage.Amount && requested.Nature==damage.Nature && requested.SourceLess==attack.IsSourceLess);
        // Native ApplyAttackDamage publishes this exact, contiguous clear block
        // immediately before the actual DamageRequested fact. The frozen native
        // propagation list and this block also prove a solo origin (empty tail).
        if(requestIndex<original.Length)return false;
        for(var i=0;i<original.Length;i++)if(events[requestIndex-original.Length+i] is not IronChainStateChangedEvent clear || clear.ResolutionId!=attack.ResolutionId || clear.SourceSeat!=attack.SourceSeat || clear.TargetSeat!=original[i] || clear.IsChained)return false;
        members=original;return true;
    }
    private bool TrySimaHuiChainOrigin(int owner,SkillProgramTrigger trigger,ProgramSkillWindowContext context,out SimaHuiChainOriginProof proof)
    {
        proof=null!;
        if(context is not{Window:SkillProgramTriggerWindow.AfterDamageApplied,Amount:>0,DamageFrameId:{}damageId,TargetSeat:{}target} ||
            _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId) is not{}window || window.ParentFrameId!=damageId || window.TargetSeat!=target ||
            _resolutionStack.OfType<DamageFrame>().SingleOrDefault(d=>d.Id==damageId) is not{}damage)return false;
        var attack=GetDamageTriggerAttack(window);if(!TrySimaHuiOriginalChainMembers(attack,out var originalMembers) || damage.ParentFrameId!=attack.ResolutionId || damage.TargetSeat!=target || damage.Amount!=context.Amount || damage.Nature!=GetDamageNature(attack))return false;
        proof=new(window.Id,attack.ResolutionId,_turnNumber,attack.SourceSeat,target,damage.Nature,attack.IsSourceLess,originalMembers.Length);return true;
    }
}
