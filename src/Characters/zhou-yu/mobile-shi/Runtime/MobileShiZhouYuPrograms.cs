using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record ShiZhouYuChiyunPhaseSeenEvent(int TurnNumber, int Phase, int OwnerSeat) : IGameEvent;
public sealed record ShiZhouYuChiyunGivenEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int TargetSeat, IReadOnlyList<int> CardIds, IReadOnlyList<bool> GivenRed) : IGameEvent;
public sealed record ShiZhouYuChiyunOptionEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int TargetSeat, int Option, IReadOnlyList<int> RevealedCardIds) : IGameEvent;
public sealed record ShiZhouYuYanhuiShownEvent(int TurnNumber, int HolderSeat, int CardId, bool Discarded) : IGameEvent;
public sealed record ShiZhouYuYanhuiEndingChosenEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int Option, int? TargetSeat, int DrawCount) : IGameEvent;
public sealed record ShiZhouYuFentaoChosenEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, long AttackResolutionId, int TargetSeat, int Option, IReadOnlyList<int> DiscardedCardIds) : IGameEvent;
public sealed record ShiZhouYuFentaoRechainedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int TargetSeat) : IGameEvent;
public sealed record ShiZhouYuXiongziActivatedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int OwnerSeat, int KeptOption) : IGameEvent;

/// <summary>Per-operation receipt of the running 势周瑜 program instruction.</summary>
public sealed record ShiZhouYuReceipt(string Operation, int ActualTurn, int Stage = 0, int TargetSeat = -1)
{
    public CardConversionSource? Source { get; init; }
    public string GameplayHash { get; init; } = "";
    public long AttackResolutionId { get; init; }
    public int Option { get; init; }
    public IReadOnlyList<int> PickedCards { get; init; } = Array.Empty<int>();
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShiZhouYuReceipt? ShiZhouYu { get; init; }
}

internal static class ShiZhouYuComposition
{
    internal const string ChiyunSkill = "mobile:shi-chiyun";
    internal const string YanhuiSkill = "mobile:shi-yanhui";
    internal const string FentaoSkill = "mobile:shi-fentao";
    internal const string XiongziSkill = "mobile:shi-xiongzi";
    internal const string ChiyunTrigger = "chiyun-gained";
    internal const string YanhuiTargetTrigger = "yanhui-targeted";
    internal const string YanhuiEndingTrigger = "yanhui-ending";
    internal const string FentaoFireTrigger = "fentao-fire";
    internal const string FentaoRechainTrigger = "fentao-rechain";
    internal const string XiongziTrigger = "xiongzi-prepare";
    internal const string ProgramAction = "mobile-shi-zhou-yu";
    internal const string ChiyunGiveReason = "skill-program.mobile:shi-chiyun.give";
    internal const string ChiyunDrawReason = "skill-program.mobile:shi-chiyun.draw";
    internal const string YanhuiShowBind = "shown";
    internal const string YanhuiDiscardReason = "skill-program.mobile:shi-yanhui.discard";
    internal const string YanhuiDrawReason = "skill-program.mobile:shi-yanhui.draw";
    internal const string FentaoDiscardReason = "skill-program.mobile:shi-fentao.discard";
    internal const string XiongziDrawReason = "skill-program.mobile:shi-xiongzi.draw";
    internal const int ChiyunStageTarget = 0;
    internal const int ChiyunStageCards = 3;
    internal const int ChiyunStageGiveIssued = 6;
    internal const int ChiyunStageOption = 9;
    internal const int ChiyunStageResolveDamage = 12;
    internal const int ChiyunStageFinished = 15;
    internal const int YanhuiShowStagePick = 21;
    internal const int YanhuiStageEndingChoice = 0;
    internal const int YanhuiStageEndingDamage = 10;
    internal const int YanhuiResultDiscardIssued = 3;
    internal const int FentaoStageChoice = 0;
    internal const int FentaoStageDiscardPick = 3;
    internal const int FentaoStageDiscardIssued = 6;
    internal const int FentaoStageFinished = 9;
    internal const int XiongziStageChoice = 0;
    internal const int XiongziStageFinished = 10;

