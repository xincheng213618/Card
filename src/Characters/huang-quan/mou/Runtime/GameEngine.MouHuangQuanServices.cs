namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramCapability]private sealed class MouHuangQuanFacts(GameEngine e):IMouHuangQuanFacts
    {
        public bool GameOver=>e._winner!=Winner.None;public int Turn=>e._turnNumber;public int TurnOwner=>e._turnProgression.OwnerSeat;public long MovementSequence=>e._movementSequence;
        public bool Alive(int seat)=>e.IsValidPlayerSeat(seat)&&e._players[seat].IsAlive;public bool Human(int seat)=>e._players[seat].IsHuman;public string Name(int seat)=>e._players[seat].Name;
        public string CardName(int id)=>e.GetAdvancedCard(id).DisplayName;public IReadOnlyList<int> Living=>Array.AsReadOnly(e._players.Where(p=>p.IsAlive).Select(p=>p.Seat).ToArray());
        public ContentSkillDefinition Skill(string id)=>e._contentRegistry.GetSkill(id);public IEnumerable<T> History<T>()where T:IGameEvent=>e.ProgramEventHistory<T>();
        public bool ParentMatches(ProgramSkillFrame f)=>e.MouHqParent(f);
        public MouHqOriginalUse? OriginalUse(int owner,ProgramSkillWindowContext? context)=>e.TryMouHqOriginalUse(owner,context,out var u,out _)?new(u.Id,u.Action!.ActionId,u.SourceSeat==owner):null;
        public IReadOnlyList<MouHqGift> Gifts(int giver)=>e._players[giver].MouHqActualGifts??[];
        public IReadOnlyList<MouHqEntity> TransferEntities(ProgramSkillFrame f,int actor)=>Array.AsReadOnly(e.BuildOwnedCardPaymentChoices(f.Id,actor,actor,[CardZoneKind.Hand,CardZoneKind.Equipment],OwnedCardMoveIntent.Transfer,
            destination:SkillProgramCardDestination.OwnerHand).Select(c=>new MouHqEntity(c.Cards.Single(),e._cardZones.GetLocation(c.Cards.Single()))).ToArray());
        public double HelpValue(int actor,int target)=>e._aiBrains[actor].ScoreProgramTarget(e.CreateSnapshot(actor),target,new(0,0,0,0,1,0,true,false));
        public int KeepValue(int actor,int card)=>GetKeepValue(e.GetAdvancedCard(card),e._players[actor]);
        public bool MovementRecoveryChild(ProgramSkillFrame f,ResolutionFrame child)
        {
            if(f.MouHuangQuan is not{Paid:true,Stage:MouHqStage.GiveChildren or MouHqStage.ReplyChildren}r)return false;
            foreach(var entity in r.Selected.Where(x=>x.From.Zone==CardZoneKind.Equipment&&e.GetAdvancedCard(x.CardId).Kind==CardKind.SilverLion))
            {
                var seat=entity.From.OwnerSeat!.Value;var reason=r.Stage==MouHqStage.GiveChildren?MouHuangQuanRuntime.MouHqGiveReason:MouHuangQuanRuntime.MouHqReplyReason;
                if(!e._cardMovements.Any(m=>m.CardId==entity.CardId&&m.From==entity.From&&m.Sequence>r.Before&&m.Sequence<=r.After&&m.Reason.Value==reason))continue;
                if(child is RecoveryReplacementFrame recovery&&e.RecoveryReplacementFrameRidesOn(recovery,f)&&recovery.Return.Continuation==PostEventContinuation.Program&&
                    recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion&&recovery.Attempt.Completion.MoveReason?.Value==reason&&recovery.Attempt.SourceSeat==seat&&recovery.Attempt.TargetSeat==seat)return true;
                if(child is HpChangedTriggerWindowFrame hp&&hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&hp.Continuation==PostEventContinuation.Program&&hp.Change.Kind==HpChangeKind.Recovery&&hp.Change.SourceSeat==seat&&hp.Change.TargetSeat==seat&&
                    e.ProgramEventHistory<SilverLionRemovedRecoveryEvent>().Any(x=>x.PlayerSeat==seat&&x.Reason.Value==reason&&x.RecoveredAmount==hp.Change.Amount&&x.RemainingHp==hp.Change.HpAfter))return true;
            }
            return false;
        }
    }
    [ProgramCapability]private sealed class MouHuangQuanNative(GameEngine e):IMouHuangQuanNative
    {
        public void Give(ProgramSkillFrame f,int giver,int recipient,IReadOnlyList<int> ids,bool reply,long before)
        {
            if(ids.Count==0)return;
            if(!e.ValidMouHq(f)||f.MouHuangQuan is not{Paid:true}r||r.Selected.Count!=ids.Count||r.Selected.Any(x=>!ids.Contains(x.CardId)||e._cardZones.GetLocation(x.CardId)!=x.From)||
                (reply?r.Recipient:f.OwnerSeat)!=giver||(reply?f.OwnerSeat:r.Recipient)!=recipient||before!=r.Before)throw new InvalidOperationException("Qiaodui/Tuicheng native payment lost its exact issued sources and payer.");
            e.MoveProgramCardsFromMultipleSources(ids,CardLocation.Hand(recipient),new(reply?MouHuangQuanRuntime.MouHqReplyReason:MouHuangQuanRuntime.MouHqGiveReason),(_,moves)=>{
                var current=e.GetActiveProgramFrame(f.Id);e.ReplaceRuntimeTop(current with{MouHuangQuan=current.MouHuangQuan! with{After=e._movementSequence}});e.RecordNativeGivenCards(f.Id,giver,recipient,ids,before);
            },disclosure:CardMovementDisclosure.FaceDown);
        }
        public bool Drain(ProgramSkillFrame f)=>e.TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.Program)||e.TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||e.TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.Program)||e.TryBeginCardsMovedProgramWindow(f.Id)||e.TryBeginAdvancedSkillsChanged(f.Id);
        public void OriginalEffect(ProgramSkillFrame f)=>e.IssueMouHqOriginalEffect(f);public void Finish(ProgramSkillFrame f,bool accepted)=>e.FinishProgramSkill(f,accepted);public void Publish()=>e.AdvanceRulesAndPublishState();
    }
}
