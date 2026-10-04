using System.Text.Json.Serialization;

namespace CardGame.Core;

// Definition collections clone at construction, init/with and JSON boundaries.
public sealed record PublicDeathDamageCostPolicy
{
    private IReadOnlyList<Role> _waiveHpDeadRoles = Array.AsReadOnly(Array.Empty<Role>());
    private IReadOnlyList<Role> _anyColorDeadRoles = Array.AsReadOnly(Array.Empty<Role>());
    private IReadOnlyList<Role> _allowEquipmentDeadRoles = Array.AsReadOnly(Array.Empty<Role>());
    public string QualifierSkillId { get; init; } = "";
    public IReadOnlyList<Role> WaiveHpDeadRoles { get => _waiveHpDeadRoles; init => _waiveHpDeadRoles = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<Role> AnyColorDeadRoles { get => _anyColorDeadRoles; init => _anyColorDeadRoles = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<Role> AllowEquipmentDeadRoles { get => _allowEquipmentDeadRoles; init => _allowEquipmentDeadRoles = Array.AsReadOnly(value.ToArray()); }
}
public enum SignedDamagePaymentStage { ChoosingCost, Paid, Adjusted }
public sealed record ProgramSignedDamagePaymentReceipt(int InstructionIndex, long BeforeDamageFrameId,
    long OriginalAttackFrameId, int SourceSeat, int TargetSeat, DamageNature Nature, int OriginalAmount,
    int Delta, int OwnerHp, int OtherHp, bool WaiveHp, bool AnyColor, bool AllowEquipment,
    int PublicDeadRoleMask, string? QualifierInstanceId, SignedDamagePaymentStage Stage,
    int? CostCardId = null, CardLocation? CostFrom = null, Suit? CostEffectiveSuit = null,
    long CostMovementSequence = 0, int? FinalAmount = null, bool Cancelled = false);
// The base is frozen only for an actual elemental propagation. Each recipient
// reads that base; local adjustments never overwrite the propagation amount.
public sealed record RecipientScopedDamageState(int FirstTargetSeat, int CurrentTargetSeat, int PropagationBaseAmount);
public sealed record ProgramSignedDamageOfferedEvent(long ProgramFrameId, long BeforeDamageFrameId,
    long OriginalAttackFrameId, int OwnerSeat, int SourceSeat, int TargetSeat, DamageNature Nature,
    int Amount, int Delta, int OwnerHp, int OtherHp, bool WaiveHp, bool AnyColor, bool AllowEquipment,
    int PublicDeadRoleMask, string? QualifierInstanceId) : IGameEvent;
public sealed record ProgramSignedDamagePaidEvent(long ProgramFrameId, long BeforeDamageFrameId,
    long OriginalAttackFrameId, int OwnerSeat, int SourceSeat, int TargetSeat, int Delta,
    int CostCardId, CardLocation CostFrom, Suit EffectiveSuit, long MovementSequence,
    bool WaiveHp, bool AnyColor, bool AllowEquipment, int PublicDeadRoleMask, string? QualifierInstanceId) : IGameEvent;
public sealed record ProgramSignedDamageAdjustedEvent(long ProgramFrameId, long BeforeDamageFrameId,
    long OriginalAttackFrameId, int SourceSeat, int TargetSeat, int BeforeAmount, int AfterAmount,
    int Delta, bool Cancelled) : IGameEvent;
public sealed record RecipientScopedDamageBaseEvent(long AttackFrameId, int FirstTargetSeat, int Amount) : IGameEvent;
public sealed record RecipientScopedDamageAdvancedEvent(long AttackFrameId, int FromSeat, int TargetSeat, int Amount) : IGameEvent;

internal interface ISignedDamagePaymentHost
{
    SkillProgramStepOutcome DiscardOwnedCardToAdjustCurrentDamage(ProgramSkillFrame frame);
}
internal sealed class DiscardOwnedCardToAdjustCurrentDamageDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardOwnedCardToAdjustCurrentDamage;
    public override ISkillProgramEffectHandler Handler { get; } = new SignedDamagePaymentHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (e, c) => { if (e.Amount < 0) c.PreventCurrentDamage(e); else c.Damage(e); });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "suits", "qualifierSkillId", "waiveHpDeadRoles", "anyColorDeadRoles", "allowEquipmentDeadRoles", "condition");
        var amount = r.RequiredInt("amount"); var suits = r.RequiredEnumArray<Suit>("suits");
        var expected = amount == 1 ? new[] { Suit.Spade, Suit.Club } : new[] { Suit.Heart, Suit.Diamond };
        if (amount is not (-1 or 1) || suits.Count != 2 || !suits.Order().SequenceEqual(expected.Order()))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: signed one damage requires its exact black/red hand cost.");
        var policy = new PublicDeathDamageCostPolicy { QualifierSkillId = r.RequiredIdentifier("qualifierSkillId"),
            WaiveHpDeadRoles = r.RequiredEnumArray<Role>("waiveHpDeadRoles"),
            AnyColorDeadRoles = r.RequiredEnumArray<Role>("anyColorDeadRoles"),
            AllowEquipmentDeadRoles = r.RequiredEnumArray<Role>("allowEquipmentDeadRoles") };
        if (policy.WaiveHpDeadRoles.Concat(policy.AnyColorDeadRoles).Concat(policy.AllowEquipmentDeadRoles)
            .Any(role => role is not (Role.Loyalist or Role.Rebel or Role.Renegade)))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: only public deceased identity roles qualify.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), amount, r.Condition(), suits: suits)
            { PublicDeathDamageCost = policy };
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];
}
internal sealed class SignedDamagePaymentHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardOwnedCardToAdjustCurrentDamage;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((ISignedDamagePaymentHost)host).DiscardOwnedCardToAdjustCurrentDamage(f);
}
internal static class SignedDamagePaymentComposition
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow? window, SkillProgramTriggerSubject? subject)
    {
        if (!effects.Any(e => e.Op == SkillProgramEffectOp.DiscardOwnedCardToAdjustCurrentDamage)) return;
        if (effects.Count != 1 || window != SkillProgramTriggerWindow.BeforeDamageApplied ||
            effects[0].PublicDeathDamageCost is null || subject != (effects[0].Amount > 0 ? SkillProgramTriggerSubject.DamageSource : SkillProgramTriggerSubject.DamageTarget))
            throw new InvalidOperationException($"Invalid skill program at {path}: signed damage cost requires standalone matching before-damage subject.");
    }
}
