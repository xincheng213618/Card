namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_SkillPrograms
{
    internal static bool BoundEquipmentUseMatches(ProgramSkillFrame frame, SkillProgramEffect? effect,
        ProgramMovementContinuation pending, CardUseFrame use) =>
        effect is { Op: SkillProgramEffectOp.UseBoundCardByTarget } &&
        pending.BeforeCount == 0 && pending.CoverageResultBind is null && pending.SubjectSeat == use.SourceSeat &&
        (effect.Target == SkillProgramEffectTarget.Owner && use.SourceSeat == frame.OwnerSeat ||
         effect.Target == SkillProgramEffectTarget.SelectedTarget && frame.SelectedTargetSeats is [var target] && target == use.SourceSeat) &&
        frame.CardSetBindings.SingleOrDefault(binding => binding.Name == effect.SourceBind) is
            { Visibility: SkillProgramCardSetVisibility.Public, CardIds: [var id] } &&
        use.CardId == id && use.PhysicalCardIds is [var material] && material == id && EquipmentCatalog.IsEquipment(use.CardKind);

    internal static bool IsAwaitingProgramMovement(ProgramSkillFrame frame) =>
        frame.PendingMovementContinuation is not null ||
        frame.InspectedHandSlash is { Stage: InspectedHandSlashStage.HpChildren or InspectedHandSlashStage.DiscardChildren } ||
        frame.DiamondDelayed is { Stage: ProgramDiamondDelayedStage.Using } ||
        frame.SelectedCardPayment is not null && frame.SelectedCardPaymentResult is null;

    internal static PromptChoice CreateProgramPlayChoice(LegalAction action)
    {
        var parameters = new Dictionary<string, string>
        {
            ["action"] = "use-program-skill",
            ["skill-id"] = action.ProgramSkillId!,
            ["activation-id"] = action.ProgramActivationId!,
            ["min-card-count"] = action.MinCardCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["max-card-count"] = action.MaxCardCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["min-target-count"] = action.MinTargetCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["max-target-count"] = action.MaxTargetCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (action.ProgramSkillOwnerSeat is { } ownerSeat)
            parameters["skill-owner-seat"] = ownerSeat.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new PromptChoice(
            new ChoiceId($"play.program.{action.ProgramSkillId!.Length}:{action.ProgramSkillId}.{action.ProgramActivationId}" +
                (action.ProgramSkillOwnerSeat is { } seat ? $".owner-{seat}" : string.Empty)),
            action.Description, [], [], parameters);
    }
}
