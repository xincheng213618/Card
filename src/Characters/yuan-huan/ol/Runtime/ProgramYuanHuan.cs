using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record YuanHuanStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op,
    int ActualTurn, long PhaseInstance, int TargetSeat, long ParentId, long? DamageFrameId, int DamageAmount) : IGameEvent;
// Guess and actual masks stay in the private owning frame; this public fact reveals no unchosen hand names or entities.
public sealed record DeruGuessCommittedEvent(long FrameId) : IGameEvent;
public sealed record YuanHuanStepIssuedEvent(long FrameId, YuanHuanStage Stage, int Index, int SubjectSeat, string Reason,
    long Before, long After, int ActualCount, int? CardId, CardLocation? From, CardLocation? To) : IGameEvent;
public sealed record YuanHuanCompletedEvent(long FrameId) : IGameEvent;
public enum YuanHuanStage { Guess, RecoveryChildren, TakeChildren, FillChildren, SourceDiscard, DiscardChildren, LinjieDrawChildren }
public sealed record YuanHuanMovement(int Index, int SubjectSeat, string Reason, long Before, long After, int ActualCount,
    int? CardId = null, CardLocation? From = null, CardLocation? To = null);
public sealed record YuanHuanReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, int InstructionIndex,
    int ActualTurn, long PhaseInstance, int TargetSeat, long ParentId, long? DamageFrameId, int DamageAmount, YuanHuanStage Stage,
    int? GuessMask = null, int? ActualMask = null, IReadOnlyList<int>? BasicCandidates = null, int RandomIndex = -1,
    CardKind? DiscardedKind = null, CardKind? DiscardedPrintedKind = null, CardKind? TakenKind = null, CardKind? TakenPrintedKind = null, IReadOnlyList<YuanHuanMovement>? Movements = null)
{
    private IReadOnlyList<int>? _basicCandidates = BasicCandidates is null ? null : Array.AsReadOnly(BasicCandidates.ToArray());
    public IReadOnlyList<int>? BasicCandidates { get => _basicCandidates; init => _basicCandidates = value is null ? null : Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<YuanHuanMovement>? _movements = Movements is null ? null : Array.AsReadOnly(Movements.ToArray());
    public IReadOnlyList<YuanHuanMovement>? Movements { get => _movements; init => _movements = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public YuanHuanReceipt? YuanHuan { get; init; }
}
internal interface IYuanHuanHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op); }
internal abstract class YuanHuanDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new YuanHuanHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => Op == SkillProgramEffectOp.ResolveLinjieSourceHandDiscard ? [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)] : [];
}
internal sealed class ResolveDeruBasicNamesDescriptor : YuanHuanDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveDeruBasicNames; }
internal sealed class ResolveLinjieSourceHandDiscardDescriptor : YuanHuanDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveLinjieSourceHandDiscard; }
internal sealed class YuanHuanHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int targetSeat, ISkillProgramEffectHost host) => host.GetCapability<IYuanHuanHost>().Resolve(f, op);
}
internal static class YuanHuanComposition
{
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveDeruBasicNames or SkillProgramEffectOp.ResolveLinjieSourceHandDiscard;
    internal static void Activation(string path, SkillProgramActivation a)
    {
        if (!a.Effects.Any(e => Handles(e.Op))) return;
        if (a.Effects is not [{ Op: SkillProgramEffectOp.ResolveDeruBasicNames, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 1 || a.MaxTargets != 1 || a.TargetKind != SkillProgramTargetKind.OtherLiving || a.UsesPerPhase != 1 || a.UsesPerTurn is not null || a.UsesPerGame is not null || a.Condition.Kind != SkillProgramConditionKind.Always)
            throw new InvalidOperationException(path + ": Deru requires one other living person, no supplied material, and one use per actual Play phase.");
    }
    internal static void Trigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => Handles(e.Op))) return;
        if (t.Effects is not [{ Op: SkillProgramEffectOp.ResolveLinjieSourceHandDiscard, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            t.Window != SkillProgramTriggerWindow.AfterDamageApplied || t.Subject != SkillProgramTriggerSubject.Owner || t.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage ||
            t.Optional || t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.MarkerCost is not null || t.UsageLimit is not null)
            throw new InvalidOperationException(path + ": Linjie requires the actual mandatory whole-damage source hand discard.");
    }
}
