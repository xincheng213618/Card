namespace CardGame.Core;

public sealed partial class GameEngine
{
    private readonly Dictionary<(int Turn, int Target, string Skill), long> _firstTurnTargetActions = [];

    // Register actual encounters before looking up enabled grants. Suppression,
    // loss and later re-grant cannot turn the second encounter into the first.
    private void RegisterFirstTurnTargetActions(CardActionContext action, SkillProgramTriggerWindow window)
    {
        if (window != SkillProgramTriggerWindow.CardUseTargetsFinalized || action.Type != CardActionType.Use) return;
        foreach (var skillId in _contentRegistry.ProgramDependencies.GetTriggerOperationSkillIds(SkillProgramEffectOp.NullifyFirstTurnTargetByHand))
        {
            var skill = _contentRegistry.GetSkill(skillId);
            foreach (var trigger in skill.Program!.Triggers.Where(trigger => trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.NullifyFirstTurnTargetByHand)))
            {
                if (!trigger.CardKinds.Contains(action.EffectiveKind)) continue;
                foreach (var target in action.EffectiveDesignatedTargetSeats.Distinct().Where(target => target != action.ActorSeat))
                    _firstTurnTargetActions.TryAdd((_turnNumber, target, skill.Id), action.ActionId);
            }
        }
    }

    private bool IsFirstTurnTarget(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        context.CardUse is { } use && use.ActorSeat != candidate.OwnerSeat && use.EventTargetSeat == candidate.OwnerSeat &&
        _firstTurnTargetActions.GetValueOrDefault((_turnNumber, candidate.OwnerSeat, candidate.SkillId), -1) == use.CardActionId;

    private void NullifyProgramFirstTurnTarget(ProgramSkillFrame frame)
    {
        var context = frame.WindowContext ?? throw new InvalidOperationException("First-target defense requires its actual use.");
        var candidate = new ProgramTriggerCandidate(frame.OwnerSeat, frame.SkillId, frame.TriggerId!, frame.SkillInstanceId, frame.GameplayHash, 0);
        var use = context.CardUse ?? throw new InvalidOperationException("First-target defense requires a card action.");
        var parent = _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(item => item.Id == use.ParentCardUseFrameId);
        if (context.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || parent?.Action?.ActionId != use.CardActionId ||
            !parent.TargetSeats.Contains(frame.OwnerSeat) || !IsFirstTurnTarget(candidate, context))
            throw new InvalidOperationException("First-target defense lost its precise designation parent.");
        if (GetHand(_players[use.ActorSeat]).Count >= GetHand(_players[frame.OwnerSeat]).Count)
        {
            MarkCardEffectIneffective(parent.Id, frame.OwnerSeat);
            AdvanceEventRulesAndQueueFact(new CardEffectSkippedEvent(parent.Id, use.ActorSeat, frame.OwnerSeat, use.EffectiveKind, CardEffectSkipReason.SkillNullified));
        }
    }

    private int ComputeTurnCriterionQuota(int ownerSeat)
    {
        var history = EventsSinceLastBoundary(item => item is TurnStartedEvent).ToArray();
        var ownedDiscard = _cardMovements.Any(record => record.TurnNumber == _turnNumber &&
            GetProgramDiscardSource(record)?.OwnerSeat == ownerSeat);
        return (history.OfType<DamageAppliedEvent>().Any(d => d.SourceSeat == ownerSeat && !d.SourceLess && d.Amount > 0) ? 1 : 0) +
            (ownedDiscard ? 0 : 1) + (_players.Where(p => p.IsAlive).All(p => GetHand(p).Count >= GetHand(_players[ownerSeat]).Count) ? 1 : 0);
    }

    private SkillProgramStepOutcome BeginQuotaTop(ProgramSkillFrame frame, int count)
    {
        if (frame.WindowContext?.Window != SkillProgramTriggerWindow.TurnEnding || frame.OwnerSeat != _currentSeat || frame.QuotaTop is not null)
            throw new InvalidOperationException("Quota peek requires a clean real owner ending window.");
        var quota = ComputeTurnCriterionQuota(frame.OwnerSeat);
        _ = EnsureDrawPile();
        var ids = _cardZones.CardsAt(CardLocation.DrawPile).TakeLast(Math.Min(count, _cardZones.Count(CardLocation.DrawPile)))
            .Reverse().Select(card => card.Id).ToArray();
        frame = frame with { QuotaTop = new(quota, ids, [], [], quota == 0 ? "order" : "gain") };
        ReplaceRuntimeTop(frame);
        PublishQuotaTop(frame);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private bool QuotaTopSourceValid(ProgramSkillFrame frame) => _players[frame.OwnerSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) &&
        EnabledSkillPrograms(_players[frame.OwnerSeat]).Any(program => program.Id == frame.SkillId && program.GameplayHash == frame.GameplayHash);

    private void PublishQuotaTop(ProgramSkillFrame frame)
    {
        var draft = frame.QuotaTop!;
        var remaining = draft.ViewedIds.Except(draft.ObtainedIds).Except(draft.UnavailableIds ?? []).Except(draft.OrderedTopIds).ToArray();
        if (draft.Stage == "gain" && draft.ObtainedIds.Count == Math.Min(draft.Quota, draft.ViewedIds.Count))
        {
            frame = frame with { QuotaTop = draft with { Stage = "gain-movement" } };
            ReplaceRuntimeTop(frame);
            var cards = draft.ObtainedIds.Select(id => _cardZones.CardsAt(CardLocation.DrawPile).Single(card => card.Id == id)).ToArray();
            MoveCards(cards, CardLocation.DrawPile, CardLocation.Hand(frame.OwnerSeat), new("program.quota-top.obtain"));
            if (AwaitProgramBoundCardMovements(frame.Id, frame.OwnerSeat) == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(frame.Id);
            return;
        }
        if (draft.Stage == "order" && remaining.Length == 0)
        {
            var kept = draft.ViewedIds.Except(draft.ObtainedIds).Except(draft.UnavailableIds ?? []).ToArray();
            _cardZones.ReorderDrawPileSubsetToTop(kept, draft.OrderedTopIds);
            if (draft.Quota > 0) { FinishQuotaTop(frame); return; }
            frame = frame with { QuotaTop = draft with { Stage = "loss-choice" } }; ReplaceRuntimeTop(frame);
            draft = frame.QuotaTop!;
        }
        IReadOnlyList<PromptChoice> choices = draft.Stage == "loss-choice"
            ? _players.Where(p => p.IsAlive && p.Seat != frame.OwnerSeat).Select(p => NamedDraftChoice(frame, "quota-top-loss", p.Name, seat: p.Seat)).ToArray()
            : remaining.Select(id =>
            {
                var card = _cardZones.CardsAt(CardLocation.DrawPile).Single(card => card.Id == id);
                return NamedDraftChoice(frame, draft.Stage == "gain" ? "quota-top-gain" : "quota-top-order",
                    $"{(draft.Stage == "gain" ? "获得" : "置于牌堆顶")}【{card.DisplayName}】（{GetSuitDisplayName(card.Suit)} {card.RankText}）", id);
            }).ToArray();
        if (choices.Count == 0) { FinishQuotaTop(frame); return; }
        PublishNamedDraftPrompt(frame, frame.OwnerSeat, draft.Stage switch
        { "gain" => $"获得 {Math.Min(draft.Quota, draft.ViewedIds.Count)} 张牌。", "order" => "依次选择剩余牌：先选的牌在牌堆最上方。", _ => "选择另一名角色，你与其依次失去1点体力。" }, choices, draft.Stage != "loss-choice");
    }

    private void ResolveQuotaTopChoice(PromptChoice choice)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Quota choice requires its program.");
        var draft = frame.QuotaTop ?? throw new InvalidOperationException("Quota choice requires its frozen cards.");
        if (!QuotaTopSourceValid(frame) || choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString())
            throw new InvalidOperationException("Quota choice lost its exact source.");
        var action = choice.Parameters.GetValueOrDefault("program-action");
        if (draft.Stage == "loss-choice" && action == "quota-top-loss" && choice.Targets is [var target] &&
            target != frame.OwnerSeat && _players[target].IsAlive)
            draft = draft with { Stage = "issued-loss", LossTarget = target };
        else if (choice.Cards is [var id] && draft.ViewedIds.Contains(id) && !draft.ObtainedIds.Contains(id) && !draft.OrderedTopIds.Contains(id) &&
            _cardZones.GetLocation(id) == CardLocation.DrawPile)
            draft = (draft.Stage, action) switch
            {
                ("gain", "quota-top-gain") => draft with { ObtainedIds = [..draft.ObtainedIds, id] },
                ("order", "quota-top-order") => draft with { OrderedTopIds = [..draft.OrderedTopIds, id] },
                _ => throw new InvalidOperationException("Quota choice has an incorrect stage.")
            };
        else throw new InvalidOperationException("Quota choice contains no current entity or participant.");
        ClearPendingDecision();
        ReplaceRuntimeTop(frame = frame with { QuotaTop = draft });
        if (draft.Stage == "issued-loss")
        {
            AdvanceEventRulesAndQueueFact(new ProgramQuotaLossIssuedEvent(frame.Id, frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId, draft.LossTarget!.Value));
            ResumeQuotaLoss(frame);
        }
        else PublishQuotaTop(frame);
    }

    private bool TryResumeQuotaTop(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != frameId || frame.QuotaTop is not { } draft) return false;
        if (_winner != Winner.None) { FinishQuotaTop(frame); return true; }
        if (draft.Stage == "issued-loss")
        {
            if (new ProgramSkillHost(this).TryStartPostInstructionWindow(frame.Id)) return true;
            ResumeQuotaLoss(frame); return true;
        }
        if (!QuotaTopSourceValid(frame)) { ReplaceRuntimeTop(frame with { QuotaTop = null }); CancelProgramBindingAndCleanup(frame with { QuotaTop = null }, "私有牌顶操作的来源已失效。"); return true; }
        if (draft.Stage == "gain-movement")
        { ReplaceRuntimeTop(frame = frame with { QuotaTop = draft with { Stage = "order", UnavailableIds = draft.ViewedIds.Except(draft.ObtainedIds).Where(id => _cardZones.GetLocation(id) != CardLocation.DrawPile).ToArray() } }); PublishQuotaTop(frame); return true; }
        return false;
    }

    private void ResumeQuotaLoss(ProgramSkillFrame frame)
    {
        var draft = frame.QuotaTop!;
        // Only this already-issued operation survives its owner's death or loss.
        // The immutable issuance proves both precommitted seats and exact grant.
        var program = _contentRegistry.GetSkill(frame.SkillId).Program!;
        if (program.GameplayHash != frame.GameplayHash || frame.WindowContext?.Window != SkillProgramTriggerWindow.TurnEnding ||
            ProgramInstructionResolver.Default.Resolve(frame, program).GetPausedInstruction(frame.InstructionIndex).Effect.Op != SkillProgramEffectOp.PeekTurnQuotaTop ||
            draft.Stage != "issued-loss" || draft.Quota != 0 || draft.LossTarget is not { } target ||
            !CompleteProgramEventHistory().OfType<ProgramQuotaLossIssuedEvent>().Any(e => e == new ProgramQuotaLossIssuedEvent(frame.Id, frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId, target)))
            throw new InvalidOperationException("Paired HP loss lacks its exact issued commitment.");
        if (_winner != Winner.None || draft.LossCursor >= 2) { FinishQuotaTop(frame); return; }
        var seat = draft.LossCursor == 0 ? frame.OwnerSeat : target;
        ReplaceRuntimeTop(frame = frame with { QuotaTop = draft with { LossCursor = draft.LossCursor + 1 } });
        if (!_players[seat].IsAlive || new ProgramSkillHost(this).LoseHp(frame.Id, frame.SkillId, seat, 1) == SkillProgramStepOutcome.Continue)
            AdvanceRuntimeProgram(frame.Id);
    }
    private void FinishQuotaTop(ProgramSkillFrame frame)
    { ReplaceRuntimeTop(frame with { QuotaTop = null }); AdvanceRuntimeProgram(frame.Id); }
    private void AssertQuotaTop(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (frame.QuotaTop is not { } draft) return;
        if (paused.Op != SkillProgramEffectOp.PeekTurnQuotaTop || frame.WindowContext?.Window != SkillProgramTriggerWindow.TurnEnding ||
            draft.Quota is < 0 or > 3 || draft.ViewedIds.Count > 3 || draft.ViewedIds.Distinct().Count() != draft.ViewedIds.Count ||
            draft.ObtainedIds.Distinct().Count() != draft.ObtainedIds.Count || draft.OrderedTopIds.Distinct().Count() != draft.OrderedTopIds.Count ||
            draft.UnavailableIds is { } unavailable && (unavailable.Distinct().Count() != unavailable.Count || unavailable.Except(draft.ViewedIds.Except(draft.ObtainedIds)).Any()) ||
            draft.ObtainedIds.Count > Math.Min(draft.Quota, draft.ViewedIds.Count) || draft.ObtainedIds.Except(draft.ViewedIds).Any() ||
            draft.OrderedTopIds.Except(draft.ViewedIds.Except(draft.ObtainedIds)).Any() || draft.LossCursor is < 0 or > 2 ||
            draft.Stage is not ("gain" or "gain-movement" or "order" or "loss-choice" or "issued-loss"))
            throw new InvalidOperationException("Quota top lost its exact operation, frozen entities or cursor.");
        if (draft.Stage == "issued-loss" && (draft.Quota != 0 || draft.LossTarget is not { } target || target == frame.OwnerSeat ||
            !IsValidPlayerSeat(target) || !CompleteProgramEventHistory().OfType<ProgramQuotaLossIssuedEvent>().Any(e =>
                e == new ProgramQuotaLossIssuedEvent(frame.Id, frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId, target))))
            throw new InvalidOperationException("Issued quota loss lost its precommitted participants.");
        if (draft.Stage is "gain" or "order" && draft.ViewedIds.Except(draft.ObtainedIds).Except(draft.UnavailableIds ?? []).Any(id => _cardZones.GetLocation(id) != CardLocation.DrawPile))
            throw new InvalidOperationException("The private quota view lost a real draw-pile entity.");
        if (!ReferenceEquals(frame, _resolutionStack.LastOrDefault()) || draft.Stage is "issued-loss" or "gain-movement") return;
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } prompt || prompt.PlayerSeat != frame.OwnerSeat ||
            prompt.IsPrivate != (draft.Stage != "loss-choice") || prompt.Choices.Count == 0 ||
            prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString() ||
                choice.Parameters.GetValueOrDefault("program-action") != "quota-top-" + (draft.Stage == "loss-choice" ? "loss" : draft.Stage)))
            throw new InvalidOperationException("Quota top lost its exact chooser or private decision.");
    }
    private CardSnapshot[] GetQuotaPrivatelyViewedCards(int viewerSeat) => _resolutionStack.OfType<ProgramSkillFrame>()
        .Where(frame => frame.OwnerSeat == viewerSeat && frame.QuotaTop is { Stage: "gain" or "order" })
        .SelectMany(frame => frame.QuotaTop!.ViewedIds.Except(frame.QuotaTop.ObtainedIds))
        .Where(id => _cardZones.GetLocation(id) == CardLocation.DrawPile)
        .Select(id => ToSnapshot(_cardZones.CardsAt(CardLocation.DrawPile).Single(card => card.Id == id))).ToArray();
    private sealed partial class ProgramSkillHost : IQuotaTopProgramHost
    {
        public SkillProgramStepOutcome PeekTurnQuotaTop(ProgramSkillFrame frame, int count) => engine.BeginQuotaTop(frame, count);
        public void NullifyFirstTurnTargetByHand(ProgramSkillFrame frame) => engine.NullifyProgramFirstTurnTarget(frame);
    }
}
public sealed record ProgramQuotaLossIssuedEvent(long FrameId, int OwnerSeat, string SkillId, string SkillInstanceId, int OtherSeat) : IGameEvent;
