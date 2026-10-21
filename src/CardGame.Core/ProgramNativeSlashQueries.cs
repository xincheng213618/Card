namespace CardGame.Core;

internal sealed record SlashUseVariant(
    CardKind EffectiveKind,
    CardConversionSource? AdditionalConversionSource,
    bool UsesZhuqueFan);

public sealed partial class GameEngine
{
    // A provider merely playing a card is a different native request from the
    // actor using a Slash. Keep that distinction at the shared native frontier.
    private bool IsNativePureProviderFrontier(int actor)
    {
        var top = _resolutionStack.LastOrDefault();
        bool At(long id) => top?.Id == id || top is ResponseWindowFrame response && response.ParentFrameId == id;
        return ActiveFactionCardRequest is { AwaitingProviders: true } f && f.CurrentCandidateSeat == actor && At(f.OwnerFrameId) ||
            ActiveFactionDefense is { } d && d.CurrentCandidateSeat == actor && At(d.Attack.ResolutionId);
    }

    private bool IsNativeForcedSlashUseFrontier(CharacterState owner)
    {
        if (IsNativePureProviderFrontier(owner.Seat)) return false;
        var top = _resolutionStack.LastOrDefault();
        bool At(long id) => top?.Id == id || top is ResponseWindowFrame response && response.ParentFrameId == id;
        if (ActiveBorrowedSword is { ActiveAttack: null } borrowed && borrowed.WeaponOwnerSeat == owner.Seat && At(borrowed.ResolutionId)) return true;
        if (ActiveQinglongCrescentBlade is { } qinglong && qinglong.Attack.SourceSeat == owner.Seat &&
            SameAttackOwner(qinglong.Attack, ActiveCardAttack) && At(qinglong.Attack.ResolutionId)) return true;
        if (top is not ProgramSkillFrame f || GetPausedPrivateOfferEffect(f) is not { } effect) return false;
        if (IsQigongRequest(f)) return f.LvKuangLvXiang!.Actor == owner.Seat;
        if (IsCaoChunInitialRequest(f) || IsWuJingHejiRequest(f)) return f.OwnerSeat == owner.Seat;
        return RoundOnceCardlessProgramActor(f, effect.Op, owner.Seat);
    }
}
