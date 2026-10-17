using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string WenYangHandDiscard="skill-program.wen-yang.qingjiao.hand-discard",WenYangEndingDiscard="skill-program.wen-yang.qingjiao.ending-discard",WenYangGain="skill-program.wen-yang.qingjiao.obtain",WenYangDraw="skill-program.wen-yang.lvli.draw";
    private bool HasWenYangCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveQingjiaoWholeHandExchange);
    private static CardConversionSource WenYangSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    private int WenYangAwakeningLevel(int owner)=>ProgramEventHistory<WenYangAwakenedEvent>().Where(e=>e.OwnerSeat==owner).Select(e=>e.Level).DefaultIfEmpty(0).Max();
    private bool WenYangHasAwakened(int owner,int level)=>ProgramEventHistory<WenYangAwakenedEvent>().Any(e=>e.OwnerSeat==owner&&e.Level==level);
    private int WenYangLvliLimit(int owner)=>WenYangHasAwakened(owner,1)&&owner==_currentSeat?2:1;
    private int WenYangLvliUses(int owner)=>ProgramEventHistory<WenYangLvliIssuedEvent>().Count(e=>e.OwnerSeat==owner&&e.ActualTurn==_turnNumber&&e.TurnOwner==_currentSeat);
    [ProgramCapability]private sealed class WenYangCapability(GameEngine e):IWenYangHost{public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginWenYang(f,op);}
    private bool WenYangDamageParent(ProgramTriggerCandidate candidate,ProgramSkillWindowContext c)
    {
        if(c.Window!=SkillProgramTriggerWindow.AfterDamageApplied||c.OwnerSeat!=candidate.OwnerSeat||c.Amount<=0||c.OccurrenceIndex!=0||c.DamageFrameId is not{}id||
            _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w=>w.Id==c.ParentFrameId)is not{}w||w.ParentFrameId!=id||w.TriggerWindow!=c.Window||w.CandidateIndex<0||w.CandidateIndex>=w.Candidates.Count||w.Candidates[w.CandidateIndex].ToProgramCandidate()!=candidate)return false;
        var a=GetDamageTriggerAttack(w);return a.DamageWasApplied&&a.TargetSeat==c.TargetSeat&&(a.IsSourceLess?c.SourceSeat is null:a.SourceSeat==c.SourceSeat)&&
            (candidate.BindingId=="after-causing-damage"?!a.IsSourceLess&&a.SourceSeat==candidate.OwnerSeat:a.TargetSeat==candidate.OwnerSeat&&(a.IsSourceLess||a.SourceSeat!=candidate.OwnerSeat));
    }
    private bool WenYangAwakeningParent(ProgramTriggerCandidate candidate,ProgramSkillWindowContext c)
    {
        if(c.OwnerSeat!=candidate.OwnerSeat)return false;
        if(candidate.SkillId=="ol:choujue")return WenYangChoujueParent(candidate,c);
        return candidate.SkillId=="ol:beishui"&&candidate.BindingId=="actual-preparation"&&c.Window==SkillProgramTriggerWindow.TurnStartBeforeNormalFlow&&candidate.OwnerSeat==_currentSeat&&c.SourceSeat==candidate.OwnerSeat&&c.TargetSeat==candidate.OwnerSeat&&
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w=>w.Id==c.ParentFrameId)is{Window:SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,Continuation:ProgramLifecycleContinuation.NormalTurnStart}p&&p.OwnerSeat==candidate.OwnerSeat&&p.CandidateIndex>=0&&p.CandidateIndex<p.Candidates.Count&&p.Candidates[p.CandidateIndex]==candidate;
    }
    private bool IsWenYangChoujueCandidate(ProgramTriggerCandidate candidate,ProgramSkillWindowContext c)=>IsValidPlayerSeat(candidate.OwnerSeat)&&_players[candidate.OwnerSeat].IsAlive&&WenYangChoujueParent(candidate,c);
    private bool WenYangChoujueParent(ProgramTriggerCandidate candidate,ProgramSkillWindowContext c)=>candidate.SkillId=="ol:choujue"&&candidate.BindingId is("own-actual-turn-ended"or"foreign-actual-turn-ended")&&
        c.Window==SkillProgramTriggerWindow.AfterTurnEnded&&c.OwnerSeat==candidate.OwnerSeat&&IsValidPlayerSeat(candidate.OwnerSeat)&&
        _contentRegistry.GetSkill(candidate.SkillId).Program is{}program&&program.GameplayHash==candidate.GameplayHash&&GetProgramTrigger(candidate).Effects is[{Op:SkillProgramEffectOp.ResolveWenYangAwakening}]&&
        _resolutionStack.OfType<DeferredTurnEndFrame>().SingleOrDefault(p=>p.Id==c.ParentFrameId)is{}parent&&IsActualAfterTurnEndedParent(parent)&&parent.AfterTurnEnded is{}window&&window.ItemIndex>=0&&window.ItemIndex<window.Items.Count&&window.Items[window.ItemIndex].Candidate==candidate&&
        c.SourceSeat==parent.OwnerSeat&&c.TargetSeat==parent.OwnerSeat&&c.OccurrenceIndex==candidate.OccurrenceIndex&&c.Facts==window.Items[window.ItemIndex].Facts&&
        (candidate.BindingId=="own-actual-turn-ended"?candidate.OwnerSeat==parent.OwnerSeat:candidate.OwnerSeat!=parent.OwnerSeat);
    private bool WenYangPlayParent(ProgramTriggerCandidate candidate,ProgramSkillWindowContext c)=>c.Window==SkillProgramTriggerWindow.PlayPhaseStarting&&c.OwnerSeat==candidate.OwnerSeat&&candidate.OwnerSeat==_currentSeat&&c.SourceSeat==candidate.OwnerSeat&&c.TargetSeat==candidate.OwnerSeat&&
        _resolutionStack.OfType<PlayPhaseStartingBoundaryFrame>().SingleOrDefault(w=>w.Id==c.ParentFrameId)is{}p&&p.OwnerSeat==candidate.OwnerSeat&&p.ItemIndex>=0&&p.ItemIndex<p.Items.Count&&p.Items[p.ItemIndex].Candidate==candidate;
    private IReadOnlyList<WenYangEntity> WenYangDiscardable(int owner,string instance,bool handOnly)=>Array.AsReadOnly(GetHand(_players[owner]).Concat(handOnly?[]:GetEquipment(owner)).Where(c=>
        !IsSelfHandCategoryDiscardForbidden(owner,c,_cardZones.GetLocation(c.Id),OwnedCardMoveIntent.Discard)&&!IsForeignEquipmentDiscardPrevented(owner,c,_cardZones.GetLocation(c.Id),OwnedCardMoveIntent.Discard)&&!IsActiveProgramSourceEquipmentCard(owner,"ol:qingjiao",instance,c)).Select(c=>new WenYangEntity(c.Id,c.Kind,_cardZones.GetLocation(c.Id))).ToArray());
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.Collection,27360)]private bool CollectWenYang(SkillProgramTrigger t)=>t.Effects is not[{Op:SkillProgramEffectOp.ResolveQingjiaoIssuedEndingDiscard}];
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,27360)]
    private bool CanOfferWenYang(ProgramTriggerCandidate candidate,SkillProgramTrigger t,ProgramSkillWindowContext c)
    {
        if(t.Effects is not[var e]||!WenYangComposition.Handles(e.Op))return true;
        var owner=_players[candidate.OwnerSeat];if(e.Op==SkillProgramEffectOp.ResolveQingjiaoIssuedEndingDiscard)return IsIssuedWenYangQingjiaoEndingCandidate(candidate,c);
        if(!owner.IsAlive||_winner!=Winner.None)return false;
        if(e.Op==SkillProgramEffectOp.ResolveLvliActualDamageChoice)return WenYangDamageParent(candidate,c)&&(candidate.BindingId!="after-receiving-damage"||WenYangHasAwakened(owner.Seat,2))&&WenYangLvliUses(owner.Seat)<WenYangLvliLimit(owner.Seat)&&!ProgramEventHistory<WenYangLvliIssuedEvent>().Any(x=>x.OwnerSeat==owner.Seat&&x.DamageWindowId==c.ParentFrameId);
        if(e.Op==SkillProgramEffectOp.ResolveWenYangAwakening)return WenYangAwakeningParent(candidate,c)&&owner.MaxHp>0&&(candidate.SkillId=="ol:choujue"?!WenYangHasAwakened(owner.Seat,1)&&Math.Abs(GetHand(owner).Count-owner.Hp)>=3:!WenYangHasAwakened(owner.Seat,2)&&(GetHand(owner).Count<2||owner.Hp<2));
        return WenYangPlayParent(candidate,c)&&WenYangDiscardable(owner.Seat,candidate.SkillInstanceId,true).Count==GetHand(owner).Count;
    }
    private SkillProgramStepOutcome BeginWenYang(ProgramSkillFrame input,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(input.Id);if(f.WenYang is not null||f.InstructionIndex!=1||f.TriggerId is null||f.ActivationId!=f.TriggerId||f.WindowContext is not{}c||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||GetPausedPrivateOfferEffect(f)?.Op!=op)throw new InvalidOperationException("Wen Yang requires his exact native zero-input binding.");
        var candidate=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,c.OccurrenceIndex);
        if(!CanOfferWenYang(candidate,GetProgramTrigger(f),c))throw new InvalidOperationException("Wen Yang lost his unpaid eligibility or exact issued Ending debt.");
        foreach(var p in _players)_observedSkillGrantRevisions.TryAdd(p.Seat,p.SkillGrants.Revision);
        var owner=_players[f.OwnerSeat];var level=op==SkillProgramEffectOp.ResolveWenYangAwakening?(f.SkillId=="ol:choujue"?1:2):0;var debt=op==SkillProgramEffectOp.ResolveQingjiaoIssuedEndingDiscard?CurrentWenYangDebt(candidate,c):null;
        var stage=op==SkillProgramEffectOp.ResolveLvliActualDamageChoice?WenYangStage.Choice:op==SkillProgramEffectOp.ResolveWenYangAwakening?WenYangStage.MaximumChildren:op==SkillProgramEffectOp.ResolveQingjiaoWholeHandExchange?WenYangStage.HandDiscardChildren:WenYangStage.EndingChildren;
        var r=new WenYangReceipt(WenYangSource(f),f.GameplayHash,op,c.ParentFrameId,_turnNumber,_currentSeat,stage,c.DamageFrameId,debt?.FrameId,level,MaximumBefore:level==0?0:owner.MaxHp,MaximumAfter:level==0?0:owner.MaxHp-1,HpBefore:owner.Hp,HpAfter:level==0?owner.Hp:Math.Min(owner.Hp,owner.MaxHp-1),Paid:stage!=WenYangStage.Choice);
        ReplaceRuntimeTop(f=f with{WenYang=r});AdvanceEventRulesAndQueueFact(new WenYangStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.ParentId,r.ActualTurn,r.TurnOwner,r.DamageId,r.DebtId,level));
        if(stage==WenYangStage.Choice){PublishWenYang(f);return SkillProgramStepOutcome.AwaitChoice;}
        if(level!=0)
        {
            AdvanceEventRulesAndQueueFact(new WenYangAwakenedEvent(f.Id,f.OwnerSeat,level,r.MaximumBefore,r.MaximumAfter,r.HpBefore,r.HpAfter));
            if(r.MaximumAfter==0){owner.MaxHp=0;owner.Hp=r.HpAfter;AdvanceEventRulesAndQueueFact(new MaximumHpChangedEvent(owner.Seat,-1,0,f.SkillId){HpBeforeClamp=r.HpBefore});BeginPlayerDeath(f.Id,owner,null,null,null,causingProgramSkillFrameId:f.Id);return SkillProgramStepOutcome.AwaitChild;}
            ChangeProgramMaximumHp(f,-1);
        }
        else
        {
            if(debt is not null)AdvanceEventRulesAndQueueFact(new WenYangQingjiaoDebtConsumedEvent(debt.FrameId,f.Id,c.ParentFrameId,_turnNumber));
            MoveWenYang(f,WenYangDiscardable(f.OwnerSeat,f.SkillInstanceId,stage==WenYangStage.HandDiscardChildren),stage,stage==WenYangStage.HandDiscardChildren?WenYangHandDiscard:WenYangEndingDiscard,true);
        }
        ContinueWenYang(f.Id);return SkillProgramStepOutcome.AwaitChild;
    }
    private IReadOnlyList<PromptChoice> WenYangChoices(ProgramSkillFrame f)=>f.WenYang is{Stage:WenYangStage.Choice}?Array.AsReadOnly(new[]{"draw","recover"}.Select(branch=>new PromptChoice(new($"wen-yang.{f.Id}.{branch}"),branch=="draw"?"将手牌摸至当前体力值":"将体力回复至当前手牌数",[],[],new Dictionary<string,string>{{"program-action","wen-yang"},{"branch",branch},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)}})).ToArray()):[];
    private void PublishWenYang(ProgramSkillFrame f)
    {
        if(_winner!=Winner.None||!_players[f.OwnerSeat].IsAlive||!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)){FinishWenYang(f);return;}
        var choices=WenYangChoices(f);var skill=_contentRegistry.GetSkill(f.SkillId);ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});_pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"膂力：选择一项（没有正收益也可以选择）。",[],[],f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=true,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};_status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("wen-yang")]private void AnswerWenYang(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{WenYang:{Stage:WenYangStage.Choice}r}f||_pendingDecision?.PlayerSeat!=f.OwnerSeat||!WenYangChoices(f).Any(c=>AssistedChoicesEqual([c],[choice])))throw new InvalidOperationException("Lvli requires the original owner's exact private branch.");
        AssertWenYang(f);ClearPendingDecision();var owner=_players[f.OwnerSeat];var branch=choice.Parameters["branch"];var requested=Math.Max(0,branch=="draw"?owner.Hp-GetHand(owner).Count:GetHand(owner).Count-owner.Hp);var capped=branch=="recover"?Math.Min(requested,owner.MaxHp-owner.Hp):0;
        ReplaceRuntimeTop(f=f with{WenYang=r with{Stage=branch=="draw"?WenYangStage.DrawChildren:WenYangStage.RecoveryChildren,Branch=branch,Requested=requested,ExpectedRecovery=capped,HpBefore=owner.Hp,HpAfter=owner.Hp+capped,Before=_movementSequence,After=_movementSequence,Paid=true}});r=f.WenYang!;
        AdvanceEventRulesAndQueueFact(new WenYangLvliIssuedEvent(f.Id,f.OwnerSeat,_turnNumber,_currentSeat,r.DamageId!.Value,r.ParentId,branch,requested,capped,owner.Hp));
        if(branch=="draw"){var cards=DrawCards(owner,requested,true,new(WenYangDraw));f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{WenYang=f.WenYang! with{After=_movementSequence}});AdvanceEventRulesAndQueueFact(new WenYangDrawIssuedEvent(f.Id,requested,cards.Count,r.Before,_movementSequence));}
        else if(requested>0)new ProgramSkillHost(this).Recover(f.Id,f.OwnerSeat,f.OwnerSeat,requested,null,null);
        if(_resolutionStack.LastOrDefault()?.Id==f.Id)ContinueWenYang(f.Id);
    }
    private void MoveWenYang(ProgramSkillFrame f,IReadOnlyList<WenYangEntity> cards,WenYangStage stage,string reason,bool discard)
    {
        var r=f.WenYang!;var grain=cards.Where(c=>c.From.Zone==CardZoneKind.Equipment&&c.Kind==CardKind.WoodenOx).SelectMany(c=>GetWoodenOxGrain(_players[f.OwnerSeat]).Select(g=>g.Id)).ToArray();
        ReplaceRuntimeTop(f=f with{WenYang=r with{Stage=stage,Entities=cards,WoodenOxGrain=grain,MovementStep=r.MovementStep+1,Before=_movementSequence,After=_movementSequence,Paid=true}});
        void Invoice(long batch,IReadOnlyList<CardMovementRecord> moved){var current=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(current with{WenYang=current.WenYang! with{After=_movementSequence}});foreach(var m in moved)AdvanceEventRulesAndQueueFact(new WenYangEntityMovedEvent(f.Id,current.WenYang!.MovementStep,m.CardId,m.CardKind,m.From,m.To,m.Sequence));}
        if(cards.Count>0){if(discard)MoveProgramLogicalDiscard(f,f.OwnerSeat,cards.Select(c=>c.CardId).ToArray(),new(reason),Invoice);else MoveProgramCardsFromMultipleSources(cards.Select(c=>c.CardId).ToArray(),CardLocation.Hand(f.OwnerSeat),new(reason),Invoice);}
        f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{WenYang=f.WenYang! with{After=_movementSequence}});r=f.WenYang!;AdvanceEventRulesAndQueueFact(new WenYangMovementIssuedEvent(f.Id,r.MovementStep,stage,r.Before,r.After,cards.Count));
    }
    private IReadOnlyList<WenYangEntity> WenYangRandomInventory()
    {
        var pool=_cardZones.CardsAt(CardLocation.DrawPile).Concat(_cardZones.CardsAt(CardLocation.DiscardPile)).ToList();var selected=new List<WenYangEntity>();var names=new HashSet<CardKind>();var slots=new HashSet<EquipmentSlot>();
        while(selected.Count<8){var eligible=pool.Where(c=>!names.Contains(c.Kind)&&(!EquipmentCatalog.IsEquipment(c.Kind)||!slots.Contains(EquipmentCatalog.Get(c.Kind).Slot))).ToArray();if(eligible.Length==0)break;var card=eligible[_random.Next(eligible.Length)];selected.Add(new(card.Id,card.Kind,_cardZones.GetLocation(card.Id)));names.Add(card.Kind);if(EquipmentCatalog.IsEquipment(card.Kind))slots.Add(EquipmentCatalog.Get(card.Kind).Slot);pool.Remove(card);}
        return Array.AsReadOnly(selected.ToArray());
    }
    private bool DrainWenYang(long id)=>TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.Program)||TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.Program)||TryBeginCardsMovedProgramWindow(id)||TryBeginAdvancedSkillsChanged(id);
    private void ContinueWenYang(long id)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{WenYang:{}r}f||f.Id!=id)return;AssertWenYang(f);
        if(r.Stage==WenYangStage.Choice){if(_pendingDecision is null)PublishWenYang(f);return;}
        if(DrainWenYang(id))return;f=GetActiveProgramFrame(id);r=f.WenYang!;
        if(_winner!=Winner.None||!_players[f.OwnerSeat].IsAlive){FinishWenYang(f);return;}
        if(r.Stage==WenYangStage.MaximumChildren){ReplaceRuntimeTop(f=f with{WenYang=r with{Stage=WenYangStage.GrantChildren}});var leaf=r.Awakening==1?"ol:beishui":"ol:qingjiao";GrantProgramSkills(f,[leaf]);AdvanceEventRulesAndQueueFact(new WenYangLeafGrantedEvent(f.Id,f.OwnerSeat,leaf));ContinueWenYang(id);return;}
        if(r.Stage==WenYangStage.HandDiscardChildren)
        {var selected=WenYangRandomInventory();AdvanceEventRulesAndQueueFact(new WenYangQingjiaoDebtIssuedEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.TurnOwner,selected.Count));MoveWenYang(f,selected,WenYangStage.GainChildren,WenYangGain,false);ContinueWenYang(id);return;}
        FinishWenYang(f);
    }
    private void FinishWenYang(ProgramSkillFrame f){AdvanceEventRulesAndQueueFact(new WenYangCompletedEvent(f.Id,f.WenYang!.Op));ReplaceRuntimeTop(f=f with{WenYang=null});FinishProgramSkill(f,true);}
    [ProgramContinuation(ProgramContinuationKind.Resume,27360)]private bool ResumeWenYang(long id){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{WenYang:not null}f||f.Id!=id)return false;ContinueWenYang(id);return true;}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27360)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27360)]private bool ContinuePaidWenYang(ProgramSkillFrame f)
    {if(f.WenYang is{Paid:true})return ValidWenYang(f);return f.WenYang is null&&f.InstructionIndex is 0 or 1&&f.TriggerId=="issued-actual-ending"&&f.WindowContext is{}c&&IsIssuedWenYangQingjiaoEndingCandidate(new(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,c.OccurrenceIndex),c);}
    [ProgramAiChoice(SkillProgramEffectOp.ResolveLvliActualDamageChoice)]private PromptChoice ChooseWenYangAi(PendingDecision p,ProgramSkillFrame f)
    {var owner=_players[f.OwnerSeat];var draw=Math.Max(0,owner.Hp-GetHand(owner).Count);var recover=Math.Min(Math.Max(0,GetHand(owner).Count-owner.Hp),owner.MaxHp-owner.Hp);return p.Choices.First(c=>c.Parameters["branch"]==(recover*2>draw?"recover":"draw"));}
}
