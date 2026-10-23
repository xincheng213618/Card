using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryZhangWenHost
{
    void Refund(ProgramSkillFrame frame);
    SkillProgramStepOutcome Sibian(ProgramSkillFrame frame);
}
internal sealed class RefundAnniversaryZhangWenSongshuDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RefundAnniversaryZhangWenSongshu;
    public override ISkillProgramEffectHandler Handler { get; } = new RefundAnniversaryZhangWenSongshuHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.RefundCardUseDebit, static (_, c) => c.PublicControlValue(1));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        if (effect.Condition.Kind != SkillProgramConditionKind.PindianWon || effect.Condition.SourceBind != "__active-pindian-result")
            throw new InvalidOperationException(r.Path + ": Songshu refunds only its own completed winning Pindian.");
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new ReadPindianResult("__active-pindian-result")];
}
internal sealed class RefundAnniversaryZhangWenSongshuHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RefundAnniversaryZhangWenSongshu;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host)
    { host.GetCapability<IAnniversaryZhangWenHost>().Refund(frame); return SkillProgramStepOutcome.Continue; }
}
internal sealed class ResolveAnniversaryZhangWenSibianDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryZhangWenSibian;
    public override ISkillProgramEffectHandler Handler { get; } = new ResolveAnniversaryZhangWenSibianHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.DrawPhaseStarting)];
}
internal sealed class ResolveAnniversaryZhangWenSibianHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryZhangWenSibian;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => host.GetCapability<IAnniversaryZhangWenHost>().Sibian(frame);
}
public sealed record ZhangWenSongshuReceipt(CardConversionSource Source, string Hash, int Turn, int TurnOwner, int PlayPhase, string UsageGroup, int TurnUses, int PhaseUses, bool Refunded = false);
public sealed record ZhangWenSongshuAcceptedEvent(long FrameId, CardConversionSource Source, string Hash, int Turn, int TurnOwner, int PlayPhase, string UsageGroup, int TurnUses, int PhaseUses) : IGameEvent;
public sealed record ZhangWenSongshuRefundedEvent(long FrameId, int Turn, int PlayPhase, int TurnBefore, int TurnAfter, int PhaseBefore, int PhaseAfter) : IGameEvent;
public enum ZhangWenSibianStage { RevealChildren, GainChildren, Recipient, FinishChildren }
public sealed record ZhangWenSibianEntity(int CardId, CardKind Kind, int Rank, int RevealSequence);
public sealed record ZhangWenSibianStep(ZhangWenSibianStage Stage, int Before, int After);
public sealed record ZhangWenSibianReceipt(CardConversionSource Source, string Hash, long ParentId, int Turn, ZhangWenSibianStage Stage, int? Recipient = null)
{
    private IReadOnlyList<ZhangWenSibianEntity> _entities = Array.AsReadOnly(Array.Empty<ZhangWenSibianEntity>());
    private IReadOnlyList<int> _extrema = Array.AsReadOnly(Array.Empty<int>());
    private IReadOnlyList<ZhangWenSibianStep> _steps = Array.AsReadOnly(Array.Empty<ZhangWenSibianStep>());
    public IReadOnlyList<ZhangWenSibianEntity> Entities { get => _entities; init => _entities = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Extrema { get => _extrema; init => _extrema = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<ZhangWenSibianStep> Steps { get => _steps; init => _steps = Array.AsReadOnly(value.ToArray()); }
}
public sealed record ZhangWenSibianStartedEvent(long FrameId, CardConversionSource Source, string Hash, long ParentId, int Turn) : IGameEvent;
public sealed record ZhangWenSibianEntityEvent(long FrameId, int Index, ZhangWenSibianEntity Entity, bool Extreme) : IGameEvent;
public sealed record ZhangWenSibianStepEvent(long FrameId, ZhangWenSibianStep Step) : IGameEvent;
public sealed record ZhangWenSibianRecipientEvent(long FrameId, int Recipient) : IGameEvent;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ZhangWenSongshuReceipt? ZhangWenSongshu { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ZhangWenSibianReceipt? ZhangWenSibian { get; init; }
}
internal static class AnniversaryZhangWenComposition
{
    internal const string Songshu = "anniversary:songshu", Sibian = "anniversary:sibian", Contest = "hand-contest", Replacement = "replace-draw-four";
    [ProgramCatalogValidation(45100)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => e.Op is SkillProgramEffectOp.RefundAnniversaryZhangWenSongshu or SkillProgramEffectOp.ResolveAnniversaryZhangWenSibian)) return;
        var correct = p.Id == Songshu ? p.Triggers.Count == 0 && p.Activations is [{ Id: Contest, MinCards: 1, MaxCards: 1, MinTargets: 1, MaxTargets: 1,
            SourceZones: [CardZoneKind.Hand], TargetKind: SkillProgramTargetKind.OtherLivingWithHand, UsesPerTurn: null, UsesPerPhase: 1, Effects: [
            { Op: SkillProgramEffectOp.Pindian, Target: SkillProgramEffectTarget.SelectedTarget, Amount: 1, Condition.Kind: SkillProgramConditionKind.Always },
            { Op: SkillProgramEffectOp.RefundAnniversaryZhangWenSongshu, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.PindianWon, Condition.SourceBind: "__active-pindian-result" },
            { Op: SkillProgramEffectOp.Draw, Target: SkillProgramEffectTarget.Owner, Amount: 2, NumberExpression: null, Condition.Kind: SkillProgramConditionKind.PindianNotWon },
            { Op: SkillProgramEffectOp.Draw, Target: SkillProgramEffectTarget.SelectedTarget, Amount: 2, NumberExpression: null, Condition.Kind: SkillProgramConditionKind.PindianNotWon }] }]
            : p.Id == Sibian && p.Activations.Count == 0 && p.Triggers is [{ Id: Replacement, Window: SkillProgramTriggerWindow.DrawPhaseStarting,
                Subject: SkillProgramTriggerSubject.Owner, Optional: true, DrawPhaseMode: SkillProgramDrawPhaseMode.Replacement,
                Effects: [{ Op: SkillProgramEffectOp.ResolveAnniversaryZhangWenSibian, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] }];
        if (!correct || p.Modifiers.Count != 0 || p.ViewAs.Count != 0 || p.Contributions.Count != 0 || p.BooleanStates.Count != 0 || p.CardIdentities.Count != 0 || p.CardPolicies.Count != 0)
            throw new InvalidOperationException(path + ": Zhang Wen requires the winning once-per-Play Pindian refund or the current unrestricted top-four extrema draw replacement.");
    }
}
