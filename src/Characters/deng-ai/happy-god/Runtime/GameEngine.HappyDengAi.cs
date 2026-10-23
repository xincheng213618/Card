namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string HappyDaTuoYu = "happy:tuoyu", HappyDaXianjin = "happy:xianjin", HappyDaQijing = "happy:qijing", HappyDaCuixin = "happy:cuixin";
    private static bool IsShenDaTuoYu(string id) => id is ShenDaTuoYu or HappyDaTuoYu;
    private static bool IsShenDaXianjin(string id) => id is ShenDaXianjin or HappyDaXianjin;
    private static bool IsShenDaQijing(string id) => id is ShenDaQijing or HappyDaQijing;
    private static bool IsShenDaCuixin(string id) => id is ShenDaCuixin or HappyDaCuixin;
    private string? ShenDaTuoYuId(CharacterState owner) => HasRuntimeSkill(owner, ShenDaTuoYu) ? ShenDaTuoYu : HasRuntimeSkill(owner, HappyDaTuoYu) ? HappyDaTuoYu : null;
    private int[] ShenDaAllocationCards(ProgramSkillFrame frame) => frame.SkillId != HappyDaTuoYu ? ShenDaUnassigned(frame.OwnerSeat) :
        GetHand(_players[frame.OwnerSeat]).Select(c => c.Id).Except(ProgramEventHistory<ShenDengAiAssignedEvent>()
            .Where(e => e.FrameId == frame.Id && e.OwnerSeat == frame.OwnerSeat).Select(e => e.CardId)).ToArray();
}
