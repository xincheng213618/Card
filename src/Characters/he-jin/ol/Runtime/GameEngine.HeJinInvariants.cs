namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidHeJinMouzhu(ProgramSkillFrame f)
    {
        if (f.HeJinMouzhu is not { } r || f.SkillId != "ol:mouzhu" || f.TriggerId is not null || f.WindowContext is not null ||
            f.InstructionIndex != 1 || r.InstructionIndex != 1 || r.Source != HeJinSource(f) || r.GameplayHash != f.GameplayHash ||
            r.ActualTurn != _turnNumber || r.PhaseInstanceId != _cardUseDebitPhaseInstanceId || f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats is not [var actor] || actor != r.ActorSeat || actor == f.OwnerSeat || !IsValidPlayerSeat(actor) ||
            !Enum.IsDefined(r.Stage) || r.StartedAt < 0 || r.StartedAt > _movementSequence || r.HandIds.Count == 0 || r.HandIds.Distinct().Count() != r.HandIds.Count ||
            GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.ResolveMouzhuHandDemandAndAssault ||
            !r.HandIds.Order().SequenceEqual(HeJinHandAt(r.StartedAt, actor)) ||
            CompleteProgramEventHistory().OfType<HeJinMouzhuStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash &&
                e.ActualTurn == r.ActualTurn && e.PhaseInstanceId == r.PhaseInstanceId && e.ActorSeat == actor && e.MovementSequence == r.StartedAt && e.HandCount == r.HandIds.Count) != 1) return false;
        var paid = CompleteProgramEventHistory().OfType<HeJinMouzhuGiftPaidEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Stage == HeJinMouzhuStage.ChoosingGift)
            return r.GiftId is null && r.Before == 0 && r.After == 0 && r.Assault is null && paid.Length == 0 &&
                GetHand(_players[actor]).Select(c => c.Id).Order().SequenceEqual(r.HandIds.Order());
        if (r.GiftId is not { } id || !r.HandIds.Contains(id) || r.Before < r.StartedAt || r.After <= r.Before || r.After > _movementSequence ||
            paid is not [var p] || p.ActorSeat != actor || p.OwnerSeat != f.OwnerSeat || p.Before != r.Before || p.After != r.After ||
            _cardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(actor) && m.To == CardLocation.Hand(f.OwnerSeat) &&
                m.Reason.Value == HeJinGiftReason && m.Sequence > r.Before && m.Sequence <= r.After) != 1) return false;
        if (r.Stage != HeJinMouzhuStage.AssaultIssued) return r.Assault is null;
        return r.Assault is { } a && !a.AttackStarted && a.ProgramFrameId == f.Id && a.InstructionIndex == r.InstructionIndex &&
            a.CardUseFrameId > f.Id && a.Source == r.Source && a.GameplayHash == r.GameplayHash && a.ActualTurn == r.ActualTurn &&
            a.PhaseInstanceId == r.PhaseInstanceId && a.ActorSeat == actor && a.TargetSeat == f.OwnerSeat && a.CardKind is CardKind.Slash or CardKind.Duel &&
            CompleteProgramEventHistory().OfType<HeJinMouzhuAssaultIssuedEvent>().Count(e => e.Origin == a) == 1;
    }
    private int[] HeJinHandAt(long sequence, int seat) => _cardMovements.Where(m => m.Sequence <= sequence && m.From != m.To).GroupBy(m => m.CardId)
        .Select(g => g.MaxBy(m => m.Sequence)!).Where(m => m.To == CardLocation.Hand(seat)).Select(m => m.CardId).Order().ToArray();
    // Purely structural: this is also called while ActiveDying filters the suspended original victim.
    private bool ValidHeJinYanhuo(ProgramSkillFrame f)
    {
        if (f.HeJinYanhuo is not { } r || f.SkillId != "ol:yanhuo" || f.InstructionIndex != 1 || r.InstructionIndex != 1 ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || r.Source != HeJinSource(f) || r.GameplayHash != f.GameplayHash ||
            r.ActualTurn != _turnNumber || r.TurnOwnerSeat != _currentSeat || !IsValidPlayerSeat(r.KillerSeat) || r.KillerSeat == f.OwnerSeat ||
            !Enum.IsDefined(r.Stage) || !ExactHeJinDeathEntry(f, out var death, out var w) || w.Id != r.DeathWindowId || w.CandidateIndex != r.CandidateIndex ||
            w.Step != r.WindowStep || w.Candidates[w.CandidateIndex] != r.Candidate || r.Limit < 1 || r.Limit != HeJinDeathOwnedCardCount(death) ||
            GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.ResolveYanhuoKillerDiscard) return false;
        var d = r.OriginalDeath;
        if (death.Id != d.FrameId || death.ParentFrameId != d.ParentFrameId || death.VictimSeat != d.VictimSeat || death.KillerSeat != d.KillerSeat ||
            death.KillerSeat != r.KillerSeat || death.ReturnKind != d.ReturnKind || death.Step != d.Step || death.OwnerDiedProgramsResolved != d.OwnerDiedProgramsResolved ||
            death.KillerProgramsResolved != d.KillerProgramsResolved || !death.CleanedUpCardIds.SequenceEqual(d.CleanedUpCardIds) ||
            death.PendingRecoveryAttempts is { Count: > 0 } || death.PaidFactionRequestCostRecovery is not null ||
            death.HeJinYanhuoCards is not { } cards || cards.OwnerSeat != f.OwnerSeat || cards.HandIds.Concat(cards.EquipmentIds).Distinct().Count() != r.Limit ||
            cards.HandIds.Concat(cards.EquipmentIds).Any(id => !death.CleanedUpCardIds.Contains(id)) ||
            CompleteProgramEventHistory().OfType<HeJinYanhuoDeathCapturedEvent>().Count(e => e.DeathFrameId == death.Id && e.OwnerSeat == f.OwnerSeat &&
                e.HandCount == cards.HandIds.Count && e.EquipmentCount == cards.EquipmentIds.Count) != 1 ||
            CompleteProgramEventHistory().OfType<HeJinYanhuoStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash &&
                e.DeathFrameId == death.Id && e.KillerSeat == r.KillerSeat && e.Limit == r.Limit && e.ReturnHash == HeJinDeathReturnHash(r)) != 1) return false;
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (r.OriginalDying is { } dy)
        {
            if (death.ReturnKind != DeathReturnKind.Dying || i < 3 || _resolutionStack[i - 3] is not DyingFrame actual || actual.Id != dy.FrameId ||
                actual.Id != death.ParentFrameId || actual.ParentFrameId != dy.ParentFrameId || actual.VictimSeat != dy.VictimSeat || actual.KillerSeat != dy.KillerSeat ||
                actual.Continuation != dy.Continuation || actual.Step != dy.Step || actual.ResponderIndex != dy.ResponderIndex ||
                !actual.ResponderSeats.SequenceEqual(dy.ResponderSeats) || !actual.AttemptedSelfDyingBindings.SequenceEqual(dy.AttemptedSelfDyingBindings) ||
                actual.PendingRecoveryAttempts is { Count: > 0 } || actual.PaidFactionRequestCostRecovery is not null) return false;
        }
        else if (death.ReturnKind != DeathReturnKind.ProgramSkill || i < 3 || _resolutionStack[i - 3] is not ProgramSkillFrame parent || parent.Id != death.ParentFrameId) return false;
        if (r.OriginalDamage is { } dam)
        {
            if (r.OriginalDying is not { Continuation: DyingContinuationKind.Damage } damageDying || damageDying.ParentFrameId != dam.FrameId ||
                _resolutionStack.Take(i - 2).OfType<DamageFrame>().SingleOrDefault(x => x.Id == dam.FrameId) is not { } actual ||
                actual.ParentFrameId != dam.ParentFrameId || actual.SourceSeat != dam.SourceSeat || actual.TargetSeat != dam.TargetSeat ||
                actual.Amount != dam.Amount || actual.Nature != dam.Nature || actual.Step != dam.Step) return false;
        }
        else if (r.OriginalDying?.Continuation == DyingContinuationKind.Damage) return false;
        if (r.OriginalAttackOwnerFrameId is { } attackId && !_resolutionStack.Take(i - 2).Any(x => x.Id == attackId &&
            x is ProgramSkillFrame { AttackAttempt: not null } or ProgramSkillFrame { CardAttack: not null } or CardUseFrame { CardAttack: not null } or JudgmentFrame { CardAttack: not null })) return false;
        if (r.SelectedCards.Count > r.Limit || r.SelectedCards.Select(s => s.CardId).Distinct().Count() != r.SelectedCards.Count ||
            r.SelectedCards.Any(s => s.From.OwnerSeat != r.KillerSeat || s.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment))) return false;
        var paid = CompleteProgramEventHistory().OfType<HeJinYanhuoDiscardPaidEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Stage == HeJinYanhuoStage.ChoosingCard)
            return r.Discarded == 0 && r.Before == 0 && r.After == 0 && paid.Length == 0 && r.SelectedCards.All(s => _cardZones.GetLocation(s.CardId) == s.From);
        if (r.Discarded != r.SelectedCards.Count || r.Before < 0 || r.After < r.Before || r.After > _movementSequence || paid.Length != r.Discarded) return false;
        return r.SelectedCards.Select((s, n) => paid.Count(e => e.Index == n && e.KillerSeat == r.KillerSeat && e.CardId == s.CardId && e.From == s.From &&
            e.Before == r.Before && e.After == r.After) == 1 && _cardMovements.Count(m => m.CardId == s.CardId && m.From == s.From &&
                m.To == (s.From.Zone == CardZoneKind.Equipment && GetAttackCard(s.CardId).IsGeneralWeapon ? CardLocation.OutsideGame : CardLocation.DiscardPile) &&
                m.Reason.Value == HeJinDiscardReason && m.Sequence > r.Before && m.Sequence <= r.After) == 1).All(x => x);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 26710)]
    private void RequireHeJin(ProgramSkillFrame f)
    {
        if (f.HeJinMouzhu is not null && !ValidHeJinMouzhu(f) || f.HeJinYanhuo is not null && !ValidHeJinYanhuo(f))
            throw new InvalidOperationException("He Jin lost its exact source, private payment, native death cursor or issued card origin.");
        var choosing = f.HeJinMouzhu?.Stage is HeJinMouzhuStage.ChoosingGift or HeJinMouzhuStage.ChoosingAssault || f.HeJinYanhuo?.Stage == HeJinYanhuoStage.ChoosingCard;
        if (choosing && _resolutionStack.LastOrDefault()?.Id == f.Id && (_pendingDecision is not { IsPrivate: true } p ||
            p.PlayerSeat != (f.HeJinMouzhu?.ActorSeat ?? f.OwnerSeat) || !AssistedChoicesEqual(p.Choices, f.HeJinMouzhu is not null ? HeJinMouzhuChoices(f) : HeJinYanhuoChoices(f))))
            throw new InvalidOperationException("He Jin lost the actual chooser's private prompt.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 26710)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 26710)]
    private bool ContinuePaidHeJin(ProgramSkillFrame f) => f.HeJinMouzhu is { Stage: not HeJinMouzhuStage.ChoosingGift } && ValidHeJinMouzhu(f) ||
        f.HeJinYanhuo is not null && ValidHeJinYanhuo(f);
}
