namespace CardGame.Core;

public sealed record ProgramSkillSuppressedEvent(int OwnerSeat, int TargetSeat, string SkillId, bool Suppressed) : IGameEvent;
public sealed partial class GameEngine
{
    private sealed record StrategicDraft(SkillProgramEffectOp Op, int TargetSeat, string? Bind,
        IReadOnlyList<int> Candidates, IReadOnlyList<int> Selected, int Maximum);
    private sealed record ProgramSuppression(int SourceSeat, int TargetSeat, string SkillId, int CreatedTurn,
        IReadOnlyList<string> GrantIds);
    private readonly Dictionary<long, StrategicDraft> _strategicDrafts = [];
    private readonly Dictionary<(long Frame, int Instruction), Queue<int>> _strategicDamageQueues = [];
    private readonly List<ProgramSuppression> _programSuppressions = [];
    private int _strategicEndPlayTurn = -1;
    private readonly Dictionary<int, int> _strategicHandLimitPenaltyTurns = [];

    private bool UsesStrategicTriggerValue(SkillProgramTriggerValueKind kind) => _contentRegistry.Skills.Values.Any(skill =>
        skill.Program?.Triggers.Any(trigger => ContainsStrategicValue(trigger.Condition, kind)) == true);
    private static bool ContainsStrategicValue(SkillProgramTriggerCondition condition, SkillProgramTriggerValueKind kind) =>
        condition.Left?.Kind == kind || condition.Right?.Kind == kind || condition.Children.Any(child => ContainsStrategicValue(child, kind));

