namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool IsCaoTengQingliuDyingExitContext(ProgramTriggerCandidate candidate,ProgramSkillWindowContext context)
    {
        if(GetProgramTrigger(candidate).Effects is not[{Op:SkillProgramEffectOp.ResolveQingliuFactionChoice}]||context.Window!=SkillProgramTriggerWindow.DyingExited||context.OwnerSeat!=candidate.OwnerSeat||
            context.SourceSeat!=candidate.OwnerSeat||context.TargetSeat!=candidate.OwnerSeat||!_players[candidate.OwnerSeat].IsAlive||_players[candidate.OwnerSeat].Hp<=0||
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId)is not{Window:SkillProgramTriggerWindow.DyingExited,Continuation:ProgramLifecycleContinuation.ResumeYanPuDyingExit,YanPuDyingExit:{}ret}parent||
            ret.VictimSeat!=candidate.OwnerSeat||ret.Round!=_roundNumber||ret.Turn!=_turnNumber||ret.TurnOwner!=_currentSeat||parent.OwnerSeat!=candidate.OwnerSeat||
            parent.CandidateIndex<0||parent.CandidateIndex>=parent.Candidates.Count||parent.Candidates[parent.CandidateIndex]!=candidate)return false;
        var i=_resolutionStack.FindIndex(w=>w.Id==parent.Id);
        return i>0&&_resolutionStack[i-1].Id==ret.DyingParentFrameId&&ProgramEventHistory<PlayerDyingEvent>().FirstOrDefault(e=>e.VictimSeat==candidate.OwnerSeat)?.ResolutionId==ret.DyingFrameId&&
            ProgramEventHistory<DyingResolvedEvent>().Count(e=>e.ResolutionId==ret.DyingFrameId&&e.VictimSeat==candidate.OwnerSeat&&e.Survived)==1&&
            !ProgramEventHistory<CaoTengFactionCompletedEvent>().Any(e=>e.Source.OwnerSeat==candidate.OwnerSeat&&!e.Setup);
    }
    private void ApplyCaoTengFaction(ProgramSkillFrame f,string faction,bool setup)
    {
        ReplaceRuntimeTop(f=f with{CaoTeng=f.CaoTeng! with{Faction=faction,Stage=CaoTengStage.FactionApplying}});
        AdvanceEventRulesAndQueueFact(new CaoTengFactionChosenEvent(f.Id,faction,setup));ApplyCaoTengFactionChange(f,faction);
        AdvanceEventRulesAndQueueFact(new CaoTengFactionCompletedEvent(f.Id,CaoTengSource(f),faction,setup));FinishCaoTeng(GetActiveProgramFrame(f.Id));
    }
    private bool IsCaoTengFactionChange(ProgramSkillFrame f,string faction)=>f.CaoTeng is{Op:SkillProgramEffectOp.ResolveQingliuFactionChoice,Stage:CaoTengStage.FactionApplying}r&&r.Faction==faction&&faction is("qun" or "wei")&&
        r.Source==CaoTengSource(f)&&r.GameplayHash==f.GameplayHash&&f.InstructionIndex==1&&!ProgramEventHistory<CaoTengFactionCompletedEvent>().Any(e=>e.FrameId==f.Id)&&CaoTengParentMatches(f)&&_resolutionStack.LastOrDefault()?.Id==f.Id&&
        ProgramEventHistory<CaoTengFactionChosenEvent>().Count(e=>e.FrameId==f.Id&&e.Faction==faction&&e.Setup==(f.WindowContext!.Window==SkillProgramTriggerWindow.GameStarting))==1&&
        (f.WindowContext!.Window==SkillProgramTriggerWindow.GameStarting||faction==(GetEffectiveFactionId(_players[f.OwnerSeat])=="wei"?"qun":"wei"));
}
