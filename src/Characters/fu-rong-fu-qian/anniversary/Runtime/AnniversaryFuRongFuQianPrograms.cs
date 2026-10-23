using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryFuRongFuQianHost { SkillProgramStepOutcome Begin(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class AnniversaryFuRongFuQianDescriptor(SkillProgramEffectOp operation) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => operation;
    public override ISkillProgramEffectHandler Handler => new AnniversaryFuRongFuQianHandler(operation);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(operation switch
    {
        SkillProgramEffectOp.ArmAnniversaryFuRongFuQianXuewei => SkillProgramTriggerWindow.TurnEnding,
        SkillProgramEffectOp.ResolveAnniversaryFuRongFuQianXuewei => SkillProgramTriggerWindow.BeforeDamageApplied,
        _ => SkillProgramTriggerWindow.AfterTurnEnded
    })];
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class ArmAnniversaryFuRongFuQianXueweiDescriptor() : AnniversaryFuRongFuQianDescriptor(SkillProgramEffectOp.ArmAnniversaryFuRongFuQianXuewei);
internal sealed class ResolveAnniversaryFuRongFuQianXueweiDescriptor() : AnniversaryFuRongFuQianDescriptor(SkillProgramEffectOp.ResolveAnniversaryFuRongFuQianXuewei);
internal sealed class ResolveAnniversaryFuRongFuQianYuguanDescriptor() : AnniversaryFuRongFuQianDescriptor(SkillProgramEffectOp.ResolveAnniversaryFuRongFuQianYuguan);
internal sealed class AnniversaryFuRongFuQianHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<IAnniversaryFuRongFuQianHost>().Begin(frame, operation);
}
internal static class AnniversaryFuRongFuQianComposition
{
    internal const string Xuewei = "anniversary:xuewei", Yuguan = "anniversary:yuguan", Arm = "arm-until-next-actual-turn",
        Protect = "prevent-and-pay-once-per-damage", Refill = "maximum-lost-hp-actual-turn-end";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ArmAnniversaryFuRongFuQianXuewei or
        SkillProgramEffectOp.ResolveAnniversaryFuRongFuQianXuewei or SkillProgramEffectOp.ResolveAnniversaryFuRongFuQianYuguan;
    [ProgramCatalogValidation(44700)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Any(e => Handles(e.Op))) return;
        if (p.Id is not (Xuewei or Yuguan) || p.Triggers.Count != (p.Id == Xuewei ? 2 : 1) || p.Activations.Count != 0 || p.ViewAs.Count != 0 ||
            p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0 ||
            p.Triggers.Any(t => !Exact(p.Id, t)))
            throw new InvalidOperationException(path + ": Fu Rong Fu Qian requires his exact ending protection grant, whole-damage replacement and actual-turn-end maximum-HP payment.");
    }
    private static bool Exact(string skill, SkillProgramTrigger t) => t.OwnerRelation is null && t.Priority == 0 &&
        t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageScope is null && t.UsageLimit is null && t.MarkerCost is null &&
        t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && t.Suits.Count == 0 && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 &&
        t.SourceSkillId is null && !t.IncludeResponseUses && t.DamageOccurrence is null &&
        t.Effects is [{ Target: SkillProgramEffectTarget.Owner, TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always } e] &&
        (skill == Xuewei && t.Id == Arm && t.Window == SkillProgramTriggerWindow.TurnEnding && t.Subject == SkillProgramTriggerSubject.Owner &&
             t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own && t.Optional && t.IsSkillInvocation && e.Op == SkillProgramEffectOp.ArmAnniversaryFuRongFuQianXuewei ||
         skill == Xuewei && t.Id == Protect && t.Window == SkillProgramTriggerWindow.BeforeDamageApplied && t.Subject == SkillProgramTriggerSubject.Any &&
             !t.Optional && !t.IsSkillInvocation && e.Op == SkillProgramEffectOp.ResolveAnniversaryFuRongFuQianXuewei ||
         skill == Yuguan && t.Id == Refill && t.Window == SkillProgramTriggerWindow.AfterTurnEnded && t.Subject == SkillProgramTriggerSubject.Owner &&
             t.TurnOwnerScope == SkillProgramTurnOwnerScope.AnyLiving && t.Optional && t.IsSkillInvocation && e.Op == SkillProgramEffectOp.ResolveAnniversaryFuRongFuQianYuguan);
}

// Public invoices contain scalar facts only. Native draw material identities stay on their owning frame.
public sealed record AnniversaryXueweiGrant(long FrameId, CardConversionSource Source, string Hash, int ActualTurn, int TargetSeat, int OwnerHp, int TargetHp);
public sealed record AnniversaryXueweiArmedEvent(AnniversaryXueweiGrant Grant) : IGameEvent;
public sealed record AnniversaryXueweiExpiredEvent(long GrantFrameId, int ActualTurn) : IGameEvent;
public sealed record AnniversaryFuRongFuQianStartedEvent(long FrameId, CardConversionSource Source, string Hash, SkillProgramEffectOp Operation, long ParentId, int ActualTurn, long GrantFrameId, int TargetSeat, int Amount) : IGameEvent;
public sealed record AnniversaryXueweiHpPaidEvent(long FrameId, int Before, int After, int ActualLoss) : IGameEvent;
public sealed record AnniversaryYuguanMaximumPaidEvent(long FrameId, int MaximumBefore, int MaximumAfter, int HpBefore, int HpAfter, int LostHp, int TargetCount) : IGameEvent;
public sealed record AnniversaryFuRongFuQianTargetChosenEvent(long FrameId, int Index, int TargetSeat) : IGameEvent;
public sealed record AnniversaryFuRongFuQianDrawReturnedEvent(long FrameId, int Index, int RecipientSeat, int Requested, int ActualCount, long? AttemptId, long Before, long After) : IGameEvent;
public sealed record AnniversaryFuRongFuQianCompletedEvent(long FrameId) : IGameEvent;
public enum AnniversaryFuRongFuQianStage { ArmChoice, HpChildren, MaximumChildren, TargetsChoice, Drawing, DrawChildren }
public sealed record AnniversaryFuRongFuQianDraw(int Index, int RecipientSeat, int Requested, NativeDrawOutcome Outcome);
public sealed record AnniversaryFuRongFuQianReceipt(CardConversionSource Source, string Hash, SkillProgramEffectOp Operation, long ParentId, int ActualTurn,
    AnniversaryFuRongFuQianStage Stage, AnniversaryXueweiGrant? Grant = null, int ProtectedSeat = -1, int PreventedAmount = 0,
    int HpBefore = 0, bool HpPaid = false, int HpAfter = 0, int ActualLoss = 0, int MaximumBefore = 0, int MaximumAfter = 0,
    int HpBeforeMaximum = 0, int HpAfterMaximum = 0, int LostHp = 0, int TargetCount = 0, int Cursor = 0, int DrawRecipient = -1, int RequestedDraw = 0)
{
    private readonly IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<AnniversaryFuRongFuQianDraw> _draws = Array.AsReadOnly(Array.Empty<AnniversaryFuRongFuQianDraw>());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<AnniversaryFuRongFuQianDraw> Draws { get => _draws; init => _draws = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryFuRongFuQianReceipt? AnniversaryFuRongFuQian { get; init; }
}
