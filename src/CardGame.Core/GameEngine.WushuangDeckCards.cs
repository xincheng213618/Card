namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class WushuangDeckCardsCapability(GameEngine engine) : IWushuangDeckCardsHost
    {
        public SkillProgramStepOutcome ObtainWushuangDeckCards(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ObtainWushuangDeckCards(frame, effect);
    }

    private SkillProgramStepOutcome ObtainWushuangDeckCards(ProgramSkillFrame supplied, SkillProgramEffect effect)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!IsWushuangMode || f.WushuangDeckGain is not null || effect.Amount is < 1 or > 4 ||
            _resolutionStack.LastOrDefault()?.Id != f.Id)
            throw new InvalidOperationException("A pile gain requires its active single-duel program instruction.");
        if (!_players[f.OwnerSeat].IsAlive || !EnsureDrawPile()) return SkillProgramStepOutcome.Continue;
        var cards = _cardZones.CardsAt(CardLocation.DrawPile).Reverse().Where(c =>
            (effect.OutputKind is null || c.Kind == effect.OutputKind) &&
            (effect.CardCategories.Count == 0 || MatchesProgramCardCategory(c.Kind, effect.CardCategories)))
            .Take(effect.Amount).Select(c => c.Id).ToArray();
        if (cards.Length == 0) return SkillProgramStepOutcome.Continue;
        var receipt = new WushuangDeckGainReceipt(f.InstructionIndex, new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId),
            f.GameplayHash, effect.Amount, effect.OutputKind, effect.CardCategories.Count == 0 ? null : effect.CardCategories[0], _movementSequence)
        { CardIds = cards };
        ReplaceRuntimeTop(f with { WushuangDeckGain = receipt });
        MoveProgramCardsFromMultipleSources(cards, CardLocation.Hand(f.OwnerSeat), new("wushuang.obtain-deck-cards"), (batch, records) =>
        {
            var active = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(active with { WushuangDeckGain = receipt with { BatchId = batch, SequenceAfter = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new WushuangDeckCardsObtainedEvent(f.Id, receipt.InstructionIndex, f.OwnerSeat,
                records.Count, batch, receipt.SequenceBefore, _movementSequence));
        });
        var outcome = AwaitProgramBoundCardMovements(f.Id, f.OwnerSeat);
        if (outcome == SkillProgramStepOutcome.Continue)
            ReplaceRuntimeTop(GetActiveProgramFrame(f.Id) with { WushuangDeckGain = null });
        return outcome;
    }

    private bool ValidWushuangDeckGain(ProgramSkillFrame f)
    {
        if (f.WushuangDeckGain is not { } r || r.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) ||
            r.GameplayHash != f.GameplayHash || r.InstructionIndex != f.InstructionIndex || r.BatchId <= 0 ||
            r.SequenceAfter <= r.SequenceBefore || r.CardIds.Count is < 1 or > 4 || r.CardIds.Count > r.Requested || r.CardIds.Distinct().Count() != r.CardIds.Count)
            return false;
        var moves = _cardMovements.Where(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter).ToArray();
        return moves.Length == r.CardIds.Count && moves.Select(m => m.CardId).SequenceEqual(r.CardIds) &&
            moves.All(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == "wushuang.obtain-deck-cards" &&
                (r.Kind is null || m.CardKind == r.Kind) && (r.Category is null || MatchesProgramCardCategory(m.CardKind, [r.Category.Value]))) &&
            ProgramEventHistory<WushuangDeckCardsObtainedEvent>().Count(e => e.ProgramFrameId == f.Id &&
                e.InstructionIndex == r.InstructionIndex && e.BatchId == r.BatchId && e.Count == moves.Length &&
                e.SequenceBefore == r.SequenceBefore && e.SequenceAfter == r.SequenceAfter) == 1;
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 62000)]
    private void AssertWushuangDeckGain(ProgramSkillFrame f, ProgramExecutionPlan plan)
    {
        if (f.WushuangDeckGain is not { } r) return;
        if (f.InstructionIndex < 1 || f.InstructionIndex > plan.Instructions.Count ||
            plan.Instructions[f.InstructionIndex - 1] is not { Op: SkillProgramEffectOp.ObtainWushuangDeckCards } effect ||
            !IsWushuangMode || effect.Amount != r.Requested || effect.OutputKind != r.Kind ||
            (effect.CardCategories.Count == 0 ? null : (SkillProgramCardCategory?)effect.CardCategories[0]) != r.Category ||
            r.CardIds is not System.Collections.IList { IsReadOnly: true } || !ValidWushuangDeckGain(f))
            throw new InvalidOperationException("The pile gain lost its instruction, immutable payment or physical movement.");
    }

    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 62000)]
    private bool IsWushuangDeckGainMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        effect?.Op == SkillProgramEffectOp.ObtainWushuangDeckCards && pending.SubjectSeat == f.OwnerSeat &&
        pending.BeforeCount == 0 && pending.CoverageResultBind is null && ValidWushuangDeckGain(f);

    [ProgramContinuation(ProgramContinuationKind.Movement, 62000)]
    private bool ReturnWushuangDeckGain(ProgramSkillFrame f)
    {
        if (f.WushuangDeckGain is null) return false;
        if (!ValidWushuangDeckGain(f)) throw new InvalidOperationException("A pile gain lost its exact issued physical movement.");
        ReplaceRuntimeTop(f with { WushuangDeckGain = null, PendingMovementContinuation = null });
        AdvanceRuntimeProgram(f.Id);
        return true;
    }
}
