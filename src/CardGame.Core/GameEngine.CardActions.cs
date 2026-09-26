namespace CardGame.Core;

public sealed partial class GameEngine
{
    private CommandResult SubmitRecast(RecastCardCommand command)
    {
        var validation = ValidateHumanPrompt(command.ActorSeat, DecisionKind.PlayCard,
            command.PromptId, CommandErrorCode.IllegalAction);
        if (validation is not null) return Reject(validation.Code, validation.Message);
        var actor = _players[command.ActorSeat];
        if (!GetHand(actor).Any(card => card.Id == command.CardId))
            return Reject(CommandErrorCode.InvalidCard, "The recast card is not in the actor's hand.");
        var action = BuildLegalActions(actor).SingleOrDefault(action =>
            action.Kind == LegalActionKind.Recast && action.CardId == command.CardId &&
            action.ConversionSource == command.ConversionSource);
        if (action is null) return Reject(CommandErrorCode.IllegalAction, "This card cannot be recast under the current rules.");
        return Accept(() =>
        {
            ClearPendingDecision();
            ExecuteAction(actor, action);
            PublishState();
            return _options.AdvanceAfterHumanCommands ? AdvanceToHumanBoundary() : BuildResult();
        });
    }

    private void ResolveRecast(CharacterState actor, Card card, CardKind? playedCardKind = null)
    {
        _selectedUseConversion = null;
        _hasSelectedUseConversionChoice = false;
        if ((playedCardKind is null && card.Kind != CardKind.IronChain) ||
            (playedCardKind is not null && playedCardKind != CardKind.IronChain))
            throw new InvalidOperationException("Only Iron Chain may be recast.");
        MoveCards([card], CardLocation.Hand(actor.Seat), CardLocation.DiscardPile, CardMoveReasons.RecastDiscard);
        var drawn = DrawCards(actor, 1, log: false, reason: CardMoveReasons.RecastDraw);
        if (_aiBrains.TryGetValue(actor.Seat, out var brain)) brain.ObserveRecast(_turnNumber, card.Id);
        QueueGameEvent(new CardRecastEvent(actor.Seat, card.Id, playedCardKind ?? card.Kind, drawn.Count));
        AddLog("Recast", $"{actor.Name} 重铸【铁索连环】，摸 {drawn.Count} 张牌。", actor.Seat);
    }

    private long _cardActionSequence;
    private AttackResolution? _programCardAttack;
    private readonly HashSet<long> _acceptedProgramUses = [];
    private readonly HashSet<long> _committedProgramUses = [];
    private readonly Dictionary<long, List<AttackResolution>> _preparedProgramTargets = [];

    private int GetFangtianEffectiveTarget(FangtianHalberdResolution pending, int index) =>
        _acceptedProgramUses.Contains(pending.ResolutionId) &&
        _preparedProgramTargets.TryGetValue(pending.ResolutionId, out var prepared)
            ? prepared[index].TargetSeat : pending.TargetSeats[index];

