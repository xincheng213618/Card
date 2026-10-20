using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static int DeruNameBit(CardKind kind) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_yuan_huan_ol_Deru_Queries.DeruNameBit(kind);
    private int DeruActualNames(int seat) => GetHand(_players[seat]).Aggregate(0, (mask, card) => mask | DeruNameBit(AdvancedEffectiveHandKind(_players[seat], card)));
    private void IssueDeruTake(ProgramSkillFrame f)
    {
        var r = f.YuanHuan!; var candidates = r.GuessMask != r.ActualMask && _players[r.TargetSeat].IsAlive ? GetHand(_players[r.TargetSeat]).Where(c => DeruNameBit(AdvancedEffectiveHandKind(_players[r.TargetSeat], c)) != 0).Select(c => c.Id).ToArray() : [];
        var index = candidates.Length == 0 ? -1 : _random.Next(candidates.Length); var card = index < 0 ? null : GetAdvancedCard(candidates[index]); var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { YuanHuan = r with { Stage = YuanHuanStage.TakeChildren, BasicCandidates = candidates, RandomIndex = index,
            TakenKind = card is null ? null : AdvancedEffectiveHandKind(_players[r.TargetSeat], card), TakenPrintedKind = card?.Kind }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (card is not null) MoveCard(card, CardLocation.Hand(r.TargetSeat), CardLocation.Hand(f.OwnerSeat), new(DeruTakeReason));
        RecordYuanHuanStep(f.Id, DeruTakeReason, before, card is null ? 0 : 1, card: card?.Id,
            from: card is null ? null : CardLocation.Hand(r.TargetSeat), to: card is null ? null : CardLocation.Hand(f.OwnerSeat));
    }
}
