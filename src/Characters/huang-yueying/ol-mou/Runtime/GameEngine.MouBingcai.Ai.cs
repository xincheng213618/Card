namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouHuangYueyingBingcai)]
    private PromptChoice ChooseMouBingcai(PendingDecision decision, ProgramSkillFrame frame)
    {
        // Only the chooser's own private cards and the already public Basic kind are inspected.
        var cards = CreateSnapshot(frame.OwnerSeat).Players[frame.OwnerSeat].Hand.Concat(CreateSnapshot(frame.OwnerSeat).Players[frame.OwnerSeat].Equipment).ToDictionary(c => c.Id);
        return decision.Choices.Where(c => c.Parameters.GetValueOrDefault("branch") != "cancel").OrderByDescending(c => c.Cards.Count == 1 && cards.TryGetValue(c.Cards[0], out var card)
            ? (IsMouHuangYueyingDamageCardKind(card.Kind) == IsMouHuangYueyingDamageCardKind(frame.MouBingcai!.BasicKind) ? 2 : 0) +
                (CardUseCategoryCatalog.Get(card.Kind) is CardUseCategories.InstantTrick or CardUseCategories.DelayedTrick ? 1 : 0) : 0).FirstOrDefault() ?? decision.Choices[0];
    }
}
