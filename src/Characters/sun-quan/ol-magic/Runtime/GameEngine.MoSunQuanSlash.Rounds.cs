namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasUnstartedMoQuanyuExtraRound(long id) => LifecycleCardUse(id) is
        { MoQuanyuExtraRound: { Started: false }, Action: { Type: CardActionType.Use } } use &&
        _winner == Winner.None && !IsNamedAmbushUseIneffective(use);
    private bool IsMoQuanyuExtraRound(long id) => LifecycleCardUse(id)?.MoQuanyuExtraRound is { Started: true };
    private void CaptureMoQuanyuExtraAttack(CardAttackHandle attack)
    {
        if (LifecycleCardUse(attack.ResolutionId) is not { MoQuanyuExtraRound: { Started: false } r, CardAttack: { } original } use ||
            IsZixiExtraEffectRound(use.Id) || IsFeiYiExtraEffectRound(use.Id) || use.ExtraCardEffect is { WholeGroupRepeated: true } ||
            r.Native.OriginalAttacks.Any(s => s.TargetIndex == use.TargetIndex)) return;
        UpdateLifecycleCardUse(use.Id, current => current with { MoQuanyuExtraRound = r with { Native = r.Native with
        { OriginalAttacks = r.Native.OriginalAttacks.Append(new(use.TargetIndex, original)).ToArray(),
            Fangtian = current.Continuations.FangtianHalberd ?? r.Native.Fangtian } } });
    }
    private void CompleteMoQuanyuExtraRound(long id)
    {
        if (LifecycleCardUse(id) is not { MoQuanyuExtraRound: { Active: true } r } use) return;
        UpdateLifecycleCardUse(id, current => current with { MoQuanyuExtraRound = r with { Active = false } });
        AdvanceEventRulesAndQueueFact(new MoQuanyuExtraRoundCompletedEvent(r.ProgramFrameId, use.Id, r.ActionId));
    }
    private bool TryBeginMoQuanyuExtraRound(long id, bool afterAttack)
    {
        if (!HasUnstartedMoQuanyuExtraRound(id) || LifecycleCardUse(id) is not { MoQuanyuExtraRound: { } r } use ||
            use.ExtraCardEffect is { WholeGroupRepeated: false } || HasUnstartedFeiYiExtraEffect(id) || HasUnstartedZixiExtraEffect(id)) return false;
        if (_resolutionStack.LastOrDefault()?.Id != id) throw new InvalidOperationException("Quanyu must await the complete original native effect and its children.");
        var previous = NativeExtraEffectState(use);
        var native = r.Native with { WholeGroupRepeated = true, MaterialClaims = previous?.MaterialClaims ?? r.Native.MaterialClaims };
        ReplaceRuntimeFrame(id, use with { MoQuanyuExtraRound = r with { Started = true, Active = true, Native = native },
            TargetIndex = 0, Step = ResolutionFrameStep.ResolvingEffect });
        AdvanceEventRulesAndQueueFact(new MoQuanyuExtraRoundStartedEvent(r.ProgramFrameId, id, r.ActionId));
        var first = native.OriginalAttacks.OrderBy(s => s.TargetIndex).FirstOrDefault(s =>
            _players[s.Attack.TargetSeat].IsAlive && !IsCardEffectIneffective(id, s.Attack.TargetSeat));
        if (first is null) { CompleteMoQuanyuExtraRound(id); return false; }
        AdvanceEventRulesAndQueueFact(new ExtraCardEffectStartedEvent(id, r.ActionId, first.TargetIndex, first.Attack.TargetSeat, use.CardKind));
        if (afterAttack) { ActiveCardAttack = null; ActiveDuel = null; ClearPendingDecision(); }
        var prepared = use.PreparedTargetAttacks?.Select((state, index) =>
            native.OriginalAttacks.SingleOrDefault(s => s.TargetIndex == index)?.Attack ?? state).ToArray();
        UpdateLifecycleCardUse(id, current => current with { TargetIndex = first.TargetIndex,
            PreparedTargetAttacks = prepared is null ? null : Array.AsReadOnly(prepared) });
        if (native.Fangtian is { } fang)
        {
            UpdateCardContinuations(id, state => state with { FangtianHalberd = fang with { Active = true, CurrentAttackOwnerId = id } });
            ActiveFangtianHalberd = new(this, id);
        }
        UpdateCardAttackState(id, _ => ResetExtraCardEffectAttack(first.Attack));
        var attack = new CardAttackHandle(this, id); ActiveCardAttack = attack; ContinueSlashDefense(attack); return true;
    }
    private bool ContinueMoQuanyuFangtian(CardAttackHandle attack)
    {
        if (LifecycleCardUse(attack.ResolutionId) is not { MoQuanyuExtraRound: { Active: true } r } use) return false;
        var seed = r.Native.OriginalAttacks.SingleOrDefault(s => s.TargetIndex == use.TargetIndex);
        if (seed is null) { CompleteAttack(attack); return true; }
        UpdateCardAttackState(use.Id, _ => ResetExtraCardEffectAttack(seed.Attack)); ContinueSlashDefense(attack); return true;
    }
}
