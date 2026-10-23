using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IOlLuShiHost { SkillProgramStepOutcome Begin(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class OlLuShiDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new OlLuShiHandler(op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements => op == SkillProgramEffectOp.ResolveOlLuShiZhuyan
        ? new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard)
        : new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        op == SkillProgramEffectOp.ResolveOlLuShiZhuyan ? [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPhaseEnded)] : [];
}
internal sealed class ResolveOlLuShiZhuyanDescriptor() : OlLuShiDescriptor(SkillProgramEffectOp.ResolveOlLuShiZhuyan);
internal sealed class ResolveOlLuShiLeijieDescriptor() : OlLuShiDescriptor(SkillProgramEffectOp.ResolveOlLuShiLeijie);
internal sealed class OlLuShiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => host.GetCapability<IOlLuShiHost>().Begin(frame, op);
}

public enum OlLuShiStage { Choice, Pool, Ready, Discard, Loss, Drawing, Children, NativeUse, Finished }
public sealed record OlLuShiSnapshotEvent(long BoundaryId, int Turn, int Seat, bool Initial, int? Hp, HappyQiaoDualHealth? Dual, int HandCount) : IGameEvent;
public sealed record OlLuShiStartedEvent(long FrameId, CardConversionSource Source, string Hash, int InstructionIndex, long Parent, SkillProgramEffectOp Operation) : IGameEvent;
public sealed record OlLuShiAdjustmentIssuedEvent(long FrameId, CardConversionSource Source, int TargetSeat, string Item, OlLuShiSnapshotEvent Snapshot) : IGameEvent;
public sealed record OlLuShiPoolAcceptedEvent(long FrameId, int TargetSeat, HappyQiaoPool Pool, int TargetHp, HappyQiaoDualHealth Before) : IGameEvent;
public sealed record OlLuShiMovementEvent(long FrameId, int TargetSeat, bool Draw, int Count, long Before, long After) : IGameEvent;
public sealed record OlLuShiSlashOrigin(long FrameId, CardConversionSource Source, string Hash, int Ordinal, long UseId, long ActionId, int TargetSeat);
public sealed record OlLuShiSlashIssuedEvent(OlLuShiSlashOrigin Origin) : IGameEvent;
public sealed record OlLuShiSlashReturnedEvent(long FrameId, int Ordinal, long UseId, long ActionId) : IGameEvent;
public sealed record OlLuShiReceipt(CardConversionSource Source, string Hash, SkillProgramEffectOp Operation, int InstructionIndex, long Parent, OlLuShiStage Stage)
{
    public int TargetSeat { get; init; } = -1;
    public string? Item { get; init; }
    public OlLuShiSnapshotEvent? Snapshot { get; init; }
    public HappyQiaoPool? Pool { get; init; }
    public int TargetValue { get; init; }
    public int Amount { get; init; }
    public int RequiredCount { get; init; }
    private readonly IReadOnlyList<int> _cards = [];
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    public NativeDrawOutcome? Draw { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public int SlashCount { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public bool NativeReturned { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public OlLuShiReceipt? OlLuShi { get; init; } }
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public OlLuShiSlashOrigin? OlLuShiLeijie { get; init; } }

internal static class OlLuShiComposition
{
    [ProgramCatalogValidation(42700)] internal static void Validate(string path, SkillProgram program)
    {
        var effects = program.Triggers.SelectMany(t => t.Effects).Concat(program.Activations.SelectMany(a => a.Effects));
        if (!effects.Any(e => e.Op is SkillProgramEffectOp.ResolveOlLuShiZhuyan or SkillProgramEffectOp.ResolveOlLuShiLeijie)) return;
        var valid = program.Id switch
        {
            "ol:zhuyan" => program.Activations.Count == 0 && program.Triggers is [{ Id: "discard-ending", Window: SkillProgramTriggerWindow.DiscardPhaseEnded,
                Subject: SkillProgramTriggerSubject.Owner, TurnOwnerScope: SkillProgramTurnOwnerScope.Own, AllowOwnDiscardPhaseEnded: true, Optional: true,
                UsageScope: null, MarkerCost: null, Condition.Kind: SkillProgramTriggerConditionKind.Always } trigger] &&
                trigger.Effects is [{ Op: SkillProgramEffectOp.ResolveOlLuShiZhuyan, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }],
            "ol:leijie" => program.Triggers.Count == 0 && program.Activations is [{ Id: "judge-target", MinCards: 0, MaxCards: 0, MinTargets: 1, MaxTargets: 1,
                TargetKind: SkillProgramTargetKind.AnyLiving, UsesPerPhase: 1, UsesPerTurn: null, UsesPerGame: null } activation] &&
                activation.Effects is [
                    { Op: SkillProgramEffectOp.StartJudgment, Target: SkillProgramEffectTarget.SelectedTarget, JudgmentReason: "skill.ol.lushi.leijie", ResultBind: "leijie-card", OutcomeBind: "leijie-result" },
                    { Op: SkillProgramEffectOp.MoveBoundCards, SourceBind: "leijie-card", Destination: SkillProgramCardDestination.DiscardPile },
                    { Op: SkillProgramEffectOp.ResolveOlLuShiLeijie, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }],
            _ => false
        };
        if (!valid || program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": OL Lu Shi requires her complete remade discard-ending adjustment and once-per-play finalized judgment/two-native-Slash pipeline.");
    }
}
