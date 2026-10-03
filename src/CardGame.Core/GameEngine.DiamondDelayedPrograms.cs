namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool DiamondPaymentLegal(int ownerSeat, Card card, string skillId, string? instanceId = null) =>
        _players[ownerSeat].IsAlive && _cardZones.GetLocation(card.Id) is { OwnerSeat: var seat, Zone: CardZoneKind.Hand or CardZoneKind.Equipment } && seat == ownerSeat &&
        !card.IsGeneralWeapon && !IsActiveProgramSourceEquipmentCard(ownerSeat, skillId, instanceId ?? GetRuntimeSkillInstanceId(_players[ownerSeat], skillId), card) && EffectiveSuit(_players[ownerSeat], card) == Suit.Diamond;

    private bool DiamondUseTargetLegal(int ownerSeat, Card card, int targetSeat, string skillId, string? instanceId = null) =>
        DiamondPaymentLegal(ownerSeat, card, skillId, instanceId) && IsValidPlayerSeat(targetSeat) && targetSeat != ownerSeat &&
        _players[targetSeat].IsAlive && !_players[targetSeat].JudgmentAreaAbolished && !HasJudgmentEffectiveCard(_players[targetSeat], CardKind.Indulgence) &&
        !IsTurnHandCardRestricted(_players[ownerSeat], card) && !IsPlayPhasePhysicalCardRestricted(_players[ownerSeat], card) &&
        !IsCardUseForbidden(ownerSeat, CardKind.Indulgence, CardActionType.Use) &&
        !IsDirectedCardTargetProhibited(ownerSeat, targetSeat, CardKind.Indulgence) &&
        !IsCardTargetProhibited(_players[targetSeat], CardKind.Indulgence, EffectiveSuit(_players[ownerSeat], card), SuitColor(EffectiveSuit(_players[ownerSeat], card))) &&
        !HasBeneficiarySuitShield(ownerSeat, targetSeat, EffectiveSuit(_players[ownerSeat], card));

    private IEnumerable<(int Seat, Card Card)> DiamondJudgments() => _players.Where(p => p.IsAlive)
        .OrderBy(p => p.Seat).SelectMany(p => GetJudgment(p).Where(c => GetJudgmentEffectiveCardKind(c) == CardKind.Indulgence)
            .OrderBy(c => c.Id).Select(c => (p.Seat, c)));

    private bool CanActivateDiamondDelayed(CharacterState owner, SkillProgramActivation activation, string skillId) =>
        !activation.Effects.Any(e => e.Op == SkillProgramEffectOp.UseDiamondDelayedOrDiscard) ||
        GetHand(owner).Concat(GetEquipment(owner)).Where(c => DiamondPaymentLegal(owner.Seat, c, skillId))
            .Any(c => DiamondJudgments().Any() || _players.Any(p => DiamondUseTargetLegal(owner.Seat, c, p.Seat, skillId)));

    private SkillProgramStepOutcome BeginDiamondDelayed(ProgramSkillFrame frame)
    {
        if (frame.TriggerId is not null || frame.SelectedCardIds.Count != 1 || frame.SelectedTargetSeats.Count != 0 ||
            _phase != TurnPhase.Play || _currentSeat != frame.OwnerSeat)
            throw new InvalidOperationException("Diamond delayed use needs one owner payment in actual Play.");
        var card = EntityAtCurrentLocation(frame.SelectedCardIds.Single());
        if (!DiamondPaymentLegal(frame.OwnerSeat, card, frame.SkillId, frame.SkillInstanceId)) throw new InvalidOperationException("Diamond payment is not a real eligible owner entity.");
        ReplaceRuntimeTop(frame = frame with { DiamondDelayed = new(card.Id, _cardZones.GetLocation(card.Id), EffectiveSuit(_players[frame.OwnerSeat], card), _turnNumber, _currentSeat, ProgramDiamondDelayedStage.Choosing) });
        PublishDiamondDelayed(frame);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private IReadOnlyList<PromptChoice> DiamondDelayedChoices(ProgramSkillFrame frame)
    {
        var d = frame.DiamondDelayed!;
        var card = EntityAtCurrentLocation(d.CardId);
        Dictionary<string, string> P(string branch) => new() { ["program-action"] = "diamond-delayed", ["frame-id"] = frame.Id.ToString(), ["branch"] = branch };
        return _players.Where(p => DiamondUseTargetLegal(frame.OwnerSeat, card, p.Seat, frame.SkillId, frame.SkillInstanceId))
            .Select(p => new PromptChoice(new($"diamond-delayed.{frame.Id}.use.{p.Seat}"), $"对 {p.Name} 使用【乐不思蜀】", [], [p.Seat], P("use")))
            .Concat(DiamondJudgments().Select(j => new PromptChoice(new($"diamond-delayed.{frame.Id}.discard.{j.Card.Id}"), $"弃置 { _players[j.Seat].Name } 判定区的【乐不思蜀】", [j.Card.Id], [j.Seat], P("discard"))))
            .ToArray();
    }

    private void PublishDiamondDelayed(ProgramSkillFrame frame)
    {
        var choices = DiamondDelayedChoices(frame);
        if (choices.Count == 0) { CancelProgramBindingAndCleanup(frame, "国色的支付或目标已失效。"); return; }
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat, $"{skill.Name}：选择使用或弃置。", [], [], frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private bool IsDiamondDelayedChoiceLegal(PromptChoice choice)
    {
        if (_winner != Winner.None || _resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.DiamondDelayed is not { Stage: ProgramDiamondDelayedStage.Choosing } d ||
            choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString() || !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) ||
            d.TurnNumber != _turnNumber || d.TurnOwnerSeat != _currentSeat || _phase != TurnPhase.Play || _cardZones.GetLocation(d.CardId) != d.Source ||
            !DiamondPaymentLegal(frame.OwnerSeat, EntityAtCurrentLocation(d.CardId), frame.SkillId, frame.SkillInstanceId)) return false;
        return DiamondDelayedChoices(frame).Any(c => c.Id == choice.Id && c.Cards.SequenceEqual(choice.Cards) && c.Targets.SequenceEqual(choice.Targets) && c.Parameters.OrderBy(p => p.Key).SequenceEqual(choice.Parameters.OrderBy(p => p.Key)));
    }

    private void ResolveDiamondDelayedChoice(PromptChoice choice)
    {
        if (!IsDiamondDelayedChoiceLegal(choice)) throw new InvalidOperationException("The exact diamond payment or target is stale.");
        var frame = (ProgramSkillFrame)_resolutionStack[^1]; var d = frame.DiamondDelayed!; var card = EntityAtCurrentLocation(d.CardId);
        ClearPendingDecision();
        if (choice.Parameters["branch"] == "use")
        {
            var target = choice.Targets.Single();
            ReplaceRuntimeTop(frame = frame with { DiamondDelayed = d with { Stage = ProgramDiamondDelayedStage.Using, TargetSeat = target } });
            var id = BeginCardUse(card, frame.OwnerSeat, [target], CardKind.Indulgence,
                conversionSource: new(frame.SkillId, frame.ActivationId, frame.OwnerSeat, frame.SkillInstanceId));
            ReplaceRuntimeFrame(frame.Id, frame with { DiamondDelayed = frame.DiamondDelayed! with { UseFrameId = id } });
            MoveCard(card, d.Source, CardLocation.Processing, CardMoveReasons.Use);
            BeginJizhiOrNullificationWindow(id, card, frame.OwnerSeat, [target], LegalActionKind.Indulgence, playedCardKind: CardKind.Indulgence);
            return;
        }
        var judgment = EntityAtCurrentLocation(choice.Cards.Single()); var from = _cardZones.GetLocation(judgment.Id);
        ReplaceRuntimeTop(frame = frame with { DiamondDelayed = d with { Stage = ProgramDiamondDelayedStage.Moving, TargetSeat = choice.Targets.Single(), JudgmentCardId = judgment.Id } });
        var batch = BeginCardMovementBatch([d.Source, from], [CardLocation.DiscardPile]);
        var movements = new List<CardMovementRecord>(); var committed = false;
        try
        {
            _cardZones.MoveBatch([new(card.Id, d.Source, CardLocation.DiscardPile), new(judgment.Id, from, CardLocation.DiscardPile)]);
            foreach (var pair in new[] { (Card: card, From: d.Source), (Card: judgment, From: from) })
            {
                movements.Add(RecordMovement(pair.Card, pair.From, CardLocation.DiscardPile, new("skill-program.diamond-delayed.discard")));
                ResolveEquipmentSkillGrant(pair.Card, pair.From, CardLocation.DiscardPile);
                ClearJudgmentEffectiveKindAfterMove(pair.Card, pair.From, CardLocation.DiscardPile);
                ResolveSilverLionRemoval(pair.Card, pair.From, new("skill-program.diamond-delayed.discard"));
                ResolveWoodenOxMove(pair.Card, pair.From, CardLocation.DiscardPile);
                CollectDiscardPhaseHandDiscard(pair.Card, pair.From, CardLocation.DiscardPile);
            }
            committed = true;
        }
        finally { CompleteCardMovementBatch(batch, movements, committed); }
        if (AwaitProgramBoundCardMovements(frame.Id, frame.OwnerSeat) == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(frame.Id);
    }

    private void ContinueProgramAfterDiamondDelayedUse(long completedUseId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.DiamondDelayed is not { Stage: ProgramDiamondDelayedStage.Using } d) return;
        if (d.UseFrameId != completedUseId || !CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Any(e => e.ResolutionId == completedUseId && e.CardId == d.CardId && e.CardKind == CardKind.Indulgence))
            throw new InvalidOperationException("Diamond delayed use returned from a different child.");
        ReplaceRuntimeTop(frame = frame with { DiamondDelayed = d with { Stage = ProgramDiamondDelayedStage.Moving } });
        if (AwaitProgramBoundCardMovements(frame.Id, frame.OwnerSeat) == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(frame.Id);
    }

    private bool ResumeDiamondDelayed(long frameId)
    {
        var frame = GetActiveProgramFrame(frameId); if (frame.DiamondDelayed is not { } d) return false;
        if (d.Stage == ProgramDiamondDelayedStage.Using || frame.PendingMovementContinuation is not null) return true;
        if (_winner != Winner.None || !_players[frame.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
        { CancelProgramBindingAndCleanup(frame, "国色的持有人或来源已失效。"); return true; }
        if (d.Stage == ProgramDiamondDelayedStage.Choosing)
        {
            if (_cardZones.GetLocation(d.CardId) != d.Source || !DiamondPaymentLegal(frame.OwnerSeat, EntityAtCurrentLocation(d.CardId), frame.SkillId, frame.SkillInstanceId)) CancelProgramBindingAndCleanup(frame, "国色锁定的实体已失效。");
            else PublishDiamondDelayed(frame);
            return true;
        }
        if (d.Stage == ProgramDiamondDelayedStage.Moving)
        {
            ReplaceRuntimeTop(frame = frame with { DiamondDelayed = d with { Stage = ProgramDiamondDelayedStage.Drawing } });
            DrawCards(_players[frame.OwnerSeat], 1, log: true);
            if (AwaitProgramBoundCardMovements(frame.Id, frame.OwnerSeat) == SkillProgramStepOutcome.AwaitChild) return true;
            frame = GetActiveProgramFrame(frameId);
        }
        ReplaceRuntimeTop(frame with { DiamondDelayed = null }); AdvanceRuntimeProgram(frame.Id); return true;
    }

    private void AssertDiamondDelayed(ProgramSkillFrame frame, SkillProgramEffect? paused)
    {
        if (frame.DiamondDelayed is not { } d) return;
        if (paused?.Op != SkillProgramEffectOp.UseDiamondDelayedOrDiscard || frame.TriggerId is not null || frame.SelectedCardIds.Count != 1 ||
            frame.SelectedCardIds[0] != d.CardId || d.Source.OwnerSeat != frame.OwnerSeat || d.Source.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
            d.EffectiveSuit != Suit.Diamond || d.TurnNumber != _turnNumber || d.TurnOwnerSeat != _currentSeat || _phase != TurnPhase.Play || !Enum.IsDefined(d.Stage) ||
            d.Stage == ProgramDiamondDelayedStage.Using && (d.UseFrameId is null || !_resolutionStack.OfType<CardUseFrame>().Any(c => c.Id == d.UseFrameId && c.SourceSeat == frame.OwnerSeat && c.CardId == d.CardId && c.CardKind == CardKind.Indulgence && _resolutionStack.IndexOf(frame) + 1 == _resolutionStack.IndexOf(c) && c.Action is { } a && a.PhysicalCards.Count == 1 && a.PhysicalCards[0].CardId == d.CardId && a.PhysicalCards[0].From == d.Source && a.ActorSeat == frame.OwnerSeat && a.EffectiveSuit == d.EffectiveSuit && a.ConversionChain.SequenceEqual([new CardConversionSource(frame.SkillId, frame.ActivationId, frame.OwnerSeat, frame.SkillInstanceId)]))))
            throw new InvalidOperationException("Diamond delayed draft lost its exact owning operation or child.");
        if (d.Stage == ProgramDiamondDelayedStage.Choosing && ReferenceEquals(frame, _resolutionStack.LastOrDefault()) &&
            (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } prompt || prompt.PlayerSeat != frame.OwnerSeat || prompt.Choices.Count == 0 || prompt.Choices.Any(c => !IsDiamondDelayedChoiceLegal(c))))
            throw new InvalidOperationException("Diamond delayed draft lost its exact choice.");
    }
    private sealed partial class ProgramSkillHost : IDiamondDelayedProgramHost
    { public SkillProgramStepOutcome UseDiamondDelayedOrDiscard(ProgramSkillFrame frame) => engine.BeginDiamondDelayed(frame); }
}
