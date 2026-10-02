namespace CardGame.Core;

public enum ProgramFinalTargetComparison { TargetHandAtMostActor, TargetHpAtLeastActor }
public sealed record ProgramTargetSlashReceipt(long ActionId, int ActorSeat, int TargetSeat,
    long CardUseFrameId, long ProducerFrameId, string SkillId, string SkillInstanceId, string GameplayHash, string TriggerId,
    int EffectIndex, bool PreventCancellation, int DamageBonus);
public sealed record ProgramTargetSlashReceiptIssuedEvent(long CardUseFrameId, ProgramTargetSlashReceipt Receipt) : IGameEvent;

internal interface IFinalTargetSlashProgramHost
{
    void IssueFinalTargetSlashReceipt(ProgramSkillFrame frame, SkillProgramEffect effect);
}
internal abstract class FinalTargetSlashDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ProhibitCurrentResponse,
        static (effect, context) => context.FinalTargetSlashValue(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "comparison", "amount", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException("A final target Slash fact requires actor ownership.");
        var amount = Op == SkillProgramEffectOp.AddCurrentTargetSlashDamage ? r.RequiredInt("amount") : 0;
        if (amount is < 0 or > 20 || Op == SkillProgramEffectOp.AddCurrentTargetSlashDamage && amount < 1 ||
            Op == SkillProgramEffectOp.PreventCurrentTargetSlashCancellation && r.Has("amount"))
            throw new InvalidOperationException("A final target Slash damage amount must be 1..20; cancellation has no amount.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, amount, r.Condition())
        { FinalTargetComparison = r.RequiredEnum<ProgramFinalTargetComparison>("comparison") };
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized), new RequireCardActionActor(),
         new RequireCardActionRelation(SkillProgramCardActionOwnerRelation.Actor, [CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash])];
}
internal sealed class PreventCurrentTargetSlashCancellationDescriptor : FinalTargetSlashDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PreventCurrentTargetSlashCancellation;
    public override ISkillProgramEffectHandler Handler { get; } = new FinalTargetSlashHandler(SkillProgramEffectOp.PreventCurrentTargetSlashCancellation);
}
internal sealed class AddCurrentTargetSlashDamageDescriptor : FinalTargetSlashDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.AddCurrentTargetSlashDamage;
    public override ISkillProgramEffectHandler Handler { get; } = new FinalTargetSlashHandler(SkillProgramEffectOp.AddCurrentTargetSlashDamage);
}
internal sealed class FinalTargetSlashHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    {
        ((IFinalTargetSlashProgramHost)host).IssueFinalTargetSlashReceipt(frame, effect);
        return SkillProgramStepOutcome.Continue;
    }
}