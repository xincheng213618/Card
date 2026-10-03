namespace CardGame.Core;

public enum ProgramCappedHandRefreshStage { Drawing, Selecting, Discarding }
public sealed record ProgramCappedHandRefresh(int TargetSeat, int Maximum, ProgramCappedHandRefreshStage Stage,
    int DiscardCount, IReadOnlyList<int> CandidateCardIds, IReadOnlyList<int> SelectedCardIds);
public sealed record ProgramCappedHandRefreshDrawnEvent(long FrameId, int OwnerSeat, int TargetSeat,
    int Maximum, int DrawCount) : IGameEvent;
public sealed record ProgramCappedHandRefreshCompletedEvent(long FrameId, int OwnerSeat, int TargetSeat,
    int Maximum, int DiscardCount, bool Completed) : IGameEvent;

internal interface ICappedHandRefreshProgramHost
{
    SkillProgramStepOutcome DrawThenDiscardHandToMaximumHp(ProgramSkillFrame frame, int targetSeat, int cap);
}
internal sealed class DrawThenDiscardHandToMaximumHpDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawThenDiscardHandToMaximumHp;
    public override ISkillProgramEffectHandler Handler { get; } = new DrawThenDiscardHandToMaximumHpHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw,
            SkillProgramEffectTarget.SelectedTarget, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "amount", "condition");
        var target = reader.RequiredEnum<SkillProgramEffectTarget>("target");
        var cap = reader.RequiredInt("amount");
        if (target != SkillProgramEffectTarget.SelectedTarget || cap is < 1 or > 16)
            throw new InvalidOperationException("Capped hand refresh requires one selected living target and a cap of 1..16.");
        return new(Op, target, cap, reader.Condition());
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSelectedTarget(), new RequireTriggerWindows([
            SkillProgramTriggerWindow.AfterDamageApplied, SkillProgramTriggerWindow.OwnerDied])];
}
public sealed class DrawThenDiscardHandToMaximumHpHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DrawThenDiscardHandToMaximumHp;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat,
        ISkillProgramEffectHost host) => ((ICappedHandRefreshProgramHost)host)
            .DrawThenDiscardHandToMaximumHp(frame, targetSeat, effect.Amount);
}
