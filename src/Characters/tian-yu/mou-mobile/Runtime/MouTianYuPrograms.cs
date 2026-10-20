using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum MouTianYuStage { Branch, Discard, Move, Children, Complete }
public sealed record MouTianYuReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation,
    long WindowId, long? UseId, long? ActionId, int TargetSeat, int Distance, bool Backwater, int TurnOrdinal,
    MouTianYuStage Stage, string? Branch = null, MouTianYuStage AfterChildren = MouTianYuStage.Complete,
    long Before = 0, long After = 0)
{
    private readonly IReadOnlyList<int> _hand = Array.AsReadOnly(Array.Empty<int>()), _selected = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Hand { get => _hand; init => _hand = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouTianYuReceipt? MouTianYu { get; init; }
}
public sealed record MouTianYuStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long WindowId, long? UseId, long? ActionId, int TargetSeat, int Distance, bool Backwater, int TurnOrdinal) : IGameEvent;
public sealed record MouTianYuBranchChosenEvent(long FrameId, string Branch) : IGameEvent;
public sealed record MouTianYuDiscardPaidEvent(long FrameId, int OwnerSeat, int TargetSeat, int Count) : IGameEvent;
public sealed record MouTianYuFieldMovedEvent(long FrameId, int CardId, CardLocation From, CardLocation To) : IGameEvent;
public sealed record MouTianYuMovementSignedEvent(long FrameId, int CardId, CardKind Kind, CardLocation From, CardLocation To,
    CardMoveReason Reason, long Sequence) : IGameEvent;
public sealed record MouTianYuRangeIncreasedEvent(long FrameId, int OwnerSeat, CardConversionSource Source, string GameplayHash, int RangeBefore) : IGameEvent;
public sealed record MouTianYuSlashObtainedEvent(long FrameId, int OwnerSeat, int? CardId) : IGameEvent;
public sealed record MouTianYuCompletedEvent(long FrameId) : IGameEvent;

internal interface IMouTianYuHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal sealed class ResolveMouTianYuZhenxiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouTianYuZhenxi;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized),
            new RequireCardActionRelation(SkillProgramCardActionOwnerRelation.Actor, [CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash])];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.DiscardSelected, static (_, c) => c.PublicControlValue(6));
    public override ISkillProgramEffectHandler Handler { get; } = new MouTianYuHandler(SkillProgramEffectOp.ResolveMouTianYuZhenxi);
}
internal sealed class ResolveMouTianYuYangshiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouTianYuYangshi;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(5));
    public override ISkillProgramEffectHandler Handler { get; } = new MouTianYuHandler(SkillProgramEffectOp.ResolveMouTianYuYangshi);
}
internal sealed class MouTianYuHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) => h.GetCapability<IMouTianYuHost>().Resolve(f, op);
}
internal static class MouTianYuComposition
{
    internal const string Zhenxi = "mou-mobile:zhenxi", Yangshi = "mou-mobile:yangshi";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveMouTianYuZhenxi or SkillProgramEffectOp.ResolveMouTianYuYangshi;
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var reserved = p.Triggers.SelectMany(t => t.Effects).Any(e => Handles(e.Op));
        if (p.Id is not (Zhenxi or Yangshi)) { if (reserved) throw new InvalidOperationException(path + ": mobile Tian Yu's bindings cannot be transplanted."); return; }
        if (p.Triggers is not [var t] || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 ||
            p.BooleanStates.Count != 0 || t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.SingleActionInstance ||
            t.Effects is not [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } e] ||
            (p.Id == Zhenxi ? e.Op != SkillProgramEffectOp.ResolveMouTianYuZhenxi || t.Id != "designated-slash-choice" || !t.Optional ||
                t.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor ||
                t.UsageScope != SkillUsageScope.Turn || t.UsageLimit != 1 || !t.CardKinds.SequenceEqual([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash])
                : e.Op != SkillProgramEffectOp.ResolveMouTianYuYangshi || t.Id != "damage-range-or-slash" || t.Optional ||
                t.Window != SkillProgramTriggerWindow.AfterDamageApplied || t.Subject != SkillProgramTriggerSubject.Owner || t.UsageScope is not null || t.UsageLimit is not null))
            throw new InvalidOperationException(path + ": mobile Tian Yu requires its complete Slash-target choice and native damage range-or-Slash programs.");
    }
}
