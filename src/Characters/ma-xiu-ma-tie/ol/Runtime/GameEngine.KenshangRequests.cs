using System.Globalization;
using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record KenshangRequestReceipt(long OwnerFrameId,int InstructionIndex,SkillProgramEffectOp? RequestOp,int ActorSeat,int TargetSeat,CardConversionSource Source,string GameplayHash,PendingDecision OriginalDecision)
{
    private PendingDecision _original=RequestedDeckBasicFrame.FreezeDecision(OriginalDecision);
    public PendingDecision OriginalDecision{get=>_original;init=>_original=RequestedDeckBasicFrame.FreezeDecision(value);}
    private IReadOnlyList<int> _selected=Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Selected{get=>_selected;init=>_selected=Array.AsReadOnly(value.ToArray());}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public CardConversionSource? RequestSource{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public string? RequestGameplayHash{get;init;}
}
public sealed record KenshangRequestStartedEvent(long OwnerFrameId,int InstructionIndex,SkillProgramEffectOp? RequestOp,int ActorSeat,int TargetSeat,CardConversionSource Source,string GameplayHash,PromptId OriginalPromptId,long OriginalRevision,string OriginalDecisionHash):IGameEvent;
public sealed record KenshangRequestMaterialSelectedEvent(long OwnerFrameId,PromptId OriginalPromptId,int ActorSeat,int Index,int CardId,CardLocation From):IGameEvent;
public sealed record KenshangRequestFinishedEvent(long OwnerFrameId,int ActorSeat,bool Accepted,int MaterialCount):IGameEvent;
public sealed record KenshangKeActionAcceptedEvent(long ProgramFrameId,int InstructionIndex,CardConversionSource RequestSource,string RequestGameplayHash,int ActorSeat,int TargetSeat,CardConversionSource Source,string GameplayHash,PromptId OriginalPromptId,int MaterialCount):IGameEvent;

public sealed partial class GameEngine
{
    private const string KenshangRequestOpen="kenshang-request-open",KenshangRequestPick="kenshang-request-pick";
    private IEnumerable<CardConversionSource> KenshangSources(CharacterState actor)=>GetSkillBindingShard(actor).ProgramInstances.SelectMany(i=>i.Program.ViewAs.Where(r=>r.KenshangUse==true&&r.Condition.Evaluate(CreateSkillContext(actor))).Select(r=>new CardConversionSource(i.SkillId,r.Id,actor.Seat,i.SkillInstanceId)));
    private IReadOnlyList<Card> KenshangRequestCards(CardConversionSource source)
    {
        if(!IsValidPlayerSeat(source.OwnerSeat)||!_players[source.OwnerSeat].IsAlive||!HasRuntimeSkillInstance(_players[source.OwnerSeat],source.SkillId,source.SkillInstanceId)||ViewAsRule(source)is not{KenshangUse:true}r||IsCardUseForbidden(source.OwnerSeat,CardKind.Slash,CardActionType.Use))return [];
        var actor=_players[source.OwnerSeat];return r.SourceZones.SelectMany(z=>_cardZones.CardsAt(new(z,actor.Seat))).Where(c=>!IsTurnHandCardRestricted(actor,c)&&!HasProgramCardIdentity(actor,c)&&!IsTurnPhysicalUseForbidden(actor.Seat,[c.Id])).DistinctBy(c=>c.Id).OrderBy(c=>c.Id).ToArray();
    }
    private IReadOnlyList<PromptChoice> KenshangProgramSlashChoices(ProgramSkillFrame f,int actorSeat,IReadOnlyList<int> targets,string? resultBind=null)
    {
        if(!IsValidPlayerSeat(actorSeat)||!_players[actorSeat].IsAlive||GetPausedPrivateOfferEffect(f)is not{}effect||!KenshangProgramActor(f,effect.Op,actorSeat))return [];
        var exact=KenshangProgramTargets(f,actorSeat,effect.Op);var result=new List<PromptChoice>();
        foreach(var source in KenshangSources(_players[actorSeat]).Where(s=>KenshangRequestCards(s).Count>=2))
        foreach(var target in targets.Where(exact.Contains).Distinct())
        {
            if(target==actorSeat||!IsValidPlayerSeat(target)||!_players[target].IsAlive)continue;
            var p=new Dictionary<string,string>{{"program-action",KenshangRequestOpen},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)},{"actor-seat",actorSeat.ToString(CultureInfo.InvariantCulture)},{"target-seat",target.ToString(CultureInfo.InvariantCulture)}};AddConversionParameters(p,source);
            result.Add(new(new($"kenshang.request.{f.Id}.{source.SkillInstanceId}.{source.BindingId}.{actorSeat}.{target}"),"发动【垦伤】：私下选择至少两张手牌或装备牌当【杀】使用",[],[target],p));
        }
        return Array.AsReadOnly(result.ToArray());
    }
    private bool HasKenshangProgramSlash(CharacterState actor,IEnumerable<int> targets)=>actor.IsAlive&&targets.Any(s=>IsValidPlayerSeat(s)&&s!=actor.Seat&&_players[s].IsAlive)&&KenshangSources(actor).Any(s=>KenshangRequestCards(s).Count>=2);
    private bool KenshangProgramActor(ProgramSkillFrame f,SkillProgramEffectOp op,int actor)=>
        op==SkillProgramEffectOp.ResolvePingduanOrderedBasicRecastEquipment?f.KeBiNengPingduan is{Stage:KeBiNengPingduanStage.BasicChoice}r&&r.ActorSeat==actor&&ValidKeBiNeng(f):RoundOnceCardlessProgramActor(f,op,actor);
    private int[] KenshangProgramTargets(ProgramSkillFrame f,int actor,SkillProgramEffectOp op)=>op==SkillProgramEffectOp.ResolvePingduanOrderedBasicRecastEquipment&&KenshangProgramActor(f,op,actor)?_players.Where(p=>p.IsAlive&&p.Seat!=actor).Select(p=>p.Seat).ToArray():RoundOnceCardlessProgramTargets(f,actor,op);
    private IReadOnlyList<PromptChoice> KenshangBorrowedSlashChoices(BorrowedSwordHandle pending)
    {
        if(!SameContinuationOwner(ActiveBorrowedSword,pending)||!IsValidPlayerSeat(pending.WeaponOwnerSeat)||!_players[pending.WeaponOwnerSeat].IsAlive||!_players[pending.SlashTargetSeat].IsAlive)return [];
        return Array.AsReadOnly(KenshangSources(_players[pending.WeaponOwnerSeat]).Where(s=>KenshangRequestCards(s).Count>=2).Select(s=>
        {
            var p=new Dictionary<string,string>{{"program-action",KenshangRequestOpen},{"response",KenshangRequestOpen},{"frame-id",pending.ResolutionId.ToString(CultureInfo.InvariantCulture)},{"actor-seat",pending.WeaponOwnerSeat.ToString(CultureInfo.InvariantCulture)},{"target-seat",pending.SlashTargetSeat.ToString(CultureInfo.InvariantCulture)}};AddConversionParameters(p,s);
            return new PromptChoice(new($"kenshang.borrowed.{pending.ResolutionId}.{s.SkillInstanceId}.{s.BindingId}"),"发动【垦伤】：将至少两张手牌或装备牌当【杀】使用",[],[pending.SlashTargetSeat],p);
        }).ToArray());
    }
    private bool TryBeginKenshangBorrowedSlash(BorrowedSwordHandle pending,PromptChoice selected)
    {
        if(selected.Parameters.GetValueOrDefault("response")!=KenshangRequestOpen)return false;
        if(_pendingDecision is not{Kind:DecisionKind.RespondSlash}original||!pending.AwaitingSlashChoice||original.PlayerSeat!=pending.WeaponOwnerSeat||!KenshangBorrowedSlashChoices(pending).Any(c=>AssistedChoicesEqual([c],[selected]))||LifecycleCardUse(pending.ResolutionId)is not{KenshangRequest:null}parent||_resolutionStack.LastOrDefault()is not ResponseWindowFrame response||response.ParentFrameId!=parent.Id)throw new InvalidOperationException("Kenshang lost its original Borrowed Sword Slash-use request.");
        var source=RequireConversionSource(selected);var r=new KenshangRequestReceipt(parent.Id,0,null,pending.WeaponOwnerSeat,pending.SlashTargetSeat,source,_contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash,original);
        UpdateLifecycleCardUse(parent.Id,u=>u with{KenshangRequest=r});BeginKenshangRequest(r);return true;
    }
    private IReadOnlyList<PromptChoice> KenshangQinglongSlashChoices(QinglongCrescentBladeHandle pending)
    {
        var attack=pending.Attack;var actor=_players[attack.SourceSeat];var target=_players[attack.TargetSeat];
        if(!SameAttackOwner(ActiveCardAttack,attack)||!CanUseQinglongCrescentBladeTarget(actor,target))return [];
        return Array.AsReadOnly(KenshangSources(actor).Where(s=>KenshangRequestCards(s).Count>=2).Select(s=>
        {var p=new Dictionary<string,string>{{"program-action",KenshangRequestOpen},{"action",KenshangRequestOpen},{"frame-id",attack.ResolutionId.ToString(CultureInfo.InvariantCulture)},{"actor-seat",actor.Seat.ToString(CultureInfo.InvariantCulture)},{"target-seat",target.Seat.ToString(CultureInfo.InvariantCulture)}};AddConversionParameters(p,s);return new PromptChoice(new($"kenshang.qinglong.{attack.ResolutionId}.{s.SkillInstanceId}.{s.BindingId}"),"发动【垦伤】：选择至少两张牌追杀",[],[target.Seat],p);}).ToArray());
    }
    private bool TryBeginKenshangQinglongSlash(QinglongCrescentBladeHandle pending,PromptChoice selected)
    {
        if(selected.Parameters.GetValueOrDefault("action")!=KenshangRequestOpen)return false;
        var attack=pending.Attack;
        if(_pendingDecision is not{Kind:DecisionKind.QinglongCrescentBlade,IsPrivate:true}original||!SameContinuationOwner(ActiveQinglongCrescentBlade,pending)||original.PlayerSeat!=attack.SourceSeat||!KenshangQinglongSlashChoices(pending).Any(c=>AssistedChoicesEqual([c],[selected]))||LifecycleCardUse(attack.ResolutionId)is not{KenshangRequest:null}parent||_resolutionStack.LastOrDefault()?.Id!=parent.Id)throw new InvalidOperationException("Kenshang lost its exact native Qinglong Slash-use request.");
        var source=RequireConversionSource(selected);var r=new KenshangRequestReceipt(parent.Id,0,null,attack.SourceSeat,attack.TargetSeat,source,_contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash,original);UpdateLifecycleCardUse(parent.Id,u=>u with{KenshangRequest=r});BeginKenshangRequest(r);return true;
    }
    [ProgramAnswer(KenshangRequestOpen)]private void OpenKenshangProgramRequest(PromptChoice selected)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{KenshangRequest:null}f||_pendingDecision is not{Kind:DecisionKind.ProgramTrigger}original||GetPausedPrivateOfferEffect(f)is not{}effect||selected.Targets is not[var target]||!KenshangProgramSlashChoices(f,original.PlayerSeat,KenshangProgramTargets(f,original.PlayerSeat,effect.Op)).Any(c=>AssistedChoicesEqual([c],[selected]))||!original.Choices.Any(c=>AssistedChoicesEqual([c],[selected])))throw new InvalidOperationException("Kenshang lost its exact program-request actor, instruction or legal published frontier.");
        var source=RequireConversionSource(selected);var r=new KenshangRequestReceipt(f.Id,f.InstructionIndex,effect.Op,original.PlayerSeat,target,source,_contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash,original){RequestSource=new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId),RequestGameplayHash=f.GameplayHash};
        ReplaceRuntimeTop(f with{KenshangRequest=r});BeginKenshangRequest(r);
    }
    private void BeginKenshangRequest(KenshangRequestReceipt r)
    {
        AdvanceEventRulesAndQueueFact(new KenshangRequestStartedEvent(r.OwnerFrameId,r.InstructionIndex,r.RequestOp,r.ActorSeat,r.TargetSeat,r.Source,r.GameplayHash,r.OriginalDecision.PromptId,r.OriginalDecision.Revision,KenshangOriginalDecisionHash(r.OriginalDecision)));PublishKenshangRequest(r);
    }
    private static string KenshangOriginalDecisionHash(PendingDecision decision) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_ma_xiu_ma_tie_ol_KenshangRequests.KenshangOriginalDecisionHash(decision);
    private KenshangRequestReceipt? CurrentKenshangRequest()=>_resolutionStack.LastOrDefault()switch
    {ProgramSkillFrame{KenshangRequest:{}r}=>r,CardUseFrame{KenshangRequest:{}r}=>r,ResponseWindowFrame response when LifecycleCardUse(response.ParentFrameId)is{KenshangRequest:{}r}=>r,_=>null};
    private bool ValidKenshangRequest(KenshangRequestReceipt r)
    {
        if(r.OwnerFrameId<=0||r.Source.OwnerSeat!=r.ActorSeat||r.GameplayHash!=_contentRegistry.GetSkill(r.Source.SkillId).Program?.GameplayHash||ViewAsRule(r.Source)is not{KenshangUse:true}||r.OriginalDecision.PlayerSeat!=r.ActorSeat||r.Selected.Distinct().Count()!=r.Selected.Count||r.Selected.Any(id=>!KenshangRequestCards(r.Source).Any(c=>c.Id==id))||
            ProgramEventHistory<KenshangRequestStartedEvent>().Count(e=>e.OwnerFrameId==r.OwnerFrameId&&e.InstructionIndex==r.InstructionIndex&&e.RequestOp==r.RequestOp&&e.ActorSeat==r.ActorSeat&&e.TargetSeat==r.TargetSeat&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.OriginalPromptId==r.OriginalDecision.PromptId&&e.OriginalRevision==r.OriginalDecision.Revision&&e.OriginalDecisionHash==KenshangOriginalDecisionHash(r.OriginalDecision))!=1||!r.OriginalDecision.ValidTargetSeats.Contains(r.TargetSeat)||!r.OriginalDecision.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")==KenshangRequestOpen&&c.Parameters.GetValueOrDefault("frame-id")==r.OwnerFrameId.ToString(CultureInfo.InvariantCulture)&&c.Parameters.GetValueOrDefault("actor-seat")==r.ActorSeat.ToString(CultureInfo.InvariantCulture)&&c.Targets.SequenceEqual([r.TargetSeat])&&TryReadConversionSource(c.Parameters,out var s)&&s==r.Source))return false;
        var selected=ProgramEventHistory<KenshangRequestMaterialSelectedEvent>().Where(e=>e.OwnerFrameId==r.OwnerFrameId&&e.OriginalPromptId==r.OriginalDecision.PromptId).OrderBy(e=>e.Index).ToArray();
        if(selected.Length!=r.Selected.Count||!selected.Select(e=>e.Index).SequenceEqual(Enumerable.Range(0,selected.Length))||!selected.Select(e=>e.CardId).SequenceEqual(r.Selected)||selected.Any(e=>e.ActorSeat!=r.ActorSeat||e.From!=_cardZones.GetLocation(e.CardId)))return false;
        if(r.RequestOp is{}op)return r.OriginalDecision is{Kind:DecisionKind.ProgramTrigger,IsPrivate:true}&&_resolutionStack.LastOrDefault()is ProgramSkillFrame{KenshangRequest:{}stored}f&&stored==r&&f.Id==r.OwnerFrameId&&f.InstructionIndex==r.InstructionIndex&&f.GameplayHash==r.RequestGameplayHash&&r.OriginalDecision.SourceSeat==f.OwnerSeat&&r.OriginalDecision.TargetSeat==r.ActorSeat&&r.OriginalDecision.SkillPrompt?.SkillId==f.SkillId&&r.RequestSource==new CardConversionSource(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId)&&GetPausedPrivateOfferEffect(f)?.Op==op&&KenshangProgramActor(f,op,r.ActorSeat)&&KenshangProgramTargets(f,r.ActorSeat,op).Contains(r.TargetSeat);
        if(r.OriginalDecision.Kind==DecisionKind.QinglongCrescentBlade)return r.OriginalDecision.IsPrivate&&ActiveQinglongCrescentBlade is{}q&&SameAttackOwner(ActiveCardAttack,q.Attack)&&q.Attack.ResolutionId==r.OwnerFrameId&&q.Attack.SourceSeat==r.ActorSeat&&q.Attack.TargetSeat==r.TargetSeat&&r.OriginalDecision.SourceSeat==r.ActorSeat&&r.OriginalDecision.TargetSeat==r.TargetSeat&&r.OriginalDecision.IncomingCard==q.Attack.EffectiveCardKind&&r.OriginalDecision.SkillPrompt is null&&_resolutionStack.LastOrDefault()is CardUseFrame{KenshangRequest:{}native}u&&u.Id==r.OwnerFrameId&&native==r;
        return r.OriginalDecision is{Kind:DecisionKind.RespondSlash,RequiredCardKind:CardKind.Slash,IncomingCard:CardKind.BorrowedSword}&&ActiveBorrowedSword is{AwaitingSlashChoice:true}borrowed&&borrowed.ResolutionId==r.OwnerFrameId&&borrowed.WeaponOwnerSeat==r.ActorSeat&&borrowed.SlashTargetSeat==r.TargetSeat&&r.OriginalDecision.SourceSeat==borrowed.SourceSeat&&r.OriginalDecision.TargetSeat==r.TargetSeat&&r.OriginalDecision.SkillPrompt is null&&LifecycleCardUse(r.OwnerFrameId)?.KenshangRequest==r&&_resolutionStack.LastOrDefault()is ResponseWindowFrame response&&response.ParentFrameId==r.OwnerFrameId;
    }
    private bool KenshangSelectedRequestLegal(KenshangRequestReceipt r)
    {
        if(r.Selected.Count<2||FindProgramMultiCardViewAsSelection(_players[r.ActorSeat],r.Selected,CardKind.Slash,false,r.Source)is not{}selection)return false;
        var actor=_players[r.ActorSeat];var target=_players[r.TargetSeat];var cards=selection.Cards;var suit=PhysicalGroupSuit(actor,cards);var color=PhysicalGroupColor(actor,cards);
        if(r.RequestOp is null)return r.OriginalDecision.Kind==DecisionKind.QinglongCrescentBlade?CanUseQinglongCrescentBladeTarget(actor,target,CardKind.Slash,color,false,suit,physicalCardIds:r.Selected):IsLegalBorrowedSwordSlashTarget(actor,target,CardKind.Slash,suit,false,color,physicalCardIds:r.Selected);
        var f=GetActiveProgramFrame(r.OwnerFrameId);var ignore=r.RequestOp is SkillProgramEffectOp.UseHejiAtCompletedSingleTarget or SkillProgramEffectOp.RequestSlashByTarget or SkillProgramEffectOp.RequestSlashByNearest||r.RequestOp==SkillProgramEffectOp.UseOwnerSlashAgainstTurnOwner&&f.FixedTargetSlash!.IgnoreDistance;
        if(r.RequestOp==SkillProgramEffectOp.ResolvePingduanOrderedBasicRecastEquipment&&!CanSpendSlashUse(actor,target,IgnoresProgramSlashLimit(actor,r.Source),CardKind.Slash,physicalCardIds:r.Selected))return false;
        return CanUseSlashTarget(actor,target,cards[0],r.Source,CardKind.Slash,ignoreDistance:ignore,noEffectiveRank:true,physicalCardIds:r.Selected)&&!HasBeneficiarySuitShield(actor.Seat,target.Seat,suit)&&!IsCardTargetProhibited(target,CardKind.Slash,suit,color)&&
            (ignore||AssistedSlashPaymentHasRange(actor,target.Seat,cards));
    }
    private IReadOnlyList<PromptChoice> KenshangRequestPickChoices(KenshangRequestReceipt r)
    {
        if(!ValidKenshangRequest(r))throw new InvalidOperationException("Kenshang private material selection lost its exact unchanged request.");var choices=new List<PromptChoice>();
        foreach(var c in KenshangRequestCards(r.Source).Where(c=>!r.Selected.Contains(c.Id)))choices.Add(new(new($"kenshang.{r.OwnerFrameId}.card.{c.Id}"),"选择【"+c.DisplayName+"】",[c.Id],[],new Dictionary<string,string>{{"program-action",KenshangRequestPick},{"branch","card"}}));
        if(KenshangSelectedRequestLegal(r))choices.Add(new(new($"kenshang.{r.OwnerFrameId}.finish"),"将所选的至少两张牌当【杀】使用",[],[r.TargetSeat],new Dictionary<string,string>{{"program-action",KenshangRequestPick},{"branch","finish"}}));
        choices.Add(new(new($"kenshang.{r.OwnerFrameId}.cancel"),"返回原用牌请求",[],[],new Dictionary<string,string>{{"program-action",KenshangRequestPick},{"branch","cancel"}}));return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishKenshangRequest(KenshangRequestReceipt r)
    {
        var choices=KenshangRequestPickChoices(r);var skill=_contentRegistry.GetSkill(r.Source.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,r.ActorSeat,"垦伤：私下选择至少两张手牌或装备牌，然后确认使用；不会提前移动或公开未支付的牌。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),[r.TargetSeat],r.ActorSeat){PromptId=CreatePromptId(),IsPrivate=true,Choices=choices,SkillPrompt=new(r.Source.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[r.ActorSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    private void StoreKenshangRequest(KenshangRequestReceipt? r,long id)
    {if(_resolutionStack.LastOrDefault()is ProgramSkillFrame f&&f.Id==id)ReplaceRuntimeTop(f with{KenshangRequest=r});else UpdateLifecycleCardUse(id,u=>u with{KenshangRequest=r});}
    [ProgramAnswer(KenshangRequestPick)]private void AnswerKenshangRequest(PromptChoice selected)
    {
        if(CurrentKenshangRequest()is not{}r||_pendingDecision is not{Kind:DecisionKind.ProgramTrigger,IsPrivate:true}p||p.PlayerSeat!=r.ActorSeat||!KenshangRequestPickChoices(r).Any(c=>AssistedChoicesEqual([c],[selected])))throw new InvalidOperationException("Kenshang private selection requires its real published actor and exact card identity.");
        var branch=selected.Parameters["branch"];
        if(branch=="card"){var id=selected.Cards.Single();AdvanceEventRulesAndQueueFact(new KenshangRequestMaterialSelectedEvent(r.OwnerFrameId,r.OriginalDecision.PromptId,r.ActorSeat,r.Selected.Count,id,_cardZones.GetLocation(id)));r=r with{Selected=r.Selected.Append(id).ToArray()};StoreKenshangRequest(r,r.OwnerFrameId);PublishKenshangRequest(r);return;}
        if(branch=="finish"&&!KenshangSelectedRequestLegal(r))throw new InvalidOperationException("Kenshang's final original target or complete physical payment became illegal.");
        StoreKenshangRequest(null,r.OwnerFrameId);_pendingDecision=branch=="cancel"?r.OriginalDecision with{PromptId=CreatePromptId()}:r.OriginalDecision;_status=_players[r.ActorSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;
        AdvanceEventRulesAndQueueFact(new KenshangRequestFinishedEvent(r.OwnerFrameId,r.ActorSeat,branch=="finish",branch=="finish"?r.Selected.Count:0));
        if(branch=="cancel"){AdvanceRulesAndPublishState();return;}
        var selection=FindProgramMultiCardViewAsSelection(_players[r.ActorSeat],r.Selected,CardKind.Slash,false,r.Source)??throw new InvalidOperationException("Kenshang lost its exact selected native payment.");
        if(r.RequestOp is null){if(r.OriginalDecision.Kind==DecisionKind.QinglongCrescentBlade)BeginQinglongCrescentBladeFollowup(ActiveQinglongCrescentBlade!,selection.Cards[0],CardKind.Slash,r.ActorSeat,selection.Cards,conversionSource:r.Source);else ResolveBorrowedSwordProgramMultiCardSlashChoice(ActiveBorrowedSword!,selection);return;}
        var f=GetActiveProgramFrame(r.OwnerFrameId);var effect=GetPausedPrivateOfferEffect(f)!;
        if(r.RequestOp==SkillProgramEffectOp.ResolvePingduanOrderedBasicRecastEquipment)
        {
            var action=new LegalAction(LegalActionKind.Slash,selection.Cards[0].Id,r.TargetSeat,"垦伤：使用【杀】",CardKind.Slash,TargetSeats:[r.TargetSeat],MinCardCount:r.Selected.Count,MaxCardCount:r.Selected.Count){ConversionSource=r.Source,SelectableCardIds=r.Selected};
            AdvanceEventRulesAndQueueFact(new KenshangKeActionAcceptedEvent(f.Id,f.InstructionIndex,r.RequestSource!,r.RequestGameplayHash!,r.ActorSeat,r.TargetSeat,r.Source,r.GameplayHash,r.OriginalDecision.PromptId,r.Selected.Count));
            var k=f.KeBiNengPingduan!;ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect,KeBiNengPingduan=k with{BasicActions=k.BasicActions.Append(action).ToArray(),SelectedBasicAction=action,Stage=KeBiNengPingduanStage.BasicIssuing}});
            BeginKeBiNengPingduanBasicUse(f,action);return;
        }
        ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});
        if(r.RequestOp==SkillProgramEffectOp.RequestSlashByTarget)CommitProgramChoiceResult(f.Id,effect.ResultBind!,RequestSlashByTargetProgramOperationDescriptor.UsedSlashOption,r.ActorSeat,"垦伤：使用【杀】。");
        else if(r.RequestOp==SkillProgramEffectOp.RequestSlashAgainstChosenTarget){ReplaceRuntimeTop(f=f with{AssistedSlashRequest=null});CommitProgramChoiceResult(f.Id,effect.ResultBind!,"used-slash",r.ActorSeat,"垦伤：使用【杀】。");}
        else if(r.RequestOp==SkillProgramEffectOp.UseOwnerSlashAgainstTurnOwner)ReplaceRuntimeTop(f=f with{FixedTargetSlash=f.FixedTargetSlash! with{Issued=true}});
        else if(r.RequestOp==SkillProgramEffectOp.RequestLegalSlashByNearest)ReplaceRuntimeTop(f=f with{NearestLegalSlashRequest=null});
        if(r.RequestOp==SkillProgramEffectOp.UseHejiAtCompletedSingleTarget){PrepareWuJingKenshangNative(f,selection);f=GetActiveProgramFrame(f.Id);}
        if(IsSayingSlashRequest(f)){PrepareSayingSuppliedSlash(f,r.ActorSeat,r.ActorSeat,r.TargetSeat,CardKind.Slash,selection.Cards,r.Source);f=GetActiveProgramFrame(f.Id);}
        if(IsChanshuangSlashRequest(f)){PrepareChanshuangSuppliedSlash(f,r.ActorSeat,r.ActorSeat,r.TargetSeat,CardKind.Slash,selection.Cards,r.Source);f=GetActiveProgramFrame(f.Id);}
        ResolveSlashCore(_players[r.ActorSeat],_players[r.TargetSeat],selection.Cards[0],CardKind.Slash,r.ActorSeat,physicalCards:selection.Cards,countsTowardSlashLimit:false,conversionSource:r.Source,programSkillCardUseFrameId:f.Id);
    }
    private bool HasKenshangRequestPrompt()=>CurrentKenshangRequest()is{}r&&_pendingDecision is{Kind:DecisionKind.ProgramTrigger,IsPrivate:true}p&&p.PlayerSeat==r.ActorSeat&&p.SkillPrompt?.SkillId==r.Source.SkillId&&p.Choices.All(c=>c.Parameters.GetValueOrDefault("program-action")==KenshangRequestPick)&&ValidKenshangRequest(r);
    private PendingDecision? KenshangNativeOriginalDecision(long ownerId)=>HasKenshangRequestPrompt()&&CurrentKenshangRequest()is{RequestOp:null}r&&r.OwnerFrameId==ownerId?r.OriginalDecision:null;
    private PendingDecision KenshangProgramNativeDecision(PendingDecision p)=>ZonghuNativeDecision(HasKenshangRequestPrompt()&&CurrentKenshangRequest()is{RequestOp:not null}r&&p.PlayerSeat==r.ActorSeat?r.OriginalDecision:p);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,27111)]private void AssertKenshangRequestState(ProgramSkillFrame f)
    {if(f.KenshangRequest is{}r&&(!ValidKenshangRequest(r)||!HasKenshangRequestPrompt()||r.OwnerFrameId!=f.Id))throw new InvalidOperationException("Kenshang program request lost its owning instruction, original private frontier or ordered private selections.");}
    private void AssertKenshangRequests()
    {
        foreach(var f in _resolutionStack.OfType<ProgramSkillFrame>())AssertKenshangRequestState(f);
        foreach(var f in _resolutionStack.OfType<CardUseFrame>())
        {
            if(f.KenshangRequest is{}r&&(!ValidKenshangRequest(r)||!HasKenshangRequestPrompt()||r.OwnerFrameId!=f.Id||r.RequestOp is not null))throw new InvalidOperationException("Kenshang native request lost its exact Borrowed Sword/Qinglong owner, original frontier or selected entity invoices.");
            if(f.Kenshang is not null&&!KenshangUseProof(f))throw new InvalidOperationException("Kenshang native use lost its original material count, conversion source or final actor chain.");
        }
    }
    private bool HasAcceptedKenshangKeAction(ProgramSkillFrame f,LegalAction action)
    {
        if(f.KeBiNengPingduan is not{}r||action is not{Kind:LegalActionKind.Slash,PlayedCardKind:CardKind.Slash,ConversionSource:{}source}||ViewAsRule(source)?.KenshangUse!=true||source.OwnerSeat!=r.ActorSeat||action.TargetSeats is not[var target]||action.MinCardCount<2||action.MaxCardCount!=action.MinCardCount||action.SelectableCardIds.Count!=action.MinCardCount||action.SelectableCardIds.Distinct().Count()!=action.MinCardCount||action.CardId!=action.SelectableCardIds[0]||
            ProgramEventHistory<KenshangKeActionAcceptedEvent>().Where(e=>e.ProgramFrameId==f.Id).ToArray()is not[var accepted]||accepted.InstructionIndex!=f.InstructionIndex||accepted.RequestSource!=r.Source||accepted.RequestGameplayHash!=f.GameplayHash||accepted.ActorSeat!=r.ActorSeat||accepted.TargetSeat!=target||accepted.Source!=source||accepted.GameplayHash!=_contentRegistry.GetSkill(source.SkillId).Program?.GameplayHash||accepted.MaterialCount!=action.MinCardCount||
            ProgramEventHistory<KenshangRequestStartedEvent>().Count(e=>e.OwnerFrameId==f.Id&&e.RequestOp==SkillProgramEffectOp.ResolvePingduanOrderedBasicRecastEquipment&&e.ActorSeat==r.ActorSeat&&e.TargetSeat==target&&e.Source==source&&e.OriginalPromptId==accepted.OriginalPromptId)!=1||ProgramEventHistory<KenshangRequestFinishedEvent>().Count(e=>e.OwnerFrameId==f.Id&&e.ActorSeat==r.ActorSeat&&e.Accepted&&e.MaterialCount==accepted.MaterialCount)!=1)return false;
        var materials=ProgramEventHistory<KenshangRequestMaterialSelectedEvent>().Where(e=>e.OwnerFrameId==f.Id&&e.OriginalPromptId==accepted.OriginalPromptId).OrderBy(e=>e.Index).ToArray();
        return materials.Length==accepted.MaterialCount&&materials.Select(e=>e.Index).SequenceEqual(Enumerable.Range(0,materials.Length))&&materials.Select(e=>e.CardId).SequenceEqual(action.SelectableCardIds)&&materials.All(e=>e.ActorSeat==r.ActorSeat&&e.From.OwnerSeat==r.ActorSeat&&e.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment);
    }
    private bool TryChooseKenshangAi(PendingDecision p,out PromptChoice choice)
    {
        choice=null!;if(!HasKenshangRequestPrompt()||CurrentKenshangRequest()is not{}r||p.PlayerSeat!=r.ActorSeat)return false;
        choice=p.Choices.FirstOrDefault(c=>c.Parameters["branch"]=="finish")??p.Choices.Where(c=>c.Cards.Count==1).OrderBy(c=>GetKeepValue(GetAdvancedCard(c.Cards[0]),_players[r.ActorSeat])).ThenBy(c=>c.Cards[0]).FirstOrDefault()??p.Choices.Single(c=>c.Parameters["branch"]=="cancel");return true;
    }
}
