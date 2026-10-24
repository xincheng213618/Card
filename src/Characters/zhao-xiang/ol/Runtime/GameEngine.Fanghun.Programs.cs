using System.Globalization;
using System.Security.Cryptography;
using System.Text;
namespace CardGame.Core;
public sealed partial class GameEngine
{
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

    [ProgramCardUsePayment(ProgramCardUsePaymentStage.AcceptedConversionCost, 10)]
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
            !ProgramCardKindMatch.Contains(rule.InputKinds,material.CardKind) || rule.OutputKind!=action.EffectiveKind)
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
            parent.SourceCard is not(CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.ColdSlash) || parent.CandidateIndex<0 || parent.CandidateIndex>=parent.Candidates.Count ||
            !MountObserverCandidateMatches(f,parent.Candidates[parent.CandidateIndex].ToProgramCandidate()) ||
            !CanGainMeiYingFromDamage(f.OwnerSeat,GetProgramTrigger(f),parent.SourceSeat,parent.TargetSeat,c.Facts?.DamageSourceIsOwner!=true && c.SourceSeat is null) ||
            CompleteProgramEventHistory().OfType<ZhaoXiangMeiYingGainedEvent>().Any(e=>e.DamageWindowId==parent.Id && e.Source.OwnerSeat==f.OwnerSeat))
            throw new InvalidOperationException("Proportional MeiYing lost one actual owner-related applied Slash damage.");
        var owner=_players[f.OwnerSeat];var total=checked(owner.Markers.GetValueOrDefault(PlayerMarkerKind.MeiYing)+c.Amount);
        SetStrategicMarker(f.OwnerSeat,PlayerMarkerKind.MeiYing,f.OwnerSeat,total,f);
        AdvanceEventRulesAndQueueFact(new ZhaoXiangMeiYingGainedEvent(f.Id,parent.Id,damageId,new(f.SkillId,f.TriggerId,f.OwnerSeat,f.SkillInstanceId),c.Amount,total));
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

    [ProgramCardLegality(ProgramCardLegalityStage.ConversionSourceBan, 10)]
    private bool MeiYingConversionUnavailable(ProgramViewAsSourceQuery query) =>
        !CanPayMeiYingConversion(query.Owner, query.Source);
}
