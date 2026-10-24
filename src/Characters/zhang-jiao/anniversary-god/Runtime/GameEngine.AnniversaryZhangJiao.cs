using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string AnniversaryYizhao = "anniversary-god:yizhao", AnniversarySanshou = "anniversary-god:sanshou",
        AnniversarySijun = "anniversary-god:sijun", AnniversaryTianjie = "anniversary-god:tianjie";
    private const string HappyZhangJiaoYizhao = "happy:yizhao", HappyZhangJiaoSanshou = "happy:sanshou",
        HappyZhangJiaoSijun = "happy:sijun", HappyZhangJiaoTianjie = "happy:tianjie";
    private static CardConversionSource AnniversaryZjSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool AnniversaryZjTurnShuffled() => EventsSinceLastBoundary(e => e is TurnStartedEvent).Any(e =>
        e is CardMovedEvent { Reason: var reason } && reason == CardMoveReasons.Reshuffle ||
        e is AnniversaryZhangJiaoShuffledEvent s && s.TurnNumber == _turnNumber && s.TurnOwnerSeat == _currentSeat ||
        e is MobileShiHuanJieShuffledEvent);
    private bool HasAnniversaryTianjieDeadOwnerEnding(int actualOwner) => IsValidPlayerSeat(actualOwner) && actualOwner == _currentSeat &&
        !_players[actualOwner].IsAlive && _winner == Winner.None && AnniversaryZjTurnShuffled() &&
        _players.Any(p => p.Seat != actualOwner && p.IsAlive && (HasRuntimeSkill(p, AnniversaryTianjie) || HasRuntimeSkill(p, HappyZhangJiaoTianjie)));
    private bool IsAnniversaryTianjieDeadOwnerEndingCandidate(ProgramTriggerCandidate candidate, int actualOwner) =>
        HasAnniversaryTianjieDeadOwnerEnding(actualOwner) && candidate.SkillId is (AnniversaryTianjie or HappyZhangJiaoTianjie) &&
        IsValidPlayerSeat(candidate.OwnerSeat) && candidate.OwnerSeat != actualOwner && _players[candidate.OwnerSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[candidate.OwnerSeat], candidate.SkillId, candidate.SkillInstanceId) &&
        GetProgramTrigger(candidate) is { Window: SkillProgramTriggerWindow.TurnEnding, TurnOwnerScope: SkillProgramTurnOwnerScope.OtherLiving,
            Effects: [{ Op: SkillProgramEffectOp.ResolveAnniversaryZhangJiaoTianjie }] };
    private int AnniversaryZjRank(int owner, ProgramSkillWindowContext? context)
    {
        if (context is { Window: SkillProgramTriggerWindow.DiscardPileReceived, MovementBatch: { } batch, MovementIndex: { } index })
        {
            if (index < 0 || index >= batch.Movements.Count) return 0;
            var m = batch.Movements[index];
            return GetProgramDiscardSource(m)?.OwnerSeat == owner && m.To == CardLocation.DiscardPile && _cardMovements.Contains(m)
                ? GetAdvancedCard(m.CardId).Rank : 0;
        }
        if (context?.Window is not (SkillProgramTriggerWindow.CardUseCommitted or SkillProgramTriggerWindow.CardResponseAccepted)) return 0;
        var action = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault()?.Action ??
            _resolutionStack.OfType<CardUseFrame>().LastOrDefault(f => f.Action?.ActionId == context.CardUse?.CardActionId)?.Action;
        if (action?.ActorSeat != owner || action.ActionId != context.CardUse?.CardActionId) return 0;
        return action.EffectiveRank ?? (action.PhysicalCards.Count == 1 ? GetAdvancedCard(action.PhysicalCards[0].CardId).Rank : 0);
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 39400)]
    private bool CanTriggerAnniversaryZj(ProgramTriggerLegalityContext g) => g.SkillId switch
    {
        AnniversaryYizhao or HappyZhangJiaoYizhao => AnniversaryZjRank(g.OwnerSeat, g.WindowContext) > 0,
        AnniversarySijun or HappyZhangJiaoSijun => g.Owner.Markers.GetValueOrDefault(PlayerMarkerKind.Huang) > _cardZones.Count(CardLocation.DrawPile),
        AnniversaryTianjie or HappyZhangJiaoTianjie => AnniversaryZjTurnShuffled() && _players.Any(p => p.IsAlive && p.Seat != g.OwnerSeat),
        _ => true
    };
    [ProgramCapability]
    private sealed class AnniversaryZhangJiaoCapability(GameEngine engine) : IAnniversaryZhangJiaoHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffect e) => engine.BeginAnniversaryZj(f, e); }
    private SkillProgramStepOutcome BeginAnniversaryZj(ProgramSkillFrame supplied, SkillProgramEffect e)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (e.Op == SkillProgramEffectOp.ResolveAnniversaryZhangJiaoSanshou) return ResolveAnniversarySanshou(f, e);
        if (f.AnniversaryZhangJiao is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0)
            throw new InvalidOperationException("Anniversary Zhang Jiao requires his original unpaid instruction.");
        var r = new AnniversaryZhangJiaoReceipt(AnniversaryZjSource(f), f.GameplayHash, e.Op, f.InstructionIndex, AnniversaryZhangJiaoStage.Obtained,
            Before: _movementSequence, After: _movementSequence, RandomBefore: _random.State, RandomAfter: _random.State);
        if (e.Op == SkillProgramEffectOp.ResolveAnniversaryZhangJiaoYizhao)
        {
            var rank = AnniversaryZjRank(f.OwnerSeat, f.WindowContext); if (rank <= 0) return SkillProgramStepOutcome.Continue;
            var before = _players[f.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.Huang); var after = checked(before + rank);
            var tens = after / 10 % 10;
            var matches = before / 10 % 10 == tens ? [] : _cardZones.CardsAt(CardLocation.DrawPile).Where(c => c.Rank == tens).ToArray();
            var cards = matches.Length == 0 ? [] : new[] { matches[_random.Next(matches.Length)].Id };
            r = r with { Rank = rank, MarkerBefore = before, MarkerAfter = after, Cards = cards, RandomAfter = _random.State };
            ReplaceRuntimeTop(f = f with { AnniversaryZhangJiao = r });
            SetStrategicMarker(f.OwnerSeat, PlayerMarkerKind.Huang, f.OwnerSeat, after, f);
            ObtainAnniversaryZjCards(f);
            AdvanceEventRulesAndQueueFact(new AnniversaryYizhaoAccumulatedEvent(f.Id, r.Source, rank, before, after, cards.Length));
        }
        else if (e.Op == SkillProgramEffectOp.ResolveAnniversaryZhangJiaoSijun)
        {
            var before = _players[f.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.Huang);
            if (f.WindowContext?.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || before <= _cardZones.Count(CardLocation.DrawPile))
                throw new InvalidOperationException("Sijun requires its actual preparation and strictly more Huang than draw-pile cards.");
            var pool = f.SkillId == HappyZhangJiaoSijun ? _cardZones.CardsAt(CardLocation.DrawPile).ToArray() :
                _cardZones.CardsAt(CardLocation.DrawPile).Concat(_cardZones.CardsAt(CardLocation.DiscardPile)).ToArray();
            var cards = SampleAnniversarySijun(pool);
            r = r with { MarkerBefore = before, Cards = cards, Rank = cards.Sum(id => GetAdvancedCard(id).Rank), RandomAfter = _random.State };
            ReplaceRuntimeTop(f = f with { AnniversaryZhangJiao = r });
            SetStrategicMarker(f.OwnerSeat, PlayerMarkerKind.Huang, f.OwnerSeat, 0, f);
            ObtainAnniversaryZjCards(f);
            AdvanceEventRulesAndQueueFact(new AnniversarySijunObtainedEvent(f.Id, r.Source, before, cards.Length, r.Rank));
        }
        else
        {
            if (e.Op != SkillProgramEffectOp.ResolveAnniversaryZhangJiaoTianjie || f.WindowContext?.Window != SkillProgramTriggerWindow.TurnEnding || !AnniversaryZjTurnShuffled())
                throw new InvalidOperationException("Tianjie requires an actual shuffle in this ending turn.");
            ReplaceRuntimeTop(f with { AnniversaryZhangJiao = r with { Stage = AnniversaryZhangJiaoStage.Choosing } });
        }
        ContinueAnniversaryZj(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private SkillProgramStepOutcome ResolveAnniversarySanshou(ProgramSkillFrame f, SkillProgramEffect e)
    {
        if (f.SkillId is not (AnniversarySanshou or HappyZhangJiaoSanshou) || f.WindowContext is not { Window: SkillProgramTriggerWindow.BeforeDamageApplied, TargetSeat: var victim } || victim != f.OwnerSeat)
            throw new InvalidOperationException("Sanshou requires its original damage-target window.");
        if (e.SourceBind is null)
        {
            if (f.InstructionIndex != 1 || f.AnniversaryZhangJiao is not null) throw new InvalidOperationException("Sanshou already froze its actual-use categories.");
            var mask = ProgramEventHistory<CurrentTurnCardUseKindsRecordedEvent>().Where(x => x.TurnNumber == _turnNumber && x.TurnOwnerSeat == _currentSeat)
                .Aggregate(0, (m, x) => m | x.CardCategoryMask);
            ReplaceRuntimeTop(f with { AnniversaryZhangJiao = new(AnniversaryZjSource(f), f.GameplayHash, e.Op, 1, AnniversaryZhangJiaoStage.SanshouSnapshot, UsedCategories: mask) });
            return SkillProgramStepOutcome.Continue;
        }
        if (f.InstructionIndex != 3 || e.SourceBind != "sanshou-top" || f.AnniversaryZhangJiao is not { Stage: AnniversaryZhangJiaoStage.SanshouSnapshot } r)
            throw new InvalidOperationException("Sanshou lost its snapshot before the native reveal.");
        var shown = GetProgramCardSet(f, e.SourceBind);
        if (shown.Visibility != SkillProgramCardSetVisibility.Public || shown.CardIds.Count > 3 || shown.CardIds.Any(id => _cardZones.GetLocation(id) != CardLocation.Processing))
            throw new InvalidOperationException("Sanshou requires the actual publicly revealed processing entities.");
        var prevented = shown.CardIds.Any(id => (r.UsedCategories & (1 << (int)GetProgramCardCategory(GetAdvancedCard(id).Kind))) == 0);
        if (prevented) PreventProgramCurrentDamage(f);
        AdvanceEventRulesAndQueueFact(new AnniversarySanshouResolvedEvent(f.Id, r.Source, r.UsedCategories, prevented));
        ReplaceRuntimeTop(f with { AnniversaryZhangJiao = null });
        return SkillProgramStepOutcome.Continue;
    }
    private int[] SampleAnniversarySijun(IReadOnlyList<Card> pool)
    {
        var remaining = 36; var available = pool.Where(c => c.Rank > 0 && c.Rank <= remaining).ToList(); var selected = new List<int>();
        if (!RankSubsetSearch.CanComplete(available.Select(c => c.Rank), remaining)) return [];
        while (remaining > 0)
        {
            var feasible = available.Where(c => c.Rank <= remaining && RankSubsetSearch.CanComplete(available.Where(o => o.Id != c.Id).Select(o => o.Rank), remaining - c.Rank)).ToArray();
            var card = feasible[_random.Next(feasible.Length)]; selected.Add(card.Id); available.Remove(card); remaining -= card.Rank;
        }
        return selected.ToArray();
    }
    private void ObtainAnniversaryZjCards(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.AnniversaryZhangJiao!;
        List<CardMovementRecord>? happyPayments = f.SkillId == HappyZhangJiaoSijun ? [] : null;
        if (r.Cards.Count > 0)
        {
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            foreach (var id in r.Cards)
            {
                var from = _cardZones.GetLocation(id);
                if (from != CardLocation.DrawPile && (r.Op != SkillProgramEffectOp.ResolveAnniversaryZhangJiaoSijun ||
                    f.SkillId != AnniversarySijun || from != CardLocation.DiscardPile))
                    throw new InvalidOperationException("Zhang Jiao lost a randomly selected physical pile entity.");
                MoveCard(GetAdvancedCard(id), from, CardLocation.Hand(f.OwnerSeat), new("skill.anniversary-zhang-jiao.obtain"),
                    movement => happyPayments?.Add(movement), disclosure: CardMovementDisclosure.FaceDown);
            }
        }
        ReplaceRuntimeTop(GetActiveProgramFrame(f.Id) with { AnniversaryZhangJiao = r with { After = _movementSequence,
            HappySijunPayment = happyPayments is null ? null : new(r.Before, _movementSequence) { Movements = happyPayments } } });
    }
    private bool DrainAnniversaryZj(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueAnniversaryZj(long id)
    {
        while (_pendingDecision is null && _resolutionStack.LastOrDefault() is ProgramSkillFrame { AnniversaryZhangJiao: { } r } f && f.Id == id)
        {
            if (r.Stage is AnniversaryZhangJiaoStage.Obtained or AnniversaryZhangJiaoStage.Shuffled)
            {
                if (DrainAnniversaryZj(id)) return;
                f = GetActiveProgramFrame(id); r = f.AnniversaryZhangJiao!;
                if (r.Op == SkillProgramEffectOp.ResolveAnniversaryZhangJiaoSijun && r.Stage == AnniversaryZhangJiaoStage.Obtained)
                {
                    ReplaceRuntimeTop(f = f with { AnniversaryZhangJiao = r with { Stage = AnniversaryZhangJiaoStage.Shuffled, Before = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
                    MoveAllCards(CardLocation.DiscardPile, CardLocation.DrawPile, CardMoveReasons.Reshuffle);
                    _cardZones.Shuffle(CardLocation.DrawPile, _random);
                    ReplaceRuntimeTop(GetActiveProgramFrame(id) with { AnniversaryZhangJiao = f.AnniversaryZhangJiao! with { After = _movementSequence } });
                    AdvanceEventRulesAndQueueFact(new AnniversaryZhangJiaoShuffledEvent(id, r.Source, _turnNumber, _currentSeat));
                    continue;
                }
                FinishAnniversaryZj(f); return;
            }
            if (r.Stage == AnniversaryZhangJiaoStage.Choosing) { PublishAnniversaryTianjie(f); return; }
            if (r.Stage == AnniversaryZhangJiaoStage.Damaging)
            {
                if (f.AttackAttempt is not null) return;
                if (DrainAnniversaryZj(id)) return;
                if (r.Cursor == r.Targets.Count || _winner != Winner.None) { FinishAnniversaryZj(GetActiveProgramFrame(id)); return; }
                var target = r.Targets[r.Cursor];
                ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { AnniversaryZhangJiao = r with { Cursor = r.Cursor + 1 } });
                if (!_players[target].IsAlive) continue;
                var amount = Math.Max(1, GetHand(_players[target]).Count(c => AdvancedEffectiveHandKind(_players[target], c) == CardKind.Dodge));
                BeginProgramSkillDamage(f, target, amount, nature: DamageNature.Thunder); return;
            }
            return;
        }
    }
    private void FinishAnniversaryZj(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f with { AnniversaryZhangJiao = null, PendingMovementContinuation = null }); AdvanceRuntimeProgram(f.Id); }
    private PromptChoice AnniversaryTianjieChoice(ProgramSkillFrame f, int? target) => new(new($"anniversary-tianjie.{f.Id}.{f.AnniversaryZhangJiao!.Targets.Count}.{target?.ToString(CultureInfo.InvariantCulture) ?? "finish"}"),
        target is { } s ? "选择" + _players[s].Name : "结束选择", [], target is { } seat ? [seat] : [],
        new Dictionary<string, string> { ["program-action"] = "anniversary-tianjie", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["option"] = target?.ToString(CultureInfo.InvariantCulture) ?? "finish" });
    private IReadOnlyList<PromptChoice> AnniversaryTianjieChoices(ProgramSkillFrame f) => f.AnniversaryZhangJiao!.Targets.Count >= 3 ? [AnniversaryTianjieChoice(f, null)] :
        _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat && !f.AnniversaryZhangJiao.Targets.Contains(p.Seat)).OrderBy(p => RelativeTableOrder(_currentSeat, p.Seat))
            .Select(p => AnniversaryTianjieChoice(f, p.Seat)).Append(AnniversaryTianjieChoice(f, null)).ToArray();
    private void PublishAnniversaryTianjie(ProgramSkillFrame f)
    {
        var choices = AnniversaryTianjieChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "请选择至多三名其他角色承受天劫。", [], choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray()), SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("anniversary-tianjie")]
    private void AnswerAnniversaryTianjie(PromptChoice choice)
    {
        var f = GetActiveProgramFrame(_resolutionStack[^1].Id); var r = f.AnniversaryZhangJiao ?? throw new InvalidOperationException("Tianjie lost its original choice frame.");
        if (r.Stage != AnniversaryZhangJiaoStage.Choosing || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !AnniversaryTianjieChoices(f).Any(c => AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Tianjie requires its exact currently published target choice.");
        ClearPendingDecision();
        if (choice.Parameters["option"] == "finish")
            r = r with { Stage = AnniversaryZhangJiaoStage.Damaging, Targets = r.Targets.OrderBy(s => RelativeTableOrder(_currentSeat, s)).ToArray() };
        else r = r with { Targets = r.Targets.Append(choice.Targets.Single()).ToArray() };
        ReplaceRuntimeTop(f with { AnniversaryZhangJiao = r }); ContinueAnniversaryZj(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryZhangJiaoTianjie)]
    private PromptChoice ChooseAnniversaryTianjie(PendingDecision p, ProgramSkillFrame f) => p.Choices[0];
    [ProgramContinuation(ProgramContinuationKind.Resume, 39400)]
    private bool ResumeAnniversaryZj(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryZhangJiao: { } r } f || f.Id != id || r.Stage == AnniversaryZhangJiaoStage.SanshouSnapshot) return false; ContinueAnniversaryZj(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 39400)]
    private bool ReturnAnniversaryZjMovement(ProgramSkillFrame f) => f.AnniversaryZhangJiao is { Stage: not AnniversaryZhangJiaoStage.SanshouSnapshot } && ResumeAnniversaryZj(f.Id);
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 39400)]
    private bool AnniversaryZjMovementProof(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        f.AnniversaryZhangJiao is { Stage: AnniversaryZhangJiaoStage.Obtained or AnniversaryZhangJiaoStage.Shuffled } && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 39400)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 39400)]
    private bool ContinueIssuedAnniversaryZj(ProgramSkillFrame f) => f.AnniversaryZhangJiao is not null;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 39400)]
    private void AssertAnniversaryZj(ProgramSkillFrame f)
    {
        if (f.AnniversaryZhangJiao is not { } r) return;
        if (r.Source != AnniversaryZjSource(f) || r.GameplayHash != f.GameplayHash || r.InstructionIndex != 1 ||
            !Enum.IsDefined(r.Stage) || r.Cursor < 0 || r.Cursor > r.Targets.Count || r.Targets.Count > 3 || r.Targets.Distinct().Count() != r.Targets.Count ||
            r.Targets.Any(s => !IsValidPlayerSeat(s) || s == f.OwnerSeat) || r.Cards.Distinct().Count() != r.Cards.Count || r.Before < 0 || r.After < r.Before ||
            (r.Stage == AnniversaryZhangJiaoStage.SanshouSnapshot ? f.SkillId is not (AnniversarySanshou or HappyZhangJiaoSanshou) || f.InstructionIndex is < 1 or > 3 || r.Op != SkillProgramEffectOp.ResolveAnniversaryZhangJiaoSanshou : f.InstructionIndex != r.InstructionIndex) ||
            r.Op == SkillProgramEffectOp.ResolveAnniversaryZhangJiaoYizhao && (f.SkillId is not (AnniversaryYizhao or HappyZhangJiaoYizhao) || r.Rank <= 0 || r.MarkerAfter != r.MarkerBefore + r.Rank || r.Cards.Count > 1) ||
            r.Op == SkillProgramEffectOp.ResolveAnniversaryZhangJiaoSijun && (f.SkillId is not (AnniversarySijun or HappyZhangJiaoSijun) || r.MarkerBefore <= 0 || r.Rank is not (0 or 36)) ||
            r.Op == SkillProgramEffectOp.ResolveAnniversaryZhangJiaoTianjie && f.SkillId is not (AnniversaryTianjie or HappyZhangJiaoTianjie) ||
            f.SkillId == HappyZhangJiaoSijun && !HasHappySijunDrawPilePayment(f, r) ||
            f.SkillId != HappyZhangJiaoSijun && r.HappySijunPayment is not null)
            throw new InvalidOperationException("Anniversary Zhang Jiao lost his exact source, frozen categories, random entities, paid marker or target cursor.");
    }
    private bool HasHappySijunDrawPilePayment(ProgramSkillFrame f, AnniversaryZhangJiaoReceipt r)
    {
        if (r.Op != SkillProgramEffectOp.ResolveAnniversaryZhangJiaoSijun || r.HappySijunPayment is not { } paid ||
            paid.Before < 0 || paid.After < paid.Before || paid.After > _movementSequence || paid.Before > r.Before ||
            paid.Movements is not System.Collections.IList { IsReadOnly: true } ||
            !paid.Movements.Select(m => m.CardId).SequenceEqual(r.Cards) ||
            paid.Movements.Any(m => m.From != CardLocation.DrawPile || m.Sequence <= paid.Before || m.Sequence > paid.After ||
                m.Reason.Value != "skill.anniversary-zhang-jiao.obtain" || _cardMovements.Count(actual => actual == m) != 1 ||
                m.To != NormalizeEphemeralCardDestination(GetAdvancedCard(m.CardId), CardLocation.DrawPile, CardLocation.Hand(f.OwnerSeat)))) return false;
        return r.Stage == AnniversaryZhangJiaoStage.Obtained ? paid.Before == r.Before && paid.After == r.After :
            r.Stage == AnniversaryZhangJiaoStage.Shuffled && paid.After <= r.Before;
    }
}
