namespace CardGame.Core;

/// <summary>Reusable lifecycle primitives for skill grants, deck searches and equipment evolution.</summary>
internal abstract class AdvancedLifecycleOperationDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => SkillProgramEffectCatalog.Default.Resolve(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        string[] fields = Op switch
        {
            SkillProgramEffectOp.AlterEquipmentSlots => ["amount", "equipmentSlots"],
            SkillProgramEffectOp.EquipSampledGenerals => ["amount"],
            SkillProgramEffectOp.SampleFactionSkills => ["providerFactionId"],
            SkillProgramEffectOp.ExpireSampledSkills => ["stateId"],
            SkillProgramEffectOp.ReplaceSkillsOnAwakening => ["skillIds"],
            SkillProgramEffectOp.AccumulateCardRank => ["marker"],
            SkillProgramEffectOp.ObtainDeckRankSum => ["marker", "maximumRankSum"],
            SkillProgramEffectOp.PlaceNamedWeapon or SkillProgramEffectOp.ReclaimNamedWeapon => ["outputKind"],
            _ => []
        };
        r.AllowOnly(["op", "target", "condition", .. fields]);
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner && !(target == SkillProgramEffectTarget.SelectedTarget && Op is
            (SkillProgramEffectOp.AlterEquipmentSlots or SkillProgramEffectOp.AbolishRandomEquipmentSlot or SkillProgramEffectOp.DamageFarthestCharacter or SkillProgramEffectOp.PlaceNamedWeapon)))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.target: unsupported participant for {Op}.");
        foreach (var required in fields.Where(name => name != "stateId"))
            if (!r.Has(required)) throw new InvalidOperationException($"Invalid skill program at {r.Path}: missing required property '{required}'.");
        var amount = r.Has("amount") ? r.RequiredInt("amount") : 0;
        if (Op == SkillProgramEffectOp.AlterEquipmentSlots && amount is < 0 or > 5 ||
            Op == SkillProgramEffectOp.EquipSampledGenerals && amount is < 1 or > 20)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.amount: outside the bounded operation range.");
        var slots = r.OptionalEnumArray<EquipmentSlot>("equipmentSlots") ?? [];
        var skills = r.Has("skillIds") ? r.RequiredIdentifierArray("skillIds") : [];
        if (Op == SkillProgramEffectOp.AlterEquipmentSlots && slots.Count == 0 ||
            Op == SkillProgramEffectOp.ReplaceSkillsOnAwakening && skills.Count is < 1 or > 20)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: requires a nonempty bounded list.");
        var maximumRankSum = r.Has("maximumRankSum") ? r.RequiredInt("maximumRankSum") : 0;
        if (Op == SkillProgramEffectOp.ObtainDeckRankSum && maximumRankSum is < 1 or > 1000)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.maximumRankSum: must be 1..1000.");
        var outputKind = r.Has("outputKind") ? r.RequiredEnum<CardKind>("outputKind") : (CardKind?)null;
        if (outputKind is { } kind && (!EquipmentCatalog.IsEquipment(kind) || EquipmentCatalog.Get(kind).Slot != EquipmentSlot.Weapon || kind == CardKind.GeneralWeapon))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.outputKind: requires a named weapon.");
        return new(Op, target, amount,
            r.Condition(), stateId: r.Has("stateId") ? r.RequiredIdentifier("stateId") : null,
            providerFactionId: r.Has("providerFactionId") ? r.RequiredIdentifier("providerFactionId") : null,
            skillIds: skills,
            equipmentSlots: slots,
            marker: r.Has("marker") ? r.RequiredEnum<PlayerMarkerKind>("marker") : null,
            outputKind: outputKind, maximumRankSum: maximumRankSum);
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        effect.Target == SkillProgramEffectTarget.SelectedTarget ? [new ReadSelectedTarget()] : [];
}
public abstract class AdvancedLifecycleEffectHandler : ISkillProgramEffectHandler
{
    public abstract SkillProgramEffectOp Op { get; }
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.ExecuteAdvancedLifecycle(effect, frame, targetSeat);
}
internal sealed class AlterEquipmentSlotsOperationDescriptor : AdvancedLifecycleOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.AlterEquipmentSlots; }
internal sealed class AbolishRandomEquipmentSlotOperationDescriptor : AdvancedLifecycleOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.AbolishRandomEquipmentSlot; }
internal sealed class SampleFactionSkillsOperationDescriptor : AdvancedLifecycleOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SampleFactionSkills; }
internal sealed class ExpireSampledSkillsOperationDescriptor : AdvancedLifecycleOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ExpireSampledSkills; }
internal sealed class ReplaceSkillsOnAwakeningOperationDescriptor : AdvancedLifecycleOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReplaceSkillsOnAwakening; }
internal sealed class AccumulateCardRankOperationDescriptor : AdvancedLifecycleOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.AccumulateCardRank; }
internal sealed class ObtainDeckRankSumOperationDescriptor : AdvancedLifecycleOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ObtainDeckRankSum; }
internal sealed class DamageAfterDeckShuffleOperationDescriptor : AdvancedLifecycleOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DamageAfterDeckShuffle; }
internal sealed class EquipSampledGeneralsOperationDescriptor : AdvancedLifecycleOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.EquipSampledGenerals; }
internal sealed class DamageFarthestCharacterOperationDescriptor : AdvancedLifecycleOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DamageFarthestCharacter; }
internal sealed class PlaceNamedWeaponOperationDescriptor : AdvancedLifecycleOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.PlaceNamedWeapon; }
internal sealed class ReclaimNamedWeaponOperationDescriptor : AdvancedLifecycleOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReclaimNamedWeapon; }
internal sealed class InheritWeaponOperationDescriptor : AdvancedLifecycleOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.InheritWeapon; }
internal sealed class BalanceHandAttackTricksOperationDescriptor : AdvancedLifecycleOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.BalanceHandAttackTricks; }
internal sealed class ReplaceJudgmentPhaseOperationDescriptor : AdvancedLifecycleOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReplaceJudgmentPhase; }

