namespace CardGame.Core;

internal interface IGuanduGeneralHost
{
    SkillProgramStepOutcome Execute(ProgramSkillFrame frame, SkillProgramEffectOp operation);
}

internal abstract class GuanduGeneralDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new GuanduGeneralHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(2));
}

internal sealed class ResolveGuanduShicaiDescriptor : GuanduGeneralDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveGuanduShicai;
}
internal sealed class ResolveGuanduZezhuDescriptor : GuanduGeneralDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveGuanduZezhu;
}
internal sealed class ResolveGuanduJieliangDescriptor : GuanduGeneralDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveGuanduJieliang;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DrawPhaseStarting)];
}
internal sealed class ResolveGuanduJieliangClaimDescriptor : GuanduGeneralDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveGuanduJieliangClaim;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPhaseEnded)];
}
internal sealed class ResolveGuanduCangchuDescriptor : GuanduGeneralDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveGuanduCangchu;
}
internal sealed class ResolveGuanduSushouDescriptor : GuanduGeneralDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveGuanduSushou;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPhaseStarting)];
}
internal sealed class ResolveGuanduPoluDescriptor : GuanduGeneralDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveGuanduPolu;
}
internal sealed class ResolveGuanduThunderChariotDescriptor : GuanduGeneralDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveGuanduThunderChariot;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
internal sealed class ResolveGuanduChoulueDescriptor : GuanduGeneralDescriptor
{
    internal static readonly IReadOnlyList<string> OutputNames = Array.AsReadOnly(Enum.GetValues<CardKind>()
        .Where(kind => kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash ||
            CardCatalog.Get(kind).CategoryName == "锦囊牌" && kind is not (CardKind.Indulgence or CardKind.SupplyShortage or CardKind.Lightning))
        .Select(kind => char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..]).Append("none").ToArray());
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveGuanduChoulue;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting), new CreateChoiceResult("choulue-kind", OutputNames)];
}
internal sealed class ResolveGuanduHengjiangDescriptor : GuanduGeneralDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveGuanduHengjiang;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
internal sealed class ResolveGuanduHengjiangRewardDescriptor : GuanduGeneralDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveGuanduHengjiangReward;
}
internal sealed class ResolveGuanduXiyingDescriptor : GuanduGeneralDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveGuanduXiying;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)];
}
internal sealed class ResolveGuanduBeizhanDescriptor : GuanduGeneralDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveGuanduBeizhan;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterTurnEnded)];
}
internal sealed class ResolveGuanduYuanlveDescriptor : GuanduGeneralDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveGuanduYuanlve;
}
internal sealed class ResolveGuanduMoushiDescriptor : GuanduGeneralDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveGuanduMoushi;
}
internal sealed class ResolveGuanduZhongjieDescriptor : GuanduGeneralDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveGuanduZhongjie;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.OwnerDied)];
}
internal sealed class ResolveGuanduSuishiDyingDescriptor : GuanduGeneralDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveGuanduSuishiDying;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DyingEntering)];
}
internal sealed class ResolveGuanduSuishiDeathDescriptor : GuanduGeneralDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveGuanduSuishiDeath;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CharacterDied)];
}
internal sealed class ResolveGuanduFuyuanDescriptor : GuanduGeneralDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveGuanduFuyuan;
}

internal sealed class GuanduGeneralHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IGuanduGeneralHost>().Execute(frame, operation);
}

internal sealed class GuanduFuyuanCommittedUseContract : IProgramCommittedUseContract
{
    public string SkillId => "guandu:fuyuan";
    public void ValidateTrigger(SkillProgramTrigger t)
    {
        if (t.Window != SkillProgramTriggerWindow.CardUseCommitted || t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor ||
            !t.IncludeResponseUses || !t.Optional || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            t.Effects is not [{ Op: SkillProgramEffectOp.ResolveGuanduFuyuan, Target: SkillProgramEffectTarget.Owner,
                Condition.Kind: SkillProgramConditionKind.Always }] ||
            t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 ||
            t.UsageScope is not null || t.UsageLimit is not null || t.MarkerCost is not null || t.Priority != 0 ||
            t.SourceSkillId is not null || t.SourceViewAsId is not null || t.SingleActionInstance || t.NoDyingAtActivation)
            throw new InvalidOperationException("Activity Fuyuan requires its exact optional committed own use, including rescue Uses.");
    }
}