    private CardActionContext? CaptureCardUseAction(Card card, int actorSeat,
        IReadOnlyList<int> targets, CardKind effectiveKind, IReadOnlyList<int> physicalIds,
        CardConversionSource? explicitConversion = null,
        IReadOnlyList<CardConversionSource>? additionalConversions = null,
        SkillKind? cardKindModifierSkill = null,
        IReadOnlyList<int>? designatedTargetSeats = null)
    {
        var costs = physicalIds.Select(id => new CardActionCost(id,
            _cardZones.CardsAt(_cardZones.GetLocation(id)).Single(item => item.Id == id).Kind,
            _cardZones.GetLocation(id))).ToArray();
        var provider = costs.FirstOrDefault()?.From.OwnerSeat ?? actorSeat;
        CardConversionSource? conversion;
        if (explicitConversion is not null)
        {
            var selectedConversion = _selectedUseConversion ?? _selectedResponseConversion;
            _selectedUseConversion = null;
            _selectedResponseConversion = null;
            _hasSelectedUseConversionChoice = false;
            _hasSelectedResponseConversionChoice = false;
            if (selectedConversion is not null && selectedConversion != explicitConversion)
            {
                throw new InvalidOperationException("The explicit card-use conversion no longer matches the selected action.");
            }
            conversion = explicitConversion;
        }
        else
        {
            conversion = physicalIds.Count == 1
                ? GetSelectedUseConversion(_players[actorSeat], _players[provider], card, effectiveKind)
                : null;
        }
        var conversionChain = new List<CardConversionSource>();
        if (conversion is not null) conversionChain.Add(conversion);
        if (additionalConversions is not null)
        {
            if (additionalConversions.Any(source => source is null) ||
                additionalConversions.Distinct().Count() != additionalConversions.Count ||
                conversion is not null && additionalConversions.Contains(conversion))
            {
                throw new InvalidOperationException("A card-use conversion chain must contain distinct sources.");
            }
            conversionChain.AddRange(additionalConversions);
        }
        return new CardActionContext(++_cardActionSequence,
            _resolutionStack.OfType<CardUseFrame>().LastOrDefault()?.Action?.ActionId,
            CardActionType.Use, actorSeat, provider, provider == actorSeat ? null : actorSeat,
            null, null, effectiveKind, targets, costs, conversionChain,
            (designatedTargetSeats ?? targets ));
    }

