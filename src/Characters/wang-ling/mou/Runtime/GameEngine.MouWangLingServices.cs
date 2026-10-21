namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class MouWangLingFacts(GameEngine engine) : IMouWangLingFacts
    {
        public int Turn=>engine._turnNumber;
        public int CurrentSeat=>engine._currentSeat;
        public int PlayerCount=>engine._playerCount;
        public long MovementSequence=>engine._movementSequence;
        public bool GameOver=>engine._winner!=Winner.None;
        public bool Alive(int seat)=>engine.IsValidPlayerSeat(seat)&&engine._players[seat].IsAlive;
        public bool Human(int seat)=>engine._players[seat].IsHuman;
        public int MaximumHp(int seat)=>engine._players[seat].MaxHp;
        public string Name(int seat)=>engine._players[seat].Name;
        public SkillProgram Program(string id)=>engine._contentRegistry.GetSkill(id).Program!;
        public CardLocation Location(int id)=>engine._cardZones.GetLocation(id);
        public IReadOnlyList<Card> Hand(int seat)=>engine.GetHand(engine._players[seat]);
        public CardKind HandKind(int seat,int id)=>engine.AdvancedEffectiveHandKind(engine._players[seat],engine.GetAdvancedCard(id));
        public IEnumerable<T> History<T>() where T:IGameEvent=>engine.ProgramEventHistory<T>();
        public IReadOnlyList<CardMovementRecord> Movements(long before,long after)=>Array.AsReadOnly(engine._cardMovements.Where(m=>m.Sequence>before&&m.Sequence<=after).ToArray());
        public double KeepValue(int owner,int id)=>GetKeepValue(engine.GetAdvancedCard(id),engine._players[owner]);
        public double TargetValue(int owner,int target)=>engine._aiBrains[owner].ScoreProgramTarget(engine.CreateSnapshot(owner),target,new(0,0,0,0,1,0,true,false));
        public bool ParentMatches(ProgramSkillFrame f)=>engine.MouWangLingParentMatches(f);
    }
    [ProgramCapability]
    private sealed class MouWangLingOperations(GameEngine engine) : IMouWangLingOperations
    {
        public void Give(ProgramSkillFrame f,int id,int target)=>engine.MoveProgramCardsFromMultipleSources([id], CardLocation.Hand(target), new CardMoveReason(MouWangLingRuntime.GiftReason), engine.NativeGiveCompletionRecords(f.Id,f.OwnerSeat,target,[id],engine._movementSequence,null), disclosure:CardMovementDisclosure.FaceUp);
        public void Discard(ProgramSkillFrame f,IReadOnlyList<int> ids)=>engine.MoveProgramLogicalDiscard(f,f.OwnerSeat,ids,new(MouWangLingRuntime.DiscardReason));
        public bool Drain(ProgramSkillFrame f)=>engine.TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.Program)||
            engine.TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||engine.TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.Program)||
            engine.TryBeginCardsMovedProgramWindow(f.Id)||engine.TryBeginAdvancedSkillsChanged(f.Id);
        public void Finish(ProgramSkillFrame f)=>engine.FinishProgramSkill(f,true);
    }
    private bool MouWangLingParentMatches(ProgramSkillFrame f)
    {
        if(f.TriggerId is null||f.ActivationId!=f.TriggerId||f.WindowContext is not{}context||context.SourceSeat!=f.OwnerSeat||context.TargetSeat!=f.OwnerSeat||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash!=f.GameplayHash)return false;
        var i=_resolutionStack.FindIndex(x=>x.Id==f.Id);
        if(i<1||_resolutionStack[i-1].Id!=context.ParentFrameId)return false;
        ProgramTriggerCandidate? candidate=_resolutionStack[i-1]switch{
            TurnEndingBoundaryFrame p when p.OwnerSeat==f.OwnerSeat&&p.TurnNumber==_turnNumber&&p.ItemIndex>=0&&p.ItemIndex<p.Items.Count=>p.Items[p.ItemIndex].Candidate,
            PlayPhaseStartingBoundaryFrame p when p.OwnerSeat==f.OwnerSeat&&p.ItemIndex>=0&&p.ItemIndex<p.Items.Count=>p.Items[p.ItemIndex].Candidate,
            ProgramLifecycleTriggerWindowFrame p when p.OwnerSeat==f.OwnerSeat&&p.Window==context.Window&&p.CandidateIndex>=0&&p.CandidateIndex<p.Candidates.Count=>p.Candidates[p.CandidateIndex],
            DeferredTurnEndFrame p when p.OwnerSeat==f.OwnerSeat&&p.TurnNumber==_turnNumber&&p.AfterTurnEnded is not null=>AfterTurnEndedCandidate(p),
            _=>null};
        return candidate is{}c&&c.OwnerSeat==f.OwnerSeat&&c.SkillId==f.SkillId&&c.BindingId==f.TriggerId&&c.SkillInstanceId==f.SkillInstanceId&&c.GameplayHash==f.GameplayHash;
    }
}
