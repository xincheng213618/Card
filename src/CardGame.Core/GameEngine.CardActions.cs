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
        var action = BuildLegalActions(actor).SingleOrDefault(action => action.Kind == LegalActionKind.Recast && action.CardId == command.CardId);
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
        if (_rulesVersion < 6 ||
            (playedCardKind is null && card.Kind != CardKind.IronChain) ||
            (playedCardKind is not null && playedCardKind != CardKind.IronChain))
            throw new InvalidOperationException("Only Iron Chain may be recast under rules version 6.");
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
        SkillKind? cardKindModifierSkill = null)
    {
        if (_rulesVersion < 80) return null;
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
            _selectedSlashConversionChoice = false;
            if (selectedConversion is not null && selectedConversion != explicitConversion)
            {
                throw new InvalidOperationException("The explicit card-use conversion no longer matches the selected action.");
            }
            conversion = explicitConversion;
        }
        else
        {
            conversion = physicalIds.Count == 1
                ? GetSelectedUseConversion(_players[provider], card, effectiveKind)
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
            null, null, effectiveKind, targets, costs, conversionChain);
    }

    private bool TryBeginCardResponsePrograms(AttackResolution attack, CharacterState actor,
        CharacterState provider, int? requesterSeat, int opponentSeat, CardKind effectiveKind,
        IReadOnlyList<CardActionCost> costs, ProgramCardContinuation continuation,
        CardConversionSource? conversionSource = null)
    {
        if (_rulesVersion < 80) return false;
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
        if (_rulesVersion < 80 || _acceptedProgramUses.Contains(attack.ResolutionId)) return false;
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
            captured.OpponentSeat, captured.EffectiveKind, frame.TargetSeats, captured.PhysicalCards, captured.ConversionChain);
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
        var candidates = new List<ProgramCardTriggerCandidate>();
        foreach (var ownerSeat in action.ConversionChain.Select(source => source.OwnerSeat)
                     .Append(action.ActorSeat).Distinct().Order())
        {
            var owner = _players[ownerSeat];
            if (!owner.IsAlive) continue;
            foreach (var binding in EnabledUniqueProgramTriggers(owner, window))
            {
                var program = binding.Program;
                var trigger = binding.Trigger;
                if (program.UsesCompositionKernel) continue;
                if (!(trigger.CardKinds.Count > 0
                             ? ownerSeat == action.ActorSeat && trigger.CardKinds.Contains(action.EffectiveKind)
                             : action.ConversionChain.Any(source => source.OwnerSeat == ownerSeat &&
                                 source.SkillId == trigger.SourceSkillId &&
                                 (trigger.SourceViewAsId is null || source.BindingId == trigger.SourceViewAsId))))
                    continue;
                foreach (var opponentSeat in opponents.Distinct())
                {
                    var candidate = new ProgramCardTriggerCandidate(ownerSeat, opponentSeat, program.Id,
                        trigger.Id, program.GameplayHash);
                    if (CanRunProgramCardTrigger(candidate, trigger)) candidates.Add(candidate);
                }
            }
        }
        candidates.AddRange(CollectSharedCardActionCandidates(action, window, opponents,
            cardUseCausedDamage));
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

    /// <summary>Called before Jizhi/Nullification and the first ordinary-trick target effect.</summary>
    private bool TryBeginProgramCardUseBeforeTargetEffects(JizhiResolution pending)
    {
        var frame = _resolutionStack.OfType<CardUseFrame>().Single(item => item.Id == pending.ResolutionId);
        var action = frame.Action ?? throw new InvalidOperationException(
            "An ordinary-trick before-target-effects program window requires the frozen card action.");
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
            if (!binding.Program.UsesCompositionKernel || trigger.OwnerRelation is not { } relation ||
                trigger.CardKinds.Count > 0 && !trigger.CardKinds.Contains(action.EffectiveKind)) continue;
            var matches = relation switch
            {
                SkillProgramCardActionOwnerRelation.Actor => owner.Seat == action.ActorSeat,
                SkillProgramCardActionOwnerRelation.Target => targets.Contains(owner.Seat),
                SkillProgramCardActionOwnerRelation.Observer => true,
                _ => false
            };
            if (!matches) continue;
            var eventTarget = relation == SkillProgramCardActionOwnerRelation.Target
                ? owner.Seat : targets.Count == 1 ? targets.Single() : -1;
            var facts = CaptureProgramTriggerFacts(owner, action) with
            {
                CardUseCausedDamage = cardUseCausedDamage
            };
            var context = CreateCardActionProgramContext(action, window, parentFrameId: 0,
                owner.Seat, eventTarget, facts);
            result.Add(new(owner.Seat, eventTarget, binding.SkillId, trigger.Id,
                binding.Program.GameplayHash, binding.SkillInstanceId, true, trigger.Priority, context));
        }
        return result.DistinctBy(candidate => (action.ActionId, candidate.OwnerSeat, candidate.SkillId,
            candidate.SkillInstanceId, candidate.TriggerId)).ToArray();
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
                action.EffectiveKind, publicSuit, isRed, debit is not null, debit));
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

    private bool CanRunProgramCardTrigger(ProgramCardTriggerCandidate candidate, SkillProgramTrigger trigger)
    {
        var owner = _players[candidate.OwnerSeat];
        var opponent = _players[candidate.OpponentSeat];
        if (!owner.IsAlive || !opponent.IsAlive) return false;
        var first = trigger.Effects.FirstOrDefault(effect =>
            effect.Condition.Evaluate(CreateSkillContext(owner)));
        if (first is null) return false;
        if (first.Op == SkillProgramTriggerEffectOp.SelectTarget)
            return GetProgramCardTargetSeats(owner, first).Count > 0;
        return trigger.Effects.Any(effect =>
            effect.Condition.Evaluate(CreateSkillContext(owner)) &&
            (effect.Op != SkillProgramTriggerEffectOp.ObtainOpponentHandCard ||
             owner.Seat != opponent.Seat && GetHand(opponent).Count > 0));
    }

    private IReadOnlyList<int> GetProgramCardTargetSeats(
        CharacterState owner,
        SkillProgramTriggerEffect effect)
    {
        if (effect is not
            {
                Op: SkillProgramTriggerEffectOp.SelectTarget,
                TargetKind: { } targetKind
            })
            return [];
        return _players
            .Where(target => target.IsAlive &&
                (targetKind is SkillProgramTargetKind.AnyLiving or SkillProgramTargetKind.AnyWounded ||
                 target.Seat != owner.Seat) &&
                (targetKind is SkillProgramTargetKind.OtherLiving or SkillProgramTargetKind.AnyLiving ||
                 target.Hp < target.MaxHp))
            .Select(target => target.Seat)
            .Order()
            .ToArray();
    }

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
                    ContinueOrdinaryTrickAfterProgramTargetEffects(frame);
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
            var owner = _players[candidate.OwnerSeat];
            if (candidate.UsesSharedExecutor)
            {
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
            if (!frame.Activated)
            {
                if (!CanRunProgramCardTrigger(candidate, trigger) ||
                    !GetSkillBindingShard(owner)!.HasProgram(candidate.SkillId, candidate.GameplayHash))
                {
                    AdvanceProgramCardCandidate(frame);
                    continue;
                }
                if (trigger.Optional)
                {
                    ExposeProgramCardPrompt(frame, candidate,
                        [ProgramTriggerChoice("activate", "发动技能", candidate),
                         ProgramTriggerChoice("skip", "不发动", candidate)]);
                    return;
                }
                _resolutionStack[^1] = frame with { Activated = true };
                continue;
            }
            if (frame.InstructionIndex == trigger.Effects.Count)
            {
                AdvanceProgramCardCandidate(frame);
                continue;
            }
            var effect = trigger.Effects[frame.InstructionIndex];
            if (!owner.IsAlive || !effect.Condition.Evaluate(CreateSkillContext(owner)))
            {
                _resolutionStack[^1] = frame with { InstructionIndex = frame.InstructionIndex + 1 };
                continue;
            }
            if (effect.Op == SkillProgramTriggerEffectOp.SelectTarget)
            {
                ExposeProgramCardTargetPrompt(frame, candidate, effect);
                return;
            }
            var targetSeat = effect.Target switch
            {
                SkillProgramTriggerEffectTarget.Owner => candidate.OwnerSeat,
                SkillProgramTriggerEffectTarget.Opponent => candidate.OpponentSeat,
                SkillProgramTriggerEffectTarget.SelectedTarget when frame.SelectedTargetSeat is { } selected => selected,
                _ => throw new InvalidOperationException("The configured card trigger has no executable target.")
            };
            var target = _players[targetSeat];
            if (effect.Op == SkillProgramTriggerEffectOp.ObtainOpponentHandCard &&
                _players[candidate.OpponentSeat].IsAlive && GetHand(_players[candidate.OpponentSeat]).Count > 0)
            {
                var choices = Enumerable.Range(0, GetHand(_players[candidate.OpponentSeat]).Count)
                    .Select(slot => ProgramTriggerChoice("take", $"选择第 {slot + 1} 张手牌", candidate, slot)).ToArray();
                ExposeProgramCardPrompt(frame, candidate, choices);
                return;
            }
            // Advance before executing an effect; a replay/resume never pays it twice.
            _resolutionStack[^1] = frame with { InstructionIndex = frame.InstructionIndex + 1 };
            if (effect.Op == SkillProgramTriggerEffectOp.StartJudgment)
            {
                if (!target.IsAlive) continue;
                var judgmentResult = BeginJudgment(
                    _programCardAttack,
                    target.Seat,
                    effect.JudgmentReason ??
                        throw new InvalidOperationException("A configured judgment has no stable reason."),
                    frame.Id,
                    frame.Action.EffectiveKind,
                    JudgmentContinuationKind.ProgramCard,
                    damageSkill: null,
                    sourceSeat: owner.Seat);
                if (judgmentResult is not null) ContinueProgramCardWindow();
                return;
            }
            if (!target.IsAlive) continue;
            if (effect.Op == SkillProgramTriggerEffectOp.Draw)
                DrawCards(target, effect.Amount, log: true, reason: new CardMoveReason($"skill-program.{candidate.SkillId}.draw"));
            else if (effect.Op == SkillProgramTriggerEffectOp.Recover)
            {
                var amount = Math.Min(effect.Amount, target.MaxHp - target.Hp);
                if (amount <= 0) continue;
                var recovery = BeginRecovery(frame.Id, owner.Seat, target.Seat, amount);
                target.Hp += amount;
                QueueGameEvent(new RecoveryAppliedEvent(owner.Seat, target.Seat, amount, target.Hp));
                PopResolutionFrame(recovery, ResolutionFrameKind.Recovery);
            }
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
        if (_rulesVersion < 84 || !_acceptedProgramUses.Add(resolutionId)) return false;
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

    private void ContinueOrdinaryTrickAfterProgramTargetEffects(ProgramCardTriggerWindowFrame frame)
    {
        var continuation = frame.TrickContinuation ??
            throw new InvalidOperationException("An ordinary-trick program window lost its frozen continuation.");
        var action = frame.Action;
        if (action.Type != CardActionType.Use || !IsOrdinaryTrick(action.EffectiveKind) ||
            action.PhysicalCards.All(cost => cost.CardId != continuation.EffectCardId))
            throw new InvalidOperationException("The ordinary-trick trigger continuation does not match its card action.");
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
            InstructionIndex = 0,
            Activated = false,
            SelectedTargetSeat = null
        };
    }

    private static PromptChoice ProgramTriggerChoice(string action, string label,
        ProgramCardTriggerCandidate candidate, int? slot = null, int? targetSeat = null)
    {
        var parameters = new Dictionary<string, string> { ["action"] = "program-trigger-" + action };
        if (slot is { } value) parameters["slot"] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (targetSeat is { } seat)
            parameters["target-seat"] = seat.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var choiceId = targetSeat is { } target
            ? $"program-trigger.{action}.target-{target}"
            : $"program-trigger.{action}.{slot}";
        return new PromptChoice(new ChoiceId(choiceId), label, [],
            [targetSeat ?? candidate.OpponentSeat], parameters);
    }

    private void ExposeProgramCardTargetPrompt(
        ProgramCardTriggerWindowFrame frame,
        ProgramCardTriggerCandidate candidate,
        SkillProgramTriggerEffect effect)
    {
        var owner = _players[candidate.OwnerSeat];
        var targetSeats = GetProgramCardTargetSeats(owner, effect);
        if (targetSeats.Count == 0)
        {
            AdvanceProgramCardCandidate(frame);
            ContinueProgramCardWindow();
            return;
        }
        ExposeProgramCardPrompt(frame, candidate,
            targetSeats.Select(targetSeat => ProgramTriggerChoice(
                "select-target", $"选择 {_players[targetSeat].Name} 进行判定", candidate, targetSeat: targetSeat)).ToArray(),
            targetSeats);
    }

    private void ExposeProgramCardPrompt(ProgramCardTriggerWindowFrame frame,
        ProgramCardTriggerCandidate candidate, IReadOnlyList<PromptChoice> choices,
        IReadOnlyList<int>? validTargetSeats = null)
    {
        var owner = _players[candidate.OwnerSeat];
        _pendingDecision = new PendingDecision(DecisionKind.ProgramCardTrigger, owner.Seat,
            $"【{_contentRegistry!.Skills[candidate.SkillId].Name}】：{_players[candidate.OpponentSeat].Name}", [],
            validTargetSeats ?? [],
            SourceSeat: owner.Seat)
        {
            PromptId = CreatePromptId(), IsPrivate = true,
            TargetSeat = candidate.OpponentSeat, Choices = choices
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private CommandResult SubmitProgramCardTriggerAnswer(int actorSeat, PromptId prompt, ChoiceId choice)
    {
        var error = ValidateHumanPrompt(actorSeat, DecisionKind.ProgramCardTrigger, prompt, CommandErrorCode.IllegalAction);
        if (error is not null) return Reject(error.Code, error.Message);
        var selected = _pendingDecision!.Choices.SingleOrDefault(item => item.Id == choice);
        if (selected is null) return Reject(CommandErrorCode.InvalidChoice, "The card trigger choice is not available.");
        return Accept(() =>
        {
            ResolveProgramCardChoice(selected);
            PublishState();
            return _options.AdvanceAfterHumanCommands ? AdvanceToHumanBoundary() : BuildResult();
        });
    }

    private void ResolveProgramCardChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramCardTriggerWindowFrame
            ?? throw new InvalidOperationException("Missing card trigger frame.");
        var candidate = frame.Candidates[frame.CandidateIndex];
        ClearPendingDecision();
        switch (selected.Parameters["action"])
        {
            case "program-trigger-skip":
                AdvanceProgramCardCandidate(frame);
                break;
            case "program-trigger-activate":
                _resolutionStack[^1] = frame with { Activated = true };
                break;
            case "program-trigger-take":
                var slot = int.Parse(selected.Parameters["slot"], System.Globalization.CultureInfo.InvariantCulture);
                var opponent = _players[candidate.OpponentSeat];
                var cards = GetHand(opponent);
                _resolutionStack[^1] = frame with { InstructionIndex = frame.InstructionIndex + 1 };
                if (opponent.IsAlive && _players[candidate.OwnerSeat].IsAlive && slot >= 0 && slot < cards.Count)
                    MoveCard(cards[slot], CardLocation.Hand(opponent.Seat), CardLocation.Hand(candidate.OwnerSeat),
                        new CardMoveReason($"skill-program.{candidate.SkillId}.obtainOpponentHandCard"));
                break;
            case "program-trigger-select-target":
                var program = _contentRegistry!.Skills[candidate.SkillId].Program ??
                    throw new InvalidOperationException("The card trigger target program is unavailable.");
                if (program.GameplayHash != candidate.GameplayHash)
                    throw new InvalidOperationException("A running card trigger target definition changed.");
                var trigger = program.Triggers.Single(item => item.Id == candidate.TriggerId);
                var selectionEffect = trigger.Effects[frame.InstructionIndex];
                var legalTargets = GetProgramCardTargetSeats(_players[candidate.OwnerSeat], selectionEffect);
                if (selected.Targets is not [var targetSeat] || !legalTargets.Contains(targetSeat))
                    throw new InvalidOperationException("The selected card-trigger judgment target is no longer legal.");
                _resolutionStack[^1] = frame with
                {
                    InstructionIndex = frame.InstructionIndex + 1,
                    SelectedTargetSeat = targetSeat
                };
                QueueGameEvent(new ProgramCardTargetSelectedEvent(
                    frame.Id,
                    frame.Action.ActionId,
                    candidate.SkillId,
                    candidate.TriggerId,
                    candidate.OwnerSeat,
                    targetSeat));
                break;
            default: throw new InvalidOperationException("Unsupported card trigger choice.");
        }
        ContinueProgramCardWindow();
    }

    private void ResolvePendingAiProgramCardChoice()
    {
        var decision = _pendingDecision ??
            throw new InvalidOperationException("AI card-trigger prompt is missing.");
        if (decision.Kind != DecisionKind.ProgramCardTrigger || _players[decision.PlayerSeat].IsHuman)
            throw new InvalidOperationException("The pending card-trigger prompt cannot use the AI route.");
        var first = decision.Choices[0];
        if (first.Parameters.GetValueOrDefault("action") == "program-trigger-activate" &&
            _resolutionStack.LastOrDefault() is ProgramCardTriggerWindowFrame activationFrame)
        {
            var candidate = activationFrame.Candidates[activationFrame.CandidateIndex];
            var program = _contentRegistry!.Skills[candidate.SkillId].Program!;
            var trigger = program.Triggers.Single(item => item.Id == candidate.TriggerId);
            var nextEffect = trigger.Effects[activationFrame.InstructionIndex];
            if (nextEffect.Op == SkillProgramTriggerEffectOp.SelectTarget)
            {
                var targetSeats = GetProgramCardTargetSeats(_players[candidate.OwnerSeat], nextEffect);
                var (preferredTargetSeat, thought) = _aiBrains[decision.PlayerSeat].ChooseLeijiTarget(
                    CreateSnapshot(decision.PlayerSeat), targetSeats, ++_thoughtSequence);
                AddThought(thought);
                if (preferredTargetSeat is null)
                {
                    ResolveProgramCardChoice(decision.Choices.Single(choice =>
                        choice.Parameters.GetValueOrDefault("action") == "program-trigger-skip"));
                    PublishState();
                    return;
                }
                ResolveProgramCardChoice(first);
                if (_resolutionStack.LastOrDefault() is ProgramCardTriggerWindowFrame targetFrame &&
                    _pendingDecision is { Kind: DecisionKind.ProgramCardTrigger })
                    _resolutionStack[^1] = targetFrame with { SelectedTargetSeat = preferredTargetSeat };
                PublishState();
                return;
            }
        }
        if (first.Parameters.GetValueOrDefault("action") != "program-trigger-select-target")
        {
            ResolveProgramCardChoice(first);
            return;
        }
        var frame = _resolutionStack.LastOrDefault() as ProgramCardTriggerWindowFrame ??
            throw new InvalidOperationException("AI card-trigger target prompt lost its frame.");
        var targetSeat = frame.SelectedTargetSeat;
        if (targetSeat is null)
        {
            var (fallbackSeat, thought) = _aiBrains[decision.PlayerSeat].ChooseLeijiTarget(
                CreateSnapshot(decision.PlayerSeat), decision.ValidTargetSeats, ++_thoughtSequence);
            AddThought(thought);
            targetSeat = fallbackSeat;
        }
        var selected = targetSeat is { } seat
            ? decision.Choices.Single(choice => choice.Targets.SequenceEqual([seat]))
            : first;
        ResolveProgramCardChoice(selected);
        PublishState();
    }

    private void AssertProgramCardWindowState()
    {
        var frames = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().ToArray();
        if (frames.Length == 0)
        {
            if (_programCardAttack is not null || _pendingDecision?.Kind == DecisionKind.ProgramCardTrigger)
                throw new InvalidOperationException("A card trigger continuation lost its frame.");
            return;
        }
        var frame = frames.Single();
        var frameIndex = _resolutionStack.FindLastIndex(item => ReferenceEquals(item, frame));
        var resolvingProgramJudgmentDamage =
            _pendingAttack is { IsProgramJudgmentDamage: true } &&
            _pendingJudgment?.Continuation == JudgmentContinuationKind.ProgramCard;
        var attackMatches = resolvingProgramJudgmentDamage ||
            (frame.Continuation is ProgramCardContinuation.DelayedCard or
                    ProgramCardContinuation.BeforeTrickTargetEffects
                ? _programCardAttack is null && _pendingAttack is null
                : _programCardAttack is not null && ReferenceEquals(_programCardAttack, _pendingAttack));
        var judgmentIsActive =
            _pendingJudgment is { Continuation: JudgmentContinuationKind.ProgramCard } judgment &&
            judgment.ParentFrameId == frame.Id &&
            ReferenceEquals(judgment.Attack, _programCardAttack);
        var candidateCursorValid = frame.CandidateIndex >= 0 &&
                                   frame.CandidateIndex < frame.Candidates.Count;
        var directPromptMatches = !judgmentIsActive &&
            candidateCursorValid &&
            ReferenceEquals(_resolutionStack.Last(), frame) &&
            _pendingDecision is { Kind: DecisionKind.ProgramCardTrigger } prompt &&
            prompt.PlayerSeat == frame.Candidates[frame.CandidateIndex].OwnerSeat &&
            prompt.Choices.Count > 0 &&
            prompt.Choices.All(choice => choice.Cards.Count == 0);
        var candidate = candidateCursorValid ? frame.Candidates[frame.CandidateIndex] : null;
        var sharedContext = candidate?.FrozenContext;
        var sharedIdentityMatches = candidate is { UsesSharedExecutor: true } &&
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
        if (_rulesVersion < 80 || !attackMatches ||
            frameIndex < 1 || _resolutionStack[frameIndex - 1].Id != frame.ParentFrameId ||
            (frame.Continuation == ProgramCardContinuation.DelayedCard && _rulesVersion < 84) ||
            !trickContinuationMatches ||
            !candidateCursorValid ||
            (!judgmentIsActive && !directPromptMatches && !sharedPromptMatches && !sharedChildMatches) ||
             frame.Action.PhysicalCards.Any(cost =>
                 frame.Continuation == ProgramCardContinuation.CompletedSlash
                     ? _cardZones.GetLocation(cost.CardId).Zone is not
                         (CardZoneKind.DiscardPile or CardZoneKind.Hand or CardZoneKind.DrawPile)
                     : _cardZones.GetLocation(cost.CardId) != CardLocation.Processing))
            throw new InvalidOperationException("A card trigger window has an invalid cursor, prompt or paid card.");
    }
}
