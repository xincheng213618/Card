using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string TaoQianAction = "tao-qian";
    private const string ZongluanDiscardReason = "skill-program.tao-qian.zongluan.discard";
    private const string WenrenDrawReason = "skill-program.tao-qian.wenren.draw";
    private bool HasTaoQianCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveZongluanPreparationSlashAndDiscard) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveZhaohuoFirstVictimGift) || _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveWenrenSequentialDraw);
    private static CardConversionSource TaoQianSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_tao_qian_ol_TaoQian.TaoQianSource(f);
    [ProgramCapability] private sealed class TaoQianCapability(GameEngine engine) : ITaoQianHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginTaoQian(f, op); }
    private SkillProgramStepOutcome BeginTaoQian(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.TaoQian is not null) { ContinueTaoQian(f.Id); return SkillProgramStepOutcome.AwaitChoice; }
        var r = new TaoQianReceipt(TaoQianSource(f), f.GameplayHash, op, f.WindowContext?.ParentFrameId ?? 0, _turnNumber, _turnProgression.OwnerSeat,
            op == SkillProgramEffectOp.ResolveZongluanPreparationSlashAndDiscard ? TaoQianStage.ActorChoice : TaoQianStage.RecipientChoice);
        if (op == SkillProgramEffectOp.ResolveWenrenSequentialDraw)
        {
            if (f.TriggerId is not null || f.ActivationId != "distribute-draw" || CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } p || p.ActorSeat != f.OwnerSeat || ProgramEventHistory<WenrenCommittedEvent>().Any(e => e.Source.OwnerSeat == f.OwnerSeat && e.Phase == p)) throw new InvalidOperationException("Wenren requires an unspent real own Play-phase draft.");
            r = r with { Phase = p };
        }
        else if (f.TriggerId != "preparation-slash" || f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, OwnerSeat: var owner } || owner != f.OwnerSeat || _turnProgression.OwnerSeat != f.OwnerSeat) throw new InvalidOperationException("Zongluan requires its real own Preparation window.");
        ReplaceRuntimeTop(f = f with { TaoQian = r });
        AdvanceEventRulesAndQueueFact(new TaoQianStartedEvent(f.Id, r.Source, r.GameplayHash, op, r.WindowId, r.ActualTurn, r.TurnOwner));
        PublishTaoQian(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private int TaoQianChooser(ProgramSkillFrame f) => f.TaoQian!.Stage == TaoQianStage.SlashTargets ? f.TaoQian.ActorSeat : f.OwnerSeat;
    private IReadOnlyList<PromptChoice> TaoQianChoices(ProgramSkillFrame f)
    {
        var r = f.TaoQian!; var choices = new List<PromptChoice>();
        PromptChoice Choice(string branch, string label, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) => new(new($"tao-qian.{f.Id}.{r.Stage}.{(branch.EndsWith("confirm", StringComparison.Ordinal) ? "0" : "1")}.{branch}.{string.Join('-', cards ?? [])}.{string.Join('-', targets ?? [])}"), label, cards ?? [], targets ?? [], new Dictionary<string,string> { ["program-action"] = TaoQianAction, [TaoQianAction] = branch, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) });
        if (r.Stage == TaoQianStage.ActorChoice)
            foreach (var p in _players.Where(p => p.IsAlive)) choices.Add(Choice("actor", "令" + p.Name + "使用纵乱杀", targets: [p.Seat]));
        if (r.Stage is TaoQianStage.SlashTargets or TaoQianStage.RecipientChoice)
        {
            var seats = r.Stage == TaoQianStage.SlashTargets ? ZongluanTargets(r.ActorSeat) : _players.Where(p => p.IsAlive).Select(p => p.Seat).ToArray();
            foreach (var seat in seats)
            {
                var after = r.Targets.Contains(seat) ? r.Targets.Where(s => s != seat).ToArray() : r.Targets.Append(seat).ToArray();
                choices.Add(Choice(r.Stage == TaoQianStage.SlashTargets ? "slash-toggle" : "recipient-toggle", (r.Targets.Contains(seat) ? "取消选择" : "选择") + _players[seat].Name, targets: after));
            }
            if (r.Targets.Count > 0 && r.Targets.All(seats.Contains)) choices.Add(Choice(r.Stage == TaoQianStage.SlashTargets ? "slash-confirm" : "recipient-confirm", "确认所选角色", targets: r.Targets));
        }
        if (r.Stage == TaoQianStage.DiscardChoice)
        {
            if (r.Selected.Count < r.DiscardRequired) foreach (var c in ZongluanDiscardCards(f)) choices.Add(Choice("discard-card", c.Label, c.Cards));
            if (r.Selected.Count == r.DiscardRequired) choices.Add(Choice("discard-confirm", "确认弃置", cards: r.Selected));
        }
        if (!r.Paid) choices.Add(Choice("cancel", "取消发动"));
        return Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray());
    }
    private void PublishTaoQian(ProgramSkillFrame f)
    {
        var r = f.TaoQian!;
        if (!r.Paid && (!_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))) { FinishTaoQian(f); return; }
        if (r.Stage == TaoQianStage.SlashTargets && (!_players[r.ActorSeat].IsAlive || ZongluanTargets(r.ActorSeat).Length == 0)) { FinishTaoQian(f); return; }
        var choices = TaoQianChoices(f); if (choices.Count == 0) { FinishTaoQian(f); return; }
        var chooser = TaoQianChooser(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, r.Stage switch { TaoQianStage.ActorChoice => "纵乱：选择使用杀的角色。", TaoQianStage.SlashTargets => "纵乱：选择至少一名当前合法目标，然后确认。", TaoQianStage.DiscardChoice => $"纵乱：弃置{r.DiscardRequired}张牌。", _ => "温仁：选择至少一名角色，然后确认。" }, choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { IsPrivate = true, PromptId = CreatePromptId(), Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("tao-qian")]
    private void AnswerTaoQian(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { TaoQian: { } r } f || _pendingDecision?.PlayerSeat != TaoQianChooser(f) || !TaoQianChoices(f).Any(c => AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Tao Qian requires the actual chooser's exact published private input.");
        AssertTaoQian(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        switch (choice.Parameters[TaoQianAction])
        {
            case "cancel": if (r.Paid) throw new InvalidOperationException("An issued Tao Qian action cannot be cancelled."); FinishTaoQian(f); return;
            case "actor":
                var actor = choice.Targets.Single(); ReplaceRuntimeTop(f = f with { TaoQian = r with { Paid = true, ActorSeat = actor, Stage = TaoQianStage.SlashTargets } });
                AdvanceEventRulesAndQueueFact(new ZongluanCommittedEvent(f.Id, r.Source, r.GameplayHash, actor, r.ActualTurn)); PublishTaoQian(f); return;
            case "slash-toggle": case "recipient-toggle": ReplaceRuntimeTop(f = f with { TaoQian = r with { Targets = choice.Targets } }); PublishTaoQian(f); return;
            case "slash-confirm": IssueZongluanSlash(f); return;
            case "recipient-confirm":
                if (r.Paid || r.Targets.Count == 0 || r.Phase is not { } phase || ProgramEventHistory<WenrenCommittedEvent>().Any(e => e.Source.OwnerSeat == f.OwnerSeat && e.Phase == phase)) throw new InvalidOperationException("Wenren repeated its confirmed actual Play queue.");
                ReplaceRuntimeTop(f = f with { TaoQian = r with { Paid = true, Stage = TaoQianStage.Drawing, Cursor = 0 } });
                AdvanceEventRulesAndQueueFact(new WenrenCommittedEvent(f.Id, r.Source, r.GameplayHash, phase, r.Targets.Count));
                for (var i = 0; i < r.Targets.Count; i++) AdvanceEventRulesAndQueueFact(new WenrenRecipientFrozenEvent(f.Id, i, r.Targets[i])); ContinueTaoQian(f.Id); return;
            case "discard-card":
                if (r.Selected.Count >= r.DiscardRequired) throw new InvalidOperationException("Zongluan cannot select more than its actual remaining discard obligation.");
                ReplaceRuntimeTop(f = f with { TaoQian = r with { Selected = r.Selected.Append(choice.Cards.Single()).ToArray() } }); PublishTaoQian(f); return;
            case "discard-confirm": PayZongluanDiscard(f); return;
            default: throw new InvalidOperationException("Unknown Tao Qian choice.");
        }
    }
    private bool DrainTaoQian(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginAdvancedSkillsChanged(id) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id);
    private void ContinueTaoQian(long id)
    {
        while (_resolutionStack.LastOrDefault() is ProgramSkillFrame { TaoQian: { } r } f && f.Id == id && _pendingDecision is null)
        {
            AssertTaoQian(f); if (f.NativeDraw is not null || r.Stage == TaoQianStage.NativeSlash) return;
            if (r.Stage is TaoQianStage.NativeChildren or TaoQianStage.DiscardChildren or TaoQianStage.DrawChildren)
            { if (DrainTaoQian(id)) return; f = GetActiveProgramFrame(id); r = f.TaoQian!; ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null }); }
            if (_winner != Winner.None) { FinishTaoQian(f); return; }
            if (r.Stage == TaoQianStage.DiscardChildren) { FinishTaoQian(f); return; }
            if (r.Stage == TaoQianStage.NativeChildren)
            {
                if (!_players[f.OwnerSeat].IsAlive) { FinishTaoQian(f); return; }
                var required = Math.Min(r.DamageRecipientCount, BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, f.OwnerSeat, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard).SelectMany(c => c.Cards).Distinct().Count());
                AdvanceEventRulesAndQueueFact(new ZongluanDiscardIssuedEvent(f.Id, r.DamageRecipientCount, required, _movementSequence));
                if (required == 0) { FinishTaoQian(f); return; }
                ReplaceRuntimeTop(f = f with { TaoQian = r with { Stage = TaoQianStage.DiscardChoice, DiscardRequired = required, Selected = [] } }); PublishTaoQian(f); return;
            }
            if (r.Stage == TaoQianStage.DrawChildren) { ReplaceRuntimeTop(f with { TaoQian = r with { Stage = TaoQianStage.Drawing, Cursor = r.Cursor + 1, DrawOutcome = null } }); continue; }
            if (r.Stage == TaoQianStage.Drawing)
            {
                if (r.Cursor >= r.Targets.Count) { FinishTaoQian(f); return; }
                var seat = r.Targets[r.Cursor]; if (!_players[seat].IsAlive) { ReplaceRuntimeTop(f with { TaoQian = r with { Cursor = r.Cursor + 1 } }); continue; }
                var ownHand = GetHand(_players[f.OwnerSeat]).Count; var hand = GetHand(_players[seat]).Count; var amount = (hand == 0 ? 1 : 0) + (hand <= ownHand ? 1 : 0);
                AdvanceEventRulesAndQueueFact(new WenrenRecipientEvaluatedEvent(f.Id, r.Cursor, seat, ownHand, hand, amount));
                if (amount == 0) { ReplaceRuntimeTop(f with { TaoQian = r with { Cursor = r.Cursor + 1 } }); continue; }
                ReplaceRuntimeTop(f = f with { TaoQian = r with { DrawAmount = amount, Before = _movementSequence, After = _movementSequence } }); BeginOperationProgramDraw(f, seat, amount, new(WenrenDrawReason)); return;
            }
            PublishTaoQian(f); return;
        }
    }
    private void FinishTaoQian(ProgramSkillFrame f)
    { var r = f.TaoQian!; ClearPendingDecision(); AdvanceEventRulesAndQueueFact(new TaoQianCompletedEvent(f.Id, r.Op, r.Paid)); ReplaceRuntimeTop(f = f with { TaoQian = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 28120)] private bool ResumeTaoQian(long id) { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { TaoQian: not null } f || f.Id != id || f.InstructionIndex == 0) return false; ContinueTaoQian(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 28120)] private bool ReturnTaoQianMovement(ProgramSkillFrame f) { if (f.TaoQian is null) return false; ContinueTaoQian(f.Id); return true; }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveZongluanPreparationSlashAndDiscard, SkillProgramEffectOp.ResolveWenrenSequentialDraw)]
    private PromptChoice ChooseTaoQianAi(PendingDecision p, ProgramSkillFrame f)
    {
        var r = f.TaoQian!; var view = CreateSnapshot(p.PlayerSeat);
        if (r.Stage == TaoQianStage.ActorChoice)
        {
            var support = new SkillProgramAiHint(0, 0, 0, 1, 0, 0, false, false); var harm = new SkillProgramAiHint(0, 0, 0, 0, 0, 1, false, false);
            var actors = p.Choices.Where(c => c.Targets is [var actor] && ZongluanTargets(actor).Length > 0 && (actor == f.OwnerSeat || _aiBrains[p.PlayerSeat].ScoreProgramTarget(view, actor, support) > 0)).ToArray();
            if (actors.Length == 0) return p.Choices.Single(c => c.Parameters[TaoQianAction] == "cancel");
            return actors.OrderByDescending(c => _aiBrains[p.PlayerSeat].ScoreProgramTarget(view, c.Targets[0], support) + ZongluanTargets(c.Targets[0]).Select(t => _aiBrains[p.PlayerSeat].ScoreProgramTarget(view, t, harm)).Where(v => v > 0).Sum()).ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
        }
        if (r.Stage is TaoQianStage.SlashTargets or TaoQianStage.RecipientChoice)
        {
            var hint = r.Stage == TaoQianStage.SlashTargets ? new SkillProgramAiHint(0, 0, 0, 0, 0, 1, false, false) : new SkillProgramAiHint(0, 0, 0, 1, 0, 0, false, false);
            return p.Choices.Where(c => r.Stage != TaoQianStage.SlashTargets || c.Targets.Count > 0).OrderByDescending(c => c.Targets.Sum(s => _aiBrains[p.PlayerSeat].ScoreProgramTarget(view, s, hint))).ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
        }
        return p.Choices.First();
    }
}
