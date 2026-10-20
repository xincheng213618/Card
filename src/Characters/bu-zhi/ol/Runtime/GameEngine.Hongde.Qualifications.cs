using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool BuZhiMovementQualifies(CardMovementBatchContext batch,int owner)
    {
        var hand=CardLocation.Hand(owner);
        // A source-side processing trip is still a real loss. Returning the same
        // used card to its original hand is not a new acquisition.
        return batch.NativeDrawInvocation is{}draw && draw.LastBatchId==batch.Id && draw.ParentFrameId==batch.ParentFrameId &&
            draw.ActualTurnNumber==batch.TurnNumber && draw.OwnerSeat==owner && draw.ActualCount>=2 && ValidNativeDrawInvocation(draw) ||
            batch.NativeBuZhiDiscardInvocation is{}discard && discard.LastBatchId==batch.Id && discard.ParentFrameId==batch.ParentFrameId &&
            discard.ActualTurnNumber==batch.TurnNumber && discard.Source.OwnerSeat==owner && discard.ActualCount>=2 && ValidNativeBuZhiDiscardInvocation(discard) ||
            batch.Movements.Count(m=>m.From.OwnerSeat==owner && m.From!=m.To)>=2 ||
            batch.Movements.Count(m=>m.To==hand && ActualHandGainOriginalSource(m)!=hand)>=2;
    }
}
