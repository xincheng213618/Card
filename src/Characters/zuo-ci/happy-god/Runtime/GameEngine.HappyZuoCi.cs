using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string HappyZcAction="happy-zuo-ci";
    [ProgramCapability]private sealed class HappyZuoCiHost(GameEngine e):IHappyZuoCiHost
    {public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginHappyZuoCi(f,op);}
    private CardConversionSource HappyZcSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    private int HappyZcBonus(CharacterState owner)=>owner.HappyZuoCi is{BonusTurn:var turn,BonusUses:var count}&&turn==_turnNumber?count:0;
    private int HappyZcTransformCount(int owner)=>ProgramEventHistory<HappyZuoCiTransformedEvent>().Count(e=>e.OwnerSeat==owner&&
        ProgramEventHistory<HappyZuoCiStartedEvent>().Any(s=>s.FrameId==e.FrameId&&s.ActualTurn==_turnNumber));
    private IReadOnlyList<Card> HappyZcPhantomChoices(CharacterState owner)=>Array.AsReadOnly(GetHand(owner).Where(c=>HappyZcEntity(c.Id) is{}p&&p.Owner==owner.Seat&&!p.Entity.ManuallyTransformed&&
        HappyZcTemplates(owner,c.Id).Count>0).ToArray());
    private IReadOnlyList<Card> HappyZcTemplates(CharacterState owner,int phantomId)=>owner.HappyZuoCi is not{}state?[]:Array.AsReadOnly(GetHand(owner).Where(c=>
        c.Id!=phantomId&&!state.UsedTemplates.Contains(c.Id)&&(HappyZcEntity(c.Id) is null||HappyZcBonus(owner)>0)).ToArray());
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount,42600,SkillProgramEffectOp.TransformHappyZuoCiPhantom)]
    private bool CanTransformHappyZc(CharacterState owner)=>owner.HappyZuoCi is{}state&&HappyZcSourceProof(owner.Seat,state)&&
        HappyZcTransformCount(owner.Seat)<2+HappyZcBonus(owner)&&HappyZcPhantomChoices(owner).Count>0;
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount,42601,SkillProgramEffectOp.ResolveHappyZuoCiHuanjing)]
    private bool CanHuanjingHappyZc(CharacterState owner)=>owner.HappyZuoCi is{}state&&HappyZcSourceProof(owner.Seat,state)&&owner.HasHpValue;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,42600)]
    private bool CanOfferHappyZc(ProgramTriggerLegalityContext gate)
    {
        if(gate.SkillId!=HappyZuoCiComposition.Huanshu)return true;
        if(gate.WindowContext is not{}context)return false;
        if(gate.Trigger.Id=="initial-phantoms")return gate.Owner.HappyZuoCi is null;
        if(gate.Owner.HappyZuoCi is not{}state||!HappyZcSourceProof(gate.OwnerSeat,state))return false;
        if(gate.Trigger.Id=="round-phantoms")return IsHappyZcActualRoundStart(gate.Candidate,context);
        if(gate.Trigger.Id=="foreign-phantom")return IsHappyZuoCiForeignRewardCandidate(gate.Candidate,context);
        return true;
    }
    private bool IsHappyZcActualRoundStart(ProgramTriggerCandidate candidate,ProgramSkillWindowContext context)
    {
        if(context.Window!=SkillProgramTriggerWindow.RoundStarting||context.SourceSeat!=_currentSeat||
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId) is not{}window)return false;
        var facts=CompleteProgramEventHistory().ToArray();var start=Array.FindLastIndex(facts,e=>e is RoundStartedEvent r&&r.RoundNumber==_roundNumber);
        if(start<0||facts[start] is not RoundStartedEvent round||round.ActorSeat!=context.SourceSeat)return false;
        var since=facts.Skip(start+1).ToArray();
        if(since.OfType<ProgramBindingResolvedEvent>().Any(e=>e.Window==SkillProgramTriggerWindow.RoundStarting&&e.OwnerSeat==candidate.OwnerSeat&&e.SkillId==candidate.SkillId&&e.BindingId==candidate.BindingId&&e.SkillInstanceId==candidate.SkillInstanceId))return false;
        var turns=since.OfType<TurnStartedEvent>().ToArray();
        return window.BeforeTurnPreludeProof is not null?turns.Length==0:turns is[var first]&&first.ActorSeat==round.ActorSeat&&first.TurnNumber==_turnNumber;
    }
    private SkillProgramStepOutcome BeginHappyZuoCi(ProgramSkillFrame supplied,SkillProgramEffectOp operation)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(f.HappyZuoCi is not null||f.InstructionIndex!=1||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||!HappyZcOriginalFrame(f))
            throw new InvalidOperationException($"Happy Zuo Ci needs its exact original unstarted zero-input program: frame={f.Id}, skill={f.SkillId}, binding={GetProgramBindingId(f)}, instruction={f.InstructionIndex}, window={f.WindowContext?.Window}, parent={_resolutionStack.SingleOrDefault(p=>p.Id==f.WindowContext?.ParentFrameId)?.GetType().Name}, selectedCards={f.SelectedCardIds.Count}, selectedTargets={f.SelectedTargetSeats.Count}.");
        var stage=operation==SkillProgramEffectOp.TransformHappyZuoCiPhantom?HappyZuoCiStage.PhantomChoice:HappyZuoCiStage.InitialChildren;
        var r=new HappyZuoCiReceipt(HappyZcSource(f),f.GameplayHash,operation,f.WindowContext?.ParentFrameId??0,_turnNumber,stage){Before=_movementSequence,After=_movementSequence};
        ReplaceRuntimeTop(f=f with{HappyZuoCi=r});
        AdvanceEventRulesAndQueueFact(new HappyZuoCiStartedEvent(f.Id,r.Source,r.GameplayHash,operation,r.ParentId,r.ActualTurn));
        var owner=_players[f.OwnerSeat];
        if(operation==SkillProgramEffectOp.InitializeHappyZuoCiPhantoms)
        {
            if(owner.HappyZuoCi is not null)throw new InvalidOperationException("A phantom deck may initialize once.");
            var prototypes=_cardZones.CardsAt(CardLocation.DrawPile).Select(c=>new HappyZuoCiPrototype(c.Id,HappyZcFace(c))).ToArray();
            var deck=new CardLocation(CardZoneKind.PrivatePersistentPile,f.OwnerSeat,$"happy-huanshu:{f.SkillInstanceId}");_cardZones.EnsurePrivatePersistentPile(deck);
            owner.HappyZuoCi=new(f.Id,r.Source,r.GameplayHash,deck);
            AdvanceEventRulesAndQueueFact(new HappyZuoCiPoolInitializedEvent(f.Id,r.Source,r.GameplayHash,prototypes.Length));
            f=HappyZcChildren(f,HappyZuoCiStage.InitialChildren);CreateHappyZcPoolCards(f,prototypes);CompleteHappyZcMoves(f.Id);return SkillProgramStepOutcome.AwaitChild;
        }
        if(owner.HappyZuoCi is not{}state||!HappyZcSourceProof(owner.Seat,state))throw new InvalidOperationException("Happy Zuo Ci lost his issued physical deck.");
        if(operation==SkillProgramEffectOp.TransformHappyZuoCiPhantom){PublishHappyZc(f);return SkillProgramStepOutcome.AwaitChild;}
        if(operation==SkillProgramEffectOp.RefreshHappyZuoCiPhantoms)
        {
            f=HappyZcChildren(f,HappyZuoCiStage.RefreshChildren);var changed=0;
            foreach(var card in GetHand(owner).ToArray())
                if(HappyZcEntity(card.Id) is{}found&&found.Owner==owner.Seat&&!found.Entity.ManuallyTransformed)
                {
                    // The source says another same-suit card, not another name.
                    // Keep the copied physical prototype weights, including
                    // repeated faces, and permit the same name at another rank.
                    var currentFace=HappyZcFace(card);
                    var faces=state.Entities.Select(e=>e.Faces[0]).Where(p=>p.Suit==card.Suit&&p!=currentFace).ToArray();
                    if(faces.Length>0){ChangeHappyZcFace(owner,card,faces[_random.Next(faces.Length)],false);changed++;}
                }
            AdvanceEventRulesAndQueueFact(new HappyZuoCiRefreshedEvent(f.Id,f.OwnerSeat,changed));ContinueHappyZc(f.Id);return SkillProgramStepOutcome.AwaitChild;
        }
        if(operation==SkillProgramEffectOp.ResolveHappyZuoCiHuanjing)
        {
            var hp=UnambiguousHp(owner)??throw new InvalidOperationException("Huanjing requires a chosen real health pool before computing lost HP.");
            var amount=Math.Max(1,checked(2*(owner.MaxHp-hp)));
            owner.HappyZuoCi=state with{BonusTurn=_turnNumber,BonusUses=amount};
            AdvanceEventRulesAndQueueFact(new HappyZuoCiHuanjingIssuedEvent(f.Id,f.OwnerSeat,_turnNumber,amount));GainHappyZc(f,amount);return SkillProgramStepOutcome.AwaitChild;
        }
        if(operation!=SkillProgramEffectOp.GainHappyZuoCiPhantoms)throw new InvalidOperationException("Unknown phantom operation.");
        if(f.TriggerId=="foreign-phantom"){BeginHappyZcDraw(f,1);return SkillProgramStepOutcome.AwaitChild;}
        GainHappyZc(f,2);return SkillProgramStepOutcome.AwaitChild;
    }
    private ProgramSkillFrame HappyZcChildren(ProgramSkillFrame supplied,HappyZuoCiStage stage)
    {
        var f=GetActiveProgramFrame(supplied.Id);ReplaceRuntimeTop(f=f with{HappyZuoCi=f.HappyZuoCi! with{Stage=stage,Before=_movementSequence,After=_movementSequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});return f;
    }
    private void CompleteHappyZcMoves(long id)
    {var f=GetActiveProgramFrame(id);ReplaceRuntimeTop(f with{HappyZuoCi=f.HappyZuoCi! with{After=_movementSequence}});ContinueHappyZc(id);}
    private void GainHappyZc(ProgramSkillFrame supplied,int requested)
    {
        var f=HappyZcChildren(supplied,HappyZuoCiStage.GainChildren);var owner=_players[f.OwnerSeat];var state=owner.HappyZuoCi!;
        var holding=state.Entities.Count(e=>_cardZones.GetLocation(e.CardId) is var location&&location!=state.Deck&&location!=CardLocation.OutsideGame);
        var amount=Math.Min(requested,Math.Max(0,checked(2*owner.MaxHp)-holding));
        var gained=new List<int>();
        for(var i=0;i<amount;i++)
        {
            if(_cardZones.Count(state.Deck)==0)
            {
                var recycle=state.Entities.Where(e=>_cardZones.GetLocation(e.CardId)==CardLocation.OutsideGame).Select(e=>GetAdvancedCard(e.CardId)).ToArray();
                if(recycle.Length>0){MoveCards(recycle,CardLocation.OutsideGame,state.Deck,new("skill-program.happy:huanshu.pool-refill"));_cardZones.Shuffle(state.Deck,_random);}
            }
            if(_cardZones.Count(state.Deck)==0)break;
            var card=_cardZones.CardsAt(state.Deck)[0];MoveCard(card,state.Deck,CardLocation.Hand(f.OwnerSeat),new("skill-program.happy:huanshu.gain"));gained.Add(card.Id);
        }
        f=GetActiveProgramFrame(f.Id);var r=f.HappyZuoCi!;
        ReplaceRuntimeTop(f=f with{HappyZuoCi=r with{Requested=requested,Overflow=requested-amount,GainedIds=gained,After=_movementSequence}});
        AdvanceEventRulesAndQueueFact(new HappyZuoCiGainIssuedEvent(f.Id,requested,gained.Count,requested-amount,r.Before,_movementSequence));ContinueHappyZc(f.Id);
    }
    private PromptChoice HappyZcChoice(ProgramSkillFrame f,string mode,Card? card=null)=>new(new($"happy-zc.{f.Id}.{f.HappyZuoCi!.Stage}.{mode}.{card?.Id??0}"),
        card is null?"取消幻化":$"{(mode=="phantom"?"幻化":"参照")}【{card.DisplayName}】{GetSuitDisplayName(card.Suit)}{card.RankText}",card is null?[]:[card.Id],[],
        new Dictionary<string,string>{{"program-action",HappyZcAction},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)},{"mode",mode}});
    private IReadOnlyList<PromptChoice> HappyZcChoices(ProgramSkillFrame f)
    {
        var r=f.HappyZuoCi!;var owner=_players[f.OwnerSeat];
        var cards=r.Stage==HappyZuoCiStage.PhantomChoice?HappyZcPhantomChoices(owner):r.Stage==HappyZuoCiStage.TemplateChoice?HappyZcTemplates(owner,r.PhantomId):[];
        return Array.AsReadOnly(cards.Select(c=>HappyZcChoice(f,r.Stage==HappyZuoCiStage.PhantomChoice?"phantom":"template",c)).Append(HappyZcChoice(f,"cancel")).ToArray());
    }
    private void PublishHappyZc(ProgramSkillFrame f)
    {
        ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.AwaitingResponse});PublishParticipantHandChoice(f,f.OwnerSeat,HappyZcChoices(f),"【幻化】请选择真实手牌。",f.OwnerSeat);AdvanceRulesAndPublishState();
    }
    [ProgramAnswer(HappyZcAction)]private void AnswerHappyZc(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{HappyZuoCi:{}r}f||!ValidHappyZc(f)||_pendingDecision?.PlayerSeat!=f.OwnerSeat||
            !AssistedChoicesEqual([choice],HappyZcChoices(f).Where(c=>c.Id==choice.Id).ToArray()))throw new InvalidOperationException("Phantom choice lost its exact original private hand and cursor.");
        ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});
        if(choice.Parameters["mode"]=="cancel"){FinishHappyZc(f);return;}
        if(r.Stage==HappyZuoCiStage.PhantomChoice){ReplaceRuntimeTop(f=f with{HappyZuoCi=r with{Stage=HappyZuoCiStage.TemplateChoice,PhantomId=choice.Cards.Single()}});PublishHappyZc(f);return;}
        if(r.Stage!=HappyZuoCiStage.TemplateChoice||!CanTransformHappyZc(_players[f.OwnerSeat]))throw new InvalidOperationException("A phantom transform cannot reuse its material or turn allowance.");
        var owner=_players[f.OwnerSeat];var phantom=GetHand(owner).Single(c=>c.Id==r.PhantomId);var template=HappyZcTemplates(owner,r.PhantomId).Single(c=>c.Id==choice.Cards.Single());
        var same=phantom.Suit==template.Suit;ChangeHappyZcFace(owner,phantom,HappyZcFace(template),true);
        owner.HappyZuoCi=owner.HappyZuoCi! with{UsedTemplates=owner.HappyZuoCi.UsedTemplates.Append(template.Id).ToArray()};
        ReplaceRuntimeTop(f=f with{HappyZuoCi=r with{TemplateId=template.Id}});AdvanceEventRulesAndQueueFact(new HappyZuoCiTransformedEvent(f.Id,f.OwnerSeat,same));
        if(same)GainHappyZc(f,1);else FinishHappyZc(f);
    }
    private void BeginHappyZcDraw(ProgramSkillFrame supplied,int amount)
    {
        var f=GetActiveProgramFrame(supplied.Id);ReplaceRuntimeTop(f=f with{HappyZuoCi=f.HappyZuoCi! with{Stage=HappyZuoCiStage.Drawing,Overflow=amount},PendingMovementContinuation=null});
        BeginOperationProgramDraw(f,f.OwnerSeat,amount,new("skill-program.happy:huanshu.native-draw"));
    }
    private bool DrainHappyZc(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||
        TryBeginAdvancedSkillsChanged(f.Id)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id);
    private void ContinueHappyZc(long id)
    {
        if(_pendingDecision is not null||_resolutionStack.LastOrDefault() is not ProgramSkillFrame{HappyZuoCi:{}r}f||f.Id!=id)return;
        if(!ValidHappyZc(f))throw new InvalidOperationException("Happy Zuo Ci lost its original frame or paid native result.");
        if(r.Stage is HappyZuoCiStage.PhantomChoice or HappyZuoCiStage.TemplateChoice){PublishHappyZc(f);return;}
        if(r.Stage==HappyZuoCiStage.Drawing)return;
        if(DrainHappyZc(f))return;
        f=GetActiveProgramFrame(id);r=f.HappyZuoCi!;ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null});
        if(r.Stage==HappyZuoCiStage.GainChildren&&r.Overflow>0&&_players[f.OwnerSeat].IsAlive){BeginHappyZcDraw(f,r.Overflow);return;}
        FinishHappyZc(f);
    }
    private void FinishHappyZc(ProgramSkillFrame f)
    {ReplaceRuntimeTop(f=f with{HappyZuoCi=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    [ProgramContinuation(ProgramContinuationKind.Resume,42600)]private bool ResumeHappyZc(long id)
    {if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{HappyZuoCi:not null}f||f.Id!=id)return false;ContinueHappyZc(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,42600)]private bool ReturnHappyZcMovement(ProgramSkillFrame f)=>f.HappyZuoCi is not null&&ResumeHappyZc(f.Id);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.GainHappyZuoCiPhantoms)]private void ReturnHappyZcGainDraw(ProgramSkillFrame f,NativeDrawOutcome outcome)=>ReturnHappyZcDraw(f,outcome);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.TransformHappyZuoCiPhantom)]private void ReturnHappyZcTransformDraw(ProgramSkillFrame f,NativeDrawOutcome outcome)=>ReturnHappyZcDraw(f,outcome);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveHappyZuoCiHuanjing)]private void ReturnHappyZcLimitedDraw(ProgramSkillFrame f,NativeDrawOutcome outcome)=>ReturnHappyZcDraw(f,outcome);
    private void ReturnHappyZcDraw(ProgramSkillFrame supplied,NativeDrawOutcome outcome)
    {
        var f=GetActiveProgramFrame(supplied.Id);var r=f.HappyZuoCi!;
        if(!ValidHappyZc(f)||r.Stage!=HappyZuoCiStage.Drawing||f.NativeDraw is not null||outcome.RecipientSeat!=f.OwnerSeat||outcome.RequestedCount!=r.Overflow||outcome.SequenceAfter!=_movementSequence)
            throw new InvalidOperationException("A phantom overflow/destruction draw lost its original once-issued native draw.");
        ReplaceRuntimeTop(f=f with{HappyZuoCi=r with{Stage=HappyZuoCiStage.DrawChildren,DrawOutcome=outcome,Before=outcome.SequenceBefore,After=outcome.SequenceAfter},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        AdvanceEventRulesAndQueueFact(new HappyZuoCiDrawReturnedEvent(f.Id,outcome.RequestedCount,outcome.ActualDrawIds.Count,outcome.SequenceBefore,outcome.SequenceAfter));ContinueHappyZc(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.TransformHappyZuoCiPhantom)]private PromptChoice HappyZcAi(PendingDecision p,ProgramSkillFrame f)=>p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("mode")!="cancel")??p.Choices[0];
}
