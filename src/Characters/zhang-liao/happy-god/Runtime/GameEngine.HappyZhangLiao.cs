using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string HappyZlTake = "skill-program.happy-duorui.take";
    private const string HappyZlDiscard = "skill-program.happy-zhiti.discard";
    private const string HappyZlDraw = "skill-program.happy-duorui.draw";
    private bool HasHappyZl => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveHappyZhangLiaoDuorui);
    private static CardConversionSource HappyZlSource(ProgramSkillFrame f) => new(f.SkillId,f.TriggerId!,f.OwnerSeat,f.SkillInstanceId);
    [ProgramCapability] private sealed class HappyZhangLiaoHost(GameEngine e) : IHappyZhangLiaoHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame f,SkillProgramEffectOp op) => e.BeginHappyZl(f,op); }
    private int HappyZlScore() => _players.Count(p=>p.IsAlive&&p.Hp<p.MaxHp)+_players.Where(p=>p.IsAlive).Sum(p=>AnnZlAbolished(p.Seat));
    private IEnumerable<RuleQueryContribution> HappyZlNumericContributions(CharacterState player,SkillRuleQuery query) =>
        query == SkillRuleQuery.SlashLimit && player.IsAlive && HasProgramSkill(player,"happy:zhiti") && HappyZlScore()>2
            ? [new FiniteRuleQueryContribution("happy:zhiti",SkillRuleOperation.Add,1)] : [];
    private IEnumerable<HappyZlLeaseEvent> ActiveHappyZlLeases(int owner,int target) => ProgramEventHistory<HappyZlLeaseEvent>()
        .Where(e=>e.Source.OwnerSeat==owner&&e.Target==target&&!ProgramEventHistory<HappyZlLeaseExpiredEvent>().Any(x=>x.LeaseFrameId==e.FrameId));
    private bool HasHappyZlSourceRestoration(int source,int target)=>HasHappyZl&&IsValidPlayerSeat(source)&&IsValidPlayerSeat(target)&&_players[target].IsAlive&&
        HasProgramSkill(_players[target],"happy:duorui")&&ActiveHappyZlLeases(target,source).Any();
    private HappyZlColorEvent? HappyZlColor(int owner,int target,bool? color) => color is null || owner==target ? null : ProgramEventHistory<HappyZlColorEvent>()
        .LastOrDefault(e=>e.Source.OwnerSeat==owner&&e.Target==target&&e.Turn==_turnNumber&&e.Color==color&&
            ProgramEventHistory<HappyZlStartedEvent>().Any(s=>s.FrameId==e.FrameId&&s.Source==e.Source&&s.Turn==e.Turn&&s.Op==SkillProgramEffectOp.ResolveHappyZhangLiaoDuorui));
    private CardUseFrame? HappyZlDamageUse(int owner,ProgramSkillWindowContext w)
    {
        if (w is not {Window:SkillProgramTriggerWindow.AfterDamageApplied,Amount:>0,SourceSeat:{}source,TargetSeat:{}target} || source!=owner ||
            ActiveDamageTrigger is not {} damage || damage.Id!=w.ParentFrameId || damage.ParentFrameId!=w.DamageFrameId) return null;
        var attack=GetDamageTriggerAttack(damage);
        if(attack.IsSourceLess||!attack.DamageWasApplied||attack.SourceSeat!=owner||attack.TargetSeat!=target||attack.CardUserSeat!=owner) return null;
        return LifecycleCardUse(attack.ResolutionId) is {Action:{} a} use && a.ActorSeat==owner && HappyZlColor(owner,target,a.EffectiveIsRed??SuitColor(a.EffectiveSuit)) is not null ? use : null;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,40980,SkillProgramEffectOp.ResolveHappyZhangLiaoDuorui,SkillProgramEffectOp.ResolveHappyZhangLiaoDuoruiDamage,SkillProgramEffectOp.ResolveHappyZhangLiaoZhiti)]
    private bool HappyZlEligible(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext w) => t.Id switch
    {
        "take-hand" => _players.Any(p=>p.IsAlive&&p.Seat!=c.OwnerSeat&&GetHand(p).Count>0),
        "colored-damage" => w.TargetSeat is {} target && _players[target].IsAlive && AnnZlAvailable(target).Any() && HappyZlDamageUse(c.OwnerSeat,w) is not null,
        "restore-source" => w.Window==SkillProgramTriggerWindow.BeforeDamageApplied&&w.TargetSeat==c.OwnerSeat&&w.SourceSeat is {} source&&IsValidPlayerSeat(source)&&
            CurrentDamageAttempt is{IsSourceLess:false,DamageAmount:>0} attack&&attack.SourceSeat==source&&attack.TargetSeat==c.OwnerSeat&&ActiveHappyZlLeases(c.OwnerSeat,source).Any(),
        "wounded-slots-draw" => HappyZlScore()>1,
        "wounded-slots-discard" => _currentSeat!=c.OwnerSeat&&_players[_currentSeat].IsAlive&&HappyZlScore()>3&&AnnZlAbolished(_currentSeat)>0,
        _ => false
    };
    private SkillProgramStepOutcome BeginHappyZl(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(f.HappyZhangLiao is not null||f.InstructionIndex!=1||f.WindowContext is not {} w) throw new InvalidOperationException("Happy Zhang Liao requires his original trigger frame.");
        if(op==SkillProgramEffectOp.ResolveHappyZhangLiaoZhiti&&w.Window==SkillProgramTriggerWindow.DrawPhaseStarting)
        { AdjustProgramNormalDraw(f,1);return SkillProgramStepOutcome.Continue; }
        var r=new HappyZlReceipt(HappyZlSource(f),f.GameplayHash,op,w.ParentFrameId,_turnNumber);
        if(op==SkillProgramEffectOp.ResolveHappyZhangLiaoDuorui) r=r with{Stage=HappyZlStage.Target};
        else if(op==SkillProgramEffectOp.ResolveHappyZhangLiaoZhiti) r=r with{Stage=HappyZlStage.Discard,Target=_currentSeat,Count=AnnZlAbolished(_currentSeat)};
        else if(f.TriggerId=="restore-source")
        {
            if(w.Window!=SkillProgramTriggerWindow.BeforeDamageApplied||w.TargetSeat!=f.OwnerSeat||w.SourceSeat is not{}source||
                CurrentDamageAttempt is not{IsSourceLess:false,DamageAmount:>0} attack||attack.SourceSeat!=source||attack.TargetSeat!=f.OwnerSeat)
                throw new InvalidOperationException("Happy Duorui restoration requires the exact positive sourced damage attempt before HP changes.");
            r=r with{Stage=HappyZlStage.Restoring,Target=source,Restore=ActiveHappyZlLeases(f.OwnerSeat,source).ToArray()};
        }
        else
        {
            var target=w.TargetSeat!.Value;var slots=AnnZlAvailable(target).ToArray();
            if(!_players[target].IsAlive||slots.Length==0)return SkillProgramStepOutcome.Continue;
            var before=_random.State;var slot=slots[_random.Next(slots.Length)];
            r=r with{Stage=HappyZlStage.SlotChildren,Target=target,Count=1,Slot=slot,Capacity=_players[target].EquipmentSlotCapacity(slot),RandomSlots=slots,RandomBefore=before,RandomAfter=_random.State};
        }
        ReplaceRuntimeTop(f=f with{HappyZhangLiao=r});
        AdvanceEventRulesAndQueueFact(new HappyZlStartedEvent(f.Id,r.Source,r.Hash,op,r.Parent,r.Turn));
        if(r.Stage==HappyZlStage.SlotChildren)
        {
            var before=_movementSequence;ReplaceRuntimeTop(f with{PendingMovementContinuation=new(f.OwnerSeat,0,null)});
            SetEquipmentSlotCapacity(_players[r.Target],r.Slot!.Value,0);
            AdvanceEventRulesAndQueueFact(new HappyZlLeaseEvent(f.Id,r.Source,r.Target,r.Slot.Value,r.Capacity));
            SaveHappyZlPayment(f.Id,before);
        }
        ResumeHappyZl(f.Id);return SkillProgramStepOutcome.AwaitChild;
    }
    [ProgramContinuation(ProgramContinuationKind.Resume,40980)] private bool ResumeHappyZl(long id)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{HappyZhangLiao:not null} f||f.Id!=id)return false;
        RequireHappyZl(f);var r=f.HappyZhangLiao!;
        if(r.Stage==HappyZlStage.Drawing)return true;
        if(r.Stage is HappyZlStage.GainChildren or HappyZlStage.SlotChildren or HappyZlStage.RestoreChildren or HappyZlStage.DrawChildren or HappyZlStage.DiscardChildren)
        {
            if(DrainAnnZl(f))return true;
            f=GetActiveProgramFrame(id);r=f.HappyZhangLiao!;
            var next=r.Stage switch {HappyZlStage.RestoreChildren=>HappyZlStage.Restoring,HappyZlStage.DiscardChildren=>HappyZlStage.Discard,_=>HappyZlStage.Finished};
            ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null,HappyZhangLiao=r=r with{Stage=next}});
        }
        if(r.Stage==HappyZlStage.Restoring)
        {
            while(r.Cursor<r.Restore.Count)
            {
                var lease=r.Restore[r.Cursor];var active=ActiveHappyZlLeases(f.OwnerSeat,r.Target).Contains(lease)&&_players[r.Target].EquipmentSlotCapacity(lease.Slot)==0&&!_players[r.Target].EquipmentAreaAbolished;
                r=r with{Cursor=r.Cursor+1,Restored=r.Restored+(active?1:0)};
                ReplaceRuntimeTop(f=f with{HappyZhangLiao=r});
                if(!active)continue;
                var before=_movementSequence;
                ReplaceRuntimeTop(f with{HappyZhangLiao=r with{Stage=HappyZlStage.RestoreChildren},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
                AdvanceEventRulesAndQueueFact(new HappyZlLeaseExpiredEvent(lease.FrameId,lease.Target,lease.Slot,"damage-source-restored"));
                SetEquipmentSlotCapacity(_players[r.Target],lease.Slot,lease.Capacity);
                AdvanceEventRulesAndQueueFact(new HappyZlRestoredEvent(f.Id,lease.FrameId,lease.Target,lease.Slot));
                SaveHappyZlPayment(id,before);return ResumeHappyZl(id);
            }
            if(r.Restored>0&&_players[f.OwnerSeat].IsAlive)
            {
                ReplaceRuntimeTop(f=f with{HappyZhangLiao=r with{Stage=HappyZlStage.Drawing,Count=r.Restored}});
                BeginOperationProgramDraw(f,f.OwnerSeat,r.Restored,new(HappyZlDraw));return true;
            }
            FinishHappyZl(f);return true;
        }
        if(r.Stage==HappyZlStage.Finished||_winner!=Winner.None||!_players[f.OwnerSeat].IsAlive||
            r.Target>=0&&!_players[r.Target].IsAlive||r.Stage==HappyZlStage.Discard&&r.Cursor>=r.Count)
        {FinishHappyZl(f);return true;}
        var choices=HappyZlChoices(f);if(choices.Count==0){FinishHappyZl(f);return true;}
        var skill=_contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,skill.Name+"：请选择。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=r.Stage==HappyZlStage.Hand,Choices=Array.AsReadOnly(choices.ToArray()),SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();return true;
    }
    private IReadOnlyList<PromptChoice> HappyZlChoices(ProgramSkillFrame f)
    {
        var r=f.HappyZhangLiao!;
        PromptChoice C(string option,string label,IReadOnlyList<int> cards,IReadOnlyList<int> targets,IReadOnlyDictionary<string,string>? extra=null)=>new(new("happy-zhang-liao."+f.Id+"."+r.Stage+"."+r.Cursor+"."+option),label,cards,targets,new Dictionary<string,string>(extra??new Dictionary<string,string>()){["program-action"]="happy-zhang-liao",["option"]=option,["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture)});
        if(r.Stage==HappyZlStage.Target)return _players.Where(p=>p.IsAlive&&p.Seat!=f.OwnerSeat&&GetHand(p).Count>0).Select(p=>C("target:"+p.Seat,"观看并获得"+p.Name+"一张手牌",[],[p.Seat])).ToArray();
        if(r.Stage==HappyZlStage.Hand)
            return BuildOwnedCardPaymentChoices(f.Id,f.OwnerSeat,r.Target,[CardZoneKind.Hand],OwnedCardMoveIntent.Obtain).Select(c=>
            { var slot=int.Parse(c.Parameters["slot-index"],CultureInfo.InvariantCulture);var card=GetHand(_players[r.Target])[slot];return r.View.Contains(card.Id)?C("take:"+slot,"获得【"+card.DisplayName+"】",[card.Id],[r.Target],c.Parameters):null; }).OfType<PromptChoice>().ToArray();
        if(r.Stage==HappyZlStage.Discard)return BuildOwnedCardPaymentChoices(f.Id,f.OwnerSeat,r.Target,[CardZoneKind.Hand,CardZoneKind.Equipment,CardZoneKind.Judgment],OwnedCardMoveIntent.Discard).Select((c,i)=>C("discard:"+i,c.Description,c.Cards,[r.Target],c.Parameters)).ToArray();
        return [];
    }
    [ProgramAnswer("happy-zhang-liao")] private void AnswerHappyZl(PromptChoice chosen)
    {
        var f=_resolutionStack.LastOrDefault() as ProgramSkillFrame??throw new InvalidOperationException("Happy Zhang Liao lost choice owner.");RequireHappyZl(f);var r=f.HappyZhangLiao!;
        if(_pendingDecision?.PlayerSeat!=f.OwnerSeat||!AssistedChoicesEqual([chosen],HappyZlChoices(f).Where(c=>c.Id==chosen.Id).ToArray()))throw new InvalidOperationException("Happy Zhang Liao requires his exact current dynamic card choice.");
        ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});
        if(r.Stage==HappyZlStage.Target)
        {
            var target=chosen.Targets.Single();ReplaceRuntimeTop(f with{HappyZhangLiao=r with{Target=target,Stage=HappyZlStage.Hand,View=GetHand(_players[target]).Select(c=>c.Id).ToArray()}});ResumeHappyZl(f.Id);return;
        }
        var zone=Enum.Parse<CardZoneKind>(chosen.Parameters["source-zone"]);var slot=int.Parse(chosen.Parameters["slot-index"],CultureInfo.InvariantCulture);var card=_cardZones.CardsAt(new(zone,r.Target))[slot];var before=_movementSequence;
        if(r.Stage==HappyZlStage.Hand)
        {
            var color=SuitColor(EffectiveSuit(_players[r.Target],card));
            ReplaceRuntimeTop(f with{HappyZhangLiao=r with{Stage=HappyZlStage.GainChildren,Card=card.Id,Color=color},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
            MoveProgramCardsFromMultipleSources([card.Id],CardLocation.Hand(f.OwnerSeat),new(HappyZlTake),NativeGiveCompletionRecords(f.Id,r.Target,f.OwnerSeat,[card.Id],before,(_,_)=>
            {
                SaveHappyZlPayment(f.Id,before);AdvanceEventRulesAndQueueFact(new HappyZlColorEvent(f.Id,r.Source,r.Turn,r.Target,color));
            }));
        }
        else
        {
            ReplaceRuntimeTop(f with{HappyZhangLiao=r with{Stage=HappyZlStage.DiscardChildren,Cursor=r.Cursor+1},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
            MoveProgramLogicalDiscard(f,f.OwnerSeat,[card.Id],new(HappyZlDiscard),(_,_)=>SaveHappyZlPayment(f.Id,before));
        }
        ResumeHappyZl(f.Id);
    }
    private void SaveHappyZlPayment(long id,long before)
    {
        var f=GetActiveProgramFrame(id);var r=f.HappyZhangLiao!;var ordinal=r.Payments.Count+1;
        var moves=_cardMovements.Where(m=>m.Sequence>before&&m.Sequence<=_movementSequence).ToArray();
        ReplaceRuntimeTop(f with{HappyZhangLiao=r with{Payments=r.Payments.Append(new(ordinal,before,_movementSequence){Moves=moves}).ToArray()}});
        AdvanceEventRulesAndQueueFact(new HappyZlPaidEvent(id,ordinal,before,_movementSequence));
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveHappyZhangLiaoDuoruiDamage)] private void ReturnHappyZlDraw(ProgramSkillFrame f,NativeDrawOutcome outcome)
    {
        if(f.HappyZhangLiao is not{Stage:HappyZlStage.Drawing,Draw:null}r||outcome.RecipientSeat!=f.OwnerSeat||outcome.RequestedCount!=r.Restored||outcome.SequenceAfter!=_movementSequence)throw new InvalidOperationException("Happy Duorui lost its exact once-paid native draw return.");
        ReplaceRuntimeTop(f with{HappyZhangLiao=r with{Stage=HappyZlStage.DrawChildren,Draw=outcome},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        SaveHappyZlPayment(f.Id,outcome.SequenceBefore);ResumeHappyZl(f.Id);
    }
    private void FinishHappyZl(ProgramSkillFrame f)
    {
        if(f.PendingMovementContinuation is not null)throw new InvalidOperationException("Happy Zhang Liao cannot finish before his typed movement returns.");
        ReplaceRuntimeTop(f=f with{HappyZhangLiao=f.HappyZhangLiao! with{Stage=HappyZlStage.Finished}});AdvanceEventRulesAndQueueFact(new HappyZlCompletedEvent(f.Id));FinishProgramSkill(f,true);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement,40980)] private bool ReturnHappyZlMovement(ProgramSkillFrame f)=>f.HappyZhangLiao is not null&&ResumeHappyZl(f.Id);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveHappyZhangLiaoDuorui)] private PromptChoice AiHappyZlTake(PendingDecision p,ProgramSkillFrame f)=>p.Choices.First();
    [ProgramAiChoice(SkillProgramEffectOp.ResolveHappyZhangLiaoDuoruiDamage)] private PromptChoice AiHappyZlDamage(PendingDecision p,ProgramSkillFrame f)=>p.Choices.First();
    [ProgramAiChoice(SkillProgramEffectOp.ResolveHappyZhangLiaoZhiti)] private PromptChoice AiHappyZlZhiti(PendingDecision p,ProgramSkillFrame f)=>p.Choices.First();
    [ProgramViewContributor(ProgramViewContribution.PrivateCards,40980)] private CardSnapshot[] HappyZlPrivateHand(int viewer)=>_resolutionStack.OfType<ProgramSkillFrame>().Where(f=>f.OwnerSeat==viewer&&f.HappyZhangLiao is{Stage:HappyZlStage.Hand}).SelectMany(f=>f.HappyZhangLiao!.View).Distinct().Select(id=>ToSnapshot(GetAdvancedCard(id))).ToArray();
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent,40980)] private void ObserveHappyZlSlots(IGameEvent payload)
    {
        if(!HasHappyZl||payload is not(EquipmentSlotCapacityChangedEvent or PlayerAreasAbolishedEvent{Equipment:true}))return;
        foreach(var lease in ProgramEventHistory<HappyZlLeaseEvent>().Where(e=>!ProgramEventHistory<HappyZlLeaseExpiredEvent>().Any(x=>x.LeaseFrameId==e.FrameId)).ToArray())
            if(payload is EquipmentSlotCapacityChangedEvent changed&&changed.Seat==lease.Target&&changed.Slot==lease.Slot||payload is PlayerAreasAbolishedEvent areas&&areas.Seat==lease.Target&&areas.Equipment)
                AdvanceEventRulesAndQueueFact(new HappyZlLeaseExpiredEvent(lease.FrameId,lease.Target,lease.Slot,"intervening-slot-change"));
    }
}
