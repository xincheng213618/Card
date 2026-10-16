namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlyList<IReadOnlyList<int>> AHuiNanUseTargets(ProgramSkillFrame f)
    {
        var r=f.AHuiNanJueman!;if(r.Stage!=AHuiNanJuemanStage.UseChoice||r.History.Count!=3)return[];
        var actor=_players[f.OwnerSeat];var kind=r.History[2].Kind;
        if(!actor.IsAlive||_winner!=Winner.None||IsCardUseForbidden(actor.Seat,kind,CardActionType.Use))return[];
        if(IsSlashCard(kind))return Array.AsReadOnly(_players.Where(p=>CanUseSlashTarget(actor,p,new(0,kind,Suit.None,0),effectiveKind:kind,noEffectiveRank:true,physicalCardIds:[])&&
            !IsSelfTargetForbiddenAction(actor,kind,[p.Seat])&&!IsCardTargetProhibited(p,kind,Suit.None,null)&&!HasBeneficiarySuitShield(actor.Seat,p.Seat,Suit.None)).Select(p=>(IReadOnlyList<int>)Array.AsReadOnly(new[]{p.Seat})).ToArray());
        if(kind is CardKind.Peach or CardKind.Alcohol&&(kind!=CardKind.Peach||actor.Hp<actor.MaxHp)&&!HasSelfCardTargetProhibition(actor.Seat)&&!HasBeneficiarySuitShield(actor.Seat,actor.Seat,Suit.None)&&!IsCardTargetProhibited(actor,kind,Suit.None,null)&&!IsDirectedCardTargetProhibited(actor.Seat,actor.Seat,kind))return Array.AsReadOnly(new IReadOnlyList<int>[]{Array.AsReadOnly(new[]{actor.Seat})});
        return[];
    }
    private bool IsAHuiNanJuemanSlashQuery(CharacterState actor)=>HasAHuiNanCapability&&_resolutionStack.LastOrDefault()is ProgramSkillFrame{AHuiNanJueman:{Stage:AHuiNanJuemanStage.UseChoice}r}f&&f.OwnerSeat==actor.Seat&&r.History.Count==3&&IsSlashCard(r.History[2].Kind)&&ValidAHuiNan(f);
    private bool MatchesAHuiNanJueman(ProgramSkillFrame f,AHuiNanJuemanUseOrigin o)=>f.AHuiNanJueman is{Stage:AHuiNanJuemanStage.NativeUse,Use:{}expected}r&&o==expected&&o.ProgramFrameId==f.Id&&o.Source==r.Source&&o.GameplayHash==r.GameplayHash&&o.ActualTurn==r.ActualTurn&&o.ActorSeat==f.OwnerSeat&&r.History.Count==3&&r.History[2].Kind==o.CardKind&&ValidAHuiNan(f);
    private void BeginAHuiNanJuemanUse(ProgramSkillFrame f,AHuiNanJuemanUseOrigin o,IReadOnlyList<int> targets)
    {
        if(_resolutionStack.LastOrDefault()?.Id!=f.Id||!MatchesAHuiNanJueman(f,o)||o.CardUseFrameId!=_resolutionSequence+1||string.Join(',',targets)!=o.TargetSignature)throw new InvalidOperationException("Jueman lost its committed third basic-card receipt.");
        var actor=_players[o.ActorSeat];var card=new Card(0,o.CardKind,Suit.None,0);var id=BeginCardUse(card,actor.Seat,targets,o.CardKind,physicalCardIds:[],aHuiNanJueman:o);
        if(id!=o.CardUseFrameId)throw new InvalidOperationException("Jueman lost its reserved native use identity.");
        if(o.CardKind==CardKind.Peach){BeginSimpleCardUse(id,new(0,SimpleCardUseEffect.Recovery,1));return;}
        if(o.CardKind==CardKind.Alcohol){BeginSimpleCardUse(id,new(0,SimpleCardUseEffect.Alcohol));return;}
        if(!IsSlashCard(o.CardKind))throw new InvalidOperationException("Jueman cannot actively use an otherwise unplayable basic card.");
        var current=LifecycleCardUse(id)!;var attack=new CardAttackHandle(this,id,actor.Seat,current.TargetSeats[0],null,1+actor.AlcoholDamageBonus,o.CardKind,ignoresArmor:HasCardArmorBypass(actor,_players[current.TargetSeats[0]],o.CardKind));
        CaptureProgramAlcoholConsumption(id,actor);actor.HasAlcoholEffect=false;ActiveCardAttack=attack;CaptureProgramAdjustedSlashBaseDamage(attack);MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat,o.CardKind);
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0,o.CardKind,actor.Seat,current.TargetSeats[0]));TryMarkProgramUseCommitted(id);
        if(!TryBeginProgramCardWindow(attack,current.Action!,SkillProgramTriggerWindow.CardUseCommitted,current.TargetSeats,ProgramCardContinuation.CommittedSlash))BeginSlashTargetResolution(attack);
    }
    private bool IsAHuiNanJuemanUse(long id)
    {
        if(LifecycleCardUse(id)is not{CardId:0,PhysicalCardIds.Count:0,AHuiNanJueman:{}o,Action:{Type:CardActionType.Use}current}use)return false;var a=current;
        if(use.CurrentSlashFirePolicy is{}fire)
        {if(!IsSlashCard(o.CardKind)||fire.OriginalKind!=o.CardKind||fire.OriginalAction.ActionId!=current.ActionId)return false;AssertCurrentSlashFirePolicy(use);a=fire.OriginalAction;}
        else if(use.CardKind!=o.CardKind)return false;
        if(o.CardUseFrameId!=id||a.EffectiveKind!=o.CardKind||a.PhysicalCards.Count!=0||a.ConversionChain.Count!=0||a.EffectiveSuit!=Suit.None||a.EffectiveRank!=0||a.EffectiveIsRed!=false||a.ProviderSeat!=o.ActorSeat||a.RequesterSeat is not null||a.ResponderSeat is not null||a.OpponentSeat is not null||!current.TargetSeats.SequenceEqual(use.TargetSeats)||!ShownEntityUseActorMatches(use,o.ActorSeat,o.ActorSeat))return false;
        var i=_resolutionStack.FindIndex(f=>f.Id==id);return i>0&&_resolutionStack[i-1]is ProgramSkillFrame f&&MatchesAHuiNanJueman(f,o);
    }
    private bool HasIssuedAHuiNanJuemanUse(long id)=>IsAHuiNanJuemanUse(id);
    private bool MatchesAHuiNanJuemanUseAction(long id,CardActionContext a,int cardId)=>cardId==0&&IsAHuiNanJuemanUse(id)&&LifecycleCardUse(id)!.Action is{}issued&&a.ActionId==issued.ActionId&&a.Type==CardActionType.Use&&a.EffectiveKind==issued.EffectiveKind&&a.ActorSeat==issued.ActorSeat&&a.ProviderSeat==issued.ProviderSeat&&a.PhysicalCards.Count==0&&a.ConversionChain.SequenceEqual(issued.ConversionChain)&&a.TargetSeats.SequenceEqual(issued.TargetSeats)&&a.EffectiveSuit==Suit.None&&a.EffectiveRank==0&&a.EffectiveIsRed==false;
    private bool IsAHuiNanJuemanAttackConsistent(CardAttackHandle attack,IReadOnlyList<Card> processing)=>IsAHuiNanJuemanUse(attack.ResolutionId)&&attack.Card is null&&attack.PhysicalCards.Count==0&&processing.Count==0&&LifecycleCardUse(attack.ResolutionId)is{}use&&IsSlashCard(use.CardKind)&&attack.EffectiveCardKind==use.CardKind&&attack.SourceSeat==use.SourceSeat;
    private bool FinishAHuiNanJuemanAttack(CardAttackHandle attack,out bool paused)
    {
        paused=false;if(!IsAHuiNanJuemanUse(attack.ResolutionId)||attack.Card is not null||attack.EffectiveCardKind is not{}kind||!IsSlashCard(kind))return false;
        var use=LifecycleCardUse(attack.ResolutionId)!;SetCardUseStep(use.Id,ResolutionFrameStep.Completed);AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id,0,use.CardKind));
        if(_winner==Winner.None&&TryBeginProgramCardWindow(attack,use.Action!,SkillProgramTriggerWindow.CardUseCompleted,use.TargetSeats,ProgramCardContinuation.CompletedSlash,cardUseCausedDamage:attack.CardUseCausedDamage))paused=true;
        else{PopFinishedCardUse(use.Id);CompleteFinishedAttackCardUse(attack);}return true;
    }
    private bool ContinueAHuiNanJuemanSimpleUse(long id,ProgramSimpleCardContinuation continuation)
    {
        if(!IsAHuiNanJuemanUse(id))return false;var use=LifecycleCardUse(id)!;var actor=_players[use.SourceSeat];var card=new Card(0,use.CardKind,Suit.None,0);SetCardUseStep(id,ResolutionFrameStep.ResolvingEffect);
        if(continuation.Effect==SimpleCardUseEffect.Recovery&&use.CardKind==CardKind.Peach)CompleteRecoveryCardUse(actor,_players[use.TargetSeats[use.TargetIndex]],card,id,use.CardKind,continuation.RecoveryAmount,continuation.RecoveryPolicySources??[]);
        else if(continuation.Effect==SimpleCardUseEffect.Alcohol&&use.CardKind==CardKind.Alcohol)CompleteAlcoholUse(actor,card,id);
        else throw new InvalidOperationException("Jueman changed its native zero-material basic effect.");return true;
    }
    [NativeUseValidation(27200)]private void ValidateAHuiNanUse(CardUseFrame use)
    {if(use.AHuiNanJueman is not null&&!IsAHuiNanJuemanUse(use.Id))throw new InvalidOperationException("Jueman lost its exact printed-third-basic issuance.");}
    [NativeUseReturn(27200)]private void ReturnAHuiNanUse(CardUseFrame use)
    {
        if(use.AHuiNanJueman is not{}o)return;
        if(use.Step!=ResolutionFrameStep.Completed||_resolutionStack.LastOrDefault()is not ProgramSkillFrame f||!MatchesAHuiNanJueman(f,o))throw new InvalidOperationException("Jueman must return once to its exact original actual-ending frame.");
        AdvanceEventRulesAndQueueFact(new AHuiNanJuemanUseReturnedEvent(o));ReplaceRuntimeTop(f=f with{AHuiNanJueman=f.AHuiNanJueman! with{Stage=AHuiNanJuemanStage.NativeChildren,After=_movementSequence}});if(use.CardAttack is not{Active:true})ContinueAHuiNan(f.Id);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup,27200)]private bool ReturnAHuiNanAttack()
    {if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{AHuiNanJueman.Stage:AHuiNanJuemanStage.NativeChildren}f)return false;ContinueAHuiNan(f.Id);return true;}
    private void ExpireAHuiNanJuemanTurnEndWine()
    {
        if(!HasAHuiNanCapability)return;var ids=ProgramEventHistory<AHuiNanJuemanUseIssuedEvent>().Where(e=>e.Origin.ActualTurn==_turnNumber&&e.Origin.CardKind==CardKind.Alcohol).Select(e=>e.Origin.CardUseFrameId).ToHashSet();var facts=CompleteProgramEventHistory().ToArray();
        foreach(var group in facts.OfType<AlcoholAppliedEvent>().Where(e=>ids.Contains(e.ResolutionId)).GroupBy(e=>e.SourceSeat))
        {if(facts.OfType<AlcoholAppliedEvent>().LastOrDefault(e=>e.SourceSeat==group.Key)==group.Last()&&_players[group.Key].HasAlcoholEffect){_players[group.Key].HasAlcoholEffect=false;AdvanceEventRulesAndQueueFact(new AlcoholExpiredEvent(group.Key));}}
    }
}
