using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum NiuFuStage { RecastChoice, RecastCostChildren, RecastDrawChildren, SourceChoice, SourceGiftChoice, SourceGiftChildren, SourceHpChildren, SourceTurnChildren, ZoneChoice, ZoneChildren }
public sealed record NiuFuEntity(int CardId, CardKind Kind, CardLocation From, bool IsGeneralWeapon = false);
public sealed record NiuFuStep(int Index, NiuFuStage Stage, string Reason, long Before, long After, int ActualCount, int? CardId = null, CardKind? Kind = null, CardLocation? From = null, CardLocation? To = null);
public sealed record NiuFuStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, int ActualTurn, long WindowId, long DamageId, int DamageAmount, int TargetSeat, int? DamageSourceSeat, long AttackId, long? ActionId, int PhysicalCount) : IGameEvent;
public sealed record NiuFuSlashEntityEvent(long FrameId, int Index, int CardId, CardKind Kind) : IGameEvent;
public sealed record NiuFuStepIssuedEvent(long FrameId, NiuFuStep Step) : IGameEvent;
public sealed record NiuFuSourceChoiceEvent(long FrameId, int SourceSeat, string Branch, int HpBefore, bool WasFaceDown) : IGameEvent;
public sealed record NiuFuCompletedEvent(long FrameId) : IGameEvent;
public sealed record NiuFuReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, int InstructionIndex, int ActualTurn,
    long WindowId, long DamageId, int DamageAmount, int TargetSeat, int? DamageSourceSeat, long AttackId, long? ActionId, NiuFuStage Stage,
    IReadOnlyList<NiuFuEntity> Physical, IReadOnlyList<NiuFuStep> Steps, NiuFuEntity? Recast = null, int NextZone = 0, string? SourceBranch = null,
    int? SourceHpBefore = null, bool? SourceWasFaceDown = null)
{
    private IReadOnlyList<NiuFuEntity> _physical = Array.AsReadOnly(Physical.ToArray());
    public IReadOnlyList<NiuFuEntity> Physical { get => _physical; init => _physical = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<NiuFuStep> _steps = Array.AsReadOnly(Steps.ToArray());
    public IReadOnlyList<NiuFuStep> Steps { get => _steps; init => _steps = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public NiuFuReceipt? NiuFu { get; init; } }
internal interface INiuFuHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class NiuFuDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new NiuFuHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
internal sealed class NiuFuHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int targetSeat, ISkillProgramEffectHost host) => host.GetCapability<INiuFuHost>().Resolve(f, op);
}
internal static class NiuFuComposition
{
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveShisuanRecastSourceChoice or SkillProgramEffectOp.ObtainZonglueDamageTargetZones;
    internal static void Activation(string path, SkillProgramActivation a)
    { if (a.Effects.Any(e => Handles(e.Op))) throw new InvalidOperationException(path + ": NiuFu operations require actual after-damage triggers."); }
    internal static void Trigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => Handles(e.Op))) return;
        if (t.Effects is not [var e] || !Handles(e.Op) || e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always ||
            t.Window != SkillProgramTriggerWindow.AfterDamageApplied || t.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            t.MarkerCost is not null || t.UsageLimit is not null ||
            (e.Op == SkillProgramEffectOp.ResolveShisuanRecastSourceChoice ? t.Subject != SkillProgramTriggerSubject.Owner || t.Optional : t.Subject != SkillProgramTriggerSubject.Any || !t.Optional))
            throw new InvalidOperationException(path + ": NiuFu requires one whole actual-damage owner Shisuan or optional source-qualified Zonglue instruction.");
    }
}
