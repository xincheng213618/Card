namespace CardGame.Core;

/// <summary>
/// Public, bounded lookahead for one exact phase-skip -> hand-cost -> selected
/// beneficiary composition. It does not recognize a character or retain state.
/// </summary>
internal static class ProgramPhaseHandExtraTurnAi
{
    internal static bool TryGetPaymentAfterDeclaration(SkillProgram program,
        SkillProgramTrigger declaration, out SkillProgramTrigger payment)
    {
        payment = null!;
        if (declaration is not { Window: SkillProgramTriggerWindow.AfterNormalDraw,
                Subject: SkillProgramTriggerSubject.Owner, Optional: true, Condition.Kind: SkillProgramTriggerConditionKind.Always } ||
            declaration.Effects.Count != 2 ||
            declaration.Effects[0] is not { Op: SkillProgramEffectOp.SkipTurnPhases, Target: SkillProgramEffectTarget.Owner,
                Condition.Kind: SkillProgramConditionKind.Always } skip ||
            !skip.SkippedPhases.SequenceEqual([SkillProgramTurnPhase.Play]) ||
            declaration.Effects[1] is not { Op: SkillProgramEffectOp.SetBooleanState, Target: SkillProgramEffectTarget.Owner,
                StateId: not null, BooleanValue: true, Condition.Kind: SkillProgramConditionKind.Always } state ||
            !program.BooleanStates.Any(definition => definition.Id == state.StateId && !definition.InitialValue &&
                definition.Visibility == SkillProgramStateVisibility.Public && HasActualTurnReset(program, definition)))
            return false;
        var candidates = program.Triggers.Where(trigger => IsPaymentChain(trigger) &&
            IsExactDeclarationGate(trigger.Condition, state.StateId)).ToArray();
        if (candidates.Length != 1) return false;
        payment = candidates[0];
        return true;
    }

    internal static bool IsLinkedPayment(SkillProgram program, SkillProgramTrigger trigger) =>
        program.Triggers.Any(declaration => TryGetPaymentAfterDeclaration(program, declaration, out var payment) &&
            payment.Id == trigger.Id && payment.Window == trigger.Window);

    internal static double PublicBeneficiaryValue(PlayerSkillContext target) => target.IsFaceDown
        ? 4d : 20d + 2d * Math.Min(4, Math.Max(0, target.HandCount));

    internal static double PublicSkippedPlayOpportunity(PlayerSkillContext owner) =>
        4d + 2d * Math.Min(4, Math.Max(0, owner.HandCount));

    private static bool HasActualTurnReset(SkillProgram program, SkillProgramBooleanStateDefinition state) =>
        state.ResetScope == SkillProgramStateResetScope.Turn ||
        state.ResetScope == SkillProgramStateResetScope.Game && program.Triggers.Any(trigger =>
            trigger is { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,
                Subject: SkillProgramTriggerSubject.Owner, Optional: false, Condition.Kind: SkillProgramTriggerConditionKind.Always } &&
            trigger.Effects.Count == 1 && trigger.Effects[0] is
                { Op: SkillProgramEffectOp.SetBooleanState, Target: SkillProgramEffectTarget.Owner,
                    BooleanValue: false, Condition.Kind: SkillProgramConditionKind.Always } reset && reset.StateId == state.Id);

    private static bool IsExactDeclarationGate(SkillProgramTriggerCondition condition, string stateId) =>
        condition.Kind == SkillProgramTriggerConditionKind.All && condition.Children.Count == 2 &&
        condition.Children.Any(child => child.Kind == SkillProgramTriggerConditionKind.BooleanState &&
            child.StateId == stateId && child.ExpectedValue) &&
        condition.Children.Any(child => child is { Kind: SkillProgramTriggerConditionKind.Compare,
            Comparison: SkillProgramComparisonOperator.GreaterThanOrEqual,
            Left.Kind: SkillProgramTriggerValueKind.CurrentHandCount,
            Right.Kind: SkillProgramTriggerValueKind.IntegerConstant, Right.Value: 1 });

    private static bool IsPaymentChain(SkillProgramTrigger trigger)
    {
        if (trigger.Window is not (SkillProgramTriggerWindow.DiscardPhaseStarting or SkillProgramTriggerWindow.TurnEnding) ||
            trigger is not { Subject: SkillProgramTriggerSubject.Owner, Optional: true, UsageScope: SkillUsageScope.Turn, UsageLimit: 1 } ||
            trigger.Effects.Count != 4 || trigger.ChoiceGroup is not null || trigger.NamedUsageGroup is not null ||
            trigger.DynamicUsageLimit is not null || trigger.Effects.Any(effect => effect.Condition.Kind != SkillProgramConditionKind.Always))
            return false;
        return trigger.Effects[0] is { Op: SkillProgramEffectOp.SelectOwnedCards, Target: SkillProgramEffectTarget.Owner,
                Amount: 1, ResultBind: not null, NumberExpression: null, MinimumCards: 0, MaximumCards: 0, AllowDecline: false } select &&
            select.Zones.SequenceEqual([CardZoneKind.Hand]) && select.CardKinds.Count == 0 && select.Suits.Count == 0 &&
            select.TargetReference is null &&
            trigger.Effects[1] is { Op: SkillProgramEffectOp.MoveBoundCards, Target: SkillProgramEffectTarget.Owner,
                Destination: SkillProgramCardDestination.DiscardPile, ExceptBind: null, DestinationZone: null } move &&
            (move.AwaitMovementTriggers || trigger.Window == SkillProgramTriggerWindow.TurnEnding) &&
            move.SourceBind == select.ResultBind &&
            trigger.Effects[2] is { Op: SkillProgramEffectOp.SelectTarget, Target: SkillProgramEffectTarget.Owner,
                TargetKind: SkillProgramTargetKind.OtherLiving, ActorReference: null,
                Marker: null, SkipIfNoTarget: false } target && target.Zones.Count == 0 &&
            trigger.Effects[3] is { Op: SkillProgramEffectOp.PendExtraTurn, Target: SkillProgramEffectTarget.Owner,
                TargetReference.Kind: ProgramParticipantRef.SelectedTarget };
    }
}
