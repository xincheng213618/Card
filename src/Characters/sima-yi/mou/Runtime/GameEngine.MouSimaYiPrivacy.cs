using System.Collections.Frozen;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private void BeginMouSmSecretUse(long useId,long eventStart,long logStart)
    {
        var use=LifecycleCardUse(useId)!;if(use.Action is not{}a||!IsSlashCard(a.EffectiveKind))return;
        var b=GetSkillBindingShard(_players[use.SourceSeat]).GetInstanceTriggers(SkillProgramTriggerWindow.CardUseCommitted).FirstOrDefault(b=>b.SkillId==MouSimaYiComposition.Pingliao&&MouSimaYiComposition.Private(b.Trigger));
        var original=use.AcceptedDeclarationPayment?.MouSimaYiSecret;
        if(b is null&&original is null)return;
        var lease=new MouSmSecretLease(original?.Source??new(b!.SkillId,b.Trigger.Id,use.SourceSeat,b.SkillInstanceId),original?.GameplayHash??b!.Program.GameplayHash,a.ActionId,eventStart,logStart);
        ReplaceRuntimeFrame(use.Id,use=use with{MouSimaYiSecret=lease});
        for(var i=0;i<_pendingEvents.Count;i++)if(_eventSequence+i+1>=eventStart)CaptureMouSmSecretFact(_pendingEvents[i],_eventSequence+i+1);
        foreach(var log in _log.Where(l=>l.Sequence>logStart).ToArray())CaptureMouSmSecretLog(log);
    }
    private static bool IsMouSmTargetFact(IGameEvent e,CardUseFrame u)=>e switch
    {
        TargetsConfirmedEvent x=>x.ResolutionId==u.Id,
        CardUseAppearanceCapturedEvent x=>x.Action.ActionId==u.Action!.ActionId,
        CardActionAcceptedEvent x=>x.Action.ActionId==u.Action!.ActionId,
        CardUsedEvent x=>x.SourceSeat==u.SourceSeat&&x.CardId==u.CardId&&x.CardKind==u.CardKind&&x.TargetSeat is{}t&&u.TargetSeats.Contains(t),
        ProgramViewAsConvertedEvent x=>x.FrameId==u.Id&&x.IsUse,
        FangtianHalberdUsedEvent x=>x.ResolutionId==u.Id,
        ZhuqueFanConvertedEvent x=>x.ResolutionId==u.Id,
        ZhangbaSerpentSpearConvertedEvent x=>x.ResolutionId==u.Id&&x.IsUse,
        ProgramCardTargetCountAppliedEvent x=>x.ResolutionId==u.Id,
        LihuoSlashUsedEvent x=>x.ResolutionId==u.Id,
        RedAdditionalTargetsConsumedEvent x=>x.OwnerSeat==u.SourceSeat&&x.Targets.SequenceEqual(u.TargetSeats),
        _=>false
    };
    private void CaptureMouSmSecretFact(IGameEvent payload,long sequence)
    {
        var use=_resolutionStack.OfType<CardUseFrame>().LastOrDefault(u=>u.MouSimaYiSecret is{Started:false,Released:false}&&IsMouSmTargetFact(payload,u));
        if(use?.MouSimaYiSecret is not{}s||s.EventSequences.Contains(sequence))return;
        ReplaceRuntimeFrame(use.Id,use with{MouSimaYiSecret=s with{EventSequences=s.EventSequences.Append(sequence).ToArray()}});
    }
    private void CaptureMouSmSecretLog(GameLogEntry log)
    {
        var use=_resolutionStack.OfType<CardUseFrame>().LastOrDefault(u=>u.MouSimaYiSecret is{Started:false,Released:false}&&_resolutionStack.LastOrDefault()?.Id==u.Id&&log.ActorSeat==u.SourceSeat&&log.TargetSeat is{}t&&u.TargetSeats.Contains(t));
        if(use?.MouSimaYiSecret is not{}s||s.LogSequences.Contains(log.Sequence))return;
        ReplaceRuntimeFrame(use.Id,use with{MouSimaYiSecret=s with{LogSequences=s.LogSequences.Append(log.Sequence).ToArray()}});
    }
    private sealed record MouSmPublicProjection(IReadOnlySet<long> Events,IReadOnlySet<long> Logs,IReadOnlySet<long> Declarations);
    private MouSmPublicProjection? PrepareMouSmPublicProjection()
    {
        var leases=_resolutionStack.OfType<CardUseFrame>().Select(u=>u.MouSimaYiSecret).OfType<MouSmSecretLease>().ToArray();
        var facts=CompleteProgramEventHistory().Where(e=>e is MouSmTargetsDisclosedEvent or MouSmSecretDeclarationRecordedEvent).ToArray();
        if(leases.Length==0&&facts.Length==0)return null;
        var disclosed=facts.OfType<MouSmTargetsDisclosedEvent>().ToArray();
        return new(leases.SelectMany(x=>x.EventSequences).Concat(disclosed.SelectMany(x=>x.HeldEventSequences)).ToFrozenSet(),leases.SelectMany(x=>x.LogSequences).Concat(disclosed.SelectMany(x=>x.HeldLogSequences)).ToFrozenSet(),facts.OfType<MouSmSecretDeclarationRecordedEvent>().Select(x=>x.DeclarationId).ToFrozenSet());
    }
    private static EventEnvelope? MouSmPublicEvent(EventEnvelope envelope,MouSmPublicProjection? projection)
    {
        if(projection is null)return envelope;
        if(projection.Events.Contains(envelope.Sequence))return null;
        if(envelope.Payload is CardDeclarationCommittedEvent declaration&&projection.Declarations.Contains(declaration.DeclarationId))return envelope with{Payload=CommittedEventProjection.Freeze(declaration with{TargetSeats=Array.Empty<int>()})};
        return envelope;
    }
    private IReadOnlyList<EventEnvelope> MouSmPublicEvents()
    {
        var projection=PrepareMouSmPublicProjection();return projection is null?_events.AsReadOnly():Array.AsReadOnly(_events.Select(e=>MouSmPublicEvent(e,projection)).OfType<EventEnvelope>().ToArray());
    }
    private IReadOnlyList<GameLogEntry> MouSmPublicLog()
    {
        var projection=PrepareMouSmPublicProjection();return projection is null?_log.AsReadOnly():Array.AsReadOnly(_log.Where(e=>!projection.Logs.Contains(e.Sequence)).ToArray());
    }
    private DeclaredCardPayment CaptureMouSmDeclaration(DeclaredCardPayment payment,CardDeclarationParentReturn parent)
    {
        if(parent.Purpose is not(CardDeclarationPurpose.Use or CardDeclarationPurpose.ProvidedSlash or CardDeclarationPurpose.BorrowedSword or CardDeclarationPurpose.BorrowedSwordProvidedSlash or CardDeclarationPurpose.QinglongProvidedSlash)||!IsSlashCard(parent.FinalEffectiveKind??payment.DeclaredKind))return payment;
        var b=GetSkillBindingShard(_players[parent.ActorSeat]).GetInstanceTriggers(SkillProgramTriggerWindow.CardUseCommitted).FirstOrDefault(b=>b.SkillId==MouSimaYiComposition.Pingliao&&MouSimaYiComposition.Private(b.Trigger));
        if(b is null)return payment;
        AdvanceEventRulesAndQueueFact(new MouSmSecretDeclarationRecordedEvent(payment.DeclarationId,parent.ActorSeat));
        return payment with{MouSimaYiSecret=new(new(b.SkillId,b.Trigger.Id,parent.ActorSeat,b.SkillInstanceId),b.Program.GameplayHash)};
    }
    private IReadOnlyList<int> MouSmDeclarationTargets(CardDeclarationFrame frame,int viewer,bool revealAll)=>frame.Payment.MouSimaYiSecret is not null&&!revealAll&&viewer!=frame.Return.ActorSeat?Array.Empty<int>():Array.AsReadOnly(frame.TargetSeats.ToArray());
    private bool HoldMouSmAiAttack(int source,int target)=>_resolutionStack.OfType<CardUseFrame>().Any(u=>u.SourceSeat==source&&u.TargetSeats.Contains(target)&&u.MouSimaYiSecret is{Released:false});
    private bool TryBeginMouSmPrivateWindow(CardAttackHandle attack,MouSmPrivateCall call)
    {
        if(LifecycleCardUse(attack.ResolutionId)is not{Action:{}a,MouSimaYiSecret:{Started:false,Released:false}s}u)return false;
        if(a.ActionId!=s.ActionId||a.ActorSeat!=s.Source.OwnerSeat||!IsSlashCard(a.EffectiveKind)||s.GameplayHash!=_contentRegistry.GetSkill(s.Source.SkillId).Program?.GameplayHash||_resolutionStack[^1].Id!=u.Id)throw new InvalidOperationException("Pingliao lost its original secret Slash before target windows.");
        var trigger=_contentRegistry.GetSkill(s.Source.SkillId).Program!.Triggers.Single(t=>t.Id==s.Source.BindingId);var id=++_resolutionSequence;
        ReplaceRuntimeFrame(u.Id,u=u with{MouSimaYiSecret=s with{Started=true,WindowId=id,Call=call}});
        var context=CreateCardActionProgramContext(a,SkillProgramTriggerWindow.CardUseCommitted,id,u.SourceSeat,-1,CaptureProgramTriggerFacts(_players[u.SourceSeat],a));
        context=context with{CardUse=context.CardUse! with{ParentCardUseFrameId=u.Id}};
        var candidate=new ProgramCardTriggerCandidate(u.SourceSeat,-1,s.Source.SkillId,s.Source.BindingId,s.GameplayHash,s.Source.SkillInstanceId,trigger.Priority,context);
        PushRuntimeFrame(new ProgramCardTriggerWindowFrame(id,u.Id,a,ProgramCardContinuation.CommittedSlash,Array.AsReadOnly(new[]{candidate}),AttackOwnerFrameId:u.Id){MouSimaYiPrivate=new(u.Id,a.ActionId,call)});
        AdvanceRuntimeTop<ProgramCardTriggerWindowFrame>();return true;
    }
    private bool ValidMouSmPrivateWindow(ProgramCardTriggerWindowFrame p)=>p.MouSimaYiPrivate is{}back&&p.ParentFrameId==back.UseId&&p.AttackOwnerFrameId==back.UseId&&p.Continuation==ProgramCardContinuation.CommittedSlash&&LifecycleCardUse(back.UseId)is{Action:{}a,MouSimaYiSecret:{Started:true}s}u&&s.WindowId==p.Id&&s.ActionId==back.ActionId&&s.Call==back.Call&&a.ActionId==back.ActionId&&p.Action.ActionId==back.ActionId&&a.Type==CardActionType.Use&&a.ActorSeat==s.Source.OwnerSeat&&IsSlashCard(a.EffectiveKind)&&a.TargetSeats.SequenceEqual(p.Action.TargetSeats)&&p.Candidates is[var c]&&c.OwnerSeat==s.Source.OwnerSeat&&c.SkillId==s.Source.SkillId&&c.TriggerId==s.Source.BindingId&&c.SkillInstanceId==s.Source.SkillInstanceId&&c.GameplayHash==s.GameplayHash&&s.GameplayHash==_contentRegistry.GetSkill(c.SkillId).Program?.GameplayHash;
    private bool IsIssuedMouSmPrivateCandidate(ProgramTriggerCandidate candidate,ProgramSkillWindowContext context)
    {
        if(candidate.SkillId!=MouSimaYiComposition.Pingliao||context.Window!=SkillProgramTriggerWindow.CardUseCommitted||context.OwnerSeat!=candidate.OwnerSeat||context.SourceSeat!=candidate.OwnerSeat||context.TargetSeat is not null||context.CardUse is not{}info||_resolutionStack.SingleOrDefault(x=>x.Id==context.ParentFrameId)is not ProgramCardTriggerWindowFrame{MouSimaYiPrivate:{}back}p||!ValidMouSmPrivateWindow(p)||p.CandidateIndex<0||p.CandidateIndex>=p.Candidates.Count)return false;
        var c=p.Candidates[p.CandidateIndex];return c.OwnerSeat==candidate.OwnerSeat&&c.SkillId==candidate.SkillId&&c.TriggerId==candidate.BindingId&&c.SkillInstanceId==candidate.SkillInstanceId&&c.GameplayHash==candidate.GameplayHash&&candidate.OccurrenceIndex==0&&info.ParentCardUseFrameId==back.UseId&&info.CardActionId==back.ActionId&&info.ActorSeat==candidate.OwnerSeat&&info.EffectiveKind==p.Action.EffectiveKind&&info.DesignatedTargetSeats is{}designated&&designated.SequenceEqual(p.Action.EffectiveDesignatedTargetSeats);
    }
    private bool HasMouSmPrivateContinuation(long useId)=>_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Any(p=>p.MouSimaYiPrivate?.UseId==useId&&ValidMouSmPrivateWindow(p)&&_resolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.WindowContext?.ParentFrameId==p.Id&&f.MouSimaYi is{Operation:SkillProgramEffectOp.ResolveMouSimaYiPingliao}r&&r.UseId==useId&&MouSimaYi.Valid(f)));
    private void DiscloseMouSmTargets(ProgramSkillFrame f)
    {
        if(!MouSimaYi.Valid(f)||f.MouSimaYi is not{Operation:SkillProgramEffectOp.ResolveMouSimaYiPingliao}r||LifecycleCardUse(r.UseId)is not{Action:{}a,MouSimaYiSecret:{Started:true,Released:false}s}u||s.WindowId!=r.ParentId||a.ActionId!=r.ActionId||!u.TargetSeats.SequenceEqual(r.Targets))throw new InvalidOperationException("Pingliao cannot disclose another original Use or disclose twice.");
        ReplaceRuntimeFrame(u.Id,u with{MouSimaYiSecret=s with{Released=true}});
        AdvanceEventRulesAndQueueFact(new MouSmTargetsDisclosedEvent(u.Id,a.ActionId,u.SourceSeat,u.CardId,u.CardKind){Targets=u.TargetSeats,HeldEventSequences=s.EventSequences,HeldLogSequences=s.LogSequences});
        foreach(var target in u.TargetSeats)NotifyAiOfSlash(_players[u.SourceSeat],_players[target]);
        AddLog("CardUsed",$"{_players[u.SourceSeat].Name} 的【{CardCatalog.Get(u.CardKind).DisplayName}】指定了 {string.Join("、",u.TargetSeats.Select(t=>_players[t].Name))}。",u.SourceSeat);
    }
    private bool TryReturnMouSmPrivateWindow(ProgramCardTriggerWindowFrame p)
    {
        if(p.MouSimaYiPrivate is not{}back)return false;
        if(!ValidMouSmPrivateWindow(p)||LifecycleCardUse(back.UseId)is not{MouSimaYiSecret:{Released:true}}u||_resolutionStack[^1].Id!=u.Id||ProgramEventHistory<MouSmTargetsDisclosedEvent>().Count(d=>d.UseId==u.Id&&d.ActionId==back.ActionId)!=1)throw new InvalidOperationException("Pingliao private target window did not disclose its exact paid Use once.");
        var attack=new CardAttackHandle(this,u.Id);
        if(back.Call==MouSmPrivateCall.CommittedSlash&&TryBeginProgramCardWindow(attack,u.Action!,SkillProgramTriggerWindow.CardUseCommitted,u.Action!.TargetSeats,ProgramCardContinuation.CommittedSlash))return true;
        BeginSlashTargetResolution(attack);return true;
    }
}