    [ProgramCatalogValidation(67800)]
    internal static void ValidateProgram(string path, SkillProgram program)
    {
        if (program.Id == ChiyunSkill)
        {
            if (program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.DamageModifiers.Count != 0 ||
                program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.HpSkillGrants.Count != 0 ||
                program.Contributions.Count != 0 || program.BooleanStates.Count != 0 || program.Activations.Count != 0 ||
                program.Triggers.Count != 1 ||
                program.Triggers[0] is not { Id: ChiyunTrigger, Window: SkillProgramTriggerWindow.CardsGained,
                    Subject: SkillProgramTriggerSubject.Owner, Optional: false, IsSkillInvocation: false } ||
                !program.Triggers[0].DestinationZones.SequenceEqual([CardZoneKind.Hand]) ||
                program.Triggers[0].MovementOccurrence != SkillProgramMovementOccurrence.PerBatch ||
                !program.Triggers[0].IgnoreOwnSkillMovements ||
                program.Triggers[0].Effects is not [{ Op: SkillProgramEffectOp.ResolveShiZhouYuChiyun, Target: SkillProgramEffectTarget.Owner },
                    { Op: SkillProgramEffectOp.ResolveShiZhouYuChiyunOption, Target: SkillProgramEffectTarget.Owner }])
                throw new InvalidOperationException(path + ": Chiyun requires exactly its per-phase first-gain trigger.");
            return;
        }
        if (program.Id == YanhuiSkill)
        {
            if (program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.DamageModifiers.Count != 0 ||
                program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.HpSkillGrants.Count != 0 ||
                program.Contributions.Count != 0 || program.BooleanStates.Count != 0 || program.Activations.Count != 0 ||
                program.Triggers.Count != 2 ||
                program.Triggers[0] is not { Id: YanhuiTargetTrigger, Window: SkillProgramTriggerWindow.CardUseTargetsFinalized,
                    OwnerRelation: SkillProgramCardActionOwnerRelation.Actor, Optional: false, IsSkillInvocation: false } ||
                program.Triggers[0].Effects is not [{ Op: SkillProgramEffectOp.ResolveShiZhouYuYanhuiShow, Target: SkillProgramEffectTarget.Owner }] ||
                program.Triggers[1] is not { Id: YanhuiEndingTrigger, Window: SkillProgramTriggerWindow.PlayEnding,
                    Subject: SkillProgramTriggerSubject.Owner, Optional: false, IsSkillInvocation: false } ||
                program.Triggers[1].Effects is not [{ Op: SkillProgramEffectOp.ResolveShiZhouYuYanhuiEnding, Target: SkillProgramEffectTarget.Owner }])
                throw new InvalidOperationException(path + ": Yanhui requires its targeted-show pair plus the play-ending choice.");
            return;
        }
        if (program.Id == FentaoSkill)
        {
            if (program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.DamageModifiers.Count != 0 ||
                program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.HpSkillGrants.Count != 0 ||
                program.Contributions.Count != 0 || program.BooleanStates.Count != 0 || program.Activations.Count != 0 ||
                program.Triggers.Count != 2 ||
                program.Triggers[0] is not { Id: FentaoFireTrigger, Window: SkillProgramTriggerWindow.BeforeDamageApplied,
                    Subject: SkillProgramTriggerSubject.Any, Optional: false, IsSkillInvocation: false } ||
                program.Triggers[0].Effects is not [{ Op: SkillProgramEffectOp.ResolveShiZhouYuFentao, Target: SkillProgramEffectTarget.Owner }] ||
                program.Triggers[1] is not { Id: FentaoRechainTrigger, Window: SkillProgramTriggerWindow.AfterDamageApplied,
                    Subject: SkillProgramTriggerSubject.Any, DamageOccurrence: SkillProgramDamageOccurrence.PerDamage,
                    Optional: false, IsSkillInvocation: false } ||
                program.Triggers[1].Effects is not [{ Op: SkillProgramEffectOp.ResolveShiZhouYuFentaoRechain, Target: SkillProgramEffectTarget.Owner }])
                throw new InvalidOperationException(path + ": Fentao requires its fire-damage choice plus the settle re-chain trigger.");
            return;
        }
        if (program.Id != XiongziSkill) return;
        if (program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.DamageModifiers.Count != 0 ||
            program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.HpSkillGrants.Count != 0 ||
            program.Contributions.Count != 0 || program.BooleanStates.Count != 0 || program.Activations.Count != 0 ||
            program.Triggers.Count != 1 ||
            program.Triggers[0] is not { Id: XiongziTrigger, Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,
                Subject: SkillProgramTriggerSubject.Owner, Optional: false, IsSkillInvocation: false } ||
            program.Triggers[0].Effects is not [{ Op: SkillProgramEffectOp.ResolveShiZhouYuXiongzi, Target: SkillProgramEffectTarget.Owner }])
            throw new InvalidOperationException(path + ": Xiongzi requires exactly its preparation-phase limited trigger.");
    }
}

