using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record ShanheS5YimieOrigin(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    long BeforeDamageWindowId, long AttackId, bool CardAttack, int TargetSeat, int TurnNumber,
    int OriginalDamage, int OriginalTargetHp, int X, int CostHpBefore, DamageNature Nature);
public sealed record ShanheS5YimieDamageReceipt(ShanheS5YimieOrigin Origin, long? AppliedDamageFrameId = null,
    int AppliedAmount = 0);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShanheS5YimieOrigin? ShanheS5YimieCost { get; init; }
}
public abstract partial record ResolutionFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShanheS5YimieDamageReceipt? ShanheS5YimieDamage { get; init; }
}
public sealed record ShanheS5YimieRecoveryFrame(long Id, long ParentFrameId, ShanheS5YimieDamageReceipt Receipt,
    bool Requested = false, ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.ShanheS5YimieRecovery, Step), IProgramChildResolutionFrame;

public sealed record ShanheS5YimieFrozenEvent(ShanheS5YimieOrigin Origin) : IGameEvent;
public sealed record ShanheS5YimieIncreasedEvent(long ProgramFrameId, long AttackId, int OriginalDamage, int X, int IncreasedDamage) : IGameEvent;
public sealed record ShanheS5YimieAppliedEvent(long AttackId, long DamageFrameId, int TargetSeat, int Amount) : IGameEvent;
public sealed record ShanheS5YimieRecoveryRequestedEvent(long FrameId, long AttackId, long DamageFrameId, int SourceSeat, int TargetSeat, int Amount) : IGameEvent;
public sealed record ShanheS5YimieCompletedEvent(long AttackId, long ProgramFrameId, int TargetSeat, int X, bool ActualDamage, bool RecoveryRequested) : IGameEvent;

internal interface IShanheS5YimieHost
{ SkillProgramStepOutcome ResolveShanheS5Yimie(ProgramSkillFrame frame, string stage); }
internal sealed class ResolveShanheS5YimieDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShanheS5Yimie;
    public override ISkillProgramEffectHandler Handler { get; } = new ResolveShanheS5YimieHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (e, c) => { if (e.StateId == "increase") c.PublicControlValue(3); });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stage", "condition"); var stage = r.RequiredIdentifier("stage");
        if (stage is not ("prepare" or "increase")) throw new InvalidOperationException(r.Path + ": Yimie requires prepare or increase.");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), stateId: stage);
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];
    internal static void ValidateTrigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveShanheS5Yimie)) return;
        if (t.Window != SkillProgramTriggerWindow.BeforeDamageApplied || t.Subject != SkillProgramTriggerSubject.DamageSource || !t.Optional ||
            t.UsageScope != SkillUsageScope.Turn || t.UsageLimit != 1 || t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null ||
            t.MarkerCost is not null || t.ChoiceGroup is not null || t.RequiredPolarity is not null || t.DamageCardKinds.Count != 0 ||
            t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.Effects is not [
                { Op: SkillProgramEffectOp.ResolveShanheS5Yimie, StateId: "prepare" },
                { Op: SkillProgramEffectOp.LoseHp, Target: SkillProgramEffectTarget.Owner, Amount: 1 },
                { Op: SkillProgramEffectOp.ResolveShanheS5Yimie, StateId: "increase" }] ||
            t.Effects.Any(e => e.Condition.Kind != SkillProgramConditionKind.Always))
            throw new InvalidOperationException(path + ": Yimie requires one optional actual-source once-per-turn frozen-X, native HP loss and damage increase sequence.");
    }
    [ProgramCatalogValidation(5795)]
    internal static void ValidateCatalog(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => e.Op == SkillProgramEffectOp.ResolveShanheS5Yimie)) return;
        if (p.Id != "shanhe:s5-yimie-7005" || p.Triggers.Count != 1 || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.CardPolicies.Count != 0 || p.Modifiers.Count != 0 || p.DamageModifiers.Count != 0)
            throw new InvalidOperationException(path + ": the S5 Yimie operation belongs to its complete dedicated source.");
        ValidateTrigger(path, p.Triggers.Single());
    }
}
public sealed class ResolveShanheS5YimieHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShanheS5Yimie;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        h.GetCapability<IShanheS5YimieHost>().ResolveShanheS5Yimie(f, e.StateId!);
}
