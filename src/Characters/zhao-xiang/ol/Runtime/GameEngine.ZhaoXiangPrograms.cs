using System.Globalization;
using System.Security.Cryptography;
using System.Text;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string ZhaoXiangDrawReason="program.zhao-xiang.conversion-draw";
    private bool HasMeiYingConversionCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GainMeiYingAfterActualSlashDamage);
    private bool IsMeiYingConversion(CardConversionSource source)=>HasMeiYingConversionCapability &&
        _contentRegistry.ProgramDependencies.GetTriggerOperationSkillIds(SkillProgramEffectOp.GainMeiYingAfterActualSlashDamage).Contains(source.SkillId,StringComparer.Ordinal) &&
        _contentRegistry.Skills.TryGetValue(source.SkillId,out var definition) && definition.Program is{}p &&
        p.ViewAs.SingleOrDefault(r=>r.Id==source.BindingId) is{InputCount:1} rule &&
        (rule.OutputKind==CardKind.Slash && rule.InputKinds.SequenceEqual([CardKind.Dodge]) || rule.OutputKind==CardKind.Dodge && rule.InputKinds.SequenceEqual([CardKind.Slash,CardKind.FireSlash,CardKind.ThunderSlash]));
    private bool CanPayMeiYingConversion(CharacterState owner,CardConversionSource source)=>!IsMeiYingConversion(source) ||
        source.OwnerSeat==owner.Seat && owner.IsAlive && HasRuntimeSkillInstance(owner,source.SkillId,source.SkillInstanceId) &&
        EnabledSkillPrograms(owner).Any(p=>p.Id==source.SkillId) && owner.Markers.GetValueOrDefault(PlayerMarkerKind.MeiYing)>0;
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 10)]
    private void ObserveAcceptedMeiYingConversion(CardActionAcceptedEvent accepted)
    {
        if (accepted.Action.ConversionChain.Count != 0) PayMeiYingConversion(accepted.Action);
    }

    private void PayMeiYingConversion(CardActionContext action)
    {
        if(!HasMeiYingConversionCapability || action.ConversionChain.Count==0)return;
        var sources=action.ConversionChain.Where(IsMeiYingConversion).ToArray();if(sources.Length==0)return;
        var existing=CompleteProgramEventHistory().OfType<ZhaoXiangConversionPayment>().Where(e=>e.ActionId==action.ActionId).ToArray();
        if(existing.Length>0)
        {
            if(existing is[var paid] && sources is[var repeated] && paid.Source==repeated && paid.Source.OwnerSeat==action.ProviderSeat &&
                paid.ActualActorSeat==action.ActorSeat && paid.ProviderSeat==action.ProviderSeat && paid.RequesterSeat==action.RequesterSeat && action.PhysicalCards is[var same] && same.CardId==paid.PhysicalCardId &&
                same.CardKind==paid.InputKind && same.From==CardLocation.Hand(action.ProviderSeat) && action.EffectiveKind==paid.OutputKind && paid.Removed==1 &&
                paid.GameplayHash==_contentRegistry.GetSkill(repeated.SkillId).Program?.GameplayHash && paid.ActualTurn==_turnNumber)return;
            throw new InvalidOperationException("A repeated accepted action changed its already paid MeiYing material or exact source.");
        }
        if(sources is not [var source] || !IsValidPlayerSeat(action.ActorSeat) || !IsValidPlayerSeat(action.ProviderSeat) ||
            action.RequesterSeat is{}requester && requester!=action.ActorSeat || source.OwnerSeat!=action.ProviderSeat ||
            action.PhysicalCards is not [var material] || material.From!=CardLocation.Hand(action.ProviderSeat) ||
            !CanPayMeiYingConversion(_players[action.ProviderSeat],source) || ViewAsRule(source) is not{}rule ||
            !rule.InputKinds.Contains(material.CardKind) || rule.OutputKind!=action.EffectiveKind)
            throw new InvalidOperationException("MeiYing cost lost its exact accepted actor, one material and current conversion source.");
        var owner=_players[action.ProviderSeat];PayProgramMarkerCost(owner,new(PlayerMarkerKind.MeiYing,1),source.SkillId,source.BindingId);
        AdvanceEventRulesAndQueueFact(new ZhaoXiangConversionPayment(action.ActionId,source,_contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash,_turnNumber,
            action.ActorSeat,action.ProviderSeat,action.RequesterSeat,material.CardId,material.CardKind,action.EffectiveKind,1,owner.Markers.GetValueOrDefault(PlayerMarkerKind.MeiYing)));
    }
    private int ZhaoXiangCumulativeRemoved(int owner)=>checked(CompleteProgramEventHistory().OfType<ZhaoXiangConversionPayment>().Where(e=>e.Source.OwnerSeat==owner).Sum(e=>e.Removed)+
        CompleteProgramEventHistory().OfType<ZhaoXiangExchangePaidEvent>().Where(e=>e.Source.OwnerSeat==owner).Sum(e=>e.Removed));
    private bool CanGainMeiYingFromDamage(int owner,SkillProgramTrigger trigger,int source,int target,bool sourceLess)=>
        !ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.GainMeiYingAfterActualSlashDamage) || target==owner || !sourceLess && source==owner;
    private void GainMeiYingAfterActualSlashDamage(ProgramSkillFrame supplied)
    {
        var f=GetActiveProgramFrame(supplied.Id);var c=f.WindowContext;var index=_resolutionStack.FindIndex(x=>x.Id==f.Id);
        if(f.InstructionIndex!=1 || f.TriggerId is null || c is not{Window:SkillProgramTriggerWindow.AfterDamageApplied,Amount:>0,DamageFrameId:{}damageId} || index<=0 ||
            _resolutionStack[index-1] is not DamageTriggerWindowFrame parent || parent.Id!=c.ParentFrameId || parent.ParentFrameId!=damageId ||
            parent.SourceCard is not(CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) || parent.CandidateIndex<0 || parent.CandidateIndex>=parent.Candidates.Count ||
            !MountObserverCandidateMatches(f,parent.Candidates[parent.CandidateIndex].ToProgramCandidate()) ||
            !CanGainMeiYingFromDamage(f.OwnerSeat,GetProgramTrigger(f),parent.SourceSeat,parent.TargetSeat,c.Facts?.DamageSourceIsOwner!=true && c.SourceSeat is null) ||
            CompleteProgramEventHistory().OfType<ZhaoXiangMeiYingGainedEvent>().Any(e=>e.DamageWindowId==parent.Id && e.Source.OwnerSeat==f.OwnerSeat))
            throw new InvalidOperationException("Proportional MeiYing lost one actual owner-related applied Slash damage.");
        var owner=_players[f.OwnerSeat];var total=checked(owner.Markers.GetValueOrDefault(PlayerMarkerKind.MeiYing)+c.Amount);
        SetStrategicMarker(f.OwnerSeat,PlayerMarkerKind.MeiYing,f.OwnerSeat,total,f);
        AdvanceEventRulesAndQueueFact(new ZhaoXiangMeiYingGainedEvent(f.Id,parent.Id,damageId,new(f.SkillId,f.TriggerId,f.OwnerSeat,f.SkillInstanceId),c.Amount,total));
    }
    private string[] ZhaoXiangAvailableShuGenerals(int owner)=>PrivateGeneralCandidates().Where(id=>_contentRegistry.Generals[id].FactionId=="shu" &&
        _contentRegistry.Generals[id].SkillIds.All(s=>_contentRegistry.Skills.TryGetValue(s,out var skill) && skill.ImplementationStatus==SkillImplementationStatus.Complete)).ToArray();
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 610)]
    private bool CanOfferZhaoXiangExchange(int owner,SkillProgramTrigger trigger)=>!ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.ExchangeFiveShuBySpentMeiYing) ||
        _players[owner].IsAlive && _players[owner].HasHpValue && ZhaoXiangAvailableShuGenerals(owner).Length>=5;
    private bool ZhaoXiangPreparationParentMatches(ProgramSkillFrame f)
    {
        var index=_resolutionStack.FindIndex(x=>x.Id==f.Id);
        return f.WindowContext is{Window:SkillProgramTriggerWindow.TurnStartBeforeNormalFlow}c && f.TriggerId is not null && f.ActivationId==f.TriggerId &&
            f.OwnerSeat==_currentSeat && c.OwnerSeat==f.OwnerSeat && c.SourceSeat==f.OwnerSeat && index>0 &&
            _resolutionStack[index-1] is ProgramLifecycleTriggerWindowFrame parent && parent.Id==c.ParentFrameId && parent.OwnerSeat==f.OwnerSeat &&
            parent.Window==c.Window && parent.Continuation==ProgramLifecycleContinuation.NormalTurnStart && parent.CandidateIndex>=0 && parent.CandidateIndex<parent.Candidates.Count &&
            MountObserverCandidateMatches(f,parent.Candidates[parent.CandidateIndex]);
    }
    private static string ZhaoXiangPoolFingerprint(IReadOnlyList<string> ids)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',ids))));
    private SkillProgramStepOutcome ExchangeFiveShuBySpentMeiYing(ProgramSkillFrame supplied)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(f.ZhaoXiang is not null || f.InstructionIndex!=1 || !ZhaoXiangPreparationParentMatches(f) || !CanOfferZhaoXiangExchange(f.OwnerSeat,GetProgramTrigger(f)))
            throw new InvalidOperationException("Five-Shu replacement lost its exact limited own preparation.");
        var available=ZhaoXiangAvailableShuGenerals(f.OwnerSeat).ToList();var candidates=new List<string>();
        for(var i=0;i<5;i++){var at=_random.Next(available.Count);candidates.Add(available[at]);available.RemoveAt(at);}
        var owner=_players[f.OwnerSeat];var removed=owner.Markers.GetValueOrDefault(PlayerMarkerKind.MeiYing);
        var r=new ZhaoXiangReceipt{Source=new(f.SkillId,f.TriggerId!,f.OwnerSeat,f.SkillInstanceId),GameplayHash=f.GameplayHash,ParentFrameId=f.WindowContext!.ParentFrameId,
            Turn=_turnNumber,InstructionIndex=f.InstructionIndex,Stage="selection",Removed=removed,CumulativeRemoved=checked(ZhaoXiangCumulativeRemoved(f.OwnerSeat)+removed),
            Generals=candidates,OriginalGeneralId=owner.General.Id,MaximumBefore=owner.MaxHp};
        ReplaceRuntimeTop(f=f with{ZhaoXiang=r});if(removed>0)PayProgramMarkerCost(owner,new(PlayerMarkerKind.MeiYing,removed),f.SkillId,f.TriggerId!);
        AdvanceEventRulesAndQueueFact(new ZhaoXiangExchangePaidEvent(f.Id,r.Source,r.GameplayHash,r.ParentFrameId,r.Turn,r.Removed,r.CumulativeRemoved,ZhaoXiangPoolFingerprint(candidates)));
        PublishZhaoXiangExchange(f);return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> ZhaoXiangExchangeChoices(ProgramSkillFrame f)=>Array.AsReadOnly(f.ZhaoXiang!.Generals.Select(id=>new PromptChoice(new($"zhao-xiang.{f.Id}.{id}"),$"替换为 {_contentRegistry.Generals[id].Name}",[],[],
        new Dictionary<string,string>{["program-action"]="zhao-xiang-exchange",["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture),["general-id"]=id}){ContentIds=Array.AsReadOnly(new[]{id})}).ToArray());
    private void PublishZhaoXiangExchange(ProgramSkillFrame f)
    {
        if(!_players[f.OwnerSeat].IsAlive || _winner!=Winner.None){FinishZhaoXiang(f);return;}
        var skill=_contentRegistry.GetSkill(f.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"扶汉：从实际抽取的五张蜀势力武将牌中选择替换。",[],[],f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=true,TargetSeat=f.OwnerSeat,ValidContentIds=f.ZhaoXiang!.Generals,Choices=ZhaoXiangExchangeChoices(f),SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;
    }
    [ProgramAnswer("zhao-xiang-exchange")]
    private void ResolveZhaoXiangExchangeChoice(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{ZhaoXiang:{Stage:"selection"}}f || _pendingDecision?.PlayerSeat!=f.OwnerSeat ||
            !AssistedChoicesEqual([choice],ZhaoXiangExchangeChoices(f).Where(c=>c.Id==choice.Id).ToArray()))throw new InvalidOperationException("Five-Shu choice lost its original private sampled templates.");
        AssertZhaoXiang(f);ClearPendingDecision();var r=f.ZhaoXiang!;var generalId=choice.Parameters["general-id"];var owner=_players[f.OwnerSeat];
        if(!owner.IsAlive || _winner!=Winner.None){FinishZhaoXiang(f);return;}
        var maximum=Math.Clamp(r.CumulativeRemoved,2,8);
        ReplaceRuntimeTop(f=f with{ZhaoXiang=r=r with{Stage="replacement-children",SelectedGeneralId=generalId,MaximumAfter=maximum}});
        owner.General=_generalPool.Single(g=>g.Id==generalId);owner.GeneralRevealed=true;
        ChangeProgramMaximumHp(f,maximum-owner.MaxHp);
        AdvanceEventRulesAndQueueFact(new ZhaoXiangExchangeSelectedEvent(f.Id,f.OwnerSeat,r.OriginalGeneralId,generalId,r.MaximumBefore,maximum));
        AdvanceRuntimeProgram(f.Id);
    }
    private ProgramCardTriggerWindowFrame? ZhaoXiangConversionParent(ProgramSkillFrame f)=>f.WindowContext?.ParentFrameId is{}id?
        _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(p=>p.Id==id):null;
    private ZhaoXiangConversionPayment? ZhaoXiangActionPayment(ProgramSkillFrame f)
    {
        var parent=ZhaoXiangConversionParent(f);if(parent is null || parent.Action.ProviderSeat!=f.OwnerSeat ||
            parent.CandidateIndex<0 || parent.CandidateIndex>=parent.Candidates.Count || !MountObserverCandidateMatches(f,ToSharedCandidate(parent.Candidates[parent.CandidateIndex])))return null;
        var payments=CompleteProgramEventHistory().OfType<ZhaoXiangConversionPayment>().Where(e=>e.ActionId==parent.Action.ActionId && e.Source.SkillId==f.SkillId &&
            e.Source.SkillInstanceId==f.SkillInstanceId && e.Source.OwnerSeat==f.OwnerSeat && e.GameplayHash==f.GameplayHash && e.Removed==1 && e.ActualTurn==_turnNumber &&
            e.ActualActorSeat==parent.Action.ActorSeat && e.ProviderSeat==parent.Action.ProviderSeat && e.RequesterSeat==parent.Action.RequesterSeat &&
            parent.Action.ConversionChain.Contains(e.Source) && parent.Action.PhysicalCards is[var card] && card.CardId==e.PhysicalCardId && card.CardKind==e.InputKind && parent.Action.EffectiveKind==e.OutputKind).ToArray();
        return payments is[var paid]?paid:null;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 620)]
    private bool CanOfferMeiYingConversionDraw(ProgramTriggerCandidate candidate,SkillProgramTrigger trigger,ProgramSkillWindowContext context)=>
        !ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.DrawAfterPaidMeiYingConversion) ||
        context.CardUse is{}use && !CompleteProgramEventHistory().OfType<ZhaoXiangConversionDrawPaidEvent>().Any(e=>e.ActionId==use.CardActionId) &&
        CompleteProgramEventHistory().OfType<ZhaoXiangConversionPayment>().Any(p=>p.ActionId==use.CardActionId && p.Source.OwnerSeat==candidate.OwnerSeat && p.ProviderSeat==candidate.OwnerSeat && p.ActualActorSeat==use.ActorSeat &&
            p.Source.SkillId==candidate.SkillId && p.Source.SkillInstanceId==candidate.SkillInstanceId && p.GameplayHash==candidate.GameplayHash);
    private SkillProgramStepOutcome DrawAfterPaidMeiYingConversion(ProgramSkillFrame supplied)
    {
        var f=GetActiveProgramFrame(supplied.Id);var paid=ZhaoXiangActionPayment(f);
        if(f.ZhaoXiang is not null || f.InstructionIndex!=1 || paid is null || CompleteProgramEventHistory().OfType<ZhaoXiangConversionDrawPaidEvent>().Any(e=>e.ActionId==paid.ActionId))
            throw new InvalidOperationException("MeiYing reward lost one accepted paid conversion action.");
        var before=_movementSequence;var r=new ZhaoXiangReceipt{Source=new(f.SkillId,f.TriggerId!,f.OwnerSeat,f.SkillInstanceId),GameplayHash=f.GameplayHash,ParentFrameId=f.WindowContext!.ParentFrameId,
            Turn=_turnNumber,InstructionIndex=f.InstructionIndex,Stage="movement",ActionId=paid.ActionId,Before=before,After=before};
        ReplaceRuntimeTop(f=f with{ZhaoXiang=r,PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        DrawProgramCards(f.Id,f.OwnerSeat,1,null,null,SkillProgramCardSetVisibility.Private,new(ZhaoXiangDrawReason));f=GetActiveProgramFrame(f.Id);
        var actual=_cardMovements.Count(m=>m.Sequence>before && m.Sequence<=_movementSequence && m.From==CardLocation.DrawPile && m.To==CardLocation.Hand(f.OwnerSeat) && m.Reason.Value==ZhaoXiangDrawReason);
        ReplaceRuntimeTop(f=f with{ZhaoXiang=r with{After=_movementSequence,ActualDraw=actual}});
        AdvanceEventRulesAndQueueFact(new ZhaoXiangConversionDrawPaidEvent(f.Id,paid.ActionId,f.OwnerSeat,before,_movementSequence,actual));
        if(!TryBeginCardsMovedProgramWindow(f.Id))ReturnRuntimeProgramMovement(f.Id);return SkillProgramStepOutcome.AwaitChild;
    }
    private bool DrainZhaoXiangChildren(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramContinuation(ProgramContinuationKind.Resume,22601)]
    private bool ResumeZhaoXiang(long id)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{ZhaoXiang:not null}f || f.Id!=id)return false;
        AssertZhaoXiang(f);if(GainGiftHasFinalGameEnd())return true;
        if(f.PendingMovementContinuation is not null){if(!TryDrainFireTargetMovement(f))ReturnZhaoXiangMovement(f);return true;}
        if(DrainZhaoXiangChildren(f))return true;f=GetActiveProgramFrame(id);var r=f.ZhaoXiang!;
        if(!_players[f.OwnerSeat].IsAlive || _winner!=Winner.None){ClearPendingDecision();FinishZhaoXiang(f);return true;}
        if(r.Stage=="selection"){if(_pendingDecision is null)PublishZhaoXiangExchange(f);return true;}
        if(r.Stage=="replacement-children")
        {
            var owner=_players[f.OwnerSeat];var request=owner.HasHpValue?Math.Min(1,Math.Max(0,owner.MaxHp-owner.Hp)):0;
            ReplaceRuntimeTop(f=f with{ZhaoXiang=r with{Stage="recovery-children",RecoveryRequested=request}});
            AdvanceEventRulesAndQueueFact(new ZhaoXiangExchangeRecoveryIssuedEvent(f.Id,f.OwnerSeat,request));
            if(request>0)new ProgramSkillHost(this).Recover(f.Id,f.OwnerSeat,f.OwnerSeat,request,null,null);
            AdvanceRuntimeProgram(f.Id);return true;
        }
        if(r.Stage=="recovery-children"){FinishZhaoXiang(f);return true;}
        throw new InvalidOperationException("Zhao Xiang resumed an unrelated native stage.");
    }
    private SkillProgramEffect? ZhaoXiangPausedEffect(ProgramSkillFrame f)=>f.InstructionIndex<1?null:ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
    private bool IsZhaoXiangMovement(ProgramSkillFrame f,SkillProgramEffect?e,ProgramMovementContinuation p)=>e?.Op==SkillProgramEffectOp.DrawAfterPaidMeiYingConversion &&
        f.ZhaoXiang is{Stage:"movement",ActionId:not null} && p.SubjectSeat==f.OwnerSeat && p.BeforeCount==0 && p.CoverageResultBind is null;
    [ProgramContinuation(ProgramContinuationKind.Movement,22602)]
    private bool ReturnZhaoXiangMovement(ProgramSkillFrame f)
    {
        if(f.ZhaoXiang is null)return false;
        if(f.PendingMovementContinuation is not{}p || !IsZhaoXiangMovement(f,ZhaoXiangPausedEffect(f),p))throw new InvalidOperationException("MeiYing draw consumed a foreign movement return.");
        AssertZhaoXiang(f);if(GainGiftHasFinalGameEnd() || TryDrainFireTargetMovement(f))return true;
        FinishZhaoXiang(f);return true;
    }
    private void FinishZhaoXiang(ProgramSkillFrame f){ReplaceRuntimeTop(f=f with{ZhaoXiang=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    private bool ValidZhaoXiang(ProgramSkillFrame f)
    {
        if(f.ZhaoXiang is not{}r || f.InstructionIndex!=1 || r.InstructionIndex!=f.InstructionIndex || r.Source!=new CardConversionSource(f.SkillId,f.TriggerId!,f.OwnerSeat,f.SkillInstanceId) ||
            r.GameplayHash!=f.GameplayHash || r.ParentFrameId!=f.WindowContext?.ParentFrameId || r.Turn!=_turnNumber)return false;
        var events=CompleteProgramEventHistory().ToArray();
        if(r.ActionId is{}actionId)
        {
            if(r.Stage!="movement" || ZhaoXiangPausedEffect(f)?.Op!=SkillProgramEffectOp.DrawAfterPaidMeiYingConversion || ZhaoXiangActionPayment(f)?.ActionId!=actionId || r.Before>r.After)return false;
            return events.OfType<ZhaoXiangConversionDrawPaidEvent>().Where(e=>e.FrameId==f.Id).ToArray() is[var paid] && paid.ActionId==actionId && paid.OwnerSeat==f.OwnerSeat &&
                paid.Before==r.Before && paid.After==r.After && paid.Actual==r.ActualDraw && r.ActualDraw is >=0 and <=1 &&
                _cardMovements.Count(m=>m.Sequence>r.Before && m.Sequence<=r.After && m.From==CardLocation.DrawPile && m.To==CardLocation.Hand(f.OwnerSeat) && m.Reason.Value==ZhaoXiangDrawReason)==r.ActualDraw;
        }
        if(!ZhaoXiangPreparationParentMatches(f) || ZhaoXiangPausedEffect(f)?.Op!=SkillProgramEffectOp.ExchangeFiveShuBySpentMeiYing || r.Generals.Count!=5 || r.Generals.Distinct(StringComparer.Ordinal).Count()!=5 ||
            r.Generals.Any(id=>!_contentRegistry.Generals.TryGetValue(id,out var general) || general.FactionId!="shu" || id==r.OriginalGeneralId) ||
            r.Stage is not("selection" or "replacement-children" or "recovery-children"))return false;
        if(events.OfType<ZhaoXiangExchangePaidEvent>().Where(e=>e.FrameId==f.Id).ToArray() is not[var cost] || cost!=new ZhaoXiangExchangePaidEvent(f.Id,r.Source,r.GameplayHash,r.ParentFrameId,r.Turn,r.Removed,r.CumulativeRemoved,ZhaoXiangPoolFingerprint(r.Generals)) ||
            r.Removed<0 || r.CumulativeRemoved<r.Removed || ZhaoXiangCumulativeRemoved(f.OwnerSeat)!=r.CumulativeRemoved)return false;
        var selected=events.OfType<ZhaoXiangExchangeSelectedEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        if(r.Stage=="selection")return r.SelectedGeneralId is null && selected.Length==0;
        if(r.SelectedGeneralId is not{}chosen || !r.Generals.Contains(chosen) || r.MaximumAfter!=Math.Clamp(r.CumulativeRemoved,2,8) ||
            selected is not[var choice] || choice!=new ZhaoXiangExchangeSelectedEvent(f.Id,f.OwnerSeat,r.OriginalGeneralId,chosen,r.MaximumBefore,r.MaximumAfter))return false;
        var recovery=events.OfType<ZhaoXiangExchangeRecoveryIssuedEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        return r.Stage=="replacement-children"?recovery.Length==0:recovery is[var request] && request.OwnerSeat==f.OwnerSeat && request.Requested==r.RecoveryRequested && r.RecoveryRequested is >=0 and <=1;
    }
    private void AssertZhaoXiang(ProgramSkillFrame f)
    {
        if(f.ZhaoXiang is null)return;if(!ValidZhaoXiang(f))throw new InvalidOperationException("Zhao Xiang lost its actual conversion payment, five sampled Shu templates or once-issued recovery.");
        if(ReferenceEquals(f,_resolutionStack.LastOrDefault()) && _pendingDecision is{}p && f.ZhaoXiang.Stage=="selection" &&
            (p.PlayerSeat!=f.OwnerSeat || !p.IsPrivate || !AssistedChoicesEqual(p.Choices,ZhaoXiangExchangeChoices(f))))throw new InvalidOperationException("Private five-Shu choice leaked or changed its paid candidate set.");
    }
    [ProgramAiChoice(SkillProgramEffectOp.ExchangeFiveShuBySpentMeiYing)]
    private PromptChoice SelectAiZhaoXiangExchange(PendingDecision p,ProgramSkillFrame f)=>p.Choices.OrderByDescending(c=>_contentRegistry.Generals[c.Parameters["general-id"]].BaseHp).ThenBy(c=>c.Id.Value,StringComparer.Ordinal).First();
    [ProgramCapability]
    private sealed class ZhaoXiangProgramCapability(GameEngine engine) : IZhaoXiangProgramHost
    {
        public void GainMeiYingAfterActualSlashDamage(ProgramSkillFrame f)=>engine.GainMeiYingAfterActualSlashDamage(f);
        public SkillProgramStepOutcome ExchangeFiveShuBySpentMeiYing(ProgramSkillFrame f)=>engine.ExchangeFiveShuBySpentMeiYing(f);
        public SkillProgramStepOutcome DrawAfterPaidMeiYingConversion(ProgramSkillFrame f)=>engine.DrawAfterPaidMeiYingConversion(f);
    }
}
