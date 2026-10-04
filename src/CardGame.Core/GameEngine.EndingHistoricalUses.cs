using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TracksOwnPlayHistory => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.UseOwnPlayHistoryAtEnding);
    private void RecordOwnPlayEligibleUse(CardActionContext action)
    {
        if (!TracksOwnPlayHistory || _phase != TurnPhase.Play || action.Type != CardActionType.Use || action.ActorSeat != _currentSeat ||
            !MatchesProgramCardCategory(action.EffectiveKind,[SkillProgramCardCategory.Basic,SkillProgramCardCategory.InstantTrick]) ||
            CompleteProgramEventHistory().OfType<OwnPlayEligibleUseRecordedEvent>().Any(e => e.CardActionId == action.ActionId)) return;
        AdvanceEventRulesAndQueueFact(new OwnPlayEligibleUseRecordedEvent(_turnNumber,_currentSeat,_cardUseDebitPhaseInstanceId,action.ActorSeat,action.ActionId,action.EffectiveKind));
    }
    private IReadOnlyList<EndingHistoricalUseSlot> OwnPlayHistory(int owner) => Array.AsReadOnly(CompleteProgramEventHistory().OfType<OwnPlayEligibleUseRecordedEvent>()
        .Where(e => e.ActualTurn == _turnNumber && e.TurnOwnerSeat == owner && e.ActorSeat == owner).Take(2)
        .Select(e => new EndingHistoricalUseSlot(e.CardActionId,e.EffectiveKind,e.PhaseInstanceId)).ToArray());
    private bool ExactHistoricalEnding(ProgramSkillFrame f) => f.WindowContext is { Window:SkillProgramTriggerWindow.TurnEnding } c &&
        _resolutionStack.OfType<TurnEndingBoundaryFrame>().LastOrDefault() is { } ending && c.ParentFrameId == ending.Id &&
        ending.OwnerSeat == f.OwnerSeat && ending.TurnNumber == _turnNumber && f.OwnerSeat == _currentSeat &&
        ending.ItemIndex >= 0 && ending.ItemIndex < ending.Items.Count && ending.Items[ending.ItemIndex].Candidate is { } candidate && MountObserverCandidateMatches(f,candidate);
    private SkillProgramStepOutcome BeginEndingHistoricalUses(ProgramSkillFrame supplied)
    {
        var f=GetActiveProgramFrame(supplied.Id); var slots=OwnPlayHistory(f.OwnerSeat);
        if (f.InstructionIndex != 1 || f.EndingHistoricalUses is not null || !ExactHistoricalEnding(f)) throw new InvalidOperationException("Historical uses require the original own Ending candidate.");
        if (slots.Count == 0) return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(f=f with { EndingHistoricalUses=new(1,DyingSuitsSource(f),f.GameplayHash,_turnNumber,f.WindowContext!.ParentFrameId,slots) });
        PublishHistoricalEndingChoice(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<LegalAction> HistoricalEndingActions(ProgramSkillFrame f)
    {
        var r=f.EndingHistoricalUses!; var actor=_players[f.OwnerSeat]; var result=new List<LegalAction>();
        if (r.Complete || r.UseReturn is not null || r.SlotIndex >= r.Slots.Count || _winner != Winner.None || !actor.IsAlive ||
            !HasRuntimeSkillInstance(actor,f.SkillId,f.SkillInstanceId)) return [];
        var kind=r.Slots[r.SlotIndex].EffectiveKind;
        if (kind is CardKind.Dodge or CardKind.Nullification || IsCardUseForbidden(actor.Seat,kind,CardActionType.Use)) return [];
        foreach (var card in GetHand(actor).Where(c => !IsTurnHandCardRestricted(actor,c) && !IsTurnPhysicalUseForbidden(actor.Seat,[c.Id])))
        {
            var suit=GetProgramEffectiveSuit(actor,card); var color=SuitColor(suit);
            if (IsSlashCard(kind))
            {
                var targets=_players.Where(t => CanUseVirtualSlashTarget(actor,t,kind,suit,color,card.Rank,[card.Id]) &&
                    !IsDirectedCardTargetProhibited(actor.Seat,t.Seat,kind) && !IsCardTargetProhibited(t,kind,suit,color) && !HasBeneficiarySuitShield(actor.Seat,t.Seat,suit)).ToArray();
                foreach(var target in targets) result.Add(new(LegalActionKind.Slash,card.Id,target.Seat,$"将【{card.DisplayName}】当【{CardCatalog.Get(kind).DisplayName}】对 {target.Name} 使用",kind,TargetSeats:[target.Seat]));
                AddFangtianHalberdSlashActions(result,actor,card,targets,CardCatalog.Get(kind).DisplayName,kind);
                AddProgramTargetCountSlashActions(result,actor,card,targets,CardCatalog.Get(kind).DisplayName,kind);
            }
            else if (kind == CardKind.Peach && actor.Hp < actor.MaxHp && !HasSelfCardTargetProhibition(actor.Seat) && !IsCardTargetProhibited(actor,kind,suit,color) && !HasBeneficiarySuitShield(actor.Seat,actor.Seat,suit))
                result.Add(new(LegalActionKind.Peach,card.Id,null,$"将【{card.DisplayName}】当【桃】使用",kind,TargetSeats:[actor.Seat]));
            else if (kind == CardKind.Alcohol && !actor.HasAlcoholEffect && !HasSelfCardTargetProhibition(actor.Seat) && !IsCardTargetProhibited(actor,kind,suit,color) &&
                !HasBeneficiarySuitShield(actor.Seat,actor.Seat,suit))
                result.Add(new(LegalActionKind.Alcohol,card.Id,null,$"将【{card.DisplayName}】当【酒】使用",kind,TargetSeats:[actor.Seat]));
            else if (IsOrdinaryTrick(kind))
                foreach(var option in BuildProgramOrdinaryTrickUseOptions(actor,kind,suit,enforceUsePermission:true,beneficiaryShieldSuit:suit,actualEffectiveColor:color,hasActualColor:true,physicalCardIds:[card.Id],includeNextActualUseAdjustment:true))
                    result.Add(new(option.ActionKind,card.Id,option.TargetSeats.Count == 1 ? option.TargetSeats[0] : null,$"将【{card.DisplayName}】{option.Description}",kind,option.TargetCardId,option.TargetSeats));
        }
        var basicActions = result.Where(a => MatchesProgramCardCategory(a.PlayedCardKind!.Value, [SkillProgramCardCategory.Basic])).ToList();
        var basicCount = basicActions.Count;
        AddNextActualUseAdjustmentActions(basicActions, actor);
        result.AddRange(basicActions.Skip(basicCount));
        return Array.AsReadOnly(result.ToArray());
    }
    private IReadOnlyList<PromptChoice> HistoricalEndingChoices(ProgramSkillFrame f)
    {
        var actions=HistoricalEndingActions(f); var result=actions.Select((a,i) => new PromptChoice(new($"historical-ending.{f.Id}.{i}"),a.Description,[a.CardId!.Value],a.TargetSeats ?? (a.TargetSeat is { } t ? [t] : []),
            new Dictionary<string,string>{["program-action"]="historical-ending",["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture),["option"]=i.ToString(CultureInfo.InvariantCulture)})).ToList();
        result.Add(new(new($"historical-ending.{f.Id}.skip"),"不使用，结束本次技能。",[],[],new Dictionary<string,string>{["program-action"]="historical-ending",["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture),["option"]="skip"}));
        return Array.AsReadOnly(result.ToArray());
    }
    private PromptChoice SelectAiHistoricalEndingUse(PendingDecision decision, ProgramSkillFrame f)
    {
        var (index, thought) = _aiBrains[decision.PlayerSeat].ChooseHistoricalEndingUse(CreateSnapshot(decision.PlayerSeat), HistoricalEndingActions(f), ++_thoughtSequence);
        AddThought(thought);
        return decision.Choices.Single(c => c.Parameters["option"] == (index < 0 ? "skip" : index.ToString(CultureInfo.InvariantCulture)));
    }
    private void PublishHistoricalEndingChoice(ProgramSkillFrame f)
    {
        if (HistoricalEndingActions(f).Count == 0) { FinishProgramSkill(f,true); return; }
        var choices=HistoricalEndingChoices(f); var skill=_contentRegistry.GetSkill(f.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,$"请选择第 {f.EndingHistoricalUses!.SlotIndex + 1} 次真实用牌，或结束。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=true,TargetSeat=f.OwnerSeat,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private void ResolveHistoricalEndingChoice(PromptChoice choice)
    {
        var f=(ProgramSkillFrame)_resolutionStack.Last(); AssertEndingHistoricalUses(f); var r=f.EndingHistoricalUses!;
        var expected=HistoricalEndingChoices(f).SingleOrDefault(c=>c.Id==choice.Id);
        if(expected is null || !expected.Cards.SequenceEqual(choice.Cards) || !expected.Targets.SequenceEqual(choice.Targets)) throw new InvalidOperationException("Historical Ending lost its exact private material and ordered target choice.");
        ClearPendingDecision(); if(choice.Parameters.GetValueOrDefault("option")=="skip") {FinishProgramSkill(f,true);return;}
        var index=int.Parse(choice.Parameters["option"],CultureInfo.InvariantCulture); var action=HistoricalEndingActions(f)[index]; var actor=_players[f.OwnerSeat]; var physical=GetHand(actor).Single(c=>c.Id==action.CardId); var kind=action.PlayedCardKind!.Value;
        var suit=GetProgramEffectiveSuit(actor,physical);
        var option=IsOrdinaryTrick(kind) ? BuildProgramOrdinaryTrickUseOptions(actor,kind,suit,enforceUsePermission:true,beneficiaryShieldSuit:suit,actualEffectiveColor:SuitColor(suit),hasActualColor:true,physicalCardIds:[physical.Id],includeNextActualUseAdjustment:true)
            .Single(o=>o.ActionKind==action.Kind && o.TargetCardId==action.TargetCardId && o.TargetSeats.SequenceEqual(action.TargetSeats!)) : null;
        var adjustedBasic = action.ProgramActivationId == NextActualUseAdjustmentBinding;
        var fangtian = IsSlashCard(kind) && action.TargetSeats.Count > 1 && UsesFormalFangtianHalberd && GetHand(actor).Count == 1 && HasWeaponAbility(actor,CardKind.FangtianHalberd);
        var programTargetCount = IsSlashCard(kind) && action.TargetSeats.Count > 1 && UsesProgramCardTargetCount(actor,physical,kind,action.TargetSeats.Count);
        if(option?.NextActualUseAdjusted==true) _selectedNextCardTargetSeats=option.TargetSeats;
        else if (adjustedBasic) _selectedNextCardTargetSeats=action.TargetSeats;
        var id=BeginCardUse(physical,actor.Seat,action.TargetSeats ?? [],kind,physicalCardIds:[physical.Id],conversionSource:r.Source,
            designatedTargetSeats: kind == CardKind.BorrowedSword ? action.TargetSeats.Where((_, targetIndex) => targetIndex % 2 == 0).ToArray() : null);
        var use=LifecycleCardUse(id)!; var ret=new EndingHistoricalUseReturn(f.Id,f.InstructionIndex,r.Source,f.GameplayHash,_turnNumber,r.EndingFrameId,r.SlotIndex,r.Slots[r.SlotIndex].CardActionId,id,use.Action!.ActionId,physical.Id,kind);
        ReplaceRuntimeFrame(f.Id,f with {EndingHistoricalUses=r with {UseReturn=ret}});
        UpdateLifecycleCardUse(id,u=>u with {EndingHistoricalUseReturn=ret,EndingHistoricalCostBefore=RecipientContestSequence,EndingHistoricalActionKind=action.Kind,EndingHistoricalTargetCardId=action.TargetCardId,EndingHistoricalRequiredCardKind=option?.RequiredCardKind,
            EndingHistoricalUsesFangtian=fangtian,EndingHistoricalUsesProgramTargetCount=programTargetCount});
        MoveCard(physical,CardLocation.Hand(actor.Seat),CardLocation.Processing,CardMoveReasons.Use);
        UpdateLifecycleCardUse(id,u=>u with {EndingHistoricalCostAfter=RecipientContestSequence});
        AdvanceEventRulesAndQueueFact(new EndingHistoricalUseIssuedEvent(ret)); ContinueHistoricalEndingPayment(id);
    }
    private bool ResumeEndingHistoricalUses(long id)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id!=id || f.EndingHistoricalUses is not {} r)return false;
        AssertEndingHistoricalUses(f); if(r.Complete){FinishProgramSkill(f,true);return true;} if(r.UseReturn is not null)throw new InvalidOperationException("Historical Use must return once after its complete owning Use.");
        if(_pendingDecision is null)PublishHistoricalEndingChoice(f);return true;
    }
    private void ContinueHistoricalEndingPayment(long id)
    {
        var use=LifecycleCardUse(id)!; AssertHistoricalEndingUse(use);
        if(TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.HistoricalEndingCardUse) || TryBeginHpChangedProgramWindow(id,PostEventContinuation.HistoricalEndingCardUse) || TryBeginCardsMovedProgramWindow(id))return;
        UpdateLifecycleCardUse(id,u=>u with {EndingHistoricalCostDrained=true});
        var actor=_players[use.SourceSeat]; var card=GetAttackCard(use.CardId); var kind=use.CardKind;
        if(_winner!=Winner.None || !actor.IsAlive){FinishCardUse(id,card,kind);return;}
        if(kind==CardKind.Peach){BeginSimpleCardUse(id,new(card.Id,SimpleCardUseEffect.Recovery));return;}
        if(kind==CardKind.Alcohol){BeginSimpleCardUse(id,new(card.Id,SimpleCardUseEffect.Alcohol));return;}
        if(IsSlashCard(kind))
        {
            var targets=LifecycleCardUse(id)!.TargetSeats;
            var nuzhan=GetNuzhanModifiers(id,actor);
            var damage=(actor.HasAlcoholEffect ? 2 : 1)+nuzhan.DamageBonus;
            CaptureProgramAlcoholConsumption(id,actor);actor.HasAlcoholEffect=false;
            // Ending is outside Play: issue a genuine Use without consuming a
            // finite Play quota. The mature ordered owner drains every target.
            if(targets.Count>1)
            {
                var pending=new FangtianHalberdHandle(this,id,actor.Seat,card,kind,false,damage,targets.ToArray(),use.EndingHistoricalUsesFangtian,false,use.EndingHistoricalUseReturn!.Source);
                ActiveFangtianHalberd=pending;
                if (use.EndingHistoricalUsesFangtian) AdvanceEventRulesAndQueueFact(new FangtianHalberdUsedEvent(id,actor.Seat,card.Id,kind,targets));
                if (use.EndingHistoricalUsesProgramTargetCount)
                {
                    var targetCountRule=EvaluateCardTargetCount(actor,kind);
                    AdvanceEventRulesAndQueueFact(new ProgramCardTargetCountAppliedEvent(id,actor.Seat,kind,targets,
                        Array.AsReadOnly(targetCountRule.Value.Contributions.Select(c=>c.SourceId).ToArray())));
                }
                AdvanceEventRulesAndQueueFact(new CardUsedEvent(card.Id,kind,actor.Seat,targets[0]));
                foreach (var target in targets) NotifyAiOfSlash(actor,_players[target]);
                BeginNextFangtianHalberdTarget(pending);return;
            }
            var attack=new CardAttackHandle(this,id,actor.Seat,targets[0],card,damageAmount:damage,playedCardKind:kind,physicalCards:[card],
                ignoresArmor:HasCardArmorBypass(actor,_players[targets[0]],kind),programSkillCardUseFrameId:use.EndingHistoricalUseReturn!.ProgramFrameId);
            ActiveCardAttack=attack;CaptureProgramAdjustedSlashBaseDamage(attack);
            MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat,kind);
            AdvanceEventRulesAndQueueFact(new CardUsedEvent(card.Id,kind,actor.Seat,targets[0]));TryMarkProgramUseCommitted(id);
            if(!TryBeginProgramCardWindow(attack,LifecycleCardUse(id)!.Action!,SkillProgramTriggerWindow.CardUseCommitted,targets,ProgramCardContinuation.CommittedSlash))BeginSlashTargetResolution(attack);return;
        }
        BeginJizhiOrNullificationWindow(id,card,actor.Seat,LifecycleCardUse(id)!.TargetSeats,use.EndingHistoricalActionKind!.Value,use.EndingHistoricalTargetCardId,use.EndingHistoricalRequiredCardKind,kind);
    }
    private void ReturnHistoricalEndingUse(CardUseFrame use)
    {
        // This completed frame has already been popped. Its frozen Active bit
        // distinguishes a real attack completion from a later skipped trick tail.
        if(use.EndingHistoricalUseReturn is not {} ret || use.CardAttack is { Active: true })return;
        FinishHistoricalEndingReturn(ret);
    }
    private void CompleteHistoricalEndingAttack(AttackCompletionReceipt completion) => FinishHistoricalEndingReturn(completion.EndingHistoricalUseReturn!);
    private void FinishHistoricalEndingReturn(EndingHistoricalUseReturn ret)
    {
        var f=GetActiveProgramFrame(ret.ProgramFrameId);var r=f.EndingHistoricalUses!;
        if(_resolutionStack.LastOrDefault()?.Id!=f.Id || r.UseReturn!=ret || LifecycleCardUse(ret.CardUseFrameId)is not null ||
            CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Count(e=>e.ResolutionId==ret.CardUseFrameId)!=1 || CompleteProgramEventHistory().OfType<EndingHistoricalUseReturnedEvent>().Any(e=>e.Return==ret))
            throw new InvalidOperationException("Historical Ending returned before its entire issued card use finished.");
        AdvanceEventRulesAndQueueFact(new EndingHistoricalUseReturnedEvent(ret));
        // Let the mature attack/group/simple producer finish its own tail
        // before the next runtime step can publish or pay the next slot.
        ReplaceRuntimeTop(f with {EndingHistoricalUses=r with {UseReturn=null,SlotIndex=r.SlotIndex+1,Complete=r.SlotIndex+1>=r.Slots.Count}});
    }
}
