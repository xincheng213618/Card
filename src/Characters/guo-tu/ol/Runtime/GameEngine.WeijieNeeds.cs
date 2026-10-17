using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string WeijieOpen="weijie-request",WeijieChoice="weijie-choice",WeijieDiscardReason="skill-program.guo-tu.weijie.discard";
    private bool HasWeijieCapability=>_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveWeijieRequiredBasicRequest);
    private static CardConversionSource WeijieSource(ProgramSkillFrame f)=>new(f.SkillId,f.ActivationId,f.OwnerSeat,f.SkillInstanceId);
    private bool WeijieTurnOpen(int seat)=>_setupComplete&&_turnNumber>0&&_turnNumber==_turnProgression.TurnNumber&&seat!=_turnProgression.OwnerSeat&&!ProgramEventHistory<ActualTurnCursorReturnedEvent>().Any(e=>e.ActualTurn==_turnNumber&&e.OwnerSeat==_turnProgression.OwnerSeat);
    private bool WeijieSpent(int seat)=>ProgramEventHistory<WeijieCostPaidEvent>().Any(e=>e.Source.OwnerSeat==seat&&e.ActualTurn==_turnNumber&&e.ActualTurnOwner==_turnProgression.OwnerSeat);
    private IEnumerable<(CardConversionSource Source,string Hash)> WeijieSources(CharacterState actor)
    {
        if(!HasWeijieCapability||!actor.IsAlive||_winner!=Winner.None||!WeijieTurnOpen(actor.Seat)||WeijieSpent(actor.Seat))yield break;
        foreach(var i in GetSkillBindingShard(actor).ProgramInstances)
        foreach(var a in i.Program.Activations.Where(a=>a.Effects is[{Op:SkillProgramEffectOp.ResolveWeijieRequiredBasicRequest}]))
            yield return(new(i.SkillId,a.Id,actor.Seat,i.SkillInstanceId),i.Program.GameplayHash);
    }
    private IEnumerable<int> WeijieVictims(int actor)=>_players.Where(p=>p.IsAlive&&p.Seat!=actor&&GetCombatDistance(actor,p.Seat)==1&&GetHand(p).Any(c=>!IsForeignEquipmentDiscardPrevented(actor,c,CardLocation.Hand(p.Seat),OwnedCardMoveIntent.Discard))).Select(p=>p.Seat);
    private WeijieNeed? CaptureWeijieNeed(PendingDecision original,ResolutionFrame? suppliedNativeTop=null)=>WeijieCurrentNeed(original.PlayerSeat,original.Kind,suppliedNativeTop??_resolutionStack.LastOrDefault());
    private WeijieNeed? WeijieCurrentNeed(int provider,DecisionKind kind,ResolutionFrame? top)
    {
        if(!HasWeijieCapability||!IsValidPlayerSeat(provider)||top is null||top is ProgramSkillFrame{Weijie:not null})return null;
        var owner=top is ResponseWindowFrame response?response.ParentFrameId:top.Id;
        long? action=LifecycleCardUse(owner)?.Action?.ActionId;
        WeijieNeed N(WeijieIntent intent,int actor,IReadOnlyList<int> targets,int cursor=0,int? requester=null,int? opponent=null,int index=0,int round=0,SkillProgramEffectOp? op=null,int instruction=0,long? ownerId=null)=>new(intent,ownerId??owner,top.Id,action,provider,actor,requester,opponent,cursor,index,round,instruction,op,targets);
        if(kind==DecisionKind.RescueDying&&top is DyingFrame d&&d.ResponderSeat==provider)return N(WeijieIntent.Dying,provider,[d.VictimSeat],d.ResponderIndex,ownerId:d.Id);
        if(kind==DecisionKind.RespondDodge)
        {
            if(ActiveFactionDefense is{}fd&&fd.CurrentCandidateSeat==provider&&SameAttackOwner(fd.Attack,ActiveCardAttack))return N(WeijieIntent.FactionDodge,fd.OwnerSeat,[],fd.CandidateIndex,fd.OwnerSeat,fd.Attack.SourceSeat,ownerId:fd.Attack.ResolutionId);
            if(ActiveGroupCard is{Effect:GroupCardEffect.ResponseAttack,CurrentAttack:{}ga}g&&ga.TargetSeat==provider&&g.RequiredCardKind==CardKind.Dodge)return N(WeijieIntent.Group,provider,[],ga.SuccessfulDodgeResponses,opponent:g.SourceSeat,index:g.TargetIndex,ownerId:g.ResolutionId);
            if(ActiveCardAttack is{}a&&a.TargetSeat==provider&&IsSlashCard(a.EffectiveCardKind??CardKind.Dodge))return N(WeijieIntent.Dodge,provider,[],a.SuccessfulDodgeResponses,opponent:a.SourceSeat,ownerId:a.ResolutionId);
        }
        if(kind==DecisionKind.RespondSlash)
        {
            if(ActiveFactionCardRequest is{AwaitingProviders:true}f&&f.CurrentCandidateSeat==provider)
            {var played=!IsFactionSlashUse(f);var opponent=f.Purpose==FactionCardRequestPurpose.DuelResponse?ActiveDuel?.OpponentSeat:f.ResponseAttack?.SourceSeat;var program=_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(p=>p.Id==f.ProgramSkillFrameId);return N(played?WeijieIntent.FactionSlashPlayed:WeijieIntent.FactionSlashUse,f.OwnerSeat,f.TargetSeat is{}t?[t]:[],f.CandidateIndex,f.OwnerSeat,opponent,op:program is null?null:GetPausedPrivateOfferEffect(program)?.Op,instruction:program?.InstructionIndex??0,ownerId:f.OwnerFrameId);}
            if(ActiveBorrowedSword is{AwaitingSlashChoice:true,ActiveAttack:null}b&&b.WeaponOwnerSeat==provider)return N(WeijieIntent.BorrowedSword,provider,[b.SlashTargetSeat],opponent:b.SourceSeat,ownerId:b.ResolutionId);
            if(ActiveDuel is{}duel&&duel.ResponderSeat==provider)return N(WeijieIntent.Duel,provider,[],duel.SuccessfulSlashResponses,opponent:duel.OpponentSeat,ownerId:duel.ResolutionId);
            if(ActiveGroupCard is{Effect:GroupCardEffect.ResponseAttack,CurrentAttack:{}ga}g&&ga.TargetSeat==provider&&g.RequiredCardKind==CardKind.Slash)return N(WeijieIntent.Group,provider,[],opponent:g.SourceSeat,index:g.TargetIndex,ownerId:g.ResolutionId);
        }
        if(kind==DecisionKind.QinglongCrescentBlade&&ActiveQinglongCrescentBlade is{}q&&q.Attack.SourceSeat==provider&&SameAttackOwner(ActiveCardAttack,q.Attack))return N(WeijieIntent.Qinglong,provider,[q.Attack.TargetSeat],opponent:q.Attack.TargetSeat,ownerId:q.Attack.ResolutionId);
        if(kind!=DecisionKind.ProgramTrigger||top is not ProgramSkillFrame p||GetPausedPrivateOfferEffect(p)is not{}effect)return null;
        if(TryGetWenyiWeijiePeachNeed(p,provider,out var wenyiTarget))return N(WeijieIntent.WenyiPeach,provider,[wenyiTarget],op:effect.Op,instruction:p.InstructionIndex);
        if(IsSayingSlashRequest(p)&&TryGetSayingSlashRequest(p,out var sayingActor,out var sayingTargets)&&provider==sayingActor)return N(WeijieIntent.AssistedSlash,provider,sayingTargets,op:effect.Op,instruction:p.InstructionIndex);
        if(IsChanshuangSlashRequest(p)&&TryGetChanshuangSlashRequest(p,out var requestedActor,out var requestedTargets)&&provider==requestedActor)return N(WeijieIntent.AssistedSlash,provider,requestedTargets,op:effect.Op,instruction:p.InstructionIndex);
        if(IsQigongRequest(p)&&p.LvKuangLvXiang!.Actor==provider)return N(WeijieIntent.AssistedSlash,provider,[p.LvKuangLvXiang.Target],op:effect.Op,instruction:p.InstructionIndex);
        if(IsWuJingHejiRequest(p)&&p.OwnerSeat==provider)return N(WeijieIntent.AssistedSlash,provider,[p.WuJing!.TargetSeat],op:effect.Op,instruction:p.InstructionIndex);
        if(IsCaoChunInitialRequest(p)&&p.OwnerSeat==provider)return N(WeijieIntent.AssistedSlash,provider,_players.Where(t=>t.IsAlive&&t.Seat!=provider).Select(t=>t.Seat).ToArray(),op:effect.Op,instruction:p.InstructionIndex);
        if(p.KeBiNengPingduan is{Stage:KeBiNengPingduanStage.BasicChoice,ActorSeat:var actor}&&actor==provider&&ValidKeBiNeng(p))return N(WeijieIntent.GenericBasic,provider,_players.Where(t=>t.IsAlive).Select(t=>t.Seat).ToArray(),op:effect.Op,instruction:p.InstructionIndex);
        if(!RoundOnceCardlessProgramActor(p,effect.Op,provider))return null;
        var targets=RoundOnceCardlessProgramTargets(p,provider,effect.Op);if(targets.Length==0)return null;
        return N(effect.Op switch{SkillProgramEffectOp.RequestSlashByTarget=>WeijieIntent.ProgramSlash,SkillProgramEffectOp.RequestSlashByNearest=>WeijieIntent.ProgramNearestSlash,SkillProgramEffectOp.RequestLegalSlashByNearest=>WeijieIntent.NearestLegalSlash,SkillProgramEffectOp.UseOwnerSlashAgainstTurnOwner=>WeijieIntent.FixedSlash,_=>WeijieIntent.AssistedSlash},provider,targets,op:effect.Op,instruction:p.InstructionIndex);
    }
    private static bool WeijieIsDying(WeijieNeed n)=>n.Intent==WeijieIntent.Dying;
    private static bool WeijieIsPlayed(WeijieNeed n)=>n.Intent is WeijieIntent.Duel or WeijieIntent.Group or WeijieIntent.FactionDodge or WeijieIntent.FactionSlashPlayed;
    private IEnumerable<(CardKind Kind,int Target)> WeijieOutputs(WeijieNeed n)
    {
        var kinds=n.Intent==WeijieIntent.WenyiPeach?new[]{CardKind.Peach}:WeijieIsDying(n)?new[]{CardKind.Peach,CardKind.Alcohol}:n.Intent is WeijieIntent.Dodge or WeijieIntent.FactionDodge||n.Intent==WeijieIntent.Group&&ActiveGroupCard!.RequiredCardKind==CardKind.Dodge?new[]{CardKind.Dodge}:n.Intent==WeijieIntent.GenericBasic?new[]{CardKind.Slash,CardKind.FireSlash,CardKind.ThunderSlash,CardKind.Peach,CardKind.Alcohol}:new[]{CardKind.Slash,CardKind.FireSlash,CardKind.ThunderSlash};
        foreach(var kind in kinds)
        {
            var targets=WeijieIsDying(n)||n.Intent==WeijieIntent.WenyiPeach?n.Targets:WeijieIsPlayed(n)||n.Intent==WeijieIntent.Dodge?new[]{n.OpponentSeat??n.ActorSeat}:kind is CardKind.Peach or CardKind.Alcohol?new[]{n.ActorSeat}:n.Targets;
            foreach(var t in targets.Distinct())if(WeijieNativeLegal(n,kind,t))yield return(kind,t);
        }
    }
    private bool WeijieNativeLegal(WeijieNeed n,CardKind kind,int target)
    {
        if(!IsValidPlayerSeat(n.ProviderSeat)||!IsValidPlayerSeat(n.ActorSeat)||!_players[n.ProviderSeat].IsAlive||!_players[n.ActorSeat].IsAlive||_winner!=Winner.None)return false;
        var actor=_players[n.ActorSeat];var provider=_players[n.ProviderSeat];var action=WeijieIsPlayed(n)||n.Intent==WeijieIntent.Dodge?CardActionType.Response:CardActionType.Use;
        if(IsCardUseForbidden(n.ActorSeat,kind,action)||n.ProviderSeat!=n.ActorSeat&&IsCardUseForbidden(n.ProviderSeat,IsSlashCard(kind)?CardKind.Slash:kind,action)||IsSlashCard(kind)&&action==CardActionType.Response&&IsCardUseForbidden(n.ActorSeat,CardKind.Slash,action))return false;
        if(n.Intent==WeijieIntent.Dodge&&IsProgramResponseCardUse(actor,kind)&&HasIssuedPlayPhaseUseBan(actor.Seat))return false;
        if(WeijieIsDying(n))return ActiveDying is{}d&&d.Id==n.OwnerFrameId&&d.ResponderIndex==n.Cursor&&d.ResponderSeat==n.ProviderSeat&&d.VictimSeat==target&&_players[target].IsAlive&&_players[target].Hp<=0&&!HasBeneficiarySuitShield(n.ActorSeat,target,Suit.None)&&(kind==CardKind.Peach&&CanUsePeachToRescue(n.ActorSeat,target)||kind==CardKind.Alcohol&&n.ActorSeat==target&&!HasSelfCardTargetProhibition(target));
        if(n.Intent is WeijieIntent.Dodge or WeijieIntent.Duel or WeijieIntent.Group or WeijieIntent.FactionDodge or WeijieIntent.FactionSlashPlayed)
        {
            var attack=n.Intent==WeijieIntent.FactionDodge?ActiveFactionDefense?.Attack:n.Intent==WeijieIntent.Duel?ActiveDuel?.Attack:n.Intent is WeijieIntent.Group?ActiveGroupCard?.CurrentAttack:n.Intent==WeijieIntent.FactionSlashPlayed?ActiveFactionCardRequest?.ResponseAttack:ActiveCardAttack;
            if(attack is null||attack.ResolutionId!=n.OwnerFrameId||IsYingboUnrespondable(attack.ResolutionId)||IsIssuedCardUnrespondable(attack.ResolutionId)||IsWenjiResponseForbidden(attack.ResolutionId,n.ActorSeat)||IsNearbyTargetResponseProhibited(attack.CardUserSeat,n.ActorSeat,attack.EffectiveCardKind??CardKind.Slash,LifecycleCardUse(attack.ResolutionId)?.TargetSeats??[n.ActorSeat]))return false;
            if(kind==CardKind.Dodge&&(ConvertedSlashSameColorResponseColor(attack)is not null||!IsRecordedSuitResponseAllowed(provider,[],kind)))return false;
            return n.Intent==WeijieIntent.Dodge?kind==CardKind.Dodge&&attack.TargetSeat==n.ActorSeat&&attack.SuccessfulDodgeResponses==n.Cursor:n.Intent==WeijieIntent.Duel?IsSlashCard(kind)&&ActiveDuel?.ResponderSeat==n.ActorSeat&&ActiveDuel.SuccessfulSlashResponses==n.Cursor:n.Intent==WeijieIntent.Group?ActiveGroupCard?.TargetIndex==n.TargetIndex&&MatchesRequiredCard(kind,ActiveGroupCard.RequiredCardKind!.Value):n.Intent==WeijieIntent.FactionDodge?kind==CardKind.Dodge&&ActiveFactionDefense?.CurrentCandidateSeat==n.ProviderSeat&&ActiveFactionDefense.CandidateIndex==n.Cursor:ActiveFactionCardRequest?.CurrentCandidateSeat==n.ProviderSeat&&ActiveFactionCardRequest.CandidateIndex==n.Cursor&&IsSlashCard(kind);
        }
        if(!IsValidPlayerSeat(target)||!_players[target].IsAlive||HasBeneficiarySuitShield(n.ActorSeat,target,Suit.None)||IsDirectedCardTargetProhibited(n.ActorSeat,target,kind)||IsCardTargetProhibited(_players[target],kind,Suit.None))return false;
        if(kind==CardKind.Peach)return n.Intent==WeijieIntent.WenyiPeach?WenyiWeijiePeachLegal(n,target):n.Intent==WeijieIntent.GenericBasic&&target==n.ActorSeat&&IsRuleWounded(actor)&&!HasSelfCardTargetProhibition(n.ActorSeat);
        if(kind==CardKind.Alcohol)return n.Intent==WeijieIntent.GenericBasic&&target==n.ActorSeat&&!actor.HasAlcoholEffect&&!HasSelfCardTargetProhibition(n.ActorSeat)&&CanUseJianjiOrdinaryAlcohol(actor,null);
        if(!IsSlashCard(kind)||target==n.ActorSeat||!n.Targets.Contains(target))return false;
        if(n.Intent==WeijieIntent.BorrowedSword)return ActiveBorrowedSword is{AwaitingSlashChoice:true,ActiveAttack:null}b&&b.ResolutionId==n.OwnerFrameId&&GetWeapon(actor)is not null&&IsLegalBorrowedSwordSlashTarget(actor,_players[target],kind,Suit.None,false,null,0,[]);
        if(n.Intent==WeijieIntent.Qinglong)return ActiveQinglongCrescentBlade is{}q&&q.Attack.ResolutionId==n.OwnerFrameId&&CanUseQinglongCrescentBladeTarget(actor,_players[target],kind,null,false,Suit.None,0,[]);
        if(n.Intent==WeijieIntent.FactionSlashUse&&ActiveFactionCardRequest is{}fr)return fr.CurrentCandidateSeat==n.ProviderSeat&&fr.CandidateIndex==n.Cursor&&IsRedSlashProviderPaymentLegal(fr,kind,[])&&(fr.IsBorrowedSwordUse?IsLegalBorrowedSwordSlashTarget(actor,_players[target],kind,Suit.None,false,null,0,[]):fr.IsQinglongCrescentBladeUse?CanUseQinglongCrescentBladeTarget(actor,_players[target],kind,null,false,Suit.None,0,[]):CanUseProvidedSlashTarget(actor,_players[target],kind,Suit.None,false,null,0,[]));
        var parent=_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==n.OwnerFrameId);var ignore=n.Intent is WeijieIntent.ProgramSlash or WeijieIntent.ProgramNearestSlash||parent is not null&&(IsQigongRequest(parent)||IsWuJingHejiRequest(parent)||n.Intent==WeijieIntent.FixedSlash&&parent.FixedTargetSlash?.IgnoreDistance==true);
        return CanUseSlashTarget(actor,_players[target],new(0,kind,Suit.None,0),effectiveKind:kind,ignoreDistance:ignore,noEffectiveRank:true,physicalCardIds:[]);
    }
    private bool HasWeijieRequiredBasic(CharacterState actor,CardKind required)
    {
        if(!WeijieSources(actor).Any()||!WeijieVictims(actor.Seat).Any())return false;
        var kind=ActiveDying?.ResponderSeat==actor.Seat?DecisionKind.RescueDying:required==CardKind.Dodge?DecisionKind.RespondDodge:DecisionKind.RespondSlash;
        var n=WeijieCurrentNeed(actor.Seat,kind,_resolutionStack.LastOrDefault());return n is not null&&WeijieOutputs(n).Any(o=>MatchesRequiredCard(o.Kind,required)||WeijieIsDying(n)&&required==CardKind.Peach);
    }
    private bool HasWeijieForcedSlash(CharacterState actor,CharacterState target,bool qinglong)=>WeijieSources(actor).Any()&&WeijieVictims(actor.Seat).Any()&&WeijieCurrentNeed(actor.Seat,qinglong?DecisionKind.QinglongCrescentBlade:DecisionKind.RespondSlash,_resolutionStack.LastOrDefault())is{}n&&n.Targets.Contains(target.Seat)&&WeijieOutputs(n).Any();
    private bool HasWeijieForcedSlash(CharacterState actor,CharacterState target,BorrowedSwordHandle pending)=>HasWeijieCapability&&SameContinuationOwner(ActiveBorrowedSword,pending)&&LifecycleCardUse(pending.ResolutionId)is{}use&&_resolutionStack.LastOrDefault()?.Id==use.Id&&pending.ActiveAttack is null&&pending.WeaponOwnerSeat==actor.Seat&&pending.SlashTargetSeat==target.Seat&&actor.IsAlive&&target.IsAlive&&GetWeapon(actor)is not null&&WeijieSources(actor).Any()&&WeijieVictims(actor.Seat).Any()&&SlashKinds.Any(kind=>!IsCardUseForbidden(actor.Seat,kind,CardActionType.Use)&&!HasBeneficiarySuitShield(actor.Seat,target.Seat,Suit.None)&&!IsCardTargetProhibited(target,kind,Suit.None)&&!IsDirectedCardTargetProhibited(actor.Seat,target.Seat,kind)&&IsLegalBorrowedSwordSlashTarget(actor,target,kind,Suit.None,false,null,0,[]));
    private bool HasWeijieForcedSlash(CharacterState actor,CharacterState target,QinglongCrescentBladeHandle pending)=>HasWeijieCapability&&SameAttackOwner(ActiveCardAttack,pending.Attack)&&LifecycleCardUse(pending.Attack.ResolutionId)is{}use&&_resolutionStack.LastOrDefault()?.Id==use.Id&&pending.Attack.SourceSeat==actor.Seat&&pending.Attack.TargetSeat==target.Seat&&actor.IsAlive&&target.IsAlive&&WeijieSources(actor).Any()&&WeijieVictims(actor.Seat).Any()&&SlashKinds.Any(kind=>!IsCardUseForbidden(actor.Seat,kind,CardActionType.Use)&&!HasBeneficiarySuitShield(actor.Seat,target.Seat,Suit.None)&&!IsCardTargetProhibited(target,kind,Suit.None)&&!IsDirectedCardTargetProhibited(actor.Seat,target.Seat,kind)&&CanUseQinglongCrescentBladeTarget(actor,target,kind,null,false,Suit.None,0,[]));
    private bool HasWeijieProgramSlash(CharacterState actor,IEnumerable<int> targets)=>WeijieSources(actor).Any()&&WeijieVictims(actor.Seat).Any()&&WeijieCurrentNeed(actor.Seat,DecisionKind.ProgramTrigger,_resolutionStack.LastOrDefault())is{}n&&WeijieOutputs(n).Any(o=>IsSlashCard(o.Kind)&&targets.Contains(o.Target));
    private bool HasWeijieGenericBasic(ProgramSkillFrame f)=>f.KeBiNengPingduan is{}r&&WeijieSources(_players[r.ActorSeat]).Any()&&WeijieVictims(r.ActorSeat).Any()&&WeijieCurrentNeed(r.ActorSeat,DecisionKind.ProgramTrigger,f)is{}n&&WeijieOutputs(n).Any();
    private PendingDecision? AddWeijieRequestChoices(PendingDecision? p)
    {
        if(p is null||!HasWeijieCapability||p.Choices.Any(c=>c.Parameters.ContainsKey(WeijieOpen))||CaptureWeijieNeed(p)is not{}n||!WeijieSources(_players[p.PlayerSeat]).Any()||!WeijieVictims(p.PlayerSeat).Any()||!WeijieOutputs(n).Any())return p;
        if(p.PromptId==default)p=p with{PromptId=CreatePromptId()};var choices=p.Choices.ToList();foreach(var(source,_)in WeijieSources(_players[p.PlayerSeat])){var args=new Dictionary<string,string>{{WeijieOpen,"open"}};AddConversionParameters(args,source);choices.Add(new(new($"weijie.{n.RequestFrameId}.{source.SkillInstanceId}"),"发动【诿解】：弃置距离1的他人一张暗手牌尝试提供基本牌",[],[],args));}return p with{Choices=Array.AsReadOnly(choices.ToArray())};
    }
    private static PendingDecision WeijieNativeDecision(PendingDecision p)=>p.Choices.Any(c=>c.Parameters.ContainsKey(WeijieOpen))?p with{Choices=Array.AsReadOnly(p.Choices.Where(c=>!c.Parameters.ContainsKey(WeijieOpen)).ToArray())}:p;
}
