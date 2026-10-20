namespace CardGame.Core;
public sealed partial class GameEngine
{
    // This accessor preserves the old one-round protocol and only selects the
    // new owning extension after its own precise native round has started.
    private ExtraCardEffectState? NativeExtraEffectState(CardUseFrame use)=>use.SoleIncomingEffects is {Active:true}incoming?incoming.Native:use.MouBingcaiRounds is {Active:true}bingcai?bingcai.Native:use.MoQuanyuExtraRound is {Started:true}mo?mo.Native:use.ZixiEffectRounds is {Started:>0}z?z.Native:use.FeiYiRounds is {Started:>0}r?r.Native:use.ExtraCardEffect??use.FeiYiRounds?.Native??use.ZixiEffectRounds?.Native??use.MoQuanyuExtraRound?.Native??use.MouBingcaiRounds?.Native??use.SoleIncomingEffects?.Native;
    private void UpdateNativeExtraEffectState(long id,Func<ExtraCardEffectState,ExtraCardEffectState> update)
    {
        UpdateLifecycleCardUse(id,use=>use.SoleIncomingEffects is{Active:true}incoming?use with{SoleIncomingEffects=incoming with{Native=update(incoming.Native)}}:use.MouBingcaiRounds is{Active:true}bingcai?use with{MouBingcaiRounds=bingcai with{Native=update(bingcai.Native)}}:
            use.MoQuanyuExtraRound is{Started:true}mo?use with{MoQuanyuExtraRound=mo with{Native=update(mo.Native)}}:
            use.ZixiEffectRounds is{Started:>0}z?use with{ZixiEffectRounds=z with{Native=update(z.Native)}}:
            use.FeiYiRounds is{Started:>0}r?use with{FeiYiRounds=r with{Native=update(r.Native)}}:
            use.ExtraCardEffect is{}old?use with{ExtraCardEffect=update(old)}:
            use.FeiYiRounds is{}pending?use with{FeiYiRounds=pending with{Native=update(pending.Native)}}:
            use.ZixiEffectRounds is{}zixi?use with{ZixiEffectRounds=zixi with{Native=update(zixi.Native)}}:
            use.MoQuanyuExtraRound is{}pendingMo?use with{MoQuanyuExtraRound=pendingMo with{Native=update(pendingMo.Native)}}:
            use.SoleIncomingEffects is{}pendingIncoming?use with{SoleIncomingEffects=pendingIncoming with{Native=update(pendingIncoming.Native)}}:
            use.MouBingcaiRounds is{}pendingBingcai?use with{MouBingcaiRounds=pendingBingcai with{Native=update(pendingBingcai.Native)}}:throw new InvalidOperationException("A native repeated effect needs its owning issued receipt."));
    }
    private void BindFeiYiRankEffects(long id,ProgramTrickContinuation trick)
    {
        if(!HasFeiYiHezhong||LifecycleCardUse(id) is not{Action:{Type:CardActionType.Use}action}use||!IsOrdinaryTrick(use.CardKind)||use.CardKind==CardKind.Nullification||
            action.ActorSeat!=use.SourceSeat||action.EffectiveKind!=use.CardKind||action.EffectiveRank is not{}rank||
            use.FeiYiRounds is{Started:>0}||_resolutionStack.LastOrDefault()?.Id!=id)return;
        var acceptedActor=FeiYiAcceptedActor(use);
        if(acceptedActor is not{}originalActor)throw new InvalidOperationException("Hezhong lost the original accepted actor or its exact native replacement chain.");
        var grants=ProgramEventHistory<FeiYiRankEffectIssuedEvent>().Where(g=>g.Source.OwnerSeat==originalActor&&g.Turn==_turnNumber&&g.TurnOwner==_turnProgression.OwnerSeat&&
            (g.Greater?rank>g.Rank:rank<g.Rank)&&!ProgramEventHistory<FeiYiRankEffectBoundEvent>().Any(e=>e.GrantId==g.FrameId)).ToArray();
        if(grants.Length==0)return;
        var old=use.FeiYiRounds;
        if(old is not null&&(old.ActionId!=action.ActionId||old.AcceptedActorSeat!=originalActor||old.Trick!=trick||old.Turn!=_turnNumber||old.TurnOwner!=_turnProgression.OwnerSeat))throw new InvalidOperationException("Hezhong cannot append grants to a different accepted effect.");
        var native=old?.Native??new ExtraCardEffectState{Source=grants[0].Source,ProgramFrameId=grants[0].FrameId,ActionId=action.ActionId,PaidCardId=0,Trick=trick};
        UpdateLifecycleCardUse(id,current=>current with{FeiYiRounds=new(action.ActionId,_turnNumber,_turnProgression.OwnerSeat,originalActor,trick,native){Grants=(old?.Grants??[]).Concat(grants.Select(g=>g.FrameId)).ToArray()}});
        foreach(var grant in grants)AdvanceEventRulesAndQueueFact(new FeiYiRankEffectBoundEvent(grant.FrameId,id,action.ActionId,rank,originalActor));
    }
    private int? FeiYiAcceptedActor(CardUseFrame use)
    {
        var declared=ProgramEventHistory<CardUseDeclaredEvent>().Where(e=>e.ResolutionId==use.Id&&e.CardId==use.CardId&&e.CardKind==use.CardKind).ToArray();
        return declared is[var original]&&use.Action is{Type:CardActionType.Use}action&&action.ActorSeat==use.SourceSeat&&
            MatchesDeclaredActualDamageUse(use,action.ActorSeat,action.ProviderSeat)?original.SourceSeat:null;
    }
    private bool HasUnstartedFeiYiExtraEffect(long id)=>LifecycleCardUse(id) is{FeiYiRounds:{}r,Action:{}a}use&&r.ActionId==a.ActionId&&r.Started<r.Grants.Count&&!IsNamedAmbushUseIneffective(use)&&_winner==Winner.None;
    private bool IsFeiYiExtraEffectRound(long id)=>LifecycleCardUse(id)?.FeiYiRounds is{Started:>0};
    private int NativeExtraEffectRoundOrdinal(long id)
    {
        var use=LifecycleCardUse(id);var old=use?.ExtraCardEffect?.WholeGroupRepeated==true?1:0;
        var feiyi=use?.FeiYiRounds is{Started:>0}r?r.Started:0;
        return checked((use?.SoleIncomingEffects?.Started??0)+old+feiyi+(use?.ZixiEffectRounds?.Started??0)+(use?.MoQuanyuExtraRound?.Started==true?1:0)+(use?.MouBingcaiRounds?.Started??0));
    }
    private bool TryBeginFeiYiExtraEffectRound(long id,bool afterAttack)
    {
        if(!HasUnstartedFeiYiExtraEffect(id)||LifecycleCardUse(id) is not{FeiYiRounds:{}r,Action:{}action}use||
            use.ExtraCardEffect is{WholeGroupRepeated:false}||r.Trick is null||HasRemainingSequentialTrickTargets(id)||HasRemainingAdjustedSimpleTargets(id))return false;
        if(_resolutionStack.LastOrDefault()?.Id!=id)throw new InvalidOperationException("Hezhong cannot pass an unfinished native child.");
        var native=r.Native with{WholeGroupRepeated=true,MaterialClaims=r.Started==0&&use.ExtraCardEffect is{}fengzi?fengzi.MaterialClaims:r.Native.MaterialClaims,
            FrozenGroupDamageSourceSeat=r.Started==0?use.ExtraCardEffect?.FrozenGroupDamageSourceSeat??r.Native.FrozenGroupDamageSourceSeat:r.Native.FrozenGroupDamageSourceSeat};
        ReplaceRuntimeFrame(id,use with{FeiYiRounds=r with{Started=r.Started+1,Native=native},TargetIndex=0,Step=ResolutionFrameStep.ResolvingEffect,
            FireAttackSelection=null,ColorFireAttack=null,UnexpectedAssaultReveal=null,VirtualBasicEffectApplied=false});
        AdvanceEventRulesAndQueueFact(new FeiYiEffectRoundStartedEvent(id,action.ActionId,r.Started+1));
        if(afterAttack){ActiveCardAttack=null;ActiveDuel=null;ClearPendingDecision();}
        ActiveGroupCard=null;ActiveBorrowedSword=null;
        var card=GetTrickRepresentation(id,use.CardId,requireProcessing:true);
        BeginNullificationWindow(id,card,use.SourceSeat,use.TargetSeats,r.Trick.ActionKind,null,r.Trick.RequiredCardKind,use.CardKind);return true;
    }
    private void AssertFeiYiEffectRounds()
    {
        foreach(var use in _resolutionStack.OfType<CardUseFrame>().Where(u=>u.FeiYiRounds is not null))
        {
            var r=use.FeiYiRounds!;
            if(use.Action is not{Type:CardActionType.Use}a||a.ActionId!=r.ActionId||!IsOrdinaryTrick(use.CardKind)||use.CardKind==CardKind.Nullification||
                r.Turn!=_turnNumber||r.TurnOwner!=_turnProgression.OwnerSeat||r.AcceptedActorSeat!=FeiYiAcceptedActor(use)||r.Started<0||r.Started>r.Grants.Count||r.Grants.Count<1||r.Grants.Distinct().Count()!=r.Grants.Count||
                r.Native.ActionId!=r.ActionId||r.Native.Trick!=r.Trick||r.Native.WholeGroupRepeated!=(r.Started>0)||
                r.Grants.Any(g=>ProgramEventHistory<FeiYiRankEffectBoundEvent>().Count(e=>e.GrantId==g&&e.UseFrameId==use.Id&&e.ActionId==a.ActionId&&e.AcceptedActorSeat==r.AcceptedActorSeat&&e.EffectiveRank==a.EffectiveRank)!=1||
                    ProgramEventHistory<FeiYiRankEffectIssuedEvent>().Count(e=>e.FrameId==g&&e.Source.OwnerSeat==r.AcceptedActorSeat&&e.Turn==r.Turn&&e.TurnOwner==r.TurnOwner&&a.EffectiveRank is{}rank&&(e.Greater?rank>e.Rank:rank<e.Rank))!=1)||
                !Enumerable.Range(1,r.Started).SequenceEqual(ProgramEventHistory<FeiYiEffectRoundStartedEvent>().Where(e=>e.UseFrameId==use.Id&&e.ActionId==r.ActionId).Select(e=>e.Round)))
                throw new InvalidOperationException("Hezhong lost its real accepted ordinary trick, exact grant invoices or ordered additional native rounds.");
        }
    }
}
