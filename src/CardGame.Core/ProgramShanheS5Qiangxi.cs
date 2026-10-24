namespace CardGame.Core;

public sealed record ShanheS5QiangxiReceipt(CardConversionSource Source, string GameplayHash,
    int TargetSeat, int RoundNumber, int TurnNumber, long PhaseInstanceId, int ActivationCount,
    int CostHpBefore, int? WeaponCardId, CardLocation? WeaponFrom, bool Paid = false);

public sealed partial record ProgramSkillFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ShanheS5QiangxiReceipt? ShanheS5Qiangxi { get; init; }
}

// No selected hand-card ID or hidden source location is published here.
public sealed record ShanheS5QiangxiPaidEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int TargetSeat, int RoundNumber, int TurnNumber, long PhaseInstanceId, int ActivationCount, int Damage) : IGameEvent;

internal interface IShanheS5QiangxiHost
{
    SkillProgramStepOutcome ResolveShanheS5Qiangxi(ProgramSkillFrame frame, string stage);
}

internal sealed class ResolveShanheS5QiangxiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShanheS5Qiangxi;
    public override ISkillProgramEffectHandler Handler { get; } = new ResolveShanheS5QiangxiHandler();
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (effect, context) =>
        {
            // The public first-paid-use floor is two. The native pipeline prices
            // the separately declared HP or real weapon cost as usual.
            if (effect.StateId == "hit") context.Damage(new(SkillProgramEffectOp.Damage,
                SkillProgramEffectTarget.SelectedTarget, 2, effect.Condition));
        });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stage", "condition");
        var stage = r.RequiredIdentifier("stage");
        if (stage is not ("prepare" or "hit")) throw new InvalidOperationException(r.Path + ": invalid S5 Qiangxi stage.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), stateId: stage);
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new ReadSelectedTarget()];
}

internal sealed class ResolveShanheS5QiangxiHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShanheS5Qiangxi;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IShanheS5QiangxiHost>().ResolveShanheS5Qiangxi(frame, effect.StateId!);
}

internal static class ShanheS5QiangxiComposition
{
    [ProgramCatalogValidation(59100)]
    internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Activations.Any(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveShanheS5Qiangxi)) &&
            !p.Triggers.Any(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveShanheS5Qiangxi))) return;
        if (p.Id != "shanhe:s5-qiangxi-14139" || p.Activations.Count != 2 || p.Triggers.Count != 0 || p.ViewAs.Count != 0 ||
            p.Modifiers.Count != 0 || p.Contributions.Count != 0 || p.CardPolicies.Count != 0 || p.BooleanStates.Count != 0 || p.CardIdentities.Count != 0)
            throw new InvalidOperationException(path + ": S5 Qiangxi requires its complete two-cost activation graph.");
        foreach (var a in p.Activations)
        {
            var hp = a.Id == "lose-hp";
            if (!hp && a.Id != "discard-weapon" || a.UsageGroup != "qiangxi" || a.UsesPerPhase != 2 || a.UsesPerTurn is not null ||
                a.UsesPerGame is not null || a.MinTargets != 1 || a.MaxTargets != 1 || a.TargetKind != SkillProgramTargetKind.OtherLiving ||
                a.MinCards != (hp ? 0 : 1) || a.MaxCards != (hp ? 0 : 1) || a.MarkerCost is not null ||
                hp && (a.Condition.Kind != SkillProgramConditionKind.HpAtLeast || a.Condition.Value != 1) ||
                !hp && (a.Condition.Kind != SkillProgramConditionKind.Always ||
                    !a.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) || !a.EquipmentSlots.SequenceEqual([EquipmentSlot.Weapon])) ||
                a.Effects is not [var prepare, var cost, var hit] ||
                prepare.Op != SkillProgramEffectOp.ResolveShanheS5Qiangxi || prepare.StateId != "prepare" ||
                hit.Op != SkillProgramEffectOp.ResolveShanheS5Qiangxi || hit.StateId != "hit" ||
                cost.Op != (hp ? SkillProgramEffectOp.LoseHp : SkillProgramEffectOp.DiscardSelected) || cost.Amount != 1 ||
                a.Effects.Any(e => e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always))
                throw new InvalidOperationException(path + ": S5 Qiangxi must freeze, pay one native HP/weapon cost and issue its original-target damage.");
        }
    }
}
