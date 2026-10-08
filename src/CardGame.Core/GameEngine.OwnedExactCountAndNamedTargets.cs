namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool CanStartExactOwnedCardCount(CharacterState owner, ProgramExecutionPlan plan)
    {
        if (plan.Instructions.FirstOrDefault() is not { RequireExactCount: true } effect) return true;
        var required = Math.Max(0, owner.MaxHp - owner.Hp);
        var discards = plan.Instructions.Any(e => e.Op == SkillProgramEffectOp.MoveBoundCards &&
            e.SourceBind == effect.ResultBind && e.Destination == SkillProgramCardDestination.DiscardPile &&
            e.Condition.Kind == SkillProgramConditionKind.Always);
        var available = effect.Zones.SelectMany(zone => _cardZones.CardsAt(new(zone, owner.Seat)))
            .Count(card => (effect.CardKinds.Count == 0 || effect.CardKinds.Contains(card.Kind)) &&
                (effect.Suits.Count == 0 || effect.Suits.Contains(GetProgramEffectiveSuit(owner, card))) &&
                (!discards || !IsSelfHandCategoryDiscardForbidden(owner.Seat, card,
                    _cardZones.GetLocation(card.Id), OwnedCardMoveIntent.Discard)));
        return available >= required;
    }

    private CommandError? ValidateExactOwnedCardCountActivation(ProgramExecutionPlan? plan, int ownerSeat) =>
        plan is not null && !CanStartExactOwnedCardCount(_players[ownerSeat], plan)
            ? new(CommandErrorCode.IllegalAction, "没有足额的合法区域牌支付精确费用。") : null;

    private void FreezeExactOwnedSelectionSuits(long frameId, SkillProgramEffect effect,
        IReadOnlyList<int> cardIds, IReadOnlyList<CardLocation> locations)
    {
        if (effect.FreezeSelectedCardSuits != true) return;
        if (effect.RequireExactCount != true || cardIds.Count != locations.Count)
            throw new InvalidOperationException("Frozen selected suits require an aligned exact owner selection.");
        var frame = GetActiveProgramFrame(frameId);
        var suits = Array.AsReadOnly(cardIds.Select((id, i) => GetProgramEffectiveSuit(_players[frame.OwnerSeat],
            _cardZones.CardsAt(locations[i]).Single(c => c.Id == id))).ToArray());
        ReplaceRuntimeTop(frame with { CardSetBindings = Array.AsReadOnly(frame.CardSetBindings.Select(b =>
            b.Name == effect.ResultBind ? b with { FrozenSelectedSuits = suits } : b).ToArray()) });
    }

    private SkillProgramStepOutcome SelectProgramNamedBoundTargets(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        if (frame.NamedBoundTargetSelection is not null || effect.Op != SkillProgramEffectOp.SelectTargets ||
            effect.NumberExpression != SkillProgramNumberExpression.BoundCardCount || effect.SourceBind is null ||
            effect.TargetKind is not { } kind || effect.TargetAiOrder is not { } aiOrder)
            throw new InvalidOperationException("A named target selection requires its exact paused instruction.");
        var count = GetProgramCardSet(frame, effect.SourceBind).CardIds.Count;
        var candidates = GetProgramTargetSeats(frame.OwnerSeat, kind, frame.WindowContext);
        var maximum = Math.Min(Math.Min(count, effect.MaximumTargets), candidates.Count);
        if (maximum < effect.MinimumTargets)
        {
            CancelProgramBindingAndCleanup(frame, "没有足够的合法具名技能目标，技能结算已取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var draft = new ProgramNamedBoundTargetSelection(frame.InstructionIndex, frame.OwnerSeat,
            frame.SkillId, GetProgramBindingId(frame), frame.SkillInstanceId, frame.GameplayHash,
            effect.SourceBind, count, effect.MinimumTargets, maximum, kind, aiOrder,
            Array.AsReadOnly(candidates.ToArray()));
        frame = frame with { NamedBoundTargetSelection = draft };
        ReplaceRuntimeTop(frame);
        var choices = NamedBoundTargetChoices(frame, draft);
        var presentation = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTrigger, frame.OwnerSeat,
            $"【{presentation.Name}】请选择 {draft.MinimumTargets} 至 {draft.MaximumTargets} 名目标角色。",
            [], draft.CandidateSeats, frame.WindowContext?.SourceSeat)
        {
            PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = frame.OwnerSeat,
            SkillPrompt = new(frame.SkillId, presentation.Name, $"{presentation.Name} · 选择目标", presentation.Description),
            Choices = choices
        };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> NamedBoundTargetChoices(ProgramSkillFrame frame, ProgramNamedBoundTargetSelection draft)
    {
        var choices = new List<PromptChoice>();
        var current = new List<int>();
        Add(0);
        return Array.AsReadOnly(choices.ToArray());
        void Add(int next)
        {
            if (current.Count >= draft.MinimumTargets)
            {
                var seats = Array.AsReadOnly(current.ToArray());
                choices.Add(new(new ChoiceId($"program-named-targets.frame-{frame.Id}.seats-{string.Join("-", seats)}"),
                    seats.Count == 0 ? "不选择目标。" : $"选择 {string.Join("、", seats.Select(s => _players[s].Name))}。",
                    [], seats, new Dictionary<string, string>
                    {
                        ["program-action"] = "select-targets", ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["source-bind"] = draft.SourceBind, ["target-kind"] = draft.TargetKind.ToString(),
                        ["minimum-targets"] = draft.MinimumTargets.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["maximum-targets"] = draft.MaximumTargets.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["target-ai-order"] = draft.TargetAiOrder.ToString()
                    }));
            }
            if (current.Count == draft.MaximumTargets) return;
            for (var index = next; index < draft.CandidateSeats.Count; index++)
            {
                current.Add(draft.CandidateSeats[index]); Add(index + 1); current.RemoveAt(current.Count - 1);
            }
        }
    }

    private void ResolveProgramNamedBoundTargets(ProgramSkillFrame frame, SkillProgramEffect effect, PromptChoice selected)
    {
        AssertProgramNamedBoundTargetSelection(frame, effect);
        var draft = frame.NamedBoundTargetSelection!;
        if (selected.Cards.Count != 0 || selected.Targets.Count < draft.MinimumTargets ||
            selected.Targets.Count > draft.MaximumTargets || selected.Targets.Distinct().Count() != selected.Targets.Count ||
            selected.Targets.Any(s => !draft.CandidateSeats.Contains(s)) ||
            !NamedBoundTargetChoices(frame, draft).Any(c => c.Id == selected.Id && c.Targets.SequenceEqual(selected.Targets)) ||
            selected.Targets.Any(s => !GetProgramTargetSeats(frame.OwnerSeat, draft.TargetKind, frame.WindowContext).Contains(s)))
            throw new InvalidOperationException("The selected targets do not match the frozen named card-count range.");
        ClearPendingDecision();
        ReplaceRuntimeTop(frame with { NamedBoundTargetSelection = null,
            SelectedTargetSeats = Array.AsReadOnly(selected.Targets.ToArray()) });
        AdvanceRuntimeProgram(frame.Id);
    }

    private void AssertProgramNamedBoundTargetSelection(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (frame.NamedBoundTargetSelection is not { } draft)
        {
            if (paused.Op == SkillProgramEffectOp.SelectTargets && paused.SourceBind is not null &&
                ReferenceEquals(frame, _resolutionStack.LastOrDefault()) && _pendingDecision?.Kind == DecisionKind.ProgramTrigger)
                throw new InvalidOperationException("The named target prompt lost its owning draft.");
            return;
        }
        if (paused.Op != SkillProgramEffectOp.SelectTargets || paused.SourceBind != draft.SourceBind ||
            paused.NumberExpression != SkillProgramNumberExpression.BoundCardCount ||
            paused.TargetKind != draft.TargetKind || paused.TargetAiOrder != draft.TargetAiOrder ||
            draft.InstructionIndex != frame.InstructionIndex || draft.OwnerSeat != frame.OwnerSeat ||
            draft.SkillId != frame.SkillId || draft.BindingId != GetProgramBindingId(frame) ||
            draft.SkillInstanceId != frame.SkillInstanceId || draft.GameplayHash != frame.GameplayHash ||
            draft.FrozenBoundCardCount != GetProgramCardSet(frame, draft.SourceBind).CardIds.Count ||
            draft.MinimumTargets != paused.MinimumTargets || draft.MaximumTargets != Math.Min(
                Math.Min(paused.MaximumTargets, draft.FrozenBoundCardCount), draft.CandidateSeats.Count) ||
            draft.MinimumTargets < 0 || draft.MaximumTargets < draft.MinimumTargets || draft.MaximumTargets > 8 ||
            draft.CandidateSeats.Distinct().Count() != draft.CandidateSeats.Count ||
            !draft.CandidateSeats.SequenceEqual(GetProgramTargetSeats(frame.OwnerSeat, draft.TargetKind, frame.WindowContext)) ||
            !ReferenceEquals(frame, _resolutionStack.LastOrDefault()) ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } decision ||
            decision.PlayerSeat != frame.OwnerSeat || !decision.ValidTargetSeats.SequenceEqual(draft.CandidateSeats))
            throw new InvalidOperationException("The named target draft lost its exact instruction, frozen count or public candidates.");
        var expected = NamedBoundTargetChoices(frame, draft);
        if (decision.Choices.Count != expected.Count || decision.Choices.Where((c, i) => c.Id != expected[i].Id ||
                c.Cards.Count != 0 || !c.Targets.SequenceEqual(expected[i].Targets) ||
                c.Parameters.Count != expected[i].Parameters.Count ||
                c.Parameters.Any(p => expected[i].Parameters.GetValueOrDefault(p.Key) != p.Value)).Any())
            throw new InvalidOperationException("The named target prompt differs from its frozen range.");
    }
}
