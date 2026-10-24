using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record ShiXbLibbingAdvancedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int Option, int Count1, int Count2, int Count3) : IGameEvent;
public sealed record ShiXbLibbingResetEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, long CardActionId, string UsedKind) : IGameEvent;
public sealed record ShiXbPoxiNativeIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, long UseId, long ActionId, int CardId, int TargetSeat) : IGameEvent;
public sealed record ShiXbPoxiNativeReturnedEvent(long FrameId, long UseId) : IGameEvent;

/// <summary>Per-operation receipt of the running 势夏侯霸 program instruction.</summary>
public sealed record ShiXiahouBaReceipt(string Operation, int ActualTurn, int Stage = 0)
{
    public CardConversionSource? Source { get; init; }
    public string GameplayHash { get; init; } = "";
    public long BatchId { get; init; }
    public int CardId { get; init; }
    public int DiscardMovementSequence { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public int TargetSeat { get; init; }
}

/// <summary>Binds one completed 迫袭-native Slash use to its exact owning program frame.</summary>
public sealed record ShiXiahouBaPoxiUse(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int CardId, CardKind Kind, CardLocation From, int TargetSeat, long UseId, long ActionId);

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShiXiahouBaReceipt? ShiXiahouBa { get; init; }
}

public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShiXiahouBaPoxiUse? ShiXiahouBaPoxi { get; init; }
}

internal static class ShiXiahouBaComposition
{
    internal const string LibbingSkill = "mobile:shi-libing";
    internal const string PoxiSkill = "mobile:shi-poxi";
    internal const string LibbingTrigger = "libbing-completed-use";
    internal const string PoxiTrigger = "poxi-discarded-slash";
    internal const string ProgramAction = "shi-xiahou-ba";
    internal const string PoxiUseMoveReason = "skill-program.mobile-shi-xiahou-ba.use";
    internal const string LibbingDrawReason = "skill-program.mobile:shi-libing.draw";
    internal const int LibbingStageChoice = 0;
    internal const int PoxiStageChoice = 0;
    internal const int PoxiStageIssuingUse = 6;
    internal const int PoxiStageNativeUse = 9;
    internal const int PoxiStageReturnedUse = 12;
    internal const int PoxiStageFinished = 15;

    internal static bool IsDamageCardKind(CardKind kind) => kind is
        CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.ColdSlash or
        CardKind.Duel or CardKind.BarbarianAssault or CardKind.ArrowBarrage or
        CardKind.FireAttack or CardKind.WumiaoFlood or CardKind.QizhengXiangsheng;

    [ProgramCatalogValidation(68000)]
    internal static void ValidateProgram(string path, SkillProgram program)
    {
        if (program.Id == LibbingSkill)
        {
            if (program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.DamageModifiers.Count != 0 ||
                program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.HpSkillGrants.Count != 0 ||
                program.Contributions.Count != 0 || program.BooleanStates.Count != 0 || program.Activations.Count != 0 ||
                program.Triggers.Count != 1 ||
                program.Triggers[0] is not { Id: LibbingTrigger, Window: SkillProgramTriggerWindow.CardUseCompleted,
                    OwnerRelation: SkillProgramCardActionOwnerRelation.Actor, Optional: false, IsSkillInvocation: false })
                throw new InvalidOperationException(path + ": Libbing requires exactly its completed-use actor trigger.");
            return;
        }
        if (program.Id != PoxiSkill) return;
        if (program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.DamageModifiers.Count != 0 ||
            program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.HpSkillGrants.Count != 0 ||
            program.Contributions.Count != 0 || program.BooleanStates.Count != 0 || program.Activations.Count != 0 ||
            program.Triggers.Count != 1 ||
            program.Triggers[0] is not { Id: PoxiTrigger, Window: SkillProgramTriggerWindow.DiscardPileReceived,
                MovementDiscardOnly: true, DiscardOwnerScope: SkillProgramDiscardOwnerScope.Own,
                Optional: false, IsSkillInvocation: false } ||
            program.Triggers[0].SourceZones.Count != 2 ||
            !program.Triggers[0].SourceZones.Contains(CardZoneKind.Hand) ||
            !program.Triggers[0].SourceZones.Contains(CardZoneKind.Equipment) ||
            program.Triggers[0].CardKinds.Count != 4)
            throw new InvalidOperationException(path + ": Poxi requires exactly its own discarded-slash trigger.");
    }
}

internal interface IShiXiahouBaProgramHost
{
    SkillProgramStepOutcome ResolveLibbing(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolvePoxi(ProgramSkillFrame frame);
}

internal sealed class ResolveShiXbLibbingDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShiXbLibbing;
    public override ISkillProgramEffectHandler Handler { get; } = new ShiXiahouBaHandler(SkillProgramEffectOp.ResolveShiXbLibbing);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(1));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard,
        Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
}

internal sealed class ResolveShiXbPoxiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShiXbPoxi;
    public override ISkillProgramEffectHandler Handler { get; } = new ShiXiahouBaHandler(SkillProgramEffectOp.ResolveShiXbPoxi);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (_, context) => context.PublicControlValue(1));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard,
        Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPileReceived)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
}

internal sealed class ShiXiahouBaHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) => op switch
    {
        SkillProgramEffectOp.ResolveShiXbLibbing => host.GetCapability<IShiXiahouBaProgramHost>().ResolveLibbing(frame),
        _ => host.GetCapability<IShiXiahouBaProgramHost>().ResolvePoxi(frame),
    };
}
