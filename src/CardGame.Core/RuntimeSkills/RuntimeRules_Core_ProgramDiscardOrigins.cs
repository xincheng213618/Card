namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_ProgramDiscardOrigins
{
    internal const string PairedColorDiscardReason = "skill-program.paired-color-response.discard";

    internal static bool IsOwnedDiscardSource(CardLocation location) => location.OwnerSeat is not null &&
        location.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment;

    internal static bool IsProgramDiscardOriginReason(CardMoveReason reason)
    {
        var value = reason.Value;
        if (value == PairedColorDiscardReason) return true;
        if (value.StartsWith("skill-program.", StringComparison.Ordinal) &&
            value.EndsWith(".kangge-death-price", StringComparison.Ordinal) ||
            value.EndsWith(".liejie-discard", StringComparison.Ordinal)) return true;
        // A cleanup move can resemble a discard suffix but is never the paid discard entry.
        if (value.StartsWith("card.use", StringComparison.Ordinal) || value.StartsWith("card.respond", StringComparison.Ordinal) ||
            value.StartsWith("card.response", StringComparison.Ordinal) || value.StartsWith("card.recast.", StringComparison.Ordinal) ||
            value.Contains("replacement", StringComparison.Ordinal) || value.Contains("replace", StringComparison.Ordinal) ||
            value.Contains("death", StringComparison.Ordinal) || value.Contains("harvest", StringComparison.Ordinal) ||
            value.Contains("finished", StringComparison.Ordinal)) return false;
        if (value.StartsWith("skill-program.", StringComparison.Ordinal))
        {
            var operation = value[(value.LastIndexOf('.') + 1)..];
            if (operation is nameof(SkillProgramEffectOp.ChooseCategoryAlternativeDiscard) or
                nameof(SkillProgramEffectOp.EscalatingDiscardOrDamage) or nameof(SkillProgramEffectOp.ChooseCategoryOrSequentialDiscard) or nameof(SkillProgramEffectOp.EscalatingDiscardOrDamageFromSelected) or
                nameof(SkillProgramEffectOp.RequestAttackRangeAid) or
                nameof(SkillProgramEffectOp.PayEquipmentColorDiscard) or nameof(SkillProgramEffectOp.DiscardDrawAndOfferUniqueHpPeer)) return true;
        }
        return CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramDiscardTopPlacement.IsDiscardMovementReason(reason);
    }
}
