namespace CardGame.Core;

public sealed partial class GameEngine
{
    private MuShunDamageReceipt? MuShunDamageState(long id) => _resolutionStack.SingleOrDefault(f => f.Id == id) switch
    { CardUseFrame f => f.MuShunDamage, ProgramSkillFrame f => f.MuShunDamage, JudgmentFrame f => f.MuShunDamage, _ => null };
    private void SetMuShunDamageState(long id, MuShunDamageReceipt? r)
    {
        var f = _resolutionStack.Single(x => x.Id == id);
        ReplaceRuntimeFrame(id, f switch { CardUseFrame c => c with { MuShunDamage = r }, ProgramSkillFrame p => p with { MuShunDamage = r }, JudgmentFrame j => j with { MuShunDamage = r }, _ => throw new InvalidOperationException("Shizhao damage needs its original native attack frame.") });
    }
    private int MuShunChainCursor(IDamageAttempt a) => !a.IsChainPropagation ? -1 : a is ProgramAttackHandle ? GetProgramAttackState(a.ResolutionId).ChainedTargetIndex : GetCardAttackState(a.ResolutionId).ChainedTargetIndex;
    private MuShunShizhaoIssuedEvent? MuShunDamageArm(IDamageAttempt attack)
    {
        if (!HasMuShunShizhaoCapability) return null;
        return ProgramEventHistory<MuShunShizhaoIssuedEvent>().FirstOrDefault(e => !e.Draw && e.Source.OwnerSeat == attack.TargetSeat && e.ActualTurn == _turnNumber &&
            e.ActualTurnOwner == _turnProgression.OwnerSeat && e.MarkerBefore == 0 && e.MarkerAfter == 0 &&
            _contentRegistry.Skills.TryGetValue(e.Source.SkillId, out var skill) && e.Source.SkillId == MuShunComposition.Shizhao && e.Source.BindingId == MuShunComposition.Loss && skill.Program?.GameplayHash == e.Hash &&
            !ProgramEventHistory<MuShunShizhaoDamageSpentEvent>().Any(spent => spent.ArmId == e.FrameId) &&
            !ProgramEventHistory<TurnEndedEvent>().Any(end => end.TurnNumber == e.ActualTurn && end.ActorSeat == e.ActualTurnOwner));
    }
    [ProgramDamageHook(ProgramDamagePhase.FactModifiers, 45300)] private IEnumerable<(CardUseEffectSource Source, int Amount)> MuShunNextReceivedDamage(IDamageAttempt attack)
    {
        var arm = MuShunDamageArm(attack); if (arm is null || attack.DamageAmount <= 0) return [];
        var cursor = MuShunChainCursor(attack);
        if (MuShunDamageState(attack.ResolutionId) is not { } r || r.TargetSeat != attack.TargetSeat || r.ChainCursor != cursor || r.ArmId != arm.FrameId)
            SetMuShunDamageState(attack.ResolutionId, new(attack.ResolutionId, attack.TargetSeat, cursor, arm.FrameId, attack.DamageAmount));
        return [(new CardUseEffectSource(arm.Source.SkillId, arm.Source.BindingId, arm.Source.OwnerSeat, arm.Source.SkillInstanceId), 1)];
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 45300)] private void ConsumeMuShunDamage(IGameEvent payload)
    {
        if (!HasMuShunShizhaoCapability || payload is not DamageAppliedEvent { Amount: > 0 } applied || CurrentDamageAttempt is not { } attack ||
            _resolutionStack.LastOrDefault() is not DamageFrame damage || damage.ParentFrameId != attack.ResolutionId || damage.TargetSeat != applied.TargetSeat || damage.SourceSeat != applied.SourceSeat || damage.Amount != applied.Amount ||
            MuShunDamageState(attack.ResolutionId) is not { } r || r.TargetSeat != attack.TargetSeat || r.TargetSeat != applied.TargetSeat || r.ChainCursor != MuShunChainCursor(attack) ||
            MuShunDamageArm(attack) is not { } arm || arm.FrameId != r.ArmId) return;
        AdvanceEventRulesAndQueueFact(new MuShunShizhaoDamageSpentEvent(arm.FrameId, attack.ResolutionId, applied.TargetSeat, _turnNumber, damage.Id));
    }
    [ProgramDamageHook(ProgramDamagePhase.BeforeChainAdvance, 45300)] private void BeforeMuShunChain(IDamageAttempt attack)
    {
        if (!HasMuShunShizhaoCapability || MuShunDamageState(attack.ResolutionId) is not { } r) return;
        if (attack.IsChainPropagation && r.TargetSeat == attack.TargetSeat && r.ChainCursor == MuShunChainCursor(attack))
        {
            if (attack is ProgramAttackHandle) UpdateProgramAttackState(attack.ResolutionId, s => s with { DamageAmount = r.InheritedAmount });
            else UpdateCardAttackState(attack.ResolutionId, s => s! with { DamageAmount = r.InheritedAmount });
        }
        SetMuShunDamageState(attack.ResolutionId, null);
    }
    [ProgramDamageHook(ProgramDamagePhase.AfterChainAdvance, 45300)] private void AfterMuShunChain(IDamageAttempt attack)
    {
        if (!HasMuShunShizhaoCapability || !attack.IsChainPropagation || !attack.DamageAmountFinalized) return;
        var modifiers = MuShunNextReceivedDamage(attack).ToArray(); if (modifiers.Length == 0) return;
        if (attack is ProgramAttackHandle) UpdateProgramAttackState(attack.ResolutionId, s => s with { DamageAmount = checked(s.DamageAmount + 1) });
        else UpdateCardAttackState(attack.ResolutionId, s => s! with { DamageAmount = checked(s.DamageAmount + 1) });
    }
}
