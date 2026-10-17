namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool IsZonghuProgramRequestAttackConsistent(CardAttackHandle attack,IReadOnlyList<Card> processing)
 {
  if(attack.Card is not null||attack.PhysicalCards.Count!=0||LifecycleCardUse(attack.ResolutionId)is not{ZonghuOrigin:{}p}use||!IsIssuedZonghuUse(use)||!IsSlashCard(use.CardKind)||attack.EffectiveCardKind!=use.CardKind||attack.SourceSeat!=use.SourceSeat||p.Need.OwnerFrameId!=p.Need.RequestFrameId)return false;
  var parentIndex=_resolutionStack.FindIndex(frame=>frame.Id==p.Need.OwnerFrameId);
  var useIndex=_resolutionStack.FindIndex(frame=>frame.Id==use.Id);
  if(parentIndex<0||useIndex!=parentIndex+1||_resolutionStack[parentIndex]is not ProgramSkillFrame{ZonghuReturn:{}r}parent||r.Origin!=p||r.UseId!=use.Id||r.ActionId!=use.Action!.ActionId||parent.InstructionIndex!=p.Need.InstructionIndex||GetPausedPrivateOfferEffect(parent)?.Op!=p.Need.RequestOp||attack.ProgramSkillCardUseFrameId!=parent.Id||r.Returned)return false;
  if((parent.SunYuan is not null||parent.Chanshuang is not null||parent.WuJing is not null||parent.LvKuangLvXiang is not null||parent.CaoChun is not null)&&!HasZonghuSpecializedPayment(parent,use))return false;
  var outerCards=_resolutionStack.Take(parentIndex).OfType<CardUseFrame>().SelectMany(frame=>frame.PhysicalCardIds??[]).Where(id=>_cardZones.GetLocation(id)==CardLocation.Processing).Distinct().Order().ToArray();
  return processing.Select(card=>card.Id).Order().SequenceEqual(outerCards);
 }
 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,27513)]
 private void AssertZonghuCaller(ProgramSkillFrame f)
 {
  if(f.KeBiNengPingduan is{SelectedBasicAction:{}ks}&&ks.ConversionSource?.SkillId=="ol:zonghu"&&!HasAcceptedZonghuKeAction(f,ks))throw new InvalidOperationException("Zonghu Pingduan changed its actual issued private basic action.");
  if(f.ZonghuReturn is not{}r)return;
  if(!ValidZonghuPayment(r.Origin)||r.Origin.Need.OwnerFrameId!=f.Id||r.Origin.Need.InstructionIndex!=f.InstructionIndex||r.UseId<0||r.ActionId<0||r.UseId==0&&r.ActionId!=0||r.UseId>0&&ProgramEventHistory<ZonghuUseIssuedEvent>().Count(e=>e.AttemptId==r.Origin.AttemptId&&e.UseId==r.UseId&&e.ActionId==r.ActionId)!=1||r.Returned&&ProgramEventHistory<ZonghuUseReturnedEvent>().Count(e=>e.AttemptId==r.Origin.AttemptId&&e.UseId==r.UseId&&e.ActionId==r.ActionId)!=1)throw new InvalidOperationException("Zonghu caller lost its genuine paid request, original cursor or once-returned child.");
 }
 private bool HasZonghuSpecializedPayment(ProgramSkillFrame f,CardUseFrame? use=null)
 {
  if(f.ZonghuReturn is not{}r||r.Origin is not{}p||!ValidZonghuPayment(p)||p.Need.OwnerFrameId!=f.Id||p.Need.RequestFrameId!=f.Id||p.Need.InstructionIndex!=f.InstructionIndex||!IsSlashCard(p.OutputKind)||ProgramEventHistory<ZonghuAttemptResolvedEvent>().Count(e=>e.FrameId==p.AttemptId&&e.Matched&&e.NativeLegal)!=1)return false;
  if(use is not null&&(use.ZonghuOrigin!=p||r.UseId!=use.Id||r.ActionId!=use.Action?.ActionId||!IsIssuedZonghuUse(use)))return false;
  if(f.SunYuan is not null)return HasSayingZonghuPayment(f,use);
  if(f.Chanshuang is{}cs)return TryGetChanshuangSlashRequest(f,out var actor,out var targets)&&actor==p.Need.ActorSeat&&targets.SequenceEqual([p.TargetSeat])&&cs.Stage is ChanshuangStage.SlashIssuing or ChanshuangStage.NativeUse or ChanshuangStage.NativeChildren&&
   cs.Selection is{Materials.Count:0,Sources:[var csSource]}selection&&csSource==p.Source&&selection.ActorSeat==p.Need.ActorSeat&&selection.ProviderSeat==p.Need.ProviderSeat&&selection.TargetSeat==p.TargetSeat&&selection.Kind==p.OutputKind&&
   (cs.Stage!=ChanshuangStage.NativeChildren||r.Returned&&cs.NativeReturned&&cs.Native is{}csNative&&csNative.UseId==r.UseId&&csNative.ActionId==r.ActionId&&csNative.Selection==selection&&LifecycleCardUse(r.UseId)is null&&ProgramEventHistory<ZonghuUseReturnedEvent>().Count(e=>e.AttemptId==p.AttemptId&&e.UseId==r.UseId&&e.ActionId==r.ActionId)==1&&ProgramEventHistory<ChanshuangSlashReturnedEvent>().Count(e=>e.FrameId==f.Id&&e.Cursor==cs.Cursor&&e.UseId==r.UseId&&e.ActionId==r.ActionId)==1);
  if(f.WuJing is{}wu)return wu.Source==WuJingSource(f)&&wu.GameplayHash==f.GameplayHash&&wu.Stage is WuJingStage.IssuingUse or WuJingStage.NativeUse or WuJingStage.NativeChildren&&wu.TargetSeat==p.TargetSeat&&f.OwnerSeat==p.Need.ActorSeat&&wu.Selection is{CardIds.Count:0,Conversions:[var source]}&&source==p.Source&&wu.Selection.Kind==p.OutputKind&&(wu.Stage!=WuJingStage.NativeChildren||r.Returned&&wu.NativeCompleted&&wu.NativeOrigin is{}native&&native.UseId==r.UseId&&native.ActionId==r.ActionId&&LifecycleCardUse(r.UseId)is null&&ProgramEventHistory<ZonghuUseReturnedEvent>().Count(e=>e.AttemptId==p.AttemptId&&e.UseId==r.UseId&&e.ActionId==r.ActionId)==1&&ProgramEventHistory<WuJingHejiReturnedEvent>().Count(e=>e.ProgramFrameId==f.Id&&e.UseId==r.UseId)==1);
  if(f.LvKuangLvXiang is{}qi)return qi.Source==LvKuangLvXiangSource(f)&&qi.GameplayHash==f.GameplayHash&&qi.Stage is"issuing"or"native"&&qi.Actor==p.Need.ActorSeat&&qi.Target==p.TargetSeat&&qi.IssuingProvider==p.Need.ProviderSeat&&qi.IssuingIds.Count==0&&qi.IssuingKind==p.OutputKind;
  return f.CaoChun is{Stage:CaoChunStage.NativeSlash,Slash:{}n}cc&&cc.Source==CaoChunSource(f)&&cc.GameplayHash==f.GameplayHash&&n.ParentFrameId==f.Id&&!n.Ending&&n.Materials.Count==0&&n.Kind==p.OutputKind&&n.TargetSeat==p.TargetSeat&&f.OwnerSeat==p.Need.ActorSeat&&n.ProviderSeat==p.Need.ProviderSeat;
 }
 private bool HasAcceptedZonghuKeAction(ProgramSkillFrame f,LegalAction a)
 {
  if(f.KeBiNengPingduan is not{}r||f.ZonghuKeAcceptedOrigin is not{}p||!ValidZonghuPayment(p)||p.Need.Intent!=ZonghuIntent.GenericBasic||p.Need.OwnerFrameId!=f.Id||p.Need.InstructionIndex!=f.InstructionIndex||p.Need.ActorSeat!=r.ActorSeat||p.Need.ProviderSeat!=r.ActorSeat||a.CardId!=0||a.SelectableCardIds.Count!=0||a.ConversionSource!=p.Source||a.PlayedCardKind!=p.OutputKind||a.Kind!=(IsSlashCard(p.OutputKind)?LegalActionKind.Slash:p.OutputKind==CardKind.Peach?LegalActionKind.Peach:LegalActionKind.Alcohol)||!a.TargetSeats.SequenceEqual(p.OutputKind==CardKind.Alcohol?[]:new[]{p.TargetSeat}))return false;
  return ProgramEventHistory<ZonghuKeActionAcceptedEvent>().Count(e=>e==new ZonghuKeActionAcceptedEvent(f.Id,f.InstructionIndex,r.Source,f.GameplayHash,p.AttemptId,p.OutputKind,p.TargetSeat))==1&&ProgramEventHistory<ZonghuAttemptResolvedEvent>().Count(e=>e.FrameId==p.AttemptId&&e.Matched&&e.NativeLegal)==1;
 }
    private bool HasSayingZonghuPayment(ProgramSkillFrame f,CardUseFrame? use=null)
    {
        if(f.SunYuan is not{}s||f.ZonghuReturn is not{}r||!ValidZonghuPayment(r.Origin)||!TryGetSayingSlashRequest(f,out var actor,out var targets)||r.Origin.Need.ActorSeat!=actor||!targets.SequenceEqual([r.Origin.TargetSeat])||r.Origin.Need.OwnerFrameId!=f.Id||r.Origin.Need.RequestFrameId!=f.Id||r.Origin.Need.InstructionIndex!=f.InstructionIndex||s.Stage is not(SunYuanStage.SlashIssuing or SunYuanStage.NativeUse or SunYuanStage.NativeChildren)||s.Selection is not{Materials.Count:0,Sources:[var source]}selection||source!=r.Origin.Source||selection.ActorSeat!=actor||selection.ProviderSeat!=r.Origin.Need.ProviderSeat||selection.Kind!=r.Origin.OutputKind||ProgramEventHistory<ZonghuAttemptResolvedEvent>().Count(e=>e.FrameId==r.Origin.AttemptId&&e.Matched&&e.NativeLegal)!=1)return false;
        if(use is not null&&(use.ZonghuOrigin!=r.Origin||r.UseId!=use.Id||r.ActionId!=use.Action?.ActionId||!IsIssuedZonghuUse(use)))return false;
        return s.Stage!=SunYuanStage.NativeChildren||r.Returned&&s.NativeReturned&&s.Native is{}native&&native.UseId==r.UseId&&native.ActionId==r.ActionId&&LifecycleCardUse(r.UseId)is null&&ProgramEventHistory<SayingSlashReturnedEvent>().Count(e=>e.FrameId==f.Id&&e.UseId==r.UseId&&e.ActionId==r.ActionId)==1;
    }
}
