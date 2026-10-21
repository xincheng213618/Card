namespace CardGame.Core;
public sealed partial class GameEngine
{
    private MouZhuRanRuntime MouZhuRan=>GetRuntimeModule<MouZhuRanRuntime>();
    [ProgramCapability]
    private sealed class MouZhuRanFacts(GameEngine e):IMouZhuRanFacts
    {
        public int Turn=>e._turnNumber;
        public bool Finished=>e._winner!=Winner.None;
        public ActualDiscardRecoveryPhaseKey? Phase=>e.CurrentActualDiscardRecoveryPhase();
        public IReadOnlyList<int> Seats=>Array.AsReadOnly(e._players.Select(p=>p.Seat).ToArray());
        public IReadOnlyList<int> ActionOrder(int from,IEnumerable<int> seats)=>Array.AsReadOnly(seats.Distinct().OrderBy(s=>e.RelativeTableOrder(from,s)).ToArray());
        public MouZhuRanPlayer Player(int seat)=>new(seat,e._players[seat].IsAlive,e._players[seat].IsHuman,e.IsRuleWounded(e._players[seat]));
        public IEnumerable<T> History<T>()where T:IGameEvent=>e.ProgramEventHistory<T>();
        public SkillProgram? Program(string skill)=>e._contentRegistry.Skills.GetValueOrDefault(skill)?.Program;
        public long MovementSequence=>e._movementSequence;
        public string CardLabel(int id)=>e.GetAdvancedCard(id).DisplayName;
        public int KeepValue(int seat,int id)=>GetKeepValue(e.GetAdvancedCard(id),e._players[seat]);
        public bool Friendly(int owner,int target)=>e._aiBrains[owner].ScoreProgramTarget(e.CreateSnapshot(owner),target,new SkillProgramAiHint(0,0,0,1,0,0,false,false))>0;
        public IReadOnlyList<MouZhuRanCard> Discardable(int seat)=>Array.AsReadOnly(new[]{CardZoneKind.Hand,CardZoneKind.Equipment}.SelectMany(z=>
            e._cardZones.CardsAt(new(z,seat)).Where(c=>!e.IsForeignEquipmentDiscardPrevented(seat,c,new(z,seat),OwnedCardMoveIntent.Discard))
                .Select(c=>new MouZhuRanCard(c.Id,new(z,seat),e.GetProgramEffectiveSuit(e._players[seat],c)))).ToArray());
        public bool ExactStart(ProgramSkillFrame f,SkillProgramEffectOp operation)
        {
            if(f.InstructionIndex<1||Program(f.SkillId)?.GameplayHash!=f.GameplayHash||e.GetPausedPrivateOfferEffect(f)?.Op!=operation||
                !e.HasRuntimeSkillInstance(e._players[f.OwnerSeat],f.SkillId,f.SkillInstanceId))return false;
            if(operation==SkillProgramEffectOp.ResolveMouZhuRanZhenwei)return f.SkillId==MouZhuRanRuntime.Zhenwei&&f.ActivationId=="sealed-discard"&&
                f.TriggerId is null&&f.WindowContext is null&&f.SelectedCardIds.Count==0&&f.SelectedTargetSeats is[var target]&&target!=f.OwnerSeat&&
                e.IsValidPlayerSeat(target)&&e._players[target].IsAlive&&Phase is{Kind:ActualDiscardRecoveryPhaseKind.Play}p&&p.ActorSeat==f.OwnerSeat&&
                e.IsRecordedActualDiscardRecoveryPhase(p)&&!History<MouZhuRanStartedEvent>().Any(x=>x.Source.OwnerSeat==f.OwnerSeat&&x.Phase==p&&x.Operation==operation);
            return operation==SkillProgramEffectOp.ResolveMouZhuRanHeyuan&&f.SkillId==MouZhuRanRuntime.Heyuan&&f.TriggerId=="repeat-last"&&
                f.ActivationId==f.TriggerId&&f.SelectedCardIds.Count==0&&f.SelectedTargetSeats.Count==0&&NativeParent(f);
        }
        public bool NativeParent(ProgramSkillFrame f)
        {
            if(f.WindowContext is not{Window:SkillProgramTriggerWindow.TurnEnding}w||w.OwnerSeat!=f.OwnerSeat||w.SourceSeat!=f.OwnerSeat||w.TargetSeat!=f.OwnerSeat||
                e._currentSeat!=f.OwnerSeat)return false;
            var n=e._resolutionStack.FindIndex(p=>p.Id==f.Id);
            return n>0&&e._resolutionStack[n-1]is TurnEndingBoundaryFrame parent&&parent.Id==w.ParentFrameId&&parent.OwnerSeat==f.OwnerSeat&&parent.TurnNumber==e._turnNumber&&
                parent.ItemIndex>=0&&parent.ItemIndex<parent.Items.Count&&parent.Items[parent.ItemIndex].Candidate is{}c&&MountObserverCandidateMatches(f,c);
        }
    }
    [ProgramCapability]
    private sealed class MouZhuRanNative(GameEngine e):IMouZhuRanNative
    {
        public void PublishState()=>e.AdvanceRulesAndPublishState();
        public void Finish(ProgramSkillFrame f)=>e.FinishProgramSkill(f,true);
        public bool Drain(ProgramSkillFrame f)=>e.TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||
            e.TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||e.TryBeginAdvancedSkillsChanged(f.Id)||
            e.TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||e.TryBeginCardsMovedProgramWindow(f.Id);
        public void Damage(ProgramSkillFrame f,int target)=>e.BeginProgramSkillDamage(f,target,1);
        public void Discard(ProgramSkillFrame f,int payer,IReadOnlyList<MouZhuRanCard> cards)
        {
            var reason=f.MouZhuRan!.Operation==SkillProgramEffectOp.ResolveMouZhuRanZhenwei?MouZhuRanRuntime.CostReason:MouZhuRanRuntime.HeyuanCostReason;
            if(!e.MouZhuRan.Valid(f)||f.MouZhuRan is not{Stage:MouZhuRanStage.CostChildren}r||r.Payer!=payer||!r.Paying.SequenceEqual(cards)||
                cards.Count==0||cards.Any(c=>!new MouZhuRanFacts(e).Discardable(payer).Any(l=>l.CardId==c.CardId&&l.From==c.From)))
                throw new InvalidOperationException("Mou Zhu Ran's native discard must pay precisely this participant's sealed real HE materials.");
            e.MoveProgramLogicalDiscard(f,payer,cards.Select(c=>c.CardId).ToArray(),new(reason),(_,records)=>
            {
                var parent=e._resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(x=>x.Id==f.Id);
                if(parent?.MouZhuRan is not{Stage:MouZhuRanStage.CostChildren}paid||!e.MouZhuRan.Valid(parent)||paid.Payer!=payer||!paid.Paying.SequenceEqual(cards)||
                    records.Count!=cards.Count||records.Any(m=>!e._cardMovements.Contains(m)||m.Sequence<=paid.Before||m.Reason.Value!=reason||
                        !cards.Any(c=>c.CardId==m.CardId&&c.From==m.From)||m.To!=CardLocation.DiscardPile&&m.To!=CardLocation.OutsideGame))
                    throw new InvalidOperationException("Mou Zhu Ran's discard callback must seal its exact ancestor and native primary movement records.");
                e.ReplaceRuntimeFrame(f.Id,parent=parent with{MouZhuRan=paid with{After=records.Max(m=>m.Sequence)}});
                foreach(var m in records)
                {
                    var material=cards.Single(c=>c.CardId==m.CardId);
                    e.AdvanceEventRulesAndQueueFact(new MouZhuRanCostPaidEvent(f.Id,payer,m.CardId,m.From,material.Suit,m.Sequence));
                }
            });
        }
    }
}
