using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TracksOffTurnUsedCards =>
        _contentRegistry?.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GiveOffTurnUsedCards) == true;

    private bool HasOffTurnUsedCardGiftSource(CharacterState owner) => owner.IsAlive &&
        GetSkillBindingShard(owner).GetInstanceTriggers(SkillProgramTriggerWindow.DiscardPileReceived)
            .Any(b => b.Trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.GiveOffTurnUsedCards));

    private int? OffTurnGiftActualTurnOwner() => _turnNumber > 0 && IsValidPlayerSeat(_turnProgression.OwnerSeat) &&
        CompleteProgramEventHistory().OfType<TurnStartedEvent>().Any(e => e.TurnNumber == _turnNumber && e.ActorSeat == _turnProgression.OwnerSeat) &&
        !CompleteProgramEventHistory().OfType<TurnEndedEvent>().Any(e => e.TurnNumber == _turnNumber && e.ActorSeat == _turnProgression.OwnerSeat)
            ? _turnProgression.OwnerSeat : null;

    private static bool IsOffTurnNativeCompletion(CardMovementRecord m, OffTurnUsedCardKind kind) =>
        m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && (kind switch
        {
            OffTurnUsedCardKind.CardUse => m.Reason == CardMoveReasons.UseFinished || m.Reason == CardMoveReasons.IronChainFinished,
            OffTurnUsedCardKind.DodgeUse => m.Reason == CardMoveReasons.ResponseFinished,
            OffTurnUsedCardKind.NullificationUse => m.Reason == CardMoveReasons.NullificationFinished,
            _ => false
        });

    // Capture while the owning use is live. Multi-material native completion may
    // issue several batches: only the final batch anchors the single opportunity.
    private void CaptureOffTurnUsedCards(long batchId, int turnNumber, IReadOnlyList<CardMovementRecord> movements)
    {
        if (!TracksOffTurnUsedCards || !_players.Any(HasOffTurnUsedCardGiftSource) ||
            turnNumber != _turnNumber || OffTurnGiftActualTurnOwner() is null) return;
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().ToArray())
        {
            if (use.Action is not { Type: CardActionType.Use } action || action.ActorSeat != use.SourceSeat ||
                !(use.PhysicalCardIds ?? [use.CardId]).SequenceEqual(action.PhysicalCards.Select(c => c.CardId)) ||
                !movements.Any(m => action.PhysicalCards.Any(c => c.CardId == m.CardId) && IsOffTurnNativeCompletion(m, OffTurnUsedCardKind.CardUse))) continue;
            CaptureOffTurnUsedAction(action, use.Id, OffTurnUsedCardKind.CardUse, batchId, movements);
        }
        foreach (var action in CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Select(e => e.Action)
                     .Where(a => a.Type == CardActionType.Response && movements.Any(m => a.PhysicalCards.Any(c => c.CardId == m.CardId)))
                     .Reverse().DistinctBy(a => a.ActionId).ToArray())
        {
            if (!TryGetOffTurnResponseUse(action, out var useId, out var kind) ||
                !movements.Any(m => action.PhysicalCards.Any(c => c.CardId == m.CardId) && IsOffTurnNativeCompletion(m, kind))) continue;
            // A recycled entity in the same parent belongs only to the most recent response.
            if (action.PhysicalCards.Any(c => CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>()
                    .Any(e => e.Action.ActionId > action.ActionId && e.Action.PhysicalCards.Any(p => p.CardId == c.CardId)))) continue;
            CaptureOffTurnUsedAction(action, useId, kind, batchId, movements);
        }
    }

    // The native single-card Nullification constructs its accepted action after
    // discarding the material. This hook belongs immediately after that acceptance.
    private void CaptureOffTurnResponseUsedCards(CardActionContext action)
    {
        if (!TracksOffTurnUsedCards || !IsValidPlayerSeat(action.ActorSeat) || !HasOffTurnUsedCardGiftSource(_players[action.ActorSeat]) ||
            !TryGetOffTurnResponseUse(action, out var useId, out var kind) ||
            kind != OffTurnUsedCardKind.NullificationUse) return;
        var batch = _pendingCardsMovedBatches.LastOrDefault(b => b.TurnNumber == _turnNumber &&
            b.Movements.Any(m => action.PhysicalCards.Any(c => c.CardId == m.CardId) && IsOffTurnNativeCompletion(m, kind)));
        if (batch is not null) CaptureOffTurnUsedAction(action, useId, kind, batch.Id, batch.Movements);
    }

    private void CaptureOffTurnResponseUseOrigin(long owningUseId, CardActionContext action)
    {
        if (!TracksOffTurnUsedCards || action.Type != CardActionType.Response || action.ActorSeat != action.ProviderSeat ||
            action.RequesterSeat is not null || action.ResponderSeat != action.ActorSeat || action.ParentActionId is not null ||
            action.OpponentSeat is not { } opponent || !IsValidPlayerSeat(action.ActorSeat) ||
            _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u => u.Id == owningUseId) is not { Action: null } parent) return;
        OffTurnUsedCardKind kind;
        if (action.EffectiveKind == CardKind.Dodge && parent.CardAttack is { } attack &&
            IsSlashCard(attack.EffectiveCardKind ?? parent.CardKind) && attack.TargetSeat == action.ActorSeat && attack.SourceSeat == opponent)
            kind = OffTurnUsedCardKind.DodgeUse;
        else if (action.EffectiveKind == CardKind.Nullification && ActiveNullificationWindow is { } pending &&
                 pending.ParentFrameId == parent.Id && pending.SourceSeat == opponent)
            kind = OffTurnUsedCardKind.NullificationUse;
        else return;
        if (!CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Any(e => e.Action.ActionId == action.ActionId &&
                e.Action.ActorSeat == action.ActorSeat && e.Action.ProviderSeat == action.ProviderSeat && e.Action.PhysicalCards.SequenceEqual(action.PhysicalCards)) ||
            CompleteProgramEventHistory().OfType<OffTurnResponseUseOriginEvent>().Any(e => e.ActionId == action.ActionId)) return;
        AdvanceEventRulesAndQueueFact(new OffTurnResponseUseOriginEvent(parent.Id, action.ActionId, action.ActorSeat,
            action.ProviderSeat, opponent, parent.CardKind, kind));
    }

    private bool TryGetOffTurnResponseUse(CardActionContext action, out long useId, out OffTurnUsedCardKind kind)
    {
        useId = 0; kind = default;
        if (action.Type != CardActionType.Response || action.ActorSeat != action.ProviderSeat ||
            action.RequesterSeat is not null || action.ResponderSeat != action.ActorSeat) return false;
        var legacy = action.ParentActionId is null ? CompleteProgramEventHistory().OfType<OffTurnResponseUseOriginEvent>()
            .SingleOrDefault(e => e.ActionId == action.ActionId && e.ActorSeat == action.ActorSeat && e.ProviderSeat == action.ProviderSeat && e.OpponentSeat == action.OpponentSeat) : null;
        var parent = action.ParentActionId is { } parentActionId
            ? _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u => u.Action?.ActionId == parentActionId)
            : legacy is null ? null : _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u => u.Id == legacy.CardUseFrameId && u.CardKind == legacy.ParentKind);
        if (parent is null) return false;
        if (action.EffectiveKind == CardKind.Dodge && parent.CardAttack is { } attack &&
            IsSlashCard(attack.EffectiveCardKind ?? parent.CardKind) && attack.TargetSeat == action.ActorSeat && attack.SourceSeat == action.OpponentSeat)
            kind = OffTurnUsedCardKind.DodgeUse;
        else if (action.EffectiveKind == CardKind.Nullification && ActiveNullificationWindow is { } pending &&
                 pending.ParentFrameId == parent.Id && pending.SourceSeat == action.OpponentSeat)
            kind = OffTurnUsedCardKind.NullificationUse;
        else return false;
        if (legacy is not null && legacy.Kind != kind) return false;
        useId = parent.Id; return true;
    }

    private void CaptureOffTurnUsedAction(CardActionContext action, long useId, OffTurnUsedCardKind kind,
        long anchorBatchId, IReadOnlyList<CardMovementRecord> anchorMovements)
    {
        if (OffTurnGiftActualTurnOwner() is not { } actualTurnOwner || !IsValidPlayerSeat(action.ActorSeat) || action.ActorSeat == actualTurnOwner || action.ActorSeat != action.ProviderSeat ||
            action.RequesterSeat is not null || !HasOffTurnUsedCardGiftSource(_players[action.ActorSeat]) || action.PhysicalCards.Count == 0 ||
            action.PhysicalCards.Any(c => c.CardId <= 0) ||
            action.PhysicalCards.Select(c => c.CardId).Distinct().Count() != action.PhysicalCards.Count ||
            action.PhysicalCards.Any(c => _cardZones.GetLocation(c.CardId) == CardLocation.Processing) ||
            !CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Any(e => e.Action.ActionId == action.ActionId &&
                e.Action.PhysicalCards.SequenceEqual(action.PhysicalCards))) return;
        var entities = new List<OffTurnUsedDiscardEntity>();
        foreach (var cost in action.PhysicalCards)
        {
            var movement = _cardMovements.LastOrDefault(m => m.CardId == cost.CardId);
            if (movement is null || movement.TurnNumber != _turnNumber || movement.CardKind != cost.CardKind || !IsOffTurnNativeCompletion(movement, kind) ||
                _cardZones.GetLocation(cost.CardId) != CardLocation.DiscardPile) continue;
            var nativeBatchId = anchorMovements.Contains(movement) ? anchorBatchId :
                _pendingCardsMovedBatches.LastOrDefault(b => b.Movements.Contains(movement))?.Id;
            if (nativeBatchId is null) continue;
            entities.Add(new(cost.CardId, cost.CardKind, cost.From, nativeBatchId.Value, movement.Sequence));
        }
        if (entities.Count == 0 || !entities.Any(e => e.NativeBatchId == anchorBatchId)) return;
        foreach (var candidate in CollectProgramTriggerCandidates(_players[action.ActorSeat], SkillProgramTriggerWindow.DiscardPileReceived)
                     .Where(c => GetProgramTrigger(c).Effects.Any(e => e.Op == SkillProgramEffectOp.GiveOffTurnUsedCards)))
        {
            var source = new CardConversionSource(candidate.SkillId, candidate.BindingId, candidate.OwnerSeat, candidate.SkillInstanceId);
            if (CompleteProgramEventHistory().OfType<OffTurnUsedMaterialDiscardedEvent>()
                .Any(e => e.ActionId == action.ActionId && e.Source == source)) continue;
            foreach (var entity in entities)
                AdvanceEventRulesAndQueueFact(new OffTurnUsedMaterialDiscardedEvent(anchorBatchId, useId, action.ActionId,
                    action.ActorSeat, action.ProviderSeat, action.EffectiveKind, kind, _turnNumber, actualTurnOwner,
                    source, candidate.GameplayHash, entity));
        }
    }

    private OffTurnUsedMaterialDiscardedEvent[] OffTurnUsedFacts(long batchId, CardConversionSource source, string hash) =>
        CompleteProgramEventHistory().OfType<OffTurnUsedMaterialDiscardedEvent>()
            .Where(e => e.AnchorBatchId == batchId && e.Source == source && e.GameplayHash == hash)
            .OrderBy(e => e.Entity.MovementSequence).ToArray();

    private bool OffTurnUsedEntityStillAvailable(OffTurnUsedDiscardEntity e) =>
        _cardZones.GetLocation(e.CardId) == CardLocation.DiscardPile &&
        _cardMovements.LastOrDefault(m => m.CardId == e.CardId) is { } last && last.Sequence == e.MovementSequence;

    private int[] MatchingOffTurnUsedCardIndexes(CardMovementBatchContext batch, ProgramTriggerCandidate c)
    {
        var facts = OffTurnUsedFacts(batch.Id, new(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId), c.GameplayHash);
        if (!facts.Any(e => e.ActorSeat == c.OwnerSeat && e.ActualTurnNumber == batch.TurnNumber &&
            e.ActualTurnOwnerSeat != c.OwnerSeat && OffTurnUsedEntityStillAvailable(e.Entity))) return [];
        return batch.Movements.Select((m, i) => (m, i)).Where(x => facts.Any(e => e.Entity.NativeBatchId == batch.Id &&
            e.Entity.MovementSequence == x.m.Sequence)).Select(x => x.i).ToArray();
    }

    private int[] OffTurnGiftRecipients(int owner) => _players.Where(p => p.IsAlive && p.Seat != owner &&
        GetHand(p).Count <= GetHand(_players[owner]).Count).Select(p => p.Seat).ToArray();

    private bool CanOfferOffTurnUsedCards(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (!GetProgramTrigger(c).Effects.Any(e => e.Op == SkillProgramEffectOp.GiveOffTurnUsedCards)) return true;
        return context is { Window: SkillProgramTriggerWindow.DiscardPileReceived, MovementBatch: { } b } &&
            b.TurnNumber == _turnNumber && OffTurnGiftActualTurnOwner() is { } turnOwner && turnOwner != c.OwnerSeat && _players[c.OwnerSeat].IsAlive &&
            OffTurnUsedFacts(b.Id, new(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId), c.GameplayHash).All(e => e.ActualTurnOwnerSeat == turnOwner) &&
            MatchingOffTurnUsedCardIndexes(b, c).Length > 0 && OffTurnGiftRecipients(c.OwnerSeat).Length > 0;
    }

    private SkillProgramStepOutcome BeginOffTurnUsedCardGift(ProgramSkillFrame f)
    {
        if (f.OffTurnUsedCardGift is not null || f.WindowContext is not { MovementBatch: { } batch } context ||
            !CanOfferOffTurnUsedCards(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, 0), context))
            throw new InvalidOperationException("An off-turn used-card gift requires its unpaid native discard opportunity.");
        var source = new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
        var facts = OffTurnUsedFacts(batch.Id, source, f.GameplayHash); var first = facts[0];
        var receipt = new ProgramOffTurnUsedCardGiftReceipt(f.InstructionIndex, context.ParentFrameId, batch.Id,
            first.CardUseFrameId, first.ActionId, first.EffectiveKind, first.Kind, first.ActualTurnNumber, first.ActualTurnOwnerSeat,
            source, f.GameplayHash, facts.Select(e => e.Entity).ToArray(), OffTurnGiftRecipients(f.OwnerSeat), OffTurnUsedCardGiftStage.ChoosingRecipient);
        ReplaceRuntimeTop(f = f with { OffTurnUsedCardGift = receipt });
        AdvanceEventRulesAndQueueFact(new OffTurnUsedCardGiftStartedEvent(f.Id, receipt.WindowFrameId, batch.Id,
            receipt.CardUseFrameId, receipt.ActionId, source, f.GameplayHash, receipt.OriginalEntities.Count));
        var choices = OffTurnUsedCardGiftChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "选择手牌数不大于你的其他角色，获得本次使用后仍在弃牌堆中的牌。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), receipt.CandidateSeats, f.OwnerSeat)
        { PromptId = CreatePromptId(), Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> OffTurnUsedCardGiftChoices(ProgramSkillFrame f)
    {
        var r = f.OffTurnUsedCardGift!;
        var cards = r.OriginalEntities.Where(OffTurnUsedEntityStillAvailable).Select(e => e.CardId).ToArray();
        return Array.AsReadOnly(r.CandidateSeats.Select(seat => new PromptChoice(new($"off-turn-used-card-gift.frame-{f.Id}.seat-{seat}"),
            $"将本次使用后仍在弃牌堆中的牌交给 {_players[seat].Name}。", cards, [seat],
            new Dictionary<string, string> { ["program-action"] = "off-turn-used-card-gift", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) })).ToArray());
    }

    private void ResolveOffTurnUsedCardGift(PromptChoice selected)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Off-turn gift lost its exact program.");
        AssertOffTurnUsedCardGift(f); var r = f.OffTurnUsedCardGift!;
        if (r.Stage != OffTurnUsedCardGiftStage.ChoosingRecipient || _pendingDecision is not { Kind: DecisionKind.ProgramTrigger } p ||
            p.PlayerSeat != f.OwnerSeat || !OffTurnUsedCardGiftChoices(f).Any(c => c.Id == selected.Id && c.Cards.SequenceEqual(selected.Cards) &&
                c.Targets.SequenceEqual(selected.Targets) && c.Parameters.OrderBy(x => x.Key).SequenceEqual(selected.Parameters.OrderBy(x => x.Key))))
            throw new InvalidOperationException("Off-turn gift requires its exact published cards and recipient.");
        ClearPendingDecision(); var recipient = selected.Targets.Single();
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            !OffTurnGiftRecipients(f.OwnerSeat).Contains(recipient) || selected.Cards.Count == 0 || _turnNumber != r.ActualTurnNumber || OffTurnGiftActualTurnOwner() != r.ActualTurnOwnerSeat)
        { CancelProgramBindingAndCleanup(f, "本次使用的弃牌、技能来源或赠牌目标已失效。"); return; }
        var before = _cardMovements.LastOrDefault()?.Sequence ?? 0;
        ReplaceRuntimeTop(f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null), OffTurnUsedCardGift = r with
            { Stage = OffTurnUsedCardGiftStage.GiftChildren, RecipientSeat = recipient, PaidCardIds = selected.Cards, Before = before } });
        MoveProgramCardsFromMultipleSources(selected.Cards, CardLocation.Hand(recipient), new($"skill-program.{f.SkillId}.off-turn-used-card-gift"),
            (batchId, moves) =>
            {
                var active = GetActiveProgramFrame(f.Id);
                ReplaceRuntimeTop(active with { OffTurnUsedCardGift = active.OffTurnUsedCardGift! with
                    { GiftBatchId = batchId, After = moves.Last().Sequence } });
                AdvanceEventRulesAndQueueFact(new OffTurnUsedCardGiftPaidEvent(f.Id, r.ActionId, recipient, selected.Cards.Count, batchId, before, moves.Last().Sequence));
            });
        AdvanceRuntimeProgram(f.Id);
    }

    private bool ResumeOffTurnUsedCardGift(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.OffTurnUsedCardGift is not { } r) return false;
        AssertOffTurnUsedCardGift(f);
        if (r.Stage == OffTurnUsedCardGiftStage.ChoosingRecipient) return true;
        if (TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) ||
            TryBeginAdvancedSkillsChanged(f.Id)) return true;
        f = GetActiveProgramFrame(id);
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        AdvanceEventRulesAndQueueFact(new OffTurnUsedCardGiftResolvedEvent(f.Id, r.ActionId, r.RecipientSeat!.Value, r.PaidCardIds!.Count, r.GiftBatchId));
        FinishProgramSkill(f, true); return true;
    }

    private bool ReturnOffTurnUsedCardGiftMovement(ProgramSkillFrame f)
    {
        if (f.OffTurnUsedCardGift is null) return false;
        if (f.OffTurnUsedCardGift.Stage != OffTurnUsedCardGiftStage.GiftChildren || f.PendingMovementContinuation is null)
            throw new InvalidOperationException("Off-turn gift returned without its already-paid movement receipt.");
        AdvanceRuntimeProgram(f.Id); return true;
    }

    private PromptChoice SelectAiOffTurnUsedCardGift(PendingDecision p) =>
        p.Choices.OrderBy(c => c.Targets.Single()).ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();

    private void AssertOffTurnUsedCardGift(ProgramSkillFrame f)
    {
        if (f.OffTurnUsedCardGift is not { } r) return;
        var effect = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
        var source = new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
        var facts = OffTurnUsedFacts(r.AnchorBatchId, source, f.GameplayHash);
        if (effect.Op != SkillProgramEffectOp.GiveOffTurnUsedCards || r.InstructionIndex != 1 || f.InstructionIndex != r.InstructionIndex ||
            r.Source != source || r.GameplayHash != f.GameplayHash || r.CardUseFrameId <= 0 || r.ActionId <= 0 ||
            !Enum.IsDefined(r.Stage) || !Enum.IsDefined(r.Kind) ||
            r.ActualTurnOwnerSeat == f.OwnerSeat || !IsValidPlayerSeat(r.ActualTurnOwnerSeat) || r.ActualTurnNumber <= 0 ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.DiscardPileReceived, MovementBatch: { } batch } context ||
            context.OwnerSeat != f.OwnerSeat || context.ParentFrameId != r.WindowFrameId || batch.Id != r.AnchorBatchId || batch.TurnNumber != r.ActualTurnNumber ||
            context.MovementIndex is not { } movementIndex || movementIndex < 0 || movementIndex >= batch.Movements.Count ||
            r.OriginalEntities.Count == 0 || r.OriginalEntities.Select(e => e.CardId).Distinct().Count() != r.OriginalEntities.Count ||
            r.OriginalEntities is not System.Collections.IList { IsReadOnly: true } || r.CandidateSeats is not System.Collections.IList { IsReadOnly: true } ||
            r.CandidateSeats.Count == 0 || r.CandidateSeats.Distinct().Count() != r.CandidateSeats.Count || r.CandidateSeats.Any(s => !IsValidPlayerSeat(s) || s == f.OwnerSeat) ||
            !r.OriginalEntities.SequenceEqual(facts.Select(e => e.Entity)) || facts.Any(e => e.CardUseFrameId != r.CardUseFrameId || e.ActionId != r.ActionId ||
                e.ActorSeat != f.OwnerSeat || e.ProviderSeat != f.OwnerSeat || e.Kind != r.Kind || e.EffectiveKind != r.EffectiveKind ||
                e.ActualTurnNumber != r.ActualTurnNumber || e.ActualTurnOwnerSeat != r.ActualTurnOwnerSeat) ||
            !facts.Any(e => e.Entity.NativeBatchId == batch.Id && batch.Movements.Any(m => m.Sequence == e.Entity.MovementSequence && m.CardId == e.Entity.CardId)) ||
            r.OriginalEntities.Any(e => !_cardMovements.Any(m => m.Sequence == e.MovementSequence && m.CardId == e.CardId && m.TurnNumber == r.ActualTurnNumber && IsOffTurnNativeCompletion(m, r.Kind))) ||
            CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat &&
                e.SkillId == f.SkillId && e.BindingId == f.TriggerId && e.SkillInstanceId == f.SkillInstanceId && e.Window == context.Window) != 1 ||
            CompleteProgramEventHistory().OfType<OffTurnUsedCardGiftStartedEvent>().Count(e => e.FrameId == f.Id && e.WindowFrameId == r.WindowFrameId &&
                e.AnchorBatchId == r.AnchorBatchId && e.CardUseFrameId == r.CardUseFrameId && e.ActionId == r.ActionId && e.Source == source &&
                e.GameplayHash == r.GameplayHash && e.MaterialCount == r.OriginalEntities.Count) != 1)
            throw new InvalidOperationException("Off-turn gift lost its exact native use/discard/source/turn issuance proof.");
        var wi = _resolutionStack.FindIndex(x => x.Id == r.WindowFrameId);
        if (wi < 0 || _resolutionStack[wi] is not CardsMovedTriggerWindowFrame window || window.Batch != batch ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            !MountObserverCandidateMatches(f, window.Candidates[window.CandidateIndex]) || context.OccurrenceIndex != window.Candidates[window.CandidateIndex].OccurrenceIndex ||
            context.MovementIndex != context.OccurrenceIndex || wi + 1 >= _resolutionStack.Count || _resolutionStack[wi + 1].Id != f.Id)
            throw new InvalidOperationException("Off-turn gift lost its exact owning discard-window candidate.");
        if (r.Stage == OffTurnUsedCardGiftStage.ChoosingRecipient)
        {
            if (r.RecipientSeat is not null || r.PaidCardIds is not null || r.GiftBatchId != 0 || r.Before != 0 || r.After != 0 || f.PendingMovementContinuation is not null)
                throw new InvalidOperationException("Off-turn gift contains payment before a published recipient was chosen.");
            if (_resolutionStack.LastOrDefault()?.Id == f.Id && (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } p ||
                p.PlayerSeat != f.OwnerSeat || p.SourceSeat != f.OwnerSeat || p.SkillPrompt?.SkillId != f.SkillId ||
                !p.Choices.Select(c => c.Id).SequenceEqual(OffTurnUsedCardGiftChoices(f).Select(c => c.Id)) ||
                p.Choices.Any(c => !OffTurnUsedCardGiftChoices(f).Any(expected => expected.Id == c.Id && expected.Cards.SequenceEqual(c.Cards) &&
                    expected.Targets.SequenceEqual(c.Targets) && expected.Parameters.OrderBy(x => x.Key).SequenceEqual(c.Parameters.OrderBy(x => x.Key))))))
                throw new InvalidOperationException("Off-turn gift lost its exact published public-discard and private-chooser prompt.");
            return;
        }
        if (r.RecipientSeat is not { } recipient || !r.CandidateSeats.Contains(recipient) || r.PaidCardIds is not { Count: > 0 } paid ||
            paid is not System.Collections.IList { IsReadOnly: true } || paid.Distinct().Count() != paid.Count || r.GiftBatchId <= 0 || r.After <= r.Before ||
            paid.Any(id => !r.OriginalEntities.Any(e => e.CardId == id)) ||
            f.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending || pending.SubjectSeat != f.OwnerSeat ||
            !_cardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= r.After).Select(m => m.CardId).SequenceEqual(paid) ||
            _cardMovements.Any(m => m.Sequence > r.Before && m.Sequence <= r.After &&
                (m.From != CardLocation.DiscardPile || m.To != CardLocation.Hand(recipient) || m.Reason.Value != $"skill-program.{f.SkillId}.off-turn-used-card-gift")) ||
            paid.Any(id => _cardMovements.Any(m => m.CardId == id && m.Sequence > r.OriginalEntities.Single(e => e.CardId == id).MovementSequence &&
                m.Sequence <= r.Before && m.From == CardLocation.DiscardPile)) ||
            CompleteProgramEventHistory().OfType<OffTurnUsedCardGiftPaidEvent>().Count(e => e.FrameId == f.Id && e.ActionId == r.ActionId && e.RecipientSeat == recipient &&
                e.CardCount == paid.Count && e.GiftBatchId == r.GiftBatchId && e.SequenceBefore == r.Before && e.SequenceAfter == r.After) != 1)
            throw new InvalidOperationException("Off-turn gift lost its one atomic physical payment and immutable native child return.");
    }

    private sealed partial class ProgramSkillHost : IOffTurnUsedCardsProgramHost
    {
        public SkillProgramStepOutcome GiveOffTurnUsedCards(ProgramSkillFrame f) => engine.BeginOffTurnUsedCardGift(f);
    }
}
