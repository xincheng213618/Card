namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool IsHuiyunExactHandQuery(CharacterState actor,int? id)
    {
        if(!HasHuBanCapability||id is not{}card||card<=0||!actor.IsAlive||_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Huiyun:{}r}f||r.Stage is not(HuiyunStage.HandUseChoice or HuiyunStage.Issuing)||r.Index<0||r.Index>=r.Opportunities.Count||HuiyunCurrent(r).TargetSeat!=actor.Seat||r.Option is not(1 or 2)||!GetHand(actor).Any(c=>c.Id==card))return false;
        if(r.Stage==HuiyunStage.Issuing)return r.Native is{UseId:0,ActionId:0}n&&n.ProgramFrameId==f.Id&&n.OpportunityIndex==r.Index&&n.ActorSeat==actor.Seat&&n.CardId==card;
        return r.Stage==HuiyunStage.HandUseChoice&&(r.Option==2||HuiyunCurrent(r).ShownCardId==card);
    }
    private IReadOnlyList<LegalAction> HuiyunActions(ProgramSkillFrame f)
    {
        var r=f.Huiyun!;var o=HuiyunCurrent(r);if(r.Stage!=HuiyunStage.HandUseChoice||r.Option==3||!_players[o.TargetSeat].IsAlive||_winner!=Winner.None)return [];
        var ids=GetHand(_players[o.TargetSeat]).Where(c=>r.Option==2||c.Id==o.ShownCardId).Select(c=>c.Id).ToArray();
        return Array.AsReadOnly(ids.SelectMany(id=>BuildLegalActions(_players[o.TargetSeat],includeProgramActions:false,selectedPhysicalCardId:id)).Where(a=>a.CardId is{}id&&ids.Contains(id)&&a.ConversionSource is null&&(a.AdditionalConversionSources?.Count??0)==0&&a.MinCardCount<=1&&a.MaxCardCount<=1&&a.Kind is not(LegalActionKind.EndPlay or LegalActionKind.Recast or LegalActionKind.UseProgramSkill or LegalActionKind.UseEquipmentEffect or LegalActionKind.RevealGeneral)).Select(HuiyunFreeze.Action).ToArray());
    }
    private IReadOnlyList<int> HuiyunInitialTargets(HuiyunNativeInvoice n)
    {
        var a=n.Plan;var actor=_players[n.ActorSeat];var card=GetAttackCard(n.CardId);var kind=n.Kind;
        if(a.Kind==LegalActionKind.Peach)return [n.ActorSeat];
        if(a.Kind is not(LegalActionKind.BarbarianAssault or LegalActionKind.ArrowBarrage or LegalActionKind.PeachGarden or LegalActionKind.FiveGrains))return GetImplicitSelfCardUseTargets(kind,n.ActorSeat,a.TargetSeats);
        var offensive=a.Kind is LegalActionKind.BarbarianAssault or LegalActionKind.ArrowBarrage;
        var seats=Enumerable.Range(offensive?1:0,_playerCount-(offensive?1:0)).Select(i=>_players[TableStepSeat(n.ActorSeat, i)]).Where(p=>p.IsAlive&&(!offensive||!HasCardPolicy(p,SkillProgramCardPolicyKind.ExcludeGlobalTarget,kind))&&!IsCardTargetProhibited(p,kind,card.Suit,SuitColor(EffectiveSuit(actor,card)))&&!HasBeneficiarySuitShield(actor.Seat,p.Seat,EffectiveSuit(actor,card))).Select(p=>p.Seat).ToArray();
        if(!offensive)seats=ApplyTurnCardGroupTargetRestrictions(actor,seats).ToArray();if(a.Kind==LegalActionKind.FiveGrains)seats=seats.Take(_cardZones.Count(CardLocation.DrawPile)+_cardZones.Count(CardLocation.DiscardPile)).ToArray();return Array.AsReadOnly(seats);
    }
    private void BindHuiyunFollowupUse(CardUseFrame use)
    {
        var at=_resolutionStack.FindIndex(x=>x.Id==use.Id);
        if(at<1||_resolutionStack[at-1]is not ProgramSkillFrame{Huiyun:{Stage:HuiyunStage.Issuing,Native:{UseId:0,ActionId:0}n}r}f)return;
        if(use.Action is not{Type:CardActionType.Use}a||n.ProgramFrameId!=f.Id||n.OpportunityIndex!=r.Index||n.ActorSeat!=HuiyunCurrent(r).TargetSeat||n.Source!=r.Origin.Source||n.GameplayHash!=f.GameplayHash||use.SourceSeat!=n.ActorSeat||use.CardId!=n.CardId||use.CardKind!=n.Kind||use.PhysicalCardIds is not[var physical]||physical!=n.CardId||a.ActorSeat!=n.ActorSeat||a.ProviderSeat!=n.ActorSeat||a.RequesterSeat is not null||a.PhysicalCards is not[var cost]||cost.CardId!=n.CardId||cost.From!=CardLocation.Hand(n.ActorSeat)||a.ConversionChain.Count!=0||!use.TargetSeats.SequenceEqual(HuiyunInitialTargets(n)))
            throw new InvalidOperationException("Huiyun follow-up changed its consenting target's one natural original Hand entity, native targets or actual provider.");
        var bound=n with{UseId=use.Id,ActionId=a.ActionId};ReplaceRuntimeFrame(f.Id,f with{Huiyun=r with{Stage=HuiyunStage.NativeUse,Native=bound}});UpdateLifecycleCardUse(use.Id,u=>u with{HuiyunFollowup=bound,UnlimitedUse=IsSlashCard(u.CardKind)||u.UnlimitedUse});AdvanceEventRulesAndQueueFact(new HuiyunNativeIssuedEvent(bound));
    }
    private bool SameHuiyunNative(HuiyunNativeInvoice a,HuiyunNativeInvoice b)=>a.ProgramFrameId==b.ProgramFrameId&&a.OpportunityIndex==b.OpportunityIndex&&a.Source==b.Source&&a.GameplayHash==b.GameplayHash&&a.ActorSeat==b.ActorSeat&&a.CardId==b.CardId&&a.Kind==b.Kind&&a.BeforePayment==b.BeforePayment&&a.UseId==b.UseId&&a.ActionId==b.ActionId&&System.Text.Json.JsonSerializer.Serialize(a.Plan)==System.Text.Json.JsonSerializer.Serialize(b.Plan);
    private bool HuiyunNativeUseRidesOn(CardUseFrame use,ProgramSkillFrame f)
    {
        if(use.HuiyunFollowup is not{}n||f.Huiyun is not{Stage:HuiyunStage.NativeUse,Native:{}pending}r||!SameHuiyunNative(n,pending)||r.NativeCompleted||n.ProgramFrameId!=f.Id||n.OpportunityIndex!=r.Index||n.ActorSeat!=HuiyunCurrent(r).TargetSeat||n.Source!=r.Origin.Source||n.GameplayHash!=f.GameplayHash||use.Id!=n.UseId||use.CardId!=n.CardId||use.Action is not{Type:CardActionType.Use}current||current.ActionId!=n.ActionId||current.ProviderSeat!=n.ActorSeat||!ShownEntityUseActorMatches(use,n.ActorSeat,n.ActorSeat)||use.PhysicalCardIds is not[var id]||id!=n.CardId||ProgramEventHistory<HuiyunNativeIssuedEvent>().Count(e=>SameHuiyunNative(e.Invoice,n))!=1)return false;
        var a=current;
        if(use.CurrentSlashFirePolicy is{}fire){AssertCurrentSlashFirePolicy(use);a=fire.OriginalAction;}
        return a.EffectiveKind==n.Kind&&a.PhysicalCards is[{CardId:var cost,From:{Zone:CardZoneKind.Hand,OwnerSeat:var owner}}]&&cost==n.CardId&&owner==n.ActorSeat&&a.ConversionChain.Count==0;
    }
    private bool HasIssuedHuiyunFollowup(long? id,int actorSeat)=>id is{}native&&LifecycleCardUse(native)is{HuiyunFollowup:{}n}use&&n.ActorSeat==actorSeat&&_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==n.ProgramFrameId)is{}parent&&HuiyunNativeUseRidesOn(use,parent);
    [NativeUseValidation(27380)]private void ValidateHuiyunNativeUse(CardUseFrame use)
    {if(use.HuiyunFollowup is{}n&&(_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==n.ProgramFrameId)is not{}f||!HuiyunNativeUseRidesOn(use,f)))throw new InvalidOperationException("Huiyun lost the genuine natural-card child and exact once-issued owning native return.");}
    [NativeUseReturn(27380)]private void ReturnHuiyunNativeUse(CardUseFrame use)
    {
        if(use.HuiyunFollowup is not{}n)return;var f=GetActiveProgramFrame(n.ProgramFrameId);var r=f.Huiyun!;
        if(r.Stage!=HuiyunStage.NativeUse||r.NativeCompleted||r.Native is null||!SameHuiyunNative(r.Native,n)||ProgramEventHistory<CardUseFinishedEvent>().Count(e=>e.ResolutionId==use.Id)!=1||ProgramEventHistory<HuiyunNativeReturnedEvent>().Any(e=>e.Invoice.UseId==n.UseId))throw new InvalidOperationException("Huiyun cannot return or pay its natural-card use twice.");
        ReplaceRuntimeTop(f=f with{Huiyun=r with{NativeCompleted=true}});if(use.CardAttack is{Active:true})return;SettleHuiyunNative(f);
    }
    private void SettleHuiyunNative(ProgramSkillFrame f)
    {
        var r=f.Huiyun!;if(!r.NativeCompleted||r.Native is not{}n||LifecycleCardUse(n.UseId)is not null)throw new InvalidOperationException("Huiyun must wait for the whole genuine native use and attack tail.");
        ReplaceRuntimeTop(f with{Huiyun=r with{Stage=HuiyunStage.NativeChildren,Before=n.BeforePayment,After=_movementSequence},PendingMovementContinuation=new(n.ActorSeat,0,null)});AdvanceEventRulesAndQueueFact(new HuiyunNativeReturnedEvent(n,_movementSequence));
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword,27380)]private bool ReturnHuiyunAttack(AttackCompletionReceipt completion)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Huiyun:{Stage:HuiyunStage.NativeUse,NativeCompleted:true,Native:{}n}}f||n.UseId!=completion.ResolutionId)return false;SettleHuiyunNative(f);return true;
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,27381)]private void ExpireHuiyunAlcohol(IGameEvent fact)
    {
        if(fact is not ActualTurnCursorReturnedEvent ended||!HasHuBanCapability)return;
        foreach(var actor in ProgramEventHistory<HuiyunNativeIssuedEvent>().Select(e=>e.Invoice).Where(n=>n.Plan.Kind==LegalActionKind.Alcohol&&ProgramEventHistory<JianjiOrdinaryAlcoholPaidEvent>().Any(p=>p.NativeUseId==n.UseId&&p.ActualTurn==ended.ActualTurn)).Select(n=>n.ActorSeat).Distinct().ToArray())
            if(_players[actor].HasAlcoholEffect){_players[actor].HasAlcoholEffect=false;AdvanceEventRulesAndQueueFact(new AlcoholExpiredEvent(actor));}
    }
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.SlashCountExemption, 40)]
    private bool HasIssuedHuiyunSlashDebitExemption(long useId, int actorSeat) =>
        HasIssuedHuiyunFollowup(useId, actorSeat);

}

public sealed partial class GameEngine
{
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaBeforeLimit, 100)]
    private bool HuiyunSlashQuotaBeforeLimit(ProgramSlashQuotaQuery query) =>
        IsHuiyunExactHandQuery(query.Actor, query.PhysicalCardIds is [var huiyunCardId] ? huiyunCardId : query.PhysicalCard?.Id);
}
