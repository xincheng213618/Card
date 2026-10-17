namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Guandu expressly rechecks the original targets after the first resolution.
    // It repeats the accepted effect, retaining the original payment and quota.
    private bool GuanduRepeatTargetsRemainLegal(CardUseFrame use)
    {
        if (use.Action is not { } action || !_players[use.SourceSeat].IsAlive || use.TargetSeats.Count == 0) return false;
        bool IsTrickTargetAllowed(int seat) => IsValidPlayerSeat(seat) && _players[seat].IsAlive &&
            !IsDirectedCardTargetProhibited(use.SourceSeat, seat, use.CardKind) &&
            !IsCardTargetProhibited(_players[seat], use.CardKind, action.EffectiveSuit ?? Suit.None, ActualTargetPolicyColor(action)) &&
            !HasBeneficiarySuitShield(use.SourceSeat, seat, action.EffectiveSuit) &&
            !IsShenwuRobeTargetProhibited(use.SourceSeat, seat, use.CardKind);
        // Borrowed Sword designates weapon owners. Each following seat is that
        // owner's Slash victim, not another target of the original trick.
        if (use.CardKind == CardKind.BorrowedSword)
        {
            if (use.TargetSeats.Count % 2 != 0) return false;
            for (var i = 0; i < use.TargetSeats.Count; i += 2)
            {
                var owner = use.TargetSeats[i]; var victim = use.TargetSeats[i + 1];
                if (!IsTrickTargetAllowed(owner) || owner == use.SourceSeat || GetWeapon(_players[owner]) is null ||
                    !IsValidPlayerSeat(victim) || !IsLegalBorrowedSwordSlashTarget(_players[owner], _players[victim])) return false;
            }
            return true;
        }
        foreach (var seat in use.TargetSeats)
        {
            if (!IsTrickTargetAllowed(seat)) return false;
            var target = _players[seat];
            var legal = use.CardKind switch
            {
                CardKind.Peach or CardKind.Zong => IsRuleWounded(target),
                CardKind.Reinforcements => seat != use.SourceSeat && IsRuleWounded(target),
                CardKind.Alcohol or CardKind.RealgarWine or CardKind.DrawTwo or CardKind.IronChain => true,
                CardKind.Intoxication => !GuanduAllies(use.SourceSeat, seat),
                CardKind.FireAttack => GetHand(target).Count > 0,
                CardKind.StoreGrain or CardKind.UnitedEffort or CardKind.PeachGarden or CardKind.FiveGrains or
                    CardKind.BarbarianAssault or CardKind.ArrowBarrage => true,
                _ => CanBeProgramCardUseRoleTarget(use, target)
            };
            if (!legal) return false;
        }
        return true;
    }
}
