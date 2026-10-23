namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool TryBeginHappyLuBuActualStart(CharacterState owner)
    {
        if(!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ObtainHappyShenLuBuTurnBasic)||!owner.IsAlive||
            ProgramEventHistory<HappyLuBuActualStartIssuedEvent>().Any(e=>e.ActualTurn==_turnNumber&&e.Source.OwnerSeat==owner.Seat))return false;
        var candidates=CollectProgramTriggerCandidates(owner,SkillProgramTriggerWindow.TurnStartBeforeNormalFlow).Where(c=>c.SkillId==HappyLuBuComposition.Wuqian&&c.BindingId=="actual-turn-basic").ToArray();
        if(candidates.Length==0)return false;
        if(_resolutionStack.Count!=0||_pendingDecision is not null||_currentSeat!=owner.Seat||ProgramEventHistory<TurnStartedEvent>().LastOrDefault()!=new TurnStartedEvent(_turnNumber,owner.Seat))throw new InvalidOperationException("Wuqian needs the genuine clean actual-turn start.");
        var c=candidates[0];var source=new CardConversionSource(c.SkillId,c.BindingId,c.OwnerSeat,c.SkillInstanceId);
        var p=new ProgramLifecycleTriggerWindowFrame(++_resolutionSequence,owner.Seat,SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,Array.AsReadOnly(new[]{c}),ProgramLifecycleContinuation.NormalTurnStart,CaptureProgramTriggerFacts(owner))
        {HappyLuBuActualStart=new(_turnNumber,source,c.GameplayHash)};
        AdvanceEventRulesAndQueueFact(new HappyLuBuActualStartIssuedEvent(p.Id,_turnNumber,source,c.GameplayHash));PushRuntimeFrame(p);AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();return true;
    }
    private bool IsHappyLuBuActualStart(ProgramLifecycleTriggerWindowFrame p)=>p.HappyLuBuActualStart is{}r&&p.OwnerSeat==_currentSeat&&r.ActualTurn==_turnNumber&&r.Source.OwnerSeat==p.OwnerSeat&&
        r.Source.SkillId==HappyLuBuComposition.Wuqian&&r.Source.BindingId=="actual-turn-basic"&&p.Window==SkillProgramTriggerWindow.TurnStartBeforeNormalFlow&&p.Continuation==ProgramLifecycleContinuation.NormalTurnStart&&
        p.Candidates is [var c]&&new CardConversionSource(c.SkillId,c.BindingId,c.OwnerSeat,c.SkillInstanceId)==r.Source&&c.GameplayHash==r.GameplayHash&&
        ProgramEventHistory<HappyLuBuActualStartIssuedEvent>().Count(e=>e==new HappyLuBuActualStartIssuedEvent(p.Id,r.ActualTurn,r.Source,r.GameplayHash))==1;
    [ProgramPipelineHook(ProgramPipelinePhase.LifecycleWindowCompleted,41200)] private bool CompleteHappyLuBuActualStart(ProgramLifecycleTriggerWindowFrame p)
    {
        if(p.HappyLuBuActualStart is null)return false;
        if(!IsHappyLuBuActualStart(p)||p.CandidateIndex!=1||_resolutionStack.Any(f=>f.Id==p.Id)||ProgramEventHistory<HappyLuBuActualStartReturnedEvent>().Any(e=>e.WindowId==p.Id))throw new InvalidOperationException("Wuqian lost its original actual-turn completion.");
        AdvanceEventRulesAndQueueFact(new HappyLuBuActualStartReturnedEvent(p.Id,_turnNumber,p.OwnerSeat));
        if(_winner!=Winner.None){if(_resolutionStack.Count==0&&_status!=EngineStatus.Completed)CompleteGame();}
        else if(!_players[p.OwnerSeat].IsAlive)EndTurn();else ContinueTurnAfterActualStart(_players[p.OwnerSeat]);return true;
    }
}
