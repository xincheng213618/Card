using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum ShenCaoPiStage { Drawing, Children, BorrowChoice, Complete }
public sealed record ShenCaoPiReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation,
    int InstructionIndex, long ParentId, int TargetSeat, ShenCaoPiStage Stage, int Count = 0, long ActionId = 0,
    long IssuedId = 0, NativeDrawOutcome? DrawOutcome = null, KemingBorrowOption? ForcedBorrow = null,
    uint BorrowRandomBefore = 0, uint BorrowRandomAfter = 0)
{
    private IReadOnlyList<KemingBorrowOption> _options = Array.AsReadOnly(Array.Empty<KemingBorrowOption>());
    public IReadOnlyList<KemingBorrowOption> Options { get => _options; init => _options = Array.AsReadOnly(value.ToArray()); }
}
public sealed record KemingBorrowOption(string GeneralId, string SkillId, string BindingId, string GameplayHash);
public sealed record KemingBorrowReceipt(long IssuerFrameId, CardConversionSource Source, string SourceHash,
    long DamageFrameId, int BorrowerSeat, KemingBorrowOption Selected, DamageTriggerCandidate Candidate);
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShenCaoPiReceipt? ShenCaoPi { get; init; } }
public sealed partial record DamageTriggerWindowFrame
{
    private IReadOnlyList<KemingBorrowReceipt>? _kemingBorrows;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<KemingBorrowReceipt>? KemingBorrows
    { get => _kemingBorrows; init => _kemingBorrows = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed record YinzhiIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int TargetSeat,
    int Count, int ActualTurn, int TargetOwnTurns) : IGameEvent;
public sealed record YinzhiActivatedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int TargetSeat,
    int Count, int ActualTurn, int TargetOwnTurns) : IGameEvent;
public sealed record YinzhiTurnRestrictionReleasedEvent(long IssueFrameId, long DamageFrameId) : IGameEvent;
public sealed record YinzhiHandRewardRequestedEvent(long FrameId, long IssueFrameId, long ActionId, int Count) : IGameEvent;
public sealed record ShenCaoPiDrawReturnedEvent(long FrameId, int OwnerSeat, int Requested, int Actual, long Before, long After) : IGameEvent;
public sealed record KemingBorrowIssuedEvent(long WindowId, KemingBorrowReceipt Receipt) : IGameEvent;
public sealed record KemingEchoArmedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int TargetSeat, long InvocationCutoff) : IGameEvent;
internal interface IShenCaoPiHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class ShenCaoPiDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new ShenCaoPiHandler(op);
    public override ProgramOperationInteraction Interaction => op == SkillProgramEffectOp.ResolveKemingDamagedSkill ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeActionAppearance | ProgramTrackedFact.NativeDrawInvocation | ProgramTrackedFact.ActualDiscardRecoveryPhase,
        Activated: ProgramTrackedFact.NativeActionAppearance | ProgramTrackedFact.NativeDrawInvocation | ProgramTrackedFact.ActualDiscardRecoveryPhase);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => op switch
    {
        SkillProgramEffectOp.ApplyShenCaoPiYinzhi => [],
        SkillProgramEffectOp.DrawYinzhiHandAction => [new RequireTriggerWindows([SkillProgramTriggerWindow.CardUseCompleted, SkillProgramTriggerWindow.CardResponseCompleted])],
        _ => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)]
    };
}
internal sealed class ApplyShenCaoPiYinzhiDescriptor() : ShenCaoPiDescriptor(SkillProgramEffectOp.ApplyShenCaoPiYinzhi);
internal sealed class DrawYinzhiHandActionDescriptor() : ShenCaoPiDescriptor(SkillProgramEffectOp.DrawYinzhiHandAction);
internal sealed class ResolveKemingDamagedSkillDescriptor() : ShenCaoPiDescriptor(SkillProgramEffectOp.ResolveKemingDamagedSkill);
internal sealed class ArmKemingSkillEchoDescriptor() : ShenCaoPiDescriptor(SkillProgramEffectOp.ArmKemingSkillEcho);
internal sealed class ShenCaoPiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{ public SkillProgramEffectOp Op => op; public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) => h.GetCapability<IShenCaoPiHost>().Resolve(f, op); }
internal static class ShenCaoPiComposition
{
    internal const string Yinzhi = "anniversary:yinzhi", Keming = "anniversary:keming";
    [ProgramCatalogValidation(39700)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => e.Op is >= SkillProgramEffectOp.ApplyShenCaoPiYinzhi and <= SkillProgramEffectOp.ArmKemingSkillEcho)) return;
        var valid = p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.CardPolicies.Count == 0 && p.BooleanStates.Count == 0 && p.CardIdentities.Count == 0 && p.Contributions.Count == 0 &&
            (p.Id == Yinzhi && p.Activations is [{ Id: "edict", MinCards: 0, MaxCards: 0, MinTargets: 1, MaxTargets: 1, TargetKind: SkillProgramTargetKind.OtherLiving, UsesPerPhase: 1,
                Effects: [{ Op: SkillProgramEffectOp.ApplyShenCaoPiYinzhi, Target: SkillProgramEffectTarget.Owner }] }] &&
                p.Triggers is [{ Id: "hand-use-reward", Window: SkillProgramTriggerWindow.CardUseCompleted, OwnerRelation: SkillProgramCardActionOwnerRelation.Observer, Optional: false, IncludeResponseUses: true, IsSkillInvocation: false,
                    Effects: [{ Op: SkillProgramEffectOp.DrawYinzhiHandAction, Target: SkillProgramEffectTarget.Owner }] },
                    { Id: "hand-response-reward", Window: SkillProgramTriggerWindow.CardResponseCompleted, OwnerRelation: SkillProgramCardActionOwnerRelation.Observer, Optional: false, IsSkillInvocation: false,
                    Effects: [{ Op: SkillProgramEffectOp.DrawYinzhiHandAction, Target: SkillProgramEffectTarget.Owner }] }] ||
             p.Id == Keming && p.Activations.Count == 0 && p.Triggers is [
                { Id: "borrow-damaged-skill", Window: SkillProgramTriggerWindow.AfterDamageApplied, Subject: SkillProgramTriggerSubject.Owner, Optional: true, DamageOccurrence: SkillProgramDamageOccurrence.PerDamage,
                    Effects: [{ Op: SkillProgramEffectOp.ResolveKemingDamagedSkill, Target: SkillProgramEffectTarget.Owner }] },
                { Id: "arm-skill-echo", Window: SkillProgramTriggerWindow.AfterDamageApplied, Subject: SkillProgramTriggerSubject.DamageSource, Optional: false, IsSkillInvocation: false, DamageOccurrence: SkillProgramDamageOccurrence.PerDamage,
                    Effects: [{ Op: SkillProgramEffectOp.ArmKemingSkillEcho, Target: SkillProgramEffectTarget.Owner }] }]);
        if (!valid) throw new InvalidOperationException(path + ": Shen Cao Pi requires his complete official activation, native completed-card rewards and both damage branches.");
    }
}
