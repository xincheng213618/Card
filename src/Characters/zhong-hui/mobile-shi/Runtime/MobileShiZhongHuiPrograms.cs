using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record ShiZhHuiSiziActivatedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int Ordinal, int Consumed, int OwnerHp) : IGameEvent;
public sealed record ShiZhHuiSiziElapsedEvent(int ActivationOrdinal, int Elapsed) : IGameEvent;
public sealed record ShiZhHuiSiziExpiredEvent(int ActivationOrdinal, string Cause) : IGameEvent;
public sealed record ShiZhHuiSlashUsedEvent(int TurnNumber, int TurnOwnerSeat, int ActorSeat) : IGameEvent;
public sealed record ShiZhHuiDeathRecordedEvent(int RoundNumber, int VictimSeat) : IGameEvent;
public sealed record ShiZhHuiXiezhiOverflowEvent(long FrameId, int OwnerSeat, int Excess, int Total) : IGameEvent;
public sealed record ShiZhHuiYunanAwakenedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int VictimSeat, int Level, string FactionId) : IGameEvent;

public sealed record ShiZhHuiReceipt(string Operation, int ActualTurn, int Stage = 0, int TargetSeat = -1)
{
    public CardConversionSource? Source { get; init; }
    public string GameplayHash { get; init; } = "";
    public int Consumed { get; init; }
    public int Ordinal { get; init; }
    public long DyingFrameId { get; init; }
    public IReadOnlyList<int>? Victims { get; init; }
    public int VictimCursor { get; init; }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShiZhHuiReceipt? ShiZhHui { get; init; }
}

internal static class ShiZhHuiComposition
{
    internal const string SiziSkill = "mobile:shi-sizi";
    internal const string XiezhiSkill = "mobile:shi-xiezhi";
    internal const string YunanSkill = "mobile:shi-yunan";
    internal const string KeChangSkill = "mobile:shi-kechang";
    internal const string SiziActivation = "burn-charge";
    internal const string SiziEndingTrigger = "sizi-turn-ending";
    internal const string XiezhiTrigger = "health-gain";
    internal const string YunanTrigger = "awaken-on-dying";
    internal const string SiziProgramAction = "mobile-shi-sizi";
    internal const string SiziEndingDrawReason = "skill-program.mobile:shi-sizi.ending-draw";
    internal const string SiziEndingLossReason = "skill-program.mobile:shi-sizi.ending-loss";
    internal const string SiziChargeReason = "skill-program.mobile:shi-sizi.consume";
    internal const string XiezhiChargeReason = "skill-program.mobile:shi-xiezhi.gain";
    internal const int ChargeCapacity = 4;

