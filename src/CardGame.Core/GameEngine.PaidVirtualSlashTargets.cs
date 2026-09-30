namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TryContinuePaidVirtualSlashTargets(AttackResolution attack)
    {
        if (attack.Card is not null || !IsForeignPublicPileSlashUse(attack.ResolutionId)) return false;
        var use = _resolutionStack.OfType<CardUseFrame>().Single(frame => frame.Id == attack.ResolutionId);
        var next = use.TargetIndex + 1;
        while (next < use.TargetSeats.Count && !_players[use.TargetSeats[next]].IsAlive) next++;
        if (_winner != Winner.None || next >= use.TargetSeats.Count)
        {
            _foreignPublicPileSlashBaseDamage.Remove(attack.ResolutionId);
            return false;
        }
        if (!ReferenceEquals(use, _resolutionStack.LastOrDefault()))
            throw new InvalidOperationException("A paid virtual Slash must finish its child before the next target.");
        var targetSeat = use.TargetSeats[next];
        SetCardUseTargetIndex(use.Id, next);
        SetCardUseStep(use.Id, ResolutionFrameStep.ResolvingEffect);
        var continued = new AttackResolution(use.Id, use.SourceSeat, targetSeat, card: null,
            damageAmount: GetForeignPublicPileSlashBaseDamage(use.Id), playedCardKind: CardKind.Slash,
            ignoresArmor: HasCardArmorBypass(_players[use.SourceSeat], _players[targetSeat], CardKind.Slash));
        continued.SetCardUseCausedDamage(attack.CardUseCausedDamage);
        _pendingAttack = continued;
        _pendingDuel = null;
        ClearPendingDecision();
        BeginSlashTargetResolution(continued);
        return true;
    }
}
