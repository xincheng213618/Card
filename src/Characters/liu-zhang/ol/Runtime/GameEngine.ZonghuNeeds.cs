using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ZonghuOpen=global::CardGame.Core.RuntimeSkills.RuntimeRules_liu_zhang_ol_ZonghuNeeds.ZonghuOpen,ZonghuChoice="zonghu-choice",ZonghuGiftReason="skill-program.liu-zhang.zonghu.give";
    private bool HasZonghuCapability=>_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveZonghuRequiredUseRequest);
    private static CardConversionSource ZonghuSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_liu_zhang_ol_ZonghuNeeds.ZonghuSource(f);
    private bool ZonghuTurnOpen(int seat)=>_setupComplete&&_turnNumber>0&&_turnNumber==_turnProgression.TurnNumber&&!ProgramEventHistory<ActualTurnCursorReturnedEvent>().Any(e=>e.ActualTurn==_turnNumber&&e.OwnerSeat==_turnProgression.OwnerSeat);
    private bool ZonghuSpent(int seat)=>ProgramEventHistory<ZonghuCostPaidEvent>().Any(e=>e.Source.OwnerSeat==seat&&e.ActualTurn==_turnNumber&&e.ActualTurnOwner==_turnProgression.OwnerSeat);
    private IEnumerable<(CardConversionSource Source,string Hash)> ZonghuSources(CharacterState actor)
    {
        if(!HasZonghuCapability||!actor.IsAlive||_winner!=Winner.None||!ZonghuTurnOpen(actor.Seat)||ZonghuSpent(actor.Seat))yield break;
        foreach(var i in GetSkillBindingShard(actor).ProgramInstances)
        foreach(var a in i.Program.Activations.Where(a=>a.Effects is[{Op:SkillProgramEffectOp.ResolveZonghuRequiredUseRequest}]))
            yield return(new(i.SkillId,a.Id,actor.Seat,i.SkillInstanceId),i.Program.GameplayHash);
    }
    private int ZonghuCost=>Math.Min(3,Math.Max(1,_roundNumber));
    private IReadOnlyList<Card> ZonghuGiftCards(int seat)=>GetHand(_players[seat]).Concat(GetEquipment(seat)).Where(c=>!c.IsGeneralWeapon&&!IsForeignEquipmentDiscardPrevented(seat,c,_cardZones.GetLocation(c.Id),OwnedCardMoveIntent.Transfer)).OrderBy(c=>c.Id).ToArray();
    private IEnumerable<int> ZonghuVictims(int actor)=>ZonghuGiftCards(actor).Count>=ZonghuCost?_players.Where(p=>p.IsAlive&&p.Seat!=actor).Select(p=>p.Seat):[];
    private bool ZonghuExcludedSource(ResolutionFrame? top)=>top is ProgramSkillFrame f&&f.SkillId is "classic:zhongyong" or "classic:luanwu" or "boundary:luanwu" or "ol:qiaowu" or "ol:nisiwohuo";
    private ZonghuNeed ZonghuPlayNeed(int seat)=>new(ZonghuIntent.Play,0,0,null,seat,seat,null,null,0,0,0,0,null,_players.Where(p=>p.IsAlive&&p.Seat!=seat).Select(p=>p.Seat).ToArray());
    private ZonghuNeed? CaptureZonghuNeed(PendingDecision original,ResolutionFrame? suppliedNativeTop=null)=>ZonghuCurrentNeed(original.PlayerSeat,original.Kind,suppliedNativeTop??_resolutionStack.LastOrDefault());
    private ZonghuNeed? ZonghuCurrentNeed(int provider,DecisionKind kind,ResolutionFrame? top)
    {
        if(!HasZonghuCapability||!IsValidPlayerSeat(provider)||top is ProgramSkillFrame{Zonghu:not null}||ZonghuExcludedSource(top))return null;
        if(kind==DecisionKind.PlayCard&&_currentSeat==provider&&_phase==TurnPhase.Play&&(_resolutionStack.Count==0||top is null))return ZonghuPlayNeed(provider);
        if(top is null||kind==DecisionKind.RescueDying||kind==DecisionKind.RespondDodge&&(ActiveFactionDefense?.CurrentCandidateSeat==provider||ActiveGroupCard?.CurrentAttack?.TargetSeat==provider)||kind==DecisionKind.RespondSlash&&(ActiveFactionCardRequest?.CurrentCandidateSeat==provider||ActiveDuel?.ResponderSeat==provider||ActiveGroupCard?.CurrentAttack?.TargetSeat==provider))return null;
        var owner=top is ResponseWindowFrame response?response.ParentFrameId:top.Id;
        long? action=LifecycleCardUse(owner)?.Action?.ActionId;
        ZonghuNeed N(ZonghuIntent intent,int actor,IReadOnlyList<int> targets,int cursor=0,int? requester=null,int? opponent=null,int index=0,int round=0,SkillProgramEffectOp? op=null,int instruction=0,long? ownerId=null)=>new(intent,ownerId??owner,top.Id,action,provider,actor,requester,opponent,cursor,index,round,instruction,op,targets);
        if(kind==DecisionKind.RescueDying&&top is DyingFrame d&&d.ResponderSeat==provider)return N(ZonghuIntent.Dying,provider,[d.VictimSeat],d.ResponderIndex,ownerId:d.Id);
        if(kind==DecisionKind.RespondDodge)
        {
            if(ActiveFactionDefense is{}fd&&fd.CurrentCandidateSeat==provider&&SameAttackOwner(fd.Attack,ActiveCardAttack))return N(ZonghuIntent.FactionDodge,fd.OwnerSeat,[],fd.CandidateIndex,fd.OwnerSeat,fd.Attack.SourceSeat,ownerId:fd.Attack.ResolutionId);
            if(ActiveGroupCard is{Effect:GroupCardEffect.ResponseAttack,CurrentAttack:{}ga}g&&ga.TargetSeat==provider&&g.RequiredCardKind==CardKind.Dodge)return N(ZonghuIntent.Group,provider,[],ga.SuccessfulDodgeResponses,opponent:g.SourceSeat,index:g.TargetIndex,ownerId:g.ResolutionId);
            if(ActiveCardAttack is{}a&&a.TargetSeat==provider&&IsSlashCard(a.EffectiveCardKind??CardKind.Dodge))return N(ZonghuIntent.Dodge,provider,[],a.SuccessfulDodgeResponses,opponent:a.SourceSeat,ownerId:a.ResolutionId);
        }
        if(kind==DecisionKind.RespondSlash)
        {
            if(ActiveFactionCardRequest is{AwaitingProviders:true}f&&f.CurrentCandidateSeat==provider)
            {var played=!IsFactionSlashUse(f);var opponent=f.Purpose==FactionCardRequestPurpose.DuelResponse?ActiveDuel?.OpponentSeat:f.ResponseAttack?.SourceSeat;var program=_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(p=>p.Id==f.ProgramSkillFrameId);return N(played?ZonghuIntent.FactionSlashPlayed:ZonghuIntent.FactionSlashUse,f.OwnerSeat,f.TargetSeat is{}t?[t]:[],f.CandidateIndex,f.OwnerSeat,opponent,op:program is null?null:GetPausedPrivateOfferEffect(program)?.Op,instruction:program?.InstructionIndex??0,ownerId:f.OwnerFrameId);}
            if(ActiveBorrowedSword is{AwaitingSlashChoice:true,ActiveAttack:null}b&&b.WeaponOwnerSeat==provider)return N(ZonghuIntent.BorrowedSword,provider,[b.SlashTargetSeat],opponent:b.SourceSeat,ownerId:b.ResolutionId);
            if(ActiveDuel is{}duel&&duel.ResponderSeat==provider)return N(ZonghuIntent.Duel,provider,[],duel.SuccessfulSlashResponses,opponent:duel.OpponentSeat,ownerId:duel.ResolutionId);
            if(ActiveGroupCard is{Effect:GroupCardEffect.ResponseAttack,CurrentAttack:{}ga}g&&ga.TargetSeat==provider&&g.RequiredCardKind==CardKind.Slash)return N(ZonghuIntent.Group,provider,[],opponent:g.SourceSeat,index:g.TargetIndex,ownerId:g.ResolutionId);
        }
        if(kind==DecisionKind.QinglongCrescentBlade&&ActiveQinglongCrescentBlade is{}q&&q.Attack.SourceSeat==provider&&SameAttackOwner(ActiveCardAttack,q.Attack))return N(ZonghuIntent.Qinglong,provider,[q.Attack.TargetSeat],opponent:q.Attack.TargetSeat,ownerId:q.Attack.ResolutionId);
        if(kind!=DecisionKind.ProgramTrigger||top is not ProgramSkillFrame p||GetPausedPrivateOfferEffect(p)is not{}effect)return null;
        if(IsChanshuangSlashRequest(p)&&TryGetChanshuangSlashRequest(p,out var requestedActor,out var requestedTargets)&&provider==requestedActor)return N(ZonghuIntent.AssistedSlash,provider,requestedTargets,op:effect.Op,instruction:p.InstructionIndex);
        if(IsQigongRequest(p)&&p.LvKuangLvXiang!.Actor==provider)return N(ZonghuIntent.AssistedSlash,provider,[p.LvKuangLvXiang.Target],op:effect.Op,instruction:p.InstructionIndex);
        if(IsWuJingHejiRequest(p)&&p.OwnerSeat==provider)return N(ZonghuIntent.AssistedSlash,provider,[p.WuJing!.TargetSeat],op:effect.Op,instruction:p.InstructionIndex);
        if(IsCaoChunInitialRequest(p)&&p.OwnerSeat==provider)return N(ZonghuIntent.AssistedSlash,provider,_players.Where(t=>t.IsAlive&&t.Seat!=provider).Select(t=>t.Seat).ToArray(),op:effect.Op,instruction:p.InstructionIndex);
        if(p.KeBiNengPingduan is{Stage:KeBiNengPingduanStage.BasicChoice,ActorSeat:var actor}&&actor==provider&&ValidKeBiNeng(p))return N(ZonghuIntent.GenericBasic,provider,_players.Where(t=>t.IsAlive).Select(t=>t.Seat).ToArray(),op:effect.Op,instruction:p.InstructionIndex);
        if(!RoundOnceCardlessProgramActor(p,effect.Op,provider))return null;
        var targets=RoundOnceCardlessProgramTargets(p,provider,effect.Op);if(targets.Length==0)return null;
        return N(effect.Op switch{SkillProgramEffectOp.RequestSlashByTarget=>ZonghuIntent.ProgramSlash,SkillProgramEffectOp.RequestSlashByNearest=>ZonghuIntent.ProgramNearestSlash,SkillProgramEffectOp.RequestLegalSlashByNearest=>ZonghuIntent.NearestLegalSlash,SkillProgramEffectOp.UseOwnerSlashAgainstTurnOwner=>ZonghuIntent.FixedSlash,_=>ZonghuIntent.AssistedSlash},provider,targets,op:effect.Op,instruction:p.InstructionIndex);
    }
    private static bool ZonghuIsDying(ZonghuNeed n) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_liu_zhang_ol_ZonghuNeeds.ZonghuIsDying(n);
    private static bool ZonghuIsPlayed(ZonghuNeed n) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_liu_zhang_ol_ZonghuNeeds.ZonghuIsPlayed(n);
    private IEnumerable<(CardKind Kind,int Target)> ZonghuOutputs(ZonghuNeed n)
    {
        if(n.ProviderSeat!=n.ActorSeat||n.RequesterSeat is not null||n.Intent is ZonghuIntent.Dying or ZonghuIntent.Group or ZonghuIntent.Duel or ZonghuIntent.FactionDodge or ZonghuIntent.FactionSlashPlayed or ZonghuIntent.FactionSlashUse)yield break;
        if(n.Intent==ZonghuIntent.Dodge){if(ZonghuNativeLegal(n,CardKind.Dodge,n.OpponentSeat??n.ActorSeat))yield return(CardKind.Dodge,n.OpponentSeat??n.ActorSeat);yield break;}
        foreach(var target in n.Targets.Distinct())if(ZonghuNativeLegal(n,CardKind.Slash,target))yield return(CardKind.Slash,target);
    }
    private bool ZonghuNativeLegal(ZonghuNeed n,CardKind kind,int target)
    {
        if(!IsValidPlayerSeat(n.ProviderSeat)||!IsValidPlayerSeat(n.ActorSeat)||!_players[n.ProviderSeat].IsAlive||!_players[n.ActorSeat].IsAlive||_winner!=Winner.None)return false;
        if(n.ProviderSeat!=n.ActorSeat||kind is not(CardKind.Slash or CardKind.Dodge))return false;var actor=_players[n.ActorSeat];var provider=_players[n.ProviderSeat];var action=ZonghuIsPlayed(n)||n.Intent==ZonghuIntent.Dodge?CardActionType.Response:CardActionType.Use;
        if(IsCardUseForbidden(n.ActorSeat,kind,action)||n.ProviderSeat!=n.ActorSeat&&IsCardUseForbidden(n.ProviderSeat,IsSlashCard(kind)?CardKind.Slash:kind,action)||IsSlashCard(kind)&&action==CardActionType.Response&&IsCardUseForbidden(n.ActorSeat,CardKind.Slash,action))return false;
        if(n.Intent==ZonghuIntent.Dodge&&IsProgramResponseCardUse(actor,kind)&&HasIssuedPlayPhaseUseBan(actor.Seat))return false;
        if(ZonghuIsDying(n))return ActiveDying is{}d&&d.Id==n.OwnerFrameId&&d.ResponderIndex==n.Cursor&&d.ResponderSeat==n.ProviderSeat&&d.VictimSeat==target&&_players[target].IsAlive&&_players[target].Hp<=0&&!HasBeneficiarySuitShield(n.ActorSeat,target,Suit.None)&&(kind==CardKind.Peach&&CanUsePeachToRescue(n.ActorSeat,target)||kind==CardKind.Alcohol&&n.ActorSeat==target&&!HasSelfCardTargetProhibition(target));
        if(n.Intent is ZonghuIntent.Dodge or ZonghuIntent.Duel or ZonghuIntent.Group or ZonghuIntent.FactionDodge or ZonghuIntent.FactionSlashPlayed)
        {
            var attack=n.Intent==ZonghuIntent.FactionDodge?ActiveFactionDefense?.Attack:n.Intent==ZonghuIntent.Duel?ActiveDuel?.Attack:n.Intent is ZonghuIntent.Group?ActiveGroupCard?.CurrentAttack:n.Intent==ZonghuIntent.FactionSlashPlayed?ActiveFactionCardRequest?.ResponseAttack:ActiveCardAttack;
            if(attack is null||attack.ResolutionId!=n.OwnerFrameId||IsYingboUnrespondable(attack.ResolutionId)||IsIssuedCardUnrespondable(attack.ResolutionId)||IsWenjiResponseForbidden(attack.ResolutionId,n.ActorSeat)||IsNearbyTargetResponseProhibited(attack.CardUserSeat,n.ActorSeat,attack.EffectiveCardKind??CardKind.Slash,LifecycleCardUse(attack.ResolutionId)?.TargetSeats??[n.ActorSeat]))return false;
            if(kind==CardKind.Dodge&&(ConvertedSlashSameColorResponseColor(attack)is not null||!IsRecordedSuitResponseAllowed(provider,[],kind)))return false;
            return n.Intent==ZonghuIntent.Dodge?kind==CardKind.Dodge&&attack.TargetSeat==n.ActorSeat&&attack.SuccessfulDodgeResponses==n.Cursor:n.Intent==ZonghuIntent.Duel?IsSlashCard(kind)&&ActiveDuel?.ResponderSeat==n.ActorSeat&&ActiveDuel.SuccessfulSlashResponses==n.Cursor:n.Intent==ZonghuIntent.Group?ActiveGroupCard?.TargetIndex==n.TargetIndex&&MatchesRequiredCard(kind,ActiveGroupCard.RequiredCardKind!.Value):n.Intent==ZonghuIntent.FactionDodge?kind==CardKind.Dodge&&ActiveFactionDefense?.CurrentCandidateSeat==n.ProviderSeat&&ActiveFactionDefense.CandidateIndex==n.Cursor:ActiveFactionCardRequest?.CurrentCandidateSeat==n.ProviderSeat&&ActiveFactionCardRequest.CandidateIndex==n.Cursor&&IsSlashCard(kind);
        }
        if(!IsValidPlayerSeat(target)||!_players[target].IsAlive||HasBeneficiarySuitShield(n.ActorSeat,target,Suit.None)||IsDirectedCardTargetProhibited(n.ActorSeat,target,kind)||IsCardTargetProhibited(_players[target],kind,Suit.None))return false;
        if(kind==CardKind.Peach)return n.Intent==ZonghuIntent.GenericBasic&&target==n.ActorSeat&&IsRuleWounded(actor)&&!HasSelfCardTargetProhibition(n.ActorSeat);
        if(kind==CardKind.Alcohol)return n.Intent==ZonghuIntent.GenericBasic&&target==n.ActorSeat&&!actor.HasAlcoholEffect&&!HasSelfCardTargetProhibition(n.ActorSeat)&&CanUseJianjiOrdinaryAlcohol(actor,null);
        if(!IsSlashCard(kind)||target==n.ActorSeat||!n.Targets.Contains(target))return false;
        if(n.Intent==ZonghuIntent.BorrowedSword)return ActiveBorrowedSword is{AwaitingSlashChoice:true,ActiveAttack:null}b&&b.ResolutionId==n.OwnerFrameId&&GetWeapon(actor)is not null&&IsLegalBorrowedSwordSlashTarget(actor,_players[target],kind,Suit.None,false,null,0,[]);
        if(n.Intent==ZonghuIntent.Qinglong)return ActiveQinglongCrescentBlade is{}q&&q.Attack.ResolutionId==n.OwnerFrameId&&CanUseQinglongCrescentBladeTarget(actor,_players[target],kind,null,false,Suit.None,0,[]);
        if(n.Intent==ZonghuIntent.FactionSlashUse&&ActiveFactionCardRequest is{}fr)return fr.CurrentCandidateSeat==n.ProviderSeat&&fr.CandidateIndex==n.Cursor&&IsRedSlashProviderPaymentLegal(fr,kind,[])&&(fr.IsBorrowedSwordUse?IsLegalBorrowedSwordSlashTarget(actor,_players[target],kind,Suit.None,false,null,0,[]):fr.IsQinglongCrescentBladeUse?CanUseQinglongCrescentBladeTarget(actor,_players[target],kind,null,false,Suit.None,0,[]):CanUseProvidedSlashTarget(actor,_players[target],kind,Suit.None,false,null,0,[]));
        var parent=_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==n.OwnerFrameId);var ignore=false;
        return CanSpendSlashUse(actor,_players[target],false,kind,physicalCardIds:[])&&CanUseSlashTarget(actor,_players[target],new(0,kind,Suit.None,0),effectiveKind:kind,ignoreDistance:ignore,noEffectiveRank:true,physicalCardIds:[]);
    }
    private bool HasZonghuRequiredBasic(CharacterState actor,CardKind required)
    {
        if(!ZonghuSources(actor).Any()||!ZonghuVictims(actor.Seat).Any())return false;
        var kind=ActiveDying?.ResponderSeat==actor.Seat?DecisionKind.RescueDying:required==CardKind.Dodge?DecisionKind.RespondDodge:DecisionKind.RespondSlash;
        var n=ZonghuCurrentNeed(actor.Seat,kind,_resolutionStack.LastOrDefault());return n is not null&&ZonghuOutputs(n).Any(o=>MatchesRequiredCard(o.Kind,required)||ZonghuIsDying(n)&&required==CardKind.Peach);
    }
    private bool HasZonghuForcedSlash(CharacterState actor,CharacterState target,bool qinglong)=>ZonghuSources(actor).Any()&&ZonghuVictims(actor.Seat).Any()&&ZonghuCurrentNeed(actor.Seat,qinglong?DecisionKind.QinglongCrescentBlade:DecisionKind.RespondSlash,_resolutionStack.LastOrDefault())is{}n&&n.Targets.Contains(target.Seat)&&ZonghuOutputs(n).Any();
    private bool HasZonghuForcedSlash(CharacterState actor,CharacterState target,BorrowedSwordHandle pending)=>HasZonghuCapability&&SameContinuationOwner(ActiveBorrowedSword,pending)&&LifecycleCardUse(pending.ResolutionId)is{}use&&_resolutionStack.LastOrDefault()?.Id==use.Id&&pending.ActiveAttack is null&&pending.WeaponOwnerSeat==actor.Seat&&pending.SlashTargetSeat==target.Seat&&actor.IsAlive&&target.IsAlive&&GetWeapon(actor)is not null&&ZonghuSources(actor).Any()&&ZonghuVictims(actor.Seat).Any()&&new[]{CardKind.Slash}.Any(kind=>!IsCardUseForbidden(actor.Seat,kind,CardActionType.Use)&&!HasBeneficiarySuitShield(actor.Seat,target.Seat,Suit.None)&&!IsCardTargetProhibited(target,kind,Suit.None)&&!IsDirectedCardTargetProhibited(actor.Seat,target.Seat,kind)&&IsLegalBorrowedSwordSlashTarget(actor,target,kind,Suit.None,false,null,0,[]));
    private bool HasZonghuForcedSlash(CharacterState actor,CharacterState target,QinglongCrescentBladeHandle pending)=>HasZonghuCapability&&SameAttackOwner(ActiveCardAttack,pending.Attack)&&LifecycleCardUse(pending.Attack.ResolutionId)is{}use&&_resolutionStack.LastOrDefault()?.Id==use.Id&&pending.Attack.SourceSeat==actor.Seat&&pending.Attack.TargetSeat==target.Seat&&actor.IsAlive&&target.IsAlive&&ZonghuSources(actor).Any()&&ZonghuVictims(actor.Seat).Any()&&new[]{CardKind.Slash}.Any(kind=>!IsCardUseForbidden(actor.Seat,kind,CardActionType.Use)&&!HasBeneficiarySuitShield(actor.Seat,target.Seat,Suit.None)&&!IsCardTargetProhibited(target,kind,Suit.None)&&!IsDirectedCardTargetProhibited(actor.Seat,target.Seat,kind)&&CanUseQinglongCrescentBladeTarget(actor,target,kind,null,false,Suit.None,0,[]));
    private bool HasZonghuProgramSlash(CharacterState actor,IEnumerable<int> targets)=>ZonghuSources(actor).Any()&&ZonghuVictims(actor.Seat).Any()&&ZonghuCurrentNeed(actor.Seat,DecisionKind.ProgramTrigger,_resolutionStack.LastOrDefault())is{}n&&ZonghuOutputs(n).Any(o=>IsSlashCard(o.Kind)&&targets.Contains(o.Target));
    private bool HasZonghuGenericBasic(ProgramSkillFrame f)=>f.KeBiNengPingduan is{}r&&ZonghuSources(_players[r.ActorSeat]).Any()&&ZonghuVictims(r.ActorSeat).Any()&&ZonghuCurrentNeed(r.ActorSeat,DecisionKind.ProgramTrigger,f)is{}n&&ZonghuOutputs(n).Any();
    private PendingDecision? AddZonghuRequestChoices(PendingDecision? p)
    {
        if(p is null||!HasZonghuCapability||p.Choices.Any(c=>c.Parameters.ContainsKey(ZonghuOpen))||CaptureZonghuNeed(p)is not{}n||!ZonghuSources(_players[p.PlayerSeat]).Any()||!ZonghuVictims(p.PlayerSeat).Any()||!ZonghuOutputs(n).Any())return p;
        if(p.PromptId==default)p=p with{PromptId=CreatePromptId()};var choices=p.Choices.ToList();foreach(var(source,_)in ZonghuSources(_players[p.PlayerSeat])){var args=new Dictionary<string,string>{{ZonghuOpen,"open"}};AddConversionParameters(args,source);choices.Add(new(new($"zonghu.{n.RequestFrameId}.{source.SkillInstanceId}"),"发动【宗护】：交给他人牌，视为本人使用杀或闪",[],[],args));}return p with{Choices=Array.AsReadOnly(choices.ToArray())};
    }
    private static PendingDecision ZonghuNativeDecision(PendingDecision p) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_liu_zhang_ol_ZonghuNeeds.ZonghuNativeDecision(p);
}
