using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum SelectedActorDuelParticipantMode { OrderedSelectedPair, OwnerToSelectedTarget }

public sealed partial class SkillProgramEffect
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SelectedActorDuelParticipantMode? ParticipantMode { get; internal init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ProhibitNullification { get; internal init; }
}

/// <summary>The original skill owner's actual discard, not a material of the issued Duel.</summary>
public sealed record OrderedSelectedActorDuelPayment(long ProgramFrameId, int OwnerSeat, int ActorSeat, int TargetSeat,
    string SkillId, string ActivationId, string SkillInstanceId, string GameplayHash, int TurnNumber,
    long MovementBatchId, long MovementSequence, int CardId, CardKind PrintedKind, CardLocation From, CardLocation To, string Reason);
public sealed record OrderedSelectedActorDuelState(OrderedSelectedActorDuelPayment Payment,
    long? IssuedCardUseFrameId = null, bool Completed = false);
public sealed record OrderedSelectedActorDuelPaidEvent(OrderedSelectedActorDuelPayment Payment) : IGameEvent;
public sealed record OrderedSelectedActorDuelIssuedEvent(long CardUseFrameId, OrderedSelectedActorDuelPayment Payment) : IGameEvent;

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OrderedSelectedActorDuelState? OrderedSelectedActorDuel { get; init; }
}

internal static class OrderedSelectedActorDuelContract
{
    internal static bool IsOwnerToSelected(SkillProgramEffect effect) => effect.Op == SkillProgramEffectOp.UseSelectedActorDuel &&
        effect.ParticipantMode == SelectedActorDuelParticipantMode.OwnerToSelectedTarget && effect.ProhibitNullification is null;
    internal static bool IsOptIn(SkillProgramEffect effect) =>
        effect.Op == SkillProgramEffectOp.UseSelectedActorDuel &&
        effect.ParticipantMode == SelectedActorDuelParticipantMode.OrderedSelectedPair && effect.ProhibitNullification == true;

    internal static void ValidateActivation(string path, SkillProgramActivation activation)
    {
        var effects = activation.Effects;
        if (effects.Any(IsOwnerToSelected))
        {
            if (activation.MinCards != 0 || activation.MaxCards != 0 || activation.MinTargets != 1 || activation.MaxTargets != 1 ||
                activation.TargetKind != SkillProgramTargetKind.OtherLiving || effects.Count != 1 ||
                activation.UsesPerPhaseExpression != SkillProgramPhaseUsageExpression.CurrentRound || activation.UsesPerPhase is not null ||
                activation.UsesPerTurn is not null || activation.UsesPerGame is not null || activation.UsageGroup != activation.Id ||
                activation.TargetPhaseLedgerId is not null || activation.CategoryTargetLedgerId is not null || activation.OtherCharactersOnly ||
                activation.MarkerCost is not null || activation.PindianHandReturn is not null || activation.Condition.Kind != SkillProgramConditionKind.Always ||
                effects[0].Condition.Kind != SkillProgramConditionKind.Always)
                throw new InvalidOperationException($"Invalid skill program at {path}: owner-to-selected Duel requires one zero-card, one-target actual Play activation with a current-round phase quota.");
            return;
        }
        if (effects.Any(IsOptIn))
        {
            if (activation.MinCards != 1 || activation.MaxCards != 1 || activation.MinTargets != 2 || activation.MaxTargets != 2 ||
                activation.TargetKind != SkillProgramTargetKind.OtherLivingMale || activation.UsesPerPhase != 1 || activation.UsesPerTurn is not null ||
                activation.SourceZones.Count == 0 || activation.SourceZones.Any(zone => zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)) ||
                effects is not [{ Op: SkillProgramEffectOp.DiscardSelected, Target: SkillProgramEffectTarget.Owner, Amount: 1 } cost, var duel] ||
                cost.Condition.Kind != SkillProgramConditionKind.Always || !IsOptIn(duel) || duel.Condition.Kind != SkillProgramConditionKind.Always)
                throw new InvalidOperationException($"Invalid skill program at {path}: ordered actor Duel requires one HE discard, two ordered other male targets and one use per Play phase.");
            return;
        }
        if (activation.MinCards != 0 || activation.MaxCards != 0 || activation.MinTargets != 1 || activation.MaxTargets != 1 ||
            activation.TargetKind != SkillProgramTargetKind.OtherLiving || effects.Count != 1 ||
            activation.UsesPerPhase != 2 || activation.UsesPerTurn is not null)
            throw new InvalidOperationException($"Invalid skill program at {path}: selected actor Duel requires one other target, zero cards and exactly two uses per actual Play phase.");
    }
}
