namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsShanheS5FuqiResponseProhibited(int sourceSeat, int responderSeat) =>
        sourceSeat != responderSeat && IsValidPlayerSeat(sourceSeat) && IsValidPlayerSeat(responderSeat) &&
        _players[sourceSeat].IsAlive && _players[responderSeat].IsAlive &&
        HasCardPolicy(_players[sourceSeat], SkillProgramCardPolicyKind.ProhibitOthersResponse);

    private bool ShanheS5FuqiNullificationBanned(int responderSeat)
    {
        if (ActiveNullificationWindow is not { } window) return false;
        // A counterspell responds to the last actual counterspell, rather than
        // retaining the initial trick user's privilege throughout the chain.
        var sourceSeat = window.ChainDepth == 0 ? window.SourceSeat : ProgramEventHistory<NullificationRespondedEvent>()
            .LastOrDefault(e => e.WindowFrameId == window.Id && e.ResolutionId == window.ParentFrameId && e.ChainDepth == window.ChainDepth)?.ResponderSeat ?? -1;
        return IsShanheS5FuqiResponseProhibited(sourceSeat, responderSeat);
    }

    [ProgramCardLegality(ProgramCardLegalityStage.NativeResponseBan, 5724)]
    private bool ShanheS5FuqiNativeResponseForbidden(ProgramNativeResponseQuery query) =>
        !query.DyingUse && query.Kind == CardKind.Nullification && ShanheS5FuqiNullificationBanned(query.Provider.Seat);

    private bool IsShanheS5JiaoziQualified(int ownerSeat) => _roundNumber >= 1 &&
        IsValidPlayerSeat(ownerSeat) && _players[ownerSeat].IsAlive &&
        _players.Where(p => p.IsAlive && p.Seat != ownerSeat).All(p => GetHand(p).Count <= GetHand(_players[ownerSeat]).Count);

    private int ShanheS5JiaoziModifierAmount(SkillProgramDamageModifier modifier) =>
        modifier.Condition == SkillProgramDamageModifierCondition.OwnerMaximumHandCurrentRound ? _roundNumber : modifier.Amount;

    private int ShanheS5JiaoziPublicDamageBonus(GameSnapshot view, int sourceSeat, int targetSeat, bool sourceLess, int paidHandCards = 0)
    {
        if (!_contentRegistry.ProgramDependencies.HasMaximumHandRoundDamageModifiers) return 0;
        // All comparisons are public hand counts; only visible skill instances
        // contribute to the viewer's prediction. Actual payment uses the same
        // qualification and amount helpers in the passive damage pipeline.
        int HandCount(PlayerSnapshot player) => Math.Max(0, player.HandCount - (player.Seat == sourceSeat ? paidHandCards : 0));
        var participants = sourceLess ? new[] { targetSeat } : new[] { sourceSeat, targetSeat }.Distinct();
        return participants.Where(seat => _roundNumber >= 1 && view.Players.Single(p => p.Seat == seat) is { IsAlive: true } owner &&
            view.Players.Where(p => p.IsAlive && p.Seat != seat).All(p => HandCount(p) <= HandCount(owner))).Sum(seat =>
        {
            var visible = VisiblePredictionSkillIds(view.Players.Single(p => p.Seat == seat));
            return GetSkillBindingShard(_players[seat]).ProgramInstances.Where(instance => visible.Contains(instance.SkillId))
                .SelectMany(instance => instance.Program.DamageModifiers)
                .Where(modifier => modifier.Condition == SkillProgramDamageModifierCondition.OwnerMaximumHandCurrentRound)
                .Sum(ShanheS5JiaoziModifierAmount);
        });
    }
}
