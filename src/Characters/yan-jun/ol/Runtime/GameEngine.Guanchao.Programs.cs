namespace CardGame.Core;

public sealed partial class GameEngine
{

    // The committed pattern is active only while its play-phase boundary is
    // still the owner's latest one; an extra play phase in the same turn never
    // inherits an earlier choice because every entry enqueues its own boundary.
    private (bool Ascending, int PhaseEventIndex)? CurrentGuanchaoPattern(int ownerSeat)
    {
        var facts = CompleteProgramEventHistory().ToArray();
        var phaseIndex = Array.FindLastIndex(facts, e => e is PhaseChangedEvent phase &&
            phase.Phase == TurnPhase.Play && phase.ActorSeat == ownerSeat);
        var choice = facts.OfType<ProgramGuanchaoPatternChosenEvent>().LastOrDefault(e =>
            e.OwnerSeat == ownerSeat && e.PhaseEventIndex == phaseIndex);
        return choice is null ? null : (choice.Ascending, choice.PhaseEventIndex);
    }

    // Prior uses of this phase, in order, deduplicated per accepted action; a
    // use without a rank (virtual declarations, multi-card conversions) stays
    // null and breaks the pattern instead of silently comparing as zero.
    private IReadOnlyList<int?> GuanchaoPhaseUseRanks(int ownerSeat, int phaseEventIndex, long currentActionId, bool includeResponseUses = false)
    {
        var ranks = new List<int?>();
        var seen = new HashSet<long>();
        foreach (var accepted in CompleteProgramEventHistory().Skip(phaseEventIndex + 1).OfType<CardActionAcceptedEvent>())
        {
            var action = accepted.Action;
            if ((action.Type != CardActionType.Use && !(includeResponseUses && ProgramEventHistory<WushuangCardUseNameRecordedEvent>().Any(e => e.CardActionId == action.ActionId))) || action.ActorSeat != ownerSeat || !seen.Add(action.ActionId) ||
                action.ActionId == currentActionId)
                continue;
            var rank = action.EffectiveRank ?? (action.PhysicalCards.Count == 1 ? GetAdvancedCard(action.PhysicalCards[0].CardId).Rank : (int?)null);
            ranks.Add(includeResponseUses && rank is <= 0 ? null : rank);
        }
        return ranks;
    }

    private static bool MatchesGuanchaoPattern(IReadOnlyList<int?> ranks, bool ascending) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_yan_jun_ol_Guanchao_Programs.MatchesGuanchaoPattern(ranks,ascending);

    // 观潮 launch: the owner commits this play phase to one strict pattern.
    private SkillProgramStepOutcome GuanchaoProgramChoosePattern(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (!owner.IsAlive || _winner != Winner.None || _phase != TurnPhase.Play || owner.Seat != _currentSeat)
            return SkillProgramStepOutcome.Continue;
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        PromptChoice Pattern(bool ascending) => new(
            new ChoiceId($"guanchao-pattern.frame-{frame.Id}.{(ascending ? "ascending" : "descending")}"),
            ascending ? "严格递增。" : "严格递减。", [], [],
            new Dictionary<string, string>
            {
                ["program-action"] = "guanchao-pattern",
                ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["ascending"] = ascending ? "true" : "false"
            });
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】请选择本阶段使用牌点数的规律。",
            [], [], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 选择规律",
                "本阶段你使用过的所有牌的点数均符合所选规律时，你每次使用牌摸一张牌。"),
            Choices = new[] { Pattern(true), Pattern(false) }.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    [ProgramAnswer("guanchao-pattern")]
    private void ResolveGuanchaoPatternChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Guanchao pattern choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.GuanchaoChoosePattern } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Guanchao pattern choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != active.OwnerSeat)
            throw new InvalidOperationException("The Guanchao pattern chooser changed while suspended.");
        ClearPendingDecision();
        var ascending = selected.Parameters.GetValueOrDefault("ascending") == "true";
        AdvanceEventRulesAndQueueFact(new ProgramGuanchaoPatternChosenEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, CurrentPlayPhaseEventIndex(active.OwnerSeat), ascending));
        AddLog("SkillEffect",
            $"{_players[active.OwnerSeat].Name} 观潮：本阶段使用牌的点数按{(ascending ? "严格递增" : "严格递减")}结算。",
            active.OwnerSeat);
        AdvanceRuntimeProgram(active.Id);
    }

    // 观潮 draw: the whole phase ledger including the current card must match.
    private SkillProgramStepOutcome GuanchaoProgramRankDraw(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (!owner.IsAlive || _winner != Winner.None) return SkillProgramStepOutcome.Continue;
        var pattern = active.SkillId == BGuanchao ? WushuangGuanchaoPattern(owner.Seat) : CurrentGuanchaoPattern(owner.Seat);
        if (pattern is null) return SkillProgramStepOutcome.Continue;
        var context = frame.WindowContext?.CardUse;
        var action = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault()?.Action ??
            _resolutionStack.OfType<CardUseFrame>().LastOrDefault(item => item.Action?.ActionId == context?.CardActionId)?.Action;
        if (action is null || action.ActorSeat != owner.Seat) return SkillProgramStepOutcome.Continue;
        var currentRank = action.EffectiveRank ?? (action.PhysicalCards.Count == 1 ? GetAdvancedCard(action.PhysicalCards[0].CardId).Rank : (int?)null);
        if (active.SkillId == BGuanchao && currentRank is <= 0) currentRank = null;
        var ranks = GuanchaoPhaseUseRanks(owner.Seat, pattern.Value.PhaseEventIndex, action.ActionId, active.SkillId == BGuanchao).Append(currentRank).ToArray();
        var drew = MatchesGuanchaoPattern(ranks, pattern.Value.Ascending);
        if (drew)
            DrawCards(owner, 1, true, new($"skill-program.{active.SkillId}.guanchao-draw"));
        AddLog("SkillEffect", drew
            ? $"{owner.Name} 观潮：本阶段使用过的牌点数均符合所选规律，摸一张牌。"
            : $"{owner.Name} 观潮：本阶段使用过的牌点数不再符合所选规律。", owner.Seat);
        AdvanceEventRulesAndQueueFact(new ProgramGuanchaoRankDrawEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), owner.Seat, _turnNumber, currentRank ?? 0, drew));
        if (drew && !TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
        return drew ? SkillProgramStepOutcome.AwaitChild : SkillProgramStepOutcome.Continue;
    }
}
