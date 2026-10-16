using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string CaoXiDrawReason = "skill-program.cao-xi.jianxuan.draw";
    private static CardConversionSource CaoXiSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static readonly SkillRuleQuery[] CaoXiQueries = [SkillRuleQuery.AttackRange, SkillRuleQuery.DrawCount, SkillRuleQuery.SlashLimit];
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 26810)]
    private bool CanOfferCaoXi(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context)
    {
        if (t.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveGangshuAttributeIncrement))
            return context.Window == SkillProgramTriggerWindow.CardUseCompleted && context.CardUse is { } use && use.ActorSeat == c.OwnerSeat &&
                !MatchesProgramCardCategory(use.EffectiveKind, [SkillProgramCardCategory.Basic]) && CaoXiQueries.Any(q => CurrentCaoXiNumbers(c.OwnerSeat).Value(q) < 5);
        if (t.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveJianxuanRepeatedDraw))
            return context.Window == SkillProgramTriggerWindow.AfterDamageApplied && context.TargetSeat == c.OwnerSeat && context.Amount > 0 &&
                context.DamageFrameId is { } id && _resolutionStack.OfType<DamageTriggerWindowFrame>().Any(w => w.Id == context.ParentFrameId &&
                    w.ParentFrameId == id && w.TargetSeat == c.OwnerSeat && w.TriggerWindow == SkillProgramTriggerWindow.AfterDamageApplied);
        return true;
    }
    private SkillProgramStepOutcome BeginCaoXi(ProgramSkillFrame input, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(input.Id); var gangshu = op == SkillProgramEffectOp.ResolveGangshuAttributeIncrement;
        if (f.CaoXiGangshu is not null || f.CaoXiJianxuan is not null || f.InstructionIndex != 1 || f.WindowContext is not { } c ||
            f.SkillId != (gangshu ? "ol:gangshu" : "ol:jianxuan") || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            !CanOfferCaoXi(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, 0), GetProgramTrigger(f), c))
            throw new InvalidOperationException("Cao Xi requires its exact completed nonbasic use or one actual positive damage instance.");
        if (gangshu)
        {
            var use = c.CardUse!; var r = new CaoXiGangshuReceipt(CaoXiSource(f), f.GameplayHash, f.InstructionIndex, c.ParentFrameId,
                use.ParentCardUseFrameId, use.CardActionId, CurrentCaoXiNumbers(f.OwnerSeat));
            ReplaceRuntimeTop(f = f with { CaoXiGangshu = r });
            AdvanceEventRulesAndQueueFact(new CaoXiGangshuStartedEvent(f.Id, r.Source, r.GameplayHash, r.ParentWindowId, r.CardUseFrameId, r.ActionId, r.Numbers));
        }
        else
        {
            var r = new CaoXiJianxuanReceipt(CaoXiSource(f), f.GameplayHash, f.InstructionIndex, c.ParentFrameId, c.DamageFrameId!.Value, _turnNumber);
            ReplaceRuntimeTop(f = f with { CaoXiJianxuan = r });
            AdvanceEventRulesAndQueueFact(new CaoXiJianxuanStartedEvent(f.Id, r.Source, r.GameplayHash, r.ParentWindowId, r.DamageFrameId, r.ActualTurn));
        }
        PublishCaoXi(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> CaoXiChoices(ProgramSkillFrame f)
    {
        Dictionary<string, string> Params() => new() { ["program-action"] = "cao-xi", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) };
        if (f.CaoXiGangshu is { } r)
            return Array.AsReadOnly(CaoXiQueries.Where(q => r.Numbers.Value(q) < 5).Select(q => new PromptChoice(new($"cao-xi.{f.Id}.{q}"),
                (q == SkillRuleQuery.AttackRange ? "攻击范围" : q == SkillRuleQuery.DrawCount ? "下个摸牌阶段摸牌数" : "出牌阶段使用杀的次数上限") + $"：{r.Numbers.Value(q)}→{r.Numbers.Value(q) + 1}", [], [],
                new Dictionary<string, string>(Params()) { ["query"] = q.ToString() })).ToArray());
        return Array.AsReadOnly(_players.Where(p => p.IsAlive).OrderBy(p => p.Seat).Select(p => new PromptChoice(new($"cao-xi.{f.Id}.recipient.{p.Seat}"),
            $"令{p.Name}摸牌", [], [p.Seat], new Dictionary<string, string>(Params()) { ["recipient"] = p.Seat.ToString(CultureInfo.InvariantCulture) })).ToArray());
    }
    private void PublishCaoXi(ProgramSkillFrame f)
    {
        var choices = CaoXiChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        if (choices.Count == 0) throw new InvalidOperationException("Cao Xi has no legal original-owner decision.");
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, f.CaoXiGangshu is null ? "谏旋：选择本次始终摸牌的角色。" : "刚述：选择一项当前小于5的数值+1。",
            [], choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("cao-xi")]
    private void AnswerCaoXi(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Cao Xi lost the decision owner.");
        RequireCaoXi(f);
        if (_pendingDecision is not { IsPrivate: true } p || p.PlayerSeat != f.OwnerSeat || !CaoXiChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Cao Xi requires its exact source-owned decision.");
        ClearPendingDecision();
        if (f.CaoXiGangshu is { } r)
        {
            var query = Enum.Parse<SkillRuleQuery>(choice.Parameters["query"]); var value = CurrentCaoXiNumbers(f.OwnerSeat).Value(query);
            if (value != r.Numbers.Value(query) || value >= 5) throw new InvalidOperationException("Gangshu cannot grow a capped or changed numeric value.");
            var token = CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Draw } draw ? draw.Token : (long?)null;
            var latestPhase = CompleteProgramEventHistory().OfType<ActualDiscardRecoveryPhaseStartedEvent>().Select(e => e.Phase.Token).DefaultIfEmpty().Max();
            AdvanceEventRulesAndQueueFact(new CaoXiGangshuIncreasedEvent(f.Id, r.Source, r.GameplayHash, query, value, value + 1, token, latestPhase));
            ReplaceRuntimeTop(f = f with { CaoXiGangshu = null }); FinishProgramSkill(f, true); return;
        }
        var recipient = int.Parse(choice.Parameters["recipient"], CultureInfo.InvariantCulture);
        ReplaceRuntimeTop(f = f with { CaoXiJianxuan = f.CaoXiJianxuan! with { RecipientSeat = recipient, Stage = CaoXiJianxuanStage.DrawChildren } });
        AdvanceEventRulesAndQueueFact(new CaoXiJianxuanRecipientEvent(f.Id, recipient)); IssueCaoXiDraw(f);
    }
    private void IssueCaoXiDraw(ProgramSkillFrame f)
    {
        var r = f.CaoXiJianxuan!; var before = _movementSequence; var cursor = checked(r.Cursor + 1);
        ReplaceRuntimeTop(f = f with { CaoXiJianxuan = r with { Cursor = cursor, Before = before, After = before, ActualDrawCount = 0 } });
        var drawn = DrawCards(_players[r.RecipientSeat!.Value], 1, true, new(CaoXiDrawReason));
        f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f = f with { CaoXiJianxuan = f.CaoXiJianxuan! with { After = _movementSequence, ActualDrawCount = drawn.Count } });
        AdvanceEventRulesAndQueueFact(new CaoXiJianxuanDrawPaidEvent(f.Id, cursor, r.RecipientSeat.Value, drawn.Count, before, _movementSequence));
        AdvanceRuntimeProgram(f.Id);
    }
    private bool DrainCaoXiChildren(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 26810)]
    private bool ResumeCaoXi(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.CaoXiGangshu is null && f.CaoXiJianxuan is null) return false;
        RequireCaoXi(f);
        if (f.CaoXiGangshu is not null || f.CaoXiJianxuan?.Stage == CaoXiJianxuanStage.ChoosingRecipient)
        { if (_pendingDecision is null) PublishCaoXi(f); return true; }
        if (DrainCaoXiChildren(id)) return true;
        f = GetActiveProgramFrame(id); var r = f.CaoXiJianxuan!; var recipient = r.RecipientSeat!.Value;
        var numbers = CurrentCaoXiNumbers(f.OwnerSeat); var count = GetHand(_players[recipient]).Count;
        var repeat = _winner == Winner.None && _players[recipient].IsAlive && r.ActualDrawCount == 1 && numbers.Contains(count);
        AdvanceEventRulesAndQueueFact(new CaoXiJianxuanComparedEvent(f.Id, r.Cursor, recipient, count, numbers, repeat));
        if (repeat) { IssueCaoXiDraw(f); return true; }
        AdvanceEventRulesAndQueueFact(new CaoXiJianxuanCompletedEvent(f.Id, recipient, r.Cursor));
        ReplaceRuntimeTop(f = f with { CaoXiJianxuan = null }); FinishProgramSkill(f, true); return true;
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveGangshuAttributeIncrement, SkillProgramEffectOp.ResolveJianxuanRepeatedDraw)]
    private PromptChoice ChooseCaoXiAi(PendingDecision p, ProgramSkillFrame f)
    {
        if (f.CaoXiGangshu is not null)
            return p.Choices.OrderBy(c => c.Parameters.GetValueOrDefault("query") == nameof(SkillRuleQuery.DrawCount) ? 0 :
                c.Parameters.GetValueOrDefault("query") == nameof(SkillRuleQuery.SlashLimit) ? 1 : 2).First();
        var numbers = CurrentCaoXiNumbers(f.OwnerSeat);
        return p.Choices.OrderByDescending(c => c.Targets is [var seat] && seat == f.OwnerSeat ? 1 : 0)
            .ThenByDescending(c => c.Targets is [var seat] && numbers.Contains(GetHand(_players[seat]).Count + 1) ? 1 : 0).First();
    }
    [ProgramCapability]
    private sealed class CaoXiCapability(GameEngine e) : ICaoXiProgramHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => e.BeginCaoXi(f, op); }
}
