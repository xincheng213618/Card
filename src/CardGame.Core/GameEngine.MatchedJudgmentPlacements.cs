using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome BeginMatchedJudgmentPlacement(ProgramSkillFrame input, string bind, IReadOnlyList<Suit> suits)
    {
        var f = GetActiveProgramFrame(input.Id); var plan = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!);
        var set = GetProgramCardSet(f, bind);
        if (f.MatchedJudgmentPlacement is not null || f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } ||
            f.InstructionIndex != 2 || plan.Instructions.Count != 2 || plan.Instructions[0] is not { Op: SkillProgramEffectOp.StartJudgment } judged ||
            judged.ResultBind != bind || plan.Instructions[1].Op != SkillProgramEffectOp.PlaceMatchedJudgmentCard || set.Visibility != SkillProgramCardSetVisibility.Public)
            throw new InvalidOperationException("Judgment placement requires its own exact completed public judgment.");
        if (set.CardIds.Count == 0 || set.FrozenRevealedSuit is not { } suit || !suits.Contains(suit)) return SkillProgramStepOutcome.Continue;
        if (set.CardIds.Count != 1 || set.SourceLocations.Count != 1)
            throw new InvalidOperationException("Judgment placement cannot substitute a different result set.");
        var id = set.CardIds[0]; var from = set.SourceLocations[0];
        if (from != CardLocation.Processing || _cardZones.GetLocation(id) != from) return SkillProgramStepOutcome.Continue;
        var fact = CompleteProgramEventHistory().OfType<JudgmentResolvedEvent>().Single(e => e.ParentResolutionId == f.Id &&
            e.TargetSeat == f.OwnerSeat && e.Reason == judged.JudgmentReason && e.CardId == id && e.Suit == suit);
        var moved = _cardMovements.Single(m => m.CardId == id && m.From == CardLocation.Judgment(f.OwnerSeat) && m.To == from &&
            m.Reason.Value == "skill-program.judgment.result" && m.Sequence > 0 &&
            !_cardMovements.Any(later => later.CardId == id && later.Sequence > m.Sequence && later.Sequence <= (_cardMovements.LastOrDefault()?.Sequence ?? 0)));
        ReplaceRuntimeTop(f with { MatchedJudgmentPlacement = new(f.InstructionIndex, bind, fact.ResolutionId, id, suit, from,
            moved.Sequence, MatchedJudgmentPlacementStage.ChoosingDestination) });
        f = GetActiveProgramFrame(f.Id);
        if (DrainSuitPlacementChildren(f)) return SkillProgramStepOutcome.AwaitChild;
        PublishMatchedJudgmentPlacement(f); return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> MatchedJudgmentPlacementChoices(ProgramSkillFrame f)
    {
        var r = f.MatchedJudgmentPlacement!; var choices = new List<PromptChoice>();
        Dictionary<string, string> Params(string branch) => new()
        { ["program-action"] = "matched-judgment-placement", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch };
        if (r.Stage == MatchedJudgmentPlacementStage.ChoosingDestination && _cardZones.GetLocation(r.CardId) == r.ResultFrom)
        {
            var result = Array.AsReadOnly(new[] { r.CardId });
            choices.Add(new(new($"judgment-placement.{f.Id}.top"), "将原判定牌置于牌堆顶", result, [], Params("top")));
            foreach (var p in _players.Where(p => p.IsAlive).OrderBy(p => p.Seat))
                choices.Add(new(new($"judgment-placement.{f.Id}.gift.{p.Seat}"), $"将原判定牌交给 {p.Name}" + (p.Seat == f.OwnerSeat ? "，然后弃置一张牌" : ""),
                    result, Array.AsReadOnly(new[] { p.Seat }), Params("gift")));
        }
        else if (r.Stage == MatchedJudgmentPlacementStage.ChoosingSelfDiscard)
            foreach (var card in SuitBenefitCosts(f.OwnerSeat, f.SkillId, f.SkillInstanceId))
                choices.Add(new(new($"judgment-placement.{f.Id}.discard.{card.Id}"), $"弃置【{card.DisplayName}】", Array.AsReadOnly(new[] { card.Id }), [], Params("self-discard")));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishMatchedJudgmentPlacement(ProgramSkillFrame f)
    {
        var choices = MatchedJudgmentPlacementChoices(f);
        if (choices.Count == 0 || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None)
        { ReplaceRuntimeTop(f with { MatchedJudgmentPlacement = null }); AdvanceRuntimeProgram(f.Id); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat,
            f.MatchedJudgmentPlacement!.Stage == MatchedJudgmentPlacementStage.ChoosingDestination ? "选择原判定牌的去向。" : "自取判定牌后，选择一张实际手牌或装备牌弃置。",
            Array.AsReadOnly(choices.SelectMany(c => c.Cards).Distinct().ToArray()), Array.AsReadOnly(choices.SelectMany(c => c.Targets).Distinct().ToArray()), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private void ResolveMatchedJudgmentPlacement(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.MatchedJudgmentPlacement is not { } r ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([choice], [MatchedJudgmentPlacementChoices(f).Single(c => c.Id == choice.Id)]))
            throw new InvalidOperationException("Judgment placement answer differs from its exact frozen result.");
        AssertMatchedJudgmentPlacement(f);
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None ||
            r.Stage == MatchedJudgmentPlacementStage.ChoosingDestination && !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
        { ClearPendingDecision(); ReplaceRuntimeTop(f with { MatchedJudgmentPlacement = null }); AdvanceRuntimeProgram(f.Id); return; }
        var branch = choice.Parameters["branch"];
        if (r.Stage == MatchedJudgmentPlacementStage.ChoosingDestination)
        {
            if (choice.Cards.Count != 1 || choice.Cards[0] != r.CardId || _cardZones.GetLocation(r.CardId) != r.ResultFrom)
                throw new InvalidOperationException("The original judgment entity is no longer available.");
            var top = branch == "top"; var seat = top ? (int?)null : choice.Targets.Single();
            if (!top && branch != "gift" || top && choice.Targets.Count != 0 || seat is { } recipient && !_players[recipient].IsAlive)
                throw new InvalidOperationException("The judgment destination is no longer legal.");
            var destination = top ? CardLocation.DrawPile : CardLocation.Hand(seat!.Value);
            var card = _cardZones.CardsAt(r.ResultFrom).Single(c => c.Id == r.CardId);
            ClearPendingDecision(); ReplaceRuntimeTop(f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null),
                MatchedJudgmentPlacement = r with { Stage = MatchedJudgmentPlacementStage.Placed, OnTop = top, RecipientSeat = seat } });
            MoveCard(card, r.ResultFrom, destination, new($"skill-program.{f.SkillId}.matched-judgment-place"), moved =>
            {
                if (top) _cardZones.PlaceDrawPileCardsAtTop(Array.AsReadOnly(new[] { card.Id }));
                var active = GetActiveProgramFrame(f.Id);
                ReplaceRuntimeTop(active with { MatchedJudgmentPlacement = active.MatchedJudgmentPlacement! with { PlacementMovementSequence = moved.Sequence } });
                AdvanceEventRulesAndQueueFact(new ProgramMatchedJudgmentPlacedEvent(f.Id, r.JudgmentFrameId, r.CardId, seat, top, moved.Sequence));
            });
            AdvanceRuntimeProgram(f.Id); return;
        }
        if (r.Stage != MatchedJudgmentPlacementStage.ChoosingSelfDiscard || branch != "self-discard" || choice.Targets.Count != 0)
            throw new InvalidOperationException("Judgment placement lost its self-discard tail.");
        var discard = SuitBenefitCosts(f.OwnerSeat, f.SkillId, f.SkillInstanceId).Single(c => c.Id == choice.Cards.Single());
        var from = _cardZones.GetLocation(discard.Id);
        ClearPendingDecision(); ReplaceRuntimeTop(f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null),
            MatchedJudgmentPlacement = r with { Stage = MatchedJudgmentPlacementStage.SelfDiscarded, SelfDiscardCardId = discard.Id, SelfDiscardFrom = from } });
        MoveCard(discard, from, CardLocation.DiscardPile, new($"skill-program.{f.SkillId}.{SkillProgramEffectOp.PlaceMatchedJudgmentCard}"), moved =>
        {
            var active = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(active with { MatchedJudgmentPlacement = active.MatchedJudgmentPlacement! with { SelfDiscardMovementSequence = moved.Sequence } });
            AdvanceEventRulesAndQueueFact(new ProgramMatchedJudgmentSelfDiscardedEvent(f.Id, r.JudgmentFrameId, discard.Id, from, moved.Sequence));
        });
        AdvanceRuntimeProgram(f.Id);
    }
    private bool ResumeMatchedJudgmentPlacement(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.MatchedJudgmentPlacement is not { } r) return false;
        AssertMatchedJudgmentPlacement(f);
        if (_pendingDecision is not null && r.Stage is MatchedJudgmentPlacementStage.ChoosingDestination or MatchedJudgmentPlacementStage.ChoosingSelfDiscard) return true;
        if (DrainSuitPlacementChildren(f)) return true;
        f = GetActiveProgramFrame(id); r = f.MatchedJudgmentPlacement!;
        if (f.PendingMovementContinuation is not null) ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (r.Stage == MatchedJudgmentPlacementStage.ChoosingDestination)
        { PublishMatchedJudgmentPlacement(f); return true; }
        if (r.Stage == MatchedJudgmentPlacementStage.Placed && !r.OnTop && r.RecipientSeat == f.OwnerSeat && _players[f.OwnerSeat].IsAlive && _winner == Winner.None)
        {
            ReplaceRuntimeTop(f with { MatchedJudgmentPlacement = r with { Stage = MatchedJudgmentPlacementStage.ChoosingSelfDiscard } });
            PublishMatchedJudgmentPlacement(GetActiveProgramFrame(id)); return true;
        }
        if (r.Stage == MatchedJudgmentPlacementStage.ChoosingSelfDiscard)
        { PublishMatchedJudgmentPlacement(f); return true; }
        ReplaceRuntimeTop(f with { MatchedJudgmentPlacement = null }); return false;
    }
    private PromptChoice SelectAiMatchedJudgmentPlacement(PendingDecision decision, ProgramSkillFrame f)
    {
        if (f.MatchedJudgmentPlacement!.Stage == MatchedJudgmentPlacementStage.ChoosingSelfDiscard)
            return decision.Choices.OrderBy(c => CardCatalog.Get(_cardZones.CardsAt(_cardZones.GetLocation(c.Cards[0])).Single(x => x.Id == c.Cards[0]).Kind).HandKeepValue).ThenBy(c => c.Cards[0]).First();
        var view = CreateSnapshot(decision.PlayerSeat); var hint = new SkillProgramAiHint(0, 0, 0, 1, 0, 0, true, false);
        return decision.Choices.Where(c => c.Targets.Count == 1).OrderByDescending(c =>
                _aiBrains[decision.PlayerSeat].ScoreProgramTarget(view, c.Targets[0], hint) - (c.Targets[0] == f.OwnerSeat ? 12d : 0d))
            .ThenBy(c => c.Targets[0]).FirstOrDefault() ?? decision.Choices.Single(c => c.Targets.Count == 0);
    }
    private void AssertMatchedJudgmentPlacement(ProgramSkillFrame f)
    {
        if (f.MatchedJudgmentPlacement is not { } r) return;
        var plan = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!);
        var set = GetProgramCardSet(f, r.SourceBind);
        if (!Enum.IsDefined(r.Stage) || !MatchedJudgmentOriginalCandidate(f) || f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } ||
            f.InstructionIndex != 2 || r.InstructionIndex != f.InstructionIndex || plan.Instructions.Count != 2 ||
            plan.Instructions[0].Op != SkillProgramEffectOp.StartJudgment || plan.Instructions[0].ResultBind != r.SourceBind ||
            plan.Instructions[1].Op != SkillProgramEffectOp.PlaceMatchedJudgmentCard || !plan.Instructions[1].Suits.Contains(r.EffectiveSuit) ||
            set.CardIds.Count != 1 || set.CardIds[0] != r.CardId || set.FrozenRevealedSuit != r.EffectiveSuit ||
            set.SourceLocations.Count != 1 || set.SourceLocations[0] != r.ResultFrom || r.ResultFrom != CardLocation.Processing ||
            !CompleteProgramEventHistory().OfType<JudgmentResolvedEvent>().Any(e => e.ResolutionId == r.JudgmentFrameId && e.ParentResolutionId == f.Id &&
                e.TargetSeat == f.OwnerSeat && e.Reason == plan.Instructions[0].JudgmentReason && e.CardId == r.CardId && e.Suit == r.EffectiveSuit) ||
            !_cardMovements.Any(m => m.Sequence == r.ResultMovementSequence && m.CardId == r.CardId && m.From == CardLocation.Judgment(f.OwnerSeat) &&
                m.To == r.ResultFrom && m.Reason.Value == "skill-program.judgment.result"))
            throw new InvalidOperationException("Judgment placement lost its actual original result producer.");
        if (r.Stage == MatchedJudgmentPlacementStage.ChoosingDestination && (r.PlacementMovementSequence != 0 || r.RecipientSeat is not null || r.OnTop || r.SelfDiscardCardId is not null) ||
            r.Stage != MatchedJudgmentPlacementStage.ChoosingDestination &&
            (r.OnTop ? r.RecipientSeat is not null : r.RecipientSeat is null || !IsValidPlayerSeat(r.RecipientSeat.Value)) ||
            r.Stage != MatchedJudgmentPlacementStage.ChoosingDestination &&
            (!_cardMovements.Any(m => m.Sequence == r.PlacementMovementSequence && m.CardId == r.CardId && m.From == r.ResultFrom &&
                m.To == (r.OnTop ? CardLocation.DrawPile : CardLocation.Hand(r.RecipientSeat!.Value)) && m.Reason.Value == $"skill-program.{f.SkillId}.matched-judgment-place") ||
             CompleteProgramEventHistory().OfType<ProgramMatchedJudgmentPlacedEvent>().Count(e => e.ProgramFrameId == f.Id && e.JudgmentFrameId == r.JudgmentFrameId &&
                e.CardId == r.CardId && e.OnTop == r.OnTop && e.RecipientSeat == r.RecipientSeat && e.MovementSequence == r.PlacementMovementSequence) != 1) ||
            r.Stage is MatchedJudgmentPlacementStage.ChoosingSelfDiscard or MatchedJudgmentPlacementStage.SelfDiscarded && (r.OnTop || r.RecipientSeat != f.OwnerSeat) ||
            r.Stage == MatchedJudgmentPlacementStage.SelfDiscarded && (r.SelfDiscardCardId is not { } id || r.SelfDiscardFrom is not { } from ||
                from.OwnerSeat != f.OwnerSeat || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
                !_cardMovements.Any(m => m.Sequence == r.SelfDiscardMovementSequence && m.CardId == id && m.From == from && m.To == CardLocation.DiscardPile &&
                    m.Reason.Value == $"skill-program.{f.SkillId}.{SkillProgramEffectOp.PlaceMatchedJudgmentCard}") ||
                CompleteProgramEventHistory().OfType<ProgramMatchedJudgmentSelfDiscardedEvent>().Count(e => e.ProgramFrameId == f.Id &&
                    e.JudgmentFrameId == r.JudgmentFrameId && e.CardId == id && e.From == from && e.MovementSequence == r.SelfDiscardMovementSequence) != 1))
            throw new InvalidOperationException("Judgment placement changed its once-only destination or self-discard payment.");
        if (ReferenceEquals(f, _resolutionStack.LastOrDefault()) && _pendingDecision is { } prompt &&
            r.Stage is MatchedJudgmentPlacementStage.ChoosingDestination or MatchedJudgmentPlacementStage.ChoosingSelfDiscard &&
            (prompt.PlayerSeat != f.OwnerSeat || !prompt.IsPrivate || !AssistedChoicesEqual(prompt.Choices, MatchedJudgmentPlacementChoices(f))))
            throw new InvalidOperationException("Judgment placement changed its owning private choice.");
    }
}
