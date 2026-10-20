using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed record JiyunCompletedEntity(long LeaseId, int CardId, CardKind Kind, long PaymentSequence);
public sealed record JiyunCompletedUseContext(CardConversionSource Source, string GameplayHash, PublicPersistentPileSource Pile,
    long NativeUseId, long ActionId, ProgramCompletedResponseKind? ResponseKind, int EntityCount);
public sealed record JiyunInitialEntityStoredEvent(long FrameId, int Index, int CardId, CardKind PrintedKind, long MovementSequence) : IGameEvent;
public sealed record JiyunRecipientFrozenEvent(long FrameId, int Index, int RecipientSeat) : IGameEvent;
public sealed record JiyunRecipientLimitFrozenEvent(long FrameId, int MaximumRecipients) : IGameEvent;
public sealed record JiyunPreparationDrawReturnedEvent(long FrameId, int Index, int RecipientSeat, int ActualCount, int ReplacementCount, long Before, long After, long? AttemptId) : IGameEvent;
public sealed record JiyunLeaseIssuedEvent(long LeaseId, long FrameId, int RecipientIndex, CardConversionSource Source, string GameplayHash, PublicPersistentPileSource Pile, int RecipientSeat, int CardId, CardKind PrintedKind, long MovementSequence) : IGameEvent;
public sealed record JiyunLeaseDepartedEvent(long LeaseId, long BatchId, long? ParentFrameId, int CardId, CardLocation From, CardLocation To, CardMoveReason Reason, long MovementSequence) : IGameEvent;
public sealed record JiyunLeaseMovementEvent(long LeaseId, long BatchId, long? ParentFrameId, int CardId, CardLocation From, CardLocation To, CardMoveReason Reason, long MovementSequence, bool TrueUseDiscard) : IGameEvent;
public sealed record JiyunCompletedUseLeaseIssuedEvent(CardConversionSource Source, string GameplayHash, PublicPersistentPileSource Pile, long NativeUseId, long ActionId, ProgramCompletedResponseKind? ResponseKind, long LeaseId, int CardId, CardKind PrintedKind, long PaymentSequence) : IGameEvent;
public sealed record JiyunClaimedEntityEvent(long FrameId, long NativeUseId, long ActionId, ProgramCompletedResponseKind? ResponseKind, long LeaseId, int CardId, CardKind PrintedKind, CardLocation From, CardLocation To, long MovementSequence) : IGameEvent;
public sealed record JiyunLeaseResolvedEvent(long FrameId, long ActionId, long LeaseId, bool Claimed) : IGameEvent;
internal sealed class InitializeJiyunCategoryGrainDescriptor() : LiFengDescriptor(SkillProgramEffectOp.InitializeJiyunCategoryGrain){
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeTrueUseProcessingEntry);

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.LiFeng is { Paid: true };
}
internal sealed class ResolveJiyunPreparationDrawLeasesDescriptor() : LiFengDescriptor(SkillProgramEffectOp.ResolveJiyunPreparationDrawLeases){
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeTrueUseProcessingEntry);

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.LiFeng is { Paid: true };
}
internal sealed class CollectJiyunCompletedUseEntitiesDescriptor() : LiFengDescriptor(SkillProgramEffectOp.CollectJiyunCompletedUseEntities){
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeTrueUseProcessingEntry);

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.LiFeng is { Paid: true };
}
internal sealed class JiyunCompletedUseContract : IProgramCommittedUseContract
{
    public string SkillId => "ol:jiyun";
    public void ValidateTrigger(SkillProgramTrigger t)
    {
        if (t.Window != SkillProgramTriggerWindow.CardUseCompleted || !t.IncludeResponseUses || !t.AllowNoEventTarget || !t.SingleActionInstance || t.Optional || t.OwnerRelation != SkillProgramCardActionOwnerRelation.Observer || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.Effects is not [{ Op: SkillProgramEffectOp.CollectJiyunCompletedUseEntities, Target: SkillProgramEffectTarget.Owner }])
            throw new InvalidOperationException("Jiyun requires the exact completed native use and original drawn entity leases.");
    }
}
