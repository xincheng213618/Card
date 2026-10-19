namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string LeCaiYongJiaoweiSkill = "le:jiaowei";
    private const string LeCaiYongJiaoweiInitialBinding = "mark-initial-entities";
    // Engineering contracts pending a more specific official ruling: an Xian
    // belongs permanently to its physical entity; repeated losses coalesce to
    // one next-damage prevention. Keep these policies explicit at the ledger.
    private bool HasLeCaiYongJiaoweiCapability => _contentRegistry.ProgramDependencies
        .HasTriggerOperation(SkillProgramEffectOp.InitializeLeCaiYongJiaowei);
    private static CardConversionSource JiaoweiSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);
    private bool HasXianEntityMark(int cardId) => HasLeCaiYongJiaoweiCapability && cardId > 0 &&
        ProgramEventHistory<XianEntityMarkedEvent>().Any(mark => mark.CardId == cardId &&
            ProgramEventHistory<XianMarkingCompletedEvent>().Any(done => done.FrameId == mark.FrameId && done.Origin == mark.Origin));
    private bool IsJiaoweiHandLimitExempt(CharacterState owner, int cardId) => owner.IsAlive &&
        HasRuntimeSkill(owner, LeCaiYongJiaoweiSkill) && _cardZones.GetLocation(cardId) == CardLocation.Hand(owner.Seat) && HasXianEntityMark(cardId);

    private ProgramLifecycleTriggerWindowFrame CaptureJiaoweiInitialEntities(ProgramLifecycleTriggerWindowFrame window)
    {
        if (!HasLeCaiYongJiaoweiCapability || window.Window != SkillProgramTriggerWindow.GameStarting ||
            window.Continuation != ProgramLifecycleContinuation.CompleteGameStarting) return window;
        if (window.JiaoweiInitialEntities is not null) throw new InvalidOperationException("Jiaowei may freeze its final opening Hand only once.");
        var owners = window.Candidates.Where(candidate => candidate.SkillId == LeCaiYongJiaoweiSkill &&
            candidate.BindingId == LeCaiYongJiaoweiInitialBinding && GetProgramTrigger(candidate).Effects is
                [{ Op: SkillProgramEffectOp.InitializeLeCaiYongJiaowei }]).Select(candidate => candidate.OwnerSeat).Distinct().Order();
        var entities = owners.SelectMany(owner => GetHand(_players[owner]).Select(card =>
        {
            var entry = _cardMovements.LastOrDefault(move => move.CardId == card.Id && move.To == CardLocation.Hand(owner));
            if (card.Id <= 0 || entry is null || entry.CardKind != card.Kind)
                throw new InvalidOperationException("Jiaowei needs the original physical opening-Hand entry.");
            return new JiaoweiInitialEntity(owner, card.Id, card.Kind, entry.Sequence);
        })).ToArray();
        return window with { JiaoweiInitialEntities = entities };
    }
    private bool IsIssuedJiaoweiInitialCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        HasLeCaiYongJiaoweiCapability && candidate.SkillId == LeCaiYongJiaoweiSkill &&
        candidate.BindingId == LeCaiYongJiaoweiInitialBinding && IsValidPlayerSeat(candidate.OwnerSeat) &&
        _contentRegistry.GetSkill(candidate.SkillId).Program?.GameplayHash == candidate.GameplayHash &&
        GetProgramTrigger(candidate).Effects is [{ Op: SkillProgramEffectOp.InitializeLeCaiYongJiaowei }] &&
        context.Window == SkillProgramTriggerWindow.GameStarting && context.OwnerSeat == candidate.OwnerSeat &&
        _phase == TurnPhase.NotStarted && _resolutionStack.SingleOrDefault(frame => frame.Id == context.ParentFrameId) is
            ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.GameStarting,
                Continuation: ProgramLifecycleContinuation.CompleteGameStarting, JiaoweiInitialEntities: not null } parent &&
        parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count && parent.Candidates[parent.CandidateIndex] == candidate &&
        context.SourceSeat == parent.OwnerSeat && context.TargetSeat == parent.OwnerSeat &&
        !ProgramEventHistory<JiaoweiInitializedEvent>().Any(done => done.Source.OwnerSeat == candidate.OwnerSeat);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 29070, SkillProgramEffectOp.InitializeLeCaiYongJiaowei)]
    private bool CanOfferJiaoweiInitial(ProgramTriggerLegalityContext gate) => IsIssuedJiaoweiInitialCandidate(gate.Candidate, gate.Context);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 29070)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 29070)]
    private bool PermitIssuedJiaoweiInitial(ProgramSkillFrame frame)
    {
        if (frame.SkillId != LeCaiYongJiaoweiSkill || frame.TriggerId != LeCaiYongJiaoweiInitialBinding || frame.ActivationId != frame.TriggerId ||
            frame.InstructionIndex is < 0 or > 1 || frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats.Count != 0 ||
            frame.WindowContext is not { } context || _contentRegistry.GetSkill(frame.SkillId).Program?.GameplayHash != frame.GameplayHash ||
            !ExactOriginalHandLifecycleParent(frame, SkillProgramTriggerWindow.GameStarting) ||
            !OriginalHandBindingStarted(frame, SkillProgramTriggerWindow.GameStarting)) return false;
        var candidate = new ProgramTriggerCandidate(frame.OwnerSeat, frame.SkillId, frame.TriggerId, frame.SkillInstanceId,
            frame.GameplayHash, GetProgramTrigger(frame).Priority, context.OccurrenceIndex);
        return IsIssuedJiaoweiInitialCandidate(candidate, context) || frame.InstructionIndex == 1 &&
            ProgramEventHistory<JiaoweiInitializedEvent>().Count(done => done.FrameId == frame.Id &&
                done.Source == JiaoweiSource(frame) && done.GameplayHash == frame.GameplayHash) == 1;
    }
    private SkillProgramStepOutcome InitializeJiaowei(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.InstructionIndex != 1 || !PermitIssuedJiaoweiInitial(frame) ||
            ProgramEventHistory<JiaoweiInitializedEvent>().Any(done => done.Source.OwnerSeat == frame.OwnerSeat) ||
            ProgramEventHistory<XianEntityMarkedEvent>().Any(mark => mark.FrameId == frame.Id))
            throw new InvalidOperationException("Jiaowei requires its once-issued native opening mother.");
        var parent = (ProgramLifecycleTriggerWindowFrame)_resolutionStack.Single(item => item.Id == frame.WindowContext!.ParentFrameId);
        var entities = parent.JiaoweiInitialEntities!.Where(entity => entity.OwnerSeat == frame.OwnerSeat).ToArray();
        if (entities.Select(entity => entity.CardId).Distinct().Count() != entities.Length || entities.Any(entity =>
            !_cardMovements.Any(move => move.Sequence == entity.EntrySequence && move.CardId == entity.CardId &&
                move.CardKind == entity.PrintedKind && move.To == CardLocation.Hand(entity.OwnerSeat))))
            throw new InvalidOperationException("Jiaowei cannot replace the frozen initial entities with a later Hand.");
        foreach (var entity in entities) AdvanceEventRulesAndQueueFact(new XianEntityMarkedEvent(frame.Id, JiaoweiSource(frame), frame.GameplayHash,
            entity.OwnerSeat, entity.CardId, entity.PrintedKind, entity.EntrySequence, XianMarkOrigin.OpeningHand));
        AdvanceEventRulesAndQueueFact(new XianMarkingCompletedEvent(frame.Id, XianMarkOrigin.OpeningHand, entities.Length));
        AdvanceEventRulesAndQueueFact(new JiaoweiInitializedEvent(frame.Id, JiaoweiSource(frame), frame.GameplayHash, parent.Id, entities.Length));
        return SkillProgramStepOutcome.Continue;
    }
    [ProgramCapability]
    private sealed class LeCaiYongJiaoweiCapability(GameEngine engine) : ILeCaiYongJiaoweiHost
    { public SkillProgramStepOutcome Initialize(ProgramSkillFrame frame) => engine.InitializeJiaowei(frame); }

    private void MarkFeibaiActualDrawEntities(ProgramSkillFrame frame, NativeDrawOutcome outcome)
    {
        if (!HasLeCaiYongJiaoweiCapability || frame.SkillId != "le:feibai" ||
            _resolutionStack.LastOrDefault() is not ProgramSkillFrame current || current.Id != frame.Id ||
            current.GameplayHash != frame.GameplayHash || _contentRegistry.GetSkill(frame.SkillId).Program is not { } program ||
            program.GameplayHash != frame.GameplayHash || !LeCaiYongComposition.IsFeibaiProgram(program) ||
            GetPausedPrivateOfferEffect(current)?.Op != SkillProgramEffectOp.ResolveLeCaiYongFeibai ||
            !IsExactFeibaiFallbackDraw(current, outcome) || outcome.RecipientSeat != frame.OwnerSeat || outcome.RequestedCount != 2 ||
            outcome.SequenceBefore < 0 || outcome.SequenceAfter < outcome.SequenceBefore || outcome.SequenceAfter > _movementSequence ||
            outcome.ActualDrawIds.Count > 2 || outcome.ActualDrawIds.Distinct().Count() != outcome.ActualDrawIds.Count ||
            ProgramEventHistory<XianMarkingCompletedEvent>().Any(done => done.FrameId == frame.Id && done.Origin == XianMarkOrigin.FeibaiFallbackDraw))
            throw new InvalidOperationException("Xian marking needs its one exact paid Feibai fallback native Draw2 return.");
        var movements = outcome.ActualDrawIds.Select(id => _cardMovements.Where(move => move.Sequence > outcome.SequenceBefore &&
            move.Sequence <= outcome.SequenceAfter && move.CardId == id && move.From == CardLocation.DrawPile &&
            move.To == CardLocation.Hand(frame.OwnerSeat)).ToArray() is [var move] ? move :
                throw new InvalidOperationException("Feibai may mark only its actual original draw materials.")).ToArray();
        var source = JiaoweiSource(frame);
        foreach (var move in movements) AdvanceEventRulesAndQueueFact(new XianEntityMarkedEvent(frame.Id, source, frame.GameplayHash,
            frame.OwnerSeat, move.CardId, move.CardKind, move.Sequence, XianMarkOrigin.FeibaiFallbackDraw));
        AdvanceEventRulesAndQueueFact(new XianMarkingCompletedEvent(frame.Id, XianMarkOrigin.FeibaiFallbackDraw, movements.Length));
    }

    // Native TurnEnded can precede an inserted Play phase. Keep the earned
    // protection until the whole actual turn cursor returns, including that phase.
    private bool JiaoweiActualTurnIsOpen(int turn) => _setupComplete && turn > 0 && turn == _turnNumber &&
        HasOngoingWholeActualUseTurn && ProgramEventHistory<TurnStartedEvent>().LastOrDefault()?.TurnNumber == turn;
    private static bool IsJiaoweiOwnedZone(CardLocation location, int owner) =>
        location.OwnerSeat == owner && location.Zone is CardZoneKind.Hand or CardZoneKind.Equipment;
    private IReadOnlyList<JiaoweiLossSource> CaptureJiaoweiLossSources(IEnumerable<CardLocation> locations)
    {
        if (!HasLeCaiYongJiaoweiCapability || !JiaoweiActualTurnIsOpen(_turnNumber)) return [];
        var owned = locations.Where(location => location.OwnerSeat is { } owner && IsValidPlayerSeat(owner) &&
            IsJiaoweiOwnedZone(location, owner)).Distinct().ToArray();
        return owned.Select(location => location.OwnerSeat!.Value).Distinct().Order().Where(owner => _players[owner].IsAlive)
            .SelectMany(owner => GetSkillBindingShard(_players[owner]).ProgramInstances.Where(instance => instance.SkillId == LeCaiYongJiaoweiSkill &&
                LeCaiYongComposition.IsJiaoweiProgram(instance.Program) &&
                instance.Program.Triggers.Any(trigger => trigger.Id == LeCaiYongJiaoweiInitialBinding && trigger.Effects is
                    [{ Op: SkillProgramEffectOp.InitializeLeCaiYongJiaowei }])).OrderBy(instance => instance.SkillInstanceId, StringComparer.Ordinal).Take(1)
                .Select(instance => new JiaoweiLossSource(new(instance.SkillId, LeCaiYongJiaoweiInitialBinding, owner, instance.SkillInstanceId),
                    instance.Program.GameplayHash) { Materials = owned.Where(location => location.OwnerSeat == owner)
                        .SelectMany(location => _cardZones.CardsAt(location).Where(card => HasXianEntityMark(card.Id))
                            .Select(card => new JiaoweiOwnedXianEntity(card.Id, card.Kind, location))).ToArray() }))
            .Where(source => source.Materials.Count > 0).ToArray();
    }
    private bool ValidJiaoweiProtectionSource(JiaoweiLossSource source) =>
        source.Source.SkillId == LeCaiYongJiaoweiSkill && source.Source.BindingId == LeCaiYongJiaoweiInitialBinding &&
        IsValidPlayerSeat(source.Source.OwnerSeat) && _contentRegistry.GetSkill(source.Source.SkillId).Program is { } program &&
        program.GameplayHash == source.GameplayHash && LeCaiYongComposition.IsJiaoweiProgram(program) &&
        source.Materials.Count > 0 && source.Materials.Select(card => card.CardId).Distinct().Count() == source.Materials.Count &&
        source.Materials.All(card => card.CardId > 0 && IsJiaoweiOwnedZone(card.From, source.Source.OwnerSeat) &&
            GetAdvancedCard(card.CardId).Kind == card.PrintedKind && HasXianEntityMark(card.CardId));
    private void CaptureJiaoweiLossQualifications(CardMovementBatchContext batch, IReadOnlyList<JiaoweiLossSource> sources)
    {
        if (!HasLeCaiYongJiaoweiCapability || !JiaoweiActualTurnIsOpen(batch.TurnNumber)) return;
        foreach (var source in sources)
        {
            if (!ValidJiaoweiProtectionSource(source)) throw new InvalidOperationException("Jiaowei lost its pre-departure marked HE/source snapshot.");
            var owner = source.Source.OwnerSeat;
            var lost = batch.Movements.Where(move => source.Materials.Any(card => card.CardId == move.CardId && card.PrintedKind == move.CardKind &&
                card.From == move.From) && !IsJiaoweiOwnedZone(move.To, owner) && _cardMovements.Contains(move)).ToArray();
            if (lost.Length == 0) continue;
            if (lost.Select(move => move.CardId).Distinct().Count() != lost.Length ||
                ProgramEventHistory<JiaoweiProtectionArmedEvent>().Any(armed => armed.OwnerSeat == owner && armed.LossBatchId == batch.Id))
                throw new InvalidOperationException("Jiaowei cannot arm the same actual marked loss batch twice.");
            var previous = ActiveJiaoweiProtection(owner);
            foreach (var move in lost) AdvanceEventRulesAndQueueFact(new JiaoweiLossQualifiedEvent(batch.Id, source.Source, source.GameplayHash,
                move.CardId, move.CardKind, move.From, move.To, move.Sequence, batch.TurnNumber));
            AdvanceEventRulesAndQueueFact(new JiaoweiProtectionArmedEvent(owner, batch.TurnNumber, batch.Id, batch.Id,
                source.Source, source.GameplayHash, lost.Length, previous?.ArmId));
        }
    }
    private JiaoweiProtectionArmedEvent? ActiveJiaoweiProtection(int owner)
    {
        if (!HasLeCaiYongJiaoweiCapability || !IsValidPlayerSeat(owner) || !JiaoweiActualTurnIsOpen(_turnNumber)) return null;
        // The latest issuance replaces every earlier unconsumed entitlement;
        // consumption must never reveal an older charge underneath it.
        var latest = ProgramEventHistory<JiaoweiProtectionArmedEvent>().LastOrDefault(armed => armed.OwnerSeat == owner && armed.ActualTurn == _turnNumber);
        return latest is not null && !ProgramEventHistory<JiaoweiDamagePreventedEvent>().Any(used =>
            used.OwnerSeat == owner && used.ActualTurn == latest.ActualTurn && used.ArmId == latest.ArmId) ? latest : null;
    }
    private bool TryConsumeJiaoweiProtection(IDamageAttempt attack, int amount, DamageNature nature)
    {
        if (amount <= 0 || !_players[attack.TargetSeat].IsAlive || ActiveJiaoweiProtection(attack.TargetSeat) is not { } armed) return false;
        var facts = ProgramEventHistory<JiaoweiLossQualifiedEvent>().Where(loss => loss.BatchId == armed.LossBatchId &&
            loss.Source == armed.Source && loss.GameplayHash == armed.GameplayHash && loss.ActualTurn == armed.ActualTurn).ToArray();
        if (!SameAttackOwner(attack, CurrentDamageAttempt) || _resolutionStack.Count(frame => frame.Id == attack.ResolutionId) != 1 ||
            !attack.DamageAmountFinalized || armed.ArmId != armed.LossBatchId || armed.OwnerSeat != attack.TargetSeat || armed.ActualLostCount <= 0 ||
            armed.Source.SkillId != LeCaiYongJiaoweiSkill || armed.Source.BindingId != LeCaiYongJiaoweiInitialBinding ||
            ProgramEventHistory<JiaoweiProtectionArmedEvent>().Count(issued => issued == armed) != 1 ||
            _contentRegistry.GetSkill(armed.Source.SkillId).Program?.GameplayHash != armed.GameplayHash ||
            facts.Length != armed.ActualLostCount || facts.Select(fact => fact.CardId).Distinct().Count() != facts.Length ||
            facts.Any(fact => !IsJiaoweiOwnedZone(fact.From, armed.OwnerSeat) || IsJiaoweiOwnedZone(fact.To, armed.OwnerSeat) ||
                !_cardMovements.Any(move => move.Sequence == fact.MovementSequence && move.CardId == fact.CardId && move.CardKind == fact.PrintedKind &&
                    move.From == fact.From && move.To == fact.To)))
            throw new InvalidOperationException("Jiaowei prevention requires the exact unexpired actual loss and current finalized native damage owner.");
        AdvanceEventRulesAndQueueFact(new JiaoweiDamagePreventedEvent(attack.ResolutionId, armed.OwnerSeat, armed.ActualTurn,
            armed.ArmId, armed.LossBatchId, attack.IsSourceLess ? null : attack.SourceSeat, amount, nature, attack.IsChainPropagation));
        AddLog("DamagePrevented", $"{_players[armed.OwnerSeat].Name} 的【焦尾】防止了本次{amount}点伤害。", armed.OwnerSeat, armed.OwnerSeat);
        return true;
    }
}
