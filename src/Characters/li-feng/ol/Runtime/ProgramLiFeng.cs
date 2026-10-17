using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum LiFengStage { InitialChildren, PreparationTargets, PreparationDraw, PreparationChildren, CollectNext, CollectChildren, DistributionChoice, DistributionChildren }
public sealed record LiFengEntity(int CardId, CardKind Kind, CardLocation From);
public sealed record JiyunCompletedEntity(long LeaseId, int CardId, CardKind Kind, long PaymentSequence);
public sealed record JiyunCompletedUseContext(CardConversionSource Source, string GameplayHash, PublicPersistentPileSource Pile,
    long NativeUseId, long ActionId, ProgramCompletedResponseKind? ResponseKind, int EntityCount);
public sealed partial record ProgramSkillWindowContext
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public JiyunCompletedUseContext? Jiyun { get; init; } }
public sealed record LiFengReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op,
    long WindowId, int ActualTurn, int TurnOwner, PublicPersistentPileSource Pile, LiFengStage Stage)
{
    public bool Paid { get; init; }
    public int MaximumRecipients { get; init; }
    public int Cursor { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public int ActiveCardId { get; init; }
    public int ActiveRecipient { get; init; } = -1;
    public CardLocation? ActiveFrom { get; init; }
    public NativeDrawOutcome? DrawOutcome { get; init; }
    public JiyunCompletedUseContext? CompletedUse { get; init; }
    private readonly IReadOnlyList<int> _recipients = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<LiFengEntity> _entities = Array.AsReadOnly(Array.Empty<LiFengEntity>());
    private readonly IReadOnlyList<int> _drawIds = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<int> _distributed = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<JiyunCompletedEntity> _completedEntities = Array.AsReadOnly(Array.Empty<JiyunCompletedEntity>());
    public IReadOnlyList<int> Recipients { get => _recipients; init => _recipients = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<LiFengEntity> Entities { get => _entities; init => _entities = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> ActualDrawIds { get => _drawIds; init => _drawIds = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> DistributedIds { get => _distributed; init => _distributed = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<JiyunCompletedEntity> CompletedEntities { get => _completedEntities; init => _completedEntities = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public LiFengReceipt? LiFeng { get; init; } }

// These facts contain no mutable collections; the owning frame freezes all cursors.
public sealed record LiFengStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, long WindowId, int ActualTurn, int TurnOwner, PublicPersistentPileSource Pile) : IGameEvent;
public sealed record LiFengPaidEvent(long FrameId, SkillProgramEffectOp Op) : IGameEvent;
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
public sealed record ShuliangGrainDistributedEvent(long FrameId, int CardId, int RecipientSeat, CardLocation From, long MovementSequence) : IGameEvent;
public sealed record ShuliangDistributionEntityFrozenEvent(long FrameId, int Index, int CardId, CardKind PrintedKind, CardLocation From) : IGameEvent;
public sealed record ShuliangGrainPaidEvent(long ParentFrameId, long ActionId, CardConversionSource Source, int ProviderSeat, int ActualTurn, int TurnOwner, int CardId, CardLocation From, long MovementSequence, bool Played) : IGameEvent;
public sealed record LiFengCompletedEvent(long FrameId, SkillProgramEffectOp Op, bool Paid) : IGameEvent;

internal interface ILiFengHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class LiFengDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(op switch
    {
        SkillProgramEffectOp.InitializeJiyunCategoryGrain => SkillProgramTriggerWindow.GameStarting,
        SkillProgramEffectOp.ResolveJiyunPreparationDrawLeases => SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,
        SkillProgramEffectOp.CollectJiyunCompletedUseEntities => SkillProgramTriggerWindow.CardUseCompleted,
        _ => SkillProgramTriggerWindow.TurnEnding
    })];
    public override ProgramOperationInteraction Interaction => op is SkillProgramEffectOp.InitializeJiyunCategoryGrain or SkillProgramEffectOp.CollectJiyunCompletedUseEntities ? ProgramOperationInteraction.Automatic : ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(1));
    public override ISkillProgramEffectHandler Handler { get; } = new LiFengHandler(op);
}
internal sealed class InitializeJiyunCategoryGrainDescriptor() : LiFengDescriptor(SkillProgramEffectOp.InitializeJiyunCategoryGrain);
internal sealed class ResolveJiyunPreparationDrawLeasesDescriptor() : LiFengDescriptor(SkillProgramEffectOp.ResolveJiyunPreparationDrawLeases);
internal sealed class CollectJiyunCompletedUseEntitiesDescriptor() : LiFengDescriptor(SkillProgramEffectOp.CollectJiyunCompletedUseEntities);
internal sealed class DistributeShuliangEndingGrainDescriptor() : LiFengDescriptor(SkillProgramEffectOp.DistributeShuliangEndingGrain);
internal sealed class LiFengHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<ILiFengHost>().Resolve(f, op);
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
internal static class LiFengComposition
{
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.InitializeJiyunCategoryGrain or SkillProgramEffectOp.ResolveJiyunPreparationDrawLeases or SkillProgramEffectOp.CollectJiyunCompletedUseEntities or SkillProgramEffectOp.DistributeShuliangEndingGrain;
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Any(e => Handles(e.Op))) return;
        if (p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardIdentities.Count != 0 || p.DamageModifiers.Count != 0 || p.HpSkillGrants.Count != 0 || p.Contributions.Count != 0 || p.BooleanStates.Count != 0 || p.CardPolicies.Count != 0 || p.Triggers.Any(t => t.Effects.Count != 1 || !Handles(t.Effects[0].Op))) throw new InvalidOperationException(path + ": Li Feng requires his exact native grain and original-card authority.");
        if (p.Id == "ol:jiyun")
        {
            if (p.Triggers.Count != 3 || p.Triggers.SingleOrDefault(t => t.Id == "initial-categories") is not { Window: SkillProgramTriggerWindow.GameStarting, Optional: false, Subject: SkillProgramTriggerSubject.Owner } || p.Triggers.SingleOrDefault(t => t.Id == "preparation-recipients") is not { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, Optional: true, Subject: SkillProgramTriggerSubject.Owner } || p.Triggers.SingleOrDefault(t => t.Id == "completed-drawn-entity") is not { } complete) throw new InvalidOperationException(path + ": Jiyun requires all three phases of its original-entity lifecycle.");
            new JiyunCompletedUseContract().ValidateTrigger(complete);
        }
        else if (p.Id != "ol:shuliang" || p.Triggers is not [{ Id: "ending-distribution", Window: SkillProgramTriggerWindow.TurnEnding, Subject: SkillProgramTriggerSubject.Owner, Optional: true, Effects: [{ Op: SkillProgramEffectOp.DistributeShuliangEndingGrain }] }]) throw new InvalidOperationException(path + ": Shuliang requires original-grain Use/Played authority and arbitrary ending distribution.");
    }
}
