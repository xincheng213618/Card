using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum AnniversaryHzBody { Lifeng, Diji, Zhongshu, Qihai, Tianchong }
public enum AnniversaryHzStage { Choose, Drawing, Children, TianchongPayment, TianchongChildren }
public sealed record AnniversaryHzReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, int InstructionIndex,
    AnniversaryHzStage Stage, int Target = -1, AnniversaryHzBody? Body = null, int Mode = 0, int Amount = 0,
    ActualDiscardRecoveryPhaseKey? Phase = null, long DyingId = 0, bool HpPaid = false, long Before = 0, long After = 0, long IssueId = 0, long DamageId = 0)
{
    private IReadOnlyList<int> _cards = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryHzReceipt? AnniversaryHuangZhong { get; init; } }
public sealed record AnniversaryHzBodyIssuedEvent(long Id, CardConversionSource Source, int Target, AnniversaryHzBody Body, int TurnNumber, int TurnOwner) : IGameEvent;
public sealed record AnniversaryHzStatusConsumedEvent(long Id, string Reason, long Origin) : IGameEvent;
public sealed record AnniversaryHzStatusExpiredEvent(long Id, int Target, int EndedTurn) : IGameEvent;
public sealed record AnniversaryHzZhanjueIssuedEvent(long Id, CardConversionSource Source, ActualDiscardRecoveryPhaseKey Phase, int Mode, int DrawCount) : IGameEvent;
public sealed record AnniversaryHzZhanjueSpentEvent(long Id, long Origin, int Amount) : IGameEvent;
public sealed record AnniversaryHzZhanjueRecoveredEvent(long FrameId, long IssueId, long DamageId, int Requested) : IGameEvent;
public sealed record AnniversaryHzInvalidation(long ParentId, CardActionContext? Action, int Actor = -1, CardKind? LegacyKind = null)
{
    private IReadOnlyList<long> _ids = Array.AsReadOnly(Array.Empty<long>());
    public IReadOnlyList<long> StatusIds { get => _ids; init => _ids = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryHzInvalidation? AnniversaryHzInvalidation { get; init; }
    private IReadOnlyList<AnniversaryHzInvalidation>? _anniversaryHzResponses;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<AnniversaryHzInvalidation>? AnniversaryHzResponses
    { get => _anniversaryHzResponses; init => _anniversaryHzResponses = value is null ? null : Array.AsReadOnly(value.ToArray()); }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? AnniversaryHzSlashIssueId { get; init; }
}
public sealed partial record NullificationWindowFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryHzInvalidation? AnniversaryHzInvalidation { get; init; } }
internal interface IAnniversaryHzHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op); }
internal abstract class AnniversaryHzDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new AnniversaryHzHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.WholeActualUseDeclaration | ProgramTrackedFact.NativeLogicalDiscard);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
}
internal sealed class AnniversaryHzLieqiongDescriptor : AnniversaryHzDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryHuangZhongLieqiong; }
internal sealed class AnniversaryHzZhanjueDescriptor : AnniversaryHzDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryHuangZhongZhanjue; }
internal sealed class AnniversaryHzBenefitDescriptor : AnniversaryHzDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryHuangZhongDamageBenefit; }
internal sealed class AnniversaryHzHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) => h.GetCapability<IAnniversaryHzHost>().Resolve(f, op);
}
internal static class AnniversaryHzComposition
{
    [ProgramCatalogValidation(39600)] internal static void Validate(string path, SkillProgram p)
    {
        var own = p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => e.Op is SkillProgramEffectOp.ResolveAnniversaryHuangZhongLieqiong or SkillProgramEffectOp.ResolveAnniversaryHuangZhongZhanjue or SkillProgramEffectOp.ResolveAnniversaryHuangZhongDamageBenefit);
        if (!own) return;
        static bool One(SkillProgramTrigger t, SkillProgramEffectOp op) => t.Effects is [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } e] && e.Op == op;
        var valid = p.Activations.Count == 0 && p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.CardPolicies.Count == 0 && (p.Id switch
        {
            "anniversary-god:lieqiong" => p.Triggers is [{ Window: SkillProgramTriggerWindow.AfterDamageApplied, Subject: SkillProgramTriggerSubject.DamageSource, DamageOccurrence: SkillProgramDamageOccurrence.PerDamage, Optional: true, IsSkillInvocation: true } t] && One(t, SkillProgramEffectOp.ResolveAnniversaryHuangZhongLieqiong),
            "anniversary-god:zhanjue" => p.Triggers.Count == 2 && p.Triggers.SingleOrDefault(t => t.Window == SkillProgramTriggerWindow.PlayPhaseStarting) is { Optional: true, IsSkillInvocation: true } start && One(start, SkillProgramEffectOp.ResolveAnniversaryHuangZhongZhanjue) &&
                p.Triggers.SingleOrDefault(t => t.Window == SkillProgramTriggerWindow.AfterDamageApplied) is { Subject: SkillProgramTriggerSubject.DamageSource, DamageOccurrence: SkillProgramDamageOccurrence.PerDamage, Optional: false, IsSkillInvocation: false } benefit && One(benefit, SkillProgramEffectOp.ResolveAnniversaryHuangZhongDamageBenefit),
            _ => false
        });
        if (!valid) throw new InvalidOperationException(path + ": Anniversary Huang Zhong requires the actual damage hit, exact Play-start draw and non-invocation first-damage recovery composition.");
    }
}
