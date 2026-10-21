namespace CardGame.Core;
public sealed partial class GameEngine
{
    private MouCaoAngRuntime MouCaoAng=>GetRuntimeModule<MouCaoAngRuntime>();
    [ProgramCapability]private sealed class MouCaoAngFacts(GameEngine e):IMouCaoAngFacts
    {
        public int Turn=>e._turnNumber;public int TurnOwner=>e._turnProgression.OwnerSeat;public long Sequence=>e._movementSequence;public bool Finished=>e._winner!=Winner.None;
        public bool Alive(int s)=>e.IsValidPlayerSeat(s)&&e._players[s].IsAlive;public bool Human(int s)=>e._players[s].IsHuman;public string Name(int s)=>e._players[s].Name;
        public SkillProgram? Program(string id)=>e._contentRegistry.Skills.GetValueOrDefault(id)?.Program;public IEnumerable<T> History<T>()where T:IGameEvent=>e.ProgramEventHistory<T>();
        public bool Parent(ProgramSkillFrame f)=>e.MouCaParent(f);
        public int LostEquipmentOwner(ProgramSkillFrame f)=>f.WindowContext?.MovementBatch is{}batch&&f.WindowContext.MovementIndex is{}index&&index>=0&&index<batch.Movements.Count&&batch.Movements[index]is{From.Zone:CardZoneKind.Equipment}m&&m.To!=m.From&&e._cardMovements.Contains(m)?m.From.OwnerSeat??-1:-1;
        public int EmptySlots(int seat)=>Enum.GetValues<EquipmentSlot>().Sum(slot=>Math.Max(0,e._players[seat].EquipmentSlotCapacity(slot)-e.GetEquipment(seat).Count(c=>EquipmentCatalog.Get(c.Kind).Slot==slot)));
        public IReadOnlyList<MouCaEntity> Equipment(int seat)=>Array.AsReadOnly(e.GetEquipment(seat).Where(c=>!e.IsForeignEquipmentDiscardPrevented(seat,c,CardLocation.Equipment(seat),OwnedCardMoveIntent.Discard)).Select(c=>new MouCaEntity(c.Id,c.Kind,CardLocation.Equipment(seat))).ToArray());
        public IReadOnlyList<MouCaPair> Pairs(ProgramSkillFrame f)=>e.MouCaPairs(f);public IReadOnlyList<LegalAction> Plans(ProgramSkillFrame f)=>f.MouCaoAng is{}r?e.MouCaPlans(f,r.Actor,r.CardId):[];
        public CardKind Kind(int id)=>e.GetAdvancedCard(id).Kind;
        public double TargetValue(int actor,int target)=>e._aiBrains[actor].ScoreProgramTarget(e.CreateSnapshot(actor),target,new(0,0,1,0,0,0,false,false));
    }
    [ProgramCapability]private sealed class MouCaoAngNative(GameEngine e):IMouCaoAngNative
    {
        public void Finish(ProgramSkillFrame f)=>e.FinishProgramSkill(f,true);public void Publish()=>e.AdvanceRulesAndPublishState();public void Use(ProgramSkillFrame f)=>e.IssueMouCaUse(f);
        public bool Drain(ProgramSkillFrame f)=>e.TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||e.TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||e.TryBeginAdvancedSkillsChanged(f.Id)||e.TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||e.TryBeginCardsMovedProgramWindow(f.Id);
        public void Discard(ProgramSkillFrame f)
        {
            if(!e.MouCaoAng.Valid(f)||f.MouCaoAng is not{Stage:MouCaoAngStage.DiscardChildren,ActualDiscard:0}r||r.Equipment.Count==0||r.Equipment.Any(c=>e._cardZones.GetLocation(c.Id)!=c.From))throw new InvalidOperationException("Zhiwang requires its real current equipment group once.");
            e.MoveProgramLogicalDiscard(f,f.OwnerSeat,r.Equipment.Select(c=>c.Id).ToArray(),new(MouCaoAngRuntime.DiscardReason),(_,moves)=>
            {
                var current=e.GetActiveProgramFrame(f.Id);var paid=current.MouCaoAng!;
                if(!e.MouCaoAng.Valid(current)||moves.Count!=r.Equipment.Count||moves.Any(m=>!r.Equipment.Any(c=>c.Id==m.CardId&&c.Kind==m.CardKind&&c.From==m.From)||m.Sequence<=r.Before||m.To!=CardLocation.DiscardPile&&m.To!=CardLocation.OutsideGame))throw new InvalidOperationException("Zhiwang lost its actual native logical discard group.");
                e.ReplaceRuntimeFrame(f.Id,current with{MouCaoAng=paid with{After=moves.Max(m=>m.Sequence),ActualDiscard=moves.Count}});foreach(var m in moves)e.AdvanceEventRulesAndQueueFact(new MouCaDiscardedEvent(f.Id,m.CardId,m.CardKind,m.To,m.Sequence));
            });
        }
    }
    private bool MouCaParent(ProgramSkillFrame f)
    {
        if(f.TriggerId is null||f.ActivationId!=f.TriggerId||f.WindowContext is not{}w||w.OwnerSeat!=f.OwnerSeat||_contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash!=f.GameplayHash)return false;
        var at=_resolutionStack.FindIndex(x=>x.Id==f.Id);if(at<1||_resolutionStack[at-1].Id!=w.ParentFrameId)return false;
        ProgramTriggerCandidate? candidate=_resolutionStack[at-1] switch
        {
            CardsMovedTriggerWindowFrame m when f.SkillId==MouCaoAngRuntime.Fengmin&&w.Window==SkillProgramTriggerWindow.CardsMoved&&m.CandidateIndex>=0&&m.CandidateIndex<m.Candidates.Count&&m.Batch.Id==w.MovementBatch?.Id=>m.Candidates[m.CandidateIndex],
            TurnEndingBoundaryFrame end when f.SkillId==MouCaoAngRuntime.Zhiwang&&w.Window==SkillProgramTriggerWindow.TurnEnding&&end.OwnerSeat==f.OwnerSeat&&end.TurnNumber==_turnNumber&&end.ItemIndex>=0&&end.ItemIndex<end.Items.Count=>end.Items[end.ItemIndex].Candidate,
            ProgramLifecycleTriggerWindowFrame life when f.SkillId==MouCaoAngRuntime.Zhiwang&&life.Window==SkillProgramTriggerWindow.TurnEnding&&w.Window==life.Window&&life.CandidateIndex>=0&&life.CandidateIndex<life.Candidates.Count=>life.Candidates[life.CandidateIndex],
            _=>null
        };
        return candidate is{}c&&MountObserverCandidateMatches(f,c);
    }
}
