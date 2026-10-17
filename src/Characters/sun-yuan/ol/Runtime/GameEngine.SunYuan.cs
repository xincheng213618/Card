using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramCapability]private sealed class SunYuanCapability(GameEngine e):ISunYuanHost{public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginSunYuan(f,op);}
    private SkillProgramStepOutcome BeginSunYuan(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(f.SunYuan is not null||f.InstructionIndex!=1||GetPausedPrivateOfferEffect(f)?.Op!=op||!SunYuanParent(f)||f.WindowContext is not{}c)throw new InvalidOperationException("Sun Yuan requires her exact registered native candidate and original instruction.");
        var saying=op==SkillProgramEffectOp.ResolveSayingPlayStartObtainAndReply;
        if(saying?f.SkillId!="ol:saying"||f.TriggerId!="play-start-obtain":f.SkillId!="ol:dongxin"||!DongxinContextLegal(f.OwnerSeat,f.TriggerId!,c))throw new InvalidOperationException("Sun Yuan lost the accepted play-start or damage-direction qualification.");
        var holder=saying?f.OwnerSeat:c.TargetSeat!.Value;var lost=saying?0:Math.Max(0,_players[holder].MaxHp-_players[holder].Hp);
        var required=saying?1:Math.Min(lost+1,GetHand(_players[holder]).Count+GetEquipment(holder).Count);
        var r=new SunYuanReceipt(SunYuanSource(f),f.GameplayHash,op,c.ParentFrameId,_turnNumber,_turnProgression.OwnerSeat,saying?SunYuanStage.Target:SunYuanStage.DiscardCards){Holder=holder,DamageFrameId=c.DamageFrameId??0,DamageSource=saying?null:c.SourceSeat,DamageAmount=saying?0:c.Amount,FrozenLostHp=lost,Required=required};
        ReplaceRuntimeTop(f=f with{SunYuan=r});AdvanceEventRulesAndQueueFact(new SunYuanStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.WindowId,r.ActualTurn,r.TurnOwner,holder,r.DamageFrameId,r.DamageSource,r.DamageAmount,lost,required));
        foreach(var p in _players)_observedSkillGrantRevisions.TryAdd(p.Seat,p.SkillGrants.Revision);
        if(!saying&&required==0)PayDongxin(f);else PublishSaying(f);return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> SunYuanChoices(ProgramSkillFrame f)
    {
        var r=f.SunYuan!;
        PromptChoice C(string branch,string text,int? target=null)=>new(new($"sun-yuan.{f.Id}.{r.Stage}.{r.Selected.Count}.{branch}.{target}"),text,[],target is{}seat?[seat]:[],new Dictionary<string,string>{{"program-action","sun-yuan"},{"branch",branch},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)}});
        if(r.Stage==SunYuanStage.Target)return Array.AsReadOnly(_players.Where(p=>p.IsAlive&&p.Seat!=f.OwnerSeat&&SunYuanEligibleCards(f.OwnerSeat,p.Seat,true).Count>0).Select(p=>C("target",p.Name,p.Seat)).ToArray());
        if(r.Stage is SunYuanStage.ObtainCard or SunYuanStage.DiscardCards)
        {
            var obtain=r.Stage==SunYuanStage.ObtainCard;var eligible=SunYuanEligibleCards(f.OwnerSeat,r.Holder,obtain).Select(c=>c.Id).ToHashSet();
            return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id,f.OwnerSeat,r.Holder,obtain?[CardZoneKind.Hand,CardZoneKind.Equipment,CardZoneKind.Judgment]:[CardZoneKind.Hand,CardZoneKind.Equipment],obtain?OwnedCardMoveIntent.Transfer:OwnedCardMoveIntent.Discard,canSelect:(_,card)=>eligible.Contains(card.Id)&&!r.Selected.Contains(card.Id))
                .Select(choice=>choice with{Id=new($"sun-yuan.{f.Id}.{r.Stage}.{r.Selected.Count}.{choice.Id.Value}"),Parameters=new Dictionary<string,string>(choice.Parameters){["program-action"]="sun-yuan",["branch"]="card"}}).ToArray());
        }
        if(r.Stage!=SunYuanStage.Request||!_players[r.Holder].IsAlive||!_players[f.OwnerSeat].IsAlive)return [];
        var view=f with{AssistedSlashRequest=new(r.Holder,f.OwnerSeat,ActorChoosesTarget:true)};
        var ordinary=AssistedPhysicalSlashChoices(view).Where(c=>c.Parameters.GetValueOrDefault("request-option")!="decline").Select(c=>c with{Parameters=new Dictionary<string,string>(c.Parameters){["program-action"]="sun-yuan",["branch"]="use"}});
        return Array.AsReadOnly(ordinary.Concat(RoundOnceCardlessProgramSlashChoices(f,r.Holder,[f.OwnerSeat])).Concat(SayingTieredProgramSlashChoices(f)).Concat(KenshangProgramSlashChoices(f,r.Holder,[f.OwnerSeat])).Append(C("decline","不使用杀")).ToArray());
    }
    private void PublishSaying(ProgramSkillFrame supplied,bool keepCurrentRequest=false)
    {
        var f=GetActiveProgramFrame(supplied.Id);var r=f.SunYuan!;
        if(r.Stage is SunYuanStage.SlashIssuing or SunYuanStage.NativeUse or SunYuanStage.NativeChildren)return;
        if(!_players[f.OwnerSeat].IsAlive||r.Stage==SunYuanStage.Request&&!_players[r.Holder].IsAlive){FinishSunYuan(f,r.Paid);return;}
        if(!r.Paid&&!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)){FinishSunYuan(f,false);return;}
        var choices=SunYuanChoices(f);if(choices.Count==0){FinishSunYuan(f,r.Paid);return;}var chooser=r.Stage==SunYuanStage.Request?r.Holder:f.OwnerSeat;var skill=_contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});_pendingDecision=new(DecisionKind.ProgramTrigger,chooser,r.Stage==SunYuanStage.Request?"飒影：可对孙鸢使用一张杀。":r.Stage==SunYuanStage.Target?"飒影：选择一名其他角色。":r.Stage==SunYuanStage.ObtainCard?"飒影：获得一张区域内的牌。":$"恫心：选择第 {r.Selected.Count+1}/{r.Required} 张弃置牌。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=true,TargetSeat=r.Holder,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[chooser].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("sun-yuan")]
    private void AnswerSunYuan(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{SunYuan:{}r}f||_pendingDecision?.PlayerSeat!=(r.Stage==SunYuanStage.Request?r.Holder:f.OwnerSeat)||!SunYuanChoices(f).Any(c=>AssistedChoicesEqual([c],[choice])))throw new InvalidOperationException("Sun Yuan requires her exact private choice and participant.");
        AssertSunYuan(f);ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});
        if(choice.Parameters["branch"]=="decline"){FinishSunYuan(f,true);return;}
        if(r.Stage==SunYuanStage.Target){var holder=choice.Targets.Single();ReplaceRuntimeTop(f=f with{SunYuan=r with{Holder=holder,Stage=SunYuanStage.ObtainCard}});AdvanceEventRulesAndQueueFact(new SunYuanTargetEvent(f.Id,holder));PublishSaying(f);return;}
        if(r.Stage==SunYuanStage.Request)
        {
            if(choice.Parameters.GetValueOrDefault("request-option")=="faction"){BeginSayingFactionRequest(f,r.Holder,f.OwnerSeat);return;}
            var(cards,kind,source)=ReadAssistedSlashPayment(_players[r.Holder],choice);
            if(cards.Count==0||!AssistedSlashPaymentHasRange(_players[r.Holder],f.OwnerSeat,cards)||!CanUseSlashTarget(_players[r.Holder],_players[f.OwnerSeat],cards[0],source,kind,noEffectiveRank:cards.Count>1,specificEffectiveRank:choice.Parameters.GetValueOrDefault("equipment")==CardKind.ZhangbaSerpentSpear.ToString()?ZhangbaSpecificSlashRank(_players[r.Holder],cards):null,physicalCardIds:cards.Select(c=>c.Id).ToArray()))throw new InvalidOperationException("Saying cannot bypass native Slash range, targets or material legality.");
            PrepareSayingSuppliedSlash(f,r.Holder,r.Holder,f.OwnerSeat,kind,cards,source);ResolveSlashCore(_players[r.Holder],_players[f.OwnerSeat],cards[0],kind,r.Holder,physicalCards:cards,countsTowardSlashLimit:false,usesZhuqueFan:kind==CardKind.FireSlash&&(cards[0].Kind==CardKind.Slash||IsOwnPublicPileSlashMaterial(_players[r.Holder],cards[0]))&&HasZhuqueFan(_players[r.Holder]),conversionSource:source,programSkillCardUseFrameId:f.Id);return;
        }
        if(!Enum.TryParse<CardZoneKind>(choice.Parameters.GetValueOrDefault("source-zone"),out var zone)||!int.TryParse(choice.Parameters.GetValueOrDefault("slot-index"),out var slot))throw new InvalidOperationException("Sun Yuan lost the opaque original card slot.");
        var cardsAt=_cardZones.CardsAt(new(zone,r.Holder));if(slot<0||slot>=cardsAt.Count)throw new InvalidOperationException("Sun Yuan's original card slot departed.");var id=cardsAt[slot].Id;
        ReplaceRuntimeTop(f=f with{SunYuan=r with{Selected=r.Selected.Append(id).ToArray()}});AdvanceEventRulesAndQueueFact(new SunYuanSelectedEvent(f.Id,r.Selected.Count,id));
        if(r.Stage==SunYuanStage.ObtainCard){IssueSunYuanMove(f,[id],CardLocation.Hand(f.OwnerSeat),SunYuanStage.ObtainChildren,SayingObtainReason,false);ContinueSunYuan(f.Id);return;}
        if(f.SunYuan!.Selected.Count<r.Required){PublishSaying(f);return;}PayDongxin(f);
    }
    private void PayDongxin(ProgramSkillFrame f)
    {
        var r=f.SunYuan!;
        if(r.Paid||r.Selected.Count!=r.Required||r.Selected.Any(id=>!SunYuanEligibleCards(f.OwnerSeat,r.Holder,false).Any(c=>c.Id==id))||!DongxinDirectionAvailable(f.OwnerSeat,f.TriggerId!))throw new InvalidOperationException("Dongxin cannot underpay, repeat a direction, or replace its exact HE primary set.");
        ReplaceRuntimeTop(f=f with{SunYuan=r with{Paid=true}});AdvanceEventRulesAndQueueFact(new SunYuanPaidEvent(f.Id,r.ActualTurn,r.TurnOwner,f.OwnerSeat,f.TriggerId!,r.Selected.Count));
        IssueSunYuanMove(f,r.Selected,CardLocation.DiscardPile,SunYuanStage.DiscardChildren,DongxinDiscardReason,true);ContinueSunYuan(f.Id);
    }
    private void IssueSunYuanMove(ProgramSkillFrame supplied,IReadOnlyList<int> ids,CardLocation to,SunYuanStage stage,string reason,bool discard)
    {
        var f=GetActiveProgramFrame(supplied.Id);var r=f.SunYuan!;var materials=ids.Select(id=>{var c=GetAdvancedCard(id);var from=_cardZones.GetLocation(id);return new SunYuanEntity(id,c.Kind,from==CardLocation.Hand(r.Holder)?AdvancedEffectiveHandKind(_players[r.Holder],c):c.Kind,from,c.IsGeneralWeapon);}).ToArray();
        var grain=materials.Any(c=>c.Kind==CardKind.WoodenOx&&c.From==CardLocation.Equipment(r.Holder))?GetWoodenOxGrain(_players[r.Holder]).Select(c=>c.Id).ToArray():[];
        if(stage is SunYuanStage.ObtainChildren or SunYuanStage.DiscardChildren)
        {
            for(var i=0;i<materials.Length;i++){var c=materials[i];AdvanceEventRulesAndQueueFact(new SunYuanMaterialEvent(f.Id,i,c.CardId,c.Kind,c.EffectiveKind,c.From,c.GeneralWeapon));}
            for(var i=0;i<grain.Length;i++)AdvanceEventRulesAndQueueFact(new SunYuanGrainEvent(f.Id,i,grain[i]));
        }
        r=r with{Stage=stage,Paid=true,Materials=stage==SunYuanStage.ReclaimChildren?r.Materials:materials,Grain=grain,Step=r.Step+1,Before=_movementSequence,After=_movementSequence,ActualCount=ids.Count};
        ReplaceRuntimeTop(f=f with{SunYuan=r,PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        if(stage==SunYuanStage.ObtainChildren)AdvanceEventRulesAndQueueFact(new SunYuanPaidEvent(f.Id,r.ActualTurn,r.TurnOwner,f.OwnerSeat,"saying",1));
        void Paid(long batch,IReadOnlyList<CardMovementRecord> records)
        {
            foreach(var material in materials)
            {
                var m=records.Single(m=>m.CardId==material.CardId);AdvanceEventRulesAndQueueFact(new SunYuanEntityMovedEvent(f.Id,r.Step,stage,batch,m.CardId,m.CardKind,m.From,m.To,m.Sequence));
                if(stage==SunYuanStage.ReclaimChildren){var original=r.Materials.Single(c=>c.CardId==m.CardId);var paid=ProgramEventHistory<SunYuanEntityMovedEvent>().Single(e=>e.FrameId==f.Id&&e.Stage==SunYuanStage.DiscardChildren&&e.CardId==m.CardId);AdvanceEventRulesAndQueueFact(new DongxinEntityAcquiredEvent(f.Id,r.Source,f.OwnerSeat,m.CardId,original.EffectiveKind,paid.Sequence,m.Sequence));}
            }
        }
        if(ids.Count>0){if(discard)MoveProgramLogicalDiscard(f,f.OwnerSeat,ids,new(reason),Paid);else MoveProgramCardsFromMultipleSources(ids,to,new(reason),Paid);}
        f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{SunYuan=f.SunYuan! with{After=_movementSequence}});AdvanceEventRulesAndQueueFact(new SunYuanMovementIssuedEvent(f.Id,r.Step,stage,r.Before,_movementSequence,ids.Count));
    }
    private bool DrainSunYuan(ProgramSkillFrame f)=>TryBeginAdvancedSkillsChanged(f.Id)||TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id);
    private void ContinueSunYuan(long id)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{SunYuan:{}r}f||f.Id!=id)return;AssertSunYuan(f);
        if(r.Stage is SunYuanStage.NativeUse or SunYuanStage.SlashIssuing)return;
        if(r.Stage is SunYuanStage.Target or SunYuanStage.ObtainCard or SunYuanStage.DiscardCards or SunYuanStage.Request){if(_pendingDecision is null)PublishSaying(f);return;}
        if(f.AttackAttempt is not null||DrainSunYuan(f))return;f=GetActiveProgramFrame(id);r=f.SunYuan!;ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null});
        if(r.Stage==SunYuanStage.ObtainChildren)
        {
            if(_winner!=Winner.None||!_players[f.OwnerSeat].IsAlive||!_players[r.Holder].IsAlive){FinishSunYuan(f,true);return;}
            if(!IsWithinAttackRange(r.Holder,f.OwnerSeat)){AdvanceEventRulesAndQueueFact(new SayingRangeGrantedEvent(id,r.Source,_roundNumber,f.OwnerSeat,r.Holder));FinishSunYuan(f,true);return;}
            ReplaceRuntimeTop(f=f with{SunYuan=r with{Stage=SunYuanStage.Request}});AdvanceEventRulesAndQueueFact(new SayingRequestIssuedEvent(id,r.Holder,f.OwnerSeat));PublishSaying(f);return;
        }
        if(r.Stage==SunYuanStage.DiscardChildren)
        {
            ReplaceRuntimeTop(f=f with{SunYuan=r with{Stage=SunYuanStage.DamageChildren}});
            if(_winner==Winner.None&&r.DamageSource is{}source&&_players[source].IsAlive&&_players[f.OwnerSeat].IsAlive){AdvanceEventRulesAndQueueFact(new DongxinDamageIssuedEvent(id,f.OwnerSeat,source));BeginProgramSkillDamage(f,source,1,nature:DamageNature.Normal);return;}
            ReclaimDongxin(f);return;
        }
        if(r.Stage==SunYuanStage.DamageChildren){ReclaimDongxin(f);return;}
        if(r.Stage is SunYuanStage.ReclaimChildren or SunYuanStage.NativeChildren){FinishSunYuan(f,true);return;}
        throw new InvalidOperationException("Sun Yuan returned to an unissued native stage.");
    }
    private void ReclaimDongxin(ProgramSkillFrame f)
    {
        var r=f.SunYuan!;var eligible=r.Materials.Where(c=>IsSlashCard(c.EffectiveKind)&&_cardZones.Contains(CardLocation.DiscardPile,c.CardId)&&ProgramEventHistory<SunYuanEntityMovedEvent>().SingleOrDefault(e=>e.FrameId==f.Id&&e.Stage==SunYuanStage.DiscardChildren&&e.CardId==c.CardId)is{}paid&&_cardMovements.Last(m=>m.CardId==c.CardId).Sequence==paid.Sequence).Select(c=>c.CardId).ToArray();
        if(!_players[f.OwnerSeat].IsAlive||_winner!=Winner.None||eligible.Length==0){FinishSunYuan(f,true);return;}
        IssueSunYuanMove(f,eligible,CardLocation.Hand(f.OwnerSeat),SunYuanStage.ReclaimChildren,DongxinReclaimReason,false);ContinueSunYuan(f.Id);
    }
    private void FinishSunYuan(ProgramSkillFrame f,bool applied)
    {
        if(f.WeijieReturn is{}wj&&f.SunYuan is{Native:{}o}r&&(!wj.Returned||!r.NativeReturned||wj.UseId!=o.UseId))throw new InvalidOperationException("Saying cannot clear a supplied native lease before its whole child returns.");
        if(f.HuanpeiReturn is{}huanpei&&(!huanpei.Returned||f.SunYuan is not{NativeReturned:true,Native:{}huanpeiNative}||huanpei.UseId!=huanpeiNative.UseId))throw new InvalidOperationException("Saying cannot release Huanpei before all native children returned.");
        if(f.ZonghuReturn is not null&&!HasSayingZonghuPayment(f))throw new InvalidOperationException("Saying cannot clear its paid Zonghu lease before the exact native child and all settlement children return.");
        AdvanceEventRulesAndQueueFact(new SunYuanCompletedEvent(f.Id,applied));ReplaceRuntimeTop(f=f with{SunYuan=null,PendingMovementContinuation=null,WeijieReturn=null,ZonghuReturn=null,HuanpeiReturn=null});FinishProgramSkill(f,applied);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume,27490)]private bool ResumeSunYuan(long id){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{SunYuan:not null}f||f.Id!=id)return false;ContinueSunYuan(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,27490)]private bool ReturnSunYuanMovement(ProgramSkillFrame f){if(f.SunYuan is null)return false;ContinueSunYuan(f.Id);return true;}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27490)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27490)]private bool ContinueIssuedSunYuan(ProgramSkillFrame f){if(f.SunYuan is not{Paid:true})return false;AssertSunYuan(f);return true;}
    [ProgramAiChoice(SkillProgramEffectOp.ResolveSayingPlayStartObtainAndReply,SkillProgramEffectOp.ResolveDongxinDamageDiscardAndReclaim)]private PromptChoice ChooseSunYuanAi(PendingDecision p)=>p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("branch")!="decline")??p.Choices.Last();
}