    private bool TryBeginCardResponsePrograms(AttackResolution attack, CharacterState actor,
        CharacterState provider, int? requesterSeat, int opponentSeat, CardKind effectiveKind,
        IReadOnlyList<CardActionCost> costs, ProgramCardContinuation continuation,
        CardConversionSource? conversionSource = null)
    {
        var parent = _resolutionStack.OfType<CardUseFrame>().LastOrDefault(frame => frame.Id == attack.ResolutionId);
        var action = new CardActionContext(++_cardActionSequence, parent?.Action?.ActionId,
            CardActionType.Response, actor.Seat, provider.Seat, requesterSeat, actor.Seat,
            opponentSeat, effectiveKind, [], costs, conversionSource is null ? [] : [conversionSource]);
        QueueGameEvent(new CardActionAcceptedEvent(action));
        return TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardResponseAccepted,
            [opponentSeat], continuation);
    }

    // Target redirection is completed for every target before any use trigger or
    // response starts. Keep the prepared attack objects so Liuli cannot run twice.
    private bool PrepareProgramSlashTargets(AttackResolution attack)
    {
        if (_acceptedProgramUses.Contains(attack.ResolutionId)) return false;
        var frame = _resolutionStack.OfType<CardUseFrame>().Single(item => item.Id == attack.ResolutionId);
        if (frame.Action is not { } captured) return false;
        if (_pendingFangtianHalberd is { } multi && multi.ResolutionId == attack.ResolutionId)
        {
            if (!_preparedProgramTargets.TryGetValue(attack.ResolutionId, out var prepared))
                _preparedProgramTargets[attack.ResolutionId] = prepared = [];
            prepared.Add(attack);
            if (prepared.Count < multi.TargetSeats.Count)
            {
                multi.TargetIndex++;
                BeginNextFangtianHalberdTarget(multi);
                return true;
            }
            multi.TargetIndex = 0;
            attack = prepared[0];
            multi.CurrentAttack = attack;
            _pendingAttack = attack;
            SetCardUseTargetIndex(multi.ResolutionId, 0);
            frame = _resolutionStack.OfType<CardUseFrame>().Single(item => item.Id == attack.ResolutionId);
        }
        _acceptedProgramUses.Add(attack.ResolutionId);
        var finalized = new CardActionContext(captured.ActionId, captured.ParentActionId, captured.Type,
            captured.ActorSeat, captured.ProviderSeat, captured.RequesterSeat, captured.ResponderSeat,
            captured.OpponentSeat, captured.EffectiveKind, frame.TargetSeats, captured.PhysicalCards, captured.ConversionChain,
            (frame.TargetSeats.Where(seat => _players[seat].IsAlive).Distinct().ToArray() ));
        var index = _resolutionStack.FindLastIndex(item => item.Id == frame.Id);
        _resolutionStack[index] = frame with { Action = finalized };
        QueueGameEvent(new CardActionAcceptedEvent(finalized));
        if (TryBeginProgramCardWindow(attack, finalized, SkillProgramTriggerWindow.CardUseTargetsFinalized,
                finalized.TargetSeats, ProgramCardContinuation.Slash)) return true;
        if (_preparedProgramTargets.ContainsKey(attack.ResolutionId))
        {
            ContinueSlashAfterFinalizedTargets(attack);
            return true;
        }
        return false;
    }

    private bool TryBeginProgramCardWindow(AttackResolution? attack, CardActionContext action,
        SkillProgramTriggerWindow window, IReadOnlyList<int> opponents, ProgramCardContinuation continuation,
        bool? cardUseCausedDamage = null, ProgramTrickContinuation? trickContinuation = null)
    {
        var candidates = CollectSharedCardActionCandidates(action, window, opponents,
            cardUseCausedDamage).ToList();
        candidates = candidates
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.OwnerSeat)
            .ThenBy(candidate => candidate.SkillId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.SkillInstanceId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.TriggerId, StringComparer.Ordinal)
            .ToList();
        if (candidates.Count == 0) return false;
        if (_resolutionStack.Any(frame => frame is ProgramCardTriggerWindowFrame))
            throw new InvalidOperationException("Card trigger windows cannot overlap.");
        _programCardAttack = attack;
        var frameId = ++_resolutionSequence;
        candidates = candidates.Select(candidate => candidate.FrozenContext is { } frozen
                ? candidate with
                {
                    FrozenContext = frozen with
                    {
                        ParentFrameId = frameId,
                        CardUse = frozen.CardUse! with { ParentCardUseFrameId = _resolutionStack[^1].Id }
                    }
                }
                : candidate)
            .ToList();
        var frame = new ProgramCardTriggerWindowFrame(frameId,
            _resolutionStack[^1].Id, action, continuation, Array.AsReadOnly(candidates.ToArray()),
            TrickContinuation: trickContinuation);
        _resolutionStack.Add(frame);
        ContinueProgramCardWindow();
        return true;
    }

    /// <summary>Called after target redirection and before the first Slash target effect.</summary>
    private bool TryBeginProgramCardUseBeforeTargetEffects(AttackResolution attack)
    {
        var frame = _resolutionStack.OfType<CardUseFrame>().Single(item => item.Id == attack.ResolutionId);
        var action = frame.Action ?? throw new InvalidOperationException(
            "A before-target-effects program window requires the frozen card action.");
        return TryBeginProgramCardWindow(attack, action,
            SkillProgramTriggerWindow.CardUseBeforeTargetEffects, [attack.TargetSeat],
            ProgramCardContinuation.BeforeTargetEffects);
    }

    /// <summary>Called before Jizhi/Nullification and the first trick target effect.</summary>
    private bool TryBeginProgramCardUseBeforeTargetEffects(JizhiResolution pending)
    {
        var frame = _resolutionStack.OfType<CardUseFrame>().Single(item => item.Id == pending.ResolutionId);
        var action = frame.Action ?? throw new InvalidOperationException(
            "A trick before-target-effects program window requires the frozen card action.");
        return TryBeginProgramCardWindow(
            null,
            action,
            SkillProgramTriggerWindow.CardUseBeforeTargetEffects,
            frame.TargetSeats,
            ProgramCardContinuation.BeforeTrickTargetEffects,
            trickContinuation: new(
                pending.Card.Id,
                pending.ActionKind,
                pending.TargetCardId,
                pending.RequiredCardKind));
    }

    private IReadOnlyList<ProgramCardTriggerCandidate> CollectSharedCardActionCandidates(
        CardActionContext action, SkillProgramTriggerWindow window, IReadOnlyList<int> eventTargets,
        bool? cardUseCausedDamage)
    {
        var targets = eventTargets.Distinct().ToHashSet();
        var result = new List<ProgramCardTriggerCandidate>();
        foreach (var owner in _players.Where(player => player.IsAlive).OrderBy(player => player.Seat))
        foreach (var binding in GetSkillBindingShard(owner)?.GetInstanceTriggers(window) ?? [])
        {
            var trigger = binding.Trigger;
            if (trigger.OwnerRelation is not { } relation ||
                trigger.CardKinds.Count > 0 && !trigger.CardKinds.Contains(action.EffectiveKind) ||
                trigger.CardCategories.Count > 0 && (action.Type != CardActionType.Use ||
                    !MatchesProgramCardCategory(action.EffectiveKind, trigger.CardCategories))) continue;
            var matchingSources = action.ConversionChain.Where(source =>
                source.OwnerSeat == owner.Seat &&
                source.SkillId == trigger.SourceSkillId &&
                (trigger.SourceViewAsId is null || source.BindingId == trigger.SourceViewAsId)).ToArray();
            if (trigger.SourceSkillId is not null && matchingSources.Length == 0) continue;
            var matches = relation switch
            {
                SkillProgramCardActionOwnerRelation.Actor => owner.Seat == action.ActorSeat,
                SkillProgramCardActionOwnerRelation.Target => targets.Contains(owner.Seat),
                SkillProgramCardActionOwnerRelation.Observer => true,
                SkillProgramCardActionOwnerRelation.ConversionSource => matchingSources.Length > 0,
                _ => false
            };
            if (!matches) continue;
            var eventTargetsForBinding = relation == SkillProgramCardActionOwnerRelation.ConversionSource
                ? targets.Where(seat => _players[seat].IsAlive).Order().ToArray()
                : [relation == SkillProgramCardActionOwnerRelation.Target
                    ? owner.Seat : targets.Count == 1 ? targets.Single() : -1];
            foreach (var eventTarget in eventTargetsForBinding)
            {
                var facts = CaptureProgramTriggerFacts(owner, action) with
                {
                    CardUseCausedDamage = cardUseCausedDamage
                };
                var context = CreateCardActionProgramContext(action, window, parentFrameId: 0,
                    owner.Seat, eventTarget, facts);
                result.Add(new(owner.Seat, eventTarget, binding.SkillId, trigger.Id,
                    binding.Program.GameplayHash, binding.SkillInstanceId, trigger.Priority, context));
            }
        }
        return result.DistinctBy(candidate => (action.ActionId, candidate.OwnerSeat, candidate.SkillId,
            candidate.SkillInstanceId, candidate.TriggerId, candidate.OpponentSeat)).ToArray();
    }

    private static ProgramTriggerCandidate ToSharedCandidate(ProgramCardTriggerCandidate candidate) =>
        new(candidate.OwnerSeat, candidate.SkillId, candidate.TriggerId, candidate.SkillInstanceId,
            candidate.GameplayHash, candidate.Priority);

    private ProgramSkillWindowContext CreateCardActionProgramContext(
        ProgramCardTriggerWindowFrame frame, ProgramCardTriggerCandidate candidate)
        => candidate.FrozenContext ?? CreateCardActionProgramContext(frame.Action,
            GetCardActionWindow(frame), frame.Id, candidate.OwnerSeat, candidate.OpponentSeat,
            CaptureProgramTriggerFacts(_players[candidate.OwnerSeat], frame.Action));

    private ProgramSkillWindowContext CreateCardActionProgramContext(
        CardActionContext action, SkillProgramTriggerWindow window, long parentFrameId,
        int ownerSeat, int eventTargetSeat, SkillProgramTriggerFacts facts)
    {
        var suits = action.PhysicalCards.Select(cost =>
        {
            var location = _cardZones.GetLocation(cost.CardId);
            return _cardZones.CardsAt(location).Single(card => card.Id == cost.CardId).Suit;
        }).ToArray();
        var publicSuit = suits.Length == 1 ? suits[0] : (Suit?)null;
        bool? isRed = suits.Length == 0 ? null : suits.All(suit => suit is Suit.Heart or Suit.Diamond);
        var debit = GetCardUseDebit(action.ActionId);
        return new(window, parentFrameId, ownerSeat, SourceSeat: action.ActorSeat,
            TargetSeat: eventTargetSeat >= 0 ? eventTargetSeat : null, Facts: facts,
            CardUse: new(action.ActionId, parentFrameId, action.ActorSeat,
                eventTargetSeat >= 0 ? eventTargetSeat : null,
                action.EffectiveKind, publicSuit, isRed, debit is not null, debit,
                action.DesignatedTargetSeats));
    }

    private static SkillProgramTriggerWindow GetCardActionWindow(ProgramCardTriggerWindowFrame frame) =>
        frame.Continuation == ProgramCardContinuation.CompletedSlash
            ? SkillProgramTriggerWindow.CardUseCompleted
            : frame.Continuation == ProgramCardContinuation.CommittedSlash
            ? SkillProgramTriggerWindow.CardUseCommitted
            : frame.Continuation is ProgramCardContinuation.BeforeTargetEffects or
                ProgramCardContinuation.BeforeTrickTargetEffects
                ? SkillProgramTriggerWindow.CardUseBeforeTargetEffects
                : frame.Action.Type == CardActionType.Use
                    ? SkillProgramTriggerWindow.CardUseTargetsFinalized
                    : SkillProgramTriggerWindow.CardResponseAccepted;

    private void ContinueProgramCardWindow()
    {
        while (_resolutionStack.LastOrDefault() is ProgramCardTriggerWindowFrame frame)
        {
            if (frame.CandidateIndex == frame.Candidates.Count)
            {
                var attack = _programCardAttack;
                _programCardAttack = null;
                PopResolutionFrame(frame.Id, ResolutionFrameKind.ProgramCardTriggerWindow);
                if (frame.Continuation == ProgramCardContinuation.CommittedSlash)
                    ContinueCommittedSlashAfterPrograms(attack ??
                        throw new InvalidOperationException("A committed Slash trigger lost its attack continuation."));
                else if (frame.Continuation == ProgramCardContinuation.BeforeTargetEffects)
                    ContinueSlashAfterProgramTargetEffects(attack ??
                        throw new InvalidOperationException("A before-target-effects trigger lost its attack continuation."));
                else if (frame.Continuation == ProgramCardContinuation.BeforeTrickTargetEffects)
                    ContinueTrickAfterProgramTargetEffects(frame);
                else if (frame.Continuation == ProgramCardContinuation.Slash)
                    ContinueSlashAfterFinalizedTargets(attack ??
                        throw new InvalidOperationException("A Slash card trigger lost its attack continuation."));
                else if (frame.Continuation == ProgramCardContinuation.CompletedSlash)
                    ContinueCompletedSlashAfterPrograms(attack ??
                        throw new InvalidOperationException("A completed Slash trigger lost its attack continuation."));
                else if (frame.Continuation == ProgramCardContinuation.DelayedCard)
                    ContinueAcceptedDelayedCardUse(frame);
                else ContinueAcceptedCardResponse(attack ??
                    throw new InvalidOperationException("A response card trigger lost its attack continuation."),
                    frame.Action, frame.Continuation);
                return;
            }
            var candidate = frame.Candidates[frame.CandidateIndex];
            var program = _contentRegistry!.Skills[candidate.SkillId].Program!;
            if (program.GameplayHash != candidate.GameplayHash)
                throw new InvalidOperationException("A running card trigger definition changed.");
            var trigger = program.Triggers.Single(item => item.Id == candidate.TriggerId);
            var shared = ToSharedCandidate(candidate);
            var context = CreateCardActionProgramContext(frame, candidate);
            if (!CanRunProgramTrigger(shared, context))
            {
                AdvanceProgramCardCandidate(frame);
                continue;
            }
            if (trigger.Optional)
            {
                ExposeProgramTriggerDecision(shared, context);
                return;
            }
            BeginProgramBinding(shared, context);
            return;
        }
    }

    private void ContinueCompletedSlashAfterPrograms(AttackResolution attack)
    {
        PopFinishedCardUse(attack.ResolutionId);
        CompleteFinishedAttackCardUse(attack);
        CompleteAttackAfterCardResolution(attack);
    }

    private bool TryBeginDelayedCardUsePrograms(long resolutionId)
    {
        if (!_acceptedProgramUses.Add(resolutionId)) return false;
        var frame = _resolutionStack.OfType<CardUseFrame>().Single(item => item.Id == resolutionId);
        var action = frame.Action ??
            throw new InvalidOperationException("A direct delayed-card trigger requires a captured card action.");
        QueueGameEvent(new CardActionAcceptedEvent(action));
        return TryBeginProgramCardWindow(
            attack: null,
            action,
            SkillProgramTriggerWindow.CardUseTargetsFinalized,
            action.TargetSeats,
            ProgramCardContinuation.DelayedCard);
    }

    private void ContinueAcceptedDelayedCardUse(ProgramCardTriggerWindowFrame frame)
    {
        var action = frame.Action;
        if (action.EffectiveKind != CardKind.Lightning || action.PhysicalCards.Count != 1 ||
            action.TargetSeats is not [var targetSeat] || targetSeat != action.ActorSeat)
        {
            throw new InvalidOperationException("The delayed-card trigger continuation is not a self-targeted Lightning.");
        }
        var card = _cardZones.CardsAt(CardLocation.Processing)
            .Single(item => item.Id == action.PhysicalCards[0].CardId);
        BeginJizhiOrNullificationWindow(
            frame.ParentFrameId,
            card,
            action.ActorSeat,
            action.TargetSeats,
            LegalActionKind.Lightning,
            playedCardKind: action.EffectiveKind);
    }

    private void ContinueTrickAfterProgramTargetEffects(ProgramCardTriggerWindowFrame frame)
    {
        var continuation = frame.TrickContinuation ??
            throw new InvalidOperationException("A trick program window lost its frozen continuation.");
        var action = frame.Action;
        if (action.Type != CardActionType.Use ||
            CardCatalog.Get(action.EffectiveKind).CategoryName != "锦囊牌" ||
            action.PhysicalCards.All(cost => cost.CardId != continuation.EffectCardId))
            throw new InvalidOperationException("The trick trigger continuation does not match its card action.");
        var card = _cardZones.CardsAt(CardLocation.Processing)
            .Single(item => item.Id == continuation.EffectCardId);
        ContinueJizhiOrNullificationAfterTargetTriggers(new JizhiResolution(
            action.ActorSeat,
            frame.ParentFrameId,
            card,
            action.EffectiveKind,
            action.TargetSeats,
            continuation.ActionKind,
            continuation.TargetCardId,
            continuation.RequiredCardKind,
            activeNullification: null));
    }

    private void ContinueCommittedSlashAfterPrograms(AttackResolution attack)
    {
        BeginSlashTargetResolution(attack);
    }

    private void AdvanceProgramCardCandidate(ProgramCardTriggerWindowFrame frame)
    {
        var candidate = frame.Candidates[frame.CandidateIndex];
        QueueGameEvent(new ProgramCardTriggerResolvedEvent(frame.Id, candidate.SkillId, candidate.TriggerId,
            candidate.OwnerSeat, candidate.OpponentSeat, frame.Activated));
        _resolutionStack[^1] = frame with
        {
            CandidateIndex = frame.CandidateIndex + 1,
            Activated = false
        };
    }

    private void AssertProgramCardWindowState()
    {
        var frames = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().ToArray();
        if (frames.Length == 0)
        {
            if (_programCardAttack is not null)
                throw new InvalidOperationException("A card trigger continuation lost its frame.");
            return;
        }
        var frame = frames.Single();
        var frameIndex = _resolutionStack.FindLastIndex(item => ReferenceEquals(item, frame));
        var resolvingProgramJudgmentDamage =
            _pendingAttack is { IsProgramJudgmentDamage: true } &&
            _pendingJudgment?.Continuation == JudgmentContinuationKind.ProgramSkill;
        var attackMatches = resolvingProgramJudgmentDamage ||
            (frame.Continuation is ProgramCardContinuation.DelayedCard or
                    ProgramCardContinuation.BeforeTrickTargetEffects
                ? _programCardAttack is null && _pendingAttack is null
                : _programCardAttack is not null && ReferenceEquals(_programCardAttack, _pendingAttack));
        var candidateCursorValid = frame.CandidateIndex >= 0 &&
                                   frame.CandidateIndex < frame.Candidates.Count;
        var candidate = candidateCursorValid ? frame.Candidates[frame.CandidateIndex] : null;
        var sharedContext = candidate?.FrozenContext;
        var sharedIdentityMatches = candidate is not null &&
            sharedContext is { CardUse: { } cardUse } && sharedContext.ParentFrameId == frame.Id &&
            cardUse.ParentCardUseFrameId == frame.ParentFrameId && cardUse.CardActionId == frame.Action.ActionId;
        var sharedPromptMatches = sharedIdentityMatches && ReferenceEquals(_resolutionStack.Last(), frame) &&
            _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } sharedPrompt &&
            sharedPrompt.PlayerSeat == candidate!.OwnerSeat && sharedPrompt.SkillPrompt?.SkillId == candidate.SkillId &&
            sharedPrompt.Choices.Count > 0;
        var sharedChildMatches = sharedIdentityMatches && frameIndex + 1 < _resolutionStack.Count &&
            _resolutionStack[frameIndex + 1] is ProgramSkillFrame child &&
            child.WindowContext == sharedContext && child.OwnerSeat == candidate!.OwnerSeat &&
            child.SkillId == candidate.SkillId && child.SkillInstanceId == candidate.SkillInstanceId &&
            child.TriggerId == candidate.TriggerId && child.GameplayHash == candidate.GameplayHash;
        var trickContinuationMatches = frame.Continuation == ProgramCardContinuation.BeforeTrickTargetEffects
            ? frame.TrickContinuation is { } trick &&
              frame.Action.Type == CardActionType.Use &&
              frame.Action.PhysicalCards.Any(cost => cost.CardId == trick.EffectCardId)
            : frame.TrickContinuation is null;
        if (!attackMatches ||
            frameIndex < 1 || _resolutionStack[frameIndex - 1].Id != frame.ParentFrameId ||
            !trickContinuationMatches ||
            !candidateCursorValid ||
            !sharedPromptMatches && !sharedChildMatches ||
             frame.Action.PhysicalCards.Any(cost =>
                 frame.Continuation == ProgramCardContinuation.CompletedSlash
                     ? _cardZones.GetLocation(cost.CardId).Zone is not
                         (CardZoneKind.DiscardPile or CardZoneKind.Hand or CardZoneKind.DrawPile)
                     : _cardZones.GetLocation(cost.CardId) != CardLocation.Processing))
            throw new InvalidOperationException("A card trigger window has an invalid cursor, prompt or paid card.");
    }
}
