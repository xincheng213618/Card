namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool BeginMouDongZhuoOlWine(ProgramSkillFrame f)
    {
        if(!MouDongZhuoOl.Valid(f)||f.MouDongZhuoOl is not{Paid:true,Stage:MouDongZhuoOlStage.Wine,WineUseId:null}r)throw new InvalidOperationException("Fengshang Wine lost its original paid allocation.");
        var owner=_players[f.OwnerSeat];
        if(!owner.IsAlive||_winner!=Winner.None||IsCardUseForbidden(owner.Seat,CardKind.Alcohol,CardActionType.Use)||
            !TargetRules.AllowsNativeUseMaterial(new(owner.Seat,CardKind.Alcohol,[]))||IsDirectedCardTargetProhibited(owner.Seat,owner.Seat,CardKind.Alcohol)||IsCardTargetProhibited(owner,CardKind.Alcohol,Suit.None)||HasSelfCardTargetProhibition(owner.Seat))return false;
        var dying=ActiveDying is{}d&&d.VictimSeat==owner.Seat&&owner.Hp<=0;
        var id=++_resolutionSequence;var a=CaptureFactionAction(new CardActionContext(++_cardActionSequence,_resolutionStack.OfType<CardUseFrame>().LastOrDefault()?.Action?.ActionId,CardActionType.Use,owner.Seat,owner.Seat,null,null,null,CardKind.Alcohol,[owner.Seat],[],[r.Source],effectiveSuit:Suit.None,effectiveRank:0));
        ReplaceRuntimeTop(f with{MouDongZhuoOl=r with{WineUseId=id}});
        PushRuntimeFrame(new CardUseFrame(id,owner.Seat,0,CardKind.Alcohol,[owner.Seat],PhysicalCardIds:[]){Action=a,MouDongZhuoWine=new(f.Id,f.InstructionIndex,r.Source,r.GameplayHash,dying)});
        AdvanceEventRulesAndQueueFact(new MouDongZhuoOlWineIssuedEvent(f.Id,id,a.ActionId,dying));
        AdvanceEventRulesAndQueueFact(new CardActionAcceptedEvent(a));
        if(TracksPlayCardHistory)AdvanceEventRulesAndQueueFact(new CardUseAppearanceCapturedEvent(a));
        AdvanceEventRulesAndQueueFact(new CardUseDeclaredEvent(id,0,CardKind.Alcohol,owner.Seat));AdvanceEventRulesAndQueueFact(new TargetsConfirmedEvent(id,[owner.Seat]));
        RecordYingboCardUse(id,owner.Seat,CardKind.Alcohol);RecordProgramUsedBasicCard(owner.Seat,CardKind.Alcohol);RecordActualPlayPhaseUse(a);
        BeginSimpleCardUse(id,new(0,dying?SimpleCardUseEffect.Recovery:SimpleCardUseEffect.Alcohol));return true;
    }
    private bool IsMouDongZhuoOlWine(long id)
    {
        if(LifecycleCardUse(id)is not{CardId:0,CardKind:CardKind.Alcohol,PhysicalCardIds.Count:0,MouDongZhuoWine:{}r,Action:{Type:CardActionType.Use}a}u||
            a.ActorSeat!=u.SourceSeat||a.ProviderSeat!=u.SourceSeat||a.EffectiveKind!=CardKind.Alcohol||a.PhysicalCards.Count!=0||a.EffectiveSuit!=Suit.None||a.EffectiveRank!=0||!a.ConversionChain.SequenceEqual([r.Source]))return false;
        var n=_resolutionStack.FindIndex(x=>x.Id==id);if(n<1||_resolutionStack[n-1]is not ProgramSkillFrame{MouDongZhuoOl:{Paid:true,Stage:MouDongZhuoOlStage.Wine}p}f||
            f.Id!=r.ParentId||f.InstructionIndex!=r.Instruction||p.WineUseId!=id||p.Source!=r.Source||p.GameplayHash!=r.Hash||!MouDongZhuoOl.Valid(f))return false;
        return ProgramEventHistory<MouDongZhuoOlWineIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.UseId==id&&e.ActionId==a.ActionId&&e.SelfDying==r.SelfDying)==1;
    }
    private bool ContinueMouDongZhuoOlWine(long id,ProgramSimpleCardContinuation c)
    {
        if(LifecycleCardUse(id)?.MouDongZhuoWine is null)return false;
        if(c.CardId!=0||!IsMouDongZhuoOlWine(id))throw new InvalidOperationException("Fengshang Wine lost its exact real Use.");
        var u=LifecycleCardUse(id)!;SetCardUseStep(id,ResolutionFrameStep.ResolvingEffect);var card=new Card(0,CardKind.Alcohol,Suit.None,0);
        if(u.MouDongZhuoWine!.SelfDying&&c.Effect==SimpleCardUseEffect.Recovery)CompleteRecoveryCardUse(_players[u.SourceSeat],_players[u.SourceSeat],card,id,CardKind.Alcohol,c.RecoveryAmount,c.RecoveryPolicySources??[]);
        else if(!u.MouDongZhuoWine.SelfDying&&c.Effect==SimpleCardUseEffect.Alcohol)CompleteAlcoholUse(_players[u.SourceSeat],card,id);
        else throw new InvalidOperationException("Fengshang changed Wine's actual self-rescue mode.");return true;
    }
    [NativeUseValidation(34700)]private void ValidateMouDongZhuoOlWine(CardUseFrame u)
    {if(u.MouDongZhuoWine is not null&&(!IsMouDongZhuoOlWine(u.Id)||u.Step!=ResolutionFrameStep.Completed||u.PendingRecoveryAttempts is{Count:>0}||u.RecoveryPaidContinuation is not null))throw new InvalidOperationException("Fengshang Wine cannot return before its native use and recovery children.");}
    [NativeUseReturn(34700)]private void ReturnMouDongZhuoOlWine(CardUseFrame u)
    {
        if(u.MouDongZhuoWine is not{}r)return;
        if(LifecycleCardUse(u.Id)is not null||_resolutionStack.LastOrDefault()is not ProgramSkillFrame{MouDongZhuoOl:{WineReturned:false}p}f||f.Id!=r.ParentId||p.WineUseId!=u.Id||u.Action is not{}a)throw new InvalidOperationException("Fengshang Wine lost its exact typed return.");
        ReplaceRuntimeTop(f with{MouDongZhuoOl=p with{Stage=MouDongZhuoOlStage.WineChildren,WineReturned=true}});
        AdvanceEventRulesAndQueueFact(new MouDongZhuoOlWineReturnedEvent(f.Id,u.Id,a.ActionId));AdvanceRuntimeProgram(f.Id);
    }
}
