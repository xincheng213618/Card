namespace CardGame.Core;

internal interface IWushuangOriginalSkillHost
{
    SkillProgramStepOutcome Execute(ProgramSkillFrame frame, SkillProgramEffectOp operation);
}
internal abstract class WushuangOriginalSkillDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new WushuangOriginalSkillHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(2));
}
internal sealed class ResolveWushuangOriginalYijiDescriptor : WushuangOriginalSkillDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWushuangOriginalYiji; }
internal sealed class ResolveWushuangYashiDescriptor : WushuangOriginalSkillDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWushuangYashi; }
internal sealed class ResolveWushuangShenfuDescriptor : WushuangOriginalSkillDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWushuangShenfu; }
internal sealed class ResolveWushuangLingrenDescriptor : WushuangOriginalSkillDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWushuangLingren; }
internal sealed class ResolveWushuangTairanDescriptor : WushuangOriginalSkillDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWushuangTairan; }
internal sealed class SettleWushuangTairanDescriptor : WushuangOriginalSkillDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SettleWushuangTairan; }
internal sealed class ResolveWushuangGuanxuDescriptor : WushuangOriginalSkillDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWushuangGuanxu; }
internal sealed class ResolveWushuangChoufaDescriptor : WushuangOriginalSkillDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWushuangChoufa; }
internal sealed class NullifyWushuangSecondUseDescriptor : WushuangOriginalSkillDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.NullifyWushuangSecondUse; }
internal sealed class ResolveWushuangOriginalJiemingDescriptor : WushuangOriginalSkillDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWushuangOriginalJieming; }
internal sealed class PayAndDrawWushuangJinzhiDescriptor : WushuangOriginalSkillDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PayAndDrawWushuangJinzhi;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);
}
internal sealed class SkipWushuangFenliPhaseDescriptor : WushuangOriginalSkillDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SkipWushuangFenliPhase; }
internal sealed class RestrictWushuangQianxiJudgmentColorDescriptor : WushuangOriginalSkillDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RestrictWushuangQianxiJudgmentColor; }
internal sealed class WushuangOriginalSkillHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IWushuangOriginalSkillHost>().Execute(frame, operation);
}
