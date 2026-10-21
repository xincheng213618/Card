using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasProspectiveMouXqQinglong(CharacterState actor,CharacterState target,CardAttackHandle attack)=>
        UsesFormalQinglongCrescentBlade&&attack.SourceSeat==actor.Seat&&attack.TargetSeat==target.Seat&&SameAttackOwner(ActiveCardAttack,attack)&&
        GetHand(actor).Any(card=>IsSlashCard(AdvancedEffectiveHandKind(actor,card))&&!IsTurnHandCardRestricted(actor,card)&&MouXiaoQiaoOl.JueyanAvailable(actor.Seat,card.Id)&&
            CanUseQinglongCrescentBladeTarget(actor,target,effectiveKind:AdvancedEffectiveHandKind(actor,card),effectiveColor:null,allowAnyColor:false,physicalSuit:Suit.None,effectiveRank:0,physicalCardIds:[]))&&
        CollectProgramTriggerCandidates(actor,SkillProgramTriggerWindow.PublicHandRevealed).Any(c=>c.SkillId==MouXiaoQiaoOlRuntime.Jueyan);
    private PendingDecision? AddMouXqJueyanRequestChoices(PendingDecision? p)
    {
        if(p is null||!IsValidPlayerSeat(p.PlayerSeat)||!_players[p.PlayerSeat].IsAlive)return p;
        IReadOnlyList<PromptChoice> choices=[];var actor=_players[p.PlayerSeat];
        if(p.Kind==DecisionKind.QinglongCrescentBlade&&ActiveQinglongCrescentBlade is{}blade)
            choices=MouXqForcedSlashChoices(actor,_players[blade.Attack.TargetSeat],blade.Attack.ResolutionId,true);
        else if(p.Kind==DecisionKind.RespondSlash&&ActiveBorrowedSword is{AwaitingSlashChoice:true}b)
            choices=MouXqForcedSlashChoices(actor,_players[b.SlashTargetSeat],b.ResolutionId,false);
        else if(p.Kind==DecisionKind.ProgramTrigger&&_resolutionStack.LastOrDefault()is ProgramSkillFrame f&&GetPausedPrivateOfferEffect(f)is{}effect)
            choices=MouXqProgramSlashChoices(f,p.PlayerSeat,RoundOnceCardlessProgramTargets(f,p.PlayerSeat,effect.Op),effect.ResultBind);
        return choices.Count==0?p:p with{Choices=Array.AsReadOnly(p.Choices.Concat(choices).DistinctBy(c=>c.Id).ToArray())};
    }
    private IReadOnlyList<PromptChoice> MouXqForcedSlashChoices(CharacterState actor,CharacterState target,long parentId,bool qinglong)
    {
        if(qinglong ? ActiveQinglongCrescentBlade is not{}blade||blade.Attack.ResolutionId!=parentId||blade.Attack.SourceSeat!=actor.Seat||blade.Attack.TargetSeat!=target.Seat||!SameAttackOwner(ActiveCardAttack,blade.Attack)
            :ActiveBorrowedSword is not{}borrowed||borrowed.ResolutionId!=parentId||borrowed.WeaponOwnerSeat!=actor.Seat||borrowed.SlashTargetSeat!=target.Seat||borrowed.ActiveAttack is not null||GetWeapon(actor) is null)return [];
        var c=CollectProgramTriggerCandidates(actor,SkillProgramTriggerWindow.PublicHandRevealed).FirstOrDefault(c=>c.SkillId==MouXiaoQiaoOlRuntime.Jueyan);if(c is null)return [];
        var source=new CardConversionSource(c.SkillId,"retained-hand-use",actor.Seat,c.SkillInstanceId);
        return Array.AsReadOnly(GetHand(actor).Where(card=>IsSlashCard(AdvancedEffectiveHandKind(actor,card))&&!IsTurnHandCardRestricted(actor,card)&&MouXiaoQiaoOl.JueyanAvailable(actor.Seat,card.Id)).Where(card=>
            qinglong?CanUseQinglongCrescentBladeTarget(actor,target,effectiveKind:AdvancedEffectiveHandKind(actor,card),effectiveColor:null,allowAnyColor:false,physicalSuit:Suit.None,effectiveRank:0,physicalCardIds:[]):
            IsLegalBorrowedSwordSlashTarget(actor,target,AdvancedEffectiveHandKind(actor,card),Suit.None,false,null,0,[])).Select(card=>
        {
            var kind=AdvancedEffectiveHandKind(actor,card);var p=new Dictionary<string,string>{[qinglong?"action":"response"]="mou-xiao-qiao-forced-slash",
                ["output-kind"]=kind.ToString(),["original-frame-id"]=parentId.ToString(CultureInfo.InvariantCulture),["forced-use"]=qinglong?"qinglong":"borrowed-sword"};
            AddConversionParameters(p,source);return new PromptChoice(new($"jueyan.forced.{parentId}.{actor.Seat}.{card.Id}.{kind}"),$"绝颜：展示并保留【{card.DisplayName}】，视为使用之。",[card.Id],[target.Seat],p);
        }).ToArray());
    }
    private bool BeginMouXqForcedSlash(PromptChoice choice)
    {
        if(_pendingDecision is not{}prompt||choice.Cards is not[var cardId]||choice.Targets is not[var target]||
            !long.TryParse(choice.Parameters.GetValueOrDefault("original-frame-id"),out var parent)||!Enum.TryParse<CardKind>(choice.Parameters.GetValueOrDefault("output-kind"),out var kind))throw new InvalidOperationException("Jueyan lost its native forced Slash choice.");
        var qinglong=choice.Parameters.GetValueOrDefault("forced-use")=="qinglong";var actor=_players[prompt.PlayerSeat];
        if(prompt.Kind!=(qinglong?DecisionKind.QinglongCrescentBlade:DecisionKind.RespondSlash)||!prompt.Choices.Any(c=>AssistedChoicesEqual([c],[choice]))||
            !MouXqForcedSlashChoices(actor,_players[target],parent,qinglong).Any(c=>AssistedChoicesEqual([c],[choice])))throw new InvalidOperationException("Jueyan cannot replace another native target or a pure Played response.");
        StartMouXqRequestedReveal(prompt,RequireConversionSource(choice),cardId,new(parent,prompt.Kind,kind,actor.Seat,target,_resolutionStack[^1].Id));return true;
    }
    private void StartMouXqRequestedReveal(PendingDecision prompt,CardConversionSource source,int cardId,MouXiaoQiaoJueyanResponseRequest request)
    {
        ClearPendingDecision();var program=_contentRegistry.GetSkill(source.SkillId).Program!;
        var f=new ProgramSkillFrame(++_resolutionSequence,source.OwnerSeat,source.SkillId,"retained-hand-use",program.GameplayHash,0,[cardId],[])
            {SkillInstanceId=source.SkillInstanceId,ResponseDecision=prompt,MouXiaoQiaoJueyanResponse=request};
        PushRuntimeFrame(f);AdvanceEventRulesAndQueueFact(new ProgramSkillStartedEvent(f.Id,f.OwnerSeat,f.SkillId,f.ActivationId));AdvanceRuntimeProgram(f.Id);
    }
    private bool IsMouXqForcedRequest(MouXiaoQiaoJueyanResponseRequest r,ResolutionFrame parent)=>r.DecisionKind switch
    {
        DecisionKind.RespondSlash=>parent is ResponseWindowFrame w&&w.ParentFrameId==r.ParentFrameId&&w.ResponderSeat==r.ActorSeat&&
            LifecycleCardUse(r.ParentFrameId)?.Continuations.BorrowedSword is{Active:true,AwaitingSlashChoice:true,ActiveAttackOwnerId:null}b&&b.WeaponOwnerSeat==r.ActorSeat&&b.SlashTargetSeat==r.TargetSeat,
        DecisionKind.QinglongCrescentBlade=>parent is CardUseFrame use&&use.Id==r.ParentFrameId&&use.SourceSeat==r.ActorSeat&&
            use.Continuations.QinglongCrescentBlade is{Active:true}&&use.CardAttack is{}a&&a.SourceSeat==r.ActorSeat&&a.TargetSeat==r.TargetSeat,
        DecisionKind.ProgramTrigger=>parent is ProgramSkillFrame f&&IsMouXqProgramRequest(f,r),
        _=>false
    };
    private void IssueMouXqRequestedSlash(MouXiaoQiaoJueyanResponseGrant grant)
    {
        var r=grant.Request;var actor=_players[r.ActorSeat];var target=_players[r.TargetSeat];BorrowedSwordHandle? borrowed=null;
        if(r.DecisionKind==DecisionKind.RespondSlash)
        {
            borrowed=ActiveBorrowedSword??throw new InvalidOperationException("Jueyan lost its original borrowed weapon request.");
            PopResponseWindow(borrowed.ResolutionId);SetCardUseStep(borrowed.ResolutionId,ResolutionFrameStep.ResolvingEffect);
            borrowed.AwaitingSlashChoice=false;borrowed.SlashCardId=0;borrowed.EffectiveSlashKind=r.OutputKind;
        }
        else if(r.DecisionKind==DecisionKind.QinglongCrescentBlade)
        {
            var old=ActiveQinglongCrescentBlade!.Attack;ActiveQinglongCrescentBlade=null;SetCardUseStep(old.ResolutionId,ResolutionFrameStep.ResolvingEffect);
            AdvanceEventRulesAndQueueFact(new QinglongCrescentBladeResolvedEvent(old.ResolutionId,actor.Seat,target.Seat,true,[],r.OutputKind));SuspendContinuationForQinglongFollowup(old.ResolutionId);
        }
        else PrepareMouXqProgramSlash(r);
        var card=new Card(0,r.OutputKind,Suit.None,0);var id=BeginCardUse(card,actor.Seat,[target.Seat],r.OutputKind,ignoresArmor:HasCardArmorBypass(actor,target,r.OutputKind),physicalCardIds:[],conversionSource:grant.Source);
        var attack=new CardAttackHandle(this,id,actor.Seat,target.Seat,card:null,damageAmount:1+actor.AlcoholDamageBonus,playedCardKind:r.OutputKind,
            ignoresArmor:HasCardArmorBypass(actor,target,r.OutputKind),programSkillCardUseFrameId:r.DecisionKind==DecisionKind.ProgramTrigger?r.ParentFrameId:null);
        if(borrowed is not null)borrowed.ActiveAttack=attack;ActiveCardAttack=attack;CaptureProgramAdjustedSlashBaseDamage(attack);
        if(r.DecisionKind==DecisionKind.RespondSlash&&_phase==TurnPhase.Play&&actor.Seat==_currentSeat&&LifecycleCardUse(id)?.UnlimitedUse!=true&&!IgnoresProgramSlashLimit(actor,grant.Source))RecordSlashUseDebit(id,actor.Seat);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat,r.OutputKind);CaptureProgramAlcoholConsumption(id,actor);actor.HasAlcoholEffect=false;
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0,r.OutputKind,actor.Seat,target.Seat,IgnoresArmor:attack.IgnoresArmor));NotifyAiOfSlash(actor,target);
        if(!TryMarkProgramUseCommitted(id)||!TryBeginProgramCardWindow(attack,LifecycleCardUse(id)!.Action!,SkillProgramTriggerWindow.CardUseCommitted,[target.Seat],ProgramCardContinuation.CommittedSlash))BeginSlashTargetResolution(attack);
    }
}
