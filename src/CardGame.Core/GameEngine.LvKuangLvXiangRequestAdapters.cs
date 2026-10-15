namespace CardGame.Core;
public sealed partial class GameEngine
{
 private const string QigongFactionBind="qigong-native-faction";
 private bool IsQigongRequest(ProgramSkillFrame f)=>f.LvKuangLvXiang is{Stage:"slash",OriginalUseId:not null,Actor:>=0}r&&r.Actor!=r.Target&&r.Source==LvKuangLvXiangSource(f)&&r.GameplayHash==f.GameplayHash&&r.Turn==_turnNumber;
 private bool QigongRequestParticipantsLive(ProgramSkillFrame f)=>f.LvKuangLvXiang is{Actor:>=0}r&&_players[r.Actor].IsAlive&&_players[r.Target].IsAlive&&_winner==Winner.None;
 private bool QigongAdapterActor(ProgramSkillFrame f,int actor,int?target,int cursor,long?parent,long?request)=>f.LvKuangLvXiang is{}r&&r.Stage is "slash" or "issuing" or "native"&&actor==r.Actor&&target==r.Target&&cursor==f.InstructionIndex&&parent==f.Id&&request==f.Id&&r.Source==LvKuangLvXiangSource(f)&&r.GameplayHash==f.GameplayHash&&r.Turn==_turnNumber;
 private void PrepareQigongDrawFundedSlash(ProgramSkillFrame f,DrawFundedDistinctBasicPayment p)
 {if(!IsQigongRequest(f)||!QigongRequestParticipantsLive(f)||!ValidDrawFundedDistinctBasicPayment(p)||p.Intent!=DrawFundedDistinctBasicIntent.AssistedSlash||!QigongAdapterActor(f,p.Source.OwnerSeat,p.TargetSeat,p.Cursor,p.ParentFrameId,p.RequestFrameId))throw new InvalidOperationException("Qigong draw-funded cost lost its original actor, target and native request.");ReplaceRuntimeFrame(f.Id,f with{LvKuangLvXiang=f.LvKuangLvXiang!with{Stage="issuing",IssuingIds=[],IssuingKind=p.EffectiveKind,DrawFundedPayment=p}});}
 private void PrepareQigongChainedSlash(ProgramSkillFrame f,ChainedStateBasicPayment p)
 {if(!IsQigongRequest(f)||!QigongRequestParticipantsLive(f)||!ValidChainedStateBasicPayment(p)||p.Intent!=ChainedStateBasicIntent.AssistedSlash||!QigongAdapterActor(f,p.Source.OwnerSeat,p.TargetSeat,p.Cursor,p.ParentFrameId,p.RequestFrameId))throw new InvalidOperationException("Qigong chained cost lost its original actor, target and native request.");ReplaceRuntimeFrame(f.Id,f with{LvKuangLvXiang=f.LvKuangLvXiang!with{Stage="issuing",IssuingIds=[],IssuingKind=p.EffectiveKind,ChainedPayment=p}});}
 private void DispatchQigongRequestedDeckSlash(ProgramSkillFrame f,Card card,IReadOnlyList<int>targets)
 {
  if(!IsQigongRequest(f)||!QigongRequestParticipantsLive(f)||f.RequestedDeckBasicMaterial is not{Claiming:true,SelectedCardId:{}id}material||id!=card.Id||material.ActorSeat!=f.LvKuangLvXiang!.Actor||material.Intent!=RequestedDeckBasicIntent.AssistedSlash||!HasValidRequestedDeckBasicMaterial(f)||!targets.SequenceEqual([f.LvKuangLvXiang.Target])||!CanUseSlashTarget(_players[material.ActorSeat],_players[targets.Single()],card,effectiveKind:card.Kind,ignoreDistance:true,physicalCardIds:[card.Id]))throw new InvalidOperationException("Qigong deck request lost its exact selected original physical entity.");
  var r=f.LvKuangLvXiang;ReplaceRuntimeTop(f=f with{LvKuangLvXiang=r with{Stage="issuing",IssuingIds=[card.Id],IssuingKind=card.Kind}});ClearPendingDecision();ResolveSlashCore(_players[r.Actor],_players[r.Target],card,card.Kind,r.Actor,countsTowardSlashLimit:false,programSkillCardUseFrameId:f.Id);
 }
 private void AddQigongNativeAdapterChoices(ProgramSkillFrame f,List<PromptChoice>choices)
 {
  var r=f.LvKuangLvXiang!;var actor=_players[r.Actor];if(GetFactionResponsePolicy(actor,CardKind.Slash)is{}policy&&ActiveFactionCardRequest is null&&GetFactionProviderSeats(actor.Seat,policy.FactionId).Count>0&&CanUseSlashTarget(actor,_players[r.Target],new(0,CardKind.Slash,Suit.None,0),effectiveKind:CardKind.Slash,ignoreDistance:true,noEffectiveRank:true,physicalCardIds:[]))choices.Add(LvChoice(f,"faction","faction","请求势力角色提供杀",targets:[r.Target]));
  foreach(var card in GetHand(actor))foreach(var kind in SlashKinds)foreach(var source in GetProgramViewAsConversions(actor,card,kind,false).Where(s=>ViewAsRule(s)?.DeclarationValidation is not null))
  {if(!CanUseSlashTarget(actor,_players[r.Target],card,source,kind,ignoreDistance:true,physicalCardIds:[card.Id])||IsTurnPhysicalUseForbidden(actor.Seat,[card.Id]))continue;var args=new Dictionary<string,string>{{"effective-kind",kind.ToString()}};AddConversionParameters(args,source);choices.Add(LvChoice(f,"declaration",$"declare-{card.Id}-{source.SkillInstanceId}-{source.BindingId}","以真实手牌声明杀",[card.Id],[r.Target],args));}
 }
 private bool TryBeginQigongNativeAdapter(ProgramSkillFrame f,PromptChoice choice)
 {
  if(choice.Parameters["branch"]=="declaration"){BeginQigongDeclaration(f,choice);return true;}if(choice.Parameters["branch"]!="faction")return false;
  if(!IsQigongRequest(f)||!QigongRequestParticipantsLive(f)||!LvKuangLvXiangCurrent(f)||ActiveFactionCardRequest is not null)throw new InvalidOperationException("Qigong faction need is no longer current.");var r=f.LvKuangLvXiang!;var policy=GetFactionResponsePolicy(_players[r.Actor],CardKind.Slash)!;ReplaceRuntimeTop(f=f with{LvKuangLvXiang=r with{Faction=new(f.Id,r.Actor,r.Target,policy)}});var seats=GetFactionProviderSeats(r.Actor,policy.FactionId);ActiveFactionCardRequest=new FactionCardRequestHandle(this,f.Id,FactionCardRequestPurpose.AssistedProgramUse,r.Actor,seats,policy.FactionId,targetSeat:r.Target,programSkillFrameId:f.Id,policySource:policy,assistedResultBind:QigongFactionBind);_status=EngineStatus.Running;AdvanceEventRulesAndQueueFact(new FactionSlashRequestedEvent(f.Id,r.Actor,seats,policy.SkillId,IsActiveUse:true,r.Target));AdvanceFactionSlashCandidate();return true;
 }
 private bool ValidateQigongFactionSlashParent(FactionCardRequestHandle pending,ProgramSkillFrame f)
 {
  if(f.LvKuangLvXiang?.Faction is not{}r)return false;
  if(r.FrameId!=f.Id||r.Actor!=f.LvKuangLvXiang.Actor||r.Target!=f.LvKuangLvXiang.Target||pending.ProgramSkillFrameId!=f.Id||pending.ResolutionId!=f.Id||pending.OwnerSeat!=r.Actor||pending.TargetSeat!=r.Target||pending.PolicySource!=r.Policy||pending.AssistedResultBind!=QigongFactionBind||!pending.IsAssistedProgramUse||f.LvKuangLvXiang.Stage is not("slash" or "issuing" or "native")||CompleteProgramEventHistory().OfType<FactionSlashRequestedEvent>().Count(e=>e.ResolutionId==f.Id&&e.OwnerSeat==r.Actor&&e.TargetSeat==r.Target&&e.SkillId==r.Policy.SkillId)!=1)throw new InvalidOperationException("Qigong faction provider lost the original exact actor/policy/request.");return true;
 }
 private bool PrepareQigongFactionNative(ProgramSkillFrame f,FactionCardRequestHandle pending,CharacterState provider,IReadOnlyList<Card>cards,CardKind kind)
 {
  if(!ValidateQigongFactionSlashParent(pending,f))return false;if(!IsQigongRequest(f)||!QigongRequestParticipantsLive(f)||cards.Count==0||!pending.CandidateSeats.Contains(provider.Seat)||cards.Any(c=>_cardZones.GetLocation(c.Id).OwnerSeat!=provider.Seat))throw new InvalidOperationException("Qigong faction native cost needs the actual provider's physical Slash.");ReplaceRuntimeTop(f with{LvKuangLvXiang=f.LvKuangLvXiang!with{Stage="issuing",IssuingIds=cards.Select(c=>c.Id).ToArray(),IssuingKind=kind,IssuingProvider=provider.Seat}});return true;
 }
 private bool FinishQigongFactionDeclined(long id,FactionCardRequestHandle pending)
 {if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{LvKuangLvXiang:{Faction:not null}}f||f.Id!=id)return false;ValidateQigongFactionSlashParent(pending,f);FinishLvKuangLvXiang(f);return true;}
}
