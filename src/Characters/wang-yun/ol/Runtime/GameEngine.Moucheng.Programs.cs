using System.Globalization;
using System.Security.Cryptography;
using System.Text;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 360)]
    private bool CanAwakenWangYun(int owner,SkillProgramTrigger trigger)=>!ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.AwakenFromIssuedLianjiSlashDamage) ||
        CompleteProgramEventHistory().OfType<WangYunLianjiDamageEvent>().Any(e=>e.PlanOwner==owner && e.Amount>0) || HasShenGuoJiaAwakeningCondition(owner,"ol:moucheng",trigger.Id);
    private SkillProgramStepOutcome AwakenFromIssuedLianjiSlashDamage(ProgramSkillFrame supplied)
    {
        var f=GetActiveProgramFrame(supplied.Id);var c=f.WindowContext;
        if(f.WangYunAwakening is not null || f.InstructionIndex!=1 || f.TriggerId is null || c is not{Window:SkillProgramTriggerWindow.TurnStartBeforeNormalFlow} || f.OwnerSeat!=_currentSeat ||
            !CanAwakenWangYun(f.OwnerSeat,GetProgramTrigger(f)) || _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(p=>p.Id==c.ParentFrameId) is not{}parent ||
            parent.OwnerSeat!=f.OwnerSeat || parent.CandidateIndex<0 || parent.CandidateIndex>=parent.Candidates.Count || !MountObserverCandidateMatches(f,parent.Candidates[parent.CandidateIndex]))
            throw new InvalidOperationException("Moucheng lost its actual issued plan Slash damage and own preparation.");
        ReplaceRuntimeTop(f=f with{WangYunAwakening=new(WangYunSource(f),f.GameplayHash,c.ParentFrameId,_turnNumber,1)});
        GrantProgramSkills(f,["ol:jingong"]);foreach(var grant in _players[f.OwnerSeat].SkillGrants.Grants.Where(g=>g.SkillId=="ol:lianji").ToArray())_players[f.OwnerSeat].SkillGrants.RemoveGrant(grant.GrantId);
        AdvanceEventRulesAndQueueFact(new WangYunAwakenedEvent(f.Id,f.OwnerSeat,"ol:lianji","ol:jingong"));AdvanceRuntimeProgram(f.Id);return SkillProgramStepOutcome.AwaitChild;
    }
}
