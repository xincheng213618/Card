using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record GroupTargetNullificationReturn(int TargetIndex, int TargetSeat);

public sealed partial record NullificationWindowFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GroupTargetNullificationReturn? GroupTargetReturn { get; init; }
}

public sealed partial class GameEngine
{
    private static bool UsesGroupTargetNullification(LegalActionKind kind) => kind is
        LegalActionKind.BarbarianAssault or LegalActionKind.ArrowBarrage or
        LegalActionKind.FiveGrains or LegalActionKind.PeachGarden;

    private static LegalActionKind GroupNullificationAction(CardKind kind) => kind switch
    {
        CardKind.BarbarianAssault => LegalActionKind.BarbarianAssault,
        CardKind.ArrowBarrage => LegalActionKind.ArrowBarrage,
        CardKind.FiveGrains => LegalActionKind.FiveGrains,
        CardKind.PeachGarden => LegalActionKind.PeachGarden,
        _ => throw new InvalidOperationException("Unknown group counterspell effect.")
    };

    private CardKind GroupNullificationCardKind(GroupCardHandle group) => IsLeChengyanEffectUse(group.ResolutionId)
        ? LifecycleCardUse(group.ResolutionId)!.CardKind : group.Card.Kind;

    private bool IsGroupTargetNullification(NullificationWindowFrame window) =>
        window.GroupTargetReturn is { } target && ActiveGroupCard is { } group &&
        group.ResolutionId == window.ParentFrameId && group.Card.Id == window.EffectCardId &&
        GroupNullificationCardKind(group) == window.EffectCardKind && group.SourceSeat == window.SourceSeat &&
        group.TargetIndex == target.TargetIndex && target.TargetIndex >= 0 &&
        target.TargetIndex < group.TargetSeats.Count && group.TargetSeats[target.TargetIndex] == target.TargetSeat &&
        window.TargetSeats.SequenceEqual([target.TargetSeat]) && group.CurrentAttack is null &&
        group.NullificationCompletedTargetIndex != target.TargetIndex &&
        window.ActionKind == GroupNullificationAction(group.Card.Kind) && window.RequiredCardKind == group.RequiredCardKind;

    private bool TryBeginGroupTargetNullification(GroupCardHandle group)
    {
        if (group.NullificationCompletedTargetIndex == group.TargetIndex) return false;
        if (IsSlashCard(GroupNullificationCardKind(group)) ||
            IsOriginalOrdinaryTrickCannotNullify(group.ResolutionId) || IsOrderedSelectedActorDuelCannotNullify(group.ResolutionId) ||
            IsYingboUnrespondable(group.ResolutionId) || IsIssuedCardUnrespondable(group.ResolutionId))
        {
            group.NullificationCompletedTargetIndex = group.TargetIndex;
            return false;
        }
        if (ActiveNullificationWindow is not null || _resolutionStack.LastOrDefault()?.Id != group.ResolutionId)
            throw new InvalidOperationException("A group target counterspell must start on its owning card frame.");
        var kind = GroupNullificationAction(group.Card.Kind);
        var targetSeat = group.TargetSeats[group.TargetIndex];
        SetCardUseStep(group.ResolutionId, ResolutionFrameStep.AwaitingResponse);
        var window = new NullificationWindowFrame(++_resolutionSequence, group.ResolutionId,
            group.SourceSeat, Array.AsReadOnly(new[] { targetSeat }), group.Card.Id, GroupNullificationCardKind(group),
            kind, null, group.RequiredCardKind, BuildNullificationCandidateSeats(group.SourceSeat))
        { GroupTargetReturn = new(group.TargetIndex, targetSeat) };
        var recovery = group.Effect == GroupCardEffect.Recovery;
        var targetIndex = group.TargetIndex;
        PushRuntimeFrame(window);
        ContinueNullificationWindow(window);
        // A recovery step with no responders can apply its current target now.
        // A canceled target or a live response window has already consumed this step.
        return !recovery || ActiveNullificationWindow is not null ||
            !SameContinuationOwner(ActiveGroupCard, group) || group.TargetIndex != targetIndex;
    }

    private void CompleteGroupTargetNullification(NullificationWindowFrame window)
    {
        if (!IsGroupTargetNullification(window) || _resolutionStack.LastOrDefault()?.Id != window.ParentFrameId)
            throw new InvalidOperationException("A group counterspell lost its exact target return.");
        var group = ActiveGroupCard!;
        group.NullificationCompletedTargetIndex = group.TargetIndex;
        if (window.EffectNullified && !HasCurrentCardEnhancement(group.ResolutionId, CurrentCardEnhancement.Uncancelable))
        {
            AddLog("CardEffect", $"【{CardCatalog.Get(window.EffectCardKind).DisplayName}】对{_players[window.TargetSeats[0]].Name}的效果被无懈。",
                group.SourceSeat, window.TargetSeats[0]);
            group.TargetIndex++;
        }
        if (_winner != Winner.None) group.TargetIndex = group.TargetSeats.Count;
        switch (group.Effect)
        {
            case GroupCardEffect.ResponseAttack: BeginGroupAttackResponse(group); break;
            case GroupCardEffect.PublicDraft: BeginHarvestSelection(group); break;
            case GroupCardEffect.Recovery:
                if (group.TargetIndex >= group.TargetSeats.Count) FinishGroupRecovery(group);
                break;
        }
    }

    private string NullificationEffectDescription(NullificationWindowFrame window) =>
        window.GroupTargetReturn is { } target
            ? $"【{CardCatalog.Get(window.EffectCardKind).DisplayName}】对{_players[target.TargetSeat].Name}的效果"
            : $"【{CardCatalog.Get(window.EffectCardKind).DisplayName}】效果";
}
