using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string HappyPtAction="happy-pang-tong";
    [ProgramCapability]private sealed class HappyPangTongHost(GameEngine e):IHappyPangTongHost
    {public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginHappyPangTong(f,op);}
    private CardConversionSource HappyPtSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    private bool HasHappyPangTong=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.IssueHappyPangTongPlan);
    private HappyPangTongPlanIssuedEvent HappyPtPlan(long id)=>ProgramEventHistory<HappyPangTongPlanIssuedEvent>().Single(e=>e.PlanId==id);
    private IEnumerable<HappyPangTongPlanIssuedEvent> HappyPtOutstanding(int owner)=>ProgramEventHistory<HappyPangTongPlanIssuedEvent>()
        .Where(p=>p.Source.OwnerSeat==owner&&!ProgramEventHistory<HappyPangTongPlanSettledEvent>().Any(s=>s.PlanId==p.PlanId));
    private long[] HappyPtDue(int owner,ProgramSkillWindowContext context)
    {
        if(context.Window==SkillProgramTriggerWindow.CardUseTargetsFinalized&&context.CardUse is{}card&&
            card.ActorSeat==_currentSeat&&IsSlashCard(card.EffectiveKind)&&
            ProgramEventHistory<HappyPangTongSlashRecordedEvent>().Any(e=>e.ActualTurn==_turnNumber&&e.ActorSeat==card.ActorSeat&&e.UseId==card.ParentCardUseFrameId&&e.ActionId==card.CardActionId&&e.Ordinal==1))
            return HappyPtOutstanding(owner).Where(p=>p.Kind==HappyPangTongPlanKind.Middle&&p.TargetSeat==card.ActorSeat&&p.PlanId<card.ParentCardUseFrameId&&p.IssuedActionBoundary<card.CardActionId).Select(p=>p.PlanId).Order().ToArray();
        if(context.Window==SkillProgramTriggerWindow.AfterTurnEnded&&context.TargetSeat is{}actor&&
            _resolutionStack.OfType<DeferredTurnEndFrame>().SingleOrDefault(p=>p.Id==context.ParentFrameId) is{}parent&&IsActualAfterTurnEndedParent(parent)&&parent.OwnerSeat==actor)
            return HappyPtOutstanding(owner).Where(p=>p.TargetSeat==actor&&p.PlanId<parent.Id).Select(p=>p.PlanId).Order().ToArray();
        return [];
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,42180)]
    private bool CanOfferHappyPangTong(ProgramTriggerLegalityContext g)
    {
        if(g.SkillId is not(HappyPangTongComposition.Lunce or HappyPangTongComposition.Lanhai))return true;
        if(g.WindowContext is not{}context)return false;
        return g.Trigger.Id switch
        {
            "round-plan"=>IsHappyPtActualRoundStart(g.Candidate,context),
            "first-slash-plan" or "ended-plan"=>HappyPtDue(g.OwnerSeat,context).Length>0,
            "settled-plan" or "renew-plan"=>CanRunHappyPangTongSettled(g.Candidate,context),
            _=>true
        };
    }
    private bool IsHappyPtActualRoundStart(ProgramTriggerCandidate candidate,ProgramSkillWindowContext context)
    {
        if(context.Window!=SkillProgramTriggerWindow.RoundStarting||context.SourceSeat!=_currentSeat||
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId) is not{}window)return false;
        var facts=CompleteProgramEventHistory().ToArray();
        var start=Array.FindLastIndex(facts,e=>e is RoundStartedEvent r&&r.RoundNumber==_roundNumber);
        if(start<0||facts[start] is not RoundStartedEvent round||round.ActorSeat!=context.SourceSeat)return false;
        var since=facts.Skip(start+1).ToArray();
        if(since.OfType<ProgramBindingResolvedEvent>().Any(e=>e.Window==SkillProgramTriggerWindow.RoundStarting&&e.OwnerSeat==candidate.OwnerSeat&&
            e.SkillId==candidate.SkillId&&e.BindingId==candidate.BindingId&&e.SkillInstanceId==candidate.SkillInstanceId))return false;
        var turns=since.OfType<TurnStartedEvent>().ToArray();
        return window.BeforeTurnPreludeProof is not null?turns.Length==0:turns is[var first]&&first.ActorSeat==round.ActorSeat&&first.TurnNumber==_turnNumber;
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,42180)]
    private void ObserveHappyPangTongTurnFacts(IGameEvent payload)
    {
        if(!HasHappyPangTong)return;
        if(payload is CardUseDeclaredEvent declared&&HasOngoingActualUseTurn&&declared.SourceSeat==_currentSeat&&IsSlashCard(declared.CardKind)&&
            LifecycleCardUse(declared.ResolutionId) is{Action:{Type:CardActionType.Use}action}use&&action.ActorSeat==declared.SourceSeat&&
            !ProgramEventHistory<HappyPangTongSlashRecordedEvent>().Any(e=>e.UseId==use.Id))
            AdvanceEventRulesAndQueueFact(new HappyPangTongSlashRecordedEvent(_turnNumber,_currentSeat,use.Id,action.ActionId,
                1+ProgramEventHistory<HappyPangTongSlashRecordedEvent>().Count(e=>e.ActualTurn==_turnNumber&&e.ActorSeat==_currentSeat)));
        if(payload is DamageAppliedEvent{Amount:>0,SourceLess:false}damage&&_turnNumber>0&&_resolutionStack.LastOrDefault() is DamageFrame d&&
            d.SourceSeat==damage.SourceSeat&&d.TargetSeat==damage.TargetSeat&&d.Amount==damage.Amount&&CurrentDamageAttempt is{}attack&&
            d.ParentFrameId==attack.ResolutionId&&attack.DamageWasApplied&&!ProgramEventHistory<HappyPangTongDamageRecordedEvent>().Any(e=>e.DamageId==d.Id))
            AdvanceEventRulesAndQueueFact(new HappyPangTongDamageRecordedEvent(_turnNumber,_currentSeat,d.Id,damage.SourceSeat,damage.Amount));
    }
    private SkillProgramStepOutcome BeginHappyPangTong(ProgramSkillFrame supplied,SkillProgramEffectOp operation)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(f.HappyPangTong is not null||f.InstructionIndex!=1||f.WindowContext is not{}context||!HappyPtOriginalCandidate(f))
            throw new InvalidOperationException("Lunce requires its exact original real window and unstarted frame.");
        var stage=operation==SkillProgramEffectOp.IssueHappyPangTongPlan?HappyPangTongStage.Grant:HappyPangTongStage.NextPlan;
        var receipt=new HappyPangTongReceipt(HappyPtSource(f),f.GameplayHash,operation,context.ParentFrameId,_turnNumber,stage);
        if(operation is SkillProgramEffectOp.ResolveHappyPangTongMiddle or SkillProgramEffectOp.ResolveHappyPangTongEnding)
        {
            receipt=receipt with{Plans=HappyPtDue(f.OwnerSeat,context)};
            if(receipt.Plans.Count==0)return SkillProgramStepOutcome.Continue;
        }
        ReplaceRuntimeTop(f=f with{HappyPangTong=receipt});
        AdvanceEventRulesAndQueueFact(new HappyPangTongStartedEvent(f.Id,receipt.Source,f.GameplayHash,receipt.ParentId,operation,_turnNumber));
        for(var ordinal=0;ordinal<receipt.Plans.Count;ordinal++)
            AdvanceEventRulesAndQueueFact(new HappyPangTongDueIssuedEvent(f.Id,ordinal,receipt.Plans[ordinal],receipt.ParentId));
        if(operation==SkillProgramEffectOp.IssueHappyPangTongPlan)PublishHappyPt(f);
        else if(operation==SkillProgramEffectOp.ResolveHappyPangTongLanhai)BeginHappyPtReward(f);
        else ContinueHappyPt(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private PromptChoice HappyPtChoice(ProgramSkillFrame f,string mode,string text,IReadOnlyList<int>? targets=null,string? value=null)=>
        new(new($"happy-plan.{f.Id}.{f.HappyPangTong!.CurrentPlanId}.{f.HappyPangTong.Stage}.{mode}.{value}.{string.Join('-',targets??[])}"),text,[],targets??[],new Dictionary<string,string>
        {{"program-action",HappyPtAction},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)},{"mode",mode},{"value",value??""}});
    private IReadOnlyList<PromptChoice> HappyPtChoices(ProgramSkillFrame f)
    {
        var r=f.HappyPangTong!;var choices=new List<PromptChoice>();
        if(r.Stage==HappyPangTongStage.Grant)
            foreach(var kind in Enum.GetValues<HappyPangTongPlanKind>())foreach(var p in _players.Where(p=>p.IsAlive))
                choices.Add(HappyPtChoice(f,"grant",$"对{p.Name}定{(kind==HappyPangTongPlanKind.Upper?"上":kind==HappyPangTongPlanKind.Middle?"中":"下")}策",[p.Seat],kind.ToString()));
        if(r.Stage==HappyPangTongStage.LowerChoice)
        {choices.Add(HappyPtChoice(f,"lower","摸两张牌，并令定策角色回复1点体力"));choices.Add(HappyPtChoice(f,"decline","不发动下策"));}
        if(r.Stage==HappyPangTongStage.UpperTarget)
        {
            foreach(var seat in AssistedPhysicalSlashTargets(f.OwnerSeat))choices.Add(HappyPtChoice(f,"slash-target",$"选择对{_players[seat].Name}使用【杀】",[seat],seat.ToString(CultureInfo.InvariantCulture)));
            choices.Add(HappyPtChoice(f,"decline","不使用【杀】"));
        }
        if(r.Stage==HappyPangTongStage.UpperCard)
        {
            var view=f with{AssistedSlashRequest=new(f.OwnerSeat,r.SlashTarget,ActorChoosesTarget:true)};
            foreach(var c in NearestPhysicalSlashChoices(view))
            {
                var parameters=new Dictionary<string,string>(c.Parameters){{"mode",c.Parameters["request-option"]}};parameters["program-action"]=HappyPtAction;
                choices.Add(c with{Id=new($"happy-upper.{f.Id}.{r.CurrentPlanId}.{c.Id.Value}"),Parameters=parameters});
            }
        }
        if(r.Stage==HappyPangTongStage.MiddleCard)
        {
            var plan=HappyPtPlan(r.CurrentPlanId);var cardUse=f.WindowContext!.CardUse!;
            var seats=new[]{plan.TargetSeat}.Concat(cardUse.DesignatedTargetSeats??LifecycleCardUse(cardUse.ParentCardUseFrameId)?.TargetSeats??[]).Distinct();
            foreach(var seat in seats.Where(s=>_players[s].IsAlive))
            foreach(var c in BuildOwnedCardPaymentChoices(f.Id,f.OwnerSeat,seat,seat==f.OwnerSeat?[CardZoneKind.Equipment]:[CardZoneKind.Hand,CardZoneKind.Equipment],OwnedCardMoveIntent.Obtain))
            {
                var parameters=new Dictionary<string,string>(c.Parameters){{"mode","take"},{"card-owner",seat.ToString(CultureInfo.InvariantCulture)}};parameters["program-action"]=HappyPtAction;
                choices.Add(c with{Id=new($"happy-middle.{f.Id}.{r.CurrentPlanId}.{seat}.{c.Id.Value}"),Targets=Array.AsReadOnly(new[]{seat}),Parameters=parameters});
            }
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishHappyPt(ProgramSkillFrame f)
    {
        var choices=HappyPtChoices(f);
        if(choices.Count==0){if(f.HappyPangTong!.Stage==HappyPangTongStage.Grant)FinishHappyPt(f);else SettleHappyPt(f,false);return;}
        ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.AwaitingResponse});
        PublishParticipantHandChoice(f,f.OwnerSeat,choices,"【论策】请选择。",f.OwnerSeat);
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer(HappyPtAction)]private void AnswerHappyPt(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{HappyPangTong:{}r}f||!ValidHappyPt(f)||_pendingDecision?.PlayerSeat!=f.OwnerSeat||
            !AssistedChoicesEqual([choice],HappyPtChoices(f).Where(c=>c.Id==choice.Id).ToArray()))throw new InvalidOperationException("Lunce lost its exact current native menu or original plan.");
        ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});var mode=choice.Parameters["mode"];
        if(r.Stage==HappyPangTongStage.Grant)
        {
            AdvanceEventRulesAndQueueFact(new HappyPangTongPlanIssuedEvent(f.Id,r.Source,r.GameplayHash,Enum.Parse<HappyPangTongPlanKind>(choice.Parameters["value"]),choice.Targets.Single(),_turnNumber,_cardActionSequence));
            FinishHappyPt(f);return;
        }
        if(mode=="decline"){SettleHappyPt(f,false);return;}
        if(r.Stage==HappyPangTongStage.UpperTarget)
        {ReplaceRuntimeTop(f=f with{HappyPangTong=r with{Stage=HappyPangTongStage.UpperCard,SlashTarget=choice.Targets.Single()}});PublishHappyPt(f);return;}
        if(r.Stage==HappyPangTongStage.UpperCard)
        {
            if(mode=="faction")
            {
                ReplaceRuntimeTop(f=f with{HappyPangTong=r with{Stage=HappyPangTongStage.UpperUse,FactionPending=true}});
                BeginAssistedProgramFactionSlashRequest(f,f.OwnerSeat,r.SlashTarget,HappyPtUpperBind(f));return;
            }
            var actor=_players[f.OwnerSeat];var (cards,kind,conversion)=ReadAssistedSlashPayment(actor,choice);
            if(!NearestSlashPaymentHasRange(actor,r.SlashTarget,cards,conversion,kind)||!CanUseSlashTarget(actor,_players[r.SlashTarget],cards[0],conversion,kind,
                noEffectiveRank:cards.Count>1,specificEffectiveRank:choice.Parameters.GetValueOrDefault("equipment")==CardKind.ZhangbaSerpentSpear.ToString()?ZhangbaSpecificSlashRank(actor,cards):null,physicalCardIds:cards.Select(c=>c.Id).ToArray()))
                throw new InvalidOperationException("Upper plan Slash lost its real payment or legal target.");
            ReplaceRuntimeTop(f with{HappyPangTong=r with{Stage=HappyPangTongStage.UpperUse}});
            ResolveSlashCore(actor,_players[r.SlashTarget],cards[0],kind,actor.Seat,physicalCards:cards,countsTowardSlashLimit:false,
                usesZhuqueFan:kind==CardKind.FireSlash&&(cards[0].Kind==CardKind.Slash||IsOwnPublicPileSlashMaterial(actor,cards[0]))&&HasZhuqueFan(actor),conversionSource:conversion,programSkillCardUseFrameId:f.Id);return;
        }
        if(r.Stage==HappyPangTongStage.LowerChoice)
        {
            ReplaceRuntimeTop(f=f with{HappyPangTong=r with{Stage=HappyPangTongStage.LowerDrawing,LowerAccepted=true,DrawCount=2}});
            BeginOperationProgramDraw(f,f.OwnerSeat,2,new("skill-program.happy:lunce.lower-draw"));return;
        }
        if(r.Stage!=HappyPangTongStage.MiddleCard)throw new InvalidOperationException("Lunce cannot pay a resolved plan twice.");
        var source=int.Parse(choice.Parameters["card-owner"],CultureInfo.InvariantCulture);var zone=Enum.Parse<CardZoneKind>(choice.Parameters["source-zone"]);
        var from=new CardLocation(zone,source);var slot=int.Parse(choice.Parameters["slot-index"],CultureInfo.InvariantCulture);var card=_cardZones.CardsAt(from)[slot];
        f=HappyPtChildren(f,HappyPangTongStage.GainChildren);ReplaceRuntimeTop(f=f with{HappyPangTong=f.HappyPangTong! with{GainCardId=card.Id,GainFrom=from}});
        MoveCard(card,from,CardLocation.Hand(f.OwnerSeat),new("skill-program.happy:lunce.middle-gain"));
        f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f with{HappyPangTong=f.HappyPangTong! with{After=_movementSequence}});ContinueHappyPt(f.Id);
    }
    private ProgramSkillFrame HappyPtChildren(ProgramSkillFrame f,HappyPangTongStage stage)
    {
        f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{HappyPangTong=f.HappyPangTong! with{Stage=stage,Before=_movementSequence,After=_movementSequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});return f;
    }
    private bool DrainHappyPt(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||
        TryBeginAdvancedSkillsChanged(f.Id)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id);
    private void ContinueHappyPt(long id)
    {
        while(_pendingDecision is null&&_resolutionStack.LastOrDefault() is ProgramSkillFrame{HappyPangTong:{}r}f&&f.Id==id)
        {
            if(!ValidHappyPt(f))throw new InvalidOperationException("Lunce continuation lost its original plan, native candidate or already-paid return.");
            if(r.Stage is HappyPangTongStage.UpperUse)
            {
                if(r.FactionPending&&r.NativeUseId==0&&f.ChoiceBindings.SingleOrDefault(b=>b.Name==HappyPtUpperBind(f)) is{OptionId:"declined"}){SettleHappyPt(f,false);return;}
                return;
            }
            if(r.Stage is HappyPangTongStage.Settling or HappyPangTongStage.LowerDrawing or HappyPangTongStage.RewardDrawing)return;
            if(DrainHappyPt(f))return;
            f=GetActiveProgramFrame(id);r=f.HappyPangTong!;if(f.PendingMovementContinuation is not null)ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null});
            if(r.Stage==HappyPangTongStage.NextPlan)
            {
                if(r.Cursor>=r.Plans.Count||_winner!=Winner.None||!_players[f.OwnerSeat].IsAlive){FinishHappyPt(f);return;}
                var plan=HappyPtPlan(r.Plans[r.Cursor]);ReplaceRuntimeTop(f=f with{HappyPangTong=r=r with{CurrentPlanId=plan.PlanId,SlashTarget=-1,NativeUseId=0,NativeActionId=0,FactionPending=false,GainCardId=0,GainFrom=null,DrawOutcome=null,LowerAccepted=false}});
                if(r.Operation==SkillProgramEffectOp.ResolveHappyPangTongMiddle)
                {ReplaceRuntimeTop(f=f with{HappyPangTong=r with{Stage=HappyPangTongStage.MiddleCard}});PublishHappyPt(f);return;}
                var damaged=ProgramEventHistory<HappyPangTongDamageRecordedEvent>().Any(e=>e.ActualTurn==r.ActualTurn&&e.SourceSeat==plan.TargetSeat);
                if(plan.Kind==HappyPangTongPlanKind.Middle||plan.Kind==HappyPangTongPlanKind.Upper&&!damaged||plan.Kind==HappyPangTongPlanKind.Lower&&damaged){SettleHappyPt(f,false);return;}
                ReplaceRuntimeTop(f=f with{HappyPangTong=r with{Stage=plan.Kind==HappyPangTongPlanKind.Upper?HappyPangTongStage.UpperTarget:HappyPangTongStage.LowerChoice}});PublishHappyPt(f);return;
            }
            if(r.Stage==HappyPangTongStage.LowerDrawChildren)
            {
                f=HappyPtChildren(f,HappyPangTongStage.LowerRecoveryChildren);var target=HappyPtPlan(r.CurrentPlanId).TargetSeat;
                AdvanceEventRulesAndQueueFact(new HappyPangTongRecoveryIssuedEvent(f.Id,r.CurrentPlanId,target,1));
                if(_players[target].IsAlive)new ProgramSkillHost(this).Recover(f.Id,f.OwnerSeat,target,1,null,null);
                continue;
            }
            if(r.Stage is HappyPangTongStage.GainChildren or HappyPangTongStage.UpperReturned or HappyPangTongStage.LowerRecoveryChildren)
            {SettleHappyPt(f,true);return;}
            if(r.Stage==HappyPangTongStage.GrowthMaximumChildren)
            {f=HappyPtChildren(f,HappyPangTongStage.GrowthHpChildren);AdvanceEventRulesAndQueueFact(new HappyPangTongRecoveryIssuedEvent(f.Id,r.CurrentPlanId,f.OwnerSeat,1));new ProgramSkillHost(this).Recover(f.Id,f.OwnerSeat,f.OwnerSeat,1,null,null);continue;}
            if(r.Stage is HappyPangTongStage.RewardChildren or HappyPangTongStage.GrowthHpChildren){FinishHappyPt(f);return;}
            PublishHappyPt(f);return;
        }
    }
    private void SettleHappyPt(ProgramSkillFrame supplied,bool success)
    {
        var f=GetActiveProgramFrame(supplied.Id);var r=f.HappyPangTong!;var plan=HappyPtPlan(r.CurrentPlanId);
        if(ProgramEventHistory<HappyPangTongPlanSettledEvent>().Any(e=>e.PlanId==plan.PlanId))throw new InvalidOperationException("An independently issued plan may settle exactly once.");
        ReplaceRuntimeTop(f=f with{HappyPangTong=r with{Stage=HappyPangTongStage.Settling}});
        AdvanceEventRulesAndQueueFact(new HappyPangTongPlanSettledEvent(f.Id,plan.PlanId,f.OwnerSeat,plan.TargetSeat,plan.Kind,_turnNumber,_currentSeat,success));
        if(!BeginHappyPtSettled(f,success))ReturnHappyPtSettled(f.Id,plan.PlanId);
    }
    private void BeginHappyPtReward(ProgramSkillFrame f)
    {
        var w=_resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(p=>p.Id==f.WindowContext!.ParentFrameId);var ret=w.HappyPlanReturn!;
        ReplaceRuntimeTop(f=f with{HappyPangTong=f.HappyPangTong! with{CurrentPlanId=ret.PlanId}});
        if(ret.Success)
        {
            var count=ProgramEventHistory<HappyPangTongPlanSettledEvent>().Count(e=>e.OwnerSeat==f.OwnerSeat&&e.ActualTurn==_turnNumber&&e.Success);
            ReplaceRuntimeTop(f=f with{HappyPangTong=f.HappyPangTong! with{Stage=HappyPangTongStage.RewardDrawing,DrawCount=count}});
            AdvanceEventRulesAndQueueFact(new HappyPangTongBenefitIssuedEvent(f.Id,ret.PlanId,"draw",count,_players[f.OwnerSeat].MaxHp,_players[f.OwnerSeat].MaxHp));
            BeginOperationProgramDraw(f,f.OwnerSeat,count,new("skill-program.happy:lanhai.success-draw"));return;
        }
        var owner=_players[f.OwnerSeat];var before=owner.MaxHp;
        f=HappyPtChildren(f,HappyPangTongStage.GrowthMaximumChildren);
        var actualGrowth=ProgramEventHistory<HappyPangTongBenefitIssuedEvent>().Count(e=>e.Branch=="grow"&&e.MaximumAfter>e.MaximumBefore&&
            ProgramEventHistory<HappyPangTongStartedEvent>().Any(s=>s.FrameId==e.FrameId&&s.Source.OwnerSeat==f.OwnerSeat))<3;
        ReplaceRuntimeTop(f=f with{HappyPangTong=f.HappyPangTong! with{MaximumBefore=before,MaximumAfter=before+(actualGrowth?1:0)}});
        if(actualGrowth)new ProgramSkillHost(this).ChangeMaximumHp(f,1);
        f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f with{HappyPangTong=f.HappyPangTong! with{MaximumAfter=owner.MaxHp}});
        AdvanceEventRulesAndQueueFact(new HappyPangTongBenefitIssuedEvent(f.Id,ret.PlanId,"grow",1,before,owner.MaxHp));ContinueHappyPt(f.Id);
    }
    private void FinishHappyPt(ProgramSkillFrame f)
    {ReplaceRuntimeTop(f=f with{HappyPangTong=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    [ProgramContinuation(ProgramContinuationKind.Resume,42180)]private bool ResumeHappyPt(long id)
    {if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{HappyPangTong:not null}f||f.Id!=id)return false;ContinueHappyPt(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,42180)]private bool ReturnHappyPtMovement(ProgramSkillFrame f)=>f.HappyPangTong is not null&&ResumeHappyPt(f.Id);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveHappyPangTongEnding)]private void ReturnHappyPtLowerDraw(ProgramSkillFrame f,NativeDrawOutcome outcome)=>ReturnHappyPtDraw(f,outcome,HappyPangTongStage.LowerDrawing,HappyPangTongStage.LowerDrawChildren);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveHappyPangTongLanhai)]private void ReturnHappyPtRewardDraw(ProgramSkillFrame f,NativeDrawOutcome outcome)=>ReturnHappyPtDraw(f,outcome,HappyPangTongStage.RewardDrawing,HappyPangTongStage.RewardChildren);
    private void ReturnHappyPtDraw(ProgramSkillFrame supplied,NativeDrawOutcome outcome,HappyPangTongStage from,HappyPangTongStage to)
    {
        var f=GetActiveProgramFrame(supplied.Id);var r=f.HappyPangTong!;
        if(!ValidHappyPt(f)||r.Stage!=from||f.NativeDraw is not null||outcome.RecipientSeat!=f.OwnerSeat||outcome.RequestedCount!=r.DrawCount||outcome.SequenceAfter!=_movementSequence)
            throw new InvalidOperationException("Lunce/Lanhai lost its exact once-paid native draw.");
        ReplaceRuntimeTop(f=f with{HappyPangTong=r with{Stage=to,DrawOutcome=outcome,Before=outcome.SequenceBefore,After=outcome.SequenceAfter},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        AdvanceEventRulesAndQueueFact(new HappyPangTongDrawReturnedEvent(f.Id,r.CurrentPlanId,outcome.RequestedCount,outcome.ActualDrawIds.Count,outcome.SequenceBefore,outcome.SequenceAfter));ContinueHappyPt(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.IssueHappyPangTongPlan,SkillProgramEffectOp.ResolveHappyPangTongMiddle,SkillProgramEffectOp.ResolveHappyPangTongEnding)]
    private PromptChoice HappyPtAi(PendingDecision p,ProgramSkillFrame f)=>p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("mode")=="grant"&&c.Parameters.GetValueOrDefault("value")==HappyPangTongPlanKind.Lower.ToString()&&c.Targets.Contains(f.OwnerSeat))??
        p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("mode") is "lower" or "take" or "use")??p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("mode")=="decline")??p.Choices[0];
}
