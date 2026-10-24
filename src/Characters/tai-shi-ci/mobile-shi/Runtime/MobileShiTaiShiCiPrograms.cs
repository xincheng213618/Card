using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record ShiTzHanzhanStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int CounterpartSeat) : IGameEvent;
public sealed record ShiTzHanzhanRallyDrawnEvent(long FrameId, int Seat, int Requested, int ActualCount) : IGameEvent;
public sealed record ShiTzRangeRecordedEvent(int TurnNumber, int TurnOwnerSeat, int Range, int? FixedValue) : IGameEvent;
public sealed record ShiTzLieGainedEvent(int TurnNumber, int TurnOwnerSeat, int CardId, int Ordinal, int Total) : IGameEvent;
public sealed record ShiTzSlashIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int[] TargetSeats, int Options) : IGameEvent;
public sealed record ShiTzRespondGatePaidEvent(long FrameId, long AttackResolutionId, int TargetSeat, bool Paid) : IGameEvent;
public sealed record ShiTzZhenfengFixedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, string Kind, int Value) : IGameEvent;

public sealed record ShiTaiShiCiReceipt(string Operation, int ActualTurn, int Stage = 0,
    int CounterpartSeat = -1, int TargetSeat = -1, int DrawCursor = 0, long Before = 0)
{
    public CardConversionSource? Source { get; init; }
    public string GameplayHash { get; init; } = "";
    public int Options { get; init; }
    public IReadOnlyList<int>? TargetSeats { get; init; }
    public int SlashCursor { get; init; }
    public long AttackResolutionId { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShiTaiShiCiReceipt? ShiTaiShiCi { get; init; }
}

internal static class ShiTaiShiCiComposition
{
    internal const string HanzhanSkill = "mobile:shi-hanzhan";
    internal const string ZhanlieSkill = "mobile:shi-zhanlie";
    internal const string ZhenfengSkill = "mobile:shi-zhenfeng";
    internal const string HanzhanActivation = "rally";
    internal const string ZhanlieRangeTrigger = "range-record";
    internal const string ZhanlieEndingTrigger = "zhanlie-ending";
    internal const string ZhanlieGateTrigger = "respond-gate";
    internal const string ZhenfengActivation = "spearhead";
    internal const string ZhanlieProgramAction = "mobile-shi-zhanlie";
    internal const string HanzhanRallyDrawReason = "skill-program.mobile:shi-hanzhan.rally";
    internal const string HanzhanGateMoveReason = "skill-program.mobile:shi-zhanlie.gate";
    internal const string ZhanlieRewardDrawReason = "skill-program.mobile:shi-zhanlie.reward";
    internal const int MaxLieCount = 6;
    internal const int OptionExtraTarget = 1;
    internal const int OptionExtraDamage = 2;
    internal const int OptionRespondCost = 4;
    internal const int OptionRewardDraw = 8;

    internal static bool IsSlashKind(CardKind kind) =>
        kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.ColdSlash;

    [ProgramCatalogValidation(67600)]
    internal static void ValidateProgram(string path, SkillProgram program)
    {
        if (program.Id == HanzhanSkill)
        {
            if (program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.DamageModifiers.Count != 0 ||
                program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.HpSkillGrants.Count != 0 ||
                program.Contributions.Count != 0 || program.BooleanStates.Count != 0 || program.Triggers.Count != 0 ||
                program.Activations is not [{ Id: HanzhanActivation, MinCards: 0, MaxCards: 0, MinTargets: 2, MaxTargets: 2,
                    TargetKind: SkillProgramTargetKind.AnyLiving, UsesPerPhase: 1, UsesPerTurn: null, UsesPerGame: null }] ||
                program.Activations[0].Effects is not
                [
                    { Op: SkillProgramEffectOp.ResolveShiTzHanzhan, Target: SkillProgramEffectTarget.Owner },
                    { Op: SkillProgramEffectOp.StartVirtualDuel, Target: SkillProgramEffectTarget.Owner }
                ])
                throw new InvalidOperationException(path + ": Hanzhan requires exactly its rally activation and the virtual Duel instruction.");
            return;
        }
        if (program.Id == ZhanlieSkill)
        {
            if (program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.DamageModifiers.Count != 0 ||
                program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.HpSkillGrants.Count != 0 ||
                program.Contributions.Count != 0 || program.BooleanStates.Count != 0 || program.Activations.Count != 0 ||
                program.Triggers.Count != 3 ||
                program.Triggers[0] is not { Id: ZhanlieRangeTrigger, Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,
                    Subject: SkillProgramTriggerSubject.Owner, TurnOwnerScope: SkillProgramTurnOwnerScope.AnyLiving,
                    Optional: false, IsSkillInvocation: false } ||
                program.Triggers[1] is not { Id: ZhanlieEndingTrigger, Window: SkillProgramTriggerWindow.PlayEnding,
                    Subject: SkillProgramTriggerSubject.Owner, Optional: true } ||
                program.Triggers[2] is not { Id: ZhanlieGateTrigger, Window: SkillProgramTriggerWindow.SlashBeforeResponse,
                    OwnerRelation: SkillProgramCardActionOwnerRelation.Actor,
                    Optional: false, IsSkillInvocation: false })
                throw new InvalidOperationException(path + ": Zhanlie requires its range record, play-ending option and slash response gate triggers.");
            return;
        }
        if (program.Id != ZhenfengSkill) return;
        if (program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.DamageModifiers.Count != 0 ||
            program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.HpSkillGrants.Count != 0 ||
            program.Contributions.Count != 0 || program.BooleanStates.Count != 0 || program.Triggers.Count != 0 ||
            program.Activations is not [{ Id: ZhenfengActivation, MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0,
                TargetKind: SkillProgramTargetKind.AnyLiving, UsesPerTurn: null, UsesPerGame: 1 }] ||
            program.Activations[0].Effects is not [{ Op: SkillProgramEffectOp.ResolveShiTzZhenfeng, Target: SkillProgramEffectTarget.Owner }])
            throw new InvalidOperationException(path + ": Zhenfeng requires exactly its once-per-game spearhead activation.");
    }
}

internal interface IShiTzProgramHost
{
    SkillProgramStepOutcome ResolveHanzhan(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveRangeRecord(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveZhanlie(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveRespondGate(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveZhenfeng(ProgramSkillFrame frame);
}

internal sealed class ResolveShiTzHanzhanDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShiTzHanzhan;
    public override ISkillProgramEffectHandler Handler { get; } = new ShiTzHandler(SkillProgramEffectOp.ResolveShiTzHanzhan);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(1));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard,
        Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
}

internal sealed class ResolveShiTzRangeRecordDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShiTzRangeRecord;
    public override ISkillProgramEffectHandler Handler { get; } = new ShiTzHandler(SkillProgramEffectOp.ResolveShiTzRangeRecord);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(1));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard,
        Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
}

internal sealed class ResolveShiTzZhanlieDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShiTzZhanlie;
    public override ISkillProgramEffectHandler Handler { get; } = new ShiTzHandler(SkillProgramEffectOp.ResolveShiTzZhanlie);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (_, context) => context.PublicControlValue(1));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard,
        Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayEnding)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
}

internal sealed class ResolveShiTzRespondGateDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShiTzRespondGate;
    public override ISkillProgramEffectHandler Handler { get; } = new ShiTzHandler(SkillProgramEffectOp.ResolveShiTzRespondGate);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.LoseHp,
        static (_, context) => context.PublicControlValue(1));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard,
        Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.SlashBeforeResponse)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
}

internal sealed class ResolveShiTzZhenfengDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShiTzZhenfeng;
    public override ISkillProgramEffectHandler Handler { get; } = new ShiTzHandler(SkillProgramEffectOp.ResolveShiTzZhenfeng);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Recover,
        static (_, context) => context.PublicControlValue(1));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard,
        Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
}

internal sealed class ShiTzHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) => op switch
    {
        SkillProgramEffectOp.ResolveShiTzHanzhan => host.GetCapability<IShiTzProgramHost>().ResolveHanzhan(frame),
        SkillProgramEffectOp.ResolveShiTzRangeRecord => host.GetCapability<IShiTzProgramHost>().ResolveRangeRecord(frame),
        SkillProgramEffectOp.ResolveShiTzZhanlie => host.GetCapability<IShiTzProgramHost>().ResolveZhanlie(frame),
        SkillProgramEffectOp.ResolveShiTzRespondGate => host.GetCapability<IShiTzProgramHost>().ResolveRespondGate(frame),
        _ => host.GetCapability<IShiTzProgramHost>().ResolveZhenfeng(frame),
    };
}
