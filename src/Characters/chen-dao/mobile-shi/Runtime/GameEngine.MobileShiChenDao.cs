using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShiCdWanglie = "mobile:shi-wanglie", ShiCdHongyi = "mobile:shi-hongyi";
    private static CardConversionSource ShiCdSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private int ShiCdMarks(int owner) => ProgramEventHistory<MobileShiHongyiMarksChangedEvent>().LastOrDefault(e => e.Source.OwnerSeat == owner)?.After ?? 0;
    private void ChangeShiCdMarks(ProgramSkillFrame f, int amount)
    {
        var before = ShiCdMarks(f.OwnerSeat); var after = amount < 0 ? 0 : Math.Min(4, before + amount);
        AdvanceEventRulesAndQueueFact(new MobileShiHongyiMarksChangedEvent(f.Id, ShiCdSource(f), before, after));
    }
    private MobileShiHongyiPreparedEvent? ShiCdPendingEnding(int owner) => ProgramEventHistory<MobileShiHongyiPreparedEvent>().SingleOrDefault(e =>
        e.Source.OwnerSeat == owner && e.ActualTurn == _turnNumber &&
        !ProgramEventHistory<MobileShiHongyiEndingEvent>().Any(x => x.PreparationId == e.FrameId));

    [ProgramCapability]
    private sealed class MobileShiChenDaoHost(GameEngine engine) : IMobileShiChenDaoHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.ResolveShiCd(f, op); }

    private SkillProgramStepOutcome ResolveShiCd(ProgramSkillFrame input, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (op == SkillProgramEffectOp.ResolveMobileShiWanglie && f.TriggerId == "wanglie-response-complete")
        {
            if (f.SkillId != ShiCdWanglie || f.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseCompleted, CardUse: { } use } responseContext ||
                _resolutionStack.SingleOrDefault(x => x.Id == responseContext.ParentFrameId) is not ProgramCardTriggerWindowFrame
                    { Action.Type: CardActionType.Response, CompletedResponseReturn: { IsCommitted: false } } ||
                !ProgramEventHistory<MobileShiWanglieUsedEvent>().Any(e => e.ActionId == use.CardActionId && e.OwnerSeat == f.OwnerSeat))
                throw new InvalidOperationException("Wanglie lost its exact completed response use.");
            CompleteShiCdWanglie(use.CardActionId); return SkillProgramStepOutcome.Continue;
        }
        if (f.WindowContext is not { } context || f.InstructionIndex != 1 || f.MobileShiChenDao is not null)
            throw new InvalidOperationException("Shi Chen Dao requires its original unpaid native trigger.");
        if (op == SkillProgramEffectOp.ResolveMobileShiHongyi)
        {
            if (f.SkillId != ShiCdHongyi) throw new InvalidOperationException("Hongyi lost its own skill.");
            if (f.TriggerId == "hongyi-start")
            { ChangeShiCdMarks(f, 2); return SkillProgramStepOutcome.Continue; }
            if (f.TriggerId is "hongyi-damage-taken" or "hongyi-damage-dealt")
            { ChangeShiCdMarks(f, 1); return SkillProgramStepOutcome.Continue; }
            if (f.TriggerId == "hongyi-ending")
            {
                var grant = ShiCdPendingEnding(f.OwnerSeat) ?? throw new InvalidOperationException("Hongyi ending lost its issued preparation.");
                if (grant.Source.SkillInstanceId != f.SkillInstanceId || grant.GameplayHash != f.GameplayHash || context.Window != SkillProgramTriggerWindow.TurnEnding)
                    throw new InvalidOperationException("Hongyi ending changed its original skill instance or content.");
                AdvanceEventRulesAndQueueFact(new MobileShiHongyiEndingEvent(f.Id, grant.FrameId, f.OwnerSeat, _turnNumber, grant.FrozenCount, !grant.DrawFirst));
                if (grant.DrawFirst) ChangeShiCdMarks(f, -1);
                BindShiCdDraw(f, grant.DrawFirst ? 0 : grant.FrozenCount);
                return SkillProgramStepOutcome.Continue;
            }
            if (f.TriggerId != "hongyi-prepare" || context.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || f.OwnerSeat != _currentSeat)
                throw new InvalidOperationException("Hongyi needs its actual preparation boundary.");
        }
        else if (op != SkillProgramEffectOp.ResolveMobileShiWanglie || f.SkillId != ShiCdWanglie ||
            context.Window != SkillProgramTriggerWindow.PlayPhaseStarting || f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play)
            throw new InvalidOperationException("Wanglie needs its actual Play-start boundary.");
        ReplaceRuntimeTop(f = f with { MobileShiChenDao = new(ShiCdSource(f), f.GameplayHash, context.ParentFrameId,
            _turnNumber, _cardUseDebitPhaseInstanceId, ShiCdMarks(f.OwnerSeat)) });
        PublishShiCdChoice(f);
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private void BindShiCdDraw(ProgramSkillFrame supplied, int count)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (count is < 0 or > 4 || f.ChoiceBindings.Any(b => b.Name == "hongyi-draw")) throw new InvalidOperationException("Hongyi cannot bind a second draw.");
        ReplaceRuntimeTop(f with { ChoiceBindings = f.ChoiceBindings.Append(new ProgramChoiceResultBinding("hongyi-draw", "draw-" + count, f.OwnerSeat)).ToArray() });
        AdvanceEventRulesAndQueueFact(new MobileShiHongyiDrawBoundEvent(f.Id, f.OwnerSeat, count));
    }
    private IReadOnlyList<PromptChoice> ShiCdChoices(ProgramSkillFrame f)
    {
        PromptChoice Choice(string branch, string label, int? card = null) => new(new($"shi-chen-dao.{f.Id}.{branch}.{card ?? 0}"), label,
            card is { } id ? [id] : [], [], new Dictionary<string, string> { ["program-action"] = "shi-chen-dao", ["branch"] = branch,
                ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) });
        if (f.SkillId == ShiCdHongyi) return [Choice("draw", $"现在摸{f.MobileShiChenDao!.FrozenCount}张牌，结束阶段清空毅"), Choice("clear", $"现在清空毅，结束阶段摸{f.MobileShiChenDao.FrozenCount}张牌")];
        return GetHand(_players[f.OwnerSeat]).Select(c => Choice("card", "选择" + c.DisplayName, c.Id)).Append(Choice("cancel", "取消往烈")).ToArray();
    }
    private void PublishShiCdChoice(ProgramSkillFrame f)
    {
        var skill = _contentRegistry.GetSkill(f.SkillId); var choices = ShiCdChoices(f);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, f.SkillId == ShiCdHongyi
            ? $"弘毅：本次准备阶段的X已固定为{f.MobileShiChenDao!.FrozenCount}。选择先执行的一项。" : "往烈：选择一张手牌，赋予本次实际出牌阶段的效果。",
            choices.SelectMany(c => c.Cards).ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), Choices = choices, IsPrivate = f.SkillId == ShiCdWanglie,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("shi-chen-dao")]
    private void AnswerShiCdChoice(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiChenDao: { Finished: false } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !ShiCdChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Shi Chen Dao rejects a stale preparation or physical hand-card choice.");
        RequireShiCdChoice(f);
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, MobileShiChenDao = r with { Finished = true } });
        var branch = choice.Parameters["branch"];
        if (f.SkillId == ShiCdHongyi)
        {
            AdvanceEventRulesAndQueueFact(new MobileShiHongyiPreparedEvent(f.Id, r.Source, r.GameplayHash, r.ActualTurn, r.FrozenCount, branch == "draw"));
            if (branch == "clear") ChangeShiCdMarks(f, -1);
            BindShiCdDraw(f, branch == "draw" ? r.FrozenCount : 0);
        }
        else if (branch == "card")
            AdvanceEventRulesAndQueueFact(new MobileShiWanglieSelectedEvent(f.Id, r.Source, r.GameplayHash, r.ActualTurn, r.PhaseId, choice.Cards.Single()));
        AdvanceRuntimeProgram(f.Id); AdvanceRulesAndPublishState();
    }
    private void RequireShiCdChoice(ProgramSkillFrame f)
    {
        if (f.MobileShiChenDao is not { } r || r.Source != ShiCdSource(f) || r.GameplayHash != f.GameplayHash ||
            f.WindowContext?.ParentFrameId != r.WindowId || r.ActualTurn != _turnNumber || r.FrozenCount is < 0 or > 4 ||
            f.OwnerSeat != _currentSeat || f.SkillId == ShiCdWanglie && r.PhaseId != _cardUseDebitPhaseInstanceId)
            throw new InvalidOperationException("Shi Chen Dao lost its exact source, frozen X, actual turn or Play phase.");
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 47800)]
    private void AssertShiCdChoice(ProgramSkillFrame f)
    {
        if (f.MobileShiChenDao is not { } r) return;
        RequireShiCdChoice(f);
        if (!r.Finished && _resolutionStack.LastOrDefault()?.Id == f.Id && (_pendingDecision is not { } p || p.PlayerSeat != f.OwnerSeat ||
                !AssistedChoicesEqual(p.Choices, ShiCdChoices(f)))) throw new InvalidOperationException("Shi Chen Dao lost its original choices.");
    }
    private bool IsMobileShiChenDaoChoiceResult(ProgramSkillFrame f, ProgramChoiceResultBinding b) => f.SkillId == ShiCdHongyi &&
        b.Name == "hongyi-draw" && b.ChooserSeat == f.OwnerSeat && ProgramEventHistory<MobileShiHongyiDrawBoundEvent>()
            .Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && b.OptionId == "draw-" + e.Count) == 1;

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 47800)]
    private bool CanOfferShiCd(ProgramTriggerLegalityContext g) => g.SkillId == ShiCdHongyi && g.Trigger.Id == "hongyi-ending"
        ? ShiCdPendingEnding(g.OwnerSeat) is not null : g.SkillId != ShiCdWanglie || g.Trigger.Id != "wanglie-response-complete" ||
            g.Context.CardUse is { } c && ProgramEventHistory<CardActionAcceptedEvent>().Any(e => e.Action.ActionId == c.CardActionId && e.Action.Type == CardActionType.Response) &&
            ProgramEventHistory<MobileShiWanglieUsedEvent>().Any(e => e.OwnerSeat == g.OwnerSeat && e.ActionId == c.CardActionId);
    [ProgramPipelineHook(ProgramPipelinePhase.IncludeTriggerCandidate, 47800)]
    private IReadOnlyList<ProgramTriggerCandidate> IncludeShiCdEnding(CharacterState owner, SkillProgramTriggerWindow window, IReadOnlyList<ProgramTriggerCandidate> ordinary)
    {
        if (window != SkillProgramTriggerWindow.TurnEnding) return ordinary;
        var grant = ShiCdPendingEnding(owner.Seat); var rest = ordinary.Where(c => c.SkillId != ShiCdHongyi || c.BindingId != "hongyi-ending");
        return (grant is null ? rest : rest.Append(new(owner.Seat, ShiCdHongyi, "hongyi-ending", grant.Source.SkillInstanceId, grant.GameplayHash, 0))).ToArray();
    }
    [ProgramPipelineHook(ProgramPipelinePhase.AppendTurnEndingItems, 47800)]
    private void AppendShiCdEnding(List<TurnEndingBoundaryItem> items)
    {
        // The ordinary ending collector first filters out owners with no live
        // ending binding. A paid preparation still owes its other effect after
        // Hongyi itself is lost, so append that exact issued instance here too.
        if (!_players[_currentSeat].IsAlive || ShiCdPendingEnding(_currentSeat) is not { } grant) return;
        items.RemoveAll(item => item.Candidate is { SkillId: ShiCdHongyi, BindingId: "hongyi-ending" } c && c.OwnerSeat == _currentSeat);
        var candidate = new ProgramTriggerCandidate(_currentSeat, ShiCdHongyi, "hongyi-ending", grant.Source.SkillInstanceId, grant.GameplayHash, 0);
        items.Add(new(TurnEndingBoundaryItemKind.Program, 0,
            $"program:{ShiCdHongyi}:hongyi-ending:{grant.Source.SkillInstanceId}", candidate));
    }
    [ProgramPipelineHook(ProgramPipelinePhase.IssuedTriggerCandidate, 47800)]
    private bool IsIssuedShiCdEnding(ProgramTriggerCandidate c, ProgramSkillWindowContext context) => c.SkillId == ShiCdHongyi && c.BindingId == "hongyi-ending" &&
        context.Window == SkillProgramTriggerWindow.TurnEnding && c.OwnerSeat == _currentSeat && ShiCdPendingEnding(c.OwnerSeat) is { } grant &&
        grant.Source.SkillInstanceId == c.SkillInstanceId && grant.GameplayHash == c.GameplayHash &&
        context.SourceSeat == c.OwnerSeat && context.TargetSeat == c.OwnerSeat &&
        _resolutionStack.SingleOrDefault(x => x.Id == context.ParentFrameId) is TurnEndingBoundaryFrame boundary &&
        boundary.OwnerSeat == c.OwnerSeat && boundary.TurnNumber == grant.ActualTurn && boundary.ItemIndex < boundary.Items.Count &&
        boundary.Items[boundary.ItemIndex].Candidate == c;
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 47800)]
    private bool KeepPaidShiCdDraw(ProgramSkillFrame f)
    {
        if (f.SkillId != ShiCdHongyi) return false;
        if (IsMobileShiChenDaoChoiceResult(f, f.ChoiceBindings.FirstOrDefault(b => b.Name == "hongyi-draw") ?? new("", "", -1)))
            return true;
        // The preparation already issued this ending effect. Its original
        // instance may be absent before the first instruction creates DrawBound.
        if (f.TriggerId != "hongyi-ending" || f.InstructionIndex != 0 || f.MobileShiChenDao is not null ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } context ||
            _resolutionStack.SingleOrDefault(x => x.Id == context.ParentFrameId) is not TurnEndingBoundaryFrame boundary ||
            boundary.ItemIndex < 0 || boundary.ItemIndex >= boundary.Items.Count ||
            boundary.Items[boundary.ItemIndex].Candidate is not { } candidate ||
            candidate.OwnerSeat != f.OwnerSeat || candidate.SkillId != f.SkillId || candidate.BindingId != f.TriggerId ||
            candidate.SkillInstanceId != f.SkillInstanceId || candidate.GameplayHash != f.GameplayHash)
            return false;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        return index > 0 && _resolutionStack[index - 1].Id == boundary.Id && IsIssuedShiCdEnding(candidate, context);
    }

    [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileShiWanglie, SkillProgramEffectOp.ResolveMobileShiHongyi)]
    private PromptChoice ChooseShiCdAi(PendingDecision p, ProgramSkillFrame f) => f.SkillId == ShiCdHongyi
        ? p.Choices.First(c => c.Parameters["branch"] == "draw")
        : p.Choices.FirstOrDefault(c => c.Cards.Any(id => IsDamageCardKind(GetAdvancedCard(id).Kind))) ?? p.Choices.Last();
}
