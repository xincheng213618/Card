namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed record QinLangUseOption(LegalActionKind Kind,IReadOnlyList<int> TargetSeats,int? TargetCardId,CardKind? RequiredKind,string Description);
    private IReadOnlyList<QinLangUseOption> QinLangUseOptions(ProgramSkillFrame f,QinLangXianyingGrantedEvent grant)
    {
        var actor=_players[f.OwnerSeat];var kind=grant.Name;
        if(!actor.IsAlive||_winner!=Winner.None||!QinLangImitable(kind)||IsCardUseForbidden(actor.Seat,kind,CardActionType.Use))return [];
        var result=new List<QinLangUseOption>();
        if(IsSlashCard(kind))
        {
            foreach(var target in _players.Where(p=>CanUseSlashTarget(actor,p,new(0,kind,Suit.None,0),effectiveKind:kind,noEffectiveRank:true,physicalCardIds:[])&&
                !IsSelfTargetForbiddenAction(actor,kind,[p.Seat])&&!IsCardTargetProhibited(p,kind,Suit.None,null)&&!HasBeneficiarySuitShield(actor.Seat,p.Seat,Suit.None)))
                result.Add(new(LegalActionKind.Slash,[target.Seat],null,CardKind.Dodge,$"视为对{target.Name}使用【杀】"));
        }
        else if(kind is CardKind.Peach or CardKind.Alcohol)
        {
            if((kind!=CardKind.Peach||IsRuleWounded(actor))&&!HasSelfCardTargetProhibition(actor.Seat)&&!HasBeneficiarySuitShield(actor.Seat,actor.Seat,Suit.None)&&
                !IsCardTargetProhibited(actor,kind,Suit.None,null)&&!IsDirectedCardTargetProhibited(actor.Seat,actor.Seat,kind))
                result.Add(new(kind==CardKind.Peach?LegalActionKind.Peach:LegalActionKind.Alcohol,[actor.Seat],null,null,$"视为使用【{CardCatalog.Get(kind).DisplayName}】"));
        }
        else
        {
            IReadOnlyList<ProgramOrdinaryTrickUseOption> options;
            if(kind==CardKind.FiveGrains)
            {
                var targets=Enumerable.Range(0,_players.Count).Select(i=>TableStepSeat(actor.Seat, i)).Where(s=>_players[s].IsAlive&&
                    (!HasTurnCardTargetRestriction(actor.Seat,SkillProgramCardTargetRestriction.SelfOnly)||s==actor.Seat)&&!HasCardPolicy(_players[s],SkillProgramCardPolicyKind.ExcludeGlobalTarget,kind)&&
                    !IsDirectedCardTargetProhibited(actor.Seat,s,kind)&&!IsCardTargetProhibited(_players[s],kind,Suit.None,null)&&!HasBeneficiarySuitShield(actor.Seat,s,Suit.None)).ToArray();
                options=targets.Length==0||IsSelfTargetForbiddenAction(actor,kind,targets)||HasIssuedPlayPhaseUseBan(actor.Seat)||IsNextActualUseTrickForbidden(actor.Seat,kind)?[]:
                    NextActualUseOrdinaryTrickOptions(actor,Array.AsReadOnly(new[]{new ProgramOrdinaryTrickUseOption(new("xianying.five-grains"),kind,LegalActionKind.FiveGrains,Array.AsReadOnly(targets),null,null,"视为使用【五谷丰登】")}),Suit.None,null,Suit.None,false);
            }
            else options=BuildProgramOrdinaryTrickUseOptions(actor,kind,Suit.None,enforceUsePermission:true,beneficiaryShieldSuit:Suit.None,actualEffectiveColor:null,hasActualColor:true,physicalCardIds:[],includeNextActualUseAdjustment:true);
            foreach(var option in options)result.Add(new(option.ActionKind,option.TargetSeats.Count==0&&kind==CardKind.DrawTwo?[actor.Seat]:option.TargetSeats,option.TargetCardId,option.RequiredCardKind,option.Description));
        }
        return Array.AsReadOnly(result.ToArray());
    }
    private bool IsQinLangXianyingSlashQuery(CharacterState actor)=>HasQinLangCapability&&_resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()is{QinLangXianying:{Stage:QinLangXianyingStage.EndChoice}r}f&&f.OwnerSeat==actor.Seat&&r.ActualTurn==_turnNumber&&
        _resolutionStack.LastOrDefault()?.Id==f.Id&&r.Grants.Any(g=>!QinLangGrantConsumed(g.FrameId))&&ValidQinLang(f);
    private void BeginQinLangXianyingUse(ProgramSkillFrame f,QinLangXianyingUseOrigin origin,IReadOnlyList<int> targets)
    {
        if(_resolutionStack.LastOrDefault()?.Id!=f.Id||!MatchesQinLangXianying(f,origin)||origin.CardUseFrameId!=_resolutionSequence+1||string.Join(',',targets)!=origin.TargetSignature)
            throw new InvalidOperationException("Xianying lost its exact committed grant and selected native use.");
        var kind=origin.CardKind;var actor=_players[origin.ActorSeat];var card=new Card(0,kind,Suit.None,0);
        var id=BeginCardUse(card,actor.Seat,targets,kind,physicalCardIds:[],qinLangXianying:origin);
        if(id!=origin.CardUseFrameId)throw new InvalidOperationException("Xianying lost its reserved native use identity.");
        if(kind==CardKind.Peach){BeginSimpleCardUse(id,new(0,SimpleCardUseEffect.Recovery,1));return;}
        if(kind==CardKind.Alcohol){BeginSimpleCardUse(id,new(0,SimpleCardUseEffect.Alcohol));return;}
        if(IsSlashCard(kind))
        {
            var currentTargets=LifecycleCardUse(id)!.TargetSeats;var attack=new CardAttackHandle(this,id,actor.Seat,currentTargets[0],null,1+actor.AlcoholDamageBonus,kind,ignoresArmor:HasCardArmorBypass(actor,_players[currentTargets[0]],kind));
            CaptureProgramAlcoholConsumption(id,actor);actor.HasAlcoholEffect=false;ActiveCardAttack=attack;CaptureProgramAdjustedSlashBaseDamage(attack);
            MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat,kind);AdvanceEventRulesAndQueueFact(new CardUsedEvent(0,kind,actor.Seat,currentTargets[0]));TryMarkProgramUseCommitted(id);
            if(!TryBeginProgramCardWindow(attack,LifecycleCardUse(id)!.Action!,SkillProgramTriggerWindow.CardUseCommitted,currentTargets,ProgramCardContinuation.CommittedSlash))BeginSlashTargetResolution(attack);return;
        }
        BeginJizhiOrNullificationWindow(id,card,actor.Seat,LifecycleCardUse(id)!.TargetSeats,origin.ActionKind,origin.TargetCardId,origin.RequiredCardKind,kind);
    }
    private bool MatchesQinLangXianying(ProgramSkillFrame f,QinLangXianyingUseOrigin o)=>f.QinLangXianying is{Stage:QinLangXianyingStage.NativeUse,Use:{}expected}r&&o==expected&&o.ProgramFrameId==f.Id&&
        o.Source==r.Source&&o.GameplayHash==r.GameplayHash&&o.ActualTurn==r.ActualTurn&&o.ActorSeat==f.OwnerSeat&&r.Grants.Any(g=>g.FrameId==o.GrantFrameId&&g.Name==o.CardKind)&&ValidQinLang(f);
    private bool IsQinLangXianyingUse(long id)
    {
        if(LifecycleCardUse(id)is not{CardId:0,PhysicalCardIds.Count:0,QinLangXianying:{}o,Action:{Type:CardActionType.Use}current}use)return false;
        var a=current;
        if(use.CurrentSlashFirePolicy is{}fire)
        {if(!IsSlashCard(o.CardKind)||fire.OriginalKind!=o.CardKind||fire.OriginalAction.ActionId!=current.ActionId)return false;AssertCurrentSlashFirePolicy(use);a=fire.OriginalAction;}
        else if(use.CardKind!=o.CardKind)return false;
        if(o.CardUseFrameId!=id||a.EffectiveKind!=o.CardKind||a.PhysicalCards.Count!=0||a.ConversionChain.Count!=0||a.EffectiveSuit!=Suit.None||a.EffectiveRank!=0||a.EffectiveIsRed!=false||a.ProviderSeat!=o.ActorSeat||
            a.RequesterSeat is not null||a.ResponderSeat is not null||a.OpponentSeat is not null||!current.TargetSeats.SequenceEqual(use.TargetSeats)||!ShownEntityUseActorMatches(use,o.ActorSeat,o.ActorSeat))return false;
        var i=_resolutionStack.FindIndex(f=>f.Id==id);return i>0&&_resolutionStack[i-1]is ProgramSkillFrame f&&MatchesQinLangXianying(f,o);
    }
    private bool HasIssuedQinLangXianyingUse(long id)=>IsQinLangXianyingUse(id);
    private bool MatchesQinLangXianyingUseAction(long id,CardActionContext a,int cardId)=>cardId==0&&IsQinLangXianyingUse(id)&&LifecycleCardUse(id)!.Action is{}issued&&a.ActionId==issued.ActionId&&a.Type==CardActionType.Use&&
        a.EffectiveKind==issued.EffectiveKind&&a.ActorSeat==issued.ActorSeat&&a.ProviderSeat==issued.ProviderSeat&&a.PhysicalCards.Count==0&&a.ConversionChain.SequenceEqual(issued.ConversionChain)&&a.TargetSeats.SequenceEqual(issued.TargetSeats)&&a.EffectiveSuit==Suit.None&&a.EffectiveRank==0&&a.EffectiveIsRed==false;
    private bool IsQinLangXianyingAttackConsistent(CardAttackHandle attack,IReadOnlyList<Card> processing)
    {
        if(!IsQinLangXianyingUse(attack.ResolutionId)||attack.Card is not null||attack.PhysicalCards.Count!=0||processing.Count!=0||LifecycleCardUse(attack.ResolutionId)is not{}use||attack.EffectiveCardKind!=use.CardKind)return false;
        if(use.CardKind==CardKind.Duel)return ActiveDuel is{}duel&&duel.ResolutionId==use.Id&&SameAttackOwner(duel.Attack,attack)&&attack.CardUserSeat==use.SourceSeat&&
            use.TargetIndex>=0&&use.TargetIndex<use.TargetSeats.Count&&
            (attack.SourceSeat==use.SourceSeat&&attack.TargetSeat==use.TargetSeats[use.TargetIndex]||attack.TargetSeat==use.SourceSeat&&attack.SourceSeat==use.TargetSeats[use.TargetIndex]);
        if(IsSlashCard(use.CardKind)||use.CardKind==CardKind.FireAttack)return attack.SourceSeat==use.SourceSeat&&
            (use.CardKind!=CardKind.FireAttack||CompleteProgramEventHistory().OfType<FireAttackResolvedEvent>().Any(e=>e.ResolutionId==use.Id&&e.CausedDamage&&e.MatchingDiscardCardId>0));
        return use.CardKind is CardKind.BarbarianAssault or CardKind.ArrowBarrage&&ActiveGroupCard is{}group&&group.ResolutionId==use.Id&&group.DamageSourceSeat==attack.SourceSeat&&SameAttackOwner(group.CurrentAttack,attack);
    }
    private bool IsQinLangXianyingBorrowedAttackConsistent(CardAttackHandle attack,IReadOnlyList<Card> processing)=>ActiveBorrowedSword is{ActiveAttack:{}child}borrowed&&SameAttackOwner(child,attack)&&IsQinLangXianyingUse(borrowed.ResolutionId)&&attack.Card is not null&&
        processing.Select(c=>c.Id).Order().SequenceEqual(attack.PhysicalCards.Where(c=>_cardZones.GetLocation(c.Id)==CardLocation.Processing).Select(c=>c.Id).Order());
    [ProgramDamageHook(ProgramDamagePhase.IssuedAttackCompletion, 140)]
    private bool FinishQinLangXianyingAttack(CardAttackHandle attack,out bool paused)
    {
        paused=false;if(!IsQinLangXianyingUse(attack.ResolutionId)||attack.Card is not null||attack.EffectiveCardKind is not{}kind||!IsSlashCard(kind)&&kind is not(CardKind.Duel or CardKind.FireAttack))return false;
        var use=LifecycleCardUse(attack.ResolutionId)!;SetCardUseStep(use.Id,ResolutionFrameStep.Completed);AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id,0,use.CardKind));
        if(_winner==Winner.None&&TryBeginProgramCardWindow(attack,use.Action!,SkillProgramTriggerWindow.CardUseCompleted,use.TargetSeats,ProgramCardContinuation.CompletedSlash,cardUseCausedDamage:attack.CardUseCausedDamage))paused=true;
        else{PopFinishedCardUse(use.Id);CompleteFinishedAttackCardUse(attack);}return true;
    }
    private bool ContinueQinLangXianyingSimpleUse(long id,ProgramSimpleCardContinuation continuation)
    {
        if(!IsQinLangXianyingUse(id))return false;var use=LifecycleCardUse(id)!;var actor=_players[use.SourceSeat];var card=new Card(0,use.CardKind,Suit.None,0);SetCardUseStep(id,ResolutionFrameStep.ResolvingEffect);
        if(continuation.Effect==SimpleCardUseEffect.Recovery&&use.CardKind==CardKind.Peach)
            CompleteRecoveryCardUse(actor,_players[use.TargetSeats[use.TargetIndex]],card,id,use.CardKind,continuation.RecoveryAmount,continuation.RecoveryPolicySources??[]);
        else if(continuation.Effect==SimpleCardUseEffect.Alcohol&&use.CardKind==CardKind.Alcohol)CompleteAlcoholUse(actor,card,id);
        else throw new InvalidOperationException("Xianying changed its exact zero-material simple card effect.");return true;
    }
    [NativeUseValidation(27170)]private void ValidateQinLangUse(CardUseFrame use)
    {if(use.QinLangXianying is not null&&!IsQinLangXianyingUse(use.Id))throw new InvalidOperationException("Xianying lost its genuine zero-material issuance and exact owning grant.");}
    [NativeUseReturn(27170)]private void ReturnQinLangUse(CardUseFrame use)
    {
        if(use.QinLangXianying is not{}origin)return;
        if(use.Step!=ResolutionFrameStep.Completed||_resolutionStack.LastOrDefault()is not ProgramSkillFrame f||!MatchesQinLangXianying(f,origin))throw new InvalidOperationException("Xianying requires its exact completed native use before returning once.");
        AdvanceEventRulesAndQueueFact(new QinLangXianyingUseReturnedEvent(origin));ReplaceRuntimeTop(f=f with{QinLangXianying=f.QinLangXianying! with{Stage=QinLangXianyingStage.NativeChildren,After=_movementSequence}});
        if(use.CardAttack is not{Active:true})ContinueQinLang(f.Id);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup,27170)]private bool ReturnQinLangAttack()
    {if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{QinLangXianying.Stage:QinLangXianyingStage.NativeChildren}f)return false;ContinueQinLang(f.Id);return true;}
    private void ExpireQinLangXianyingTurnEndWine()
    {
        if(!HasQinLangCapability)return;
        var ids=ProgramEventHistory<QinLangXianyingUseIssuedEvent>().Where(e=>e.Origin.ActualTurn==_turnNumber&&e.Origin.CardKind==CardKind.Alcohol).Select(e=>e.Origin.CardUseFrameId).ToHashSet();
        var facts=CompleteProgramEventHistory().ToArray();
        foreach(var entry in facts.OfType<AlcoholAppliedEvent>().Where(e=>ids.Contains(e.ResolutionId)).GroupBy(e=>e.SourceSeat))
        {
            var last=entry.Last();var lastAny=facts.OfType<AlcoholAppliedEvent>().LastOrDefault(e=>e.SourceSeat==entry.Key);
            if(lastAny==last&&_players[entry.Key].HasAlcoholEffect){_players[entry.Key].HasAlcoholEffect=false;AdvanceEventRulesAndQueueFact(new AlcoholExpiredEvent(entry.Key));}
        }
    }
}

public sealed partial class GameEngine
{
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaBeforeLimit, 70)]
    private bool QinLangXianyingSlashQuotaBeforeLimit(ProgramSlashQuotaQuery query) =>
        IsQinLangXianyingSlashQuery(query.Actor);
}
