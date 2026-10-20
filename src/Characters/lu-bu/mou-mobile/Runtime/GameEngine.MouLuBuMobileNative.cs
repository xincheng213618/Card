namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool CanMouLbDuel(ProgramSkillFrame f, int target)
    {
        var r = f.MouLuBuLiyu!;
        return _winner == Winner.None && IsValidPlayerSeat(target) && target != f.OwnerSeat && target != r.VictimSeat &&
            _players[target].IsAlive && _players[f.OwnerSeat].IsAlive && _players[r.VictimSeat].IsAlive &&
            !IsCardUseForbidden(f.OwnerSeat, CardKind.Duel, CardActionType.Use) &&
            !HasTurnCardTargetRestriction(f.OwnerSeat, SkillProgramCardTargetRestriction.SelfOnly) &&
            !IsCardTargetProhibited(_players[target], CardKind.Duel, Suit.None) && !IsDirectedCardTargetProhibited(f.OwnerSeat, target, CardKind.Duel) &&
            !HasBeneficiarySuitShield(f.OwnerSeat, target, Suit.None);
    }
    private void IssueMouLbDuel(ProgramSkillFrame f, int target)
    {
        var r = f.MouLuBuLiyu!;
        if (!ValidMouLb(f) || r.Stage != MouLuBuLiyuStage.VictimChoice || r.CategoryMask != 7 || !CanMouLbDuel(f, target))
            throw new InvalidOperationException("Mobile Liyu Duel lost its actual gain categories and victim's legal choice.");
        ReplaceRuntimeTop(f = f with { MouLuBuLiyu = r = r with { Stage = MouLuBuLiyuStage.DuelIssued, DuelTarget = target } });
        UpdateCardAttackState(r.OuterUseId, s => s! with { Active = false });
        var origin = new ProgramDamageTargetDuelOrigin(f.Id, f.InstructionIndex, MouLbOriginAnchor(f), r.OuterUseId, r.WindowId, r.DamageFrameId, r.OuterActionId,
            f.OwnerSeat, r.VictimSeat, target, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, r.ActualTurn);
        var card = new Card(0, CardKind.Duel, Suit.None, 0);
        var use = BeginCardUse(card, f.OwnerSeat, [target], CardKind.Duel, physicalCardIds: [], damageTargetDuelOrigin: origin);
        AdvanceEventRulesAndQueueFact(new MouLuBuLiyuDuelIssuedEvent(f.Id, use, f.OwnerSeat, target));
        BeginJizhiOrNullificationWindow(use, card, f.OwnerSeat, [target], LegalActionKind.Duel, playedCardKind: CardKind.Duel);
    }
    private long MouLbOriginAnchor(ProgramSkillFrame f) => ProgramEventHistory<MouLuBuLiyuObtainedEvent>().First(e => e.FrameId == f.Id).Sequence;
    private static bool IsMouLbDuelOrigin(ProgramDamageTargetDuelOrigin o) => o.SkillId == MouLuBuMobileComposition.Liyu && o.BindingId == "obtain-draw-and-choose";
    private bool MatchesMouLbDuelParent(ProgramSkillFrame f, ProgramDamageTargetDuelOrigin o) => IsMouLbDuelOrigin(o) && ValidMouLb(f) &&
        f.MouLuBuLiyu is { Stage: MouLuBuLiyuStage.DuelIssued, Paid: true, CategoryMask: 7, DuelTarget: { } target } r &&
        o.ParentProgramFrameId == f.Id && o.InstructionIndex == f.InstructionIndex && o.ReceiptSequence == MouLbOriginAnchor(f) &&
        o.OuterCardUseFrameId == r.OuterUseId && o.DamageWindowId == r.WindowId && o.DamageFrameId == r.DamageFrameId && o.OuterActionId == r.OuterActionId &&
        o.InitialActorSeat == f.OwnerSeat && o.VictimChooserSeat == r.VictimSeat && o.InitialTargetSeat == target &&
        o.SkillId == f.SkillId && o.BindingId == GetProgramBindingId(f) && o.SkillInstanceId == f.SkillInstanceId && o.GameplayHash == f.GameplayHash &&
        o.TurnNumber == r.ActualTurn && o.BaseDamage is null &&
        ProgramEventHistory<MouLuBuLiyuBranchEvent>().Count(e => e.FrameId == f.Id && e.ChooserSeat == r.VictimSeat && e.Branch == "duel" && e.TargetSeat == target && e.CategoryMask == 7) == 1;
    private IReadOnlyList<int> MouLbDuelOuterProcessing(CardUseFrame use)
    {
        if (use.DamageTargetDuelOrigin is not { } o || _resolutionStack.SingleOrDefault(x => x.Id == o.ParentProgramFrameId) is not ProgramSkillFrame f ||
            !MatchesMouLbDuelParent(f, o) || LifecycleCardUse(o.OuterCardUseFrameId) is not { CardAttack.Active: false } outer)
            throw new InvalidOperationException("Mobile Liyu lost the original suspended physical Slash.");
        return (outer.PhysicalCardIds ?? []).Where(id => _cardZones.GetLocation(id) == CardLocation.Processing).ToArray();
    }
    private void AssertMouLbDuelUse(CardUseFrame use)
    {
        var o = use.DamageTargetDuelOrigin!; var i = _resolutionStack.FindIndex(x => x.Id == use.Id);
        if (i < 1 || _resolutionStack[i - 1] is not ProgramSkillFrame f || !MatchesMouLbDuelParent(f, o) || use.CardId != 0 || use.CardKind != CardKind.Duel ||
            use.PhysicalCardIds?.Count != 0 || use.SelectedActorDuelOrigin is not null ||
            use.Action is not { Type: CardActionType.Use, EffectiveKind: CardKind.Duel, PhysicalCards.Count: 0, ConversionChain.Count: 0, EffectiveSuit: Suit.None, EffectiveIsRed: false } ||
            LifecycleCardUse(o.OuterCardUseFrameId)?.CardAttack is not { Active: false })
            throw new InvalidOperationException("Mobile Liyu Duel requires its real exact paid parent and neutral empty-material use.");
    }
    [NativeUseReturn(229)]
    private void ReturnMouLbNullifiedDuel(CardUseFrame use)
    {
        if (use.DamageTargetDuelOrigin is not { AttackStarted: false } o || !IsMouLbDuelOrigin(o)) return;
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || !MatchesMouLbDuelParent(f, o)) throw new InvalidOperationException("Nullified mobile Liyu Duel lost its parent.");
        CompleteMouLbDuel(f, use.Id, false);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 99)]
    private bool ReturnMouLbDuelAttack(AttackCompletionReceipt c)
    {
        if (c.DamageTargetDuelReturn is not { AttackStarted: true } o || !IsMouLbDuelOrigin(o)) return false;
        if (c.ProgramFrameId != o.ParentProgramFrameId || _resolutionStack.LastOrDefault() is not ProgramSkillFrame f || !MatchesMouLbDuelParent(f, o))
            throw new InvalidOperationException("Mobile Liyu Duel attack lost its complete native return.");
        CompleteMouLbDuel(f, c.ResolutionId, c.DamageWasApplied); return true;
    }
    private void CompleteMouLbDuel(ProgramSkillFrame f, long useId, bool damage)
    {
        var r = f.MouLuBuLiyu!;
        if (LifecycleCardUse(r.OuterUseId)?.CardAttack is not { Active: false }) throw new InvalidOperationException("Liyu return cannot resume another attack.");
        AdvanceEventRulesAndQueueFact(new MouLuBuLiyuDuelReturnedEvent(f.Id, useId, damage));
        UpdateCardAttackState(r.OuterUseId, s => s! with { Active = true }); FinishMouLb(f);
    }
}
