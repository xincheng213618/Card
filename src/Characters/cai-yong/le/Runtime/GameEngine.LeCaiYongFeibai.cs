using System.Globalization;
using System.Text;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string FeibaiSkill = "le:feibai";
    private const string FeibaiBinding = "after-use";
    private const string FeibaiObtainReason = "skill-program.le-cai-yong.feibai.obtain";
    private const string FeibaiDrawReason = "skill-program.le-cai-yong.feibai.draw";
    private bool HasLeCaiYongFeibaiCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveLeCaiYongFeibai);
    private static CardConversionSource FeibaiSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static int FeibaiNameLength(CardKind kind) => CardCatalog.Get(kind).DisplayName.EnumerateRunes().Count();
    private bool FeibaiDisabled(int owner, int turn) => ProgramEventHistory<FeibaiDisabledEvent>().Any(e => e.OwnerSeat == owner && e.ActualTurn == turn);
    private long? FeibaiSetupEpoch => _turnNumber == 0 && _phase == TurnPhase.NotStarted &&
        _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Where(window => window.Window == SkillProgramTriggerWindow.GameStarting &&
            window.Continuation == ProgramLifecycleContinuation.CompleteGameStarting).ToArray() is [var startup] ? startup.Id : null;
    private bool FeibaiActualUseScopeIsOpen => HasOngoingWholeActualUseTurn || FeibaiSetupEpoch is not null;
    private void CaptureFeibaiDeclaredName(long origin, CardActionContext? action, CardKind kind, int? instruction)
    {
        if (!HasLeCaiYongFeibaiCapability || !FeibaiActualUseScopeIsOpen) return;
        var fact = new FeibaiDeclaredNameEvent(_turnNumber, _currentSeat, origin, action?.ActionId, instruction, kind) { SetupEpochId = FeibaiSetupEpoch };
        if (ProgramEventHistory<FeibaiDeclaredNameEvent>().Any(e => e.ActualTurn == fact.ActualTurn && e.TurnOwnerSeat == fact.TurnOwnerSeat &&
            e.OriginFrameId == origin && e.ActionId == action?.ActionId && e.ProgramInstructionIndex == instruction))
            throw new InvalidOperationException("Feibai cannot capture a declaration name twice.");
        AdvanceEventRulesAndQueueFact(fact);
    }
    private FeibaiDeclaredNameEvent FeibaiRecordedName(CurrentTurnCardUseKindsRecordedEvent fact) =>
        ProgramEventHistory<FeibaiDeclaredNameEvent>().Where(e => e.ActualTurn == fact.TurnNumber && e.TurnOwnerSeat == fact.TurnOwnerSeat &&
            e.OriginFrameId == fact.OriginFrameId && e.ActionId == fact.CardActionId && e.ProgramInstructionIndex == fact.ProgramInstructionIndex &&
            e.SetupEpochId == FeibaiSetupEpoch).ToArray()
            is [var name] ? name : throw new InvalidOperationException("Feibai needs the exact dependency-captured native declaration name.");
    private CardKind FeibaiRecordedKind(CurrentTurnCardUseKindsRecordedEvent fact) => FeibaiRecordedName(fact).EffectiveKind;
    private CardKind FeibaiSourceBoundEffectiveUseKind(CurrentTurnCardUseKindsRecordedEvent fact,
        IReadOnlyList<IGameEvent> history, int prefixEnd, CurrentTurnCardUseKindsRecordedEvent current)
    {
        var original = FeibaiRecordedKind(fact);
        // A defensive response shares the incoming Use's origin frame. Its own
        // name must never inherit that Slash's later Lihuo conversion.
        if (fact.RoundActionType != CardActionType.Use || !IsSlashCard(original)) return original;
        var changes = history.Select((payload, index) => (payload, index)).Where(item =>
            item.payload is ProgramCurrentSlashFireChangedEvent change && change.CardUseFrameId == fact.OriginFrameId &&
            (item.index < prefixEnd || fact == current)).ToArray();
        if (changes.Length == 0) return original;
        var declarations = history.Select((payload, index) => (payload, index)).Where(item => Equals(item.payload, fact)).ToArray();
        if (changes is not [var entry] || entry.payload is not ProgramCurrentSlashFireChangedEvent changed ||
            declarations is not [var declaration] || entry.index <= declaration.index || fact.CardActionId != changed.ActionId ||
            changed.OriginalKind != original || changed.FinalKind != CardKind.FireSlash ||
            history.OfType<CardUseDeclaredEvent>().Count(e => e.ResolutionId == fact.OriginFrameId &&
                e.SourceSeat == fact.ActorSeat && e.CardKind == original) != 1)
            throw new InvalidOperationException("Feibai's changed name requires the exact original declaration and one native Slash conversion.");
        var starts = history.Select((payload, index) => (payload, index)).Where(item =>
            item.payload is ProgramBindingStartedEvent started && started.FrameId == changed.ProgramFrameId).ToArray();
        if (starts is not [var start] || start.index <= declaration.index || start.index >= entry.index ||
            start.payload is not ProgramBindingStartedEvent binding || binding.SkillId != changed.Source.SkillId ||
            binding.BindingId != changed.Source.BindingId || binding.SkillInstanceId != changed.Source.SkillInstanceId ||
            binding.OwnerSeat != changed.Source.OwnerSeat || binding.Window != SkillProgramTriggerWindow.CardUseCommitted ||
            _contentRegistry.Skills.GetValueOrDefault(binding.SkillId)?.Program is not { } program ||
            program.Triggers.SingleOrDefault(trigger => trigger.Id == binding.BindingId) is not { } trigger ||
            trigger.Window != binding.Window || trigger.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor ||
            trigger.CardKinds.Count != 0 && !trigger.CardKinds.Contains(original) ||
            !ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.OfferCurrentSlashFireAndExtraTarget))
            throw new InvalidOperationException("Feibai's changed name lost its source-bound native committed binding.");
        var actor = fact.ActorSeat;
        foreach (var replacement in history.Take(entry.index).OfType<ProgramCardUseActorReplacedEvent>().Where(e => e.CardUseFrameId == fact.OriginFrameId))
        {
            if (replacement.PreviousActorSeat != actor || replacement.OwnerSeat != actor ||
                replacement.ProviderSeat != fact.RoundProviderSeat || !IsValidPlayerSeat(replacement.ActorSeat))
                throw new InvalidOperationException("Feibai's native kind change lost its actual actor chain.");
            actor = replacement.ActorSeat;
        }
        if (actor != changed.Source.OwnerSeat) throw new InvalidOperationException("Feibai's native kind change belongs to another user.");
        if (_resolutionStack.SingleOrDefault(frame => frame.Id == changed.ProgramFrameId) is ProgramSkillFrame producer)
        {
            var plan = ProgramInstructionResolver.Default.Resolve(producer, program);
            if (producer.SkillId != binding.SkillId || producer.TriggerId != binding.BindingId ||
                producer.SkillInstanceId != binding.SkillInstanceId || producer.OwnerSeat != binding.OwnerSeat ||
                producer.GameplayHash != program.GameplayHash || producer.WindowContext is not
                    { Window: SkillProgramTriggerWindow.CardUseCommitted, CardUse: { } context } ||
                context.ParentCardUseFrameId != fact.OriginFrameId || context.CardActionId != changed.ActionId ||
                producer.InstructionIndex < 1 || producer.InstructionIndex > plan.Instructions.Count ||
                !plan.Instructions.Take(producer.InstructionIndex).Any(effect => effect.Op == SkillProgramEffectOp.OfferCurrentSlashFireAndExtraTarget))
                throw new InvalidOperationException("Feibai's live native conversion lost its exact paused producer.");
        }
        else if (history.Skip(entry.index + 1).OfType<ProgramBindingResolvedEvent>().Count(e => e.FrameId == binding.FrameId &&
            e.SkillId == binding.SkillId && e.BindingId == binding.BindingId && e.SkillInstanceId == binding.SkillInstanceId &&
            e.OwnerSeat == binding.OwnerSeat && e.Window == binding.Window && e.Activated) != 1)
            throw new InvalidOperationException("Feibai's historical native conversion lost its original producer return.");
        if (LifecycleCardUse(fact.OriginFrameId) is { } use)
        {
            if (use.CurrentSlashFirePolicy is not { } policy || policy.ProgramFrameId != changed.ProgramFrameId || policy.Source != changed.Source ||
                policy.OriginalKind != original || policy.OriginalAction.ActionId != changed.ActionId || policy.ExtraTargetSeat != changed.ExtraTargetSeat)
                throw new InvalidOperationException("Feibai's native conversion lost its owning card-use policy.");
            AssertCurrentSlashFirePolicy(use);
        }
        return changed.FinalKind;
    }
    private int FeibaiDeclarationSequence(CurrentTurnCardUseKindsRecordedEvent fact)
    {
        if (fact.WholeActualTurnUseSequence is > 0) return fact.WholeActualTurnUseSequence.Value;
        // Setup has real declarations but intentionally has no gameplay ordinal.
        // Its exact native GameStarting mother bounds this one setup-only prefix.
        var name = FeibaiRecordedName(fact);
        if (fact.TurnNumber != 0 || FeibaiSetupEpoch is not { } epoch || name.SetupEpochId != epoch)
            throw new InvalidOperationException("Feibai cannot invent a whole-turn ordinal outside an actual turn or its original setup mother.");
        var names = ProgramEventHistory<FeibaiDeclaredNameEvent>().Where(e => e.ActualTurn == 0 && e.TurnOwnerSeat == fact.TurnOwnerSeat && e.SetupEpochId == epoch).ToArray();
        return Array.IndexOf(names, name) + 1;
    }
    private CurrentTurnCardUseKindsRecordedEvent? FeibaiDeclarationForAction(CardActionContext action, CurrentTurnCardUseKindsRecordedEvent[] facts)
    {
        var matching = facts.Where(e => e.CardActionId == action.ActionId).ToArray();
        if (matching is [var native]) return native;
        if (matching.Length != 0) throw new InvalidOperationException("Feibai found duplicate actual declaration identities.");
        var use = _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(frame => frame.Action?.ActionId == action.ActionId);
        if (action.Type != CardActionType.Use || use is null || !(IsExactLegacyActualUseCompletion(use) ||
            IsNormalizedLegacyCompletedCategoryUse(use, action.ActorSeat, action.EffectiveKind, action.TargetSeats))) return null;
        return facts.Where(e => e.CardActionId is null && e.OriginFrameId == use.Id && !e.IsNullificationUse).ToArray() is [var legacy] ? legacy : null;
    }
    private bool IsFeibaiTrueUse(CardActionContext action)
    {
        if (action.Type == CardActionType.Use) return true;
        if (action.EffectiveKind == CardKind.Nullification) return IsCompletedResponseUse(action, ProgramCardContinuation.NullificationResponse);
        if (!IsCompletedResponseUse(action, ProgramCardContinuation.Dodge)) return false;
        return _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(use => use.Action?.ActionId == action.ParentActionId)
            is { CardAttack: { EffectiveCardKind: { } kind } attack } && IsSlashCard(kind) && attack.TargetSeat == action.ActorSeat;
    }

    // The declaration ledger, rather than completion order, supplies the prefix.
    // Later nested declarations are deliberately excluded from this immutable slice.
    private FeibaiUsePrefixCapturedEvent? FeibaiPrefix(CardActionContext action)
    {
        if (!FeibaiActualUseScopeIsOpen || !IsFeibaiTrueUse(action)) return null;
        var existing = ProgramEventHistory<FeibaiUsePrefixCapturedEvent>().SingleOrDefault(e => e.ActionId == action.ActionId);
        if (existing is not null) return existing.ActorSeat == action.ActorSeat && existing.EffectiveKind == action.EffectiveKind && existing.ActualTurn == _turnNumber && existing.SetupEpochId == FeibaiSetupEpoch
            ? existing : throw new InvalidOperationException("Feibai cannot change an already frozen actual-use prefix.");
        var facts = WholeActualTurnUseHistory();
        if (FeibaiDeclarationForAction(action, facts) is not { } current || FeibaiDeclarationSequence(current) < 1 ||
            current.RoundActionType != action.Type || current.RoundProviderSeat != action.ProviderSeat)
            throw new InvalidOperationException("Feibai needs its exact native declaration and effective-name dependency.");
        var history = CompleteProgramEventHistory().ToArray();
        var declarationPositions = history.Select((payload, index) => (payload, index))
            .Where(item => Equals(item.payload, current)).Select(item => item.index).ToArray();
        if (declarationPositions is not [var declarationPosition] ||
            FeibaiSourceBoundEffectiveUseKind(current, history, declarationPosition + 1, current) != action.EffectiveKind)
            throw new InvalidOperationException("Feibai needs its exact native declaration and effective-name dependency.");
        var prefixEnd = declarationPosition + 1;
        var replacements = EventsSinceLastBoundary(e => e is TurnStartedEvent or ActualTurnCursorReturnedEvent)
            .OfType<ProgramCardUseActorReplacedEvent>().GroupBy(e => e.CardUseFrameId).ToDictionary(g => g.Key, g => g.ToArray());
        CurrentTurnCardUseKindsRecordedEvent? previous = null;
        var seen = new HashSet<(long?, long?, int?)>();
        var sequence = FeibaiDeclarationSequence(current);
        for (var i = 0; i < sequence; i++)
        {
            if (i >= facts.Length || FeibaiDeclarationSequence(facts[i]) != i + 1 ||
                facts[i].RoundActionType is not (CardActionType.Use or CardActionType.Response) ||
                facts[i].RoundProviderSeat is not { } provider || !IsValidPlayerSeat(provider))
                throw new InvalidOperationException("Feibai's declaration prefix cannot skip an actual native use or its name.");
            var fact = facts[i];
            _ = FeibaiSourceBoundEffectiveUseKind(fact, history, prefixEnd, current);
            if (!seen.Add(fact.CardActionId is { } id ? (id, null, null) : (null, fact.OriginFrameId, fact.ProgramInstructionIndex)))
                throw new InvalidOperationException("Feibai cannot count one declaration twice.");
            var actor = fact.ActorSeat;
            if (fact.RoundActionType == CardActionType.Use && replacements.TryGetValue(fact.OriginFrameId, out var changes))
                foreach (var change in changes)
                {
                    if (change.PreviousActorSeat != actor || change.OwnerSeat != actor || change.ProviderSeat != provider || !IsValidPlayerSeat(change.ActorSeat))
                        throw new InvalidOperationException("Feibai lost the native actor-replacement chain.");
                    actor = change.ActorSeat;
                }
            if (i == sequence - 1)
            {
                if (fact != current || actor != action.ActorSeat) throw new InvalidOperationException("Feibai's completed actor differs from its final native user.");
                break;
            }
            if (actor == action.ActorSeat) previous = fact;
        }
        var length = FeibaiNameLength(action.EffectiveKind);
        var previousKind = previous is null ? (CardKind?)null : FeibaiSourceBoundEffectiveUseKind(previous, history, prefixEnd, current);
        var priorLength = previousKind is { } prior ? FeibaiNameLength(prior) : 0;
        var prefix = new FeibaiUsePrefixCapturedEvent(action.ActionId, action.ActorSeat, _turnNumber, sequence,
            action.EffectiveKind, previous?.CardActionId, previousKind, length, priorLength, checked(length + priorLength)) { SetupEpochId = FeibaiSetupEpoch };
        AdvanceEventRulesAndQueueFact(prefix);
        return prefix;
    }
    private void CaptureLeCaiYongFeibaiQualification(CardActionContext action, SkillProgramTriggerWindow window)
    {
        if (!HasLeCaiYongFeibaiCapability || window != SkillProgramTriggerWindow.CardUseCompleted || !IsFeibaiTrueUse(action) ||
            FeibaiPrefix(action) is not { } prefix || !_players[action.ActorSeat].IsAlive || FeibaiDisabled(action.ActorSeat, prefix.ActualTurn)) return;
        foreach (var b in GetSkillBindingShard(_players[action.ActorSeat]).GetInstanceTriggers(window).Where(b =>
            b.SkillId == FeibaiSkill && b.Trigger.Id == FeibaiBinding && LeCaiYongComposition.IsFeibaiTrigger(b.Trigger)))
        {
            var q = new FeibaiQualification(prefix, new(b.SkillId, b.Trigger.Id, action.ActorSeat, b.SkillInstanceId), b.Program.GameplayHash);
            if (!ProgramEventHistory<FeibaiQualifiedEvent>().Any(e => e.Qualification == q)) AdvanceEventRulesAndQueueFact(new FeibaiQualifiedEvent(q));
        }
    }
    private FeibaiQualification? FeibaiQualificationFor(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        candidate.SkillId != FeibaiSkill || candidate.BindingId != FeibaiBinding || context.Window != SkillProgramTriggerWindow.CardUseCompleted || context.CardUse is not { } use ? null :
        ProgramEventHistory<FeibaiQualifiedEvent>().Select(e => e.Qualification).SingleOrDefault(q => q.Prefix.ActionId == use.CardActionId &&
            q.Source == new CardConversionSource(candidate.SkillId, candidate.BindingId, candidate.OwnerSeat, candidate.SkillInstanceId) && q.GameplayHash == candidate.GameplayHash);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 29071, SkillProgramEffectOp.ResolveLeCaiYongFeibai)]
    private bool CanOfferLeCaiYongFeibai(ProgramTriggerLegalityContext gate) => gate.Owner.IsAlive &&
        FeibaiQualificationFor(gate.Candidate, gate.Context) is { } q && q.Prefix.ActualTurn == _turnNumber && !FeibaiDisabled(gate.Owner.Seat, _turnNumber) &&
        !ProgramEventHistory<FeibaiStartedEvent>().Any(e => e.Source == q.Source && e.ActionId == q.Prefix.ActionId);
    private bool FeibaiWindowMatches(ProgramCardTriggerWindowFrame window, FeibaiQualification q) =>
        GetCardActionWindow(window) == SkillProgramTriggerWindow.CardUseCompleted && window.Action.ActionId == q.Prefix.ActionId &&
        window.Action.ActorSeat == q.Source.OwnerSeat && window.Action.EffectiveKind == q.Prefix.EffectiveKind && IsFeibaiTrueUse(window.Action) &&
        (window.Action.Type != CardActionType.Response || window.CompletedResponseReturn is { IsCommitted: false } && IsCompletedResponseParentConsistent(window));
    private SkillProgramStepOutcome BeginLeCaiYongFeibai(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.LeCaiYongFeibai is not null || f.SkillId != FeibaiSkill || f.TriggerId != FeibaiBinding || f.ActivationId != f.TriggerId || f.InstructionIndex != 1 ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.WindowContext is not { } context ||
            FeibaiQualificationFor(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex), context) is not { } q ||
            FeibaiDisabled(f.OwnerSeat, q.Prefix.ActualTurn) ||
            _resolutionStack.SingleOrDefault(item => item.Id == context.ParentFrameId) is not ProgramCardTriggerWindowFrame window || !FeibaiWindowMatches(window, q) ||
            !DesignatedExtraTargetCandidateMatches(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex), context, window) ||
            ProgramEventHistory<FeibaiStartedEvent>().Any(e => e.Source == q.Source && e.ActionId == q.Prefix.ActionId))
            throw new InvalidOperationException("Feibai requires its exact completed true Use and issued source-bound declaration prefix.");
        var pool = _cardZones.CardsAt(CardLocation.DrawPile).Where(card => FeibaiNameLength(card.Kind) == q.Prefix.X).Select(card => card.Id).ToList();
        var randomBefore = _random.State; _random.Shuffle(pool);
        var r = new FeibaiReceipt(q, window.Id, window.ParentFrameId, FeibaiStage.Selecting, randomBefore, _random.State)
        { Sample = pool.Take(2).ToArray(), Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { LeCaiYongFeibai = r });
        AdvanceEventRulesAndQueueFact(new FeibaiStartedEvent(f.Id, q.Prefix.ActionId, q.Source, q.GameplayHash, window.Id, window.ParentFrameId, randomBefore, _random.State, r.Sample.Count));
        for (var i = 0; i < r.Sample.Count; i++) AdvanceEventRulesAndQueueFact(new FeibaiSampleEntityEvent(f.Id, i, r.Sample[i]));
        if (r.Sample.Count == 0) { BeginFeibaiFallback(f); return SkillProgramStepOutcome.AwaitChild; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, $"飞白：获得一张字数为{q.Prefix.X}的牌。", r.Sample, [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description), Choices = FeibaiSampleChoices(f) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanCardSelection : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> FeibaiSampleChoices(ProgramSkillFrame f) => f.LeCaiYongFeibai!.Sample.Select(id =>
        new PromptChoice(new($"le-cai-yong.feibai.{f.Id}.{id}"), $"获得【{GetAdvancedCard(id).DisplayName}】。", [id], [],
            new Dictionary<string, string> { ["program-action"] = "le-cai-yong-feibai-sample", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) })).ToArray();
    [ProgramAnswer("le-cai-yong-feibai-sample")]
    private void AnswerFeibaiSample(PromptChoice choice)
    {
        var f = GetActiveProgramFrame(_resolutionStack[^1].Id); AssertLeCaiYongFeibai(f);
        if (f.LeCaiYongFeibai is not { Stage: FeibaiStage.Selecting } r ||
            !AssistedChoicesEqual([choice], FeibaiSampleChoices(f).Where(item => item.Id == choice.Id).ToArray()) || choice.Cards is not [var id] || choice.Targets.Count != 0 ||
            _cardZones.GetLocation(id) != CardLocation.DrawPile) throw new InvalidOperationException("Feibai must choose one of its real private deck entities.");
        ClearPendingDecision();
        if (!_players[f.OwnerSeat].IsAlive) { BeginFeibaiFallback(f); return; }
        ReplaceRuntimeTop(f with { LeCaiYongFeibai = r with { Stage = FeibaiStage.ObtainChildren, Before = _movementSequence, After = _movementSequence },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveCard(GetAdvancedCard(id), CardLocation.DrawPile, CardLocation.Hand(f.OwnerSeat), new(FeibaiObtainReason), movement =>
        {
            var current = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(current with { LeCaiYongFeibai = current.LeCaiYongFeibai! with { ObtainedCardId = id, After = movement.Sequence } });
            AdvanceEventRulesAndQueueFact(new FeibaiObtainedEvent(f.Id, id, movement.Sequence));
        });
        ContinueLeCaiYongFeibai(f.Id);
    }
    private void BeginFeibaiFallback(ProgramSkillFrame f)
    {
        var r = f.LeCaiYongFeibai ?? throw new InvalidOperationException("Feibai fallback has no original sample.");
        if (r.ObtainedCardId is not null || ProgramEventHistory<FeibaiFallbackDrawIssuedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Feibai cannot pay fallback after an actual obtain or issue it twice.");
        // Commit before invoking native Draw2: replacement and gain observers can re-enter uses.
        AdvanceEventRulesAndQueueFact(new FeibaiDisabledEvent(f.OwnerSeat, r.Qualification.Prefix.ActualTurn, f.Id));
        ReplaceRuntimeTop(f = f with { LeCaiYongFeibai = r with { Stage = FeibaiStage.Drawing }, PendingMovementContinuation = null });
        AdvanceEventRulesAndQueueFact(new FeibaiFallbackDrawIssuedEvent(f.Id, f.OwnerSeat, r.Qualification.Prefix.ActualTurn));
        BeginOperationProgramDraw(f, f.OwnerSeat, 2, new(FeibaiDrawReason));
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveLeCaiYongFeibai)]
    private void ReturnLeCaiYongFeibaiDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.LeCaiYongFeibai is not { Stage: FeibaiStage.Drawing, ObtainedCardId: null } r || outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != 2 ||
            ProgramEventHistory<FeibaiFallbackDrawReturnedEvent>().Any(e => e.FrameId == f.Id)) throw new InvalidOperationException("Feibai cannot substitute or replay its native fallback draw.");
        ReplaceRuntimeTop(f = f with { LeCaiYongFeibai = r with { Stage = FeibaiStage.DrawChildren, DrawOutcome = outcome }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new FeibaiFallbackDrawReturnedEvent(f.Id, outcome.AttemptFrameId, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.SequenceBefore, outcome.SequenceAfter));
        MarkFeibaiActualDrawEntities(GetActiveProgramFrame(f.Id), outcome);
        ContinueLeCaiYongFeibai(f.Id);
    }
    private bool IsExactFeibaiFallbackDraw(ProgramSkillFrame f, NativeDrawOutcome outcome) =>
        f.LeCaiYongFeibai is { Stage: FeibaiStage.DrawChildren, ObtainedCardId: null, DrawOutcome: { } stored } r && f.NativeDraw is null &&
        stored.RecipientSeat == outcome.RecipientSeat && stored.RequestedCount == 2 && stored.AttemptFrameId == outcome.AttemptFrameId &&
        stored.SequenceBefore == outcome.SequenceBefore && stored.SequenceAfter == outcome.SequenceAfter && stored.ActualDrawIds.SequenceEqual(outcome.ActualDrawIds) &&
        stored.ReplacementGainedIds.SequenceEqual(outcome.ReplacementGainedIds) && outcome.RecipientSeat == f.OwnerSeat && outcome.RequestedCount == 2 &&
        FeibaiRootMatches(f) && ProgramEventHistory<FeibaiDisabledEvent>().Count(e => e == new FeibaiDisabledEvent(f.OwnerSeat, r.Qualification.Prefix.ActualTurn, f.Id)) == 1 &&
        ProgramEventHistory<FeibaiFallbackDrawIssuedEvent>().Count(e => e == new FeibaiFallbackDrawIssuedEvent(f.Id, f.OwnerSeat, r.Qualification.Prefix.ActualTurn)) == 1 &&
        ProgramEventHistory<FeibaiFallbackDrawReturnedEvent>().Count(e => e == new FeibaiFallbackDrawReturnedEvent(f.Id, outcome.AttemptFrameId,
            outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.SequenceBefore, outcome.SequenceAfter)) == 1 &&
        outcome.ActualDrawIds.All(id => _cardMovements.Count(m => m.Sequence > outcome.SequenceBefore && m.Sequence <= outcome.SequenceAfter && m.CardId == id &&
            m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == FeibaiDrawReason) == 1);
    private void ContinueLeCaiYongFeibai(long id)
    {
        var f = GetActiveProgramFrame(id); AssertLeCaiYongFeibai(f); var r = f.LeCaiYongFeibai!;
        if (r.Stage is FeibaiStage.Selecting or FeibaiStage.Drawing) return;
        if (AwaitFeibaiChildren(id)) return;
        f = GetActiveProgramFrame(id); r = f.LeCaiYongFeibai!;
        if (r.Stage == FeibaiStage.ObtainChildren && r.ObtainedCardId is null) { BeginFeibaiFallback(f); return; }
        AdvanceEventRulesAndQueueFact(new FeibaiCompletedEvent(f.Id, r.Qualification.Prefix.ActionId, r.ObtainedCardId, r.Stage == FeibaiStage.DrawChildren));
        FinishProgramSkill(f, true);
    }
    private bool AwaitFeibaiChildren(long id)
    {
        var f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id)) return true;
        f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f with { PendingMovementContinuation = null }); return false;
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 29071)]
    private bool ResumeLeCaiYongFeibai(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.LeCaiYongFeibai is null) return false;
        ContinueLeCaiYongFeibai(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 29071)]
    private bool ReturnLeCaiYongFeibaiMovement(ProgramSkillFrame f)
    { if (f.LeCaiYongFeibai is null || f.PendingMovementContinuation is null) return false; ContinueLeCaiYongFeibai(f.Id); return true; }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 300)]
    private bool IsFeibaiMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        f.LeCaiYongFeibai is { Stage: FeibaiStage.ObtainChildren or FeibaiStage.DrawChildren } && effect?.Op == SkillProgramEffectOp.ResolveLeCaiYongFeibai &&
        pending == f.PendingMovementContinuation && pending.SubjectSeat == f.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null;
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 29071)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 29071)]
    private bool PermitPaidLeCaiYongFeibai(ProgramSkillFrame f)
    { if (f.LeCaiYongFeibai is null) return false; AssertLeCaiYongFeibai(f); return true; }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveLeCaiYongFeibai)]
    private PromptChoice SelectAiFeibaiSample(PendingDecision decision, ProgramSkillFrame f) => decision.Choices
        .OrderByDescending(c => GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[f.OwnerSeat])).ThenBy(c => c.Cards.Single()).First();
    private CardSnapshot[] FeibaiPrivateCards(int viewer) => _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.OwnerSeat == viewer &&
        f.LeCaiYongFeibai is { Stage: FeibaiStage.Selecting }).SelectMany(f => f.LeCaiYongFeibai!.Sample.Select(id => ToSnapshot(GetAdvancedCard(id)))).ToArray();

    private bool FeibaiRootMatches(ProgramSkillFrame f)
    {
        if (f.LeCaiYongFeibai is not { } r || f.SkillId != FeibaiSkill || f.TriggerId != FeibaiBinding || f.ActivationId != f.TriggerId || f.InstructionIndex != 1 ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.WindowContext is not { } context ||
            _contentRegistry.GetSkill(f.SkillId).Program is not { } program || program.GameplayHash != f.GameplayHash || !LeCaiYongComposition.IsFeibaiProgram(program) ||
            r.Qualification.Source != FeibaiSource(f) || r.Qualification.GameplayHash != f.GameplayHash || context.ParentFrameId != r.WindowId ||
            _resolutionStack.SingleOrDefault(item => item.Id == r.WindowId) is not ProgramCardTriggerWindowFrame window || window.ParentFrameId != r.NativeParentId ||
            !FeibaiWindowMatches(window, r.Qualification) || !DesignatedExtraTargetCandidateMatches(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId,
                f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex), context, window)) return false;
        var index = _resolutionStack.FindIndex(item => item.Id == f.Id);
        return index > 0 && _resolutionStack[index - 1].Id == window.Id && r.Sample.Count <= 2 && r.Sample.Distinct().Count() == r.Sample.Count &&
            r.Sample.All(id => id > 0 && FeibaiNameLength(GetAdvancedCard(id).Kind) == r.Qualification.Prefix.X) &&
            ProgramEventHistory<FeibaiUsePrefixCapturedEvent>().Count(e => e == r.Qualification.Prefix) == 1 &&
            ProgramEventHistory<FeibaiQualifiedEvent>().Count(e => e.Qualification == r.Qualification) == 1 &&
            ProgramEventHistory<FeibaiStartedEvent>().Count(e => e == new FeibaiStartedEvent(f.Id, r.Qualification.Prefix.ActionId, r.Qualification.Source,
                f.GameplayHash, window.Id, window.ParentFrameId, r.RandomBefore, r.RandomAfter, r.Sample.Count)) == 1 &&
            ProgramEventHistory<FeibaiSampleEntityEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).Select(e => e.CardId).SequenceEqual(r.Sample);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 29071)]
    private void AssertLeCaiYongFeibai(ProgramSkillFrame f)
    {
        if (f.LeCaiYongFeibai is not { } r) return;
        if (!FeibaiRootMatches(f) || !Enum.IsDefined(r.Stage) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence)
            throw new InvalidOperationException("Feibai lost its original native completed mother, private sample or movement interval.");
        if (r.Stage == FeibaiStage.Selecting)
        {
            if (r.ObtainedCardId is not null || r.DrawOutcome is not null || r.Sample.Any(id => _cardZones.GetLocation(id) != CardLocation.DrawPile) ||
                r.Sample.Count > 0 && _resolutionStack.LastOrDefault()?.Id == f.Id && (_pendingDecision is not { IsPrivate: true, Kind: DecisionKind.ProgramTrigger } p ||
                    p.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual(p.Choices, FeibaiSampleChoices(f))))
                throw new InvalidOperationException("Feibai's real sample escaped its original private chooser.");
        }
        else if (r.Stage == FeibaiStage.ObtainChildren && (r.ObtainedCardId is not { } id || !r.Sample.Contains(id) ||
            ProgramEventHistory<FeibaiObtainedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var paid] || paid.CardId != id || paid.Sequence != r.After ||
            !_cardMovements.Any(m => m.Sequence == paid.Sequence && FeibaiOperationMovement(f, m))))
            throw new InvalidOperationException("Feibai cannot invent or repeat its actual deck-to-Hand obtain.");
        if (r.Stage == FeibaiStage.DrawChildren && (r.DrawOutcome is not { } outcome || !IsExactFeibaiFallbackDraw(f, outcome)))
            throw new InvalidOperationException("Feibai lost its once-paid actual/replacement native Draw2 return.");
        if (r.Stage == FeibaiStage.Drawing && (r.DrawOutcome is not null || r.ObtainedCardId is not null ||
            ProgramEventHistory<FeibaiFallbackDrawIssuedEvent>().Count(e => e.FrameId == f.Id) != 1 || !FeibaiDisabled(f.OwnerSeat, r.Qualification.Prefix.ActualTurn)))
            throw new InvalidOperationException("Feibai's fallback must disable the skill before the native draw begins.");
    }
    private bool FeibaiOperationMovement(ProgramSkillFrame f, CardMovementRecord m)
    {
        if (!_cardMovements.Contains(m)) return false;
        if (f.LeCaiYongFeibai is { Stage: FeibaiStage.ObtainChildren, ObtainedCardId: { } id } r)
            return m.Sequence > r.Before && m.Sequence <= r.After && m.CardId == id && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == FeibaiObtainReason;
        if (f.LeCaiYongFeibai is not { Stage: FeibaiStage.DrawChildren, DrawOutcome: { } o } || m.Sequence <= o.SequenceBefore || m.Sequence > o.SequenceAfter) return false;
        return o.ActualDrawIds.Contains(m.CardId) && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == FeibaiDrawReason ||
            o.ReplacementGainedIds.Contains(m.CardId) && m.To == CardLocation.Hand(f.OwnerSeat) || m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle;
    }
    private bool FeibaiFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!FeibaiRootMatches(f)) return false;
        if (f.LeCaiYongFeibai is { Stage: FeibaiStage.Drawing } && f.NativeDraw is not null) return NativeDrawAttemptRidesOn(child, f);
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state) return state.ResumeProgramFrameId == f.Id && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
            ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id && e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window);
        if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        return child is CardsMovedTriggerWindowFrame moved && ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id &&
            moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
            moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => FeibaiOperationMovement(f, m));
    }
    private bool FeibaiObserverSuffix(int index) => HasObserverSuffix(index, static (g, child) => g.IsMovementObserverEdge(child) || g.IsSameNameHandObserverEdge(child) ||
        g.IsYanPuDyingExitEdge(child) || g.TypedDamagePostEventEdge(g._resolutionStack[child - 1], g._resolutionStack[child]) ||
        g._resolutionStack[child - 1] is ProgramSkillFrame f && (g.FeibaiFirstChild(f, g._resolutionStack[child]) || f.Zenrun is not null && g.ZenrunFirstChild(f, g._resolutionStack[child])) ||
        NativeDrawAttemptRidesOn(g._resolutionStack[child], g._resolutionStack[child - 1]) || g.RecoveryReplacementFrameRidesOn(g._resolutionStack[child], g._resolutionStack[child - 1]),
        static (g, child, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(child, dying));
    private ProgramSkillFrame? FeibaiObserverRoot() => FindProgramObserverRoot(static (g, f, child) => g.FeibaiFirstChild(f, child), static (g, index) => g.FeibaiObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1250)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1360)]
    private bool HasFeibaiDamageObserver(long id) => FeibaiObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 1390)]
    private bool HasFeibaiBeforeDamageObserver(long id) => FeibaiObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 1220)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 780)]
    private bool IsFeibaiDying() => ActiveDying is { } dying && FeibaiObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 40)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1790)]
    private bool AllowsFeibaiNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || FeibaiObserverRoot() is not { } root || root.Id == f.Id) return false;
        var effect = GetPausedPrivateOfferEffect(f);
        return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature &&
            target == (effect.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, effect.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 550)]
    private bool IsFeibaiRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.LeCaiYongFeibai is null || recovery.ParentFrameId == f.Id) return false;
        var parent = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return parent >= 0 && child > parent + 1 && FeibaiFirstChild(f, _resolutionStack[parent + 1]) && FeibaiObserverSuffix(parent) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 29071)]
    private bool AdvanceFeibaiChildren() => _pendingDecision is null && FeibaiObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    private bool HasLeCaiYongFeibaiUseContinuation(long nativeId)
    {
        if (_resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.LeCaiYongFeibai?.NativeParentId == nativeId).ToArray() is not [var f] || !FeibaiRootMatches(f)) return false;
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id); var native = _resolutionStack.FindIndex(x => x.Id == nativeId);
        if (native < 0 || i != native + 2 || _resolutionStack[native + 1].Id != f.LeCaiYongFeibai!.WindowId) return false;
        AssertLeCaiYongFeibai(f);
        return i == _resolutionStack.Count - 1 ? f.LeCaiYongFeibai.Stage == FeibaiStage.Selecting &&
            _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p && p.PlayerSeat == f.OwnerSeat && AssistedChoicesEqual(p.Choices, FeibaiSampleChoices(f)) ||
            f.LeCaiYongFeibai.Stage is FeibaiStage.ObtainChildren or FeibaiStage.DrawChildren && _pendingDecision is null :
            FeibaiFirstChild(f, _resolutionStack[i + 1]) && FeibaiObserverSuffix(i);
    }
    private bool HasLeCaiYongFeibaiRescueContinuation(long dyingId)
    {
        if (_resolutionStack.OfType<CardUseFrame>().Where(use => use.DyingResponse?.ResolutionId == dyingId).ToArray() is not [var rescue] ||
            rescue.DyingResponse is not { } response || rescue.Action is not { Type: CardActionType.Use } action ||
            rescue.CardKind is not (CardKind.Peach or CardKind.Alcohol) || action.ActorSeat != response.ResponderSeat || rescue.SourceSeat != response.ResponderSeat ||
            _resolutionStack.SingleOrDefault(x => x.Id == response.ResolutionId) is not DyingFrame dying ||
            !rescue.TargetSeats.SequenceEqual([dying.VictimSeat]) || !HasLeCaiYongFeibaiUseContinuation(rescue.Id)) return false;
        var index = _resolutionStack.FindIndex(x => x.Id == rescue.Id);
        return index > 0 && _resolutionStack[index - 1].Id == dying.Id &&
            _resolutionStack[index + 1] is ProgramCardTriggerWindowFrame { Continuation: ProgramCardContinuation.CompletedCard } w && w.Action.ActionId == action.ActionId &&
            (rescue.CardKind == CardKind.Peach ? response.UsedPeach : response.UsedAlcohol && dying.VictimSeat == rescue.SourceSeat);
    }
    [ProgramCapability]
    private sealed class LeCaiYongFeibaiCapability(GameEngine engine) : ILeCaiYongFeibaiHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f) => engine.BeginLeCaiYongFeibai(f); }
}
