namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string BaoXinStore="skill-program.bao-xin.mutao.store",BaoXinGift="skill-program.bao-xin.mutao.gift",BaoXinCleanup="skill-program.bao-xin.mutao.cleanup",BaoXinObtain="skill-program.bao-xin.yimou.obtain",BaoXinYimouGift="skill-program.bao-xin.yimou.gift",BaoXinDraw="skill-program.bao-xin.yimou.draw";
    [ProgramCapability]private sealed class BaoXinCapability(GameEngine e):IBaoXinHost {public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginBaoXin(f,op);}
    private SkillProgramStepOutcome BeginBaoXin(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);if(f.BaoXin is not null||f.InstructionIndex!=1||f.SelectedCardIds.Count!=0||!_players[f.OwnerSeat].IsAlive)throw new InvalidOperationException("Bao Xin requires an exact native initial cursor.");
        var holder=op==SkillProgramEffectOp.ResolveBaoXinMutao?f.SelectedTargetSeats.Single():f.WindowContext!.TargetSeat!.Value;
        var r=new BaoXinReceipt(BaoXinSource(f),f.GameplayHash,op,_turnNumber,_turnProgression.OwnerSeat,holder,op==SkillProgramEffectOp.ResolveBaoXinMutao?BaoXinStage.StoreChildren:BaoXinStage.Mode){Cursor=holder,Recipient=holder};
        if(op==SkillProgramEffectOp.ResolveBaoXinMutao)
        {
            if(f.TriggerId is not null||f.SkillId!="ol:mutao"||GetProgramBindingId(f)!="public-slash-ring"||_phase!=TurnPhase.Play||_currentSeat!=f.OwnerSeat||!_players[holder].IsAlive)throw new InvalidOperationException("Mutao requires the actual one-role Play activation.");
            var materials=GetHand(_players[holder]).Where(c=>IsSlashCard(AdvancedEffectiveHandKind(_players[holder],c))).Select(c=>new BaoXinEntity(c.Id,c.Kind,AdvancedEffectiveHandKind(_players[holder],c),CardLocation.Hand(holder))).ToArray();
            r=r with{Paid=true,Original=materials,Pile=materials.Length==0?null:new(holder,"ol:mutao","bao-xin:"+f.Id,0,"bao-xin.mutao."+f.Id)};
        }
        else
        {
            var context=f.WindowContext??throw new InvalidOperationException("Yimou requires its native damage context.");var c=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,context.OccurrenceIndex);
            r=r with{Qualification=BaoXinQualification(c,context)??throw new InvalidOperationException("Yimou lost its exact damage occurrence or frozen directional distance.")};
        }
        ReplaceRuntimeTop(f=f with{BaoXin=r});AdvanceEventRulesAndQueueFact(new BaoXinStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.ActualTurn,r.TurnOwner,holder));
        if(op==SkillProgramEffectOp.ResolveBaoXinMutao)
        {
            foreach(var(c,index)in r.Original.Select((c,index)=>(c,index)))AdvanceEventRulesAndQueueFact(new BaoXinOriginalEntityEvent(f.Id,index,c.CardId,c.Kind,c.EffectiveKind,c.From));
            if(r.Pile is null){FinishBaoXin(f);return SkillProgramStepOutcome.AwaitChild;}
            _cardZones.EnsurePublicPersistentPile(r.Pile.Location);_publicPersistentPiles.Add((r.Pile.OwnerSeat,r.Pile.SkillId,r.Pile.SkillInstanceId),r.Pile);MoveBaoXin(f,r.Original,BaoXinStage.StoreChildren,r.Pile.Location,BaoXinStore);ContinueBaoXin(f.Id);
        }
        else PublishBaoXin(f);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private void PrepareBaoXinMove(ref ProgramSkillFrame f,IReadOnlyList<BaoXinEntity> entities,BaoXinStage stage)
    {
        foreach(var player in _players)_observedSkillGrantRevisions.TryAdd(player.Seat,player.SkillGrants.Revision);var r=f.BaoXin!;ReplaceRuntimeTop(f=f with{BaoXin=r with{Stage=stage,Step=r.Step+1,Before=_movementSequence,After=_movementSequence,ActualCount=0,Moving=entities},PendingMovementContinuation=new(r.Recipient,0,null)});
    }
    private void CompleteBaoXinMove(long id,int count)
    {var f=GetActiveProgramFrame(id);var r=f.BaoXin!;ReplaceRuntimeTop(f=f with{BaoXin=r with{After=_movementSequence,ActualCount=count}});AdvanceEventRulesAndQueueFact(new BaoXinMovementPaidEvent(id,r.Step,r.Stage,r.Before,_movementSequence,count,r.Recipient));}
    private void MoveBaoXin(ProgramSkillFrame f,IReadOnlyList<BaoXinEntity> entities,BaoXinStage stage,CardLocation destination,string reason)
    {
        PrepareBaoXinMove(ref f,entities,stage);var step=f.BaoXin!.Step;
        if(entities.Count>0)MoveProgramCardsFromMultipleSources(entities.Select(c=>c.CardId).ToArray(), destination, new(reason), (stage is BaoXinStage.GiftChildren or BaoXinStage.YimouGiftChildren ? NativeGiveCompletionRecords(f.Id,f.BaoXin!.Holder,destination.OwnerSeat??f.OwnerSeat,entities.Select(c=>c.CardId).ToArray(),_movementSequence,(batch,records)=>
        {
            foreach(var m in records)AdvanceEventRulesAndQueueFact(new BaoXinEntityMovedEvent(f.Id,step,batch,m.CardId,m.CardKind,m.From,m.To,m.Sequence));
            if(stage==BaoXinStage.GiftChildren&&records is[var moved])
            {var current=GetActiveProgramFrame(f.Id);var r=current.BaoXin!;ReplaceRuntimeTop(current with{BaoXin=r with{Consumed=r.Consumed.Append(moved.CardId).ToArray(),Cursor=r.Recipient,LastRecipient=r.Recipient}});AdvanceEventRulesAndQueueFact(new BaoXinMutaoGiftEvent(f.Id,r.GiftOrdinal,moved.CardId,r.Recipient,moved.Sequence));}
        }) : (batch,records)=>
        {
            foreach(var m in records)AdvanceEventRulesAndQueueFact(new BaoXinEntityMovedEvent(f.Id,step,batch,m.CardId,m.CardKind,m.From,m.To,m.Sequence));
            if(stage==BaoXinStage.GiftChildren&&records is[var moved])
            {var current=GetActiveProgramFrame(f.Id);var r=current.BaoXin!;ReplaceRuntimeTop(current with{BaoXin=r with{Consumed=r.Consumed.Append(moved.CardId).ToArray(),Cursor=r.Recipient,LastRecipient=r.Recipient}});AdvanceEventRulesAndQueueFact(new BaoXinMutaoGiftEvent(f.Id,r.GiftOrdinal,moved.CardId,r.Recipient,moved.Sequence));}
        }));
        CompleteBaoXinMove(f.Id,entities.Count);
    }
    private void RecordBaoXinRandom(ProgramSkillFrame f,int ordinal,int recipient)
    {var r=f.BaoXin!;for(var i=0;i<r.Eligible.Count;i++)AdvanceEventRulesAndQueueFact(new BaoXinRandomCandidateEvent(f.Id,ordinal,i,r.Eligible[i]));AdvanceEventRulesAndQueueFact(new BaoXinRandomIssuedEvent(f.Id,ordinal,r.SelectedCard!.Value,recipient,r.RandomIndex,r.Eligible.Count,r.RandomBefore,r.RandomAfter));}
    private bool DrainBaoXin(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id)||TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueBaoXin(long id)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{BaoXin:{}r}f||f.Id!=id)return;AssertBaoXin(f);
        if(r.Stage is BaoXinStage.Mode or BaoXinStage.GiftCard or BaoXinStage.GiftTarget){if(_pendingDecision is null)PublishBaoXin(f);return;}
        if(f.AttackAttempt is not null)return;if(DrainBaoXin(f))return;f=GetActiveProgramFrame(id);r=f.BaoXin!;ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null});
        if(r.Op==SkillProgramEffectOp.ResolveBaoXinYimou)
        {
            if(r.Stage==BaoXinStage.YimouGiftChildren&&_players[r.Holder].IsAlive&&_winner==Winner.None)
            {ReplaceRuntimeTop(f=f with{BaoXin=r with{Recipient=r.Holder}});AdvanceEventRulesAndQueueFact(new BaoXinYimouDrawIssuedEvent(id,r.Holder));PrepareBaoXinMove(ref f,[],BaoXinStage.DrawChildren);DrawProgramCards(id,r.Holder,1,null,null,SkillProgramCardSetVisibility.Private,new(BaoXinDraw));CompleteBaoXinMove(id,_cardMovements.Count(m=>m.Sequence>f.BaoXin!.Before&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(r.Holder)&&m.Reason.Value==BaoXinDraw));ContinueBaoXin(id);return;}
            FinishBaoXin(f);return;
        }
        if(r.Stage is BaoXinStage.CleanupChildren or BaoXinStage.DamageChildren){FinishBaoXin(f);return;}
        var remaining=r.Original.Where(c=>!r.Consumed.Contains(c.CardId)).ToArray();foreach(var missing in remaining.Where(c=>_cardZones.GetLocation(c.CardId)!=r.Pile!.Location))
        {
            var departed=_cardMovements.LastOrDefault(m=>m.CardId==missing.CardId&&m.From==r.Pile!.Location)??throw new InvalidOperationException("Mutao cannot retire an original entity without its real departure.");
            r=r with{Consumed=r.Consumed.Append(missing.CardId).ToArray()};ReplaceRuntimeTop(f=f with{BaoXin=r});AdvanceEventRulesAndQueueFact(new BaoXinMutaoRetiredEvent(id,missing.CardId,departed.Sequence));
        }
        var eligible=r.Original.Where(c=>!r.Consumed.Contains(c.CardId)&&_cardZones.GetLocation(c.CardId)==r.Pile!.Location).OrderBy(c=>c.CardId).ToArray();
        if(_winner==Winner.None&&eligible.Length>0&&_players.Any(p=>p.IsAlive))
        {
            var recipient=FindNextAliveSeat(r.Cursor);var before=_random.State;var index=_random.Next(eligible.Length);var card=eligible[index];r=r with{Recipient=recipient,SelectedCard=card.CardId,Eligible=eligible.Select(c=>c.CardId).ToArray(),RandomBefore=before,RandomAfter=_random.State,RandomIndex=index,GiftOrdinal=r.GiftOrdinal+1};ReplaceRuntimeTop(f=f with{BaoXin=r});RecordBaoXinRandom(f,r.GiftOrdinal,recipient);MoveBaoXin(f,[card with{From=r.Pile!.Location}],BaoXinStage.GiftChildren,CardLocation.Hand(recipient),BaoXinGift);ContinueBaoXin(id);return;
        }
        if(_winner==Winner.None&&r.LastRecipient is{}last&&_players[last].IsAlive)
        {
            var count=GetHand(_players[last]).Count(c=>IsSlashCard(AdvancedEffectiveHandKind(_players[last],c)));var damage=Math.Min(2,count);
            if(damage>0){ReplaceRuntimeTop(f=f with{BaoXin=r with{Stage=BaoXinStage.DamageChildren,DamageIssued=true,Damage=damage}});AdvanceEventRulesAndQueueFact(new BaoXinMutaoDamageIssuedEvent(id,r.Holder,last,count,damage));BeginProgramSkillDamage(f,last,damage,nature:DamageNature.Normal,explicitSourceSeat:r.Holder);return;}
        }
        FinishBaoXin(f);
    }
    private void FinishBaoXin(ProgramSkillFrame f)
    {
        var r=f.BaoXin!;if(r.Pile is{}pile)
        {
            var residue=PublicPileCards(pile).ToArray();if(residue.Length>0&&r.Stage!=BaoXinStage.CleanupChildren)
            {var own=r.Original.Select(c=>c.CardId).ToHashSet();if(residue.Any(c=>!own.Contains(c.Id)))throw new InvalidOperationException("Mutao cannot clean an unrelated public-pile entity.");MoveBaoXin(f,residue.Select(c=>new BaoXinEntity(c.Id,c.Kind,c.Kind,pile.Location)).ToArray(),BaoXinStage.CleanupChildren,CardLocation.DiscardPile,BaoXinCleanup);ContinueBaoXin(f.Id);return;}
            _publicPersistentPiles.Remove((pile.OwnerSeat,pile.SkillId,pile.SkillInstanceId));
        }
        AdvanceEventRulesAndQueueFact(new BaoXinCompletedEvent(f.Id,r.Op));ReplaceRuntimeTop(f=f with{BaoXin=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume,27450)]private bool ResumeBaoXin(long id){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{BaoXin:not null}f||f.Id!=id)return false;ContinueBaoXin(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,27450)]private bool ReturnBaoXinMovement(ProgramSkillFrame f){if(f.BaoXin is null)return false;ContinueBaoXin(f.Id);return true;}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27450)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27450)]private bool ContinueIssuedBaoXin(ProgramSkillFrame f)=>f.BaoXin is{Paid:true}&&ValidBaoXin(f);
}
