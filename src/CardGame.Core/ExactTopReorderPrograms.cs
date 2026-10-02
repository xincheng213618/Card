namespace CardGame.Core;

public interface IExactTopReorderProgramEffectHost
{
    SkillProgramStepOutcome ReorderTopCardsExactly(ProgramSkillFrame frame, int maximumCards,
        SkillProgramNumberExpression? numberExpression, int exactTopCount);
}

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IExactTopReorderProgramEffectHost
    {
        public SkillProgramStepOutcome ReorderTopCardsExactly(ProgramSkillFrame frame, int maximumCards,
            SkillProgramNumberExpression? numberExpression, int exactTopCount) =>
            engine.BeginProgramTopReorder(frame, maximumCards, numberExpression, exactTopCount);
    }

    private CardSnapshot[] GetExactTopPrivatelyViewedCards(int viewerSeat) =>
        _resolutionStack.LastOrDefault() is ProgramSkillFrame { TopReorder.RequiredTopCount: not null } frame &&
        frame.OwnerSeat == viewerSeat && _pendingDecision is { Kind: DecisionKind.ProgramTopReorder, PlayerSeat: var seat } &&
        seat == viewerSeat
            ? frame.TopReorder!.ViewedCardIds.Select(id =>
                ToSnapshot(_cardZones.CardsAt(CardLocation.DrawPile).Single(card => card.Id == id))).ToArray()
            : [];

    private void ValidateExactTopReorder(ProgramSkillFrame frame)
    {
        var draft = frame.TopReorder!;
        var required = draft.RequiredTopCount!.Value;
        var effect = ProgramInstructionResolver.Default.Resolve(frame,
            _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        var selected = draft.TopCardIds.Concat(draft.BottomCardIds).ToArray();
        if (effect.Op != SkillProgramEffectOp.ReorderTopCards || effect.ExactTopCount is not { } exact ||
            frame.OwnerSeat != _currentSeat || draft.ViewedCardIds.Count is < 1 or > 16 ||
            draft.ViewedCardIds.Count > effect.Amount || draft.ViewedCardIds.Distinct().Count() != draft.ViewedCardIds.Count ||
            required != Math.Min(exact, draft.ViewedCardIds.Count) || required < 1 ||
            draft.TopCardIds.Count > required || selected.Distinct().Count() != selected.Length ||
            selected.Any(id => !draft.ViewedCardIds.Contains(id)) ||
            draft.ChoosingBottom && draft.TopCardIds.Count != required ||
            !draft.ChoosingBottom && (draft.BottomCardIds.Count != 0 || draft.TopCardIds.Count >= required))
            throw new InvalidOperationException("Exact top ordering lost its owning instruction, partition or stage.");
    }
}
