namespace CardGame.Core;

/// <summary>Definition-only admission to the existing real response-use window.</summary>
internal sealed class MobileLonghunCommittedUseContract : IProgramCommittedUseContract
{
    public string SkillId => "mobile:longhun";

    public void ValidateTrigger(SkillProgramTrigger t)
    {
        var blackConversion = t.Id == "two-club-discard" && t.SourceViewAsId == "2-club" ||
            t.Id == "two-spade-discard" && t.SourceViewAsId == "2-spade";
        if (!blackConversion || t.Window != SkillProgramTriggerWindow.CardUseCommitted || t.Optional || !t.IncludeResponseUses ||
            t.OwnerRelation != SkillProgramCardActionOwnerRelation.ConversionSource || t.Subject is not null ||
            t.SourceSkillId != SkillId || !t.AllowNoEventTarget || t.SingleActionInstance ||
            t.UsageScope is not null || t.UsageLimit is not null || t.DynamicUsageLimit is not null ||
            t.NamedUsageGroup is not null || t.MarkerCost is not null || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 ||
            t.RequireNoCardConversion is not null || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            t.Effects is not [
                { Op: SkillProgramEffectOp.SelectTarget, Target: SkillProgramEffectTarget.Owner,
                    TargetKind: SkillProgramTargetKind.CurrentTurnPlayer, Condition.Kind: SkillProgramConditionKind.Always },
                { Op: SkillProgramEffectOp.SelectAndMoveOwnedCard, Target: SkillProgramEffectTarget.Owner, Amount: 1,
                    ChooserRef.Kind: ProgramParticipantRef.Owner, CardOwnerRef.Kind: ProgramParticipantRef.SelectedTarget,
                    Destination: SkillProgramCardDestination.DiscardPile, Zones: [CardZoneKind.Hand, CardZoneKind.Equipment],
                    SkipIfNoCards: true, AwaitMovementTriggers: true, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException("Mobile Longhun requires its exact compulsory two-black true-use discard binding.");
    }
}
