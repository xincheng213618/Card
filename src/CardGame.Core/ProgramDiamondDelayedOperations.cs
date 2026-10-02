namespace CardGame.Core;

internal interface IDiamondDelayedProgramHost
{
    SkillProgramStepOutcome UseDiamondDelayedOrDiscard(ProgramSkillFrame frame);
}
internal sealed class DiamondDelayedDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseDiamondDelayedOrDiscard;
    public override ISkillProgramEffectHandler Handler { get; } = new DiamondDelayedHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.UseSelectedCardsAs, static (effect, context) => context.Draw(new SkillProgramEffect(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException($"Invalid diamond delayed operation at {r.Path}: owner required.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new ConsumeSelectedCards(1)];
}
public sealed class DiamondDelayedHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.UseDiamondDelayedOrDiscard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        ((IDiamondDelayedProgramHost)host).UseDiamondDelayedOrDiscard(frame);
}
public enum ProgramDiamondDelayedStage { Choosing, Using, Moving, Drawing }
public sealed record ProgramDiamondDelayedDraft(int CardId, CardLocation Source, Suit EffectiveSuit,
    int TurnNumber, int TurnOwnerSeat, ProgramDiamondDelayedStage Stage,
    long? UseFrameId = null, int? TargetSeat = null, int? JudgmentCardId = null);
