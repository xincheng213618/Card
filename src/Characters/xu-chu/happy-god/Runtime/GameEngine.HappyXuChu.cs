namespace CardGame.Core;

public sealed partial class GameEngine
{
    // The settlement's own fact freezes the history cutoff before any marker or
    // draw child can run. Only actual earlier Qing awards count as the record.
    private int HappyXuChuPreviousAward(ProgramSkillFrame frame) => CompleteProgramEventHistory()
        .TakeWhile(fact => fact is not ZhengqingSettledEvent settled || settled.FrameId != frame.Id)
        .OfType<ZhengqingSettledEvent>()
        .Where(e => e.WinnerSeat == frame.OwnerSeat && ShenXuChuComposition.IsZhengqing(e.Source.SkillId))
        .Select(e => e.MaximumDamage).DefaultIfEmpty().Max();

    private IReadOnlyList<ShenXuChuDraw> ShenXuChuRoundDraws(ProgramSkillFrame frame, int winner, int maximum)
    {
        if (winner < 0) return [];
        if (winner != frame.OwnerSeat) return [new(winner, 1), new(frame.OwnerSeat, 1)];
        if (frame.SkillId != ShenXuChuComposition.HappyZhengqing || maximum >= HappyXuChuPreviousAward(frame))
            return [new(frame.OwnerSeat, Math.Min(5, maximum))];
        // When the replacement condition fails, the winner and the owner each
        // keep their original one-card entitlement, even when they are one seat.
        return [new(frame.OwnerSeat, 1), new(frame.OwnerSeat, 1)];
    }

    private static IReadOnlyList<ShenXuChuDraw> ShenXuChuTargetDraws(ProgramSkillFrame frame, bool marked) =>
        marked && frame.SkillId == ShenXuChuComposition.Zhuangpo ? [new(frame.OwnerSeat, 1)] : [];
}
