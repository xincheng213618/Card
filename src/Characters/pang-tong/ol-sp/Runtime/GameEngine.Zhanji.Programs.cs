using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
 private int[] MatchingZhanjiDrawIndexes(CardMovementBatchContext batch,ProgramTriggerCandidate candidate)
 {
  if(batch.NativeDrawInvocation is not{}p||p.LastBatchId!=batch.Id||p.ParentFrameId!=batch.ParentFrameId||p.OwnerSeat!=candidate.OwnerSeat||p.ActualCount<1||p.ActualTurnNumber!=batch.TurnNumber||p.ActualTurnOwnerSeat!=candidate.OwnerSeat||p.PhaseActorSeat!=candidate.OwnerSeat||p.Phase?.Kind!=ActualDiscardRecoveryPhaseKind.Play||!ValidNativeDrawInvocation(p)||!HasZhanjiDrawEntitlement(p,candidate.OwnerSeat,candidate.SkillId,candidate.BindingId,candidate.SkillInstanceId,candidate.GameplayHash)||p.DirectProducer is{}producer&&producer.OwnerSeat==candidate.OwnerSeat&&producer.SkillId==candidate.SkillId)return [];
  if(CompleteProgramEventHistory().OfType<SpPangTongSegmentPaidEvent>().Any(e=>e.Source.OwnerSeat==candidate.OwnerSeat&&e.Source.SkillId==candidate.SkillId&&e.DrawInvocation==p.InvocationId))return [];
  var last=p.Materials[^1];return batch.Movements.Select((m,i)=>(m,i)).Where(x=>x.m.Sequence==last.MovementSequence&&x.m.CardId==last.CardId&&x.m.CardKind==last.PrintedKind&&x.m.From==CardLocation.DrawPile&&x.m.To==CardLocation.Hand(candidate.OwnerSeat)&&x.m.Reason==p.Reason&&_cardMovements.Contains(x.m)).Select(x=>x.i).ToArray();
 }
}
