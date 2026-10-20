using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum ZhaohuoStage { Choosing, GiftChildren }

public sealed record ZhaohuoReceipt(CardConversionSource Source, string GameplayHash,
    long DamageFrameId, long DamageWindowId, long AttackId, int VictimSeat,
    int ActualTurn, int TurnOwner, ZhaohuoStage Stage)
{
    public bool Paid { get; init; }
    public int? CardId { get; init; }
    public CardKind? PrintedKind { get; init; }
    public long SequenceBefore { get; init; }
    public long SequenceAfter { get; init; }
    public long? PaymentSequence { get; init; }
    public bool SelfGift { get; init; }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ZhaohuoReceipt? Zhaohuo { get; init; }
}

public sealed record ZhaohuoFirstVictimDamageEvent(long DamageFrameId, long AttackId,
    int ActualTurn, int TurnOwner, int? DamageSourceSeat, int VictimSeat,
    int Amount, int HandCount) : IGameEvent;
public sealed record ZhaohuoQualifiedEvent(long DamageFrameId, CardConversionSource Source,
    string GameplayHash, int ActualTurn, int TurnOwner) : IGameEvent;
public sealed record ZhaohuoStartedEvent(long FrameId, long DamageFrameId, long DamageWindowId,
    CardConversionSource Source, string GameplayHash, int VictimSeat,
    int ActualTurn, int TurnOwner) : IGameEvent;
public sealed record ZhaohuoGiftCommittedEvent(long FrameId, long DamageFrameId,
    CardConversionSource Source, string GameplayHash, int DonorSeat, int CardId,
    CardKind PrintedKind, int ActualTurn, int TurnOwner) : IGameEvent;
public sealed record ZhaohuoGiftPaidEvent(long FrameId, long DamageFrameId,
    CardConversionSource Source, string GameplayHash, int DonorSeat, int CardId,
    CardKind PrintedKind, int ActualTurn, int TurnOwner, CardLocation From,
    CardLocation To, long MovementSequence) : IGameEvent;
public sealed record ZhaohuoSelfGiftCommittedEvent(long FrameId, long DamageFrameId,
    CardConversionSource Source, string GameplayHash, int CardId, CardKind PrintedKind,
    int ActualTurn, int TurnOwner, long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record ZhaohuoCompletedEvent(long FrameId, long DamageFrameId, bool Paid) : IGameEvent;

internal interface IZhaohuoHost
{
    SkillProgramStepOutcome Resolve(ProgramSkillFrame frame);
}

internal sealed class ResolveZhaohuoFirstVictimGiftDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveZhaohuoFirstVictimGift;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } =
        new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(1));
    public override ISkillProgramEffectHandler Handler { get; } = new ZhaohuoHandler();
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}

internal sealed class ZhaohuoHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveZhaohuoFirstVictimGift;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int target, ISkillProgramEffectHost host) => host.GetCapability<IZhaohuoHost>().Resolve(frame);
}

internal static class ZhaohuoComposition
{
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var hasOp = p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects))
            .Any(e => e.Op == SkillProgramEffectOp.ResolveZhaohuoFirstVictimGift);
        if (!hasOp) return;
        if (p.Id != "ol:zhaohuo" || p.Activations.Count != 0 || p.ViewAs.Count != 0 ||
            p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 ||
            p.HpSkillGrants.Count != 0 || p.DamageModifiers.Count != 0 || p.BooleanStates.Count != 0 ||
            p.Contributions.Count != 0 || p.Triggers is not [var trigger] ||
            trigger.Id != "first-victim-damage" || trigger.Window != SkillProgramTriggerWindow.AfterDamageApplied ||
            trigger.Subject != SkillProgramTriggerSubject.Any || trigger.Optional ||
            trigger.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage ||
            trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always || trigger.UsageScope is not null ||
            trigger.UsageLimit is not null || trigger.MarkerCost is not null || trigger.OwnerRelation is not null ||
            trigger.SourceSkillId is not null || trigger.SourceViewAsId is not null ||
            trigger.IncludeResponseUses || trigger.SingleActionInstance || trigger.SourceZones.Count != 0 ||
            trigger.DestinationZones.Count != 0 || trigger.CardKinds.Count != 0 || trigger.DamageCardKinds.Count != 0 ||
            trigger.Effects is not [{ Op: SkillProgramEffectOp.ResolveZhaohuoFirstVictimGift,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException(path + ": Zhaohuo requires its exact producer-frozen first-victim damage and original Hand gift.");
    }
}
