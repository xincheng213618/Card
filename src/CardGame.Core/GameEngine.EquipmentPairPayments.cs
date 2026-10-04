namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlyList<(int First, int Second)> PayableEquipmentPairs(int ownerSeat, string skillId, string instanceId)
    {
        var result = new List<(int, int)>();
        var living = _players.Where(p => p.IsAlive).Select(p => p.Seat).Order().ToArray();
        for (var first = 0; first < living.Length; first++)
            for (var second = first + 1; second < living.Length; second++)
                if (IsPayableEquipmentPair(ownerSeat, living[first], living[second], skillId, instanceId)) result.Add((living[first], living[second]));
        return result.AsReadOnly();
    }
    private bool IsPayableEquipmentPair(int ownerSeat, int first, int second, string skillId, string instanceId)
    {
        if (!IsValidPlayerSeat(ownerSeat) || !IsValidPlayerSeat(first) || !IsValidPlayerSeat(second) || first == second ||
            !_players[ownerSeat].IsAlive || !_players[first].IsAlive || !_players[second].IsAlive) return false;
        var a = GetEquipment(_players[first]).Count; var b = GetEquipment(_players[second]).Count;
        var x = Math.Abs(a - b); var lost = Math.Max(0, _players[ownerSeat].MaxHp - _players[ownerSeat].Hp);
        return a + b > 0 && (x <= lost || LegalEquipmentPairCostCount(ownerSeat, skillId, instanceId) >= x);
    }
    private int LegalEquipmentPairCostCount(int ownerSeat, string skillId, string instanceId) =>
        GetHand(_players[ownerSeat]).Count + GetEquipment(_players[ownerSeat]).Count(c =>
            !IsForeignEquipmentDiscardPrevented(ownerSeat, c, CardLocation.Equipment(ownerSeat), OwnedCardMoveIntent.Discard) &&
            !IsActiveProgramSourceEquipmentCard(ownerSeat, skillId, instanceId, c));
    private bool MatchesEquipmentPairOwnedCost(ProgramSkillFrame frame, string bind, Card card, CardLocation from) =>
        frame.EquipmentPairPayment is not { } paid || paid.ResultBind != bind ||
        from.OwnerSeat == frame.OwnerSeat && (from.Zone is CardZoneKind.Hand or CardZoneKind.Equipment) &&
        !IsForeignEquipmentDiscardPrevented(frame.OwnerSeat, card, from, OwnedCardMoveIntent.Discard) &&
        !(from.Zone == CardZoneKind.Equipment && IsActiveProgramSourceEquipmentCard(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId, card));
    private bool CanActivateEquipmentPairPayment(CharacterState owner, string skillId, ProgramInstructionFeatures features) =>
        !features.HasOperation(SkillProgramEffectOp.SelectEquipmentPairAndPayment) ||
        PayableEquipmentPairs(owner.Seat, skillId, GetRuntimeSkillInstanceId(owner, skillId)).Count > 0;

    private SkillProgramStepOutcome SelectEquipmentPairAndPayment(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        frame = GetActiveProgramFrame(frame.Id);
        if (frame.TriggerId is not null || frame.WindowContext is not null || frame.InstructionIndex != 1 ||
            frame.EquipmentPairPayment is not null || frame.SelectedTargetSeats.Count != 0)
            throw new InvalidOperationException("A paid equipment pair requires its fresh active selection instruction.");
        var choices = PayableEquipmentPairs(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId).Select(pair => new PromptChoice(
            new ChoiceId($"equipment-pair.frame-{frame.Id}.first-{pair.First}.second-{pair.Second}"),
            $"选择{_players[pair.First].Name}与{_players[pair.Second].Name}", [], [pair.First, pair.Second],
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>
            { ["program-action"] = "equipment-pair-payment", ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) }))).ToArray();
        if (choices.Length == 0) { CancelProgramBindingAndCleanup(frame, "没有可支付的装备交换对象。"); return SkillProgramStepOutcome.AwaitChild; }
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTrigger, frame.OwnerSeat, "请选择两名角色；选定时冻结装备差及所需弃牌。", [],
            choices.SelectMany(c => c.Targets).Distinct().Order().ToArray(), SourceSeat: frame.OwnerSeat)
        { PromptId = CreatePromptId(), Choices = Array.AsReadOnly(choices), SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private void ResolveEquipmentPairPaymentChoice(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.InstructionIndex != 1 || frame.EquipmentPairPayment is not null ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision || decision.PlayerSeat != frame.OwnerSeat ||
            choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            choice.Cards.Count != 0 || choice.Targets is not [var first, var second])
            throw new InvalidOperationException("The equipment pair choice lost its exact active program.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.SelectEquipmentPairAndPayment)
            throw new InvalidOperationException("The equipment pair choice belongs to another instruction.");
        ClearPendingDecision();
        if (_winner != Winner.None || !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) ||
            !IsPayableEquipmentPair(frame.OwnerSeat, first, second, frame.SkillId, frame.SkillInstanceId))
        { CancelProgramBindingAndCleanup(frame, "交换对象或足额付款已失效，尚未移动牌。"); return; }
        var a = GetEquipment(_players[first]).Count; var b = GetEquipment(_players[second]).Count;
        var lost = Math.Max(0, _players[frame.OwnerSeat].MaxHp - _players[frame.OwnerSeat].Hp);
        var required = Math.Abs(a - b) > lost ? Math.Abs(a - b) : 0;
        var before = _cardMovements.Count == 0 ? 0 : _cardMovements[^1].Sequence;
        var receipt = new ProgramEquipmentPairPaymentReceipt(0, first, second, a, b, lost, required, effect.ResultBind!, _turnNumber, before);
        ReplaceRuntimeTop(frame = frame with { SelectedTargetSeats = Array.AsReadOnly(new[] { first, second }), EquipmentPairPayment = receipt });
        AdvanceEventRulesAndQueueFact(new ProgramEquipmentPairPaymentFrozenEvent(frame.Id,
            new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId), frame.GameplayHash,
            first, second, a, b, lost, required, effect.ResultBind!, _turnNumber, before));
        var outcome = SelectProgramOwnedCards(frame, frame.OwnerSeat, required, null, [CardZoneKind.Hand, CardZoneKind.Equipment],
            effect.ResultBind!, required, 0, [], []);
        if (outcome == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(frame.Id);
    }
    private SkillProgramEffect EquipmentPairOwnedSelectionEffect(ProgramSkillFrame frame, SkillProgramEffect effect) =>
        effect.Op != SkillProgramEffectOp.SelectEquipmentPairAndPayment ? effect : frame.EquipmentPairPayment is { } paid
            ? new(SkillProgramEffectOp.SelectOwnedCards, SkillProgramEffectTarget.Owner, paid.RequiredPaymentCount, effect.Condition,
                zones: [CardZoneKind.Hand, CardZoneKind.Equipment], resultBind: paid.ResultBind, minimumCards: paid.RequiredPaymentCount)
            : effect;

    private bool IsIssuedEquipmentPairPayment(ProgramSkillFrame frame)
    {
        if (frame.EquipmentPairPayment is not { InstructionIndex: 0 } p || frame.TriggerId is not null || frame.WindowContext is not null ||
            frame.InstructionIndex is < 1 or > 4 || p.ActualTurnNumber != _turnNumber || frame.OwnerSeat != _currentSeat ||
            frame.SelectedTargetSeats.Count != 2 || frame.SelectedTargetSeats[0] != p.FirstSeat || frame.SelectedTargetSeats[1] != p.SecondSeat ||
            !IsValidPlayerSeat(p.FirstSeat) || !IsValidPlayerSeat(p.SecondSeat) || p.FirstSeat == p.SecondSeat ||
            p.FirstEquipmentCount < 0 || p.SecondEquipmentCount < 0 || p.FirstEquipmentCount + p.SecondEquipmentCount == 0 || p.OwnerLostHp < 0 ||
            p.RequiredPaymentCount != (Math.Abs(p.FirstEquipmentCount - p.SecondEquipmentCount) > p.OwnerLostHp ? Math.Abs(p.FirstEquipmentCount - p.SecondEquipmentCount) : 0) ||
            p.MovementSequenceBefore < 0) return false;
        var program = _contentRegistry.GetSkill(frame.SkillId).Program;
        if (program is null || program.GameplayHash != frame.GameplayHash) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(frame, program);
        if (plan.Instructions.Count != 4 || plan.Instructions[0].Op != SkillProgramEffectOp.SelectEquipmentPairAndPayment || plan.Instructions[0].ResultBind != p.ResultBind) return false;
        return CompleteProgramEventHistory().OfType<ProgramEquipmentPairPaymentFrozenEvent>().Any(e => e.FrameId == frame.Id &&
            e.Source == new CardConversionSource(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId) && e.GameplayHash == frame.GameplayHash &&
            e.FirstSeat == p.FirstSeat && e.SecondSeat == p.SecondSeat && e.FirstEquipmentCount == p.FirstEquipmentCount && e.SecondEquipmentCount == p.SecondEquipmentCount &&
            e.OwnerLostHp == p.OwnerLostHp && e.RequiredPaymentCount == p.RequiredPaymentCount && e.ResultBind == p.ResultBind &&
            e.ActualTurnNumber == p.ActualTurnNumber && e.MovementSequenceBefore == p.MovementSequenceBefore);
    }
    private bool EquipmentPairCostIsPaid(ProgramSkillFrame frame)
    {
        if (!IsIssuedEquipmentPairPayment(frame) || frame.EquipmentPairPayment is not { } paid) return false;
        var set = frame.CardSetBindings.SingleOrDefault(b => b.Name == paid.ResultBind);
        if (set is null || set.CardIds.Count != paid.RequiredPaymentCount || set.SourceLocations.Count != set.CardIds.Count ||
            set.SourceLocations.Any(l => l.OwnerSeat != frame.OwnerSeat || l.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment))) return false;
        var reason = $"skill-program.{frame.SkillId}.{SkillProgramEffectOp.MoveBoundCards}";
        return !set.CardIds.Where((id, n) => _cardMovements.Count(m => m.Sequence > paid.MovementSequenceBefore && m.CardId == id &&
            m.From == set.SourceLocations[n] && m.To == OwnedPaymentDiscardDestination(id) && m.Reason.Value == reason) != 1).Any();
    }
    private CardLocation OwnedPaymentDiscardDestination(int cardId) =>
        _cardZones.CardsAt(_cardZones.GetLocation(cardId)).Single(c => c.Id == cardId).IsGeneralWeapon ? CardLocation.OutsideGame : CardLocation.DiscardPile;
    private bool CanResumePaidEquipmentPair(ProgramSkillFrame frame) => EquipmentPairCostIsPaid(frame) &&
        frame.EquipmentPairPayment is { } paid && _winner == Winner.None && _players[frame.OwnerSeat].IsAlive &&
        _players[paid.FirstSeat].IsAlive && _players[paid.SecondSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId);
    private ProgramSkillFrame FreezePaidEquipmentExchangeStart(ProgramSkillFrame frame)
    {
        if (frame.EquipmentPairPayment is not { } p) return frame;
        if (p.ExchangeMovementSequenceBefore is not null) throw new InvalidOperationException("An equipment pair must not exchange twice.");
        var before = _cardMovements.Count == 0 ? 0 : _cardMovements[^1].Sequence;
        ReplaceRuntimeTop(frame = frame with { EquipmentPairPayment = p with { ExchangeMovementSequenceBefore = before } });
        AdvanceEventRulesAndQueueFact(new ProgramEquipmentPairExchangeStartedEvent(frame.Id, p.FirstSeat, p.SecondSeat, before));
        return frame;
    }
    private void AssertEquipmentPairPayment(ProgramSkillFrame frame)
    {
        if (frame.EquipmentPairPayment is not { } paid) return;
        if (!IsIssuedEquipmentPairPayment(frame) || paid.ExchangeMovementSequenceBefore is { } before &&
            (frame.InstructionIndex != 4 || !EquipmentPairCostIsPaid(frame) ||
             !CompleteProgramEventHistory().OfType<ProgramEquipmentPairExchangeStartedEvent>().Any(e => e.FrameId == frame.Id && e.FirstSeat == paid.FirstSeat && e.SecondSeat == paid.SecondSeat && e.MovementSequenceBefore == before)))
            throw new InvalidOperationException("The paid equipment pair lost its exact frozen pair, cost or exchange receipt.");
    }
    private PromptChoice SelectAiEquipmentPairPayment(PendingDecision decision, ProgramSkillFrame frame) =>
        frame.OwnedCardSelection is not null ? SelectAiProgramOwnedCards(decision, frame) : decision.Choices
            .OrderBy(c => Math.Abs(GetEquipment(_players[c.Targets[0]]).Count - GetEquipment(_players[c.Targets[1]]).Count))
            .ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
    private sealed partial class ProgramSkillHost : IEquipmentPairPaymentProgramHost
    {
        public SkillProgramStepOutcome SelectEquipmentPairAndPayment(ProgramSkillFrame f, SkillProgramEffect e) => engine.SelectEquipmentPairAndPayment(f, e);
    }
}
