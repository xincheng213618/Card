using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record ShiCaoShuangSheQuanRoundTriggerEvent(int Round, string WindowKind, int OwnerSeat) : IGameEvent;
public sealed record ShiCaoShuangDianYiRoundTriggerEvent(int Round, string WindowKind, int OwnerSeat) : IGameEvent;
public sealed record ShiCaoShuangJianZhuanTimingEvent(long FrameId, int OwnerSeat, string SkillId, string WindowKind, int Ordinal) : IGameEvent;
public sealed record ShiCaoShuangSheQuanMarkedEvent(long FrameId, int CardId, int HolderSeat, int Round) : IGameEvent;
public sealed record ShiCaoShuangDianYiGainEvent(long FrameId, int Round, int Count, int Total) : IGameEvent;
public sealed record ShiCaoShuangOwnerCardUsedEvent(int TurnNumber, int CardId) : IGameEvent;
public sealed record ShiCaoShuangMarkedCardDiscardedEvent(int TurnNumber, int CardId, int HolderSeat) : IGameEvent;
public sealed record ShiCaoShuangNiZunStashedEvent(long FrameId, int CardId, int TurnNumber) : IGameEvent;
public sealed record ShiCaoShuangJianZhuanAwakenedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, string LostSkillId, int TimingCount, int NewMaxHp) : IGameEvent;
public sealed record ShiCaoShuangNiZunUsedEvent(long FrameId, int CardId, int Round, int Count) : IGameEvent;
public sealed record ShiCaoShuangNiZunPenaltyConsumedEvent(int Round, int Count) : IGameEvent;

public sealed record ShiCaoShuangReceipt(string Operation, int ActualTurn, int Stage = 0)
{
    public CardConversionSource? Source { get; init; }
    public string GameplayHash { get; init; } = "";
    public string WindowKind { get; init; } = "";
    public int Cursor { get; init; }
    public int HolderSeat { get; init; } = -1;
    public int Round { get; init; }
    public IReadOnlyList<int> Holders { get; init; } = [];
    public IReadOnlyList<int> SelectedIds { get; init; } = [];
    public bool PendingHpLoss { get; init; }
    public bool Skip { get; init; }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShiCaoShuangReceipt? ShiCaoShuang { get; init; }
}

internal static class ShiCaoShuangComposition
{
    internal const string SheQuanSkill = "mobile:shi-shequan";
    internal const string DianYiSkill = "mobile:shi-dianyi";
    internal const string JianZhuanSkill = "mobile:shi-jianzhuan";
    internal const string NiZunSkill = "mobile:shi-nizun";
    internal const string SheQuanCausedTrigger = "shequan-caused";
    internal const string SheQuanTakenTrigger = "shequan-taken";
    internal const string SheQuanDyingTrigger = "shequan-dying";
    internal const string DianYiCausedTrigger = "dianyi-caused";
    internal const string DianYiTakenTrigger = "dianyi-taken";
    internal const string DianYiDyingTrigger = "dianyi-dying";
    internal const string JianZhuanCausedTrigger = "jianzhuan-caused";
    internal const string JianZhuanTakenTrigger = "jianzhuan-taken";
    internal const string JianZhuanDyingTrigger = "jianzhuan-dying";
    internal const string NiZunUseActivation = "nizun-use";
    internal const string NiZunEndingTrigger = "nizun-turn-ending";
    internal const string ProgramAction = "mobile-shi-cao-shuang";
    internal const string NiZunPileId = "mobile-shi-cao-shuang.nizun";
    internal const string SheQuanMarkReason = "skill-program.mobile:shi-shequan.mark";
    internal const string DianYiGainReason = "skill-program.mobile:shi-dianyi.gain";
    internal const string NiZunStashReason = "skill-program.mobile:shi-nizun.stash";
    internal const string NiZunUseReason = "skill-program.mobile:shi-nizun.use";
    internal const int TimingCapacity = 3;

