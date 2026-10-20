namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void RevealNextChenshuo(ProgramSkillFrame f)
    {
        BeginZhouQunMovement(ref f, ZhouQunStage.RevealChildren); var r = f.ZhouQun!; var card = DrawOneToProcessing(new(ChenshuoRevealReason));
        if (card is null) ReplaceRuntimeTop(f with { ZhouQun = r with { Repeat = false } });
        else
        {
            var shown = r.Shown!; var matched = GetProgramCardCategory(card.Kind) == GetProgramCardCategory(shown.Kind) || card.Suit == shown.Suit || card.Rank == shown.Rank || ZhouQunNameLength(card.Kind) == shown.NameLength;
            ReplaceRuntimeTop(f = f with { ZhouQun = r with { Revealed = r.Revealed.Append(card.Id).ToArray(), Repeat = matched } });
            AdvanceEventRulesAndQueueFact(new ChenshuoTopRevealedEvent(f.Id, r.Revealed.Count, card.Id, card.Kind, card.Suit, card.Rank, ZhouQunNameLength(card.Kind), matched));
            AdvanceEventRulesAndQueueFact(new ProgramCardsRevealedEvent(f.Id, f.SkillId, f.TriggerId!, f.OwnerSeat, "chenshuo-top", [ToSnapshot(card)]));
        }
        CompleteZhouQunMovement(f.Id);
    }
}
