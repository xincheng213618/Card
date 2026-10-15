namespace CardGame.Core;
public sealed partial class GameEngine
{
    private static CardConversionSource HeQiSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    private static string HeQiReason(HeQiStage stage)=>"skill-program.he-qi."+stage;
    private int HeQiEmptySlots(int seat)=>Enum.GetValues<EquipmentSlot>().Sum(slot=>Math.Max(0,_players[seat].EquipmentSlotCapacity(slot)-GetEquipment(_players[seat]).Count(c=>EquipmentCatalog.Get(c.Kind).Slot==slot)));
    private bool HeQiTargetAllowed(int owner,int target)=>target!=owner && _players[target].IsAlive && GetCombatDistance(target,owner)>GetAttackRange(target);
    private bool IsHeQiActivation(SkillProgramActivation a)=>ProgramInstructionResolver.Default.Features(a).HasOperation(SkillProgramEffectOp.RevealJointHandsAndDiscardDodges);
    private bool HeQiActivationAllowed(CharacterState owner,SkillProgramActivation a)=>!IsHeQiActivation(a) || HeQiEmptySlots(owner.Seat)>0;
    private bool HeQiActivationTargetAllowed(CharacterState owner,CharacterState target,SkillProgramActivation a)=>!IsHeQiActivation(a) || HeQiTargetAllowed(owner.Seat,target.Seat);
    private SkillProgramStepOutcome BeginHeQi(ProgramSkillFrame supplied,int target)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(f.HeQi is not null || f.InstructionIndex!=1 || f.TriggerId is not null || f.OwnerSeat!=_currentSeat || _phase!=TurnPhase.Play || f.SelectedTargetSeats is not[var t] || t!=target || f.SelectedCardIds.Count!=0 || !HeQiTargetAllowed(f.OwnerSeat,target) || HeQiEmptySlots(f.OwnerSeat)==0)throw new InvalidOperationException("HeQi requires its actual reverse-range participant and current empty slots.");
        var candidates=new[]{f.OwnerSeat,target}.SelectMany(seat=>GetHand(_players[seat]).Select((c,slot)=>new HeQiMaterial(c.Id,c.Kind,CardLocation.Hand(seat),slot))).ToArray();
        var r=new HeQiReceipt(HeQiSource(f),f.GameplayHash,f.InstructionIndex,_turnNumber,_currentSeat,target,HeQiEmptySlots(f.OwnerSeat),HeQiStage.SelectingReveal,candidates);
        ReplaceRuntimeTop(f=f with{HeQi=r});AdvanceEventRulesAndQueueFact(new HeQiStartedEvent(f.Id,r.Source,r.GameplayHash,r.InstructionIndex,r.TurnNumber,target,r.Maximum));PublishHeQiChoice(f);return SkillProgramStepOutcome.AwaitChoice;
    }
    private PromptChoice HeQiChoice(ProgramSkillFrame f,string branch,string label,IReadOnlyList<int>? cards=null)=>new(new($"he-qi.{f.Id}.{branch}"),label,cards??[],[],new Dictionary<string,string>{{"program-action","he-qi"},{"branch",branch}});
    private IReadOnlyList<PromptChoice> HeQiChoices(ProgramSkillFrame f)
    {
        var r=f.HeQi!;var choices=new List<PromptChoice>();
        if(r.Stage==HeQiStage.SelectingReveal)
        {
            if(r.Revealed.Count<r.Maximum)foreach(var m in r.Candidates.Where(m=>!r.Revealed.Contains(m.CardId) && _cardZones.GetLocation(m.CardId)==m.From))
                choices.Add(HeQiChoice(f,$"reveal-{m.From.OwnerSeat}-{m.OriginalSlot}",m.From.OwnerSeat==f.OwnerSeat?$"展示自己的【{GetAdvancedCard(m.CardId).DisplayName}】":$"展示目标第{m.OriginalSlot+1}张手牌",m.From.OwnerSeat==f.OwnerSeat?[m.CardId]:[]));
            choices.Add(HeQiChoice(f,"confirm",$"确认共同展示{r.Revealed.Count}张牌"));
        }
        else if(r.Stage==HeQiStage.SelectingObtain && _players[r.TargetSeat].IsAlive)
            foreach(var zone in new[]{CardZoneKind.Hand,CardZoneKind.Equipment,CardZoneKind.Judgment})
            foreach(var pair in _cardZones.CardsAt(new(zone,r.TargetSeat)).Select((c,slot)=>(c,slot)).Where(p=>!r.Revealed.Contains(p.c.Id))) choices.Add(HeQiChoice(f,$"obtain-{zone}-{pair.slot}",zone==CardZoneKind.Hand?$"获得目标第{pair.slot+1}张未展示手牌":$"获得目标区域内【{pair.c.DisplayName}】",zone==CardZoneKind.Hand?[]:[pair.c.Id]));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishHeQiChoice(ProgramSkillFrame f)
    {
        var choices=HeQiChoices(f);if(choices.Count==0){FinishHeQi(f);return;}var skill=_contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with{PendingMovementContinuation=null,Step=ResolutionFrameStep.AwaitingResponse});_pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"请选择共同展示的手牌或未展示的目标牌。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),[],f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=true,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};_status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    [ProgramAiChoice(SkillProgramEffectOp.RevealJointHandsAndDiscardDodges)]
    private PromptChoice HeQiAiChoice(ProgramSkillFrame f)=>f.HeQi!.Stage==HeQiStage.SelectingReveal && f.HeQi.Revealed.Count>=Math.Min(2,f.HeQi.Maximum)?HeQiChoices(f).Single(c=>c.Parameters["branch"]=="confirm"):HeQiChoices(f).First();
    [ProgramAnswer("he-qi")]
    private void ResolveHeQiChoice(PromptChoice choice)
    {
        var f=(ProgramSkillFrame)_resolutionStack.Last();RequireHeQi(f);var r=f.HeQi!;
        if(_pendingDecision?.PlayerSeat!=f.OwnerSeat || !AssistedChoicesEqual([choice],HeQiChoices(f).Where(c=>c.Id==choice.Id).ToArray()))throw new InvalidOperationException("Joint hand choice changed its frozen opaque slots.");ClearPendingDecision();
        if(!_players[f.OwnerSeat].IsAlive || r.Stage==HeQiStage.SelectingReveal && !HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)){FinishHeQi(f);return;}
        var branch=choice.Parameters["branch"];
        if(r.Stage==HeQiStage.SelectingReveal && branch!="confirm")
        {
            var parts=branch.Split('-');var m=r.Candidates.Single(m=>m.From.OwnerSeat==int.Parse(parts[1],System.Globalization.CultureInfo.InvariantCulture)&&m.OriginalSlot==int.Parse(parts[2],System.Globalization.CultureInfo.InvariantCulture));ReplaceRuntimeTop(f=f with{HeQi=r with{Revealed=r.Revealed.Append(m.CardId).ToArray()}});PublishHeQiChoice(f);return;
        }
        if(r.Stage==HeQiStage.SelectingObtain)
        {
            var parts=branch.Split('-');var zone=Enum.Parse<CardZoneKind>(parts[1]);var slot=int.Parse(parts[2],System.Globalization.CultureInfo.InvariantCulture);var from=new CardLocation(zone,r.TargetSeat);var card=_cardZones.CardsAt(from)[slot];if(r.Revealed.Contains(card.Id))throw new InvalidOperationException("HeQi cannot obtain an already revealed entity.");
            r=r with{Stage=HeQiStage.ObtainChildren,ObtainedCardId=card.Id,Before=_movementSequence,After=_movementSequence,Paid=[new(card.Id,card.Kind,from,slot)]};ReplaceRuntimeTop(f with{HeQi=r,PendingMovementContinuation=new(f.OwnerSeat,0,null),Step=ResolutionFrameStep.ResolvingEffect});
            MoveProgramCardsFromMultipleSources([card.Id],CardLocation.Hand(f.OwnerSeat),new(HeQiReason(r.Stage)),(_,records)=>{var active=GetActiveProgramFrame(f.Id);var paid=r with{After=_movementSequence};ReplaceRuntimeTop(active with{HeQi=paid});IssueHeQiSegment(f.Id,paid);});AdvanceRuntimeProgram(f.Id);return;
        }
        if(r.Stage!=HeQiStage.SelectingReveal || branch!="confirm")throw new InvalidOperationException("HeQi cannot select a paid cost twice.");
        var shown=r.Candidates.Where(m=>r.Revealed.Contains(m.CardId)).ToArray();
        if(shown.Any(m=>_cardZones.GetLocation(m.CardId)!=m.From))throw new InvalidOperationException("A revealed physical card left its actual hand before the reveal.");
        AdvanceEventRulesAndQueueFact(new CardsRevealedEvent(f.Id,shown.Select(m=>ToSnapshot(GetAdvancedCard(m.CardId))).ToArray()));
        var dodges=shown.Where(m=>AdvancedEffectiveHandKind(_players[m.From.OwnerSeat!.Value],GetAdvancedCard(m.CardId))==CardKind.Dodge && !IsForeignEquipmentDiscardPrevented(f.OwnerSeat,GetAdvancedCard(m.CardId),m.From,OwnedCardMoveIntent.Discard)).ToArray();
        if(dodges.Length==0){FinishHeQi(f);return;}
        r=r with{Stage=HeQiStage.DiscardChildren,Paid=dodges,Before=_movementSequence,After=_movementSequence};ReplaceRuntimeTop(f with{HeQi=r,PendingMovementContinuation=new(f.OwnerSeat,0,null),Step=ResolutionFrameStep.ResolvingEffect});
        MoveProgramCardsFromMultipleSources(dodges.Select(m=>m.CardId).ToArray(),CardLocation.DiscardPile,new(HeQiReason(r.Stage)),(_,records)=>{var active=GetActiveProgramFrame(f.Id);var paid=r with{After=_movementSequence};ReplaceRuntimeTop(active with{HeQi=paid});IssueHeQiSegment(f.Id,paid);});AdvanceRuntimeProgram(f.Id);
    }
    private void IssueHeQiSegment(long id,HeQiReceipt r)=>AdvanceEventRulesAndQueueFact(new HeQiSegmentPaidEvent(id,r.Source,r.GameplayHash,r.Stage,r.Before,r.After,r.Paid.Count));
    private void FinishHeQi(ProgramSkillFrame f){ReplaceRuntimeTop(f=f with{HeQi=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    [ProgramContinuation(ProgramContinuationKind.Resume,22700)]
    private bool ResumeHeQi(long id)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{HeQi:{}r}f || f.Id!=id || r.Stage is HeQiStage.SelectingReveal or HeQiStage.SelectingObtain)return false;RequireHeQi(f);
        if(TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id)||TryBeginAdvancedSkillsChanged(f.Id))return true;
        if(r.Stage==HeQiStage.DiscardChildren && _players[f.OwnerSeat].IsAlive && _players[r.TargetSeat].IsAlive){ReplaceRuntimeTop(f=f with{HeQi=r with{Stage=HeQiStage.SelectingObtain}});PublishHeQiChoice(f);}else FinishHeQi(f);return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement,22700)]
    private bool ReturnHeQiMovement(ProgramSkillFrame f)=>f.HeQi is not null && ResumeHeQi(f.Id);
    private bool CanContinueHeQi(ProgramSkillFrame f){if(f.HeQi is not{Stage:not HeQiStage.SelectingReveal})return false;RequireHeQi(f);return true;}
    private bool IsHeQiMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)=>f.HeQi is{Stage:HeQiStage.DiscardChildren or HeQiStage.ObtainChildren} && e?.Op==SkillProgramEffectOp.RevealJointHandsAndDiscardDodges && CanContinueHeQi(f) && p.SubjectSeat==f.OwnerSeat && p.BeforeCount==0 && p.CoverageResultBind is null;
    private void RequireHeQi(ProgramSkillFrame f)
    {
        if(f.HeQi is not{}r || r.Source!=HeQiSource(f) || r.GameplayHash!=f.GameplayHash || r.InstructionIndex!=f.InstructionIndex || r.TurnNumber!=_turnNumber || r.TurnOwnerSeat!=f.OwnerSeat || f.SelectedTargetSeats is not[var t] || t!=r.TargetSeat || !Enum.IsDefined(r.Stage)||r.Maximum<1||r.Revealed.Count>r.Maximum||r.Revealed.Distinct().Count()!=r.Revealed.Count || r.Revealed.Any(id=>!r.Candidates.Any(m=>m.CardId==id))||r.Before>r.After||r.After>_movementSequence)throw new InvalidOperationException("HeQi lost its original source, common bound, physical reveal or native invoice.");
        if(CompleteProgramEventHistory().OfType<HeQiStartedEvent>().Count(e=>e==new HeQiStartedEvent(f.Id,r.Source,r.GameplayHash,r.InstructionIndex,r.TurnNumber,r.TargetSeat,r.Maximum))!=1)throw new InvalidOperationException("HeQi requires its once-issued actual activation.");
        if(r.Candidates.Select(m=>m.CardId).Distinct().Count()!=r.Candidates.Count || r.Candidates.Any(m=>m.From.Zone!=CardZoneKind.Hand || m.From.OwnerSeat!=f.OwnerSeat && m.From.OwnerSeat!=r.TargetSeat || m.OriginalSlot<0))throw new InvalidOperationException("Joint reveal lost its two exact original hands.");
        if(r.Stage==HeQiStage.SelectingReveal)return;
        var stage=r.Stage==HeQiStage.SelectingObtain?HeQiStage.DiscardChildren:r.Stage;
        if(stage==HeQiStage.DiscardChildren && r.Paid.Any(m=>!r.Revealed.Contains(m.CardId) || !r.Candidates.Contains(m)) || stage==HeQiStage.ObtainChildren && (r.Paid.Count!=1 || r.Paid[0].From.OwnerSeat!=r.TargetSeat || r.Revealed.Contains(r.Paid[0].CardId) || r.ObtainedCardId!=r.Paid[0].CardId))throw new InvalidOperationException("Joint reveal payment cannot substitute a different entity or recipient zone.");
        if(r.Stage==HeQiStage.SelectingObtain ? f.PendingMovementContinuation is not null : f.PendingMovementContinuation is not {SubjectSeat:var actor,BeforeCount:0,CoverageResultBind:null} || actor!=f.OwnerSeat)throw new InvalidOperationException("Joint reveal continuation lost its exact movement status.");
        if(r.Paid.Count<1 || CompleteProgramEventHistory().OfType<HeQiSegmentPaidEvent>().Count(e=>e==new HeQiSegmentPaidEvent(f.Id,r.Source,r.GameplayHash,stage,r.Before,r.After,r.Paid.Count))!=1 || r.Paid.Any(m=>_cardMovements.Count(actual=>actual.CardId==m.CardId && actual.CardKind==m.PrintedKind && actual.Sequence>r.Before && actual.Sequence<=r.After && actual.From==m.From && actual.To==(stage==HeQiStage.DiscardChildren?CardLocation.DiscardPile:CardLocation.Hand(f.OwnerSeat)) && actual.Reason.Value==HeQiReason(stage))!=1))throw new InvalidOperationException("HeQi cannot resume without its exact paid entities.");
    }
    private sealed partial class ProgramSkillHost:IHeQiProgramHost{public SkillProgramStepOutcome BeginHeQi(ProgramSkillFrame f,int target)=>engine.BeginHeQi(f,target);}
}
