using System.Text.Json.Serialization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_lu_kang_wumiao_WuLuKangPindianEntry
{
    // A selected contest card is its Pindian source, not an additional cost.
    // All other activations retain their declared physical-card requirements.
    internal static bool IsWuLuKangSelectedPindianSource(SkillProgramActivation a) =>
        a.MinCards == 1 && a.MaxCards == 1 && a.MinTargets == 1 &&
        a.TargetKind is (SkillProgramTargetKind.OtherLivingWithHand or SkillProgramTargetKind.OtherLivingWuFactionWithHand or
            SkillProgramTargetKind.OtherLivingWithHandHpGreaterThanOwner) && a.CardCountExpression is null &&
        a.SourceZones.SequenceEqual([CardZoneKind.Hand]) && a.MarkerCost is null &&
        !a.SelectedCardsSameSuit && !a.SelectedCardsDistinctSuits && a.CardKinds.Count == 0 &&
        a.CardSuits.Count == 0 && a.CardCategories.Count == 0 && a.EquipmentSlots.Count == 0 &&
        (a.MaxTargets == 3 && a.Effects is [{ Op: SkillProgramEffectOp.StartSimultaneousPindian,
            Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
         a.MaxTargets == 1 && a.Effects.Count > 0 && a.Effects[0] is { Op: SkillProgramEffectOp.Pindian,
            Target: SkillProgramEffectTarget.SelectedTarget, Condition.Kind: SkillProgramConditionKind.Always } &&
         a.Effects.Skip(1).All(e => e.NumberExpression is null &&
            e.Condition.Kind is (SkillProgramConditionKind.PindianWon or SkillProgramConditionKind.PindianNotWon) &&
            e.Op is SkillProgramEffectOp.GrantTurnCardTargetRestriction or
            SkillProgramEffectOp.GrantTurnCardActionProhibition or SkillProgramEffectOp.GrantTurnRuleModifier or
            SkillProgramEffectOp.SelectTarget or SkillProgramEffectOp.Damage or SkillProgramEffectOp.GrantNextActualUseTargetAdjustment or
            SkillProgramEffectOp.SetBooleanState or SkillProgramEffectOp.GrantDirectedTurnCardPolicy or
            SkillProgramEffectOp.GrantTurnOriginalTargetAddition or SkillProgramEffectOp.GrantTurnHandLimitCardKindExemption));

    internal static bool IsWuLuKangPindianTargetInstruction(IReadOnlyList<SkillProgramEffect> instructions, int index) =>
        index >= 0 && index + 1 < instructions.Count && instructions[index] is
        { Op: SkillProgramEffectOp.SelectTarget, TargetKind: SkillProgramTargetKind.OtherLivingWithHand, Zones.Count: 0 } &&
        instructions[index + 1] is { Op: SkillProgramEffectOp.StartPindian, Target: SkillProgramEffectTarget.Owner,
            Condition.Kind: SkillProgramConditionKind.Always, OpponentReference.Kind: ProgramParticipantRef.SelectedTarget };
}
