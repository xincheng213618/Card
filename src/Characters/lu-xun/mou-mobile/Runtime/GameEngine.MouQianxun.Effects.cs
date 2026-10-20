using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record NativeTrickTargetEffectReturn(long UseId, int TargetSeat, int TargetIndex,
    GroupCardEffect? GroupEffect = null, NullificationWindowFrame? IronChainReturn = null,
    long? WenheProgramFrameId = null, int HpAmount = 0);
public sealed partial record CardEffectBeforeApplyFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NativeTrickTargetEffectReturn? NativeTargetReturn { get; init; }
}
public sealed record NativeIronChainTargetProgress
{
    private readonly IReadOnlyList<string> _changes = Array.AsReadOnly(Array.Empty<string>());
    public IReadOnlyList<string> Changes { get => _changes; init => _changes = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public NativeIronChainTargetProgress? NativeIronChainTargets { get; init; }
}

public sealed partial class GameEngine
{
    private bool HasPerTargetTrickEffectBinding(ProgramCardTriggerCandidate c) =>
        _contentRegistry.GetSkill(c.SkillId).Program!.Triggers.Single(t => t.Id == c.TriggerId).Effects.Any(e =>
            ProgramOperationCatalog.Default.Resolve(e.Op) is IProgramPerTargetTrickEffectOperation);
    private bool CanMouLuXunRecordNativeTarget(int target, CardActionContext action) =>
        action.ActorSeat != target && !MouLuXunRecordedNames(target).Contains(action.EffectiveKind) &&
        _players[target].SkillGrants.Grants.Any(g => g.SkillId == MouLuXunComposition.Qianxun &&
            HasRuntimeSkillInstance(_players[target], g.SkillId, g.SkillInstanceId));
    private bool TryBeginMouLuXunGroupEffect(GroupCardHandle group)
    {
        if (!HasMouLuXun || group.TargetIndex >= group.TargetSeats.Count || LifecycleCardUse(group.ResolutionId)?.Action is not { } action) return false;
        var target = group.TargetSeats[group.TargetIndex];
        if (!_players[target].IsAlive || IsCardEffectIneffective(group.ResolutionId, target) || !CanMouLuXunRecordNativeTarget(target, action)) return false;
        return BeginCardEffectPrograms(action, [target], null, null,
            new(group.ResolutionId, target, group.TargetIndex, group.Effect), perTargetOnly: true);
    }
    private bool ValidNativeTrickTargetEffect(CardEffectBeforeApplyFrame window)
    {
        if (window.NativeTargetReturn is not { } r || window.ParentFrameId != (r.WenheProgramFrameId ?? r.UseId) ||
            LifecycleCardUse(r.UseId) is not { Action: { } action } use || action.ActionId != window.Action.ActionId ||
            !window.FinalDesignatedTargetSeats.SequenceEqual([r.TargetSeat]) ||
            use.PhysicalCardIds is not { } physical || physical.Any(id => _cardZones.GetLocation(id) != CardLocation.Processing)) return false;
        if (r.WenheProgramFrameId is not null) return ValidMouLuXunWenheTarget(r, action);
        if (r.TargetIndex != use.TargetIndex || r.HpAmount != 0) return false;
        if (r.GroupEffect is { } effect)
            return r.IronChainReturn is null && ActiveGroupCard is { } group && group.ResolutionId == r.UseId &&
                group.Effect == effect && group.TargetIndex == r.TargetIndex && group.TargetSeats.ElementAtOrDefault(r.TargetIndex) == r.TargetSeat;
        return r.IronChainReturn is { ActionKind: LegalActionKind.IronChain } pending && pending.ParentFrameId == r.UseId &&
            use.NativeIronChainTargets is not null && pending.TargetSeats.ElementAtOrDefault(r.TargetIndex) == r.TargetSeat;
    }
    private bool HasNativeGroupEffectAttackObserver(CardAttackHandle attack) =>
        _resolutionStack.OfType<CardEffectBeforeApplyFrame>().Any(window =>
            window.NativeTargetReturn is { GroupEffect: GroupCardEffect.ResponseAttack } r &&
            r.UseId == attack.ResolutionId && r.TargetSeat == attack.TargetSeat &&
            ActiveGroupCard?.CurrentAttack is { } original && SameAttackOwner(original, attack) &&
            ValidNativeTrickTargetEffect(window));
    private void ReturnNativeTrickTargetEffect(CardEffectBeforeApplyFrame window)
    {
        if (!ValidNativeTrickTargetEffect(window)) throw new InvalidOperationException("The real trick target effect lost its exact native target cursor.");
        var r = window.NativeTargetReturn!;
        if (r.WenheProgramFrameId is not null) { ReturnMouLuXunWenheTarget(r); return; }
        if (r.IronChainReturn is { } chain) { ResolveIronChainEffect(chain); return; }
        var group = ActiveGroupCard!;
        if (r.GroupEffect == GroupCardEffect.ResponseAttack) ContinueGroupAttackResponseAfterEffect(group);
        else if (r.GroupEffect == GroupCardEffect.PublicDraft) BeginHarvestSelection(group);
        else RunOneGroupRecoveryStep();
    }
    private bool ResolveMouLuXunMultiIronChain(NullificationWindowFrame pending)
    {
        if (!HasMouLuXun || pending.TargetSeats.Count < 2) return false;
        var use = LifecycleCardUse(pending.ParentFrameId)!;
        if (use.NativeIronChainTargets is null)
        { UpdateLifecycleCardUse(use.Id, u => u with { NativeIronChainTargets = new(), TargetIndex = 0 }); use = LifecycleCardUse(use.Id)!; }
        while (use.TargetIndex < pending.TargetSeats.Count)
        {
            var target = _players[pending.TargetSeats[use.TargetIndex]];
            if (target.IsAlive && !IsCardEffectIneffective(use.Id, target.Seat))
            {
                if (CanMouLuXunRecordNativeTarget(target.Seat, use.Action!) &&
                    BeginCardEffectPrograms(use.Action!, [target.Seat], null, null,
                        new(use.Id, target.Seat, use.TargetIndex, IronChainReturn: pending), perTargetOnly: true)) return true;
                var wasChained = target.IsChained;
                target.IsChained = ResolveEnteringChain(target, HasCardPolicy(target, SkillProgramCardPolicyKind.ForceChained) || !target.IsChained);
                if (!wasChained && target.IsChained) RecordCharacterStateChange(target.Seat, SkillProgramTriggerWindow.CharacterEnteredChain, use.Id);
                AdvanceEventRulesAndQueueFact(new IronChainStateChangedEvent(use.Id, pending.SourceSeat, target.Seat, target.IsChained));
                UpdateLifecycleCardUse(use.Id, u => u with { NativeIronChainTargets = u.NativeIronChainTargets! with
                    { Changes = u.NativeIronChainTargets.Changes.Append($"{target.Name}{(target.IsChained ? "横置" : "重置")}").ToArray() } });
            }
            UpdateLifecycleCardUse(use.Id, u => u with { TargetIndex = u.TargetIndex + 1 }); use = LifecycleCardUse(use.Id)!;
        }
        CompleteIronChainEffect(pending, use.NativeIronChainTargets!.Changes.ToList()); return true;
    }
}