    [ProgramCatalogValidation(67700)]
    internal static void ValidateProgram(string path, SkillProgram program)
    {
        if (program.Id == SiziSkill)
        {
            if (program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.DamageModifiers.Count != 0 ||
                program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.HpSkillGrants.Count != 0 ||
                program.Contributions.Count != 0 || program.BooleanStates.Count != 0 ||
                program.Activations is not [{ Id: SiziActivation, MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0,
                    TargetKind: SkillProgramTargetKind.AnyLiving, UsesPerPhase: 1, UsesPerTurn: null, UsesPerGame: null }] ||
                program.Activations[0].Effects is not [{ Op: SkillProgramEffectOp.ResolveShiZhHuiSizi, Target: SkillProgramEffectTarget.Owner }] ||
                program.Triggers is not [{ Id: SiziEndingTrigger, Window: SkillProgramTriggerWindow.TurnEnding,
                    Subject: SkillProgramTriggerSubject.Owner, TurnOwnerScope: SkillProgramTurnOwnerScope.AnyLiving,
                    Optional: false, IsSkillInvocation: false }] ||
                program.Triggers[0].Effects is not [{ Op: SkillProgramEffectOp.ResolveShiZhHuiSiziEnding, Target: SkillProgramEffectTarget.Owner }])
                throw new InvalidOperationException(path + ": Sizi requires exactly its burn-charge activation plus the foreign turn-ending trigger.");
            return;
        }
        if (program.Id == XiezhiSkill)
        {
            if (program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.DamageModifiers.Count != 0 ||
                program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.HpSkillGrants.Count != 0 ||
                program.Contributions.Count != 0 || program.BooleanStates.Count != 0 || program.Activations.Count != 0 ||
                program.Triggers is not [{ Id: XiezhiTrigger, Window: SkillProgramTriggerWindow.AfterHealthChanged,
                    Subject: SkillProgramTriggerSubject.Owner, Optional: false, IsSkillInvocation: false }] ||
                program.Triggers[0].Effects is not [{ Op: SkillProgramEffectOp.ResolveShiZhHuiXiezhi, Target: SkillProgramEffectTarget.Owner }])
                throw new InvalidOperationException(path + ": Xiezhi requires exactly its locked after-health-change trigger.");
            return;
        }
        if (program.Id == YunanSkill)
        {
            if (program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.DamageModifiers.Count != 0 ||
                program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.HpSkillGrants.Count != 0 ||
                program.Contributions.Count != 0 || program.BooleanStates.Count != 0 || program.Activations.Count != 0 ||
                program.Triggers is not [{ Id: YunanTrigger, Window: SkillProgramTriggerWindow.DyingEntering,
                    Subject: SkillProgramTriggerSubject.Any, Optional: false, IsSkillInvocation: false }] ||
                program.Triggers[0].Effects is not [{ Op: SkillProgramEffectOp.ResolveShiZhHuiYunan, Target: SkillProgramEffectTarget.Owner }])
                throw new InvalidOperationException(path + ": Yunan requires exactly its dying-entry awakening trigger.");
            return;
        }
        if (program.Id != KeChangSkill) return;
        if (!program.LordSkillProjection || program.ViewAs.Count != 0 || program.Modifiers.Count != 0 ||
            program.DamageModifiers.Count != 0 || program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 ||
            program.HpSkillGrants.Count != 0 || program.Contributions.Count != 0 || program.BooleanStates.Count != 0 ||
            program.Activations.Count != 0 || program.Triggers.Count != 0)
            throw new InvalidOperationException(path + ": Kechang is a locked lord passive evaluated by its runtime rules.");
    }
}

internal interface IShiZhHuiHost
{
    SkillProgramStepOutcome ResolveSizi(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveSiziEnding(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveXiezhi(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveYunan(ProgramSkillFrame frame);
}

internal sealed class ResolveShiZhHuiSiziDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShiZhHuiSizi;
    public override ISkillProgramEffectHandler Handler { get; } = new ShiZhHuiHandler(SkillProgramEffectOp.ResolveShiZhHuiSizi);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(1));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard,
        Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
}

internal sealed class ResolveShiZhHuiSiziEndingDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShiZhHuiSiziEnding;
    public override ISkillProgramEffectHandler Handler { get; } = new ShiZhHuiHandler(SkillProgramEffectOp.ResolveShiZhHuiSiziEnding);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(1));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard,
        Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
}

internal sealed class ResolveShiZhHuiXiezhiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShiZhHuiXiezhi;
    public override ISkillProgramEffectHandler Handler { get; } = new ShiZhHuiHandler(SkillProgramEffectOp.ResolveShiZhHuiXiezhi);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(1));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard,
        Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterHealthChanged)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
}

internal sealed class ResolveShiZhHuiYunanDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShiZhHuiYunan;
    public override ISkillProgramEffectHandler Handler { get; } = new ShiZhHuiHandler(SkillProgramEffectOp.ResolveShiZhHuiYunan);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Recover,
        static (_, context) => context.PublicControlValue(1));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard,
        Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DyingEntering)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
}

internal sealed class ShiZhHuiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) => op switch
    {
        SkillProgramEffectOp.ResolveShiZhHuiSizi => host.GetCapability<IShiZhHuiHost>().ResolveSizi(frame),
        SkillProgramEffectOp.ResolveShiZhHuiSiziEnding => host.GetCapability<IShiZhHuiHost>().ResolveSiziEnding(frame),
        SkillProgramEffectOp.ResolveShiZhHuiXiezhi => host.GetCapability<IShiZhHuiHost>().ResolveXiezhi(frame),
        _ => host.GetCapability<IShiZhHuiHost>().ResolveYunan(frame),
    };
}
