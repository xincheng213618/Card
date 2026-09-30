namespace CardGame.Core;

/// <summary>Table primitives shared by content programs; no general identity is interpreted here.</summary>
internal abstract class StrategicProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => SkillProgramEffectCatalog.Default.Resolve(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChangeAttributedMarker, static (_, _) => { });
    public override ProgramOperationInteraction Interaction => Op is SkillProgramEffectOp.SelectDistinctSuitHandDiscards or
        SkillProgramEffectOp.SuppressGeneralSkill or SkillProgramEffectOp.SelectChainedByMarker or SkillProgramEffectOp.SelectOneSelectedTarget
            ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "marker", "sourceBind", "resultBind", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        var amount = r.Has("amount") ? r.RequiredInt("amount") : 0;
        if (amount is < -1024 or > 1024) throw new InvalidOperationException("Marker amount exceeds the supported bound.");
        var sourceBind = r.OptionalIdentifier("sourceBind");
        var resultBind = r.OptionalIdentifier("resultBind");
        var marker = r.Has("marker") ? r.RequiredEnum<PlayerMarkerKind>("marker") : (PlayerMarkerKind?)null;
        if (Op is SkillProgramEffectOp.SetMarkerAmount or SkillProgramEffectOp.MoveUniqueMarker or
            SkillProgramEffectOp.ClaimMarkedHand or SkillProgramEffectOp.SelectChainedByMarker && marker is null)
            throw new InvalidOperationException("Attributed marker operations require a marker.");
        if (Op == SkillProgramEffectOp.SelectDistinctSuitHandDiscards && (resultBind is null || target != SkillProgramEffectTarget.SelectedTarget))
            throw new InvalidOperationException("Cross-owner hand selection requires a selected target and result binding.");
        if (Op == SkillProgramEffectOp.ApplyHandDiscardShare && sourceBind is null)
            throw new InvalidOperationException("Hand discard outcomes require a source binding.");
        if (Op == SkillProgramEffectOp.DamageOtherLiving && amount < 1)
            throw new InvalidOperationException("Table damage requires a positive amount.");
        return new(Op, target, amount, r.Condition(), sourceBind: sourceBind,
            resultBind: resultBind, marker: marker);
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op switch
    {
        SkillProgramEffectOp.SelectDistinctSuitHandDiscards => [new ReadSelectedTarget(), new CaptureSourceCard(e.ResultBind!, 4)],
        SkillProgramEffectOp.ApplyHandDiscardShare => [new ReadCardSet(e.SourceBind!)],
        SkillProgramEffectOp.SelectChainedByMarker => [new SelectTargetSet(0, 32)],
        SkillProgramEffectOp.DiscardTargetEquipment => [new ReadTargetSet(0)],
        SkillProgramEffectOp.SelectOneSelectedTarget => [new ReadTargetSet(0), new NarrowTargetSetToSingle()],
        _ => WithSelectedTarget(e, [])
    };
}
internal sealed class SelectDistinctSuitHandDiscardsDescriptor : StrategicProgramOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectDistinctSuitHandDiscards; }
internal sealed class ApplyHandDiscardShareDescriptor : StrategicProgramOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ApplyHandDiscardShare; }
internal sealed class SuppressGeneralSkillDescriptor : StrategicProgramOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SuppressGeneralSkill; }
internal sealed class SetMarkerAmountDescriptor : StrategicProgramOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SetMarkerAmount; }
internal sealed class MoveUniqueMarkerDescriptor : StrategicProgramOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.MoveUniqueMarker; }
internal sealed class ClaimMarkedHandDescriptor : StrategicProgramOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ClaimMarkedHand; }
internal sealed class DamageOtherLivingDescriptor : StrategicProgramOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DamageOtherLiving; }
internal sealed class SelectChainedByMarkerDescriptor : StrategicProgramOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectChainedByMarker; }
internal sealed class DiscardTargetEquipmentDescriptor : StrategicProgramOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardTargetEquipment; }
internal sealed class EndCurrentPlayDescriptor : StrategicProgramOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.EndCurrentPlay; }
internal sealed class SelectOneSelectedTargetDescriptor : StrategicProgramOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectOneSelectedTarget; }
public abstract class StrategicProgramEffectHandler : ISkillProgramEffectHandler
{
    public abstract SkillProgramEffectOp Op { get; }
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) => host.ExecuteStrategicEffect(e, f, seat);
}
public sealed class SelectDistinctSuitHandDiscardsHandler : StrategicProgramEffectHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectDistinctSuitHandDiscards; }
public sealed class ApplyHandDiscardShareHandler : StrategicProgramEffectHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ApplyHandDiscardShare; }
public sealed class SuppressGeneralSkillHandler : StrategicProgramEffectHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SuppressGeneralSkill; }
public sealed class SetMarkerAmountHandler : StrategicProgramEffectHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SetMarkerAmount; }
public sealed class MoveUniqueMarkerHandler : StrategicProgramEffectHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.MoveUniqueMarker; }
public sealed class ClaimMarkedHandHandler : StrategicProgramEffectHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ClaimMarkedHand; }
public sealed class DamageOtherLivingHandler : StrategicProgramEffectHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DamageOtherLiving; }
public sealed class SelectChainedByMarkerHandler : StrategicProgramEffectHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectChainedByMarker; }
public sealed class DiscardTargetEquipmentHandler : StrategicProgramEffectHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardTargetEquipment; }
public sealed class EndCurrentPlayHandler : StrategicProgramEffectHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.EndCurrentPlay; }
public sealed class SelectOneSelectedTargetHandler : StrategicProgramEffectHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectOneSelectedTarget; }
