using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability] private sealed class MoDiaoChanCapability(GameEngine engine) : IMoDiaoChanHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op) => engine.BeginMoDc(frame, op); }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 31400)]
    private bool CanOfferMoDc(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context)
    {
        if (!t.Effects.Any(e => MoDiaoChanComposition.Handles(e.Op))) return true;
        if (t.Effects.Count != 1) return false; var op = t.Effects[0].Op;
        if (op == SkillProgramEffectOp.ResolveMoDiaoChanHuanhuoUse) return IsIssuedMoDiaoChanCandidate(c, context);
        if (op == SkillProgramEffectOp.ResolveMoDiaoChanHuanhuoRound) return context.Window == SkillProgramTriggerWindow.RoundStarting &&
            !ProgramEventHistory<MoDiaoChanStartedEvent>().Any(e => e.Source.OwnerSeat == c.OwnerSeat && e.Source.SkillInstanceId == c.SkillInstanceId && e.Op == op && e.Round == _roundNumber);
        if (op == SkillProgramEffectOp.ResolveMoDiaoChanQingshiPrep) return context.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow &&
            context.SourceSeat == c.OwnerSeat && CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Preparation } phase && phase.ActorSeat == c.OwnerSeat &&
            (!IsOlMoRumoEntered(c.OwnerSeat) || MoDcRefreshReady(c.OwnerSeat));
        if (op == SkillProgramEffectOp.ResolveMoDiaoChanQingshiRedirect) return MoDcCurrentMarkedUse(c.OwnerSeat, context) is { } use && MoDcDiscardable(c.OwnerSeat).Length > 0 &&
            MoDcRedirectTargets(use).Length > 0 && !ProgramEventHistory<MoDiaoChanStartedEvent>().Any(e => e.Source.OwnerSeat == c.OwnerSeat && e.Op == op && e.WindowId == context.ParentFrameId);
        if (op == SkillProgramEffectOp.ResolveMoDiaoChanQingshiDamage) return MoDcDamageEligible(c.OwnerSeat, context);
        return MoDcRecaptureCards(c.OwnerSeat, context).Length > 0;
    }
    private SkillProgramStepOutcome BeginMoDc(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id); var c = f.WindowContext ?? throw new InvalidOperationException("Magic Diao Chan requires her native trigger window.");
        var candidate = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, c.OccurrenceIndex);
        if (f.MoDiaoChan is not null || f.InstructionIndex != 1 || !CanOfferMoDc(candidate, GetProgramTrigger(f), c)) throw new InvalidOperationException("Magic Diao Chan lost her exact unissued trigger.");
        var stage = op switch { SkillProgramEffectOp.ResolveMoDiaoChanHuanhuoRound or SkillProgramEffectOp.ResolveMoDiaoChanQingshiDamage => MoDiaoChanStage.Drawing,
            SkillProgramEffectOp.ResolveMoDiaoChanQingshiPrep => MoDiaoChanStage.EntryChoice, _ => MoDiaoChanStage.CostChildren };
        var r = new MoDiaoChanReceipt(MoDcSource(f), f.GameplayHash, op, c.ParentFrameId, _turnNumber, _roundNumber, stage);
        if (op == SkillProgramEffectOp.ResolveMoDiaoChanHuanhuoUse)
        {
            var q = MoDcCompletedClaim(candidate, c)!;
            r = r with { UseId = q.ParentFrameId, ActionId = q.ActionId, ActorSeat = q.ActorSeat, ProviderSeat = q.ProviderSeat, PromiseId = q.PromiseId, Paid = true };
        }
        if (op == SkillProgramEffectOp.ResolveMoDiaoChanQingshiRedirect)
        {
            var use = MoDcCurrentMarkedUse(f.OwnerSeat, c)!;
            r = r with { Stage = MoDiaoChanStage.DiscardChoice, UseId = use.Id, ActionId = use.Action!.ActionId, ActorSeat = use.SourceSeat, ProviderSeat = use.Action.ProviderSeat, PreviousTarget = use.TargetSeats.Single() };
        }
        ReplaceRuntimeTop(f = f with { MoDiaoChan = r });
        AdvanceEventRulesAndQueueFact(new MoDiaoChanStartedEvent(f.Id, r.Source, r.GameplayHash, op, r.WindowId, r.ActualTurn, r.Round));
        if (op == SkillProgramEffectOp.ResolveMoDiaoChanHuanhuoUse) PayMoDcRandomDiscard(f);
        else if (op == SkillProgramEffectOp.ResolveMoDiaoChanQingshiDiscard) RecaptureMoDcCards(f);
        else if (op == SkillProgramEffectOp.ResolveMoDiaoChanQingshiPrep && IsOlMoRumoEntered(f.OwnerSeat)) { StartMoDcCohort(f); return SkillProgramStepOutcome.AwaitChild; }
        ContinueMoDc(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private PromptChoice MoDcChoice(ProgramSkillFrame f, string branch, string text, int? card = null, int? target = null) => new(new($"mo-diao-chan.{f.Id}.{branch}.{card}.{target}"), text,
        card is { } id ? [id] : [], target is { } seat ? [seat] : [], new Dictionary<string, string>
        { ["program-action"] = "mo-diao-chan", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch });
    private IReadOnlyList<PromptChoice> MoDcChoices(ProgramSkillFrame f)
    {
        var r = f.MoDiaoChan!; var result = new List<PromptChoice>();
        if (r.Stage == MoDiaoChanStage.DiscardChoice)
        {
            var max = r.Op == SkillProgramEffectOp.ResolveMoDiaoChanHuanhuoRound ? 2 : 1;
            if (r.Cards.Count < max) foreach (var card in MoDcDiscardable(f.OwnerSeat).Where(c => !r.Cards.Contains(c.Id))) result.Add(MoDcChoice(f, "card", $"选择弃置【{card.DisplayName}】", card.Id));
            result.Add(MoDcChoice(f, "confirm", r.Cards.Count == 0 ? "不弃置" : $"弃置所选{r.Cards.Count}张牌"));
        }
        if (r.Stage == MoDiaoChanStage.TargetChoice)
            foreach (var p in _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat && !r.Targets.Contains(p.Seat))) result.Add(MoDcChoice(f, "target", $"令{p.Name}下回合受到幻惑", target: p.Seat));
        if (r.Stage == MoDiaoChanStage.EntryChoice)
        { result.Add(MoDcChoice(f, "enter", "入魔，令所有角色获得单目标伤害牌")); result.Add(MoDcChoice(f, "decline", "不入魔")); }
        if (r.Stage == MoDiaoChanStage.RedirectChoice && LifecycleCardUse(r.UseId) is { } use)
            foreach (var target in MoDcRedirectTargets(use)) result.Add(MoDcChoice(f, "redirect", $"重新指定{_players[target].Name}为目标", target: target));
        return Array.AsReadOnly(result.ToArray());
    }
    private void PublishMoDc(ProgramSkillFrame f)
    {
        var choices = MoDcChoices(f); if (choices.Count == 0) { FinishMoDc(f); return; } var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, skill.Name + "：选择本次处理。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), Choices = choices, IsPrivate = true, TargetSeat = f.OwnerSeat, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("mo-diao-chan")]
    private void AnswerMoDc(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MoDiaoChan: { } r } f || !ValidMoDc(f) || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !MoDcChoices(f).Any(c => c.Id == choice.Id && AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Magic Diao Chan choice changed its original private frame.");
        ClearPendingDecision(); var branch = choice.Parameters["branch"];
        if (branch == "card") { ReplaceRuntimeTop(f = f with { MoDiaoChan = r with { Cards = r.Cards.Append(choice.Cards.Single()).ToArray() } }); PublishMoDc(f); return; }
        if (branch == "confirm")
        {
            if (r.Cards.Count == 0) { FinishMoDc(f); return; }
            if (!_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) { FinishMoDc(f); return; }
            var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { MoDiaoChan = r = r with { Paid = true, Before = before, After = before, Stage = MoDiaoChanStage.CostChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            foreach (var card in r.Cards.Select(GetAdvancedCard)) MoDcDiscardOne(f, f.OwnerSeat, card);
            SealMoDcMovement(f.Id, before); ContinueMoDc(f.Id); return;
        }
        if (branch == "target")
        {
            var target = choice.Targets.Single(); var targets = r.Targets.Append(target).ToArray();
            var promise = ProgramEventHistory<MoDiaoChanPromiseEvent>().Select(e => e.PromiseId).DefaultIfEmpty().Max() + 1;
            AdvanceEventRulesAndQueueFact(new MoDiaoChanPromiseEvent(promise, f.Id, r.Source, r.GameplayHash, target, CompleteProgramEventHistory().OfType<TurnStartedEvent>().Count()));
            ReplaceRuntimeTop(f = f with { MoDiaoChan = r with { Targets = targets } });
            if (targets.Length >= r.CostCount) FinishMoDc(f); else PublishMoDc(f); return;
        }
        if (branch == "decline") { FinishMoDc(f); return; }
        if (branch == "enter")
        {
            ReplaceRuntimeTop(f = f with { MoDiaoChan = r with { Paid = true } });
            AdvanceEventRulesAndQueueFact(new MoDiaoChanQingshiEnteredEvent(f.Id, r.Source, r.GameplayHash, _turnNumber, _roundNumber));
            EnterOlMoRumo(f); StartMoDcCohort(GetActiveProgramFrame(f.Id)); return;
        }
        if (branch == "redirect") { ReplaceMoDcUniqueTarget(f, choice.Targets.Single()); return; }
        throw new InvalidOperationException("Unknown Magic Diao Chan choice.");
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMoDiaoChanHuanhuoRound, SkillProgramEffectOp.ResolveMoDiaoChanQingshiDamage)]
    private void ReturnMoDcDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.MoDiaoChan is not { Stage: MoDiaoChanStage.Drawing } r || r.DrawOutcome is not null || outcome.RecipientSeat != f.OwnerSeat ||
            outcome.RequestedCount != (r.Op == SkillProgramEffectOp.ResolveMoDiaoChanHuanhuoRound ? 2 : 1)) throw new InvalidOperationException("Magic Diao Chan lost its original native Draw invoice.");
        ReplaceRuntimeTop(f = f with { MoDiaoChan = r with { Stage = MoDiaoChanStage.DrawChildren, DrawOutcome = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MoDiaoChanDrawReturnedEvent(f.Id, outcome.RequestedCount, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId));
        SealMoDcMovement(f.Id, outcome.SequenceBefore); ContinueMoDc(f.Id);
    }
    private void ContinueMoDc(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MoDiaoChan: { } r } f || f.Id != id) return;
        if (!ValidMoDc(f)) throw new InvalidOperationException("Magic Diao Chan lost its typed native receipt.");
        if (r.Stage is MoDiaoChanStage.EntryChoice or MoDiaoChanStage.DiscardChoice or MoDiaoChanStage.TargetChoice or MoDiaoChanStage.RedirectChoice) { PublishMoDc(f); return; }
        if (r.Stage == MoDiaoChanStage.Drawing)
        {
            if (!_players[f.OwnerSeat].IsAlive) { FinishMoDc(f); return; }
            ReplaceRuntimeTop(f = f with { MoDiaoChan = r with { Paid = true } });
            if (f.NativeDraw is null) BeginOperationProgramDraw(f, f.OwnerSeat, r.Op == SkillProgramEffectOp.ResolveMoDiaoChanHuanhuoRound ? 2 : 1,
                new(r.Op == SkillProgramEffectOp.ResolveMoDiaoChanHuanhuoRound ? "skill-program.ol:mo-huanhuo.draw" : "skill-program.ol:mo-qingshi.draw"));
            return;
        }
        if (r.Stage == MoDiaoChanStage.Obtaining)
        { if (r.Cursor >= r.Targets.Count || _winner != Winner.None) { FinishMoDc(f); return; } ObtainMoDcCohortCard(f); ContinueMoDc(id); return; }
        if (DrainMoDc(f)) return;
        f = GetActiveProgramFrame(id); r = f.MoDiaoChan!;
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (_winner != Winner.None) { FinishMoDc(f); return; }
        if (r.Stage == MoDiaoChanStage.DrawChildren && r.Op == SkillProgramEffectOp.ResolveMoDiaoChanHuanhuoRound && _players[f.OwnerSeat].IsAlive)
        { ReplaceRuntimeTop(f = f with { MoDiaoChan = r with { Stage = MoDiaoChanStage.DiscardChoice } }); PublishMoDc(f); return; }
        if (r.Stage == MoDiaoChanStage.CostChildren && r.Op == SkillProgramEffectOp.ResolveMoDiaoChanHuanhuoRound && _players[f.OwnerSeat].IsAlive)
        {
            var count = ProgramEventHistory<MoDiaoChanCostEvent>().Count(e => e.FrameId == f.Id);
            if (count > 0) { ReplaceRuntimeTop(f = f with { MoDiaoChan = r with { Stage = MoDiaoChanStage.TargetChoice, CostCount = Math.Min(count, _players.Count(p => p.IsAlive && p.Seat != f.OwnerSeat)) } }); PublishMoDc(f); return; }
        }
        if (r.Stage == MoDiaoChanStage.CostChildren && r.Op == SkillProgramEffectOp.ResolveMoDiaoChanQingshiRedirect && _players[f.OwnerSeat].IsAlive)
        { ReplaceRuntimeTop(f = f with { MoDiaoChan = r with { Stage = MoDiaoChanStage.RedirectChoice } }); PublishMoDc(f); return; }
        if (r.Stage == MoDiaoChanStage.GainChildren && r.Op == SkillProgramEffectOp.ResolveMoDiaoChanQingshiPrep)
        { ReplaceRuntimeTop(f = f with { MoDiaoChan = r with { Stage = MoDiaoChanStage.Obtaining, Cursor = r.Cursor + 1 } }); ContinueMoDc(id); return; }
        FinishMoDc(f);
    }
    private void FinishMoDc(ProgramSkillFrame f)
    {
        var r = f.MoDiaoChan!;
        if (r.Op == SkillProgramEffectOp.ResolveMoDiaoChanHuanhuoUse) AdvanceEventRulesAndQueueFact(new MoDiaoChanUseSettledEvent(f.Id, r.PromiseId, r.ActionId));
        AdvanceEventRulesAndQueueFact(new MoDiaoChanCompletedEvent(f.Id, r.Op, r.Paid));
        ReplaceRuntimeTop(f = f with { MoDiaoChan = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMoDiaoChanHuanhuoRound, SkillProgramEffectOp.ResolveMoDiaoChanQingshiPrep, SkillProgramEffectOp.ResolveMoDiaoChanQingshiRedirect)]
    private PromptChoice ChooseMoDcAi(PendingDecision p, ProgramSkillFrame f)
    {
        var r = f.MoDiaoChan!;
        if (r.Stage == MoDiaoChanStage.DiscardChoice)
        {
            var max = r.Op == SkillProgramEffectOp.ResolveMoDiaoChanHuanhuoRound ? Math.Min(2, _players.Count(x => x.IsAlive && x.Seat != f.OwnerSeat)) : 1;
            return r.Cards.Count < max ? p.Choices.Where(c => c.Cards.Count == 1).OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards[0]), _players[f.OwnerSeat])).FirstOrDefault() ?? p.Choices.Single(c => c.Parameters["branch"] == "confirm") : p.Choices.Single(c => c.Parameters["branch"] == "confirm");
        }
        if (r.Stage == MoDiaoChanStage.TargetChoice || r.Stage == MoDiaoChanStage.RedirectChoice)
            return p.Choices.OrderBy(c => AreProgramDistributionAllies(_players[f.OwnerSeat], _players[c.Targets.Single()]) ? 1 : 0).ThenBy(c => c.Targets.Single()).First();
        return p.Choices.First(c => c.Parameters["branch"] == "enter");
    }
}
