using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private long SequentialDiscardMovementSequence => _cardMovements.Count == 0 ? 0 : _cardMovements[^1].Sequence;
    private static bool IsSequentialDiscardOp(SkillProgramEffectOp op) => op is
        SkillProgramEffectOp.ChooseCategoryOrSequentialDiscard or SkillProgramEffectOp.EscalatingDiscardOrDamageFromSelected;
    private SkillProgramEffect SequentialDiscardEffect(ProgramSkillFrame frame) =>
        ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
    private string SequentialDiscardReason(ProgramSkillFrame frame) => $"skill-program.{frame.SkillId}.{SequentialDiscardEffect(frame).Op}";
    private CardConversionSource SequentialDiscardSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private IReadOnlyList<int> SequentialDiscardOrder(int owner, int start, bool ring) => Array.AsReadOnly((ring
        ? Enumerable.Range(0, _playerCount).Select(offset => (start + offset) % _playerCount).Where(seat => seat != owner)
        : Enumerable.Repeat(start, 1)).ToArray());
    private bool SequentialDiscardCanContinue(ProgramSkillFrame frame) => _winner == Winner.None && _players[frame.OwnerSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId);
    private IEnumerable<(Card Card, CardLocation Location)> SequentialDiscardCards(ProgramSkillFrame frame, int chooser, SkillProgramEffect effect) =>
        effect.Zones.SelectMany(zone => _cardZones.CardsAt(new(zone, chooser))
            .Where(card => !IsForeignEquipmentDiscardPrevented(chooser, card, new(zone, chooser), OwnedCardMoveIntent.Discard) &&
                !(zone == CardZoneKind.Equipment && chooser == frame.OwnerSeat && IsActiveProgramSourceEquipmentCard(chooser, frame.SkillId, frame.SkillInstanceId, card)))
            .Select(card => (card, new CardLocation(zone, chooser))));

    private SkillProgramStepOutcome RunSequentialDiscard(ProgramSkillFrame supplied, SkillProgramEffect effect, int selectedTarget)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (!IsSequentialDiscardOp(effect.Op) || effect.Target != (effect.Op == SkillProgramEffectOp.EscalatingDiscardOrDamageFromSelected
                ? SkillProgramEffectTarget.Owner : SkillProgramEffectTarget.SelectedTarget) || frame.TriggerId is not null || frame.WindowContext is not null ||
            frame.SelectedTargetSeats is not [var start] || start == frame.OwnerSeat || !IsValidPlayerSeat(start))
            throw new InvalidOperationException("A sequential discard requires its original one-target activation.");
        if (frame.SequentialDiscard is null)
        {
            if (!SequentialDiscardCanContinue(frame) || !_players[start].IsAlive) return SkillProgramStepOutcome.Continue;
            var ring = effect.Op == SkillProgramEffectOp.EscalatingDiscardOrDamageFromSelected;
            var draft = new ProgramSequentialDiscardDraft(ring ? ProgramSequentialDiscardKind.SelectedStartEscalating : ProgramSequentialDiscardKind.CategoryOrSequential,
                frame.InstructionIndex - 1, SequentialDiscardSource(frame), frame.GameplayHash, _turnNumber, _currentSeat,
                start, SequentialDiscardOrder(frame.OwnerSeat, start, ring), 0, start, 0, 0, null,
                ProgramSequentialDiscardStage.BranchChoice, [], []);
            ReplaceRuntimeTop(frame = frame with { SequentialDiscard = draft, ReexecuteParticipantInstruction = true });
            AdvanceEventRulesAndQueueFact(new ProgramSequentialDiscardStartedEvent(frame.Id, draft.Kind, draft.Source,
                draft.GameplayHash, draft.ActualTurnNumber, draft.ActualTurnOwnerSeat, start));
        }
        else ReplaceRuntimeTop(frame = frame with { ReexecuteParticipantInstruction = true });
        if (!ValidSequentialDiscard(frame)) throw new InvalidOperationException("A sequential discard lost its original producer or actual ledger.");
        var current = frame.SequentialDiscard!;
        if (current.Stage is ProgramSequentialDiscardStage.AwaitingMovement or ProgramSequentialDiscardStage.AwaitingDamage)
        {
            if (frame.PendingMovementContinuation is not null || frame.AttackAttempt is not null)
                throw new InvalidOperationException("The sequential discard resumed before its real child completed.");
            if (current.Kind == ProgramSequentialDiscardKind.SelectedStartEscalating)
                current = current with { Cursor = current.Cursor + 1, Stage = ProgramSequentialDiscardStage.BranchChoice,
                    PrimaryBranch = null, Payment = null };
            else current = current with { Stage = ProgramSequentialDiscardStage.CardChoice };
            ReplaceRuntimeTop(frame = frame with { SequentialDiscard = current });
        }
        if (!SequentialDiscardCanContinue(frame))
        { CancelProgramBindingAndCleanup(frame, "弃牌来源或原实例失效，已付结算保留。"); return SkillProgramStepOutcome.AwaitChild; }
        if (current.Kind == ProgramSequentialDiscardKind.SelectedStartEscalating)
        {
            var cursor = current.Cursor;
            while (cursor < current.Order.Count && !_players[current.Order[cursor]].IsAlive) cursor++;
            if (cursor == current.Order.Count) return FinishSequentialDiscard(frame, 0);
            current = current with { Cursor = cursor, ChooserSeat = current.Order[cursor] };
            ReplaceRuntimeTop(frame = frame with { SequentialDiscard = current });
        }
        else if (!_players[current.ChooserSeat].IsAlive || current.PrimaryBranch is not null && current.Remaining == 0)
            return FinishSequentialDiscard(frame, current.Remaining);
        return PublishSequentialDiscard(frame, effect);
    }

    private SkillProgramStepOutcome FinishSequentialDiscard(ProgramSkillFrame frame, int remaining)
    {
        var d = frame.SequentialDiscard!;
        AdvanceEventRulesAndQueueFact(new ProgramSequentialDiscardFinishedEvent(frame.Id,
            d.Kind == ProgramSequentialDiscardKind.SelectedStartEscalating ? d.Cursor : 1, remaining));
        ReplaceRuntimeTop(frame with { SequentialDiscard = null, ReexecuteParticipantInstruction = false });
        return SkillProgramStepOutcome.Continue;
    }

    private SkillProgramStepOutcome PublishSequentialDiscard(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var d = frame.SequentialDiscard!;
        var available = SequentialDiscardCards(frame, d.ChooserSeat, effect).ToArray();
        var choices = new List<PromptChoice>();
        void Add(string branch, string label, IReadOnlyList<int>? cards = null, CardLocation? location = null) => choices.Add(new(
            new ChoiceId($"sequential-discard.{frame.Id}.{d.Cursor}.{d.Stage}.{branch}.{cards?.FirstOrDefault() ?? 0}"), label,
            cards ?? [], cards is null ? [] : [d.ChooserSeat], new Dictionary<string, string>
            { ["program-action"] = "sequential-discard", ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture),
              ["cursor"] = d.Cursor.ToString(CultureInfo.InvariantCulture), ["stage"] = d.Stage.ToString(), ["branch"] = branch,
              ["source-zone"] = location?.Zone.ToString() ?? "", ["selection-index"] = d.SelectedCardIds.Count.ToString(CultureInfo.InvariantCulture) }));
        if (d.Stage == ProgramSequentialDiscardStage.BranchChoice)
        {
            if (d.Kind == ProgramSequentialDiscardKind.CategoryOrSequential)
            {
                if (available.Any(item => effect.CardCategories.Contains(GetProgramCardCategory(item.Card.Kind))))
                    Add("primary", $"弃置 {effect.Amount} 张指定类别牌。");
                if (available.Length != 0) Add("sequential", $"依次弃置 {effect.MinimumValue} 张任意牌。");
            }
            else
            {
                if (available.Length > d.PreviousCount) Add("discard", $"弃置至少 {d.PreviousCount + 1} 张牌。");
                Add("damage", $"受到 {effect.Amount} 点{(effect.DamageNature == DamageNature.Fire ? "火焰" : effect.DamageNature == DamageNature.Thunder ? "雷电" : "")}伤害。");
            }
        }
        else
        {
            foreach (var item in available.Where(item => !d.SelectedCardIds.Contains(item.Card.Id) &&
                (d.PrimaryBranch != true || effect.CardCategories.Contains(GetProgramCardCategory(item.Card.Kind)))))
                Add("card", $"{(d.Stage == ProgramSequentialDiscardStage.SelectingBatch ? "选择" : "弃置")}【{item.Card.DisplayName}】。", [item.Card.Id], item.Location);
            if (d.Stage == ProgramSequentialDiscardStage.SelectingBatch && d.SelectedCardIds.Count > d.PreviousCount)
                Add("finish", $"一次弃置所选 {d.SelectedCardIds.Count} 张牌。");
        }
        if (choices.Count == 0) return FinishSequentialDiscard(frame, d.Remaining);
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, d.ChooserSeat,
            d.Kind == ProgramSequentialDiscardKind.SelectedStartEscalating ? $"【{skill.Name}】上家实际弃牌 {d.PreviousCount} 张。" : $"【{skill.Name}】请选择弃牌分支；还需弃置 {d.Remaining} 张。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), [d.ChooserSeat], frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = d.ChooserSeat,
            Choices = Array.AsReadOnly(choices.ToArray()), SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[d.ChooserSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveSequentialDiscardChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Missing sequential discard owner.");
        var d = f.SequentialDiscard ?? throw new InvalidOperationException("Missing sequential discard receipt.");
        var effect = SequentialDiscardEffect(f);
        if (!ValidSequentialDiscard(f) || _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } decision ||
            decision.PlayerSeat != d.ChooserSeat || !decision.Choices.Any(c => c.Id == choice.Id) ||
            choice.Parameters.GetValueOrDefault("frame-id") != f.Id.ToString(CultureInfo.InvariantCulture) ||
            choice.Parameters.GetValueOrDefault("cursor") != d.Cursor.ToString(CultureInfo.InvariantCulture) ||
            choice.Parameters.GetValueOrDefault("stage") != d.Stage.ToString() ||
            choice.Parameters.GetValueOrDefault("selection-index") != d.SelectedCardIds.Count.ToString(CultureInfo.InvariantCulture))
            throw new InvalidOperationException("A sequential discard choice must belong to its exact private producer.");
        if (!SequentialDiscardCanContinue(f) || !_players[d.ChooserSeat].IsAlive)
        { ClearPendingDecision(); CancelProgramBindingAndCleanup(f, "弃牌参与者或原实例失效。"); return; }
        var branch = choice.Parameters.GetValueOrDefault("branch");
        if (d.Stage == ProgramSequentialDiscardStage.BranchChoice)
        {
            if (choice.Cards.Count != 0 || choice.Targets.Count != 0) throw new InvalidOperationException("Choosing a discard branch cannot pay an entity.");
            ClearPendingDecision();
            if (d.Kind == ProgramSequentialDiscardKind.SelectedStartEscalating && branch == "damage")
            {
                ReplaceRuntimeTop(f = f with { SequentialDiscard = d with { Stage = ProgramSequentialDiscardStage.AwaitingDamage,
                    PreviousCount = 0, SelectedCardIds = [], SelectedLocations = [] }, ReexecuteParticipantInstruction = true });
                AdvanceEventRulesAndQueueFact(new ProgramSequentialDiscardDamageChosenEvent(f.Id, d.Cursor, f.OwnerSeat,
                    d.ChooserSeat, effect.Amount, effect.DamageNature ?? DamageNature.Normal));
                BeginProgramSkillDamage(f, d.ChooserSeat, effect.Amount, nature: effect.DamageNature); return;
            }
            if (d.Kind == ProgramSequentialDiscardKind.SelectedStartEscalating && branch != "discard" ||
                d.Kind == ProgramSequentialDiscardKind.CategoryOrSequential && branch is not ("primary" or "sequential"))
                throw new InvalidOperationException("Invalid sequential discard branch.");
            var primary = branch == "primary";
            d = d with { PrimaryBranch = primary, Remaining = d.Kind == ProgramSequentialDiscardKind.SelectedStartEscalating ? 0 : primary ? effect.Amount : effect.MinimumValue,
                Stage = d.Kind == ProgramSequentialDiscardKind.SelectedStartEscalating ? ProgramSequentialDiscardStage.SelectingBatch : ProgramSequentialDiscardStage.CardChoice };
            ReplaceRuntimeTop(f = f with { SequentialDiscard = d });
            AdvanceEventRulesAndQueueFact(new ProgramSequentialDiscardBranchEvent(f.Id, d.Cursor, d.ChooserSeat, primary,
                d.Kind == ProgramSequentialDiscardKind.SelectedStartEscalating ? d.PreviousCount + 1 : d.Remaining));
            if (PublishSequentialDiscard(f, effect) == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(f.Id);
            return;
        }
        if (branch == "card")
        {
            if (choice.Cards is not [var id] || choice.Targets is not [var chooser] || chooser != d.ChooserSeat || d.SelectedCardIds.Contains(id) ||
                !Enum.TryParse<CardZoneKind>(choice.Parameters.GetValueOrDefault("source-zone"), out var zone))
                throw new InvalidOperationException("Invalid sequential discard entity.");
            var item = SequentialDiscardCards(f, chooser, effect).SingleOrDefault(item => item.Card.Id == id && item.Location.Zone == zone);
            if (item.Card is null || d.PrimaryBranch == true && !effect.CardCategories.Contains(GetProgramCardCategory(item.Card.Kind)))
                throw new InvalidOperationException("The selected entity no longer satisfies the frozen discard branch.");
            ClearPendingDecision();
            if (d.Stage == ProgramSequentialDiscardStage.SelectingBatch)
            {
                ReplaceRuntimeTop(f = f with { SequentialDiscard = d with {
                    SelectedCardIds = Array.AsReadOnly(d.SelectedCardIds.Append(id).ToArray()),
                    SelectedLocations = Array.AsReadOnly(d.SelectedLocations.Append(item.Location).ToArray()) } });
                PublishSequentialDiscard(f, effect); return;
            }
            if (d.Stage != ProgramSequentialDiscardStage.CardChoice) throw new InvalidOperationException("The sequential discard is not choosing its next card.");
            PaySequentialDiscard(f, [id], [item.Location]); return;
        }
        if (branch != "finish" || d.Stage != ProgramSequentialDiscardStage.SelectingBatch || choice.Cards.Count != 0 || choice.Targets.Count != 0 ||
            d.SelectedCardIds.Count <= d.PreviousCount) throw new InvalidOperationException("The escalating discard has not exceeded its real predecessor.");
        if (d.SelectedCardIds.Where((id, i) => _cardZones.GetLocation(id) != d.SelectedLocations[i] ||
            !SequentialDiscardCards(f, d.ChooserSeat, effect).Any(item => item.Card.Id == id && item.Location == d.SelectedLocations[i])).Any())
            throw new InvalidOperationException("A privately selected discard entity left its frozen owned source.");
        ClearPendingDecision(); PaySequentialDiscard(f, d.SelectedCardIds, d.SelectedLocations);
    }

    private void PaySequentialDiscard(ProgramSkillFrame frame, IReadOnlyList<int> ids, IReadOnlyList<CardLocation> sources)
    {
        var d = frame.SequentialDiscard!; var before = SequentialDiscardMovementSequence;
        var payment = new ProgramSequentialDiscardPayment(d.ChooserSeat, d.Cursor, Array.AsReadOnly(ids.ToArray()),
            Array.AsReadOnly(sources.ToArray()), before, before, 0);
        ReplaceRuntimeTop(frame with { SequentialDiscard = d with { Stage = ProgramSequentialDiscardStage.AwaitingMovement,
            SelectedCardIds = [], SelectedLocations = [], Payment = payment }, ReexecuteParticipantInstruction = true,
            PendingMovementContinuation = new(d.ChooserSeat, 0, null) });
        MoveProgramCardsFromMultipleSources(ids, CardLocation.DiscardPile, new(SequentialDiscardReason(frame)), (_, records) =>
        {
            var actual = records.Count(m => m.From.OwnerSeat == d.ChooserSeat &&
                m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
                (m.To == CardLocation.DiscardPile || m.From.Zone == CardZoneKind.Equipment && m.To == CardLocation.OutsideGame));
            var paid = payment with { SequenceAfter = records.Max(m => m.Sequence), ActualCount = actual };
            var f = GetActiveProgramFrame(frame.Id); var current = f.SequentialDiscard!;
            var remaining = d.Kind == ProgramSequentialDiscardKind.CategoryOrSequential ? Math.Max(0, d.Remaining - actual) : 0;
            ReplaceRuntimeTop(f with { SequentialDiscard = current with { Payment = paid, Remaining = remaining,
                PreviousCount = d.Kind == ProgramSequentialDiscardKind.SelectedStartEscalating ? actual : current.PreviousCount } });
            AdvanceEventRulesAndQueueFact(new ProgramSequentialDiscardPaidEvent(frame.Id, d.Cursor, d.ChooserSeat,
                paid.SequenceBefore, paid.SequenceAfter, ids.Count, actual, remaining));
        });
        if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(frame.Id);
    }

    private PromptChoice SelectAiSequentialDiscard(PendingDecision decision, ProgramSkillFrame frame)
    {
        var d = frame.SequentialDiscard!; var effect = SequentialDiscardEffect(frame);
        var branches = decision.Choices.Where(c => c.Cards.Count == 0).ToArray();
        if (d.Stage == ProgramSequentialDiscardStage.BranchChoice)
        {
            if (d.Kind == ProgramSequentialDiscardKind.CategoryOrSequential)
                return branches.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "primary") ?? branches.Single();
            var discard = branches.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "discard");
            if (discard is null) return branches.Single();
            // The actor sees only its own current entities and public HP/range. No other concealed hand is consulted.
            var cost = SequentialDiscardCards(frame, d.ChooserSeat, effect).Select(item => GetKeepValue(item.Card, _players[d.ChooserSeat]))
                .Order().Take(d.PreviousCount + 1).Sum();
            return cost <= effect.Amount * 20d ? discard : branches.Single(c => c.Parameters.GetValueOrDefault("branch") == "damage");
        }
        if (branches.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "finish") is { } finish) return finish;
        return decision.Choices.Where(c => c.Cards.Count == 1).OrderBy(c => GetKeepValue(
            _cardZones.CardsAt(_cardZones.GetLocation(c.Cards[0])).Single(card => card.Id == c.Cards[0]), _players[d.ChooserSeat]))
            .ThenBy(c => c.Cards[0]).First();
    }

    // Opt in only after this operation's exact physical ledger has paid. A dead
    // forced participant may finish its child and leave the ring's next seat intact.
    private bool TryReturnSequentialDiscardMovement(ProgramSkillFrame frame)
    {
        if (frame.SequentialDiscard is not { Stage: ProgramSequentialDiscardStage.AwaitingMovement, Payment: not null }) return false;
        if (!ValidSequentialDiscard(frame) || frame.PendingMovementContinuation is null)
            throw new InvalidOperationException("The sequential discard movement lost its original paid continuation.");
        ReplaceRuntimeTop(frame with { PendingMovementContinuation = null });
        AdvanceRuntimeProgram(frame.Id);
        return true;
    }

    private sealed partial class ProgramSkillHost : ISequentialDiscardProgramHost
    {
        public SkillProgramStepOutcome RunSequentialDiscard(ProgramSkillFrame f, SkillProgramEffect e, int target) => engine.RunSequentialDiscard(f, e, target);
    }
}
