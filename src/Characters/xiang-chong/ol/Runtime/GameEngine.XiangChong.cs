using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string XiangGift="skill-program.xiang-chong.guying.gift",XiangDebt="skill-program.xiang-chong.guying.debt",XiangReturn="skill-program.xiang-chong.guying.return",MuzhenGift="skill-program.xiang-chong.muzhen.gift",MuzhenClaim="skill-program.xiang-chong.muzhen.claim";
    private static CardConversionSource XiangSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    private int XiangDebtCount(int owner)
    {
        var through=ProgramEventHistory<XiangChongDebtConsumedEvent>().Where(e=>e.OwnerSeat==owner).Select(e=>e.ThroughFrameId).DefaultIfEmpty(0).Max();
        return ProgramEventHistory<XiangChongGuyingInvokedEvent>().Count(e=>e.OwnerSeat==owner&&e.FrameId>through);
    }
    private CardMovementRecord? XiangLoss(int owner,ProgramSkillWindowContext context)
    {
        if(context.Window!=SkillProgramTriggerWindow.CardsMoved||context.MovementBatch is not{}b||b.TurnNumber!=_turnNumber||b.MovementTiming is not{}t||t.ActualTurnOwnerSeat!=_currentSeat||t.ActualTurnOwnerSeat==owner||!_players[owner].IsAlive||!_players[_currentSeat].IsAlive||
            ProgramEventHistory<XiangChongGuyingInvokedEvent>().Any(e=>e.OwnerSeat==owner&&e.ActualTurn==b.TurnNumber))return null;
        var lost=b.Movements.Where(m=>m.From.OwnerSeat==owner&&m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment&&m.From!=m.To&&_cardMovements.Contains(m)).ToArray();
        return lost is[var single]?single:null;
    }
    private IReadOnlyList<XiangChongCard> XiangOwned(int owner,ProgramSkillFrame? f=null,bool discard=false)=>Array.AsReadOnly(GetHand(_players[owner]).Concat(GetEquipment(owner))
        .Where(c=>!c.IsGeneralWeapon&&(f is null||!IsActiveProgramSourceEquipmentCard(owner,f.SkillId,f.SkillInstanceId,c))&&(!discard||!IsForeignEquipmentDiscardPrevented(owner,c,_cardZones.GetLocation(c.Id),OwnedCardMoveIntent.Discard)))
        .Select(c=>new XiangChongCard(c.Id,c.Kind,_cardZones.GetLocation(c.Id))).ToArray());
    private long XiangPlayToken(int owner)=>CurrentActualDiscardRecoveryPhase() is{Kind:ActualDiscardRecoveryPhaseKind.Play,ActorSeat:var actor}p&&actor==owner?p.Token:0;
    private bool MuzhenUsed(int owner,string branch,long token)=>ProgramEventHistory<XiangChongBranchPaidEvent>().Any(e=>e.OwnerSeat==owner&&e.Branch==branch&&e.PhaseToken==token);
    private bool MuzhenTarget(ProgramSkillFrame? f,int owner,int target,string branch)=>target!=owner&&IsValidPlayerSeat(target)&&_players[target].IsAlive&&
        (branch=="gift"?GetEquipment(target).Count>0&&XiangOwned(owner,f).Count>=2:XiangOwned(owner,f).Any(c=>EquipmentCatalog.IsEquipment(c.Kind)&&CanPlaceOwnedEquipment(owner,target,GetAdvancedCard(c.CardId),c.From)));
    [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart,27140,SkillProgramEffectOp.ResolveMuzhenExchange)]
    private bool AllowsMuzhen(ProgramActivationLegalityContext gate)=>XiangPlayToken(gate.OwnerSeat)is>0 and var token&&gate.Activation.Effects is[{StateId:{}branch}]&&!MuzhenUsed(gate.OwnerSeat,branch,token)&&_players.Any(t=>MuzhenTarget(null,gate.OwnerSeat,t.Seat,branch));
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,27140)]
    private bool QualifyGuying(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext context)=>
        t.Effects is[{Op:SkillProgramEffectOp.ResolveGuyingAtomicLossChoice}]?XiangLoss(c.OwnerSeat,context)is not null:
        t.Effects is not[{Op:SkillProgramEffectOp.SettleGuyingPreparationDebt}]||context.Window==SkillProgramTriggerWindow.TurnStartBeforeNormalFlow&&c.OwnerSeat==_currentSeat&&XiangDebtCount(c.OwnerSeat)>0;
    [ProgramCapability]private sealed class XiangChongCapability(GameEngine e):IXiangChongHost
    {public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffect effect)=>e.BeginXiangChong(f,effect);}
    private SkillProgramStepOutcome BeginXiangChong(ProgramSkillFrame input,SkillProgramEffect e)
    {
        var f=GetActiveProgramFrame(input.Id);
        if(f.XiangChong is not null||f.InstructionIndex!=1||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0)throw new InvalidOperationException("XiangChong requires its unchanged zero-input native instruction.");
        var branch=e.Op==SkillProgramEffectOp.ResolveGuyingAtomicLossChoice?"loss":e.Op==SkillProgramEffectOp.SettleGuyingPreparationDebt?"debt":e.StateId!;
        var r=new XiangChongReceipt(XiangSource(f),f.GameplayHash,f.InstructionIndex,_turnNumber,_currentSeat,branch,branch=="loss"?XiangChongStage.Choice:branch=="debt"?XiangChongStage.Cards:XiangChongStage.Targets);
        if(branch=="loss")
        {
            var m=f.WindowContext is{}c?XiangLoss(f.OwnerSeat,c):null;if(m is null)throw new InvalidOperationException("Guying lost its exact unconsumed off-turn atomic single-entity loss.");
            r=r with{LossBatchId=f.WindowContext!.MovementBatch!.Id,LossSequence=m.Sequence,Lost=new(m.CardId,m.CardKind,m.From),LossDestination=m.To,Paid=true};
        }
        else if(branch=="debt")
        {
            if(f.WindowContext?.Window!=SkillProgramTriggerWindow.TurnStartBeforeNormalFlow||f.OwnerSeat!=_currentSeat)throw new InvalidOperationException("Guying debt belongs to its own real preparation.");
            var count=XiangDebtCount(f.OwnerSeat);var cards=XiangOwned(f.OwnerSeat,f,true);r=r with{Required=Math.Min(count,cards.Count),Cards=cards,Paid=true};
        }
        else
        {
            var token=XiangPlayToken(f.OwnerSeat);if(f.TriggerId is not null||token<=0||MuzhenUsed(f.OwnerSeat,branch,token)||!_players.Any(t=>MuzhenTarget(f,f.OwnerSeat,t.Seat,branch)))throw new InvalidOperationException("Muzhen requires its unused own actual Play branch and legal true payment.");
            r=r with{PhaseToken=token,Required=branch=="gift"?2:1};
        }
        ReplaceRuntimeTop(f=f with{XiangChong=r});
        AdvanceEventRulesAndQueueFact(new XiangChongStartedEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.TurnOwner,branch,r.PhaseToken,r.Required,r.LossBatchId,r.LossSequence));
        if(branch=="loss")AdvanceEventRulesAndQueueFact(new XiangChongGuyingInvokedEvent(f.Id,f.OwnerSeat,r.ActualTurn,r.LossBatchId,r.LossSequence,r.Lost!.CardId,r.Source));
        if(branch=="debt")
        {
            var count=XiangDebtCount(f.OwnerSeat);var through=ProgramEventHistory<XiangChongGuyingInvokedEvent>().Where(x=>x.OwnerSeat==f.OwnerSeat).Select(x=>x.FrameId).DefaultIfEmpty(0).Max();
            AdvanceEventRulesAndQueueFact(new XiangChongDebtConsumedEvent(f.Id,f.OwnerSeat,count,through));
            if(r.Required==0){FinishXiang(f,"no-discardable-cards");return SkillProgramStepOutcome.AwaitChild;}
        }
        if(branch=="loss"&&XiangChoices(f).Count==0){FinishXiang(f,"neither-compensation-executable");return SkillProgramStepOutcome.AwaitChild;}
        PublishXiang(f);return SkillProgramStepOutcome.AwaitChoice;
    }
    private bool XiangLostStillAvailable(ProgramSkillFrame f)=>f.XiangChong is{Lost:{}lost,LossDestination:{}to}r&&_cardZones.GetLocation(lost.CardId)==to&&to!=CardLocation.OutsideGame&&to!=CardLocation.Hand(f.OwnerSeat)&&to!=CardLocation.Equipment(f.OwnerSeat)&&
        _cardMovements.Last(m=>m.CardId==lost.CardId).Sequence==r.LossSequence;
    private static Dictionary<string,string> XiangArgs(string branch)=>new(){{"program-action","xiang-chong"},{"branch",branch}};
    private IReadOnlyList<PromptChoice> XiangChoices(ProgramSkillFrame f)
    {
        var r=f.XiangChong!;var choices=new List<PromptChoice>();
        void Add(string branch,string label,int[]?cards=null,int[]?targets=null,Dictionary<string,string>?extra=null)
        {var args=XiangArgs(branch);if(extra is not null)foreach(var(k,v)in extra)args[k]=v;choices.Add(new(new($"xiang.{f.Id}.{branch}.{choices.Count}"),label,cards??[],targets??[],args));}
        if(r.Stage==XiangChongStage.Choice)
        {
            if(_players[r.TurnOwner].IsAlive&&XiangOwned(r.TurnOwner).Count>0)Add("give","随机交给向宠一张牌");
            if(XiangLostStillAvailable(f)&&(!EquipmentCatalog.IsEquipment(r.Lost!.Kind)||CanIssueXiangChongGuyingEquipment(f,r.Lost.CardId,r.LossDestination!.Value)))Add("return",EquipmentCatalog.IsEquipment(r.Lost!.Kind)?"令向宠使用原装备牌":"令向宠获得原失去的牌");
        }
        else if(r.Stage==XiangChongStage.Targets)
        {foreach(var t in _players.Where(t=>MuzhenTarget(f,f.OwnerSeat,t.Seat,r.Branch)))Add("target","选择 "+t.Name,targets:[t.Seat]);}
        else if(r.Stage==XiangChongStage.Cards)
        {
            if(r.Selected.Count<r.Required)foreach(var c in r.Cards.Where(c=>!r.Selected.Any(x=>x.CardId==c.CardId)))Add("card","选择【"+CardCatalog.Get(c.Kind).DisplayName+"】",[c.CardId]);
            if(r.Selected.Count==r.Required)Add("pay",r.Branch=="debt"?"弃置所选牌":"支付睦阵原始牌");
        }
        else if(r.Stage==XiangChongStage.ClaimChoice)
        {
            var zone=r.Branch=="gift"?CardZoneKind.Equipment:CardZoneKind.Hand;
            foreach(var raw in BuildOwnedCardPaymentChoices(f.Id,f.OwnerSeat,r.Recipient,[zone],OwnedCardMoveIntent.Obtain))
            {var a=raw.Parameters.ToDictionary(x=>x.Key,x=>x.Value);a["program-action"]="xiang-chong";a["branch"]="claim";choices.Add(new(new($"xiang.{f.Id}.claim.{choices.Count}"),raw.Label,raw.Cards,raw.Targets,a));}
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishXiang(ProgramSkillFrame f)
    {
        var r=f.XiangChong!;var choices=XiangChoices(f);if(choices.Count==0){FinishXiang(f,"no-current-legal-choice");return;}
        var actor=r.Stage==XiangChongStage.Choice?r.TurnOwner:f.OwnerSeat;var skill=_contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});_pendingDecision=new(DecisionKind.ProgramTrigger,actor,"向宠：选择当前实际结算项。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=true,TargetSeat=actor,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[actor].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("xiang-chong")]private void AnswerXiang(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{XiangChong:{}r}f||_pendingDecision?.PlayerSeat!=(r.Stage==XiangChongStage.Choice?r.TurnOwner:f.OwnerSeat)||!XiangChoices(f).Any(c=>AssistedChoicesEqual([c],[choice])))throw new InvalidOperationException("XiangChong requires its exact current chooser and frozen private card frontier.");
        AssertXiang(f);ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});var branch=choice.Parameters["branch"];
        if(branch=="target")
        {
            var target=choice.Targets.Single();var cards=XiangOwned(f.OwnerSeat,f).Where(c=>r.Branch=="gift"||EquipmentCatalog.IsEquipment(c.Kind)&&CanPlaceOwnedEquipment(f.OwnerSeat,target,GetAdvancedCard(c.CardId),c.From)).ToArray();
            ReplaceRuntimeTop(f=f with{XiangChong=r with{Recipient=target,Cards=cards,Stage=XiangChongStage.Cards}});PublishXiang(f);return;
        }
        if(branch=="card")
        {var c=r.Cards.Single(c=>c.CardId==choice.Cards.Single());ReplaceRuntimeTop(f=f with{XiangChong=r with{Selected=r.Selected.Append(c).ToArray()}});PublishXiang(f);return;}
        if(branch=="give")
        {var cards=XiangOwned(r.TurnOwner);var c=cards[_random.Next(cards.Count)];PayXiang(f,[c],CardLocation.Hand(f.OwnerSeat),XiangGift,"compensation");ContinueXiang(f.Id);return;}
        if(branch=="return")
        {
            if(EquipmentCatalog.IsEquipment(r.Lost!.Kind))
            {ReplaceRuntimeTop(f=f with{XiangChong=r with{Stage=XiangChongStage.EquipmentReady}});ContinueXiang(f.Id);return;}
            PayXiang(f,[r.Lost with{From=r.LossDestination!.Value}],CardLocation.Hand(f.OwnerSeat),XiangReturn,"return");ContinueXiang(f.Id);return;
        }
        if(branch=="claim")
        {
            var zone=Enum.Parse<CardZoneKind>(choice.Parameters["source-zone"]);var slot=int.Parse(choice.Parameters["slot-index"],CultureInfo.InvariantCulture);var from=new CardLocation(zone,r.Recipient);var card=_cardZones.CardsAt(from)[slot];
            PayXiang(f,[new(card.Id,card.Kind,from)],CardLocation.Hand(f.OwnerSeat),MuzhenClaim,"claim");ContinueXiang(f.Id);return;
        }
        if(branch!="pay"||r.Selected.Count!=r.Required||r.Selected.Any(c=>_cardZones.GetLocation(c.CardId)!=c.From))throw new InvalidOperationException("XiangChong cannot pay another count or substituted original material.");
        if(r.Branch=="place")PlaceMuzhen(f);else PayXiang(f,r.Selected,r.Branch=="debt"?CardLocation.DiscardPile:CardLocation.Hand(r.Recipient),r.Branch=="debt"?XiangDebt:MuzhenGift,"payment");ContinueXiang(f.Id);
    }
    private void PayXiang(ProgramSkillFrame f,IReadOnlyList<XiangChongCard> cards,CardLocation to,string reason,string segment)
    {
        var r=f.XiangChong!;var first=(r.Branch is"gift"or"place")&&segment=="payment";
        var grain=cards.Where(c=>c.Kind==CardKind.WoodenOx&&c.From is{Zone:CardZoneKind.Equipment,OwnerSeat:not null}).SelectMany(c=>GetWoodenOxGrain(_players[c.From.OwnerSeat!.Value]).Select(x=>new XiangChongCard(x.Id,x.Kind,CardLocation.WoodenOxGrain(c.From.OwnerSeat.Value)))).ToArray();
        ReplaceRuntimeTop(f=f with{XiangChong=r with{Paid=true,Stage=segment=="claim"?XiangChongStage.ClaimChildren:XiangChongStage.PaymentChildren,Before=_movementSequence,After=_movementSequence,Payments=cards,Auxiliaries=grain,Replaced=null}});
        MoveProgramCardsFromMultipleSources(cards.Select(c=>c.CardId).ToArray(),to,new(reason),(_,movements)=>
        {
            var current=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(current with{XiangChong=current.XiangChong! with{After=_movementSequence,BatchId=_activeCardMovementBatchIds.Peek()}});
            foreach(var m in movements)
            {
                AdvanceEventRulesAndQueueFact(new XiangChongEntityMovedEvent(f.Id,segment,m.CardId,m.CardKind,m.From,m.To,m.Sequence));
                if(segment=="return"&&m.From==CardLocation.Processing&&f.WindowContext?.MovementBatch is{ParentFrameId:{}useId}lossBatch&&LifecycleCardUse(useId)is{Action:{Type:CardActionType.Use}action}use&&
                   action.PhysicalCards.Any(c=>c.CardId==m.CardId&&c.From==r.Lost!.From&&c.CardKind==m.CardKind)&&lossBatch.Movements.Any(x=>x.CardId==m.CardId&&x.Sequence==r.LossSequence&&x.To==CardLocation.Processing))
                    AdvanceEventRulesAndQueueFact(new XiangChongUseMaterialClaimedEvent(use.Id,action.ActionId,f.Id,r.Source,r.GameplayHash,m.CardId,m.CardKind,f.OwnerSeat,r.LossSequence,m.Sequence));
            }
        });
        f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{XiangChong=f.XiangChong! with{After=_movementSequence}});
        if(first)AdvanceEventRulesAndQueueFact(new XiangChongBranchPaidEvent(f.Id,f.OwnerSeat,r.Recipient,r.Branch,r.PhaseToken,f.XiangChong!.Before,f.XiangChong.After));
    }
    private void PlaceMuzhen(ProgramSkillFrame f)
    {
        var r=f.XiangChong!;var c=r.Selected.Single();var card=GetAdvancedCard(c.CardId);if(card.IsGeneralWeapon||!CanPlaceOwnedEquipment(f.OwnerSeat,r.Recipient,card,c.From))throw new InvalidOperationException("Muzhen's true equipment placement is no longer legal.");
        var slot=EquipmentCatalog.Get(card.Kind).Slot;var equipped=GetEquipment(r.Recipient).Where(x=>EquipmentCatalog.Get(x.Kind).Slot==slot).ToArray();var old=equipped.Length>=_players[r.Recipient].EquipmentSlotCapacity(slot)?equipped[0]:null;
        var all=r.Selected.Concat(old is null?Array.Empty<XiangChongCard>():[new(old.Id,old.Kind,CardLocation.Equipment(r.Recipient))]);
        var grain=all.Where(x=>x.Kind==CardKind.WoodenOx&&x.From.OwnerSeat is not null&&x.From.Zone==CardZoneKind.Equipment).SelectMany(x=>GetWoodenOxGrain(_players[x.From.OwnerSeat!.Value]).Select(y=>new XiangChongCard(y.Id,y.Kind,CardLocation.WoodenOxGrain(x.From.OwnerSeat.Value)))).ToArray();
        ReplaceRuntimeTop(f=f with{XiangChong=r with{Paid=true,Stage=XiangChongStage.PaymentChildren,Before=_movementSequence,After=_movementSequence,Payments=r.Selected,Replaced=old is null?null:new(old.Id,old.Kind,CardLocation.Equipment(r.Recipient)),Auxiliaries=grain}});
        if(old is not null){CopyFirstReplacedWeapon(card,old);MoveCard(old,CardLocation.Equipment(r.Recipient),CardLocation.DiscardPile,CardMoveReasons.EquipmentReplace);}
        MoveCard(card,c.From,CardLocation.Equipment(r.Recipient),CardMoveReasons.EquipmentEnter);
        f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{XiangChong=f.XiangChong! with{After=_movementSequence}});
        AdvanceEventRulesAndQueueFact(new EquipmentChangedEvent(f.Id,r.Recipient,slot,card.Id,card.Kind,old?.Id));
        foreach(var m in _cardMovements.Where(m=>m.Sequence>f.XiangChong!.Before&&m.Sequence<=_movementSequence))AdvanceEventRulesAndQueueFact(new XiangChongEntityMovedEvent(f.Id,"placement",m.CardId,m.CardKind,m.From,m.To,m.Sequence));
        AdvanceEventRulesAndQueueFact(new XiangChongBranchPaidEvent(f.Id,f.OwnerSeat,r.Recipient,r.Branch,r.PhaseToken,f.XiangChong!.Before,f.XiangChong.After));
    }
    private bool DrainXiang(long id)=>TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.Program)||TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.Program)||TryBeginCardsMovedProgramWindow(id)||TryBeginAdvancedSkillsChanged(id);
    private void ContinueXiang(long id)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{XiangChong:{}r}f||f.Id!=id)return;AssertXiang(f);
        if(r.Stage is XiangChongStage.PaymentChildren or XiangChongStage.ClaimChildren or XiangChongStage.NativeChildren)
        {
            if(DrainXiang(id))return;f=GetActiveProgramFrame(id);r=f.XiangChong!;
            if(r.Stage==XiangChongStage.PaymentChildren&&(r.Branch is"gift"or"place")&&_winner==Winner.None&&_players[f.OwnerSeat].IsAlive&&_players[r.Recipient].IsAlive)
            {ReplaceRuntimeTop(f=f with{XiangChong=r with{Stage=XiangChongStage.ClaimChoice}});PublishXiang(f);return;}
            FinishXiang(f,"paid-children-finished");return;
        }
        if(_winner!=Winner.None||!_players[f.OwnerSeat].IsAlive){FinishXiang(f,"owner-dead-or-game-ended");return;}
        if(r.Stage==XiangChongStage.EquipmentReady)
        {
            if(!XiangLostStillAvailable(f)||!CanIssueXiangChongGuyingEquipment(f,r.Lost!.CardId,r.LossDestination!.Value)){FinishXiang(f,"original-equipment-no-longer-executable");return;}
            var origin=new XiangChongEquipmentOrigin(f.Id,_resolutionSequence+1,r.Lost!.CardId,r.Lost.Kind,f.OwnerSeat,r.LossDestination!.Value);
            var slot=EquipmentCatalog.Get(origin.CardKind).Slot;var eq=GetEquipment(f.OwnerSeat).Where(c=>EquipmentCatalog.Get(c.Kind).Slot==slot).ToArray();var old=eq.Length>=_players[f.OwnerSeat].EquipmentSlotCapacity(slot)?eq[0]:null;
            var grain=old?.Kind==CardKind.WoodenOx?GetWoodenOxGrain(_players[f.OwnerSeat]).Select(c=>new XiangChongCard(c.Id,c.Kind,CardLocation.WoodenOxGrain(f.OwnerSeat))).ToArray():Array.Empty<XiangChongCard>();
            ReplaceRuntimeTop(f=f with{XiangChong=r with{Stage=XiangChongStage.EquipmentUse,Equipment=origin,Before=_movementSequence,After=_movementSequence,Replaced=old is null?null:new(old.Id,old.Kind,CardLocation.Equipment(f.OwnerSeat)),Auxiliaries=grain}});AdvanceEventRulesAndQueueFact(new XiangChongEquipmentIssuedEvent(origin));BeginXiangChongGuyingEquipment(f,origin);return;
        }
        if(r.Stage!=XiangChongStage.EquipmentUse&&_pendingDecision is null)PublishXiang(f);
    }
    private bool MatchesXiangChongEquipment(ProgramSkillFrame f,XiangChongEquipmentOrigin origin)=>f.XiangChong is{Stage:XiangChongStage.EquipmentUse,Equipment:{}stored}r&&stored==origin&&origin.ProgramFrameId==f.Id&&origin.ActorSeat==f.OwnerSeat&&r.Lost?.CardId==origin.CardId&&r.Lost.Kind==origin.CardKind&&r.LossDestination==origin.From&&ValidXiang(f);
    private void FinishXiangChongEquipment(ProgramSkillFrame f,bool issued)
    {
        if(f.XiangChong?.Equipment is not{}o||!MatchesXiangChongEquipment(f,o))throw new InvalidOperationException("Guying equipment lost its original native return.");
        if(issued)CaptureXiangChongEquipmentMaterialEntry(f);
        AdvanceEventRulesAndQueueFact(new XiangChongEquipmentReturnedEvent(o,issued));ReplaceRuntimeTop(f with{XiangChong=f.XiangChong with{Stage=XiangChongStage.NativeChildren,NativeCompleted=true,After=_movementSequence}});ContinueXiang(f.Id);
    }
    [NativeUseReturn(27140)]private void ReturnXiangEquipment(CardUseFrame use)
    {if(use.XiangChongGuyingEquipment is{}o){CaptureXiangChongEquipmentResult(use);FinishXiangChongEquipment(GetActiveProgramFrame(o.ProgramFrameId),true);}}
    private void FinishXiang(ProgramSkillFrame f,string reason)
    {AdvanceEventRulesAndQueueFact(new XiangChongCompletedEvent(f.Id,f.XiangChong!.Branch,f.XiangChong.Paid,reason));ReplaceRuntimeTop(f=f with{XiangChong=null});FinishProgramSkill(f,true);}
    [ProgramContinuation(ProgramContinuationKind.Resume,27140)]private bool ResumeXiang(long id)
    {if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{XiangChong:not null}f||f.Id!=id)return false;ContinueXiang(id);return true;}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27140)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27140)]private bool ContinuePaidXiang(ProgramSkillFrame f)=>f.XiangChong is{Paid:true}&&ValidXiang(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveGuyingAtomicLossChoice,SkillProgramEffectOp.SettleGuyingPreparationDebt,SkillProgramEffectOp.ResolveMuzhenExchange)]
    private PromptChoice AiXiang(PendingDecision p,ProgramSkillFrame f)
    {
        var r=f.XiangChong!;if(r.Stage==XiangChongStage.Choice)return p.Choices.FirstOrDefault(c=>c.Parameters["branch"]=="give")??p.Choices[0];
        if(r.Stage==XiangChongStage.Cards)return p.Choices.FirstOrDefault(c=>c.Parameters["branch"]=="pay")??p.Choices.Where(c=>c.Cards.Count==1).OrderBy(c=>GetKeepValue(GetAdvancedCard(c.Cards[0]),_players[f.OwnerSeat])).First();return p.Choices[0];
    }
}
