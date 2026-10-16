using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum YangFuStage { Targets, DrawChildren, EquipmentUse, EquipmentChildren, SlashUse, SlashChildren }
public sealed record YangFuNativeOrigin(long ProgramFrameId, CardConversionSource Source, string GameplayHash, int ActualTurn,
    long UseId, long ActionId, int ActorSeat, int ProviderSeat, int CardId, CardKind Kind, long Before);
public sealed record YangFuStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op,
    int ActualTurn, long ParentId, int TargetSeat, int X, int? Ordinal, long? UseId, long? ActionId, int OriginalTargetCount) : IGameEvent;
public sealed record BingzhongOriginalTargetEvent(long FrameId, int Index, int Seat) : IGameEvent;
public sealed record BingzhongTargetSelectedEvent(long FrameId, int Index, int Seat) : IGameEvent;
public sealed record BingzhongTargetsAppliedEvent(long FrameId, long UseId, long ActionId, CardConversionSource Source, string GameplayHash,
    int X, int Ordinal, int OriginalActor, int ProviderSeat, int OriginalCount, int AddedCount) : IGameEvent;
public sealed record BingzhongRedirectedEvent(long FrameId, long UseId, long ActionId, CardConversionSource Source, string GameplayHash,
    int InstructionIndex, int TargetIndex, int PreviousSeat, int Seat) : IGameEvent;
public sealed record BingzhongDrawPaidEvent(long FrameId, int X, int ActualCount, long Before, long After) : IGameEvent;
public sealed record SuyiNativeIssuedEvent(YangFuNativeOrigin Origin) : IGameEvent;
public sealed record SuyiNativeReturnedEvent(YangFuNativeOrigin Origin) : IGameEvent;
public sealed record SuyiNoEquipmentEvent(long FrameId, int TargetSeat) : IGameEvent;
public sealed record SuyiSlashQuotaPaidEvent(long FrameId, long UseId, int ActorSeat) : IGameEvent;
public sealed record YangFuCompletedEvent(long FrameId) : IGameEvent;
public sealed record BingzhongTargetTail(long FrameId, long UseId, long ActionId, CardConversionSource Source, string GameplayHash,
    int X, int Ordinal, int OriginalActor, int ProviderSeat, IReadOnlyList<int> OriginalTargets, IReadOnlyList<int> AddedTargets)
{
    private IReadOnlyList<int> _original = Array.AsReadOnly(OriginalTargets.ToArray());
    public IReadOnlyList<int> OriginalTargets { get => _original; init => _original = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<int> _added = Array.AsReadOnly(AddedTargets.ToArray());
    public IReadOnlyList<int> AddedTargets { get => _added; init => _added = Array.AsReadOnly(value.ToArray()); }
}
public sealed record YangFuReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, int ActualTurn,
    long ParentId, int TargetSeat, int X,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Ordinal,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? UseId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ActionId, YangFuStage Stage,
    IReadOnlyList<int> OriginalTargets, IReadOnlyList<int> AddedTargets, long Before, long After, int DrawCount = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] YangFuNativeOrigin? NativeOrigin = null, bool NativeCompleted = false)
{
    private IReadOnlyList<int> _original = Array.AsReadOnly(OriginalTargets.ToArray());
    public IReadOnlyList<int> OriginalTargets { get => _original; init => _original = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<int> _added = Array.AsReadOnly(AddedTargets.ToArray());
    public IReadOnlyList<int> AddedTargets { get => _added; init => _added = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public YangFuReceipt? YangFu { get; init; } }
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public BingzhongTargetTail? BingzhongTail { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public YangFuNativeOrigin? SuyiOrigin { get; init; }
}
internal interface IYangFuHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class YangFuDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new YangFuHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op switch
    {
        SkillProgramEffectOp.ResolveSuyiEquipmentThenSlash => [new ReadSelectedTarget()],
        SkillProgramEffectOp.ResolveBingzhongExtraTargets => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)],
        _ => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)]
    };
}
internal sealed class ResolveBingzhongExtraTargetsDescriptor : YangFuDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveBingzhongExtraTargets; }
internal sealed class ResolveBingzhongAfterDamageDrawDescriptor : YangFuDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveBingzhongAfterDamageDraw; }
internal sealed class ResolveSuyiEquipmentThenSlashDescriptor : YangFuDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveSuyiEquipmentThenSlash; }
internal sealed class YangFuHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{ public SkillProgramEffectOp Op => op; public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) => host.GetCapability<IYangFuHost>().Resolve(f, op); }
internal static class YangFuComposition
{
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveBingzhongExtraTargets or SkillProgramEffectOp.ResolveBingzhongAfterDamageDraw or SkillProgramEffectOp.ResolveSuyiEquipmentThenSlash;
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (!p.Activations.Any(a => a.Effects.Any(e => Handles(e.Op))) && !p.Triggers.Any(t => t.Effects.Any(e => Handles(e.Op)))) return;
        if (p.ViewAs.Count != 0 || (p.Id == "ol:bingzhong" ? p.Activations.Count != 0 || p.Triggers.Count != 2 : p.Id != "ol:suyi" || p.Activations.Count != 1 || p.Triggers.Count != 0))
            throw new InvalidOperationException(path + ": Yang Fu requires his complete current ordinary-OL operations.");
        foreach (var a in p.Activations)
            if (a.Effects is not [{ Op: SkillProgramEffectOp.ResolveSuyiEquipmentThenSlash, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
                a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 1 || a.MaxTargets != 1 || a.TargetKind != SkillProgramTargetKind.OtherLiving || a.UsesPerPhase != 1 || a.UsesPerTurn is not null || a.UsesPerGame is not null || a.Condition.Kind != SkillProgramConditionKind.Always)
                throw new InvalidOperationException(path + ": Suyi requires one other living target and one actual Play activation.");
        foreach (var t in p.Triggers)
        {
            var target = t.Id == "first-dynamic-x-targets";
            if (t.Effects.Count != 1 || t.Effects[0].Op != (target ? SkillProgramEffectOp.ResolveBingzhongExtraTargets : SkillProgramEffectOp.ResolveBingzhongAfterDamageDraw) ||
                t.Effects[0].Target != SkillProgramEffectTarget.Owner || t.Effects[0].Condition.Kind != SkillProgramConditionKind.Always || t.Optional ||
                (target ? t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor : t.Subject != SkillProgramTriggerSubject.Owner) || t.Window != (target ? SkillProgramTriggerWindow.CardUseTargetsFinalized : SkillProgramTriggerWindow.AfterDamageApplied) ||
                t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.UsageLimit is not null || t.MarkerCost is not null ||
                !target && (t.Id != "actual-damage-dynamic-x" || t.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage))
                throw new InvalidOperationException(path + ": Bingzhong requires exact mandatory actual-use and per-damage producers.");
        }
    }
}
