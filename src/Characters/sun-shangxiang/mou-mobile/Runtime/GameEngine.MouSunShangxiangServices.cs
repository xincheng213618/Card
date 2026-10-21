namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasMouSunShangxiangMobile=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouSunShangxiangJieyin)||
        _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveMouSunShangxiangLiangzhu);
    private MouSunShangxiangRuntime MouSunShangxiang=>GetRuntimeModule<MouSunShangxiangRuntime>();
    private const string MouSsxGiveReason="skill-program.mou-mobile:jieyin.give",MouSsxDepositReason="skill-program.mou-mobile:liangzhu.zhuang",
        MouSsxObtainReason="skill-program.mou-mobile:jieyin.failure-obtain",MouSsxDiscardReason="skill-program.mou-mobile:xiaoji.discard";
    [ProgramCapability]
    private sealed class MouSunShangxiangFacts(GameEngine e):IMouSunShangxiangFacts
    {
        public bool GameOver=>e._winner!=Winner.None;
        public long MovementSequence=>e._movementSequence;
        public bool Alive(int seat)=>e.IsValidPlayerSeat(seat)&&e._players[seat].IsAlive;
        public bool Human(int seat)=>e._players[seat].IsHuman;
        public string Name(int seat)=>e._players[seat].Name;
        public string? Faction(int seat)=>e.GetEffectiveFactionId(e._players[seat]);
        public SkillProgram Program(string id)=>e._contentRegistry.GetSkill(id).Program!;
        public IReadOnlyList<int> Living=>Array.AsReadOnly(e._players.Where(p=>p.IsAlive).Select(p=>p.Seat).ToArray());
        public IReadOnlyList<Card> Hand(int seat)=>e.GetHand(e._players[seat]);
        public CardLocation Location(int id)=>e._cardZones.GetLocation(id);
        public IEnumerable<T> History<T>()where T:IGameEvent=>e.ProgramEventHistory<T>();
        public bool ParentMatches(ProgramSkillFrame f)=>e.MouSsxParentMatches(f);
        public bool MovementRecoveryChild(ProgramSkillFrame f,ResolutionFrame child)
        {
            if(f.MouSsx is not{Paid:true,Stage:MouSsxStage.DepositChildren or MouSsxStage.DiscardChildren}r||
                r.Entities is not[{Kind:CardKind.SilverLion,From.Zone:CardZoneKind.Equipment}entity])return false;
            var seat=entity.From.OwnerSeat!.Value;var reason=r.Stage==MouSsxStage.DepositChildren?MouSsxDepositReason:MouSsxDiscardReason;
            if(!e._cardMovements.Any(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&m.CardId==entity.CardId&&m.CardKind==CardKind.SilverLion&&m.From==entity.From&&m.Reason.Value==reason))return false;
            if(child is RecoveryReplacementFrame recovery)return e.RecoveryReplacementFrameRidesOn(recovery,f)&&recovery.Return.Continuation==PostEventContinuation.Program&&
                recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion&&recovery.Attempt.Completion.MoveReason?.Value==reason&&
                recovery.Attempt.Amount==1&&recovery.Attempt.SourceSeat==seat&&recovery.Attempt.TargetSeat==seat;
            return child is HpChangedTriggerWindowFrame hp&&hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&hp.Continuation==PostEventContinuation.Program&&
                hp.Change.Kind==HpChangeKind.Recovery&&hp.Change.Amount>0&&hp.Change.SourceSeat==seat&&hp.Change.TargetSeat==seat&&
                e.ProgramEventHistory<SilverLionRemovedRecoveryEvent>().Any(x=>x.ResolutionId>f.Id&&x.ResolutionId<hp.Change.Id&&x.PlayerSeat==seat&&x.Reason.Value==reason&&x.RecoveredAmount==hp.Change.Amount&&x.RemainingHp==hp.Change.HpAfter);
        }
        public IReadOnlyList<MouSsxEntity> Field(int owner,bool equipmentOnly)=>Array.AsReadOnly(e._players.Where(p=>p.IsAlive&&(!equipmentOnly||p.Seat!=owner))
            .SelectMany(p=>(equipmentOnly?new[]{CardZoneKind.Equipment}:new[]{CardZoneKind.Equipment,CardZoneKind.Judgment}).SelectMany(zone=>e._cardZones.CardsAt(new(zone,p.Seat))
                .Where(c=>!e.IsForeignEquipmentDiscardPrevented(owner,c,new(zone,p.Seat),equipmentOnly?OwnedCardMoveIntent.Transfer:OwnedCardMoveIntent.Discard))
                .Select(c=>new MouSsxEntity(c.Id,c.Kind,new(zone,p.Seat))))).ToArray());
        public IReadOnlyList<MouSsxEntity> Zhuang(int owner)=>Array.AsReadOnly(e.PublicPileSources(owner).Where(p=>p.SkillId==MouSunShangxiangRuntime.Liangzhu)
            .SelectMany(p=>e.PublicPileCards(p).Select(c=>new MouSsxEntity(c.Id,c.Kind,p.Location))).ToArray());
        public double HelpValue(int actor,int target)=>e._aiBrains[actor].ScoreProgramTarget(e.CreateSnapshot(actor),target,new(0,0,0,0,1,0,true,false));
        public double KeepValue(int actor,int id)=>GetKeepValue(e.GetAdvancedCard(id),e._players[actor]);
    }
    [ProgramCapability]
    private sealed class MouSunShangxiangNative(GameEngine e):IMouSunShangxiangNative
    {
        public void Mark(ProgramSkillFrame f,int previous,int current,bool initial)=>e.ChangeMouSsxZhu(f,previous,current,initial);
        public void Give(ProgramSkillFrame f,int payer,IReadOnlyList<int> ids)=>e.MoveProgramCardsFromMultipleSources(ids,CardLocation.Hand(f.OwnerSeat),new(MouSsxGiveReason),
            (_,moves)=>{foreach(var move in moves)e.SealMouSsxMove(f.Id,move,false);},disclosure:CardMovementDisclosure.FaceDown);
        public void Deposit(ProgramSkillFrame f,MouSsxEntity entity)
        {
            if(!new MouSunShangxiangFacts(e).Field(f.OwnerSeat,true).Contains(entity))throw new InvalidOperationException("Liangzhu lost its actual foreign Equipment entity or transfer permission.");
            var pile=e.EnsurePublicPileSource(f,int.MaxValue);e.MoveCard(e.GetAdvancedCard(entity.CardId),entity.From,pile.Location,new(MouSsxDepositReason),m=>e.SealMouSsxMove(f.Id,m,true),disclosure:CardMovementDisclosure.FaceUp);
        }
        public void ObtainZhuang(ProgramSkillFrame f,IReadOnlyList<MouSsxEntity> entities)
        {
            var live=entities.Where(x=>e._cardZones.GetLocation(x.CardId)==x.From).Select(x=>x.CardId).ToArray();
            if(live.Length>0)e.MoveProgramCardsFromMultipleSources(live,CardLocation.Hand(f.OwnerSeat),new(MouSsxObtainReason),
                (_,moves)=>{foreach(var move in moves)e.SealMouSsxMove(f.Id,move,true);},disclosure:CardMovementDisclosure.FaceUp);
        }
        public void Discard(ProgramSkillFrame f,MouSsxEntity entity)
        {
            if(!new MouSunShangxiangFacts(e).Field(f.OwnerSeat,false).Contains(entity))throw new InvalidOperationException("Xiaoji lost its current legal field entity.");
            e.MoveProgramLogicalDiscard(f,entity.From.OwnerSeat!.Value,[entity.CardId],new(MouSsxDiscardReason),(_,moves)=>{foreach(var m in moves)e.SealMouSsxMove(f.Id,m,true);});
        }
        public void Armor(ProgramSkillFrame f,int seat)=>e.GrantMouSsxArmor(f,seat);
        public void Recover(ProgramSkillFrame f,int seat)=>new ProgramSkillHost(e).Recover(f.Id,f.OwnerSeat,seat,1,null,null);
        public void Faction(ProgramSkillFrame f)=>e.ChangeMouSsxFailureFaction(f);
        public void MaximumHp(ProgramSkillFrame f)=>e.ChangeProgramMaximumHp(f,-1);
        public bool Drain(ProgramSkillFrame f)=>e.TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.Program)||e.TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||
            e.TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.Program)||e.TryBeginCardsMovedProgramWindow(f.Id)||e.TryBeginAdvancedSkillsChanged(f.Id);
        public void Finish(ProgramSkillFrame f)=>e.FinishProgramSkill(f,true);
        public void Publish()=>e.AdvanceRulesAndPublishState();
    }
    private bool MouSsxParentMatches(ProgramSkillFrame f)
    {
        if(_contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash!=f.GameplayHash)return false;
        if(f.SkillId==MouSunShangxiangRuntime.Liangzhu)return f.TriggerId is null&&f.ActivationId=="deposit-zhuang"&&f.WindowContext is null&&
            f.OwnerSeat==_currentSeat&&_phase==TurnPhase.Play&&f.SelectedCardIds.Count==0&&f.SelectedTargetSeats.Count==0;
        if(f.TriggerId is null||f.ActivationId!=f.TriggerId||f.WindowContext is not{}context||context.OwnerSeat!=f.OwnerSeat)return false;
        var at=_resolutionStack.FindIndex(x=>x.Id==f.Id);if(at<1||_resolutionStack[at-1].Id!=context.ParentFrameId)return false;
        ProgramTriggerCandidate? candidate=_resolutionStack[at-1] switch {
            ProgramLifecycleTriggerWindowFrame p when p.Window==context.Window&&p.CandidateIndex>=0&&p.CandidateIndex<p.Candidates.Count=>p.Candidates[p.CandidateIndex],
            PlayPhaseStartingBoundaryFrame p when p.OwnerSeat==f.OwnerSeat&&p.ItemIndex>=0&&p.ItemIndex<p.Items.Count=>p.Items[p.ItemIndex].Candidate,
            ProgramKillTriggerWindowFrame p when context.Window==SkillProgramTriggerWindow.CharacterDied&&p.CandidateIndex>=0&&p.CandidateIndex<p.Candidates.Count&&p.VictimSeat==context.TargetSeat&&p.KillerSeat==context.SourceSeat=>p.Candidates[p.CandidateIndex],
            CardsMovedTriggerWindowFrame p when context.Window==SkillProgramTriggerWindow.CardsMoved&&p.CandidateIndex>=0&&p.CandidateIndex<p.Candidates.Count=>p.Candidates[p.CandidateIndex],
            _=>null};
        return candidate is{}c&&MountObserverCandidateMatches(f,c);
    }
    private void ChangeMouSsxZhu(ProgramSkillFrame f,int previous,int current,bool initial)
    {
        if(!MouSunShangxiang.Valid(f)||f.SkillId!=MouSunShangxiangRuntime.Jieyin||f.MouSsx is not{}r||previous!=r.HolderSeat||current==previous||current>=0&&(!IsValidPlayerSeat(current)||!_players[current].IsAlive||current==f.OwnerSeat))
            throw new InvalidOperationException("Jieyin must change its exact source-owned current holder.");
        void Change(int seat,int delta)
        {
            var p=_players[seat];var key=(PlayerMarkerKind.MouSunShangxiangZhu,f.OwnerSeat);var old=p.MarkerSourceCounts.GetValueOrDefault(key);var next=old+delta;
            if(next<0||next>1)throw new InvalidOperationException("Jieyin's one source cannot duplicate or repay its named Zhu.");
            if(next==0)p.MarkerSourceCounts.Remove(key);else p.MarkerSourceCounts[key]=next;
            var total=p.Markers.GetValueOrDefault(PlayerMarkerKind.MouSunShangxiangZhu)+delta;
            if(total==0)p.Markers.Remove(PlayerMarkerKind.MouSunShangxiangZhu);else p.Markers[PlayerMarkerKind.MouSunShangxiangZhu]=total;
            AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(f.Id,seat,PlayerMarkerKind.MouSunShangxiangZhu,delta,total,f.OwnerSeat,"skill-program.mou-mobile:jieyin.zhu"));
        }
        if(previous>=0&&_players[previous].MarkerSourceCounts.GetValueOrDefault((PlayerMarkerKind.MouSunShangxiangZhu,f.OwnerSeat))>0)Change(previous,-1);
        if(current>=0)Change(current,1);
        AdvanceEventRulesAndQueueFact(new MouSsxZhuChangedEvent(f.Id,f.OwnerSeat,previous,current,initial));
    }
    private void SealMouSsxMove(long id,CardMovementRecord move,bool isPublic)
    {
        var f=_resolutionStack.OfType<ProgramSkillFrame>().Single(x=>x.Id==id);var r=f.MouSsx!;
        if(!MouSunShangxiang.Valid(f)||move.Sequence<=r.Before||!_cardMovements.Contains(move))throw new InvalidOperationException("Sun Shangxiang lost her real movement receipt.");
        ReplaceRuntimeFrame(f.Id,f with{MouSsx=r with{After=move.Sequence}});
        if(isPublic)AdvanceEventRulesAndQueueFact(new MouSsxMovedEvent(id,move.CardId,move.From,move.To,move.Sequence));
    }
    private void GrantMouSsxArmor(ProgramSkillFrame f,int seat)
    {
        if(f.MouSsx is not{Stage:MouSsxStage.GiftChildren,Paid:true}r||!MouSunShangxiang.Valid(f)||r.HolderSeat!=seat||
            ProgramEventHistory<ArmorGrantedEvent>().Any(e=>e.ProgramFrameId==f.Id))throw new InvalidOperationException("Jieyin cannot grant armor twice or to a different giver.");
        if(IsWushuangMode||!_players[seat].IsAlive||r.Selected.Any(id=>!_cardMovements.Any(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&m.CardId==id&&m.From==CardLocation.Hand(seat)&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==MouSsxGiveReason)))return;
        var before=_players[seat].Armor;_players[seat].Armor=checked(before+1);
        AdvanceEventRulesAndQueueFact(new ArmorGrantedEvent(f.Id,f.InstructionIndex-1,CreateProgramTurnEffectSource(f),seat,1,before,before+1));
    }
    private bool IsIssuedMouSsxZhuang(PublicPersistentPileSource source)=>source.SkillId==MouSunShangxiangRuntime.Liangzhu&&_resolutionStack.OfType<ProgramSkillFrame>().Any(f=>
        f.OwnerSeat==source.OwnerSeat&&f.MouSsx is{Paid:true,Stage:MouSsxStage.FailureRecoverChildren or MouSsxStage.FailureObtainChildren}&&MouSunShangxiang.Valid(f));
    private void ChangeMouSsxFailureFaction(ProgramSkillFrame f)
    {
        if(!TracksNativeEffectiveFactionChanges||_resolutionStack.LastOrDefault()?.Id!=f.Id||!MouSunShangxiang.Valid(f)||
            f.SkillId!=MouSunShangxiangRuntime.Jieyin||f.MouSsx is not{Stage:MouSsxStage.FailureFaction,Paid:true}||!_players[f.OwnerSeat].IsAlive||
            !ProgramEventHistory<MouSsxMissionFailedEvent>().Any(e=>e.FrameId==f.Id&&e.OwnerSeat==f.OwnerSeat))throw new InvalidOperationException("Jieyin faction change requires its actual once-paid failed mission parent.");
        SynchronizeNativeEffectiveFactions();var p=_players[f.OwnerSeat];var previous=GetEffectiveFactionId(p);
        p.NativeEffectiveFactionOverrideId="wu";p.NativeEffectiveFactionWasDeclared=true;_nativeFactionObservations[p.Seat]=new("wu",CaptureNativeFactionAvatar(p));
        if(previous=="wu")return;
        _nativeFactionRevision++;AdvanceEventRulesAndQueueFact(new NativeEffectiveFactionChangedEvent(++_resolutionSequence,_turnNumber,_turnProgression.OwnerSeat,p.Seat,previous,"wu",f.Id,MouSunShangxiangRuntime.Source(f),"jieyin-failure"));
    }
}