internal interface IShiZhouYuHost
{
    SkillProgramStepOutcome ResolveChiyun(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveChiyunOption(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveYanhuiShow(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveYanhuiEnding(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveFentao(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveFentaoRechain(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveXiongzi(ProgramSkillFrame frame);
}

internal sealed class ResolveShiZhouYuChiyunDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShiZhouYuChiyun;
    public override ISkillProgramEffectHandler Handler { get; } = new ShiZhouYuHandler(SkillProgramEffectOp.ResolveShiZhouYuChiyun);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(1));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard,
        Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsGained)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
}

internal sealed class ResolveShiZhouYuChiyunOptionDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShiZhouYuChiyunOption;
    public override ISkillProgramEffectHandler Handler { get; } = new ShiZhouYuHandler(SkillProgramEffectOp.ResolveShiZhouYuChiyunOption);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (_, context) => context.PublicControlValue(1));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard,
        Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsGained)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
}

internal sealed class ResolveShiZhouYuYanhuiShowDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShiZhouYuYanhuiShow;
    public override ISkillProgramEffectHandler Handler { get; } = new ShiZhouYuHandler(SkillProgramEffectOp.ResolveShiZhouYuYanhuiShow);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(1));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard,
        Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
}

internal sealed class ResolveShiZhouYuYanhuiEndingDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShiZhouYuYanhuiEnding;
    public override ISkillProgramEffectHandler Handler { get; } = new ShiZhouYuHandler(SkillProgramEffectOp.ResolveShiZhouYuYanhuiEnding);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(1));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard,
        Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayEnding)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
}

internal sealed class ResolveShiZhouYuFentaoDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShiZhouYuFentao;
    public override ISkillProgramEffectHandler Handler { get; } = new ShiZhouYuHandler(SkillProgramEffectOp.ResolveShiZhouYuFentao);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (_, context) => context.PublicControlValue(1));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard,
        Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
}

internal sealed class ResolveShiZhouYuFentaoRechainDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShiZhouYuFentaoRechain;
    public override ISkillProgramEffectHandler Handler { get; } = new ShiZhouYuHandler(SkillProgramEffectOp.ResolveShiZhouYuFentaoRechain);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(1));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard,
        Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
}

internal sealed class ResolveShiZhouYuXiongziDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShiZhouYuXiongzi;
    public override ISkillProgramEffectHandler Handler { get; } = new ShiZhouYuHandler(SkillProgramEffectOp.ResolveShiZhouYuXiongzi);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(1));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard,
        Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
}

internal sealed class ShiZhouYuHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) => op switch
    {
        SkillProgramEffectOp.ResolveShiZhouYuChiyun => host.GetCapability<IShiZhouYuHost>().ResolveChiyun(frame),
        SkillProgramEffectOp.ResolveShiZhouYuChiyunOption => host.GetCapability<IShiZhouYuHost>().ResolveChiyunOption(frame),
        SkillProgramEffectOp.ResolveShiZhouYuYanhuiShow => host.GetCapability<IShiZhouYuHost>().ResolveYanhuiShow(frame),
        SkillProgramEffectOp.ResolveShiZhouYuYanhuiEnding => host.GetCapability<IShiZhouYuHost>().ResolveYanhuiEnding(frame),
        SkillProgramEffectOp.ResolveShiZhouYuFentao => host.GetCapability<IShiZhouYuHost>().ResolveFentao(frame),
        SkillProgramEffectOp.ResolveShiZhouYuFentaoRechain => host.GetCapability<IShiZhouYuHost>().ResolveFentaoRechain(frame),
        _ => host.GetCapability<IShiZhouYuHost>().ResolveXiongzi(frame),
    };
}
