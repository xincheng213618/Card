namespace CardGame.Core;

[ProgramRuntimeModule]
internal sealed class ShuangjiaMarkRuntime(IDistanceRuleRuntimeQueries queries, ISelectionParticipantQueries participants)
{
    internal bool HasCapability => queries.HasTriggerOperation(SkillProgramEffectOp.InitializeShuangjiaInitialEntities);

    // Mark identity follows the physical entity across holders and zones.
    internal bool HasEntityMark(int cardId) => HasCapability &&
        queries.History<ShuangjiaInitialEntityMarkedEvent>().Any(fact => fact.CardId == cardId);

    internal bool HasHandEntityMark(int ownerSeat, int cardId) =>
        participants.IsValidSeat(ownerSeat) && queries.Location(cardId) == CardLocation.Hand(ownerSeat) &&
        HasEntityMark(cardId);

    internal bool IsHandLimitExempt(int ownerSeat, int cardId) =>
        HasCapability && queries.HasSkill(ownerSeat, "le:shuangjia") && HasHandEntityMark(ownerSeat, cardId);

    [ProgramDistanceRule(ProgramDistanceStage.IncomingContributions, 30)]
    private IEnumerable<RuleQueryContribution> ShuangjiaIncomingDistanceContributions(int targetSeat)
    {
        if (!HasCapability || !participants.IsValidSeat(targetSeat) || !participants.IsAlive(targetSeat) ||
            !queries.HasSkill(targetSeat, "le:shuangjia")) yield break;
        // Owned means Hand and Equipment only, preserving their enumeration order.
        var count = queries.OwnedCardIds(targetSeat).Count(HasEntityMark);
        if (count > 0)
            yield return new FiniteRuleQueryContribution($"shuangjia:{targetSeat}:marked-owned-incoming-distance",
                SkillRuleOperation.Add, Math.Min(5, count));
    }
}
