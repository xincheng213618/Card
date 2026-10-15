namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class TurnHandLimitKindExemptionProgramCapability(GameEngine engine) : ITurnHandLimitKindExemptionProgramHost
    {
        public void GrantTurnHandLimitCardKindExemption(ProgramSkillFrame frame, IReadOnlyList<CardKind> cardKinds) =>
            engine.GrantProgramTurnHandLimitCardKindExemption(frame, cardKinds);
    }
    private void GrantProgramTurnHandLimitCardKindExemption(ProgramSkillFrame frame, IReadOnlyList<CardKind> cardKinds,
        SkillProgramDamageModifierExpiration expiration = SkillProgramDamageModifierExpiration.CurrentTurnEnd)
    {
        ValidateProgramTurnEffectGrant(frame);
        if (_winner != Winner.None || !_players[frame.OwnerSeat].IsAlive) return;
        var policy = _turnCardUseEffects.GrantHandLimitKindExemption(_turnNumber, _turnProgression.OwnerSeat,
            frame.Id, frame.InstructionIndex - 1, CreateProgramTurnEffectSource(frame), cardKinds, expiration);
        AdvanceEventRulesAndQueueFact(new TurnHandLimitCardKindExemptionGrantedEvent(policy));
    }
    private IReadOnlyList<TurnHandLimitCardKindExemption>? GetTurnHandLimitCardKindExemptionsSnapshot(int ownerSeat)
    {
        var policies = _turnCardUseEffects.HandLimitKindExemptions.Where(policy =>
            (policy.Expiration == SkillProgramDamageModifierExpiration.NextOwnerTurnStart ||
             policy.TurnNumber == _turnNumber && policy.TurnSeat == _turnProgression.OwnerSeat) && policy.Source.OwnerSeat == ownerSeat).ToArray();
        return policies.Length == 0 ? null : Array.AsReadOnly(policies.Select(policy => policy with
            { CardKinds = Array.AsReadOnly(policy.CardKinds.ToArray()) }).ToArray());
    }
}
