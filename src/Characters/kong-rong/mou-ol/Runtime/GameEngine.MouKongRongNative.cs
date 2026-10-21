namespace CardGame.Core;

public sealed partial class GameEngine
{
    private ProgramSkillFrame? MouKrUseQueryFrame(int actor)
    {
        var top=_resolutionStack.LastOrDefault();
        var parent=top switch { DrawFundedDistinctBasicFrame paid when IsMouKrLiwenPaymentFrame(paid)=>_resolutionStack.SingleOrDefault(x=>x.Id==paid.Payment.ParentFrameId),
            ChainedStateBasicFrame paid when IsMouKrLiwenPaymentFrame(paid)=>_resolutionStack.SingleOrDefault(x=>x.Id==paid.Payment.ParentFrameId),
            ProgramSkillFrame{EquipmentSlotNamedUse:{}slot} when MouKrLiwenSlotParent(slot)is{}original=>original, _=>top };
        if(parent is not ProgramSkillFrame{MouKrLiwen:{}r}f||r.Stage is not(MouKrLiwenStage.UseChoice or MouKrLiwenStage.UseDraft or MouKrLiwenStage.Issuing)||
            r.Cursor<0||r.Cursor>=r.Schedule.Count||r.Schedule[r.Cursor].Seat!=actor||!_players[actor].IsAlive||!MouKongRong.Valid(f))return null;
        return f;
    }
    private bool IsMouKrLiwenUseQuery(CharacterState actor)=>MouKrUseQueryFrame(actor.Seat)is not null;
    private IReadOnlyList<LegalAction> MouKrActions(ProgramSkillFrame f,int actor)
    {
        if(MouKrUseQueryFrame(actor)?.Id!=f.Id||_winner!=Winner.None)return [];
        return Array.AsReadOnly(BuildLegalActions(_players[actor],includeProgramActions:false)
            .Where(a=>a.Kind is not(LegalActionKind.EndPlay or LegalActionKind.Recast or LegalActionKind.UseProgramSkill or LegalActionKind.RevealGeneral)&&
                (a.Kind!=LegalActionKind.UseEquipmentEffect||a.EquipmentKind==CardKind.ZhangbaSerpentSpear))
            .Select(MouKongRongFreeze.Action).ToArray());
    }
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaBeforeLimit,34600)]
    private bool MouKrRequestedQuota(ProgramSlashQuotaQuery q)=>IsMouKrLiwenUseQuery(q.Actor);
    private void IssueMouKrUse(ProgramSkillFrame f,int actor,LegalAction action,IReadOnlyList<int> cards,IReadOnlyList<int> targets)
    {
        if(f.MouKrLiwen is not{Stage:MouKrLiwenStage.Issuing}r||MouKrUseQueryFrame(actor)?.Id!=f.Id)throw new InvalidOperationException("Liwen lost the actual scheduled actor before native issuance.");
        if(action.DrawFundedDistinctBasicUse is not null)
        { BeginDrawFundedDistinctBasic(_players[actor],action.ConversionSource!,action.PlayedCardKind!.Value,action.TargetSeats,r.OriginalDecision!,new(DrawFundedDistinctBasicIntent.Play,f.Id,f.Id,null,f.InstructionIndex,action.TargetSeat));return; }
        if(action.ChainedStateBasicUse is not null)
        { BeginChainedStateBasic(_players[actor],action.ConversionSource!,action.PlayedCardKind!.Value,action.TargetSeats,r.OriginalDecision!,new(ChainedStateBasicIntent.Play,f.Id,f.Id,null,f.InstructionIndex,action.TargetSeat));return; }
        if(action.TieredRoundZeroUse is not null&&action.ConversionSource is{}source&&IsEquipmentSlotNamedSkill(source.SkillId))
        { _pendingDecision=r.OriginalDecision;if(TryBeginEquipmentSlotNamedPlay(_players[actor],action))return;ClearPendingDecision(); }
        ExecuteAction(_players[actor],action,cards,targets);
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,34601)]
    private void BindMouKrUse(CardUseDeclaredEvent fact)
    {
        if(!HasMouKongRong||LifecycleCardUse(fact.ResolutionId)is not{Action:{Type:CardActionType.Use}a}use)return;
        var at=_resolutionStack.FindIndex(x=>x.Id==use.Id);
        if(at<1||_resolutionStack[at-1]is not ProgramSkillFrame{MouKrLiwen:{Stage:MouKrLiwenStage.Issuing,UseId:0,ActionId:0,SelectedAction:{}selected}r}f)return;
        var actor=r.Schedule[r.Cursor].Seat;
        var expected=selected.Kind==LegalActionKind.UseEquipmentEffect?r.Cards:selected.CardId is>0?[selected.CardId.Value]:Array.Empty<int>();
        if(!MouKongRong.Valid(f)||a.ActorSeat!=actor||a.ProviderSeat!=actor||a.RequesterSeat is not null||use.SourceSeat!=actor||
            !a.PhysicalCards.Select(c=>c.CardId).Order().SequenceEqual(expected.Order())||selected.PlayedCardKind is{}kind&&a.EffectiveKind!=kind||
            selected.ConversionSource is{}source&&!a.ConversionChain.Contains(source))throw new InvalidOperationException("Liwen changed its exact native actor, source or selected real materials.");
        var origin=new MouKrLiwenNativeOrigin(f.Id,r.Cursor,r.Source,r.GameplayHash,use.Id,a.ActionId,a.ActorSeat,a.ProviderSeat);
        ReplaceRuntimeFrame(f.Id,f with{MouKrLiwen=r with{Stage=MouKrLiwenStage.NativeUse,UseId=use.Id,ActionId=a.ActionId}});
        UpdateLifecycleCardUse(use.Id,u=>u with{MouKrLiwenOrigin=origin,UnlimitedUse=IsSlashCard(u.CardKind)||u.UnlimitedUse});
        AdvanceEventRulesAndQueueFact(new MouKrLiwenNativeIssuedEvent(f.Id,r.Cursor,use.Id,a.ActionId,a.ActorSeat,a.ProviderSeat));
    }
    private bool MouKrNativeMatches(CardUseFrame use,ProgramSkillFrame f)=>use.MouKrLiwenOrigin is{}o&&f.MouKrLiwen is{Stage:MouKrLiwenStage.NativeUse}r&&MouKongRong.Valid(f)&&
        o.FrameId==f.Id&&o.Index==r.Cursor&&o.Source==r.Source&&o.GameplayHash==r.GameplayHash&&o.UseId==use.Id&&o.UseId==r.UseId&&o.ActionId==r.ActionId&&
        use.Action is{}a&&a.ActionId==o.ActionId&&ShownEntityUseActorMatches(use,o.ActorSeat,o.ProviderSeat)&&
        ProgramEventHistory<MouKrLiwenNativeIssuedEvent>().Count(e=>e==new MouKrLiwenNativeIssuedEvent(f.Id,r.Cursor,use.Id,a.ActionId,o.ActorSeat,o.ProviderSeat))==1;
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeSlashDebit,34600)]
    private bool MouKrNoSlashDebit(long id,int actor)=>LifecycleCardUse(id)is{MouKrLiwenOrigin:{}o}use&&o.ActorSeat==actor&&
        _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==o.FrameId)is{}parent&&MouKrNativeMatches(use,parent);
    [NativeUseValidation(34600)]
    private void ValidateMouKrUse(CardUseFrame use)
    {
        if(use.MouKrLiwenOrigin is{}o&&(use.Step!=ResolutionFrameStep.Completed||_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==o.FrameId)is not{}parent||!MouKrNativeMatches(use,parent)))
            throw new InvalidOperationException("Liwen must return its exact once-issued completed native use.");
    }
    [NativeUseReturn(34600)]
    private void ReturnMouKrUse(CardUseFrame use)
    {
        if(use.MouKrLiwenOrigin is not{}o)return;var f=GetActiveProgramFrame(o.FrameId);var r=f.MouKrLiwen!;
        if(!MouKrNativeMatches(use,f)||LifecycleCardUse(use.Id)is not null||r.NativeReturned||ProgramEventHistory<CardUseFinishedEvent>().Count(e=>e.ResolutionId==use.Id)!=1)
            throw new InvalidOperationException("Liwen cannot replace or repeat an issued native return.");
        ReplaceRuntimeTop(f=f with{MouKrLiwen=r with{NativeReturned=true}});
        if(use.CardAttack is not{Active:true})SettleMouKrUse(f);
    }
    private void SettleMouKrUse(ProgramSkillFrame f)
    {
        var r=f.MouKrLiwen!;
        if(!r.NativeReturned||LifecycleCardUse(r.UseId)is not null)throw new InvalidOperationException("Liwen must wait for the original native attack tail.");
        ReplaceRuntimeTop(f=f with{MouKrLiwen=r with{Stage=MouKrLiwenStage.Children,After=_movementSequence},PendingMovementContinuation=new(r.Schedule[r.Cursor].Seat,0,null)});
        AdvanceEventRulesAndQueueFact(new MouKrLiwenNativeReturnedEvent(f.Id,r.Cursor,r.UseId,r.ActionId));MouKongRong.Continue(f.Id);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup,34600)]
    private bool ReturnMouKrAttack()
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{MouKrLiwen:{Stage:MouKrLiwenStage.NativeUse,NativeReturned:true}}f)return false;
        SettleMouKrUse(f);return true;
    }
}
