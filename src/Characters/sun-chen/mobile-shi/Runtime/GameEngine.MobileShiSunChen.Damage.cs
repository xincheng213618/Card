namespace CardGame.Core;
public sealed partial class GameEngine
{
    private ShiScDamageReceipt? ShiScDamageState(long id) => _resolutionStack.SingleOrDefault(f => f.Id == id) switch
    { CardUseFrame c => c.ShiSunChenDamage, ProgramSkillFrame p => p.ShiSunChenDamage, JudgmentFrame j => j.ShiSunChenDamage, _ => null };
    private void SetShiScDamageState(long id, ShiScDamageReceipt? r)
    {
        var f = _resolutionStack.Single(x => x.Id == id); ReplaceRuntimeFrame(id, f switch {
            CardUseFrame c => c with { ShiSunChenDamage = r }, ProgramSkillFrame p => p with { ShiSunChenDamage = r }, JudgmentFrame j => j with { ShiSunChenDamage = r },
            _ => throw new InvalidOperationException("Nigu damage requires its original native attack frame.") });
    }
    private int ShiScDamageCursor(IDamageAttempt a) => !a.IsChainPropagation ? -1 : a is ProgramAttackHandle ? GetProgramAttackState(a.ResolutionId).ChainedTargetIndex : GetCardAttackState(a.ResolutionId).ChainedTargetIndex;
    private IEnumerable<ShiScArmedEvent> LiveShiScArms(int owner) => ProgramEventHistory<ShiScArmedEvent>().Where(e => e.Source.OwnerSeat == owner && e.Turn == _turnNumber && e.TurnOwner == _turnProgression.OwnerSeat && e.Count > 0 &&
        _contentRegistry.Skills.TryGetValue(ShiScNigu, out var skill) && skill.Program?.GameplayHash == e.Hash &&
        ProgramEventHistory<ShiScStartedEvent>().Count(s => s.FrameId == e.FrameId && s.Source == e.Source && s.Hash == e.Hash && s.Op == SkillProgramEffectOp.ResolveMobileShiSunChenNigu && s.Turn == e.Turn && s.TurnOwner == e.TurnOwner) == 1 &&
        ProgramEventHistory<ShiScCompletedEvent>().Count(c => c.FrameId == e.FrameId) == 1 && !ProgramEventHistory<TurnEndedEvent>().Any(t => t.TurnNumber == e.Turn && t.ActorSeat == e.TurnOwner));
    private ShiScArmedEvent? AvailableShiScArm(IDamageAttempt a) => a.IsSourceLess ? null : LiveShiScArms(a.SourceSeat).FirstOrDefault(e => ProgramEventHistory<ShiScDamageConsumedEvent>().Count(c => c.ArmId == e.FrameId) < e.Count);
    [ProgramDamageHook(ProgramDamagePhase.FactModifiers, 49100)] private IEnumerable<(CardUseEffectSource Source, int Amount)> ShiScNextDamage(IDamageAttempt a)
    {
        if (a.IsSourceLess || a.DamageAmount <= 0 || AvailableShiScArm(a) is not { } arm) return [];
        var cursor = ShiScDamageCursor(a);
        if (ShiScDamageState(a.ResolutionId) is not { } prior || prior.Source != a.SourceSeat || prior.Target != a.TargetSeat || prior.ChainCursor != cursor || prior.ArmId != arm.FrameId)
        {
            SetShiScDamageState(a.ResolutionId, new(a.ResolutionId, arm.FrameId, a.SourceSeat, a.TargetSeat, cursor, a.DamageAmount));
            AdvanceEventRulesAndQueueFact(new ShiScDamageIssuedEvent(arm.FrameId, a.ResolutionId, a.SourceSeat, a.TargetSeat, cursor, a.DamageAmount));
        }
        return [(new(arm.Source.SkillId, arm.Source.BindingId, arm.Source.OwnerSeat, arm.Source.SkillInstanceId), 1)];
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 49100)] private void ConsumeShiScDamage(IGameEvent payload)
    {
        if (payload is ExtraCardEffectStartedEvent extra && LifecycleCardUse(extra.CardUseFrameId) is { ShiSunChenDamage: not null } repeated && IsExtraCardEffectRound(repeated.Id)) SetShiScDamageState(repeated.Id, null);
        if (payload is ProgramDamageTransferredEvent moved && CurrentDamageAttempt is { } a && ShiScDamageState(a.ResolutionId) is { TransferredTarget: null } original && a.DamageRedirected && moved.ResolutionId == a.ResolutionId && moved.SourceSeat == a.SourceSeat && moved.TargetSeat == a.TargetSeat && original.ChainCursor == ShiScDamageCursor(a))
            SetShiScDamageState(a.ResolutionId, original with { TransferredTarget = moved.TargetSeat });
        if (payload is not DamageAppliedEvent { Amount: > 0, SourceLess: false } applied || CurrentDamageAttempt is not { IsSourceLess: false } attack ||
            _resolutionStack.LastOrDefault() is not DamageFrame damage || damage.ParentFrameId != attack.ResolutionId || damage.TargetSeat != applied.TargetSeat || damage.SourceSeat != applied.SourceSeat || damage.Amount != applied.Amount ||
            ShiScDamageState(attack.ResolutionId) is not { } r || r.Source != applied.SourceSeat || (r.TransferredTarget ?? r.Target) != applied.TargetSeat || r.ChainCursor != ShiScDamageCursor(attack) ||
            ProgramEventHistory<ShiScDamageConsumedEvent>().Any(e => e.DamageId == damage.Id && e.ArmId == r.ArmId)) return;
        AdvanceEventRulesAndQueueFact(new ShiScDamageConsumedEvent(r.ArmId, attack.ResolutionId, damage.Id, applied.TargetSeat, r.ChainCursor));
    }
    private void SetShiScChainAmount(IDamageAttempt a, int amount)
    {
        if (!SameAttackOwner(a, CurrentDamageAttempt) || !a.IsChainPropagation || amount < 0) throw new InvalidOperationException("Nigu may rebase only its exact current native chained recipient.");
        if (a is ProgramAttackHandle) UpdateProgramAttackState(a.ResolutionId, s => s with { DamageAmount = amount }); else UpdateCardAttackState(a.ResolutionId, s => s! with { DamageAmount = amount });
    }
    [ProgramDamageHook(ProgramDamagePhase.BeforeChainAdvance, 49100)] private void BeforeShiScChain(IDamageAttempt a)
    {
        if (ShiScDamageState(a.ResolutionId) is not { } r) return;
        if (a.IsChainPropagation && r.ChainCursor == ShiScDamageCursor(a) && (r.TransferredTarget ?? r.Target) == a.TargetSeat) SetShiScChainAmount(a, r.Inherited);
        SetShiScDamageState(a.ResolutionId, null);
    }
    [ProgramDamageHook(ProgramDamagePhase.AfterChainAdvance, 49100)] private void AfterShiScChain(IDamageAttempt a)
    {
        if (!a.IsChainPropagation || !a.DamageAmountFinalized) return;
        if (ShiScNextDamage(a).Any()) SetShiScChainAmount(a, checked(a.DamageAmount + 1));
    }
    private IReadOnlyList<ProgramPublicRuleStateSnapshot> MobileShiSunChenPublicStates(CharacterState owner, string skillId) => skillId != ShiScNigu ? [] :
        LiveShiScArms(owner.Seat).Select(e => new ProgramPublicRuleStateSnapshot(e.Source.SkillInstanceId, "nigu-damage-remaining", ProgramPublicRuleStateKind.ActivationLimit, e.Count,
            ProgramEventHistory<ShiScDamageConsumedEvent>().Count(c => c.ArmId == e.FrameId), SkillUsageScope.Turn)).ToArray();
}
