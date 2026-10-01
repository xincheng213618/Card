namespace CardGame.Core;

internal interface IQuotaTopProgramHost
{
    SkillProgramStepOutcome PeekTurnQuotaTop(ProgramSkillFrame frame, int count);
    void NullifyFirstTurnTargetByHand(ProgramSkillFrame frame);
}

internal sealed class PeekTurnQuotaTopDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PeekTurnQuotaTop;
    public override ISkillProgramEffectHandler Handler { get; } = new PeekTurnQuotaTopHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.PriceTurnQuotaPeek(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner || r.RequiredInt("amount") != 3)
            throw new InvalidOperationException($"Invalid quota peek at {r.Path}: owner and three criterion cards required.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, 3, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding), new RequireOwnTurnBoundary()];
}
public sealed class PeekTurnQuotaTopHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.PeekTurnQuotaTop;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        ((IQuotaTopProgramHost)host).PeekTurnQuotaTop(frame, effect.Amount);
}

internal sealed class NullifyFirstTurnTargetByHandDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.NullifyFirstTurnTargetByHand;
    public override ISkillProgramEffectHandler Handler { get; } = new NullifyFirstTurnTargetByHandHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.NullifyCurrentCardEffect,
        static (_, context) => context.NullifyCurrentCardEffect());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException($"Invalid first-target defense at {r.Path}: owner required.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized),
            new RequireCardActionRelation(SkillProgramCardActionOwnerRelation.Target,
                [CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.Duel])];
}
public sealed class NullifyFirstTurnTargetByHandHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.NullifyFirstTurnTargetByHand;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    { ((IQuotaTopProgramHost)host).NullifyFirstTurnTargetByHand(frame); return SkillProgramStepOutcome.Continue; }
}

/// <summary>The quota and viewed identities are frozen before any obtaining child.</summary>
public sealed record ProgramQuotaTopDraft(int Quota, IReadOnlyList<int> ViewedIds,
    IReadOnlyList<int> ObtainedIds, IReadOnlyList<int> OrderedTopIds, string Stage,
    int? LossTarget = null, int LossCursor = 0,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<int>? UnavailableIds = null);
