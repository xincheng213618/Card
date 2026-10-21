using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private MouQiaobianRuntime MouQiaobian=>GetRuntimeModule<MouQiaobianRuntime>();
    [ProgramCapability]
    private sealed class MouZhangHeFacts(GameEngine e):IMouZhangHeFacts
    {
        public int Turn=>e._turnNumber;
        public int TurnOwner=>e._turnProgression.OwnerSeat;
        public bool Finished=>e._winner!=Winner.None;
        public IReadOnlyList<int> Seats=>Array.AsReadOnly(e._players.Select(p=>p.Seat).ToArray());
        public MouZhangHePlayer Player(int s)=>new(s,e._players[s].IsAlive,e._players[s].IsHuman,e.GetHand(e._players[s]).Count,
            e.GetEquipment(e._players[s]).Count+e.GetJudgment(e._players[s]).Count,e.GetHand(e._players[s]).Count+e.GetEquipment(e._players[s]).Count+e.GetJudgment(e._players[s]).Count);
        public SkillProgram? Program(string id)=>e._contentRegistry.Skills.GetValueOrDefault(id)?.Program;
        public IEnumerable<T> History<T>()where T:IGameEvent=>e.ProgramEventHistory<T>();
        public IReadOnlyList<MouZhangHeMovement> Movements=>Array.AsReadOnly(e._cardMovements.Select(m=>new MouZhangHeMovement(m.Sequence,m.CardId,m.From,m.To,m.Reason.Value)).ToArray());
        public CardLocation Location(int id)=>e._cardZones.GetLocation(id);
        public long MovementSequence=>e._movementSequence;
        public ProgramPhaseSchedule? Schedule=>e._programPhaseSchedule;
        public bool ExactStart(ProgramSkillFrame f,SkillProgramEffectOp op)=>f.InstructionIndex==1&&f.ActivationId==f.TriggerId&&f.SkillId==MouQiaobianRuntime.Qiaobian&&
            Program(f.SkillId)?.GameplayHash==f.GameplayHash&&e.GetPausedPrivateOfferEffect(f)?.Op==op&&NativeParent(f);
        public bool NativeParent(ProgramSkillFrame f)
        {
            if(f.WindowContext is not{}x||x.OwnerSeat!=f.OwnerSeat||x.SourceSeat!=f.OwnerSeat||x.TargetSeat!=f.OwnerSeat||f.OwnerSeat!=e._currentSeat)return false;
            var n=e._resolutionStack.FindIndex(p=>p.Id==f.Id);
            if(n<1||e._resolutionStack[n-1].Id!=x.ParentFrameId)return false;
            var parent=e._resolutionStack[n-1];
            if(parent is ProgramLifecycleTriggerWindowFrame w)
                return w.Window==x.Window&&w.OwnerSeat==f.OwnerSeat&&w.CandidateIndex>=0&&w.CandidateIndex<w.Candidates.Count&&
                    MountObserverCandidateMatches(f,w.Candidates[w.CandidateIndex])&&
                    (x.Window==SkillProgramTriggerWindow.TurnStartBeforeNormalFlow&&w.Continuation==ProgramLifecycleContinuation.NormalTurnStart||
                     x.Window==SkillProgramTriggerWindow.ActualPhaseSkipped&&w.Continuation==ProgramLifecycleContinuation.CompleteActualSkippedPhase&&w.MouZhangHeSkip is{}s&&
                        s.Entitlement.Source.OwnerSeat==f.OwnerSeat&&s.Entitlement.Source.SkillInstanceId==f.SkillInstanceId&&s.Entitlement.GameplayHash==f.GameplayHash&&s.NativeReturn.ActualTurn==e._turnNumber);
            return parent is TurnEndingBoundaryFrame t&&x.Window==SkillProgramTriggerWindow.TurnEnding&&t.OwnerSeat==f.OwnerSeat&&t.TurnNumber==e._turnNumber&&
                t.ItemIndex>=0&&t.ItemIndex<t.Items.Count&&t.Items[t.ItemIndex].Candidate is{}c&&MountObserverCandidateMatches(f,c);
        }
        public IReadOnlyList<MouZhangHeCard> Discardable(int owner)=>Array.AsReadOnly(new[]{CardZoneKind.Hand,CardZoneKind.Equipment,CardZoneKind.Judgment}
            .SelectMany(z=>e._cardZones.CardsAt(new(z,owner)).Where(c=>!e.IsForeignEquipmentDiscardPrevented(owner,c,new(z,owner),OwnedCardMoveIntent.Discard))
                .Select(c=>new MouZhangHeCard(c.Id,new(z,owner)))).ToArray());
        public IReadOnlyList<MouZhangHeCard> FieldCards=>Array.AsReadOnly(e._players.Where(p=>p.IsAlive).SelectMany(p=>new[]{CardZoneKind.Equipment,CardZoneKind.Judgment}
            .SelectMany(z=>e._cardZones.CardsAt(new(z,p.Seat)).Select(c=>new MouZhangHeCard(c.Id,new(z,p.Seat))))).ToArray());
        public bool CanMoveField(int id,int target)
        {
            var from=Location(id);var card=e.GetAdvancedCard(id);
            return from.OwnerSeat is{}source&&source!=target&&e._players[source].IsAlive&&from.Zone is CardZoneKind.Equipment or CardZoneKind.Judgment&&
                e.CanMoveProgramCardToCorrespondingZone(card,from.Zone,target)&&
                (from.Zone!=CardZoneKind.Equipment||e._players[target].EquipmentSlotCapacity(EquipmentCatalog.Get(card.Kind).Slot)>0)&&
                !e.IsForeignEquipmentDiscardPrevented(e._currentSeat,card,from,OwnedCardMoveIntent.Transfer);
        }
        public IReadOnlyList<PromptChoice> HandChoices(ProgramSkillFrame f,int target)=>e.BuildOwnedCardPaymentChoices(f.Id,f.OwnerSeat,target,[CardZoneKind.Hand],OwnedCardMoveIntent.Obtain,
            destination:SkillProgramCardDestination.OwnerHand,destinationSeat:f.OwnerSeat);
        public int ResolveHandChoice(PromptChoice c,int subject)
        {
            if(c.Cards is[var id])return id;
            if(c.Parameters.GetValueOrDefault("card-owner-seat")!=subject.ToString(CultureInfo.InvariantCulture)||c.Parameters.GetValueOrDefault("source-zone")!="Hand"||
                !int.TryParse(c.Parameters.GetValueOrDefault("slot-index"),out var slot)||slot<0||slot>=e.GetHand(e._players[subject]).Count)
                throw new InvalidOperationException("Qiaobian's opaque Hand slot is stale.");
            return e.GetHand(e._players[subject])[slot].Id;
        }
        public int KeepValue(int s,int id)=>GetKeepValue(e.GetAdvancedCard(id),e._players[s]);
        public bool Friendly(int owner,int target)=>e._aiBrains[owner].ScoreProgramTarget(e.CreateSnapshot(owner),target,new SkillProgramAiHint(0,0,0,1,0,0,false,false))>0;
        public bool FirstNativeChild(ProgramSkillFrame f,ResolutionFrame child)=>e.MouQiaobianFirstNativeChild(f,child);
    }
    [ProgramCapability]
    private sealed class MouZhangHeNative(GameEngine e):IMouZhangHeNative
    {
        public void PublishState()=>e.AdvanceRulesAndPublishState();
        public void Finish(ProgramSkillFrame f)=>e.FinishProgramSkill(f,true);
        public bool Drain(ProgramSkillFrame f)=>e.TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||
            e.TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||e.TryBeginAdvancedSkillsChanged(f.Id)||
            e.TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||e.TryBeginCardsMovedProgramWindow(f.Id);
        public bool AdvanceObserverChildren()=>e.TryAdvanceObserverSubtree(true);
        private void Seal(ProgramSkillFrame f,CardMovementRecord m)
        {
            var parent=e._resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(x=>x.Id==f.Id);
            if(parent?.MouZhangHe is not{}r||!e.MouQiaobian.Valid(parent)||m.Sequence<=r.Before||!e._cardMovements.Contains(m))
                throw new InvalidOperationException("Qiaobian's native movement lost its owning ancestor and exact payment interval.");
            e.ReplaceRuntimeFrame(f.Id,parent=parent with{MouZhangHe=r=r with{After=m.Sequence,
                PaidCount=r.PaidCount+(r.Stage==MouZhangHeStage.CostChildren&&m.CardId==r.CardId&&m.From==r.From&&m.Reason.Value==MouQiaobianRuntime.CostReason?1:0)}});
            if(r.Stage==MouZhangHeStage.CostChildren&&m.CardId==r.CardId&&m.From==r.From&&m.Reason.Value==MouQiaobianRuntime.CostReason)
                e.AdvanceEventRulesAndQueueFact(new MouZhangHeCostPaidEvent(f.Id,m.CardId,m.From,m.Sequence));
            if(r.Stage==MouZhangHeStage.ObtainChildren&&m.CardId==r.CardId&&m.From==r.From&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==MouQiaobianRuntime.ObtainReason)
                e.AdvanceEventRulesAndQueueFact(new MouZhangHeObtainedEvent(f.Id,f.OwnerSeat,m.Sequence));
            if(r.Stage==MouZhangHeStage.FieldChildren&&m.CardId==r.CardId&&m.From==r.From&&m.To.OwnerSeat==r.TargetSeat&&m.Reason.Value==MouQiaobianRuntime.FieldReason)
                e.AdvanceEventRulesAndQueueFact(new MouZhangHeFieldMovedEvent(f.Id,m.CardId,m.From.OwnerSeat!.Value,r.TargetSeat,m.Sequence));
        }
        public void Discard(ProgramSkillFrame f,MouZhangHeCard card)=>e.MoveLogicalDiscard(f.OwnerSeat,f.OwnerSeat,e.GetAdvancedCard(card.CardId),card.From,
            new(MouQiaobianRuntime.CostReason),m=>Seal(f,m),f.Id);
        public void Obtain(ProgramSkillFrame f,int target,int id)=>e.MoveCard(e.GetAdvancedCard(id),CardLocation.Hand(target),CardLocation.Hand(f.OwnerSeat),
            new(MouQiaobianRuntime.ObtainReason),m=>Seal(f,m),disclosure:CardMovementDisclosure.FaceDown);
        public void MoveField(ProgramSkillFrame f,int id,int target)
        {
            var card=e.GetAdvancedCard(id);var from=e._cardZones.GetLocation(id);
            if(f.MouZhangHe?.From!=from||!new MouZhangHeFacts(e).CanMoveField(id,target))throw new InvalidOperationException("Qiaobian's corresponding-zone move is no longer native legal.");
            var destination=e.ProgramCorrespondingZoneLocation(card,from.Zone,target);
            if(destination.Zone==CardZoneKind.Equipment)
            {
                var slot=EquipmentCatalog.Get(card.Kind).Slot;
                var occupants=e.GetEquipment(e._players[target]).Where(c=>EquipmentCatalog.Get(c.Kind).Slot==slot).ToArray();
                if(occupants.Length>=e._players[target].EquipmentSlotCapacity(slot))
                    e.MoveCard(occupants[0],CardLocation.Equipment(target),CardLocation.DiscardPile,CardMoveReasons.EquipmentReplace,m=>Seal(f,m),disclosure:CardMovementDisclosure.FaceUp);
            }
            e.MoveCard(card,from,destination,new(MouQiaobianRuntime.FieldReason),m=>Seal(f,m),disclosure:CardMovementDisclosure.FaceUp);
        }
        public void ApplySkips(ProgramSkillFrame f,IReadOnlyList<SkillProgramTurnPhase> phases)
        {
            if(!e.MouQiaobian.Valid(f)||f.MouZhangHe is not{Operation:SkillProgramEffectOp.PrepareMouZhangHeQiaobian,Accepted:true}r||
                r.PaidCount!=phases.Count||!phases.SequenceEqual(r.Phases.Take(r.PaidCount))||
                e.ProgramEventHistory<MouZhangHeCostPaidEvent>().Count(x=>x.FrameId==f.Id)!=r.PaidCount)
                throw new InvalidOperationException("Only Qiaobian's actually paid distinct-area costs may issue phase skips.");
            foreach(var phase in phases)e._pendingTurnDelayedEffects|=phase switch
            {
                SkillProgramTurnPhase.Judgment=>DelayedTurnEffects.SkipJudgmentPhase,
                SkillProgramTurnPhase.Draw=>DelayedTurnEffects.SkipDrawPhase,
                SkillProgramTurnPhase.Play=>DelayedTurnEffects.SkipPlayPhase,
                SkillProgramTurnPhase.Discard=>DelayedTurnEffects.SkipDiscardPhase,
                _=>throw new InvalidOperationException("Qiaobian cannot skip Preparation or Ending.")
            };
        }
        public void OpenSkipWindow(MouZhangHeSkipEntitlement q,ProgramActualPhaseSkipContext c)
        {
            if(e._resolutionStack.Count!=0||e._pendingDecision is not null||c.OwnerSeat!=e._currentSeat||c.ActualTurn!=e._turnNumber||
                q.ActualTurn!=c.ActualTurn||q.Source.OwnerSeat!=c.OwnerSeat||q.Phase!=c.Phase||
                !e.ProgramEventHistory<MouZhangHeSkipIssuedEvent>().Any(x=>x.Entitlement==q))
                throw new InvalidOperationException("Qiaobian can suspend only its real clean skipped-phase continuation.");
            var candidate=new ProgramTriggerCandidate(c.OwnerSeat,q.Source.SkillId,"actual-skipped-phase",q.Source.SkillInstanceId!,q.GameplayHash,0);
            var window=new ProgramLifecycleTriggerWindowFrame(++e._resolutionSequence,c.OwnerSeat,SkillProgramTriggerWindow.ActualPhaseSkipped,
                Array.AsReadOnly(new[]{candidate}),ProgramLifecycleContinuation.CompleteActualSkippedPhase,e.CaptureProgramTriggerFacts(e._players[c.OwnerSeat])){MouZhangHeSkip=new(q,c)};
            e.PushRuntimeFrame(window);
            e.AdvanceEventRulesAndQueueFact(new MouZhangHeSkipObservedEvent(window.Id,q,c));
            e.AdvanceRuntimeFrame(window.Id);
        }
        public void ResumeSkip(ProgramLifecycleTriggerWindowFrame w)
        {
            var c=w.MouZhangHeSkip!.NativeReturn;
            if(e._resolutionStack.Count!=0||e._pendingDecision is not null||w.OwnerSeat!=e._currentSeat||c.ActualTurn!=e._turnNumber)
                throw new InvalidOperationException("Qiaobian's skipped-phase reward lost its native return cursor.");
            if(c.Phase==SkillProgramTurnPhase.Discard)e.CompleteDiscardPhaseAfterProgramWindow(e._players[c.OwnerSeat]);
            else e.CompleteTurnStartAfterDraw(e._players[c.OwnerSeat],(DelayedTurnEffects)c.DelayedEffects,c.AfterNormalDrawProgramsCompleted,
                c.DrawPhaseEndedProgramsCompleted,c.ActualDrawCompletionCompleted);
        }
        public void AdvanceLifecycleCursor(ProgramLifecycleTriggerWindowFrame w)=>e.AdvanceProgramLifecycleCursor(w);
        public bool BeginExtraPlay(ProgramSkillFrame f)=>e.BeginMouQiaobianExtraPlay(f);
        public void ResumeExtraPlay(ProgramPhaseSchedule s,bool cancel)=>e.ReturnMouQiaobianExtraPlay(s,cancel);
    }
    private bool BeginMouQiaobianExtraPlay(ProgramSkillFrame f)
    {
        if(!MouQiaobian.Valid(f)||f.MouZhangHe is not{Stage:MouZhangHeStage.ExtraPlay}r||f.WindowContext?.Window!=SkillProgramTriggerWindow.TurnEnding||
            _resolutionStack.Count!=2||_resolutionStack[^1]!=f||_resolutionStack[^2]is not TurnEndingBoundaryFrame parent||parent.Id!=r.WindowId||
            parent.OwnerSeat!=_currentSeat||parent.TurnNumber!=_turnNumber||_winner!=Winner.None||!_players[f.OwnerSeat].IsAlive)return false;
        r=r with{ExtraReturn=new(parent,_phase,CurrentActualDiscardRecoveryPhase(),_programPhaseSchedule)};
        ReplaceRuntimeTop(f=f with{MouZhangHe=r});
        PopResolutionFrame(f.Id,ResolutionFrameKind.ProgramSkill);PopResolutionFrame(parent.Id,ResolutionFrameKind.TurnEndingBoundary);
        _programPhaseSchedule=new(f,null,TurnPhase.Play,SkillProgramPhaseContinuation.BeforeNormalPreparation);
        AdvanceEventRulesAndQueueFact(new ProgramPhaseScheduledEvent(f.Id,f.SkillId,f.TriggerId!,f.OwnerSeat,TurnPhase.Play,true));
        EnterPlayPhase(_players[f.OwnerSeat]);return true;
    }
    private void ReturnMouQiaobianExtraPlay(ProgramPhaseSchedule s,bool cancel)
    {
        if(s.Frame.MouZhangHe is not{Stage:MouZhangHeStage.ExtraPlay,ExtraReturn:{}r}q||s!=_programPhaseSchedule||s.Phase!=TurnPhase.Play||s.ParentFrame is not null||
            q.WindowId!=r.Parent.Id||q.ActualTurn!=_turnNumber||s.Frame.OwnerSeat!=_currentSeat||r.Parent.OwnerSeat!=_currentSeat||r.Parent.TurnNumber!=_turnNumber||
            !cancel&&(_resolutionStack.Count!=0||_pendingDecision is not null)||!ProgramEventHistory<MouZhangHeEndingConsumedEvent>().Any(x=>x.FrameId==s.Frame.Id&&x.ActualTurn==_turnNumber))
            throw new InvalidOperationException("Qiaobian's automatic extra Play lost its frozen actual Ending parent.");
        _programPhaseSchedule=r.SuspendedSchedule;
        AdvanceEventRulesAndQueueFact(new ProgramPhaseScheduledEvent(s.Frame.Id,s.Frame.SkillId,s.Frame.TriggerId!,s.Frame.OwnerSeat,TurnPhase.Play,false));
        if(cancel){CompleteDetachedProgramBinding(s.Frame,false);return;}
        ClearActualDiscardRecoveryPhase();_phase=r.CoarsePhase;
        if(r.ActualPhase is{}phase)AdvanceEventRulesAndQueueFact(new ActualDiscardRecoveryPhaseRestoredEvent(s.Frame.Id,phase));
        PushRuntimeFrame(r.Parent);PushRuntimeFrame(s.Frame with{MouZhangHe=q with{Stage=MouZhangHeStage.Complete,ExtraReturn=null}});
        AdvanceRuntimeProgram(s.Frame.Id);
    }
}