public sealed class AlterEquipmentSlotsAdvancedHandler : AdvancedLifecycleEffectHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.AlterEquipmentSlots; }
public sealed class AbolishRandomEquipmentSlotAdvancedHandler : AdvancedLifecycleEffectHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.AbolishRandomEquipmentSlot; }
public sealed class SampleFactionSkillsAdvancedHandler : AdvancedLifecycleEffectHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SampleFactionSkills; }
public sealed class ExpireSampledSkillsAdvancedHandler : AdvancedLifecycleEffectHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ExpireSampledSkills; }
public sealed class ReplaceSkillsOnAwakeningAdvancedHandler : AdvancedLifecycleEffectHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReplaceSkillsOnAwakening; }
public sealed class AccumulateCardRankAdvancedHandler : AdvancedLifecycleEffectHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.AccumulateCardRank; }
public sealed class ObtainDeckRankSumAdvancedHandler : AdvancedLifecycleEffectHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ObtainDeckRankSum; }
public sealed class DamageAfterDeckShuffleAdvancedHandler : AdvancedLifecycleEffectHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DamageAfterDeckShuffle; }
public sealed class EquipSampledGeneralsAdvancedHandler : AdvancedLifecycleEffectHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.EquipSampledGenerals; }
public sealed class DamageFarthestCharacterAdvancedHandler : AdvancedLifecycleEffectHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DamageFarthestCharacter; }
public sealed class PlaceNamedWeaponAdvancedHandler : AdvancedLifecycleEffectHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.PlaceNamedWeapon; }
public sealed class ReclaimNamedWeaponAdvancedHandler : AdvancedLifecycleEffectHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReclaimNamedWeapon; }
public sealed class InheritWeaponAdvancedHandler : AdvancedLifecycleEffectHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.InheritWeapon; }
public sealed class BalanceHandAttackTricksAdvancedHandler : AdvancedLifecycleEffectHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.BalanceHandAttackTricks; }
public sealed class ReplaceJudgmentPhaseAdvancedHandler : AdvancedLifecycleEffectHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReplaceJudgmentPhase; }
