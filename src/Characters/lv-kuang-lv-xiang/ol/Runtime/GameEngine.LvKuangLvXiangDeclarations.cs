namespace CardGame.Core;
public sealed partial class GameEngine
{
 private void BeginQigongDeclaration(ProgramSkillFrame f,PromptChoice choice)
 {
  if(!IsQigongRequest(f)||!TryReadConversionSource(choice.Parameters,out var source)||source is null||ViewAsRule(source)?.DeclarationValidation is null||choice.Cards is not[var id]||source.OwnerSeat!=f.LvKuangLvXiang!.Actor||_cardZones.GetLocation(id)!=CardLocation.Hand(source.OwnerSeat))throw new InvalidOperationException("Qigong declaration needs its actual selected actor's one original hand cost.");var r=f.LvKuangLvXiang;var kind=Enum.Parse<CardKind>(choice.Parameters["effective-kind"]);var declaration=new QigongDeclarationRequest(r.Actor,id,source,kind,_movementSequence);ReplaceRuntimeTop(f=f with{LvKuangLvXiang=r with{Stage="declaration",Declaration=declaration}});
  var action=new LegalAction(LegalActionKind.Slash,id,r.Target,"齐攻原生声明用杀",PlayedCardKind:kind,TargetSeats:[r.Target]){ConversionSource=source};if(!TryBeginCardDeclaration(_players[r.Actor],GetAdvancedCard(id),kind,source,DeclarationReturn(CardDeclarationPurpose.Use,r.Actor,action),[r.Target]))throw new InvalidOperationException("Qigong failed to enter its actual native declaration.");
 }
 private bool MatchesQigongDeclaration(ProgramSkillFrame f,CardDeclarationFrame d)
 {
  if(f.LvKuangLvXiang is not{Declaration:{}r,Stage:"declaration"}p||r.Actor!=p.Actor||r.Actor!=d.OwnerSeat||d.Return is not{Purpose:CardDeclarationPurpose.Use,UseAction:{}a}ret||ret.ParentFrameId!=f.Id||ret.ParentKind!=f.Kind||ret.ActorSeat!=r.Actor||a.CardId!=r.CardId||a.ConversionSource!=r.Conversion||a.PlayedCardKind!=r.Kind||!a.TargetSeats.SequenceEqual([p.Target])||d.Payment is not{Claimed:false}payment||payment.Cost.CardId!=r.CardId||payment.Cost.From!=CardLocation.Hand(r.Actor)||payment.Source!=r.Conversion||payment.DeclaredKind!=r.Kind||payment.ProviderSeat!=r.Actor||payment.ActorSeat!=r.Actor||d.TurnNumber!=p.Turn||p.Turn!=_turnNumber||!d.TargetSeats.SequenceEqual([p.Target]))return false;
  return CompleteProgramEventHistory().OfType<CardDeclarationCommittedEvent>().Count(e=>e.DeclarationId==d.Id&&e.OwnerSeat==r.Actor&&e.ActorSeat==r.Actor&&e.DeclaredKind==r.Kind&&e.TargetSeats.SequenceEqual([p.Target]))==1&&_cardMovements.Count(m=>m.Sequence>r.Before&&m.CardId==r.CardId&&m.From==payment.Cost.From&&m.To==CardLocation.Processing&&m.Reason.Value=="conversion.declaration.pay")==1;
 }
 private bool IsQigongDeclarationQuery(CharacterState actor,int?selectedId)=>selectedId is{}id&&_resolutionStack.LastOrDefault()is CardDeclarationFrame{Stage:CardDeclarationStage.Applying,Succeeded:true}d&&_resolutionStack.SingleOrDefault(f=>f.Id==d.Return.ParentFrameId)is ProgramSkillFrame root&&MatchesQigongDeclaration(root,d)&&d.OwnerSeat==actor.Seat&&d.Payment.Cost.CardId==id&&UnclaimedDeclarationPayment(actor.Seat,id)==d.Payment;
 private bool IsQigongDeclarationChild(ProgramSkillFrame f,ResolutionFrame child)=>child is CardDeclarationFrame d&&MatchesQigongDeclaration(f,d);
 private bool TryApplyQigongDeclaration(CardDeclarationFrame declaration)
 {
  if(_resolutionStack.SingleOrDefault(f=>f.Id==declaration.Return.ParentFrameId)is not ProgramSkillFrame{LvKuangLvXiang:{Stage:"declaration",Declaration:{}d}p}root)return false;
  if(declaration.Stage!=CardDeclarationStage.Applying||declaration.Succeeded!=true||!MatchesQigongDeclaration(root,declaration)||_resolutionStack.LastOrDefault()?.Id!=declaration.Id)throw new InvalidOperationException("Qigong declaration lost its actual successful paid native application.");
  ReplaceRuntimeFrame(root.Id,root with{LvKuangLvXiang=p with{Stage="issuing",IssuingIds=[d.CardId],IssuingKind=d.Kind}});ResolveSlashCore(_players[p.Actor],_players[p.Target],GetAdvancedCard(d.CardId),d.Kind,p.Actor,countsTowardSlashLimit:false,conversionSource:d.Conversion,programSkillCardUseFrameId:root.Id);return true;
 }
 private bool TryReturnQigongDeclaration(CardDeclarationFrame d,bool success)
 {
  if(success||_resolutionStack.SingleOrDefault(f=>f.Id==d.Return.ParentFrameId)is not ProgramSkillFrame{LvKuangLvXiang:{Stage:"declaration"}}root)return false;
  if(_resolutionStack.Any(f=>f.Id==d.Id)||_resolutionStack.LastOrDefault()?.Id!=root.Id||!MatchesQigongDeclaration(root,d)||d.Stage!=CardDeclarationStage.Returning)throw new InvalidOperationException("Qigong failed declaration returned a different original payment.");FinishLvKuangLvXiang(root);return true;
 }
 private bool QigongDeclarationObserverEdge(int index)
 {
  if(index<1||_resolutionStack[index-1]is not CardDeclarationFrame d||_resolutionStack.SingleOrDefault(f=>f.Id==d.Return.ParentFrameId)is not ProgramSkillFrame root||!MatchesQigongDeclaration(root,d))return false;
  if(_resolutionStack[index]is CardDeclarationChallengeFrame challenge)return d.Stage==CardDeclarationStage.Challenging&&challenge.ParentFrameId==d.Id&&challenge.EligibleSeats.Distinct().Count()==challenge.EligibleSeats.Count&&challenge.EligibleSeats.All(s=>IsValidPlayerSeat(s)&&s!=d.OwnerSeat);
  if(_resolutionStack[index]is CardsMovedTriggerWindowFrame moved)return d.Stage is CardDeclarationStage.Paying or CardDeclarationStage.Cleaning&&d.ActiveChildFrameId==moved.Id&&moved.ResumeDeclarationFrameId==d.Id&&moved.Batch.ParentFrameId==d.Id&&moved.Batch.Movements.Count==1&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.CardId==d.Payment.Cost.CardId&&(d.Stage==CardDeclarationStage.Paying?m.From==d.Payment.Cost.From&&m.To==CardLocation.Processing&&m.Reason.Value=="conversion.declaration.pay":m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile&&m.Reason.Value is "conversion.declaration.failed" or "conversion.declaration.cancelled"));
  return _resolutionStack[index]is ProgramLifecycleTriggerWindowFrame skills&&d.Stage==CardDeclarationStage.Granting&&skills.Window==SkillProgramTriggerWindow.SkillsChanged&&skills.ResumeProgramFrameId==d.Id;
 }
}
