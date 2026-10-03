namespace CardGame.Core;

public sealed record ProgramFixedTargetSlashDraft(int TargetSeat, bool IgnoreDistance, bool Issued = false);
public sealed record ProgramDeckCriterionDraft(string Stage, string? Criterion = null, int Remaining = 0,
    int? MatchedCardId = null, int? RecipientSeat = null);
public sealed record ProgramDeckCriterionRevealedEvent(long FrameId, int OwnerSeat, string Criterion,
    int CardId, bool Matched) : IGameEvent;

internal interface IFixedSlashAndDeclaredDeckProgramHost
{
    SkillProgramStepOutcome UseOwnerSlashAgainstTurnOwner(ProgramSkillFrame frame, bool ignoreDistance);
    SkillProgramStepOutcome DeclareDeckCriterionAndGiveMatchingCard(ProgramSkillFrame frame);
}

internal sealed class UseOwnerSlashAgainstTurnOwnerDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseOwnerSlashAgainstTurnOwner;
    public override ISkillProgramEffectHandler Handler { get; } = new UseOwnerSlashAgainstTurnOwnerHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.RequestSlashByTarget,
        static (effect, context) => context.DeckPrograms(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "ignoreDistance", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0,
            reader.Condition(), booleanValue: reader.RequiredBool("ignoreDistance"));
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}
public sealed class UseOwnerSlashAgainstTurnOwnerHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.UseOwnerSlashAgainstTurnOwner;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat,
        ISkillProgramEffectHost host) => ((IFixedSlashAndDeclaredDeckProgramHost)host)
            .UseOwnerSlashAgainstTurnOwner(frame, effect.BooleanValue!.Value);
}

internal sealed class DeclareDeckCriterionAndGiveMatchingCardDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DeclareDeckCriterionAndGiveMatchingCard;
    public override ISkillProgramEffectHandler Handler { get; } = new DeclareDeckCriterionAndGiveMatchingCardHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0, reader.Condition());
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}
public sealed class DeclareDeckCriterionAndGiveMatchingCardHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DeclareDeckCriterionAndGiveMatchingCard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat,
        ISkillProgramEffectHost host) => ((IFixedSlashAndDeclaredDeckProgramHost)host).DeclareDeckCriterionAndGiveMatchingCard(frame);
}
