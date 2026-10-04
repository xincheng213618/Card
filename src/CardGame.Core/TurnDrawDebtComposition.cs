namespace CardGame.Core;

internal static class TurnDrawDebtComposition
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects, SkillProgramTriggerWindow? window,
        SkillProgramTriggerSubject? subject = null, bool? optional = null,
        SkillProgramDrawPhaseMode drawMode = SkillProgramDrawPhaseMode.Additive,
        SkillProgramTurnOwnerScope? turnOwnerScope = null)
    {
        if (effects.Any(e => e.Op == SkillProgramEffectOp.DrawExtraAndArmTurnDamageUseDebt) &&
            (window != SkillProgramTriggerWindow.DrawPhaseStarting || effects.Count != 1 ||
             subject is not null && subject != SkillProgramTriggerSubject.Owner || optional == false ||
             drawMode != SkillProgramDrawPhaseMode.Additive))
            throw new InvalidOperationException($"Invalid skill program at {path}: extra-draw debt requires one optional additive owner draw-start instruction.");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.SelectTurnDamageUseDebtPayment) &&
            (window != SkillProgramTriggerWindow.TurnEnding || subject is not null && subject != SkillProgramTriggerSubject.Owner ||
             optional == true || (turnOwnerScope ?? SkillProgramTurnOwnerScope.Own) != SkillProgramTurnOwnerScope.Own ||
             effects.Count != 3 || effects[0].Op != SkillProgramEffectOp.SelectTurnDamageUseDebtPayment ||
             effects[1] is not { Op: SkillProgramEffectOp.MoveBoundCards, Target: SkillProgramEffectTarget.Owner,
                 Destination: SkillProgramCardDestination.DiscardPile, AwaitMovementTriggers: true, Condition.Kind: SkillProgramConditionKind.Always } ||
             effects[1].SourceBind != effects[0].ResultBind ||
             effects[2] is not { Op: SkillProgramEffectOp.AwaitBoundCardMovements, Target: SkillProgramEffectTarget.Owner,
                 Condition.Kind: SkillProgramConditionKind.Always }))
            throw new InvalidOperationException($"Invalid skill program at {path}: turn debt requires one mandatory own-ending selection, exact discard and movement return.");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.PreventDamageAndConsumeSourceFaction) &&
            (window != SkillProgramTriggerWindow.BeforeDamageApplied || subject is not null && subject != SkillProgramTriggerSubject.DamageTarget ||
             optional == true || effects.Count != 2 || effects[0].Op != SkillProgramEffectOp.PreventDamageAndConsumeSourceFaction ||
             !IsSourceFactionYieldTransfer(effects[1])))
            throw new InvalidOperationException($"Invalid skill program at {path}: faction prevention requires a mandatory victim window and exact source-selected HEJ transfer.");
    }

    internal static bool IsSourceFactionYieldTransfer(SkillProgramEffect e) =>
        e.Op == SkillProgramEffectOp.SelectAndMoveOwnedCard && e.Amount == 1 && e.Target == SkillProgramEffectTarget.Owner &&
        e.Condition.Kind == SkillProgramConditionKind.Always && e.ChooserRef?.Kind == ProgramParticipantRef.EventSource &&
        e.CardOwnerRef?.Kind == ProgramParticipantRef.Owner && e.TargetReference?.Kind == ProgramParticipantRef.EventSource &&
        e.Destination == SkillProgramCardDestination.SelectedTargetHand && e.SkipIfNoCards && e.AwaitMovementTriggers &&
        e.ResultBind is not null && e.Zones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment]) &&
        e.CardKinds.Count == 0 && e.CardCategories.Count == 0 && !e.AllowDecline && e.NumberExpression is null;
}
