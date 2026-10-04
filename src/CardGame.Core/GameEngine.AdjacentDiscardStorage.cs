namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TracksAdjacentDiscardStorage => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.StoreAdjacentDiscardedSlash);

    private CardKind? FreezeAdjacentDiscardPaymentIdentity(Card card, CardLocation from, CardLocation to, CardMoveReason reason)
    {
        if (!TracksAdjacentDiscardStorage || from.OwnerSeat is not { } source || !_players[source].IsAlive ||
            from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment) ||
            to != CardLocation.DiscardPile && to != CardLocation.Processing || !IsProgramDiscardOriginReason(reason)) return null;
        var kind = from.Zone == CardZoneKind.Hand
            ? GetProgramCardIdentityMatches(_players[source], card).FirstOrDefault()?.Identity.OutputKind ?? card.Kind
            : card.Kind;
        return SlashKinds.Contains(kind) ? kind : null;
    }

    private void CaptureAdjacentDiscardOrigin(Card card, CardMovementRecord move, CardKind? frozenIdentity)
    {
        if (!TracksAdjacentDiscardStorage) return;
        var carried = _resolutionStack.SelectMany(f => f.PendingAdjacentDiscardOrigins?.Entries ?? [])
            .SingleOrDefault(r => r.CardId == card.Id);
        if (carried is not null)
        {
            var owner = _resolutionStack.Single(f => f.Id == carried.OwnerFrameId);
            var remaining = owner.PendingAdjacentDiscardOrigins!.Entries.Where(r => r.CardId != card.Id).ToArray();
            ReplaceRuntimeFrame(owner.Id, owner with { PendingAdjacentDiscardOrigins = remaining.Length == 0 ? null : new(remaining) });
            var previous = _cardMovements.LastOrDefault(m => m.CardId == card.Id && m.Sequence < move.Sequence);
            if (move.To == CardLocation.DiscardPile && move.From == CardLocation.Processing &&
                previous is not null && previous.Sequence == carried.EntryMovementSequence &&
                GetProgramDiscardSource(move) == carried.From)
                AdvanceEventRulesAndQueueFact(new ProgramAdjacentDiscardOriginEvent(move.Sequence, card.Id, carried.SourceSeat,
                    carried.PreviousLivingSeat, carried.NextLivingSeat, carried.EffectiveKind));
            // An outside move, cancellation or non-discard cleanup irreversibly ends this original discard provenance.
            return;
        }
        if (move.From.OwnerSeat is not { } source || !_players[source].IsAlive ||
            move.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment) ||
            move.To != CardLocation.DiscardPile && move.To != CardLocation.Processing ||
            !IsProgramDiscardOriginReason(move.Reason) || frozenIdentity is not { } kind || !SlashKinds.Contains(kind)) return;
        var living = _players.Where(p => p.IsAlive).Select(p => p.Seat).OrderBy(s => s).ToArray();
        var index = Array.IndexOf(living, source);
        if (index < 0) return;
        var previousSeat = living[(index + living.Length - 1) % living.Length];
        var nextSeat = living[(index + 1) % living.Length];
        if (move.To == CardLocation.DiscardPile)
            AdvanceEventRulesAndQueueFact(new ProgramAdjacentDiscardOriginEvent(move.Sequence, card.Id, source, previousSeat, nextSeat, kind));
        else
        {
            var owner = _resolutionStack.LastOrDefault() ?? throw new InvalidOperationException("A private discard payment requires its actual owning frame.");
            var origin = new PendingAdjacentDiscardOrigin(owner.Id, move.Sequence, card.Id, move.From,
                move.Reason.Value, source, previousSeat, nextSeat, kind);
            ReplaceRuntimeFrame(owner.Id, owner with { PendingAdjacentDiscardOrigins = new((owner.PendingAdjacentDiscardOrigins?.Entries ?? []).Append(origin).ToArray()) });
        }
    }

    private void AssertPendingAdjacentDiscardOrigins()
    {
        var entries = _resolutionStack.SelectMany(f => f.PendingAdjacentDiscardOrigins?.Entries ?? []).ToArray();
        if (entries.Select(e => e.CardId).Distinct().Count() != entries.Length) throw new InvalidOperationException("An entity has two private discard provenance owners.");
        foreach (var frame in _resolutionStack.Where(f => f.PendingAdjacentDiscardOrigins is not null))
        foreach (var r in frame.PendingAdjacentDiscardOrigins!.Entries)
            if (!TracksAdjacentDiscardStorage || r.OwnerFrameId != frame.Id || r.SourceSeat != r.From.OwnerSeat ||
                r.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment) ||
                !SlashKinds.Contains(r.EffectiveKind) || !IsValidPlayerSeat(r.PreviousLivingSeat) || !IsValidPlayerSeat(r.NextLivingSeat) ||
                !_cardMovements.Any(m => m.Sequence == r.EntryMovementSequence && m.CardId == r.CardId && m.From == r.From &&
                    m.To == CardLocation.Processing && m.Reason.Value == r.Reason && IsProgramDiscardOriginReason(m.Reason)) ||
                _cardMovements.Last(m => m.CardId == r.CardId).Sequence != r.EntryMovementSequence ||
                _cardZones.GetLocation(r.CardId) != CardLocation.Processing ||
                CompleteProgramEventHistory().OfType<ProgramAdjacentDiscardOriginEvent>().Any(e => e.MovementSequence == r.EntryMovementSequence))
                throw new InvalidOperationException("Private discard provenance lost its exact owning payment, unpublished identity or held entity.");
    }

    private ProgramAdjacentDiscardOriginEvent? AdjacentDiscardOrigin(CardMovementRecord move) =>
        CompleteProgramEventHistory().OfType<ProgramAdjacentDiscardOriginEvent>()
            .SingleOrDefault(e => e.MovementSequence == move.Sequence && e.CardId == move.CardId);

    private int[] MatchingAdjacentDiscardIndexes(CardMovementBatchContext batch, ProgramTriggerCandidate candidate) =>
        batch.Movements.Select((move, index) => (move, index, origin: AdjacentDiscardOrigin(move)))
            .Where(x => x.move.To == CardLocation.DiscardPile && _cardZones.GetLocation(x.move.CardId) == CardLocation.DiscardPile &&
                x.origin is { } origin && SlashKinds.Contains(origin.EffectiveKind) &&
                (candidate.OwnerSeat == origin.SourceSeat || candidate.OwnerSeat == origin.PreviousLivingSeat || candidate.OwnerSeat == origin.NextLivingSeat))
            .Select(x => x.index).ToArray();

    private sealed partial class ProgramSkillHost : IAdjacentDiscardAndRoundAlcoholHost
    {
        public SkillProgramStepOutcome OfferCurrentSlashFireAndExtraTarget(ProgramSkillFrame frame) => engine.BeginCurrentSlashFireOffer(frame);
        public SkillProgramStepOutcome StoreAdjacentDiscardedSlash(ProgramSkillFrame frame, CardZoneKind zone) => engine.BeginAdjacentDiscardStorage(frame, zone);
        public SkillProgramStepOutcome PayCompletedUseDiscardOrLoseHp(ProgramSkillFrame frame) => engine.BeginCompletedUsePayment(frame);
        public SkillProgramStepOutcome UseRoundPricedPileDyingAlcohol(ProgramSkillFrame frame, string stateId, CardZoneKind zone) => engine.BeginRoundPileAlcohol(frame, stateId, zone);
    }

    private SkillProgramStepOutcome BeginAdjacentDiscardStorage(ProgramSkillFrame input, CardZoneKind zone)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.AdjacentDiscardStorage is not null || f.WindowContext is not
            { Window: SkillProgramTriggerWindow.DiscardPileReceived, MovementBatch: { } batch, MovementIndex: { } index } ||
            index < 0 || index >= batch.Movements.Count)
            throw new InvalidOperationException("Adjacent storage lost its exact original discarded entity.");
        var move = batch.Movements[index]; var origin = AdjacentDiscardOrigin(move);
        if (origin is null || !_cardMovements.Contains(move) || move.To != CardLocation.DiscardPile ||
            !SlashKinds.Contains(origin.EffectiveKind) ||
            f.OwnerSeat != origin.SourceSeat && f.OwnerSeat != origin.PreviousLivingSeat && f.OwnerSeat != origin.NextLivingSeat)
            throw new InvalidOperationException("Adjacent storage lost its frozen identity or neighbor proof.");
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            _cardZones.GetLocation(move.CardId) != CardLocation.DiscardPile)
        { CancelProgramBindingAndCleanup(f, "弃牌实体或持久区来源失效，未储存牌。"); return SkillProgramStepOutcome.AwaitChild; }
        var card = _cardZones.CardsAt(CardLocation.DiscardPile).Single(c => c.Id == move.CardId);
        ReplaceRuntimeTop(f with { AdjacentDiscardStorage = new(f.InstructionIndex, card.Id, move.Sequence, 0, zone) });
        MoveCard(card, CardLocation.DiscardPile, new(zone, f.OwnerSeat), new($"skill-program.{f.SkillId}.adjacent-discard-store"), stored =>
        {
            var active = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(active with { AdjacentDiscardStorage = active.AdjacentDiscardStorage! with { StoreMovementSequence = stored.Sequence } });
            AdvanceEventRulesAndQueueFact(new ProgramAdjacentDiscardStoredEvent(f.Id,
                new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId), card.Id, move.Sequence, stored.Sequence, zone));
        });
        AdvanceRuntimeProgram(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }

    private bool ResumeAdjacentDiscardStorage(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.AdjacentDiscardStorage is not { }) return false;
        AssertAdjacentDiscardStorage(f);
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
            TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(id)) return true;
        ReplaceRuntimeTop(f with { AdjacentDiscardStorage = null }); return false;
    }

    private void AssertAdjacentDiscardStorage(ProgramSkillFrame f)
    {
        if (f.AdjacentDiscardStorage is not { } r) return;
        var paused = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
        if (paused.Op != SkillProgramEffectOp.StoreAdjacentDiscardedSlash || r.InstructionIndex != f.InstructionIndex || paused.DestinationZone != r.Zone ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.DiscardPileReceived, MovementBatch: { } batch, MovementIndex: { } index } ||
            index < 0 || index >= batch.Movements.Count || batch.Movements[index].Sequence != r.DiscardMovementSequence || batch.Movements[index].CardId != r.CardId ||
            !_cardMovements.Any(m => m.Sequence == r.StoreMovementSequence && m.CardId == r.CardId && m.From == CardLocation.DiscardPile &&
                m.To == new CardLocation(r.Zone, f.OwnerSeat) && m.Reason.Value == $"skill-program.{f.SkillId}.adjacent-discard-store") ||
            !CompleteProgramEventHistory().OfType<ProgramAdjacentDiscardStoredEvent>().Any(e => e.ProgramFrameId == f.Id &&
                e.Source == new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) && e.CardId == r.CardId &&
                e.DiscardMovementSequence == r.DiscardMovementSequence && e.StoreMovementSequence == r.StoreMovementSequence && e.Zone == r.Zone))
            throw new InvalidOperationException("Stored discard receipt lost its exact committed movement/source.");
    }
}
