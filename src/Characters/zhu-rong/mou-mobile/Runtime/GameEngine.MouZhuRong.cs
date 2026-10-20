namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasMouZhuRong => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GiftMouZhuRongOutsideAssault);
    private int MouZhuRongTurnOrdinal => ProgramEventHistory<TurnStartedEvent>().Count();
    private static CardConversionSource MouZhuRongSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    // This fact is source-independent: losing/reacquiring Juxiang does not erase
    // an already declared Barbarian Assault in the same actual turn.
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 30400)]
    private void ObserveMouZhuRongAssault(CardUseDeclaredEvent e)
    {
        if (HasMouZhuRong && e.CardKind == CardKind.BarbarianAssault)
            AdvanceEventRulesAndQueueFact(new MouZhuRongAssaultUsedEvent(MouZhuRongTurnOrdinal, e.SourceSeat, e.ResolutionId));
    }

    private MouZhuRongLierenWonEvent? MouZhuRongWin(int owner, ProgramSkillWindowContext c) =>
        c.CardUse is { } a ? ProgramEventHistory<MouZhuRongLierenWonEvent>().SingleOrDefault(e =>
            e.Source.OwnerSeat == owner && e.UseId == a.ParentCardUseFrameId && e.ActionId == a.CardActionId) : null;

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 30400,
        SkillProgramEffectOp.RecordMouZhuRongLieren, SkillProgramEffectOp.SelectMouZhuRongLierenTarget, SkillProgramEffectOp.GiftMouZhuRongOutsideAssault)]
    private bool CanOfferMouZhuRong(ProgramTriggerCandidate candidate, SkillProgramTrigger t, ProgramSkillWindowContext c)
    {
        if (t.Effects.Any(e => e.Op == SkillProgramEffectOp.GiftMouZhuRongOutsideAssault))
            return c.Window == SkillProgramTriggerWindow.TurnEnding && c.OwnerSeat == candidate.OwnerSeat &&
                !ProgramEventHistory<MouZhuRongAssaultUsedEvent>().Any(e => e.TurnOrdinal == MouZhuRongTurnOrdinal && e.ActorSeat == candidate.OwnerSeat);
        if (c.CardUse is not { } a || a.ActorSeat != candidate.OwnerSeat || !IsSlashCard(a.EffectiveKind)) return false;
        if (t.Effects.Any(e => e.Op == SkillProgramEffectOp.RecordMouZhuRongLieren))
            return c.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized &&
                LifecycleCardUse(a.ParentCardUseFrameId) is { Action: { Type: CardActionType.Use } action } use &&
                use.TargetSeats is [var target] && target != candidate.OwnerSeat && c.TargetSeat == target &&
                action.ActionId == a.CardActionId && !ProgramEventHistory<MouZhuRongLierenWonEvent>().Any(e => e.UseId == use.Id && e.Source.OwnerSeat == candidate.OwnerSeat);
        return c.Window == SkillProgramTriggerWindow.CardUseCompleted && MouZhuRongWin(candidate.OwnerSeat, c) is { } win &&
            !ProgramEventHistory<MouZhuRongLierenDamageChosenEvent>().Any(e => e.UseId == win.UseId && e.OwnerSeat == candidate.OwnerSeat) &&
            _players.Any(p => p.IsAlive && p.Seat != candidate.OwnerSeat && p.Seat != win.OpponentSeat);
    }

    private void RecordMouZhuRongLieren(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != MouZhuRongComposition.Lieren || f.TriggerId != "unique-slash-contest" || f.InstructionIndex != 2 ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } a } c ||
            f.PindianResultBindings.SingleOrDefault(b => b.Name == "contest") is not { } result ||
            result.SourceSeat != f.OwnerSeat || result.OpponentSeat != c.TargetSeat)
            throw new InvalidOperationException("Lieren lost its original single-target Slash Pindian return.");
        if (result.SourceWon)
            AdvanceEventRulesAndQueueFact(new MouZhuRongLierenWonEvent(f.Id, MouZhuRongSource(f), f.GameplayHash,
                a.ParentCardUseFrameId, a.CardActionId, result.OpponentSeat));
    }

    private IReadOnlyList<int> MouZhuRongLierenOtherTargets(int owner)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.OwnerSeat != owner || f.SkillId != MouZhuRongComposition.Lieren ||
            f.TriggerId != "won-slash-damage" || f.InstructionIndex != 1 ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseCompleted } c ||
            MouZhuRongWin(owner, c) is not { } won || won.Source.SkillInstanceId != f.SkillInstanceId || won.GameplayHash != f.GameplayHash)
            return [];
        return Array.AsReadOnly(_players.Where(p => p.IsAlive && p.Seat != owner && p.Seat != won.OpponentSeat).Select(p => p.Seat).ToArray());
    }
    private SkillProgramStepOutcome SelectMouZhuRongLierenTarget(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != MouZhuRongComposition.Lieren || f.TriggerId != "won-slash-damage" || f.InstructionIndex != 2 ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseCompleted } c || MouZhuRongWin(f.OwnerSeat, c) is not { } won ||
            won.Source.SkillInstanceId != f.SkillInstanceId || won.GameplayHash != f.GameplayHash ||
            f.SelectedTargetSeats is not [var target] || target == f.OwnerSeat || target == won.OpponentSeat || !_players[target].IsAlive ||
            ProgramEventHistory<MouZhuRongLierenDamageChosenEvent>().Any(e => e.UseId == won.UseId && e.OwnerSeat == f.OwnerSeat))
            throw new InvalidOperationException("Lieren requires one native-selected other target for its original won Slash.");
        AdvanceEventRulesAndQueueFact(new MouZhuRongLierenDamageChosenEvent(f.Id, won.UseId, won.ActionId, f.OwnerSeat, target));
        return SkillProgramStepOutcome.Continue;
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
        MoveCard(card, CardLocation.OutsideGame, CardLocation.Hand(recipient), new("skill-program.mou-mobile:juxiang.outside-gift"), disclosure: CardMovementDisclosure.FaceDown);
        return AwaitProgramBoundCardMovements(f.Id, f.OwnerSeat);
    }

    private void CollectIssuedMouZhuRongLieren(CardActionContext action, SkillProgramTriggerWindow window, List<ProgramCardTriggerCandidate> result)
    {
        if (window != SkillProgramTriggerWindow.CardUseCompleted || action.Type != CardActionType.Use || !IsSlashCard(action.EffectiveKind)) return;
        var trigger = _contentRegistry.Skills.GetValueOrDefault(MouZhuRongComposition.Lieren)?.Program?.Triggers.SingleOrDefault(t => t.Id == "won-slash-damage");
        if (trigger is null) return;
        foreach (var won in ProgramEventHistory<MouZhuRongLierenWonEvent>().Where(e => e.ActionId == action.ActionId &&
            _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(use => use.Action?.ActionId == action.ActionId)?.Id == e.UseId &&
            _players[e.Source.OwnerSeat].IsAlive &&
            !ProgramEventHistory<MouZhuRongLierenDamageChosenEvent>().Any(d => d.UseId == e.UseId && d.OwnerSeat == e.Source.OwnerSeat)))
        {
            if (!_players.Any(p => p.IsAlive && p.Seat != won.Source.OwnerSeat && p.Seat != won.OpponentSeat)) continue;
            var context = CreateCardActionProgramContext(action, window, 0, won.Source.OwnerSeat, won.OpponentSeat,
                CaptureProgramTriggerFacts(_players[won.Source.OwnerSeat], action));
            result.Add(new(won.Source.OwnerSeat, won.OpponentSeat, MouZhuRongComposition.Lieren, trigger.Id,
                won.GameplayHash, won.Source.SkillInstanceId, trigger.Priority, context));
        }
    }
    private bool IsIssuedMouZhuRongLieren(ProgramTriggerCandidate candidate, ProgramSkillWindowContext c)
    {
        if (candidate.SkillId != MouZhuRongComposition.Lieren || candidate.BindingId != "won-slash-damage" ||
            c.Window != SkillProgramTriggerWindow.CardUseCompleted || !_players[candidate.OwnerSeat].IsAlive || _winner != Winner.None ||
            _contentRegistry.GetSkill(candidate.SkillId).Program?.GameplayHash != candidate.GameplayHash ||
            MouZhuRongWin(candidate.OwnerSeat, c) is not { } won || won.Source.SkillInstanceId != candidate.SkillInstanceId || won.GameplayHash != candidate.GameplayHash ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is not { } parent ||
            parent.ParentFrameId != won.UseId || parent.Action.ActionId != won.ActionId || GetCardActionWindow(parent) != c.Window ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            parent.Candidates[parent.CandidateIndex] is not { } pending || pending.OwnerSeat != candidate.OwnerSeat ||
            pending.SkillInstanceId != candidate.SkillInstanceId || pending.GameplayHash != candidate.GameplayHash || pending.TriggerId != candidate.BindingId)
            return false;
        return !ProgramEventHistory<MouZhuRongLierenDamageChosenEvent>().Any(e => e.UseId == won.UseId && e.OwnerSeat == candidate.OwnerSeat) &&
            _players.Any(p => p.IsAlive && p.Seat != candidate.OwnerSeat && p.Seat != won.OpponentSeat);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 30400)]
    private bool ContinueMouZhuRongPaidLieren(ProgramSkillFrame f) => f.SkillId == MouZhuRongComposition.Lieren &&
        f.WindowContext is { CardUse: { } use } c &&
        (f.TriggerId == "unique-slash-contest" && f.PindianResultBindings.Any(r => r.Name == "contest" && r.SourceSeat == f.OwnerSeat && r.OpponentSeat == c.TargetSeat) ||
         f.TriggerId == "won-slash-damage" && MouZhuRongWin(f.OwnerSeat, c) is { } won && won.Source.SkillInstanceId == f.SkillInstanceId && won.GameplayHash == f.GameplayHash);

    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 30400)]
    private bool AllowsMouZhuRongCompletedSlashDamage(ProgramSkillFrame f, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.SkillId != MouZhuRongComposition.Lieren || f.TriggerId != "won-slash-damage" || f.ActivationId != f.TriggerId ||
            f.InstructionIndex != 3 || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id ||
            amount != 1 || source is not null || sourceLess || nature is not (null or DamageNature.Normal) ||
            f.SelectedTargetSeats is not [var selected] || selected != target || !_players[target].IsAlive || target == f.OwnerSeat ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseCompleted } c || MouZhuRongWin(f.OwnerSeat, c) is not { } won ||
            won.Source.SkillInstanceId != f.SkillInstanceId || won.GameplayHash != f.GameplayHash || target == won.OpponentSeat ||
            CurrentDamageAttempt?.ResolutionId != won.UseId || LifecycleCardUse(won.UseId) is not { Action: { } action } use ||
            action.ActionId != won.ActionId || !IsSlashCard(use.CardKind) ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is not { } parent ||
            parent.ParentFrameId != won.UseId || parent.Action.ActionId != won.ActionId || GetCardActionWindow(parent) != SkillProgramTriggerWindow.CardUseCompleted)
            return false;
        return ProgramEventHistory<MouZhuRongLierenDamageChosenEvent>().Count(e => e.FrameId == f.Id && e.UseId == won.UseId &&
            e.ActionId == won.ActionId && e.OwnerSeat == f.OwnerSeat && e.TargetSeat == target) == 1 &&
            GetPausedPrivateOfferEffect(f) is { Op: SkillProgramEffectOp.Damage, Target: SkillProgramEffectTarget.SelectedTarget,
                Amount: 1, ActorReference: null, TargetReference: null, DamageNature: null or DamageNature.Normal };
    }

    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 30400)]
    private bool IsMouZhuRongGiftMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation m) =>
        e?.Op == SkillProgramEffectOp.GiftMouZhuRongOutsideAssault && f.InstructionIndex == 2 && f.SkillId == MouZhuRongComposition.Juxiang &&
        m.SubjectSeat == f.OwnerSeat && m.CoverageResultBind is null &&
        ProgramEventHistory<MouZhuRongAssaultGeneratedEvent>().SingleOrDefault(g => g.FrameId == f.Id) is { } generated &&
        generated.Source == MouZhuRongSource(f) && f.SelectedTargetSeats.SequenceEqual([generated.RecipientSeat]) &&
        _cardMovements.Count(move => move.CardId == generated.CardId && move.From == CardLocation.OutsideGame &&
            move.To == CardLocation.Hand(generated.RecipientSeat) && move.Reason.Value == "skill-program.mou-mobile:juxiang.outside-gift") == 1;
    [ProgramCapability] private sealed class MouZhuRongCapability(GameEngine engine) : IMouZhuRongHost, IMouZhuRongLierenTargets
    {
        public IReadOnlyList<int> OtherTargets(int owner) => engine.MouZhuRongLierenOtherTargets(owner);
        public void RecordLieren(ProgramSkillFrame f) => engine.RecordMouZhuRongLieren(f);
        public SkillProgramStepOutcome SelectLierenTarget(ProgramSkillFrame f) => engine.SelectMouZhuRongLierenTarget(f);
        public SkillProgramStepOutcome GiftAssault(ProgramSkillFrame f) => engine.GiftMouZhuRongOutsideAssault(f);
    }
}
