using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryXiahouEnHost { SkillProgramStepOutcome Begin(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class AnniversaryXiahouEnDescriptor(SkillProgramEffectOp operation) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => operation;
    public override ISkillProgramEffectHandler Handler => new AnniversaryXiahouEnHandler(operation);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(operation switch
    {
        SkillProgramEffectOp.ResolveAnniversaryXiahouEnShiwu => SkillProgramTriggerWindow.OtherActualTurnStarted,
        SkillProgramEffectOp.ResolveAnniversaryXiahouEnShiwuDamage => SkillProgramTriggerWindow.AfterDamageApplied,
        _ => SkillProgramTriggerWindow.OwnerDied
    })];
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) =>
        operation == SkillProgramEffectOp.BequeathAnniversaryXiahouEnChijian || context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class ResolveAnniversaryXiahouEnShiwuDescriptor() : AnniversaryXiahouEnDescriptor(SkillProgramEffectOp.ResolveAnniversaryXiahouEnShiwu);
internal sealed class ResolveAnniversaryXiahouEnShiwuDamageDescriptor() : AnniversaryXiahouEnDescriptor(SkillProgramEffectOp.ResolveAnniversaryXiahouEnShiwuDamage);
internal sealed class BequeathAnniversaryXiahouEnChijianDescriptor() : AnniversaryXiahouEnDescriptor(SkillProgramEffectOp.BequeathAnniversaryXiahouEnChijian);
internal sealed class AnniversaryXiahouEnHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<IAnniversaryXiahouEnHost>().Begin(frame, operation);
}
internal static class AnniversaryXiahouEnComposition
{
    internal const string Chijian = "anniversary:chijian", Shiwu = "anniversary:shiwu", Duel = "other-actual-turn-duel", Damage = "duel-actual-damage", Death = "killer-obtains-sword";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveAnniversaryXiahouEnShiwu or
        SkillProgramEffectOp.ResolveAnniversaryXiahouEnShiwuDamage or SkillProgramEffectOp.BequeathAnniversaryXiahouEnChijian;
    [ProgramCatalogValidation(45000)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Any(e => Handles(e.Op))) return;
        if (p.Id != Shiwu || p.Triggers.Count != 3 || p.Triggers.Select(t => t.Id).Distinct().Count() != 3 || p.Activations.Count != 0 || p.ViewAs.Count != 0 ||
            p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0 || p.Triggers.Any(t => !Exact(t)))
            throw new InvalidOperationException(path + ": Shiwu requires the exact optional actual-turn Duel, its actual-damage settlement and real killer bequest.");
    }
    private static bool Exact(SkillProgramTrigger t) => t.OwnerRelation is null && t.Priority == 0 && t.Condition.Kind == SkillProgramTriggerConditionKind.Always &&
        t.UsageScope is null && t.UsageLimit is null && t.MarkerCost is null && t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && t.Suits.Count == 0 &&
        t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 && t.SourceSkillId is null && !t.IncludeResponseUses &&
        t.Effects is [{ Target: SkillProgramEffectTarget.Owner, TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always } e] &&
        (t.Id == Duel && t.Window == SkillProgramTriggerWindow.OtherActualTurnStarted && t.Subject == SkillProgramTriggerSubject.Owner && t.Optional && t.IsSkillInvocation && t.DamageOccurrence is null && e.Op == SkillProgramEffectOp.ResolveAnniversaryXiahouEnShiwu ||
         t.Id == Damage && t.Window == SkillProgramTriggerWindow.AfterDamageApplied && t.Subject == SkillProgramTriggerSubject.Any && !t.Optional && !t.IsSkillInvocation && t.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage && e.Op == SkillProgramEffectOp.ResolveAnniversaryXiahouEnShiwuDamage ||
         t.Id == Death && t.Window == SkillProgramTriggerWindow.OwnerDied && t.Subject == SkillProgramTriggerSubject.Owner && !t.Optional && !t.IsSkillInvocation && t.DamageOccurrence is null && e.Op == SkillProgramEffectOp.BequeathAnniversaryXiahouEnChijian);
}

public sealed record XiahouEnShiwuOrigin(long FrameId, int InstructionIndex, CardConversionSource Source, string Hash,
    int ActualTurn, int TurnOwner, long ParentWindowId, long UseId, long ActionId);
public enum AnniversaryXiahouEnStage { NativeUse, NativeReturned, GainChildren, SkillChildren }
public sealed record AnniversaryXiahouEnReceipt(CardConversionSource Source, string Hash, SkillProgramEffectOp Operation, long ParentId,
    int ActualTurn, int TurnOwner, AnniversaryXiahouEnStage Stage, XiahouEnShiwuOrigin? Origin = null, long DamageFrameId = 0,
    int DamageSource = -1, int DamageTarget = -1, int DamageAmount = 0, long Before = 0, long After = 0, int? GainedCardId = null);
public sealed record XiahouEnShiwuIssuedEvent(XiahouEnShiwuOrigin Origin) : IGameEvent;
public sealed record XiahouEnShiwuReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record XiahouEnShiwuDamageBoundEvent(long FrameId, XiahouEnShiwuOrigin Origin, long DamageFrameId, int SourceSeat, int TargetSeat, int Amount) : IGameEvent;
public sealed record XiahouEnShiwuGainPaidEvent(long FrameId, long Before, long After, int ActualCount) : IGameEvent;
public sealed record XiahouEnChijianGrantedEvent(long FrameId, int OwnerSeat, int RecipientSeat, bool DeathBequest) : IGameEvent;
public sealed record XiahouEnChijianLostGrantEvent(long FrameId, int OwnerSeat, int ActualTurn, int TurnOwner, string GeneralId,
    string? SecondaryGeneralId, SkillGrant Grant) : IGameEvent;
public sealed record XiahouEnChijianRestoredEvent(long FrameId, int OwnerSeat, string GrantId, int ActualTurn, int TurnOwner, string Outcome) : IGameEvent;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryXiahouEnReceipt? AnniversaryXiahouEn { get; init; }
}
public sealed partial record ProgramSelectedActorDuelOrigin
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public XiahouEnShiwuOrigin? XiahouEnShiwu { get; init; }
}
