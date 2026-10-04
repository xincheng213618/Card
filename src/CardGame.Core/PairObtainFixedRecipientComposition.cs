namespace CardGame.Core;

internal static class PairObtainFixedRecipientComposition
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects, SkillProgramTriggerWindow? window,
        int selectedCardCount, bool selectedTarget, int initialTargetSetMaximum)
    {
        if (effects.Any(e => e.Op is SkillProgramEffectOp.ObtainOneFromEachSelectedTarget or SkillProgramEffectOp.GiveShownCardToLeastOriginalTarget))
        {
            if (window is not null || selectedCardCount != 0 || initialTargetSetMaximum != 2 || selectedTarget || effects is not
                [{ Op: SkillProgramEffectOp.ObtainOneFromEachSelectedTarget, Target: SkillProgramEffectTarget.Owner },
                 { Op: SkillProgramEffectOp.SelectOwnedCards, Target: SkillProgramEffectTarget.Owner, Amount: 0, NumberExpression: null, ResultBind: var bind },
                 { Op: SkillProgramEffectOp.RevealBoundCards, SourceBind: var shown },
                 { Op: SkillProgramEffectOp.GiveShownCardToLeastOriginalTarget, SourceBind: var gift }] || bind != shown || bind != gift ||
                effects[1].Zones is not [CardZoneKind.Hand] || effects[1].MinimumCards != 1 || effects[1].MaximumCards != 1 || effects.Any(e => e.Condition.Kind != SkillProgramConditionKind.Always))
                throw new InvalidOperationException($"{path}: pair obtain/show/gift requires its exact two-target four-node activation.");
        }
        if (effects.Any(e => e.Op == SkillProgramEffectOp.IssueFixedRecipientBenefit) && (window != SkillProgramTriggerWindow.TurnEnding || selectedCardCount != 0 ||
            effects is not [{ Op: SkillProgramEffectOp.SelectTarget, TargetKind: SkillProgramTargetKind.OtherLiving },
                { Op: SkillProgramEffectOp.IssueFixedRecipientBenefit },
                { Op: SkillProgramEffectOp.Draw, Target: SkillProgramEffectTarget.SelectedTarget, Amount: 3 },
                { Op: SkillProgramEffectOp.Recover, Target: SkillProgramEffectTarget.SelectedTarget, Amount: 1 }] || effects.Any(e => e.Condition.Kind != SkillProgramConditionKind.Always)))
            throw new InvalidOperationException($"{path}: fixed-recipient issuance requires one own Ending select/issue/Draw3/Recover1 chain.");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.SelectIssuedFixedRecipient) && (window != SkillProgramTriggerWindow.OwnerDied || selectedCardCount != 0 ||
            effects is not [{ Op: SkillProgramEffectOp.SelectIssuedFixedRecipient },
                { Op: SkillProgramEffectOp.Draw, Target: SkillProgramEffectTarget.SelectedTarget, Amount: 3 },
                { Op: SkillProgramEffectOp.Recover, Target: SkillProgramEffectTarget.SelectedTarget, Amount: 1 }] || effects.Any(e => e.Condition.Kind != SkillProgramConditionKind.Always)))
            throw new InvalidOperationException($"{path}: death benefit requires the original issued recipient and Draw3/Recover1, without another target selection.");
    }
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.IssueFixedRecipientBenefit) &&
            (trigger.Subject != SkillProgramTriggerSubject.Owner || trigger.TurnOwnerScope != SkillProgramTurnOwnerScope.Own || !trigger.Optional ||
             trigger.UsageScope != SkillUsageScope.Game || trigger.UsageLimit != 1 || trigger.NamedUsageGroup is null))
            throw new InvalidOperationException($"{path}: ending issuance must have one owner+skill named game usage and an optional own Ending trigger.");
        if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.SelectIssuedFixedRecipient) &&
            (trigger.Subject != SkillProgramTriggerSubject.Owner || !trigger.Optional || trigger.UsageScope is not null || trigger.ChoiceGroup is not null))
            throw new InvalidOperationException($"{path}: death replay is one optional original-owner candidate, not a second limited debit.");
    }
}
