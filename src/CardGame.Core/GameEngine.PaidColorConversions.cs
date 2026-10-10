namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string PaidColorDiscardReason = "program.paid-color-conversion.discard";
    private static bool HasActualRedOrBlackSuit(Suit suit) => suit is Suit.Spade or Suit.Club or Suit.Heart or Suit.Diamond;

    private bool MatchesPaidColorDrawOwner(ProgramSkillFrame root)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == root.Id);
        return index > 0 && _resolutionStack[index - 1] is ProgramLifecycleTriggerWindowFrame parent &&
            parent.Window == SkillProgramTriggerWindow.DrawPhaseEnded && parent.OwnerSeat == root.OwnerSeat &&
            root.OwnerSeat == _currentSeat && _turnNumber > 0 &&
            root.WindowContext is { Window: SkillProgramTriggerWindow.DrawPhaseEnded } c && c.ParentFrameId == parent.Id &&
            c.OwnerSeat == root.OwnerSeat && c.SourceSeat == root.OwnerSeat && c.TargetSeat == root.OwnerSeat &&
            parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count &&
            MountObserverCandidateMatches(root, parent.Candidates[parent.CandidateIndex]) &&
            c.OccurrenceIndex == parent.Candidates[parent.CandidateIndex].OccurrenceIndex;
    }

    private SkillProgramStepOutcome BeginPaidColorConversion(ProgramSkillFrame frame, string bind)
    {
        frame = GetActiveProgramFrame(frame.Id);
        if (frame.PaidColorConversion is not null || !MatchesPaidColorDrawOwner(frame))
            throw new InvalidOperationException("A paid color conversion requires its original completed-draw candidate exactly once.");
        var cards = GetProgramCardSet(frame, bind);
        var owner = _players[frame.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive || !HasRuntimeSkillInstance(owner, frame.SkillId, frame.SkillInstanceId) ||
            cards.CardIds is not [var cardId] || cards.SourceLocations is not [var from] || from.OwnerSeat != owner.Seat ||
            from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || _cardZones.GetLocation(cardId) != from)
        { CancelProgramBindingAndCleanup(frame, "技能来源或原弃牌付款失效，未支付且不授予转换。"); return SkillProgramStepOutcome.AwaitChild; }
        var card = _cardZones.CardsAt(from).Single(c => c.Id == cardId);
        var sequence = _cardMovements.Count == 0 ? 1 : _cardMovements[^1].Sequence + 1;
        var origin = new ProgramPaidColorConversionOrigin(_turnNumber, _currentSeat, frame.GameplayHash,
            cardId, card.Kind, from, EffectiveSuit(owner, card), sequence, sequence, card.IsGeneralWeapon);
        ReplaceRuntimeTop(frame with { PaidColorConversion = new(frame.InstructionIndex, bind, origin),
            PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        MoveCard(card, from, CardLocation.DiscardPile, new(PaidColorDiscardReason));
        frame = GetActiveProgramFrame(frame.Id);
        origin = origin with { SequenceAfter = _cardMovements[^1].Sequence };
        ReplaceRuntimeTop(frame with { PaidColorConversion = frame.PaidColorConversion! with { Origin = origin } });
        AdvanceEventRulesAndQueueFact(new ProgramPaidColorConversionPaidEvent(frame.Id,
            new(frame.SkillId, GetProgramBindingId(frame), owner.Seat, frame.SkillInstanceId), frame.InstructionIndex - 1, origin));
        if (AwaitPaidColorDamageClaimMovements(frame.Id) != SkillProgramStepOutcome.Continue) return SkillProgramStepOutcome.AwaitChild;
        return ResumePaidColorDamageClaims(frame.Id) ? SkillProgramStepOutcome.AwaitChild : SkillProgramStepOutcome.Continue;
    }

    private bool ResumePaidColorDamageClaims(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id ||
            f.PaidColorConversion is null && f.ActualTurnDamageClaim is null) return false;
        AssertPaidColorDamageClaimFrame(f);
        if (AwaitPaidColorDamageClaimMovements(id) != SkillProgramStepOutcome.Continue) return true;
        f = GetActiveProgramFrame(id);
        if (f.PaidColorConversion is { Granted: false } paid)
        {
            var owner = _players[f.OwnerSeat]; var p = paid.Origin;
            if (_winner == Winner.None && owner.IsAlive && HasRuntimeSkillInstance(owner, f.SkillId, f.SkillInstanceId) &&
                p.ActualTurnNumber == _turnNumber && p.TurnOwnerSeat == _currentSeat && HasActualRedOrBlackSuit(p.EffectiveSuit))
            {
                var grant = _turnCardUseEffects.GrantConversion(p.ActualTurnNumber, p.TurnOwnerSeat, f.Id,
                    paid.InstructionIndex - 1, CreateProgramTurnEffectSource(f), paid.SourceBind,
                    SkillProgramCardColorRelation.OppositeBoundCard, IsRedSuit(p.EffectiveSuit), CardKind.Duel, p);
                AdvanceEventRulesAndQueueFact(new CardConversionGrantedEvent(grant));
            }
            ReplaceRuntimeTop(GetActiveProgramFrame(id) with { PaidColorConversion = paid with { Granted = true } });
        }
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive)
        { CancelProgramBindingAndCleanup(GetActiveProgramFrame(id), "仅结清实际付款或领取的子链，取消死亡或胜负已定的未发收益。"); return true; }
        return false;
    }

    private SkillProgramStepOutcome AwaitPaidColorDamageClaimMovements(long id)
    {
        var frame = GetActiveProgramFrame(id);
        if (frame.PendingMovementContinuation is null) return SkillProgramStepOutcome.Continue;
        // Equipment removal can queue Jiuyuan before the ordinary HP/movement windows.
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id))
            return SkillProgramStepOutcome.AwaitChild;
        ReplaceRuntimeTop(GetActiveProgramFrame(id) with { PendingMovementContinuation = null });
        return SkillProgramStepOutcome.Continue;
    }

    private IReadOnlyList<CardConversionSource> GetPaidColorTurnDuelConversions(CharacterState owner,
        Card card, CardKind kind, bool forResponse, CardZoneKind zone)
    {
        if (!owner.IsAlive || forResponse || kind != CardKind.Duel || card.IsGeneralWeapon || zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
            !HasActualRedOrBlackSuit(EffectiveSuit(owner, card))) return [];
        var color = IsRedSuit(EffectiveSuit(owner, card));
        return _turnCardUseEffects.Conversions.Where(g => g.PaidColorOrigin is not null && g.TurnNumber == _turnNumber &&
            g.TurnSeat == _currentSeat && g.Source.OwnerSeat == owner.Seat && g.OutputKind == CardKind.Duel &&
            HasRuntimeSkillInstance(owner, g.Source.SkillId, g.Source.SkillInstanceId) &&
            _contentRegistry.GetSkill(g.Source.SkillId).Program?.GameplayHash == g.PaidColorOrigin.GameplayHash)
            .GroupBy(g => (g.Source.SkillId, g.Source.SkillInstanceId))
            .Select(group => group.OrderByDescending(g => g.GrantSequence).First())
            .Where(g => g.BoundCardIsRed != color)
            .Select(g => new CardConversionSource(g.Source.SkillId, $"{g.Source.BindingId}.turn-{g.EffectIndex}",
                g.Source.OwnerSeat, g.Source.SkillInstanceId)).Distinct().ToArray();
    }

    private void AddPaidColorEquipmentDuelActions(List<LegalAction> actions, CharacterState owner,
        int? selectedPhysicalCardId = null)
    {
        foreach (var card in GetEquipment(owner).Concat(GetHand(owner).Where(c => c.Kind == CardKind.Duel))
                     .Where(c => selectedPhysicalCardId is null || c.Id == selectedPhysicalCardId)
                     .Where(c => !IsTurnHandCardRestricted(owner, c) && !HasProgramCardIdentity(owner, c)))
        foreach (var source in GetPaidColorTurnDuelConversions(owner, card, CardKind.Duel, false, _cardZones.GetLocation(card.Id).Zone))
        foreach (var target in _players.Where(p => p.IsAlive && p.Seat != owner.Seat &&
            !IsCardTargetProhibited(p, CardKind.Duel, EffectiveSuit(owner, card), SuitColor(EffectiveSuit(owner, card)))))
            actions.Add(new LegalAction(LegalActionKind.Duel, card.Id, target.Seat,
                DescribeConversion(source, $"将【{card.DisplayName}】当作【决斗】对 {target.Name} 使用"), CardKind.Duel)
                { ConversionSource = source });
    }

    private bool IsPaidColorEquipmentDuelAction(CharacterState owner, LegalAction action, Card card) =>
        action.Kind == LegalActionKind.Duel && action.PlayedCardKind == CardKind.Duel &&
        action.ConversionSource is { } source && _cardZones.GetLocation(card.Id) == CardLocation.Equipment(owner.Seat) &&
        GetPaidColorTurnDuelConversions(owner, card, CardKind.Duel, false, CardZoneKind.Equipment).Contains(source);

    private bool TracksPaidColorUseAppearance(int actorSeat, CardKind kind, IReadOnlyList<CardConversionSource> chain) =>
        kind == CardKind.Duel && _turnCardUseEffects.Conversions.Any(g => g.PaidColorOrigin is not null &&
            g.TurnNumber == _turnNumber && g.TurnSeat == _currentSeat && g.Source.OwnerSeat == actorSeat &&
            chain.Any(source => source.OwnerSeat == actorSeat && source.SkillId == g.Source.SkillId &&
                source.SkillInstanceId == g.Source.SkillInstanceId && source.BindingId == $"{g.Source.BindingId}.turn-{g.EffectIndex}"));

    private void AssertPaidColorTurnConversions()
    {
        foreach (var g in _turnCardUseEffects.Conversions.Where(g => g.PaidColorOrigin is not null))
        {
            var p = g.PaidColorOrigin!;
            var program = _contentRegistry.GetSkill(g.Source.SkillId).Program;
            var trigger = program?.Triggers.SingleOrDefault(t => t.Id == g.Source.BindingId);
            if (program?.GameplayHash != p.GameplayHash || trigger is null || trigger.Window != SkillProgramTriggerWindow.DrawPhaseEnded ||
                g.EffectIndex != 1 || trigger.Effects.Count != 2 || trigger.Effects[1].Op != SkillProgramEffectOp.DiscardBoundCardForOppositeTurnDuel ||
                trigger.Effects[1].SourceBind != g.SourceBind || p.ActualTurnNumber != g.TurnNumber || p.TurnOwnerSeat != g.TurnSeat ||
                g.Source.OwnerSeat != g.TurnSeat || g.BoundCardIsRed != IsRedSuit(p.EffectiveSuit) || !HasActualRedOrBlackSuit(p.EffectiveSuit) ||
                g.OutputKind != CardKind.Duel || !IsValidPlayerSeat(g.Source.OwnerSeat) || !IsActualPaidColorMovement(g.Source.OwnerSeat, p) ||
                CompleteProgramEventHistory().OfType<ProgramPaidColorConversionPaidEvent>().Count(e => e.ProgramFrameId == g.ParentFrameId &&
                    e.EffectIndex == g.EffectIndex && e.Origin == p && e.Source == new CardConversionSource(g.Source.SkillId,
                        g.Source.BindingId, g.Source.OwnerSeat, g.Source.SkillInstanceId)) != 1)
                throw new InvalidOperationException("A real-discard conversion lost its original paid entity, color, actual turn or exact source producer.");
        }
    }
    private bool IsActualPaidColorMovement(int owner, ProgramPaidColorConversionOrigin p) =>
        p.From.OwnerSeat == owner && p.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
        _cardMovements.Count(m => m.Sequence == p.MovementSequence && m.CardId == p.CardId && m.From == p.From &&
            m.CardKind == p.CardKind && m.Reason.Value == PaidColorDiscardReason &&
            (m.To == CardLocation.DiscardPile && !p.GeneratedMaterial ||
                p.GeneratedMaterial && p.From.Zone == CardZoneKind.Equipment && m.To == CardLocation.OutsideGame)) == 1 &&
        p.SequenceAfter >= p.MovementSequence && _cardMovements.Where(m => m.Sequence > p.MovementSequence && m.Sequence <= p.SequenceAfter)
            .All(m => p.CardKind == CardKind.WoodenOx && p.From == CardLocation.Equipment(owner) &&
                m.From == CardLocation.WoodenOxGrain(owner) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard);

    private sealed partial class ProgramSkillHost : IPaidColorDamageClaimProgramHost
    {
        public SkillProgramStepOutcome DiscardBoundCardForOppositeTurnDuel(ProgramSkillFrame f, string bind) => engine.BeginPaidColorConversion(f, bind);
        public SkillProgramStepOutcome ClaimActualTurnDamageEntities(ProgramSkillFrame f) => engine.BeginActualTurnDamageClaim(f);
    }
}