    private IReadOnlyDictionary<string, bool>? GetPublicProgramBooleanStates(CharacterState player)
    {
        var values = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var grant in player.SkillGrants.Grants.Where(grant => grant.IsEnabled))
        {
            var program = _contentRegistry.Skills.GetValueOrDefault(grant.SkillId)?.Program;
            if (program is null) continue;
            foreach (var definition in program.BooleanStates.Where(state => state.Visibility == SkillProgramStateVisibility.Public))
                values[definition.Id] = GetProgramBooleanState(player.Seat, grant.SkillId, grant.SkillInstanceId, definition.Id);
        }
        return values.Count > 0 ? values : null;
    }
    private IReadOnlyDictionary<string, int>? GetProgramPublicCounters(CharacterState player) =>
        _contentRegistry.Skills.Values.Any(skill => skill.Program?.CardPolicies.Any(policy => (int)policy.Kind >= 450) == true)
        ? new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["junlue"] = player.Markers.GetValueOrDefault(PlayerMarkerKind.Junlue),
            ["camp"] = player.Markers.GetValueOrDefault(PlayerMarkerKind.Camp),
            ["woundedPlayers"] = _players.Count(other => other.IsAlive && other.Hp < other.MaxHp)
        } : null;
    private void ResetPlayPhaseProgramStates(int ownerSeat)
    {
        foreach (var key in _programBooleanStates.Keys.Where(key => key.OwnerSeat == ownerSeat).ToArray())
        {
            var definition = GetProgramBooleanStateDefinition(key.SkillId, key.StateId);
            if (definition.ResetScope == SkillProgramStateResetScope.PlayPhase) _programBooleanStates[key] = definition.InitialValue;
        }
    }
    private IEnumerable<RuleQueryContribution> CollectStrategicRuleContributions(CharacterState player, SkillRuleQuery query)
    {
        if (query == SkillRuleQuery.HandLimit && _strategicHandLimitPenaltyTurns.GetValueOrDefault(player.Seat, -1) == _turnNumber)
            yield return new FiniteRuleQueryContribution($"turn:{_turnNumber}:hand-discard-share:{player.Seat}", SkillRuleOperation.Add, -1);
        var wounded = _players.Count(other => other.IsAlive && other.Hp < other.MaxHp);
        foreach (var source in _players.Where(source => source.IsAlive))
        foreach (var instance in GetSkillBindingShard(source).ProgramInstances)
        foreach (var policy in instance.Program.CardPolicies)
        {
            if (!policy.Condition.Evaluate(CreateSkillContext(source))) continue;
            var value = policy.Kind switch
            {
                SkillProgramCardPolicyKind.ChainedHandLimitAura when query == SkillRuleQuery.HandLimit && player.IsChained => 2,
                SkillProgramCardPolicyKind.MarkerTurnBonuses when player.MarkerSourceCounts.GetValueOrDefault((PlayerMarkerKind.Camp, source.Seat)) > 0 && query is SkillRuleQuery.DrawCount or SkillRuleQuery.SlashLimit or SkillRuleQuery.HandLimit => 1,
                SkillProgramCardPolicyKind.WoundedPopulationBonuses when source.Seat == player.Seat && query == SkillRuleQuery.HandLimit && wounded >= 1 => 1,
                SkillProgramCardPolicyKind.WoundedPopulationBonuses when source.Seat == player.Seat && query == SkillRuleQuery.DrawCount && wounded >= 3 => 1,
                SkillProgramCardPolicyKind.WoundedInRangeHandLimitPenalty when query == SkillRuleQuery.HandLimit && source.Seat != player.Seat && player.Hp < player.MaxHp && IsWithinAttackRange(source.Seat, player.Seat) => -1,
                _ => 0
            };
            if (value != 0) yield return new FiniteRuleQueryContribution($"aura:{source.Seat}:{instance.SkillId}:{instance.SkillInstanceId}:{policy.Id}", SkillRuleOperation.Add, value);
        }
    }
    private void SetStrategicMarker(int seat, PlayerMarkerKind marker, int sourceSeat, int count, ProgramSkillFrame frame)
    {
        var player = _players[seat];
        var old = player.Markers.GetValueOrDefault(marker);
        foreach (var key in player.MarkerSourceCounts.Keys.Where(key => key.Marker == marker).ToArray()) player.MarkerSourceCounts.Remove(key);
        if (count == 0) player.Markers.Remove(marker);
        else { player.Markers[marker] = count; player.MarkerSourceCounts[(marker, sourceSeat)] = count; }
        QueueGameEvent(new PlayerMarkerChangedEvent(frame.Id, seat, marker, count - old, count, sourceSeat, $"skill-program.{frame.SkillId}.marker"));
    }
    private SkillProgramStepOutcome ExecuteStrategicProgramEffect(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat)
    {
        var owner = _players[frame.OwnerSeat];
        var host = new ProgramSkillHost(this);
        var reason = new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}");
        switch (effect.Op)
        {
            case SkillProgramEffectOp.SetMarkerAmount:
                var marker = effect.Marker ?? throw new InvalidOperationException("Marker operation requires a marker.");
                if (effect.Amount < 0) SetStrategicMarker(targetSeat, marker, frame.OwnerSeat, 0, frame);
                else if (effect.Amount > 0)
                {
                    var holder = _players[targetSeat];
                    var source = (marker, frame.OwnerSeat);
                    var aggregate = checked(holder.Markers.GetValueOrDefault(marker) + effect.Amount);
                    holder.MarkerSourceCounts[source] = checked(holder.MarkerSourceCounts.GetValueOrDefault(source) + effect.Amount);
                    holder.Markers[marker] = aggregate;
                    QueueGameEvent(new PlayerMarkerChangedEvent(frame.Id, targetSeat, marker, effect.Amount, aggregate, frame.OwnerSeat, $"skill-program.{frame.SkillId}.marker"));
                }
                return SkillProgramStepOutcome.Continue;
            case SkillProgramEffectOp.MoveUniqueMarker:
                marker = effect.Marker ?? throw new InvalidOperationException("Marker transfer requires a marker.");
                if (!owner.Markers.ContainsKey(marker)) return SkillProgramStepOutcome.Continue;
                foreach (var holder in _players.Where(player => player.Markers.GetValueOrDefault(marker) > 0).ToArray()) SetStrategicMarker(holder.Seat, marker, frame.OwnerSeat, 0, frame);
                SetStrategicMarker(targetSeat, marker, frame.OwnerSeat, 1, frame);
                return SkillProgramStepOutcome.Continue;
            case SkillProgramEffectOp.ClaimMarkedHand:
                marker = effect.Marker ?? throw new InvalidOperationException("Marked hand claim requires a marker.");
                var turnOwner = _players[_currentSeat];
                if (turnOwner.Seat != owner.Seat && turnOwner.MarkerSourceCounts.GetValueOrDefault((marker, owner.Seat)) > 0)
                {
                    MoveCards(GetHand(turnOwner).ToArray(), CardLocation.Hand(turnOwner.Seat), CardLocation.Hand(owner.Seat), reason);
                    SetStrategicMarker(turnOwner.Seat, marker, owner.Seat, 0, frame);
                    SetStrategicMarker(owner.Seat, marker, owner.Seat, 1, frame);
                }
                return SkillProgramStepOutcome.Continue;
            case SkillProgramEffectOp.EndCurrentPlay:
                _strategicEndPlayTurn = _turnNumber;
                return SkillProgramStepOutcome.Continue;
            case SkillProgramEffectOp.DiscardTargetEquipment:
                foreach (var seat in frame.SelectedTargetSeats) MoveCards(GetEquipment(seat).ToArray(), CardLocation.Equipment(seat), CardLocation.DiscardPile, reason);
                return SkillProgramStepOutcome.Continue;
            case SkillProgramEffectOp.DamageOtherLiving:
                var key = (frame.Id, frame.InstructionIndex);
                if (!_strategicDamageQueues.TryGetValue(key, out var queue))
                    _strategicDamageQueues[key] = queue = new Queue<int>(_players.Where(player => player.IsAlive && player.Seat != owner.Seat).Select(player => player.Seat));
                while (queue.Count > 0 && !_players[queue.Peek()].IsAlive) queue.Dequeue();
                if (queue.Count == 0) { _strategicDamageQueues.Remove(key); return SkillProgramStepOutcome.Continue; }
                var damageSeat = queue.Dequeue();
                // The automatic instruction repeats until all frozen seats have resolved their complete damage windows.
                _resolutionStack[^1] = frame with { InstructionIndex = frame.InstructionIndex - 1 };
                return BeginProgramSkillDamage(frame, damageSeat, effect.Amount == 0 ? 1 : effect.Amount);
            case SkillProgramEffectOp.SelectDistinctSuitHandDiscards:
                var candidates = GetHand(owner).Concat(GetHand(_players[targetSeat])).Select(card => card.Id).ToArray();
                _strategicDrafts[frame.Id] = new(effect.Op, targetSeat, effect.ResultBind, candidates, [], 4);
                PublishStrategicPrompt(frame);
                return SkillProgramStepOutcome.AwaitChoice;
            case SkillProgramEffectOp.ApplyHandDiscardShare:
                var binding = GetProgramCardSet(frame, effect.SourceBind!);
                if (binding.CardIds.Count != 4) return SkillProgramStepOutcome.Continue;
                var mine = binding.SourceLocations.Count(location => location.OwnerSeat == frame.OwnerSeat);
                foreach (var group in binding.CardIds.Select((id, index) => (id, Location: binding.SourceLocations[index])).GroupBy(item => item.Location))
                    MoveCards(group.Select(item => _cardZones.CardsAt(item.Location).Single(card => card.Id == item.id)).ToArray(), group.Key, CardLocation.DiscardPile, reason);
                if (mine == 0) host.ChangeMaximumHp(frame, -1);
                if (mine == 1) { _strategicHandLimitPenaltyTurns[owner.Seat] = _turnNumber; _strategicEndPlayTurn = _turnNumber; }
                if (mine == 3) host.Recover(frame.Id, owner.Seat, owner.Seat, 1, null, null);
                if (mine == 4) host.Draw(frame.Id, owner.Seat, owner.Seat, 4, null, null, SkillProgramCardSetVisibility.Private, reason);
                return SkillProgramStepOutcome.Continue;
            case SkillProgramEffectOp.SuppressGeneralSkill:
                if (_programSuppressions.Any(item => item.SourceSeat == owner.Seat && item.TargetSeat == targetSeat)) return SkillProgramStepOutcome.Continue;
                _strategicDrafts[frame.Id] = new(effect.Op, targetSeat, null, [], [], 1);
                PublishStrategicPrompt(frame);
                return _pendingDecision is null ? SkillProgramStepOutcome.Continue : SkillProgramStepOutcome.AwaitChoice;
            case SkillProgramEffectOp.SelectChainedByMarker:
                var count = owner.Markers.GetValueOrDefault(effect.Marker!.Value);
                _strategicDrafts[frame.Id] = new(effect.Op, -1, null, _players.Where(player => player.IsAlive && player.IsChained).Select(player => player.Seat).ToArray(), [], count);
                SetStrategicMarker(owner.Seat, effect.Marker.Value, owner.Seat, 0, frame);
                PublishStrategicPrompt(frame);
                return SkillProgramStepOutcome.AwaitChoice;
            case SkillProgramEffectOp.SelectOneSelectedTarget:
                if (frame.SelectedTargetSeats.Count == 0) return SkillProgramStepOutcome.Continue;
                _strategicDrafts[frame.Id] = new(effect.Op, -1, null, frame.SelectedTargetSeats.ToArray(), [], 1);
                PublishStrategicPrompt(frame);
                return SkillProgramStepOutcome.AwaitChoice;
            default: throw new InvalidOperationException("Unknown strategic operation.");
        }
    }
    private void PublishStrategicPrompt(ProgramSkillFrame frame)
    {
        var draft = _strategicDrafts[frame.Id];
        var choices = new List<PromptChoice>();
        PromptChoice Choice(string id, string label, IReadOnlyList<int> cards, IReadOnlyList<int> targets) =>
            new(new ChoiceId($"strategic.frame-{frame.Id}.{draft.Selected.Count}.{id}"), label, cards, targets,
                new Dictionary<string, string> { ["program-action"] = "strategic-choice", ["frame-id"] = frame.Id.ToString(), ["selection-index"] = draft.Selected.Count.ToString(), ["choice"] = id });
        if (draft.Op == SkillProgramEffectOp.SelectDistinctSuitHandDiscards)
        {
            var used = draft.Selected.Select(id => _cardZones.CardsAt(_cardZones.GetLocation(id)).Single(card => card.Id == id).Suit).ToHashSet();
            foreach (var id in draft.Candidates.Where(id => !draft.Selected.Contains(id)))
            {
                var card = _cardZones.CardsAt(_cardZones.GetLocation(id)).Single(card => card.Id == id);
                if (used.Contains(card.Suit)) continue;
                var seat = _cardZones.GetLocation(id).OwnerSeat!.Value;
                choices.Add(Choice($"card-{id}", $"{_players[seat].Name}：{card.DisplayName} {card.RankText}", [id], []));
            }
            choices.Add(Choice("decline", "不弃置牌", [], []));
        }
        else if (draft.Op == SkillProgramEffectOp.SuppressGeneralSkill)
        {
            var target = _players[draft.TargetSeat];
            foreach (var skill in target.General.Skills.Concat(target.SecondaryGeneral?.Skills ?? []).Where(skill => skill.ContentId is not null).DistinctBy(skill => skill.ContentId))
                if (target.SkillGrants.Grants.Any(grant => grant.SkillId == skill.ContentId && grant.IsEnabled)) choices.Add(Choice(skill.ContentId!, $"令【{skill.Name}】失效", [], [target.Seat]));
            if (choices.Count == 0) { _strategicDrafts.Remove(frame.Id); return; }
        }
        else
        {
            if (draft.Selected.Count < draft.Maximum)
                foreach (var seat in draft.Candidates.Where(seat => !draft.Selected.Contains(seat))) choices.Add(Choice($"seat-{seat}", $"选择 {_players[seat].Name}", [], [seat]));
            if (draft.Op != SkillProgramEffectOp.SelectOneSelectedTarget) choices.Add(Choice("finish", "完成选择", [], []));
        }
        var skillDefinition = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTrigger, frame.OwnerSeat, $"【{skillDefinition.Name}】请选择。", [], [], frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = draft.Op == SkillProgramEffectOp.SelectDistinctSuitHandDiscards,
          TargetSeat = draft.TargetSeat >= 0 ? draft.TargetSeat : frame.OwnerSeat,
          SkillPrompt = new(frame.SkillId, skillDefinition.Name, skillDefinition.Name, skillDefinition.Description), Choices = choices.AsReadOnly() };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private void ResolveStrategicProgramChoice(PromptChoice choice)
    {
        var frame = _resolutionStack.Last() as ProgramSkillFrame ?? throw new InvalidOperationException("Strategic choice lost its frame.");
        var draft = _strategicDrafts.GetValueOrDefault(frame.Id) ?? throw new InvalidOperationException("Strategic choice lost its draft.");
        if (choice.Parameters["frame-id"] != frame.Id.ToString() || choice.Parameters["selection-index"] != draft.Selected.Count.ToString()) throw new InvalidOperationException("Strategic selection is stale.");
        var id = choice.Parameters["choice"];
        ClearPendingDecision();
        if (draft.Op == SkillProgramEffectOp.SelectDistinctSuitHandDiscards)
        {
            if (id == "decline") SetProgramCardSet(frame.Id, draft.Bind!, [], SkillProgramCardSetVisibility.Private, []);
            else
            {
                var cardId = choice.Cards.Single();
                var location = _cardZones.GetLocation(cardId);
                if (!draft.Candidates.Contains(cardId) || location.Zone != CardZoneKind.Hand || location.OwnerSeat is not { } seat || seat != frame.OwnerSeat && seat != draft.TargetSeat) throw new InvalidOperationException("Discard selection no longer owns the card.");
                var selected = draft.Selected.Append(cardId).ToArray();
                if (selected.Select(card => _cardZones.CardsAt(_cardZones.GetLocation(card)).Single(item => item.Id == card).Suit).Distinct().Count() != selected.Length) throw new InvalidOperationException("Discard selection repeats a suit.");
                if (selected.Length < 4) { _strategicDrafts[frame.Id] = draft with { Selected = selected }; PublishStrategicPrompt(frame); return; }
                SetProgramCardSet(frame.Id, draft.Bind!, selected, SkillProgramCardSetVisibility.Private, selected.Select(card => _cardZones.GetLocation(card)).ToArray());
            }
        }
        else if (draft.Op == SkillProgramEffectOp.SuppressGeneralSkill)
        {
            var target = _players[draft.TargetSeat];
            var grants = target.SkillGrants.Grants.Where(grant => grant.IsEnabled && grant.SkillId == id).Select(grant => grant.GrantId).ToArray();
            if (grants.Length == 0) throw new InvalidOperationException("Suppressed skill is no longer owned.");
            foreach (var grant in grants) target.SkillGrants.SetEnabled(grant, false);
            _programSuppressions.Add(new(frame.OwnerSeat, target.Seat, id, _turnNumber, grants));
            QueueGameEvent(new ProgramSkillSuppressedEvent(frame.OwnerSeat, target.Seat, id, true));
            _strategicEndPlayTurn = _turnNumber;
        }
        else
        {
            if (id != "finish")
            {
                var seat = choice.Targets.Single();
                if (!draft.Candidates.Contains(seat) || !_players[seat].IsAlive || draft.Op == SkillProgramEffectOp.SelectChainedByMarker && !_players[seat].IsChained || draft.Selected.Contains(seat) || draft.Selected.Count >= draft.Maximum) throw new InvalidOperationException("Bound target selection is unavailable.");
                if (draft.Op == SkillProgramEffectOp.SelectOneSelectedTarget)
                {
                    _resolutionStack[^1] = frame with { SelectedTargetSeats = [seat] };
                    _strategicDrafts.Remove(frame.Id);
                    ContinueProgramSkill(frame.Id);
                    return;
                }
                _strategicDrafts[frame.Id] = draft with { Selected = draft.Selected.Append(seat).ToArray() };
                PublishStrategicPrompt(frame); return;
            }
            _resolutionStack[^1] = frame with { SelectedTargetSeats = draft.Selected };
        }
        _strategicDrafts.Remove(frame.Id);
        ContinueProgramSkill(frame.Id);
    }
    private void ExpireProgramSuppressions(int turnOwnerSeat)
    {
        foreach (var suppression in _programSuppressions.Where(item => item.TargetSeat == turnOwnerSeat && item.CreatedTurn < _turnNumber).ToArray())
        {
            foreach (var id in suppression.GrantIds)
                if (_players[turnOwnerSeat].SkillGrants.Grants.Any(grant => grant.GrantId == id)) _players[turnOwnerSeat].SkillGrants.SetEnabled(id, true);
            _programSuppressions.Remove(suppression);
            QueueGameEvent(new ProgramSkillSuppressedEvent(suppression.SourceSeat, turnOwnerSeat, suppression.SkillId, false));
        }
    }
    private void AssertStrategicProgramSelection(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (!_strategicDrafts.TryGetValue(frame.Id, out var draft)) return;
        if (draft.Op != paused.Op || draft.Selected.Count > draft.Maximum ||
            draft.Selected.Distinct().Count() != draft.Selected.Count || draft.Selected.Any(item => !draft.Candidates.Contains(item)) ||
            !ReferenceEquals(frame, _resolutionStack.LastOrDefault()) ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger } prompt || prompt.PlayerSeat != frame.OwnerSeat ||
            prompt.Choices.Count == 0 || prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") != "strategic-choice" ||
                choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString() ||
                choice.Parameters.GetValueOrDefault("selection-index") != draft.Selected.Count.ToString()))
            throw new InvalidOperationException("Strategic selection lost its suspended instruction or validated prompt.");
        if (draft.Op == SkillProgramEffectOp.SelectDistinctSuitHandDiscards && !prompt.IsPrivate)
            throw new InvalidOperationException("Cross-owner hand selection must remain private.");
    }
}
