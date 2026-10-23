namespace CardGame.Core;

internal static class PaidColorDuelOperation
{
    internal static bool IsPayment(SkillProgramEffectOp op) => op is
        SkillProgramEffectOp.DiscardBoundCardForOppositeTurnDuel or
        SkillProgramEffectOp.DiscardBoundCardForRoundScaledTurnDuel;
}

internal sealed class DiscardBoundCardForRoundScaledTurnDuelDescriptor : ProgramOperationDescriptorBase
{
    private static readonly DiscardBoundCardForOppositeTurnDuelDescriptor Payment = new();
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardBoundCardForRoundScaledTurnDuel;
    public override ISkillProgramEffectHandler Handler { get; } = new DiscardBoundCardForRoundScaledTurnDuelHandler();
    public override ProgramOperationAiPolicy AiPolicy => Payment.AiPolicy;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0,
            r.Condition(), sourceBind: r.RequiredIdentifier("sourceBind"));
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Payment.Resources(e);
}

public sealed class DiscardBoundCardForRoundScaledTurnDuelHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardBoundCardForRoundScaledTurnDuel;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int t, ISkillProgramEffectHost h) =>
        h.GetCapability<IPaidColorDamageClaimProgramHost>().DiscardBoundCardForOppositeTurnDuel(f, e.SourceBind!);
}

// Both facts contain public scalar provenance only. The existing paid-color
// receipt retains the actual HE entity, movement, frozen color and child state.
public sealed record ShanheS5ShuangxiongPaidEvent(long ProgramFrameId, CardConversionSource Source,
    string GameplayHash, int RoundNumber, int ActualTurnNumber, int FrozenDamage) : IGameEvent;
public sealed record ShanheS5ShuangxiongDuelIssuedEvent(long UseFrameId, long CardActionId,
    long ProducerFrameId, long GrantSequence, CardConversionSource Source, int FrozenDamage) : IGameEvent;
