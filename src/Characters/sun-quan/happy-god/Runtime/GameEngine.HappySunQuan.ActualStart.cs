namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasHappySunQuanContent=>_contentRegistry.Skills.ContainsKey(HappySunQuanComposition.Learning);
    private int HappyXue(int holder)=>_players[holder].Markers.GetValueOrDefault(PlayerMarkerKind.HappyXue);
    private static string HappyLearningInstance(int holder,int turn)=>$"issued:happy-learning:{holder}:{turn}";
    private ProgramTriggerCandidate HappyLearningCandidate(int holder,int turn)
    {
        var p=_contentRegistry.GetSkill(HappySunQuanComposition.Learning).Program!;
        return new(holder,p.Id,"resolve-study",HappyLearningInstance(holder,turn),p.GameplayHash,p.Triggers.Single().Priority);
    }
    private bool TryBeginHappyLearningActualStart(CharacterState current)
    {
        if(!HasHappySunQuanContent||!current.IsAlive||HappyXue(current.Seat)==0||
            ProgramEventHistory<HappyLearningIssuedEvent>().Any(e=>e.ActualTurn==_turnNumber&&e.HolderSeat==current.Seat))return false;
        if(_resolutionStack.Count!=0||_pendingDecision is not null||current.Seat!=_currentSeat||_turnNumber<1||
            ProgramEventHistory<TurnStartedEvent>().LastOrDefault() is not{}start||start.ActorSeat!=current.Seat||start.TurnNumber!=_turnNumber)
            throw new InvalidOperationException("Learning requires the real clean actual turn start, including a face-down turn.");
        var grants=_players[current.Seat].MarkerSourceCounts.Where(p=>p.Key.Marker==PlayerMarkerKind.HappyXue&&p.Value>0)
            .Select(p=>ProgramEventHistory<HappyXueGrantEvent>().LastOrDefault(e=>e.Target==current.Seat&&e.Source.OwnerSeat==p.Key.SkillOwnerSeat&&e.After==p.Value)
                ??throw new InvalidOperationException("Learning lost its original printed Quanxue grant.")).ToArray();
        if(grants.Sum(g=>g.After)!=HappyXue(current.Seat))throw new InvalidOperationException("Learning lost its exact attributed source counts.");
        var candidates=new List<ProgramTriggerCandidate>{HappyLearningCandidate(current.Seat,_turnNumber)};
        candidates.AddRange(_players.Where(p=>p.IsAlive&&p.Seat!=current.Seat).OrderBy(p=>RelativeTableOrder(current.Seat,p.Seat))
            .SelectMany(p=>CollectProgramTriggerCandidates(p,SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)).Where(c=>c.SkillId==HappySunQuanComposition.Dingli));
        var parent=new ProgramLifecycleTriggerWindowFrame(++_resolutionSequence,current.Seat,SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,
            Array.AsReadOnly(candidates.ToArray()),ProgramLifecycleContinuation.NormalTurnStart,CaptureProgramTriggerFacts(current))
        {HappyLearningStart=new(_turnNumber,current.Seat){Grants=grants},ParticipantFacts=new System.Collections.ObjectModel.ReadOnlyDictionary<int,SkillProgramTriggerFacts>(candidates.Select(c=>c.OwnerSeat).Distinct().ToDictionary(s=>s,s=>CaptureProgramTriggerFacts(_players[s])))};
        AdvanceEventRulesAndQueueFact(new HappyLearningIssuedEvent(parent.Id,_turnNumber,current.Seat,grants.Length,grants.Sum(g=>g.After)));
        PushRuntimeFrame(parent);AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();return true;
    }
    private bool IsHappyLearningStart(ProgramLifecycleTriggerWindowFrame p)=>p.HappyLearningStart is{}r&&p.OwnerSeat==r.HolderSeat&&p.OwnerSeat==_currentSeat&&r.ActualTurn==_turnNumber&&
        p.Window==SkillProgramTriggerWindow.TurnStartBeforeNormalFlow&&p.Continuation==ProgramLifecycleContinuation.NormalTurnStart&&p.Candidates.Count>0&&p.Candidates[0]==HappyLearningCandidate(p.OwnerSeat,_turnNumber)&&
        r.Grants.Count>0&&r.Grants.Select(g=>g.Source.OwnerSeat).Distinct().Count()==r.Grants.Count&&r.Grants.All(g=>g.Target==p.OwnerSeat&&g.After>0&&g.After==g.Before+1&&
            g.Source.SkillId==HappySunQuanComposition.Quanxue&&g.Source.BindingId=="grant-study"&&_contentRegistry.GetSkill(g.Source.SkillId).Program!.GameplayHash==g.GameplayHash&&
            ProgramEventHistory<HappyXueGrantEvent>().Count(e=>e==g)==1&&ProgramEventHistory<ProgramBindingStartedEvent>().Count(e=>e.FrameId==g.FrameId&&e.OwnerSeat==g.Source.OwnerSeat&&e.SkillId==g.Source.SkillId&&e.SkillInstanceId==g.Source.SkillInstanceId&&e.BindingId==g.Source.BindingId)==1)&&
        ProgramEventHistory<HappyLearningIssuedEvent>().Count(e=>e==new HappyLearningIssuedEvent(p.Id,r.ActualTurn,r.HolderSeat,r.Grants.Count,r.Grants.Sum(g=>g.After)))==1;
    [ProgramPipelineHook(ProgramPipelinePhase.IssuedTriggerCandidate,40940)]
    private bool IsIssuedHappyLearning(ProgramTriggerCandidate c,ProgramSkillWindowContext w)=>c.SkillId==HappySunQuanComposition.Learning&&c==HappyLearningCandidate(c.OwnerSeat,_turnNumber)&&
        w.Window==SkillProgramTriggerWindow.TurnStartBeforeNormalFlow&&w.OwnerSeat==c.OwnerSeat&&w.SourceSeat==c.OwnerSeat&&w.TargetSeat==c.OwnerSeat&&
        _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(p=>p.Id==w.ParentFrameId) is{}parent&&IsHappyLearningStart(parent)&&parent.CandidateIndex==0&&parent.Candidates[0]==c&&
        (_players[c.OwnerSeat].IsAlive||_resolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.HappySunQuan is{Operation:SkillProgramEffectOp.ResolveHappySunQuanLearning,Branch:2}&&f.WindowContext==w&&MountObserverCandidateMatches(f,c)));
    [ProgramPipelineHook(ProgramPipelinePhase.LifecycleWindowCompleted,40940)]
    private bool CompleteHappyLearningStart(ProgramLifecycleTriggerWindowFrame parent)
    {
        if(parent.HappyLearningStart is null)return false;
        if(!IsHappyLearningStart(parent)||parent.CandidateIndex<0||parent.CandidateIndex>parent.Candidates.Count||_winner==Winner.None&&parent.CandidateIndex!=parent.Candidates.Count||
            _resolutionStack.Any(p=>p.Id==parent.Id)||ProgramEventHistory<HappyLearningRemovedEvent>().Count(e=>e.WindowId==parent.Id)!=1||
            ProgramEventHistory<HappyLearningReturnedEvent>().Any(e=>e.WindowId==parent.Id))throw new InvalidOperationException("Learning lost its exact actual-start typed return.");
        AdvanceEventRulesAndQueueFact(new HappyLearningReturnedEvent(parent.Id,_turnNumber,parent.OwnerSeat));
        if(_winner!=Winner.None){if(_resolutionStack.Count==0&&_status!=EngineStatus.Completed)CompleteGame();return true;}
        if(!_players[parent.OwnerSeat].IsAlive){EndTurn();return true;}
        ContinueTurnAfterActualStart(_players[parent.OwnerSeat]);return true;
    }
    private bool HasHappyLearningPlaySelfOnly(int actor)=>HasHappySunQuanContent&&actor==_currentSeat&&_phase==TurnPhase.Play&&
        ProgramEventHistory<HappyLearningChoiceEvent>().Any(e=>e.ActualTurn==_turnNumber&&e.HolderSeat==actor&&e.Branch==1&&
            ProgramEventHistory<HappyLearningRemovedEvent>().Count(m=>m.FrameId==e.FrameId&&m.WindowId==e.WindowId&&m.ActualTurn==e.ActualTurn&&m.HolderSeat==actor&&m.Total>0)==1);
}
