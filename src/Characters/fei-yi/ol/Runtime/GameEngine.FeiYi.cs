using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string FeiYiDrawReason="skill-program.fei-yi.draw";
    private const string FeiYiDiscardReason=global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramDiscardTopPlacement.FeiYiDiscardReason;
    private CardConversionSource FeiYiSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    private bool FeiYiSourceCurrent(ProgramSkillFrame f)=>_winner==Winner.None&&_players[f.OwnerSeat].IsAlive&&HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId);
    [ProgramCapability]
    private sealed class FeiYiCapability(GameEngine engine):IFeiYiHost
    { public SkillProgramStepOutcome Execute(ProgramSkillFrame f,SkillProgramEffectOp op)=>engine.BeginFeiYi(f,op); }
    private SkillProgramStepOutcome BeginFeiYi(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(f.FeiYi is not null||f.InstructionIndex!=1||!FeiYiSourceCurrent(f))throw new InvalidOperationException("Fei Yi requires its exact unpaid live instruction.");
        FeiYiSingletonTransition? transition=null;
        if(op==SkillProgramEffectOp.YanruParityDrawDiscard)
        { if(f.TriggerId is not null||f.WindowContext is not null||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats is{Count:>0}||!FeiYiYanruAvailable(_players[f.OwnerSeat]))throw new InvalidOperationException("Yanru lost its actual Play/parity entry."); }
        else if(f.TriggerId is null||f.WindowContext is not {} context||
            (transition=CurrentFeiYiSingleton(new(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,context.OccurrenceIndex),context)) is null)
            throw new InvalidOperationException("Hezhong lost its exact physical singleton transition.");
        var odd=GetHand(_players[f.OwnerSeat]).Count%2!=0;
        var r=new FeiYiReceipt(FeiYiSource(f),f.GameplayHash,f.InstructionIndex,_turnNumber,_turnProgression.OwnerSeat,op,
            op==SkillProgramEffectOp.YanruParityDrawDiscard?FeiYiStage.DiscardChoice:FeiYiStage.DirectionChoice,
            op==SkillProgramEffectOp.YanruParityDrawDiscard?_cardUseDebitPhaseInstanceId:0,odd,f.WindowContext?.ParentFrameId,f.WindowContext?.MovementBatch?.Id,
            transition?.CardId,transition?.Rank);
        ReplaceRuntimeTop(f=f with{FeiYi=r});
        AdvanceEventRulesAndQueueFact(new FeiYiStartedEvent(f.Id,r.Source,r.GameplayHash,r.Operation,r.Turn,r.TurnOwner,r.PhaseInstance,r.Odd,r.WindowId,r.BatchId));
        if(op==SkillProgramEffectOp.YanruParityDrawDiscard&&odd){PayFeiYiYanru(f);BeginFeiYiDraw(GetActiveProgramFrame(f.Id),false);}
        else if(op==SkillProgramEffectOp.YanruParityDrawDiscard)BeginFeiYiDiscard(f);
        else PublishFeiYiChoice(f);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private IReadOnlyList<PromptChoice> FeiYiChoices(ProgramSkillFrame f)
    {
        var r=f.FeiYi!;var choices=new List<PromptChoice>();
        PromptChoice C(string branch,string label,int? id=null)=>new(new($"fei-yi.{f.Id}.{r.Stage}.{branch}.{id}.{r.Selected.Count}"),label,id is {} value?[value]:[],[],new Dictionary<string,string>{{"program-action","fei-yi"},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)},{"branch",branch}});
        if(r.Stage==FeiYiStage.DirectionChoice&&FeiYiSourceCurrent(f)&&r.RevealedCard is{}singleton&&_cardZones.Contains(CardLocation.Hand(f.OwnerSeat),singleton))
        { if(FeiYiDirectionAvailable(f.OwnerSeat,true))choices.Add(C("greater",$"展示此牌并摸一张牌，令下次点数大于{r.RevealedRank}的普通锦囊额外结算一次"));if(FeiYiDirectionAvailable(f.OwnerSeat,false))choices.Add(C("less",$"展示此牌并摸一张牌，令下次点数小于{r.RevealedRank}的普通锦囊额外结算一次")); }
        if(r.Stage==FeiYiStage.DiscardChoice&&_players[f.OwnerSeat].IsAlive&&(_winner==Winner.None))
        {
            foreach(var id in r.Candidates.Where(id=>!r.Selected.Contains(id)&&_cardZones.Contains(CardLocation.Hand(f.OwnerSeat),id)&&FeiYiCanDiscard(f.OwnerSeat,GetAdvancedCard(id))))choices.Add(C("pick",$"弃置【{GetAdvancedCard(id).DisplayName}】",id));
            foreach(var id in r.Selected)choices.Add(C("unpick",$"撤回【{GetAdvancedCard(id).DisplayName}】",id));
            if(r.Selected.Count>=r.Minimum)choices.Add(C("finish",$"完成弃牌（{r.Selected.Count}张，至少{r.Minimum}张）"));
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishFeiYiChoice(ProgramSkillFrame f)
    {
        var choices=FeiYiChoices(f);if(choices.Count==0){CompleteFeiYi(f,f.FeiYi!.Paid);return;}
        var skill=_contentRegistry.GetSkill(f.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,skill.Name+"：请选择。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),[],f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=true,TargetSeat=f.OwnerSeat,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;
    }
    [ProgramAnswer("fei-yi")]
    private void AnswerFeiYi(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{FeiYi:{}r}f||_pendingDecision?.PlayerSeat!=f.OwnerSeat||!AssistedChoicesEqual([choice],FeiYiChoices(f).Where(c=>c.Id==choice.Id).ToArray()))throw new InvalidOperationException("Fei Yi lost its exact private legal choice.");
        AssertFeiYi(f);ClearPendingDecision();var branch=choice.Parameters["branch"];
        if(r.Stage==FeiYiStage.DirectionChoice)
        {
            var greater=branch=="greater";if(branch is not("greater" or "less")||!FeiYiDirectionAvailable(f.OwnerSeat,greater)||!FeiYiSourceCurrent(f))throw new InvalidOperationException("Hezhong direction was already spent.");
            AdvanceEventRulesAndQueueFact(new CardsRevealedEvent(f.Id,[ToSnapshot(GetAdvancedCard(r.RevealedCard!.Value))]));
            AdvanceEventRulesAndQueueFact(new FeiYiHezhongPaidEvent(f.Id,r.Source,r.GameplayHash,r.Turn,r.TurnOwner,r.BatchId!.Value,r.RevealedCard.Value,r.RevealedRank!.Value,greater));
            ReplaceRuntimeTop(f=f with{FeiYi=r with{Greater=greater,Paid=true}});BeginFeiYiDraw(f,true);return;
        }
        if(branch=="finish"){PayFeiYiDiscard(f);return;}
        var id=choice.Cards.Single();var selected=branch=="pick"?r.Selected.Append(id).ToArray():r.Selected.Where(x=>x!=id).ToArray();
        ReplaceRuntimeTop(f=f with{FeiYi=r with{Selected=selected}});PublishFeiYiChoice(f);
    }
    private void BeginFeiYiDraw(ProgramSkillFrame f,bool hezhong)
    {
        if(!_players[f.OwnerSeat].IsAlive||_winner!=Winner.None){CompleteFeiYi(f,true);return;}
        var before=_movementSequence;var stage=hezhong?FeiYiStage.HezhongDrawChildren:FeiYiStage.DrawChildren;
        ReplaceRuntimeTop(f=f with{FeiYi=f.FeiYi! with{Stage=stage,Before=before,After=before},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        DrawProgramCards(f.Id,f.OwnerSeat,hezhong?1:3,null,null,SkillProgramCardSetVisibility.Private,new(FeiYiDrawReason));
        RecordFeiYiMovement(f.Id,before,stage,0);ContinueFeiYi(f.Id);
    }
    private void RecordFeiYiMovement(long id,long before,FeiYiStage stage,int minimum)
    {
        var f=GetActiveProgramFrame(id);var after=_movementSequence;
        ReplaceRuntimeTop(f with{FeiYi=f.FeiYi! with{After=after}});
        AdvanceEventRulesAndQueueFact(new FeiYiMovementIssuedEvent(id,stage,before,after,_cardMovements.Count(m=>m.Sequence>before&&m.Sequence<=after&&m.Reason.Value==(stage==FeiYiStage.DiscardChildren?FeiYiDiscardReason:FeiYiDrawReason)),minimum));
    }
    private bool DrainFeiYiChildren(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id)||TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueFeiYi(long id)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{FeiYi:{}r}f||f.Id!=id)return;
        AssertFeiYi(f);
        if(r.Stage is FeiYiStage.DiscardChoice or FeiYiStage.DirectionChoice){if(_pendingDecision is null)PublishFeiYiChoice(f);return;}
        if(DrainFeiYiChildren(f))return;
        ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null});
        if(r.Operation==SkillProgramEffectOp.YanruParityDrawDiscard&&r.Stage==FeiYiStage.DrawChildren&&r.Odd){BeginFeiYiDiscard(f);return;}
        if(r.Operation==SkillProgramEffectOp.YanruParityDrawDiscard&&r.Stage==FeiYiStage.DiscardChildren&&!r.Odd){BeginFeiYiDraw(f,false);return;}
        if(r.Stage==FeiYiStage.HezhongDrawChildren)
            AdvanceEventRulesAndQueueFact(new FeiYiRankEffectIssuedEvent(f.Id,r.Source,r.GameplayHash,r.Turn,r.TurnOwner,r.BatchId!.Value,r.RevealedCard!.Value,r.RevealedRank!.Value,r.Greater!.Value));
        CompleteFeiYi(f,true);
    }
    private void CompleteFeiYi(ProgramSkillFrame f,bool applied){AdvanceEventRulesAndQueueFact(new FeiYiCompletedEvent(f.Id,applied));ReplaceRuntimeTop(f=f with{FeiYi=null,PendingMovementContinuation=null});FinishProgramSkill(f,applied);}
    [ProgramContinuation(ProgramContinuationKind.Resume,26600)] private bool ResumeFeiYi(long id){if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{FeiYi:not null}f||f.Id!=id)return false;ContinueFeiYi(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,26600)] private bool ReturnFeiYiMovement(ProgramSkillFrame f){if(f.FeiYi is null)return false;ContinueFeiYi(f.Id);return true;}
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 140)]
    private bool IsFeiYiAwaitedMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)=>f.FeiYi is{Paid:true,Stage:FeiYiStage.DrawChildren or FeiYiStage.DiscardChildren or FeiYiStage.HezhongDrawChildren}&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null&&(e is null||FeiYiComposition.IsOperation(e.Op));
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,26600)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,26600)]
    private bool ContinuePaidFeiYi(ProgramSkillFrame f)=>f.FeiYi is{Paid:true}r&&ProgramEventHistory<FeiYiStartedEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash)==1;
    [ProgramAiChoice(SkillProgramEffectOp.YanruParityDrawDiscard,SkillProgramEffectOp.RevealSingletonAndArmRankEffects)]
    private PromptChoice AiFeiYi(PendingDecision p,ProgramSkillFrame f)=>p.Choices.FirstOrDefault(c=>c.Parameters["branch"]=="finish")??p.Choices.Where(c=>c.Parameters["branch"]!="unpick").OrderBy(c=>c.Cards.Count==0?0:GetKeepValue(GetAdvancedCard(c.Cards[0]),_players[f.OwnerSeat])).First();
}
