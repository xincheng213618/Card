namespace CardGame.Core;

internal static class TriggeredCardConversionContract
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (trigger.Window == SkillProgramTriggerWindow.RoundStarting && trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.UseVirtualCard))
        {
            if (trigger.Subject != SkillProgramTriggerSubject.Owner || trigger.Effects is not [var target, var slash] ||
                target is not { Op: SkillProgramEffectOp.SelectTarget, Target: SkillProgramEffectTarget.Owner,
                    TargetKind: SkillProgramTargetKind.OtherLivingVirtualSlashTarget, Condition.Kind: SkillProgramConditionKind.Always } ||
                slash is not { Op: SkillProgramEffectOp.UseVirtualCard, UseCardActionWindows: true,
                    TargetRestriction: SkillProgramCardTargetRestriction.DistanceUnlimitedAgainstTarget,
                    Condition.Kind: SkillProgramConditionKind.Always } ||
                trigger.Condition is not { Kind: SkillProgramTriggerConditionKind.Compare,
                    Left.Kind: SkillProgramTriggerValueKind.RoundTurnSeatCount, Comparison: SkillProgramComparisonOperator.Equal,
                    Right.Kind: SkillProgramTriggerValueKind.IntegerConstant, Right.Value: 1 })
                throw new InvalidOperationException($"Invalid skill program at {path}: a round-first virtual Slash requires the frozen first-turn condition, one legal target selection and one full card-action use.");
        }
        if (!trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.UseBoundCardsAsSlash)) return;
        if (trigger.Window != SkillProgramTriggerWindow.TurnEnding || trigger.Subject != SkillProgramTriggerSubject.Owner ||
            trigger.TurnOwnerScope != SkillProgramTurnOwnerScope.OtherLiving ||
            trigger.Effects is not [var selection, var use] || selection.Op != SkillProgramEffectOp.SelectOwnedCards ||
            use.Op != SkillProgramEffectOp.UseBoundCardsAsSlash || selection.Target != SkillProgramEffectTarget.Owner ||
            selection.TargetReference is not null || selection.Condition.Kind != SkillProgramConditionKind.Always ||
            selection.MinimumCards != 1 || selection.MaximumCards != 1 || selection.NumberExpression is not null ||
            selection.AllowDecline || selection.RequireExactCount == true || selection.FreezeSelectedCardSuits == true ||
            selection.Zones.Count == 0 || selection.Zones.Any(zone => zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)) ||
            selection.ResultBind != use.SourceBind)
            throw new InvalidOperationException($"Invalid skill program at {path}: a triggered Slash requires another living player's ending boundary, one unconditional owner H/E selection of exactly one card, and its unique terminal conversion.");
    }
}

// A trigger consumes the owner's selected physical entity through the normal
// card-use payment/attack frames. The card set is never discarded as a cost.
internal interface ITriggeredCardConversionHost
{
    SkillProgramStepOutcome UseBoundCardsAsSlash(ProgramSkillFrame frame, SkillProgramEffect effect);
}

internal sealed class UseBoundCardsAsSlashDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseBoundCardsAsSlash;
    public override ISkillProgramEffectHandler Handler { get; } = new UseBoundCardsAsSlashHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.UseSelectedCardsAs,
        static (effect, context) => context.UseSelectedCardsAs(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "sourceBind", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0,
            reader.Condition(), sourceBind: reader.RequiredIdentifier("sourceBind"), outputKind: CardKind.Slash);
        RequireAlways(effect, reader.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding),
         new RequireOwnedCardSet(effect.SourceBind!, SkillProgramEffectTarget.Owner, 1, [CardZoneKind.Hand, CardZoneKind.Equipment]),
         new ReadSingleCardSet(effect.SourceBind!),
         new MoveCardSet(effect.SourceBind!, null, SkillProgramCardDestination.DiscardPile)];
}

internal sealed class UseBoundCardsAsSlashHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.UseBoundCardsAsSlash;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat,
        ISkillProgramEffectHost host) => host.GetCapability<ITriggeredCardConversionHost>().UseBoundCardsAsSlash(frame, effect);
}
