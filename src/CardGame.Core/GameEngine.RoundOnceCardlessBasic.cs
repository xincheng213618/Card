namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool RoundOnceCardlessBasicUsed(int ownerSeat,string skillId,string group)=>CompleteProgramEventHistory().OfType<RoundOnceCardlessBasicQuotaPaidEvent>().Any(e=>e.Receipt.Source.OwnerSeat==ownerSeat&&e.Receipt.Source.SkillId==skillId&&e.Receipt.UsageGroup==group&&e.Receipt.RoundNumber==_roundNumber);
 private bool CanUseRoundOnceCardlessBasic(CharacterState actor,IndexedSkillProgramInstance instance,SkillProgramViewAs rule,bool forResponse)
 {
  if(rule.RoundOnceCardlessBasic is not {} p)return true;
  return _winner==Winner.None&&actor.IsAlive&&_roundNumber>=1&&rule.InputCount==0&&(rule.OutputKind is CardKind.Slash or CardKind.Dodge)&&
   (forResponse?rule.ForResponse:rule.ForPlay&&rule.OutputKind==CardKind.Slash)&&instance.SkillInstanceId is not null&&HasRuntimeSkillInstance(actor,instance.SkillId,instance.SkillInstanceId)&&!RoundOnceCardlessBasicUsed(actor.Seat,instance.SkillId,p.UsageGroup)&&!IsCardUseForbidden(actor.Seat,rule.OutputKind,forResponse?CardActionType.Response:CardActionType.Use)&&IsMouGuquCardlessAllowed(actor,rule.OutputKind,forResponse);
 }
 private RoundOnceCardlessBasicPaymentReceipt ConsumeRoundOnceCardlessBasic(CardConversionSource source,bool forResponse)
 {
  var actor=_players[source.OwnerSeat];var rule=ViewAsRule(source)??throw new InvalidOperationException("Cardless basic payment lost its current rule.");var p=rule.RoundOnceCardlessBasic??throw new InvalidOperationException("Cardless basic payment lost its strict policy.");
  var instance=GetSkillBindingShard(actor).ProgramInstances.SingleOrDefault(i=>i.SkillId==source.SkillId&&i.SkillInstanceId==source.SkillInstanceId);
  if(instance is null||!CanUseRoundOnceCardlessBasic(actor,instance,rule,forResponse))throw new InvalidOperationException("Cardless basic cannot consume an invalid source or already spent shared round allowance.");
  var receipt=new RoundOnceCardlessBasicPaymentReceipt(source,instance.Program.GameplayHash,p.UsageGroup,_roundNumber,_turnNumber,_turnProgression.OwnerSeat,_cardUseDebitPhaseInstanceId);
  AdvanceEventRulesAndQueueFact(new RoundOnceCardlessBasicQuotaPaidEvent(receipt));return receipt;
 }
 private RoundOnceCardlessBasicUseReceipt IssueRoundOnceCardlessBasicUse(ResolutionFrame owner,CardActionContext action,CardConversionSource source)
 {
  var rule=ViewAsRule(source)??throw new InvalidOperationException("Issued cardless basic lost its catalog source.");var policy=rule.RoundOnceCardlessBasic??throw new InvalidOperationException("Issued cardless basic lost its policy.");
  var payment=CompleteProgramEventHistory().OfType<RoundOnceCardlessBasicQuotaPaidEvent>().Select(e=>e.Receipt).SingleOrDefault(p=>p.Source==source&&p.RoundNumber==_roundNumber&&p.UsageGroup==policy.UsageGroup)??throw new InvalidOperationException("Issued cardless basic has no accepted quota payment.");
  if(action.ActionId<=0||action.ActorSeat!=source.OwnerSeat||action.ProviderSeat!=source.OwnerSeat||action.RequesterSeat is not null||action.PhysicalCards.Count!=0||action.ConversionChain is not [var exact]||exact!=source||action.EffectiveKind!=rule.OutputKind||action.Type is not (CardActionType.Use or CardActionType.Response)||action.Type==CardActionType.Response&&action.ResponderSeat!=source.OwnerSeat||action.Type==CardActionType.Use&&!rule.ForPlay||action.Type==CardActionType.Response&&!rule.ForResponse||payment.GameplayHash!=_contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash)throw new InvalidOperationException("Issued cardless basic changed its actual actor/provider/zero-material/output/native action tuple.");
  if(CompleteProgramEventHistory().OfType<RoundOnceCardlessBasicUseIssuedEvent>().Any(e=>e.Receipt.Payment==payment))throw new InvalidOperationException("One cardless payment cannot issue a second native action.");
  var receipt=new RoundOnceCardlessBasicUseReceipt(payment,owner.Id,action.ActionId,action.ActorSeat,action.ProviderSeat,action.RequesterSeat,action.ResponderSeat,action.OpponentSeat,action.EffectiveKind,action.Type,action.ParentActionId);
  AdvanceEventRulesAndQueueFact(new RoundOnceCardlessBasicUseIssuedEvent(receipt));return receipt;
 }
 private bool IsIssuedRoundOnceCardlessBasicUse(RoundOnceCardlessBasicUseReceipt? receipt,ResolutionFrame owner,CardActionContext action)
 {
  if(receipt is not {} r||r.OwnerFrameId!=owner.Id||r.CardActionId!=action.ActionId||r.ActorSeat!=action.ActorSeat||r.ProviderSeat!=action.ProviderSeat||r.RequesterSeat!=action.RequesterSeat||r.ResponderSeat!=action.ResponderSeat||r.OpponentSeat!=action.OpponentSeat||r.ParentActionId!=action.ParentActionId||r.ActionType!=action.Type||r.EffectiveKind!=action.EffectiveKind||action.PhysicalCards.Count!=0||action.ConversionChain is not [var source]||source!=r.Payment.Source||ViewAsRule(source) is not {RoundOnceCardlessBasic:{}} rule||rule.OutputKind!=r.EffectiveKind||_contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash!=r.Payment.GameplayHash)return false;
  return CompleteProgramEventHistory().OfType<RoundOnceCardlessBasicQuotaPaidEvent>().Count(e=>e.Receipt==r.Payment)==1&&CompleteProgramEventHistory().OfType<RoundOnceCardlessBasicUseIssuedEvent>().Count(e=>e.Receipt==r)==1;
 }
}
