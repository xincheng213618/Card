namespace CardGame.Core;
public sealed partial class GameEngine
{
    private MouDongZhuoOlRuntime MouDongZhuoOl=>GetRuntimeModule<MouDongZhuoOlRuntime>();
    [ProgramCapability]
    private sealed class MouDongZhuoOlFacts(GameEngine e):IMouDongZhuoOlFacts
    {
        public int Turn=>e._turnNumber; public int Round=>e._roundNumber; public int PlayerCount=>e._playerCount;
        public int PhaseId=>e._cardUseDebitPhaseInstanceId; public long MovementSequence=>e._movementSequence; public bool Finished=>e._winner!=Winner.None;
        public bool Alive(int seat)=>e.IsValidPlayerSeat(seat)&&e._players[seat].IsAlive; public bool Human(int seat)=>e._players[seat].IsHuman;
        public string Name(int seat)=>e._players[seat].Name; public int Hp(int seat)=>e._players[seat].Hp;
        public bool Owns(int seat,string skill)=>e.HasRuntimeSkill(e._players[seat],skill);
        public SkillProgram Program(string id)=>e._contentRegistry.GetSkill(id).Program!;
        public CardLocation Location(int id)=>e._cardZones.GetLocation(id);
        public IEnumerable<T> History<T>()where T:IGameEvent=>e.ProgramEventHistory<T>();
        public int KeepValue(int seat,int id)=>GetKeepValue(e.GetAdvancedCard(id),e._players[seat]);
        public bool Friendly(int owner,int target)=>e._aiBrains[owner].ScoreProgramTarget(e.CreateSnapshot(owner),target,new(0,0,0,1,0,0,false,false))>0;
        public IReadOnlyList<MouDongZhuoOlCard> Discardable(int seat)=>Array.AsReadOnly(new[]{CardZoneKind.Hand,CardZoneKind.Equipment}.SelectMany(z=>
            e._cardZones.CardsAt(new(z,seat)).Where(c=>!e.IsForeignEquipmentDiscardPrevented(seat,c,new(z,seat),OwnedCardMoveIntent.Discard))
            .Select(c=>new MouDongZhuoOlCard(c.Id,new(z,seat),e.GetProgramEffectiveSuit(e._players[seat],c)))).ToArray());
        public IReadOnlyList<MouDongZhuoOlCard> CurrentDiscard()=>Array.AsReadOnly(e._cardZones.CardsAt(CardLocation.DiscardPile)
            .Where(c=>e._cardMovements.Any(m=>m.CardId==c.Id&&m.TurnNumber==e._turnNumber&&m.To==CardLocation.DiscardPile))
            .Select(c=>new MouDongZhuoOlCard(c.Id,CardLocation.DiscardPile,c.Suit)).ToArray());
        public bool ParentMatches(ProgramSkillFrame f)=>e.MouDongZhuoOlParent(f);
        public bool ExactStart(ProgramSkillFrame f,SkillProgramEffectOp op)
        {
            if(f.InstructionIndex!=1||e.GetPausedPrivateOfferEffect(f)?.Op!=op||Program(f.SkillId).GameplayHash!=f.GameplayHash||
                !e.HasRuntimeSkillInstance(e._players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0)return false;
            if(f.TriggerId is null)return op==SkillProgramEffectOp.ResolveMouDongZhuoFengshang&&f.SkillId==MouDongZhuoOlRuntime.Fengshang&&f.ActivationId=="distribute-pair"&&f.WindowContext is null&&
                e.CurrentActualDiscardRecoveryPhase()is{Kind:ActualDiscardRecoveryPhaseKind.Play}p&&p.ActorSeat==f.OwnerSeat&&e.IsRecordedActualDiscardRecoveryPhase(p)&&e.MouDongZhuoOl.CanFengshang(f.OwnerSeat,false);
            return e.MouDongZhuoOlParent(f)&&(op switch {
                SkillProgramEffectOp.InitializeMouDongZhuoGuanbian=>f.SkillId==MouDongZhuoOlRuntime.Guanbian&&f.WindowContext!.Window==SkillProgramTriggerWindow.GameStarting,
                SkillProgramEffectOp.RemoveMouDongZhuoGuanbian=>f.SkillId==MouDongZhuoOlRuntime.Guanbian&&f.WindowContext!.Window==SkillProgramTriggerWindow.RoundEnding&&e._roundNumber==1,
                SkillProgramEffectOp.ResolveMouDongZhuoXiongni=>f.SkillId==MouDongZhuoOlRuntime.Xiongni&&f.WindowContext!.Window==SkillProgramTriggerWindow.PlayPhaseStarting&&f.OwnerSeat==e._currentSeat,
                SkillProgramEffectOp.ResolveMouDongZhuoFengshang=>f.SkillId==MouDongZhuoOlRuntime.Fengshang&&f.WindowContext!.Window==SkillProgramTriggerWindow.DyingEntering&&e.MouDongZhuoOl.CanFengshang(f.OwnerSeat,true),
                _=>f.SkillId==MouDongZhuoOlRuntime.Zhibing&&f.WindowContext!.Window==SkillProgramTriggerWindow.TurnStartBeforeNormalFlow&&f.OwnerSeat==e._currentSeat });
        }
    }
    private bool MouDongZhuoOlParent(ProgramSkillFrame f)
    {
        if(f.TriggerId is null)return f.WindowContext is null&&f.ActivationId=="distribute-pair"&&f.SkillId==MouDongZhuoOlRuntime.Fengshang;
        if(f.ActivationId!=f.TriggerId||f.WindowContext is not{}w||w.OwnerSeat!=f.OwnerSeat)return false;
        var i=_resolutionStack.FindIndex(x=>x.Id==f.Id);if(i<1||_resolutionStack[i-1].Id!=w.ParentFrameId)return false;
        var c=_resolutionStack[i-1]switch{
            ProgramLifecycleTriggerWindowFrame p when p.Window==w.Window&&p.CandidateIndex>=0&&p.CandidateIndex<p.Candidates.Count=>p.Candidates[p.CandidateIndex],
            PlayPhaseStartingBoundaryFrame p when p.ItemIndex>=0&&p.ItemIndex<p.Items.Count=>p.Items[p.ItemIndex].Candidate,
            _=>null};
        return c is not null&&MountObserverCandidateMatches(f,c);
    }
    [ProgramCapability]
    private sealed class MouDongZhuoOlNative(GameEngine e):IMouDongZhuoOlNative
    {
        public void SetGuanbian(ProgramSkillFrame f,int amount)=>e.SetStrategicMarker(f.OwnerSeat,PlayerMarkerKind.MouDongZhuoGuanbian,f.OwnerSeat,amount,f);
        public void LoseGuanbian(ProgramSkillFrame f)
        {
            var owner=e._players[f.OwnerSeat];
            var enabled=owner.SkillGrants.Grants.Where(g=>g.IsEnabled&&g.SkillId==MouDongZhuoOlRuntime.Guanbian).ToArray();
            if(enabled.Length==0)return;
            e.SetStrategicMarker(f.OwnerSeat,PlayerMarkerKind.MouDongZhuoGuanbian,f.OwnerSeat,0,f);
            foreach(var grant in enabled)owner.SkillGrants.RemoveGrant(grant.GrantId);
            e.AdvanceEventRulesAndQueueFact(new MouDongZhuoOlGuanbianEvent(f.Id,f.OwnerSeat,0,true));
        }
        public bool Drain(ProgramSkillFrame f)=>e.TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||e.TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||
            e.TryBeginAdvancedSkillsChanged(f.Id)||e.TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||e.TryBeginCardsMovedProgramWindow(f.Id);
        public void Finish(ProgramSkillFrame f)=>e.FinishProgramSkill(f,true);
        public void Discard(ProgramSkillFrame f,int payer,MouDongZhuoOlCard card)
        {
            if(!e.MouDongZhuoOl.Valid(f)||f.MouDongZhuoOl is not{Paid:true}r||r.Payer!=payer||!r.Materials.SequenceEqual([card])||
                !new MouDongZhuoOlFacts(e).Discardable(payer).Contains(card))throw new InvalidOperationException("Xiongni requires its actual payable HE entity.");
            e.MoveProgramLogicalDiscard(f,payer,[card.Id],new(MouDongZhuoOlRuntime.CostReason),(_,records)=>{
                var parent=e._resolutionStack.OfType<ProgramSkillFrame>().Single(x=>x.Id==f.Id);var paid=parent.MouDongZhuoOl!;
                if(!e.MouDongZhuoOl.Valid(parent)||records is not[var m]||m.CardId!=card.Id||m.From!=card.From||m.Sequence<=paid.Before||
                    m.To!=CardLocation.DiscardPile&&m.To!=CardLocation.OutsideGame)throw new InvalidOperationException("Xiongni's real discard return changed its paid material.");
                e.ReplaceRuntimeFrame(f.Id,parent with{MouDongZhuoOl=paid with{After=m.Sequence}});
                e.AdvanceEventRulesAndQueueFact(new MouDongZhuoOlPaidEvent(f.Id,payer,card.Id,card.From,card.Suit,m.Sequence));});
        }
        public void Give(ProgramSkillFrame f,int id,int recipient)
        {
            var before=e._movementSequence;e.MoveProgramCardsFromMultipleSources([id], CardLocation.Hand(recipient), new(MouDongZhuoOlRuntime.GiftReason), e.NativeGiveCompletionRecords(f.Id,f.OwnerSeat,recipient,[id],e._movementSequence,null), disclosure:CardMovementDisclosure.FaceUp);
            var parent=e._resolutionStack.OfType<ProgramSkillFrame>().Single(x=>x.Id==f.Id);var paid=parent.MouDongZhuoOl!;
            e.ReplaceRuntimeFrame(f.Id,parent with{MouDongZhuoOl=paid with{Before=before,After=e._movementSequence}});
            foreach(var m in e._cardMovements.Where(m=>m.Sequence>before&&m.CardId==id&&m.From==CardLocation.DiscardPile&&m.To==CardLocation.Hand(recipient)))
                e.AdvanceEventRulesAndQueueFact(new MouDongZhuoOlGiftEvent(f.Id,id,recipient,m.Sequence));
        }
        public void Damage(ProgramSkillFrame f,int target)=>e.BeginProgramSkillDamage(f,target,1);
        public bool BeginWine(ProgramSkillFrame f)=>e.BeginMouDongZhuoOlWine(f);
        public void MaximumHp(ProgramSkillFrame f)=>e.ChangeProgramMaximumHp(f,1);
        public void Recover(ProgramSkillFrame f)=>new ProgramSkillHost(e).Recover(f.Id,f.OwnerSeat,f.OwnerSeat,1,null,null);
        public void Grant(ProgramSkillFrame f,string skill)=>e.AcquireRuntimeSkills(e._players[f.OwnerSeat],f.SkillId,[skill]);
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,34700)]
    private void ObserveMouDongZhuoOlUse(IGameEvent payload)
    {
        if(!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouDongZhuoZhibing))return;
        CardActionContext? action=null;long frameId=0;
        if(payload is CardUseDeclaredEvent declared&&LifecycleCardUse(declared.ResolutionId)is{Action:{Type:CardActionType.Use}a}use&&
            use.SourceSeat==declared.SourceSeat&&use.CardId==declared.CardId&&use.CardKind==declared.CardKind&&a.ActorSeat==use.SourceSeat&&a.EffectiveKind==use.CardKind)
        {action=a;frameId=use.Id;}
        else if(payload is CardActionAcceptedEvent{Action.Type:CardActionType.Response}accepted)
        {
            // The earlier shared observer has already published the exact
            // Slash-defense Dodge / Nullification true-Use fact. Pure played
            // responses never get that fact and cannot add to this total.
            var proof=ProgramEventHistory<CurrentTurnCardUseKindsRecordedEvent>().LastOrDefault(e=>e.CardActionId==accepted.Action.ActionId&&e.ActorSeat==accepted.Action.ActorSeat&&e.RoundActionType==CardActionType.Response);
            if(proof is not null){action=accepted.Action;frameId=proof.OriginFrameId;}
        }
        if(action is null||action.ActionId<=0||ProgramEventHistory<MouDongZhuoOlBlackUseEvent>().Any(e=>e.ActionId==action.ActionId))return;
        var suit=action.EffectiveSuit??(action.PhysicalCards is[var card]?GetProgramEffectiveSuit(_players[action.ActorSeat],GetAdvancedCard(card.CardId)):Suit.None);
        var black=suit!=Suit.None&&(action.EffectiveIsRed is{}red?!red:suit is Suit.Spade or Suit.Club);
        AdvanceEventRulesAndQueueFact(new MouDongZhuoOlBlackUseEvent(action.ActorSeat,action.FactionOrigin?.ActorFactionId??GetEffectiveFactionId(_players[action.ActorSeat]),frameId,action.ActionId,_turnNumber,black));
    }
}
