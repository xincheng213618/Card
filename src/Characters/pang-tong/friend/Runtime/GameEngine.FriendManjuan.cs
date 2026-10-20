using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int FriendManjuanMovedCount(CardMovementBatchContext batch, int seat, SkillProgramTrigger trigger, int fallback) =>
        trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.ExchangeFriendGainForDiscard) &&
        batch.NativeDrawInvocation is { } draw && draw.OwnerSeat == seat && ValidNativeDrawInvocation(draw)
            ? draw.ActualCount : fallback;
    private SkillProgramStepOutcome BeginFriendManjuan(ProgramSkillFrame frame)
    {
        var cards = FriendGainCardIds(frame.WindowContext!, frame.OwnerSeat)
            .Where(id => _cardZones.GetLocation(id) == CardLocation.Hand(frame.OwnerSeat)).ToArray();
        ReplaceRuntimeTop(frame = frame with { FriendPangTong = new(frame.InstructionIndex,
            SkillProgramEffectOp.ExchangeFriendGainForDiscard, FriendPangTongStage.Select, cards, [], []) });
        PublishFriendPangTongChoice(frame); return SkillProgramStepOutcome.AwaitChoice;
    }
}
