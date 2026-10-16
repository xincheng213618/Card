using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record CaoXiNumbers(int AttackRange, int DrawCount, int SlashLimit)
{
    public int Value(SkillRuleQuery query) => query switch { SkillRuleQuery.AttackRange => AttackRange, SkillRuleQuery.DrawCount => DrawCount, SkillRuleQuery.SlashLimit => SlashLimit, _ => throw new InvalidOperationException("Not a Gangshu number.") };
    public bool Contains(int count) => count == AttackRange || count == DrawCount || count == SlashLimit;
}
public sealed record CaoXiGangshuReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    long ParentWindowId, long CardUseFrameId, long ActionId, CaoXiNumbers Numbers);
public enum CaoXiJianxuanStage { ChoosingRecipient, DrawChildren }
public sealed record CaoXiJianxuanReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    long ParentWindowId, long DamageFrameId, int ActualTurn, CaoXiJianxuanStage Stage = CaoXiJianxuanStage.ChoosingRecipient,
    int? RecipientSeat = null, int Cursor = 0, long Before = 0, long After = 0, int ActualDrawCount = 0);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CaoXiGangshuReceipt? CaoXiGangshu { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CaoXiJianxuanReceipt? CaoXiJianxuan { get; init; }
}
public sealed record CaoXiGangshuStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long ParentWindowId, long CardUseFrameId, long ActionId, CaoXiNumbers Numbers) : IGameEvent;
public sealed record CaoXiGangshuIncreasedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    SkillRuleQuery Query, int BeforeValue, int AfterValue, long? GrantedInDrawPhaseToken = null, long GrantedAfterActualPhaseToken = 0) : IGameEvent;
public sealed record CaoXiGangshuUnitExpiredEvent(long GrantFrameId, int OwnerSeat, string Reason, long NativeId) : IGameEvent;
public sealed record CaoXiGangshuActualOffsetEvent(long NativeId, int OwnerSeat, string Kind, int Cursor) : IGameEvent;
public sealed record CaoXiGangshuDrawCaptureEvent(ActualDiscardRecoveryPhaseKey Phase, int OwnerSeat) : IGameEvent;
public sealed record CaoXiGangshuDrawUnitIssuedEvent(ActualDiscardRecoveryPhaseKey Phase, long GrantFrameId, CardConversionSource Source) : IGameEvent;
public sealed record CaoXiJianxuanStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long ParentWindowId, long DamageFrameId, int ActualTurn) : IGameEvent;
public sealed record CaoXiJianxuanRecipientEvent(long FrameId, int RecipientSeat) : IGameEvent;
public sealed record CaoXiJianxuanDrawPaidEvent(long FrameId, int Cursor, int RecipientSeat, int ActualDrawCount, long Before, long After) : IGameEvent;
public sealed record CaoXiJianxuanComparedEvent(long FrameId, int Cursor, int RecipientSeat, int HandCount, CaoXiNumbers Numbers, bool Repeat) : IGameEvent;
public sealed record CaoXiJianxuanCompletedEvent(long FrameId, int RecipientSeat, int DrawCalls) : IGameEvent;

internal interface ICaoXiProgramHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal sealed class CaoXiHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) => host.GetCapability<ICaoXiProgramHost>().Resolve(frame, operation);
}
internal abstract class CaoXiDescriptor(SkillProgramEffectOp operation) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => operation;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new CaoXiHandler(operation);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(2));
}
internal sealed class ResolveGangshuAttributeIncrementDescriptor() : CaoXiDescriptor(SkillProgramEffectOp.ResolveGangshuAttributeIncrement)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)]; }
internal sealed class ResolveJianxuanRepeatedDrawDescriptor() : CaoXiDescriptor(SkillProgramEffectOp.ResolveJianxuanRepeatedDraw)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)]; }

internal static class CaoXiComposition
{
    private static bool Own(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveGangshuAttributeIncrement or SkillProgramEffectOp.ResolveJianxuanRepeatedDraw;
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (!p.Activations.Any(a => a.Effects.Any(e => Own(e.Op))) && !p.Triggers.Any(t => t.Effects.Any(e => Own(e.Op)))) return;
        if (p.Activations.Count != 0 || p.Triggers.Count != 1 || p.Modifiers.Count != 0 || p.ViewAs.Count != 0 || p.Contributions.Count != 0 ||
            p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0 || p.DamageModifiers.Count != 0 || p.CardPolicies.Count != 0 || p.HpSkillGrants.Count != 0 ||
            p.EquipmentSuitSkillGrants.Count != 0 || p.LordSkillProjection || p.CannotChallengeDeclarations)
            throw new InvalidOperationException(path + ": Cao Xi requires one standalone original-source trigger without unrelated policy entries.");
        var t = p.Triggers[0]; var gangshu = p.Id == "ol:gangshu";
        if (!gangshu && p.Id != "ol:jianxuan" || t.Effects is not [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            t.Effects[0].Op != (gangshu ? SkillProgramEffectOp.ResolveGangshuAttributeIncrement : SkillProgramEffectOp.ResolveJianxuanRepeatedDraw) ||
            !t.Optional || t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.UsageScope is not null || t.UsageLimit is not null || t.ChoiceGroup is not null ||
            t.NamedUsageGroup is not null || t.SourceSkillId is not null || t.SourceViewAsId is not null || t.CardKinds.Count != 0 || t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 ||
            t.Suits.Count != 0 || t.DamageCardKinds.Count != 0 || t.MovementReasons.Count != 0 || t.ExcludedMovementReasons.Count != 0 || t.ExcludedReasons.Count != 0 ||
            t.JudgmentReasons.Count != 0 || t.JudgmentSource is not null || t.MovementOccurrence is not null || t.RequireDamageSource is not null || t.RequireNoCardConversion is not null ||
            t.MarkerCost is not null || t.DynamicUsageLimit is not null || t.GainPhaseQualification is not null || t.AllowNoEventTarget || t.SingleActionInstance || t.NoDyingAtActivation ||
            t.DeferredTurnEndOnly || t.OnlyDesignatedCardTargets || t.AllowOwnDiscardPhaseEnded || t.EvaluateConditionAtResolution || t.IgnoreOwnSkillMovements || t.MovementDiscardOnly ||
            (gangshu ? t.Window != SkillProgramTriggerWindow.CardUseCompleted || t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || t.Subject is not null || !t.IncludeResponseUses ||
                !t.CardCategories.Order().SequenceEqual(new[] { SkillProgramCardCategory.Equipment, SkillProgramCardCategory.Trick }.Order()) || t.DamageOccurrence is not null :
                t.Window != SkillProgramTriggerWindow.AfterDamageApplied || t.Subject != SkillProgramTriggerSubject.Owner || t.OwnerRelation is not null || t.IncludeResponseUses ||
                t.CardCategories.Count != 0 || t.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage))
            throw new InvalidOperationException(path + ": Cao Xi requires exact completed nonbasic use or optional per-instance actual owner damage.");
    }
}
