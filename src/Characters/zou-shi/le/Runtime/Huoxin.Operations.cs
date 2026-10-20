using System.Text.Json.Serialization;

namespace CardGame.Core;
public enum HuoxinStage { TargetChoice, CardChoice, ObtainChoice, ObtainChildren }
public sealed record HuoxinReceipt(CardConversionSource Source, string GameplayHash, long WindowId,
    long UseId, long ActionId, CardKind EffectiveKind, Suit EffectiveSuit)
{
    public HuoxinStage Stage { get; init; }
    public int TargetSeat { get; init; } = -1;
    private readonly IReadOnlyList<int> _candidates = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Candidates { get => _candidates; init => _candidates = Array.AsReadOnly(value.ToArray()); }
    public int? CardId { get; init; }
    public CardKind? PrintedKind { get; init; }
    public Suit ShownEffectiveSuit { get; init; }
    public bool WasMarked { get; init; }
    public bool MayObtain { get; init; }
    public long BatchId { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long PaidSequence { get; init; }
    public bool Paid { get; init; }
}
public sealed record HuoxinStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long WindowId, long UseId, long ActionId, CardKind EffectiveKind, Suit EffectiveSuit) : IGameEvent;
public sealed record HuoxinEntityShownEvent(long FrameId, int TargetSeat, int CardId, CardKind PrintedKind,
    Suit EffectiveSuit, bool WasMarked, bool MayObtain) : IGameEvent;
public sealed record HuoxinEntityMarkedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int TargetSeat, int CardId, CardKind PrintedKind) : IGameEvent;
public sealed record HuoxinObtainPaidEvent(long FrameId, long BatchId, int CardId, CardKind PrintedKind,
    CardLocation From, CardLocation To, long MovementSequence, long Before, long After) : IGameEvent;
public sealed record HuoxinCompletedEvent(long FrameId, bool Marked, bool Obtained) : IGameEvent;
internal sealed class ResolveHuoxinNonEquipmentUseDescriptor() : LeZouShiDescriptor(SkillProgramEffectOp.ResolveHuoxinNonEquipmentUse){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class HuoxinCommittedUseContract : IProgramCommittedUseContract
{
    public string SkillId => "le:huoxin";
    public void ValidateTrigger(SkillProgramTrigger t)
    {
        if (t.Id != "non-equipment-use" || t.Window != SkillProgramTriggerWindow.CardUseCommitted ||
            t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || !t.IncludeResponseUses ||
            !t.SingleActionInstance || !t.AllowNoEventTarget || !t.Optional || t.Subject is not null ||
            t.SourceSkillId is not null || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 ||
            t.Effects is not [{ Op: SkillProgramEffectOp.ResolveHuoxinNonEquipmentUse, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException("Huoxin requires its optional complete non-equipment true-use actor contract, including native response uses.");
    }
}
