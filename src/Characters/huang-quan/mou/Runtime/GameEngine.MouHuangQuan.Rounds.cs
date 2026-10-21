namespace CardGame.Core;
public sealed partial class GameEngine
{
    private void IssueMouHqOriginalEffect(ProgramSkillFrame f)
    {
        if(!ValidMouHq(f)||f.MouHuangQuan is not{Paid:true,Stage:MouHqStage.GiveChildren}r||
            !TryMouHqOriginalUse(f.OwnerSeat,f.WindowContext,out var use,out var window)||use.Id!=r.UseId||use.Action!.ActionId!=r.ActionId||
            ProgramEventHistory<MouHqEffectIssuedEvent>().Any(e=>e.Grant.FrameId==f.Id))throw new InvalidOperationException("Qiaodui requires the exact once-paid original Use after all Give children return.");
        var grant=new MouHqEffectGrant(f.Id,r.Source,r.GameplayHash,r.WindowId,r.UseId,r.ActionId,r.Recipient,r.Count);
        if(!r.Outgoing)
        {
            if(use.SourceSeat==f.OwnerSeat||!use.Action.EffectiveDesignatedTargetSeats.Contains(f.OwnerSeat))throw new InvalidOperationException("Incoming Qiaodui lost its original other-user target identity.");
            if(use.MouHqIneffective is null)UpdateLifecycleCardUse(use.Id,u=>u with{MouHqIneffective=grant});
            AdvanceEventRulesAndQueueFact(new MouHqEffectIssuedEvent(grant,false));
            foreach(var target in use.TargetSeats.Distinct())AdvanceEventRulesAndQueueFact(new CardEffectSkippedEvent(use.Id,use.SourceSeat,target,use.CardKind,CardEffectSkipReason.SkillNullified));return;
        }
        if(use.SourceSeat!=f.OwnerSeat||use.MouHqRounds is{Started:>0})throw new InvalidOperationException("Outgoing Qiaodui cannot attach another card or restart an effect already in progress.");
        var old=use.MouHqRounds;var native=old?.Native??new ExtraCardEffectState{Source=r.Source,ProgramFrameId=f.Id,ActionId=r.ActionId,Simple=window.SimpleContinuation,Trick=window.TrickContinuation};
        UpdateLifecycleCardUse(use.Id,u=>u with{MouHqRounds=new(r.ActionId,native){Grants=(old?.Grants??[]).Append(grant).ToArray()}});
        AdvanceEventRulesAndQueueFact(new MouHqEffectIssuedEvent(grant,true));
    }
    private bool ValidMouHqEffectGrant(CardUseFrame use,MouHqEffectGrant g,bool repeats)=>use.Action is{Type:CardActionType.Use}a&&g.UseId==use.Id&&g.ActionId==a.ActionId&&g.Source.SkillId==MouHqQiaodui&&g.Source.BindingId=="give-for-original-effect"&&
        g.WindowId>use.Id&&g.FrameId>g.WindowId&&g.GivenCount is>=0 and<=2&&g.Recipient!=g.Source.OwnerSeat&&_contentRegistry.GetSkill(g.Source.SkillId).Program?.GameplayHash==g.GameplayHash&&
        (repeats?a.ActorSeat==g.Source.OwnerSeat&&a.EffectiveDesignatedTargetSeats.Any(t=>t!=g.Source.OwnerSeat):a.ActorSeat!=g.Source.OwnerSeat&&a.EffectiveDesignatedTargetSeats.Contains(g.Source.OwnerSeat))&&
        ProgramEventHistory<MouHqEffectIssuedEvent>().Count(e=>e.Grant==g&&e.Repeats==repeats)==1&&
        ProgramEventHistory<MouHqCommittedEvent>().Count(e=>e.FrameId==g.FrameId&&e.OwnerSeat==g.Source.OwnerSeat&&e.Recipient==g.Recipient&&e.Count==g.GivenCount)==1;
    private bool IsMouHqUseIneffective(CardUseFrame use)
    {
        if(use.MouHqIneffective is not{}g)return false;
        if(!ValidMouHqEffectGrant(use,g,false))throw new InvalidOperationException("Qiaodui whole-use ineffectiveness lost its original paid target-owner proof.");return true;
    }
    private bool HasUnstartedMouHqRound(long id)=>LifecycleCardUse(id)is{MouHqRounds:{}r,Action:{}a}u&&a.ActionId==r.ActionId&&r.Started<r.Grants.Count&&_winner==Winner.None&&!IsMouHqUseIneffective(u)&&!IsNamedAmbushUseIneffective(u)&&!IsProgramUseIneffective(u);
    private bool IsMouHqRound(long id)=>LifecycleCardUse(id)?.MouHqRounds is{Active:true};
    private void CaptureMouHqAttack(CardAttackHandle attack)
    {
        if(LifecycleCardUse(attack.ResolutionId)is not{MouHqRounds:{Started:0}r,CardAttack:{}original}u||!IsSlashCard(u.CardKind)||r.Native.OriginalAttacks.Any(s=>s.TargetIndex==u.TargetIndex))return;
        UpdateLifecycleCardUse(u.Id,current=>current with{MouHqRounds=r with{Native=r.Native with{OriginalAttacks=r.Native.OriginalAttacks.Append(new(u.TargetIndex,original)).ToArray(),Fangtian=current.Continuations.FangtianHalberd??r.Native.Fangtian}}});
    }
    private void CompleteMouHqRound(long id)
    {
        if(LifecycleCardUse(id)is not{MouHqRounds:{Active:true,Started:>0}r}u)return;
        if(_resolutionStack.LastOrDefault()?.Id!=id||u.NativeDraw is not null||u.NativeCardEffectDraw is not null||u.WumiaoFlood is not null||u.GuanduCardEffect is not null||u.DragonBoatCardEffect is not null)throw new InvalidOperationException("Qiaodui cannot finish an original effect with native children still pending.");
        UpdateLifecycleCardUse(id,current=>current with{MouHqRounds=r with{Active=false}});AdvanceEventRulesAndQueueFact(new MouHqEffectRoundCompletedEvent(id,r.ActionId,r.Grants[r.Started-1].FrameId,r.Started));
    }
    private bool TryBeginMouHqRound(long id,bool afterAttack)
    {
        if(!HasUnstartedMouHqRound(id)||LifecycleCardUse(id)is not{MouHqRounds:{}r,Action:{}a}u||u.ExtraCardEffect is{WholeGroupRepeated:false}||HasUnstartedFeiYiExtraEffect(id)||HasUnstartedZixiExtraEffect(id)||HasUnstartedMoQuanyuExtraRound(id)||HasUnstartedMouBingcaiExtraEffect(id)||HasUnstartedSoleIncomingEffect(id)||
            HasRemainingSequentialTrickTargets(id)||HasRemainingAdjustedSimpleTargets(id))return false;
        if(_resolutionStack.LastOrDefault()?.Id!=id||r.Active)throw new InvalidOperationException("Qiaodui must await the original native effect and its actual children.");
        var grant=r.Grants[r.Started];if(!ValidMouHqEffectGrant(u,grant,true))throw new InvalidOperationException("Qiaodui lost the original-use extra-effect grant.");
        // Delayed placement has one actual entity. The extra placement cannot duplicate it after it entered Judgment.
        if(IsDelayedCard(u.CardKind))
        {
            UpdateLifecycleCardUse(id,current=>current with{MouHqRounds=r with{Started=r.Started+1}});
            AdvanceEventRulesAndQueueFact(new MouHqEffectRoundStartedEvent(id,a.ActionId,grant.FrameId,r.Started+1));AdvanceEventRulesAndQueueFact(new MouHqEffectRoundCompletedEvent(id,a.ActionId,grant.FrameId,r.Started+1));return false;
        }
        var previous=NativeExtraEffectState(u);var native=r.Native with{WholeGroupRepeated=true,MaterialClaims=previous?.MaterialClaims??r.Native.MaterialClaims,FrozenGroupDamageSourceSeat=previous?.FrozenGroupDamageSourceSeat??r.Native.FrozenGroupDamageSourceSeat};
        ReplaceRuntimeFrame(id,u with{MouHqRounds=r with{Started=r.Started+1,Active=true,Native=native},TargetIndex=0,Step=ResolutionFrameStep.ResolvingEffect,FireAttackSelection=null,ColorFireAttack=null,UnexpectedAssaultReveal=null,VirtualBasicEffectApplied=false});
        AdvanceEventRulesAndQueueFact(new MouHqEffectRoundStartedEvent(id,a.ActionId,grant.FrameId,r.Started+1));AdvanceEventRulesAndQueueFact(new ExtraCardEffectStartedEvent(id,a.ActionId,0,u.TargetSeats.FirstOrDefault(u.SourceSeat),u.CardKind));
        if(afterAttack){ActiveCardAttack=null;ActiveDuel=null;ClearPendingDecision();}
        if(IsSlashCard(u.CardKind))
        {
            var first=native.OriginalAttacks.OrderBy(s=>s.TargetIndex).FirstOrDefault(s=>_players[s.Attack.TargetSeat].IsAlive&&!IsCardEffectIneffective(id,s.Attack.TargetSeat));
            if(first is null){CompleteMouHqRound(id);return false;}
            var prepared=u.PreparedTargetAttacks?.Select((state,index)=>native.OriginalAttacks.SingleOrDefault(s=>s.TargetIndex==index)?.Attack??state).ToArray();
            UpdateLifecycleCardUse(id,current=>current with{TargetIndex=first.TargetIndex,PreparedTargetAttacks=prepared is null?null:Array.AsReadOnly(prepared)});
            if(native.Fangtian is{}fang){UpdateCardContinuations(id,c=>c with{FangtianHalberd=fang with{Active=true,CurrentAttackOwnerId=id}});ActiveFangtianHalberd=new(this,id);}
            UpdateCardAttackState(id,_=>ResetExtraCardEffectAttack(first.Attack));var attack=new CardAttackHandle(this,id);ActiveCardAttack=attack;ContinueSlashDefense(attack);return true;
        }
        if(native.Trick is{}trick)
        {
            ActiveGroupCard=null;ActiveBorrowedSword=null;var card=GetTrickRepresentation(id,u.CardId,requireProcessing:true);BeginNullificationWindow(id,card,u.SourceSeat,u.TargetSeats,trick.ActionKind,null,trick.RequiredCardKind,u.CardKind);return true;
        }
        if(native.Simple is{}simple)
        {
            if(u.TargetSeats.Count>1&&simple.Effect is SimpleCardUseEffect.Recovery or SimpleCardUseEffect.Alcohol or SimpleCardUseEffect.GuanduIntoxication)UpdateLifecycleCardUse(id,current=>current with{AdjustedSimpleContinuation=simple});
            ContinueSimpleCardUse(id,simple);return true;
        }
        throw new InvalidOperationException("Qiaodui lost its original native card-effect continuation.");
    }
    private bool ContinueMouHqFangtian(CardAttackHandle attack)
    {
        if(LifecycleCardUse(attack.ResolutionId)is not{MouHqRounds:{Active:true}r}u)return false;
        var seed=r.Native.OriginalAttacks.SingleOrDefault(s=>s.TargetIndex==u.TargetIndex);if(seed is null){CompleteAttack(attack);return true;}
        UpdateCardAttackState(u.Id,_=>ResetExtraCardEffectAttack(seed.Attack));ContinueSlashDefense(attack);return true;
    }
    private void AssertMouHqRounds()
    {
        foreach(var u in _resolutionStack.OfType<CardUseFrame>())
        {
            if(u.MouHqIneffective is not null)IsMouHqUseIneffective(u);
            if(u.MouHqRounds is not{}r)continue;
            if(u.Action?.ActionId!=r.ActionId||r.Started<0||r.Started>r.Grants.Count||r.Active&&r.Started==0||r.Grants.Count==0||r.Grants.Any(g=>!ValidMouHqEffectGrant(u,g,true))||r.Grants.Select(g=>g.FrameId).Distinct().Count()!=r.Grants.Count||
                ProgramEventHistory<MouHqEffectRoundStartedEvent>().Count(e=>e.UseId==u.Id)!=r.Started||ProgramEventHistory<MouHqEffectRoundCompletedEvent>().Count(e=>e.UseId==u.Id)!=r.Started-(r.Active?1:0))throw new InvalidOperationException("Qiaodui lost its exact original-use grant or native extra-effect cursor.");
        }
    }
}
