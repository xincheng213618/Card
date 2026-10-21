namespace CardGame.Core;

public sealed partial class GameEngine
{
    private MouKongRongRuntime MouKongRong => GetRuntimeModule<MouKongRongRuntime>();
    private bool HasMouKongRong => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouKongRongLiwen);
    [ProgramCapability]
    private sealed class MouKongRongFacts(GameEngine e) : IMouKongRongFacts
    {
        public SkillProgram Program(string id)=>e._contentRegistry.GetSkill(id).Program!;
        public string Name(int seat)=>e._players[seat].Name;
        public long MovementSequence=>e._movementSequence;
        public bool GameOver=>e._winner!=Winner.None;
        public bool Alive(int seat)=>e.IsValidPlayerSeat(seat)&&e._players[seat].IsAlive;
        public bool Human(int seat)=>e._players[seat].IsHuman;
        public bool Friendly(int actor,int target)=>e._aiBrains[actor].ScoreProgramTarget(e.CreateSnapshot(actor),target,new(0,0,0,0,1,0,true,false))>0;
        public int Hp(int seat)=>e._players[seat].Hp;
        public int Xian(int seat)=>e._players[seat].Markers.GetValueOrDefault(PlayerMarkerKind.MouKongRongXian);
        public IReadOnlyList<int> Living=>Array.AsReadOnly(e._players.Where(p=>p.IsAlive).Select(p=>p.Seat).ToArray());
        public IReadOnlyList<T> History<T>()where T:IGameEvent=>Array.AsReadOnly(e.ProgramEventHistory<T>().ToArray());
        public bool ParentMatches(ProgramSkillFrame f)=>e.MouKrParentMatches(f);
        public CardActionContext? Action(ProgramSkillFrame f)=>e._resolutionStack.SingleOrDefault(p=>p.Id==f.WindowContext?.ParentFrameId)is ProgramCardTriggerWindowFrame w?w.Action:null;
        public MouKrZhengyiStartedEvent? Damage(ProgramSkillFrame f)=>e._resolutionStack.SingleOrDefault(p=>p.Id==f.WindowContext?.ParentFrameId)is BeforeDamageProgramWindowFrame{Nature:DamageNature.Normal,Amount:>0}w&&
            e.CurrentDamageAttempt is{}a&&a.TargetSeat==w.TargetSeat?new(f.Id,MouKongRongRuntime.Source(f),f.GameplayHash,w.Id,a.ResolutionId,w.TargetSeat,w.Amount):null;
        public IReadOnlyList<LegalAction> Actions(ProgramSkillFrame f,int seat)=>e.MouKrActions(f,seat);
        public bool DraftValid(LegalAction a,IReadOnlyList<int> cards,IReadOnlyList<int> targets)=>cards.Count>=a.MinCardCount&&cards.Count<=a.MaxCardCount&&cards.Distinct().Count()==cards.Count&&
            targets.Count>=a.MinTargetCount&&targets.Count<=a.MaxTargetCount&&targets.Distinct().Count()==targets.Count&&cards.All(a.SelectableCardIds.Contains)&&targets.All(a.SelectableTargetSeats.Contains)&&
            (a.SelectableTargetSequences is null||a.SelectableTargetSequences.Any(t=>t.SequenceEqual(targets)));
    }
    [ProgramCapability]
    private sealed class MouKongRongNative(GameEngine e):IMouKongRongNative
    {
        public int ChangeXian(ProgramSkillFrame f,int seat,int delta)
        {
            var old=e._players[seat].Markers.GetValueOrDefault(PlayerMarkerKind.MouKongRongXian);var next=Math.Clamp(old+delta,0,5);
            if(old!=next)e.SetStrategicMarker(seat,PlayerMarkerKind.MouKongRongXian,seat,next,f);return next-old;
        }
        public void IssueUse(ProgramSkillFrame f,int actor,LegalAction a,IReadOnlyList<int> cards,IReadOnlyList<int> targets)=>e.IssueMouKrUse(f,actor,a,cards,targets);
        public SkillProgramStepOutcome LoseHp(ProgramSkillFrame f,int seat,int amount)=>e.ApplyProgramHpLoss(f.Id,f.SkillId,seat,amount);
        public void PreventDamage(ProgramSkillFrame f)=>e.PreventProgramCurrentDamage(f);
        public bool Drain(ProgramSkillFrame f)=>e.TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.Program)||e.TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||
            e.TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.Program)||e.TryBeginCardsMovedProgramWindow(f.Id)||e.TryBeginAdvancedSkillsChanged(f.Id);
        public void Finish(ProgramSkillFrame f)=>e.FinishProgramSkill(f,true);
        public void Publish()=>e.AdvanceRulesAndPublishState();
    }
    private bool MouKrParentMatches(ProgramSkillFrame f)
    {
        if(f.TriggerId is null||f.ActivationId!=f.TriggerId||f.WindowContext is not{}c||c.OwnerSeat!=f.OwnerSeat||_contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash!=f.GameplayHash)return false;
        var index=_resolutionStack.FindIndex(p=>p.Id==f.Id);if(index<1||_resolutionStack[index-1].Id!=c.ParentFrameId)return false;
        var candidate=_resolutionStack[index-1]switch {
            ProgramLifecycleTriggerWindowFrame p when p.Window==c.Window&&p.CandidateIndex>=0&&p.CandidateIndex<p.Candidates.Count=>p.Candidates[p.CandidateIndex],
            DeferredTurnEndFrame p when c.Window==SkillProgramTriggerWindow.AfterTurnEnded&&p.TurnNumber==_turnNumber&&p.AfterTurnEnded is not null=>AfterTurnEndedCandidate(p),
            ProgramCardTriggerWindowFrame p when p.CandidateIndex>=0&&p.CandidateIndex<p.Candidates.Count=>new ProgramTriggerCandidate(p.Candidates[p.CandidateIndex].OwnerSeat,p.Candidates[p.CandidateIndex].SkillId,p.Candidates[p.CandidateIndex].TriggerId,p.Candidates[p.CandidateIndex].SkillInstanceId,p.Candidates[p.CandidateIndex].GameplayHash,p.Candidates[p.CandidateIndex].Priority),
            BeforeDamageProgramWindowFrame p when c.Window==SkillProgramTriggerWindow.BeforeDamageApplied&&p.CandidateIndex>=0&&p.CandidateIndex<p.Candidates.Count=>p.Candidates[p.CandidateIndex].Candidate,
            _=>(ProgramTriggerCandidate?)null};
        return candidate is{}actual&&MountObserverCandidateMatches(f,actual);
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,34600)]
    private void ObserveMouKrAction(IGameEvent payload)
    {
        if(!HasMouKongRong)return;
        CardActionContext? action=payload switch {
            CardUseDeclaredEvent e=>LifecycleCardUse(e.ResolutionId)?.Action,
            CardActionAcceptedEvent{Action.Type:CardActionType.Response}e=>e.Action,
            _=>null};
        if(action is null||ProgramEventHistory<MouKrActionObservedEvent>().Any(e=>e.ActionId==action.ActionId))return;
        var actor=action.ActorSeat;var previous=ProgramEventHistory<MouKrActionObservedEvent>().LastOrDefault(e=>e.ActorSeat==actor);
        var category=GetProgramCardCategory(action.EffectiveKind);var instance=GetSkillBindingShard(_players[actor]).ProgramInstances.FirstOrDefault(i=>i.SkillId==MouKongRongRuntime.Liwen);
        var source=instance is null?null:new CardConversionSource(instance.SkillId,"after-use",actor,instance.SkillInstanceId);
        var matches=previous is not null&&(action.EffectiveSuit is{}suit&&suit!=Suit.None&&previous.Suit==suit||previous.Category==category);
        AdvanceEventRulesAndQueueFact(new MouKrActionObservedEvent(action.ActionId,actor,action.EffectiveKind,action.EffectiveSuit,category,matches,source,instance?.Program.GameplayHash));
    }
}