    internal static string WindowKind(string triggerId) => triggerId switch
    {
        SheQuanCausedTrigger or DianYiCausedTrigger or JianZhuanCausedTrigger => "caused",
        SheQuanTakenTrigger or DianYiTakenTrigger or JianZhuanTakenTrigger => "taken",
        _ => "dying"
    };

    [ProgramCatalogValidation(67900)]
    internal static void ValidateProgram(string path, SkillProgram program)
    {
        var triggerShape = (string caused, string taken, string dying, SkillProgramEffectOp op) =>
            program.Triggers is [{ Id: var first }, { Id: var second }, { Id: var third }] &&
            first == caused && second == taken && third == dying &&
            program.Triggers[0].Effects is [{ Op: var firstOp, Target: SkillProgramEffectTarget.Owner }] &&
            program.Triggers[1].Effects is [{ Op: var secondOp, Target: SkillProgramEffectTarget.Owner }] &&
            program.Triggers[2].Effects is [{ Op: var thirdOp, Target: SkillProgramEffectTarget.Owner }] &&
            firstOp == op && secondOp == op && thirdOp == op;
        if (program.Id == SheQuanSkill)
        {
            if (program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.DamageModifiers.Count != 0 ||
                program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.HpSkillGrants.Count != 0 ||
                program.Contributions.Count != 0 || program.BooleanStates.Count != 0 || program.Activations.Count != 0 ||
                program.Triggers.Any(trigger => trigger.Optional || trigger.IsSkillInvocation) ||
                !triggerShape(SheQuanCausedTrigger, SheQuanTakenTrigger, SheQuanDyingTrigger, SkillProgramEffectOp.ResolveShiCaoShuangSheQuan))
                throw new InvalidOperationException(path +
                    ": SheQuan requires exactly its three locked damage/dying triggers resolving the owner marking operation.");
            return;
        }
        if (program.Id == DianYiSkill)
        {
            if (program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.DamageModifiers.Count != 0 ||
                program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.HpSkillGrants.Count != 0 ||
                program.Contributions.Count != 0 || program.BooleanStates.Count != 0 || program.Activations.Count != 0 ||
                program.Triggers.Any(trigger => trigger.Optional || trigger.IsSkillInvocation) ||
                !triggerShape(DianYiCausedTrigger, DianYiTakenTrigger, DianYiDyingTrigger, SkillProgramEffectOp.ResolveShiCaoShuangDianYi))
                throw new InvalidOperationException(path +
                    ": DianYi requires exactly its three locked damage/dying triggers resolving the owner take operation.");
            return;
        }
        if (program.Id == JianZhuanSkill)
        {
            if (program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.DamageModifiers.Count != 0 ||
                program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.HpSkillGrants.Count != 0 ||
                program.Contributions.Count != 0 || program.BooleanStates.Count != 0 || program.Activations.Count != 0 ||
                program.Triggers.Any(trigger => trigger.Optional || trigger.IsSkillInvocation) ||
                !triggerShape(JianZhuanCausedTrigger, JianZhuanTakenTrigger, JianZhuanDyingTrigger, SkillProgramEffectOp.ResolveShiCaoShuangJianZhuan))
                throw new InvalidOperationException(path +
                    ": JianZhuan requires exactly its three locked first-time damage/dying triggers resolving the timing operation.");
            return;
        }
        if (program.Id != NiZunSkill) return;
        if (program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.DamageModifiers.Count != 0 ||
            program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.HpSkillGrants.Count != 0 ||
            program.Contributions.Count != 0 || program.BooleanStates.Count != 0 ||
            program.Activations is not [{ Id: NiZunUseActivation, MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0,
                TargetKind: SkillProgramTargetKind.AnyLiving, UsesPerTurn: 1, UsesPerPhase: null, UsesPerGame: null }] ||
            program.Activations[0].Effects is not [{ Op: SkillProgramEffectOp.ResolveShiCaoShuangNiZunUse, Target: SkillProgramEffectTarget.Owner }] ||
            program.Triggers is not [{ Id: NiZunEndingTrigger, Window: SkillProgramTriggerWindow.TurnEnding,
                Subject: SkillProgramTriggerSubject.Owner, TurnOwnerScope: SkillProgramTurnOwnerScope.AnyLiving,
                Optional: false, IsSkillInvocation: false }] ||
            program.Triggers[0].Effects is not [{ Op: SkillProgramEffectOp.ResolveShiCaoShuangNiZunStash, Target: SkillProgramEffectTarget.Owner }])
            throw new InvalidOperationException(path +
                ": NiZun requires exactly its once-per-turn activation plus the foreign turn-ending stash trigger.");
    }
}

