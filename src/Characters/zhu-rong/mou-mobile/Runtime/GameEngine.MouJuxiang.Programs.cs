namespace CardGame.Core;

public sealed partial class GameEngine
{

    // This fact is source-independent: losing/reacquiring Juxiang does not erase
    // an already declared Barbarian Assault in the same actual turn.
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 30400)]
    private void ObserveMouZhuRongAssault(CardUseDeclaredEvent e)
    {
        if (HasMouZhuRong && e.CardKind == CardKind.BarbarianAssault)
            AdvanceEventRulesAndQueueFact(new MouZhuRongAssaultUsedEvent(MouZhuRongTurnOrdinal, e.SourceSeat, e.ResolutionId));
    }

    private SkillProgramStepOutcome GiftMouZhuRongOutsideAssault(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != MouZhuRongComposition.Juxiang || f.TriggerId != "outside-assault-gift" || f.InstructionIndex != 2 ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } || f.SelectedTargetSeats is not [var recipient] || !_players[recipient].IsAlive ||
            ProgramEventHistory<MouZhuRongAssaultGeneratedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Juxiang requires its current Ending and one living recipient.");
        // The fixed mode recipe, not the depleted stock, supplies printed faces.
        // A mode without this kind uses the ordinary game's three printed faces.
        var templates = CreateDeckFromRegistry(_contentRegistry, ResolveDeckDefinition(_contentRegistry, _options.DeckId ?? _modeDefinition.DeckId))
            .Where(c => c.Kind == CardKind.BarbarianAssault).Select(c => (c.Suit, c.Rank)).Distinct().ToArray();
        if (templates.Length == 0) templates = [(Suit.Spade, 7), (Suit.Spade, 13), (Suit.Club, 7)];
        var before = _random.State; var template = templates[_random.Next(templates.Length)];
        var card = new Card(checked(CreateCardZoneDiagnostics().Select(c => c.CardId).DefaultIfEmpty(0).Max() + 1), CardKind.BarbarianAssault, template.Suit, template.Rank);
        _cardZones.AddGeneratedCard(card); _generatedPhysicalCardIds.Add(card.Id);
        AdvanceEventRulesAndQueueFact(new MouZhuRongAssaultGeneratedEvent(f.Id, MouZhuRongSource(f), recipient, card.Id, before, _random.State));
        MoveCard(card, CardLocation.OutsideGame, CardLocation.Hand(recipient), new("skill-program.mou-mobile:juxiang.outside-gift"), NativeGiveCompletion(f.Id,f.OwnerSeat,recipient,[card.Id],_movementSequence,null), disclosure: CardMovementDisclosure.FaceDown);
        return AwaitProgramBoundCardMovements(f.Id, f.OwnerSeat);
    }

    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 30400)]
    private bool IsMouZhuRongGiftMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation m) =>
        e?.Op == SkillProgramEffectOp.GiftMouZhuRongOutsideAssault && f.InstructionIndex == 2 && f.SkillId == MouZhuRongComposition.Juxiang &&
        m.SubjectSeat == f.OwnerSeat && m.CoverageResultBind is null &&
        ProgramEventHistory<MouZhuRongAssaultGeneratedEvent>().SingleOrDefault(g => g.FrameId == f.Id) is { } generated &&
        generated.Source == MouZhuRongSource(f) && f.SelectedTargetSeats.SequenceEqual([generated.RecipientSeat]) &&
        _cardMovements.Count(move => move.CardId == generated.CardId && move.From == CardLocation.OutsideGame &&
            move.To == CardLocation.Hand(generated.RecipientSeat) && move.Reason.Value == "skill-program.mou-mobile:juxiang.outside-gift") == 1;
}
