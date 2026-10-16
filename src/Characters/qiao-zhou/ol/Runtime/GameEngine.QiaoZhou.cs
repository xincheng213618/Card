namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string QiaoZhouTableBind = "xingbu-public-three";
    private const string QiaoZhouRevealReason = "skill-program.ol:xingbu.forecast.reveal";
    private const string QiaoZhouCleanupReason = "skill-program.ol:xingbu.forecast.discard";
    private const string QiaoZhouPreparationReason = "skill-program.ol:xingbu.preparation-discard";
    private bool HasQiaoZhouCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveXingbuThreeCardForecast);
    private CardConversionSource QiaoZhouSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    [ProgramCapability]
    private sealed class QiaoZhouCapability(GameEngine engine) : IQiaoZhouProgramHost
    { public SkillProgramStepOutcome Forecast(ProgramSkillFrame frame) => engine.BeginQiaoZhouForecast(frame); }

    private QiaoZhouForecastGrantedEvent[] QiaoZhouMarks()
    {
        if (!HasQiaoZhouCapability) return [];
        var expired = ProgramEventHistory<QiaoZhouForecastExpiredEvent>().Select(e => e.GrantId).ToHashSet();
        return ProgramEventHistory<QiaoZhouForecastGrantedEvent>().Where(e => !expired.Contains(e.GrantId)).ToArray();
    }
    private QiaoZhouForecastActivatedEvent? QiaoZhouActivation(long grantId) =>
        ProgramEventHistory<QiaoZhouForecastActivatedEvent>().SingleOrDefault(e => e.GrantId == grantId);
    private bool QiaoZhouRecipientAvailable(int owner, int recipient) => IsValidPlayerSeat(recipient) && recipient != owner &&
        _players[recipient].IsAlive && !QiaoZhouMarks().Any(e => e.RecipientSeat == recipient);

    private SkillProgramStepOutcome BeginQiaoZhouForecast(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.QiaoZhouForecast is not null || f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } context ||
            _resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(p => p.Id == context.ParentFrameId) is not { } ending ||
            ending.OwnerSeat != f.OwnerSeat || ending.TurnNumber != _turnNumber || f.OwnerSeat != _currentSeat)
            throw new InvalidOperationException("Xingbu requires its owner's actual ending boundary.");
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { QiaoZhouForecast = new(QiaoZhouSource(f), f.GameplayHash, f.InstructionIndex,
            ending.Id, QiaoZhouForecastStage.RevealChildren, 0, before, before) });
        RevealProgramTopCards(f.Id, f.OwnerSeat, 3, null, QiaoZhouTableBind, SkillProgramCardSetVisibility.Public);
        f = GetActiveProgramFrame(f.Id);
        var cards = GetProgramCardSet(f, QiaoZhouTableBind).CardIds.Select(id => new QiaoZhouRevealedCard(id,
            GetAdvancedCard(id).Suit, _cardMovements.Last(m => m.CardId == id).Sequence)).ToArray();
        ReplaceRuntimeTop(f with { QiaoZhouForecast = f.QiaoZhouForecast! with { Cards = cards,
            RedCount = cards.Count(c => IsRedSuit(c.Suit)), SequenceAfter = _movementSequence } });
        ContinueQiaoZhouForecast(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private bool DrainQiaoZhouChildren(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueQiaoZhouForecast(long id)
    {
        while (_resolutionStack.LastOrDefault() is ProgramSkillFrame { QiaoZhouForecast: { } r } f && f.Id == id)
        {
            if (DrainQiaoZhouChildren(id)) return;
            f = GetActiveProgramFrame(id); r = f.QiaoZhouForecast!;
            if (r.Stage == QiaoZhouForecastStage.CleanupChildren)
            { ReplaceRuntimeTop(f with { QiaoZhouForecast = null }); AdvanceRuntimeProgram(id); return; }
            if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { CleanupQiaoZhouForecast(f); return; }
            if (r.Stage == QiaoZhouForecastStage.RevealChildren)
                ReplaceRuntimeTop(f = f with { QiaoZhouForecast = r with { Stage = QiaoZhouForecastStage.Recipient } });
            PublishQiaoZhouForecast(f); return;
        }
    }
    private IReadOnlyList<PromptChoice> QiaoZhouForecastChoices(ProgramSkillFrame f)
    {
        var r = f.QiaoZhouForecast!;
        var result = _players.Where(p => QiaoZhouRecipientAvailable(f.OwnerSeat, p.Seat)).Select(p => new PromptChoice(
            new($"qiao-zhou.{f.Id}.recipient.{p.Seat}"), $"令{p.Name}于下个行动回合获得星卜{Math.Max(1, r.RedCount)}效果", [], [p.Seat],
            new Dictionary<string, string> { ["program-action"] = "qiao-zhou-forecast", ["option"] = "recipient" })).ToList();
        result.Add(new(new($"qiao-zhou.{f.Id}.finish"), "不赋予星卜效果", [], [],
            new Dictionary<string, string> { ["program-action"] = "qiao-zhou-forecast", ["option"] = "finish" }));
        return Array.AsReadOnly(result.ToArray());
    }
    private void PublishQiaoZhouForecast(ProgramSkillFrame f)
    {
        var skill = _contentRegistry.GetSkill(f.SkillId); var choices = QiaoZhouForecastChoices(f);
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, $"星卜：已亮出{f.QiaoZhouForecast!.RedCount}张红色牌，可选择另一名角色。", [],
            choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(skill.Id, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("qiao-zhou-forecast")]
    private void AnswerQiaoZhouForecast(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { QiaoZhouForecast.Stage: QiaoZhouForecastStage.Recipient } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual([choice], QiaoZhouForecastChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Xingbu lost its exact living, unmarked recipient choice.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (choice.Parameters["option"] == "recipient")
            AdvanceEventRulesAndQueueFact(new QiaoZhouForecastGrantedEvent(f.Id, QiaoZhouSource(f), f.GameplayHash,
                choice.Targets.Single(), _turnNumber, f.QiaoZhouForecast!.RedCount));
        CleanupQiaoZhouForecast(f);
    }
    private void CleanupQiaoZhouForecast(ProgramSkillFrame f)
    {
        if (f.QiaoZhouForecast!.Stage != QiaoZhouForecastStage.CleanupChildren)
        {
            var r = f.QiaoZhouForecast; var before = _movementSequence;
            var ids = r.Cards.Where(c => _cardZones.GetLocation(c.CardId) == CardLocation.Processing &&
                _cardMovements.Last(m => m.CardId == c.CardId).Sequence == c.MovementSequence).Select(c => c.CardId).ToArray();
            ReplaceRuntimeTop(f = f with { QiaoZhouForecast = r with { Stage = QiaoZhouForecastStage.CleanupChildren,
                SequenceBefore = before, SequenceAfter = before } });
            MoveProgramCardsFromMultipleSources(ids, CardLocation.DiscardPile, new(QiaoZhouCleanupReason));
            f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { QiaoZhouForecast = f.QiaoZhouForecast! with { SequenceAfter = _movementSequence } });
        }
        ContinueQiaoZhouForecast(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 26220)]
    private bool ResumeQiaoZhouForecast(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { QiaoZhouForecast: not null } f || f.Id != id) return false; ContinueQiaoZhouForecast(id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 26220)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 26220)]
    private bool ContinueIssuedQiaoZhouForecast(ProgramSkillFrame f) => f.QiaoZhouForecast is not null;
    [ProgramAiChoice(SkillProgramEffectOp.ResolveXingbuThreeCardForecast)]
    private PromptChoice AiQiaoZhouForecast(PendingDecision p, ProgramSkillFrame f) =>
        p.Choices.FirstOrDefault(c => c.Parameters["option"] == "recipient") ?? p.Choices.Single(c => c.Parameters["option"] == "finish");

    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 26220)]
    private void ExpireQiaoZhouForecasts(IGameEvent payload)
    {
        if (!HasQiaoZhouCapability || payload is not (PlayerDiedEvent or TurnEndedEvent)) return;
        foreach (var grant in QiaoZhouMarks())
        {
            var expired = payload is PlayerDiedEvent died && (died.VictimSeat == grant.Source.OwnerSeat || died.VictimSeat == grant.RecipientSeat) ||
                payload is TurnEndedEvent ended && QiaoZhouActivation(grant.GrantId) is { } active &&
                active.ActualTurnNumber == ended.TurnNumber && active.RecipientSeat == ended.ActorSeat;
            if (!expired) continue;
            AdvanceEventRulesAndQueueFact(new QiaoZhouForecastExpiredEvent(grant.GrantId, grant.RecipientSeat, _turnNumber,
                payload is PlayerDiedEvent ? "participant-death" : "recipient-turn-ended"));
            var ids = _turnCardUseEffects.RevokeQiaoZhouForecastModifiers(grant.GrantId);
            if (ids.Count > 0) AdvanceEventRulesAndQueueFact(new TurnCardUseEffectsExpiredEvent(_turnNumber, _currentSeat, ids));
        }
    }
    private bool TryBeginQiaoZhouPreparation(CharacterState current)
    {
        if (!HasQiaoZhouCapability) return false;
        var grants = QiaoZhouMarks().Where(g => g.RecipientSeat == current.Seat && QiaoZhouActivation(g.GrantId) is null).ToArray();
        if (grants.Length == 0) return false;
        if (grants.Length != 1 || current.Seat != _currentSeat || current.IsFaceDown || !current.IsAlive ||
            _resolutionStack.Count != 0 || _pendingDecision is not null || grants[0].IssuedTurn >= _turnNumber)
            throw new InvalidOperationException("Xingbu lost its one pending mark at the recipient's actual preparation.");
        var grant = grants.Single();
        AdvanceEventRulesAndQueueFact(new QiaoZhouForecastActivatedEvent(grant.GrantId, current.Seat, _turnNumber));
        if (grant.RedCount >= 2)
        {
            var source = new CardUseEffectSource(grant.Source.SkillId, grant.Source.BindingId, grant.Source.OwnerSeat, grant.Source.SkillInstanceId);
            void Add(SkillRuleQuery query, int amount, int index)
            {
                var modifier = _turnCardUseEffects.GrantRuleModifier(_turnNumber, _currentSeat, grant.GrantId, index,
                    source, query, SkillRuleOperation.Add, amount, affectedSeat: current.Seat);
                AdvanceEventRulesAndQueueFact(new TurnRuleModifierGrantedEvent(modifier));
            }
            Add(SkillRuleQuery.SlashLimit, grant.RedCount == 3 ? 1 : -1, 0);
            if (grant.RedCount == 3) Add(SkillRuleQuery.DrawCount, 2, 1);
            return false;
        }
        var frame = new QiaoZhouPreparationFrame(++_resolutionSequence, current.Seat, _turnNumber, grant);
        PushRuntimeFrame(frame); ContinueQiaoZhouPreparation(frame.Id); return true;
    }
    private bool TrySkipQiaoZhouDiscardPhase(CharacterState current)
    {
        if (!HasQiaoZhouCapability) return false;
        var mark = QiaoZhouMarks().SingleOrDefault(g => g.RecipientSeat == current.Seat && g.RedCount == 2 &&
            QiaoZhouActivation(g.GrantId) is { } a && a.ActualTurnNumber == _turnNumber);
        if (mark is null) return false;
        AdvanceEventRulesAndQueueFact(new QiaoZhouDiscardPhaseSkippedEvent(mark.GrantId, current.Seat, _turnNumber)); return true;
    }
}