internal interface IShiCaoShuangHost
{
    SkillProgramStepOutcome ResolveSheQuan(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveDianYi(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveJianZhuan(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveNiZunStash(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveNiZunUse(ProgramSkillFrame frame);
}

internal sealed class ResolveShiCaoShuangSheQuanDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShiCaoShuangSheQuan;
    public override ISkillProgramEffectHandler Handler { get; } = new ShiCaoShuangHandler(SkillProgramEffectOp.ResolveShiCaoShuangSheQuan);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(1));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard,
        Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindows([SkillProgramTriggerWindow.AfterDamageApplied, SkillProgramTriggerWindow.DyingEntering])];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
}

internal sealed class ResolveShiCaoShuangDianYiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShiCaoShuangDianYi;
    public override ISkillProgramEffectHandler Handler { get; } = new ShiCaoShuangHandler(SkillProgramEffectOp.ResolveShiCaoShuangDianYi);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(1));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard,
        Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindows([SkillProgramTriggerWindow.AfterDamageApplied, SkillProgramTriggerWindow.DyingEntering])];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
}

internal sealed class ResolveShiCaoShuangJianZhuanDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShiCaoShuangJianZhuan;
    public override ISkillProgramEffectHandler Handler { get; } = new ShiCaoShuangHandler(SkillProgramEffectOp.ResolveShiCaoShuangJianZhuan);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Recover,
        static (_, context) => context.PublicControlValue(1));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard,
        Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindows([SkillProgramTriggerWindow.AfterDamageApplied, SkillProgramTriggerWindow.DyingEntering])];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
}

internal sealed class ResolveShiCaoShuangNiZunStashDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShiCaoShuangNiZunStash;
    public override ISkillProgramEffectHandler Handler { get; } = new ShiCaoShuangHandler(SkillProgramEffectOp.ResolveShiCaoShuangNiZunStash);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(1));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard,
        Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
}

internal sealed class ResolveShiCaoShuangNiZunUseDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShiCaoShuangNiZunUse;
    public override ISkillProgramEffectHandler Handler { get; } = new ShiCaoShuangHandler(SkillProgramEffectOp.ResolveShiCaoShuangNiZunUse);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(1));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard,
        Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
}

internal sealed class ShiCaoShuangHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) => op switch
    {
        SkillProgramEffectOp.ResolveShiCaoShuangSheQuan => host.GetCapability<IShiCaoShuangHost>().ResolveSheQuan(frame),
        SkillProgramEffectOp.ResolveShiCaoShuangDianYi => host.GetCapability<IShiCaoShuangHost>().ResolveDianYi(frame),
        SkillProgramEffectOp.ResolveShiCaoShuangJianZhuan => host.GetCapability<IShiCaoShuangHost>().ResolveJianZhuan(frame),
        SkillProgramEffectOp.ResolveShiCaoShuangNiZunStash => host.GetCapability<IShiCaoShuangHost>().ResolveNiZunStash(frame),
        _ => host.GetCapability<IShiCaoShuangHost>().ResolveNiZunUse(frame),
    };
}
