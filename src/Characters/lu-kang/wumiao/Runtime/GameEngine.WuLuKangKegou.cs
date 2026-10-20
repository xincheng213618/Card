using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string WuLuKangKegouGainReason = "skill-program.wu-lu-kang.kegou.gain";
    private WuLuKangKegouQualification CaptureWuLuKangQualification(int owner, bool freezeAtEnded = false)
    {
        var history = CompleteProgramEventHistory().ToArray();
        if (freezeAtEnded)
        {
            var currentStart = Array.FindLastIndex(history, e => e is TurnStartedEvent started && started.TurnNumber == _turnNumber && started.ActorSeat == _turnProgression.OwnerSeat);
            var ended = Array.FindLastIndex(history, e => e is TurnEndedEvent turn && turn.TurnNumber == _turnNumber && turn.ActorSeat == _turnProgression.OwnerSeat);
            if (currentStart < 0 || ended <= currentStart) return new(_turnNumber, _turnProgression.OwnerSeat, WuLuKangTurnOrdinal, 0, false, false);
            history = history.Take(ended + 1).ToArray();
        }
        var start = Array.FindLastIndex(history, e => e is TurnStartedEvent);
        var prefix = start < 0 ? [] : history.Skip(start + 1).ToArray();
        return new(_turnNumber, _turnProgression.OwnerSeat, history.OfType<TurnStartedEvent>().Count(), history.Length,
            prefix.OfType<CardUseDeclaredEvent>().Any(e => e.SourceSeat == owner), prefix.OfType<CardRespondedEvent>().Any(e => e.ResponderSeat == owner));
    }
    private bool WuLuKangQualifiedAtCutoff(int owner, WuLuKangKegouQualification q)
    {
        var history = CompleteProgramEventHistory().Take(checked((int)q.HistoryCutoff)).ToArray(); var start = Array.FindLastIndex(history, e => e is TurnStartedEvent);
        if (q.HistoryCutoff < 0 || q.HistoryCutoff > CompleteProgramEventHistory().Count() || start < 0 || history[start] is not TurnStartedEvent turn ||
            turn.TurnNumber != q.ActualTurn || turn.ActorSeat != q.TurnOwner || history.OfType<TurnStartedEvent>().Count() != q.TurnOrdinal) return false;
        var prefix = history.Skip(start + 1).ToArray(); return q.Used == prefix.OfType<CardUseDeclaredEvent>().Any(e => e.SourceSeat == owner) &&
            q.Played == prefix.OfType<CardRespondedEvent>().Any(e => e.ResponderSeat == owner);
    }
    private bool WuLuKangActualEndedCutoff(WuLuKangKegouQualification q) => q.HistoryCutoff > 0 &&
        CompleteProgramEventHistory().Take(checked((int)q.HistoryCutoff)).LastOrDefault() is TurnEndedEvent ended && ended.TurnNumber == q.ActualTurn && ended.ActorSeat == q.TurnOwner;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 29700, SkillProgramEffectOp.ResolveWuLuKangKegou)]
    private bool CanOfferWuLuKangKegou(ProgramTriggerLegalityContext c)
    {
        if (c.SkillId != WuLuKangComposition.Kegou || !c.Owner.IsAlive || !HasWuLuKangPindianInput(c.Owner) || !_players.Any(p => p.IsAlive && p.Seat != c.OwnerSeat && CanBePindianTarget(c.OwnerSeat, p.Seat))) return false;
        if (c.Context.Window == SkillProgramTriggerWindow.PlayPhaseStarting) return c.Context.SourceSeat == c.OwnerSeat && CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase && phase.ActorSeat == c.OwnerSeat && IsRecordedActualDiscardRecoveryPhase(phase);
        var q = CaptureWuLuKangQualification(c.OwnerSeat, freezeAtEnded: true); return c.Context.Window == SkillProgramTriggerWindow.AfterTurnEnded && (q.Used || q.Played);
    }
    private bool WuLuKangKegouParent(ProgramSkillFrame f)
    {
        if (f.SkillId != WuLuKangComposition.Kegou || f.InstructionIndex != 1 || f.TriggerId is null || f.ActivationId != f.TriggerId || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            f.WindowContext is not { } c || c.OwnerSeat != f.OwnerSeat || _contentRegistry.GetSkill(f.SkillId).Program is not { } p || p.GameplayHash != f.GameplayHash || !WuLuKangComposition.IsKegou(p)) return false;
        if (c.Window == SkillProgramTriggerWindow.PlayPhaseStarting && f.TriggerId == "play-start")
            return _resolutionStack.OfType<PlayPhaseStartingBoundaryFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is { } w && w.OwnerSeat == f.OwnerSeat && w.ItemIndex >= 0 && w.ItemIndex < w.Items.Count &&
                w.Items[w.ItemIndex].Candidate is { } candidate && MountObserverCandidateMatches(f, candidate);
        if (c.Window == SkillProgramTriggerWindow.AfterTurnEnded && f.TriggerId is "used-own-turn-end" or "used-other-turn-end")
            return _resolutionStack.OfType<DeferredTurnEndFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is { } ended && MatchesAfterTurnEndedChild(ended, f, c);
        return false;
    }
    private SkillProgramStepOutcome BeginWuLuKangKegou(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id); var q = CaptureWuLuKangQualification(f.OwnerSeat, f.WindowContext?.Window == SkillProgramTriggerWindow.AfterTurnEnded);
        if (!WuLuKangKegouParent(f) || f.WuLuKangKegou is not null || f.WindowContext!.Window == SkillProgramTriggerWindow.AfterTurnEnded && !(q.Used || q.Played))
            throw new InvalidOperationException("Kegou requires its exact own actual Play-start or used/played actual-turn-end.");
        var r = new WuLuKangKegouReceipt(WuLuKangSource(f), f.GameplayHash, f.WindowContext.ParentFrameId, q);
        ReplaceRuntimeTop(f = f with { WuLuKangKegou = r }); AdvanceEventRulesAndQueueFact(new WuLuKangKegouStartedEvent(f.Id, r.Source, r.GameplayHash, r.WindowId, q));
        PublishWuLuKang(f, f.OwnerSeat, WuLuKangKegouChoices(f), "克构：选择一名其他角色拼点。"); return SkillProgramStepOutcome.AwaitChoice;
    }
    private bool CanWuLuKangKegouSlash(ProgramSkillFrame f) => f.WuLuKangKegou is { OpponentSeat: { } other } && _winner == Winner.None &&
        _players[f.OwnerSeat].IsAlive && _players[other].IsAlive && CanUseVirtualSlashTarget(_players[other], _players[f.OwnerSeat], CardKind.Slash, Suit.None, effectiveColor: null, effectiveRank: 0, physicalCardIds: [], conversionSource: f.WuLuKangKegou.Source) &&
        !HasTurnCardTargetRestriction(other, SkillProgramCardTargetRestriction.SelfOnly) && !IsDirectedCardTargetProhibited(other, f.OwnerSeat, CardKind.Slash) &&
        !IsCardTargetProhibited(_players[f.OwnerSeat], CardKind.Slash, Suit.None, null) && !HasBeneficiarySuitShield(other, f.OwnerSeat, Suit.None) && !IsCuiYanNextUseForbidden(other, [], Suit.None);
    private IReadOnlyList<LegalAction> WuLuKangKegouSlashPlans(ProgramSkillFrame f)
    {
        if (!CanWuLuKangKegouSlash(f)) return [];
        var r = f.WuLuKangKegou!; var actor = _players[r.OpponentSeat!.Value];
        var actions = new List<LegalAction> { new(LegalActionKind.Slash, 0, f.OwnerSeat, "视为对其使用一张杀", CardKind.Slash) { ConversionSource = r.Source } };
        AddNextCardTargetAdjustmentActions(actions, actor); AddNextActualUseAdjustmentActions(actions, actor);
        return Array.AsReadOnly(actions.Where(a => a.TargetSeats.Count > 0 && a.TargetSeats[0] == f.OwnerSeat).ToArray());
    }
    private IReadOnlyList<PromptChoice> WuLuKangKegouChoices(ProgramSkillFrame f)
    {
        var r = f.WuLuKangKegou!;
        Dictionary<string, string> P(string branch) => new() { ["program-action"] = "wu-lu-kang-kegou", ["branch"] = branch, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["iteration"] = r.Iteration.ToString(CultureInfo.InvariantCulture) };
        if (r.Stage == WuLuKangKegouStage.OpponentChoice)
            return Array.AsReadOnly(_players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat && CanBePindianTarget(f.OwnerSeat, p.Seat)).Select(p => new PromptChoice(new($"wu-lu-kang.{f.Id}.{r.Iteration}.opponent.{p.Seat}"), $"与{p.Name}拼点", [], [p.Seat], P("opponent"))).ToArray());
        var choices = new List<PromptChoice>();
        if (r.Stage == WuLuKangKegouStage.OpponentSlashChoice)
        {
            foreach (var plan in WuLuKangKegouSlashPlans(f))
            {
                var parameters = P("slash"); parameters["adjusted"] = IsTargetAdjustmentAction(_players[r.OpponentSeat!.Value], plan) ? "true" : "false";
                choices.Add(new(new($"wu-lu-kang.{f.Id}.{r.Iteration}.slash.{string.Join("-", plan.TargetSeats)}"), plan.Description, [], plan.TargetSeats, parameters));
            }
            choices.Add(new(new($"wu-lu-kang.{f.Id}.{r.Iteration}.decline"), "不使用杀", [], [], P("decline")));
        }
        if (r.Stage == WuLuKangKegouStage.RepeatChoice)
        {
            if (_players[f.OwnerSeat].IsAlive && HasWuLuKangPindianInput(_players[f.OwnerSeat]) && _players.Any(p => p.IsAlive && p.Seat != f.OwnerSeat && CanBePindianTarget(f.OwnerSeat, p.Seat))) choices.Add(new(new($"wu-lu-kang.{f.Id}.{r.Iteration}.repeat"), "继续拼点", [], [], P("repeat")));
            choices.Add(new(new($"wu-lu-kang.{f.Id}.{r.Iteration}.repeat-decline"), "结束克构", [], [], P("repeat-decline")));
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    [ProgramAnswer("wu-lu-kang-kegou")]
    private void AnswerWuLuKangKegou(PromptChoice selected)
    {
        var f = GetActiveProgramFrame(_resolutionStack[^1].Id); var r = f.WuLuKangKegou!;
        var chooser = r.Stage == WuLuKangKegouStage.OpponentSlashChoice ? r.OpponentSeat!.Value : f.OwnerSeat;
        if (!ValidWuLuKangKegou(f) || _pendingDecision?.PlayerSeat != chooser || !AssistedChoicesEqual([selected], WuLuKangKegouChoices(f).Where(c => c.Id == selected.Id).ToArray())) throw new InvalidOperationException("Kegou requires the exact current participant's canonical choice.");
        ClearPendingDecision(); var branch = selected.Parameters["branch"];
        if (r.Stage == WuLuKangKegouStage.OpponentChoice)
        {
            var other = selected.Targets.Single(); ReplaceRuntimeTop(f = f with { WuLuKangKegou = r with { Stage = WuLuKangKegouStage.Pindian, OpponentSeat = other } });
            var skill = _contentRegistry.GetSkill(f.SkillId); BeginSharedPindian(f.Id, new(f.SkillId, skill.Name, skill.Name, skill.Description), f.OwnerSeat, other,
                programResultBind: $"wu-lu-kang-kegou-{r.Iteration}");
            var pair = (PindianFrame)_resolutionStack[^1]; var parent = (ProgramSkillFrame)_resolutionStack[^2];
            ReplaceRuntimeFrame(parent.Id, parent with { WuLuKangKegou = parent.WuLuKangKegou! with { PindianId = pair.Id } });
            AdvanceEventRulesAndQueueFact(new WuLuKangKegouPindianIssuedEvent(f.Id, r.Iteration, pair.Id, other)); return;
        }
        if (r.Stage == WuLuKangKegouStage.OpponentSlashChoice)
        {
            var accepted = branch == "slash"; ReplaceRuntimeTop(f = f with { WuLuKangKegou = r with { SlashAccepted = accepted, Stage = accepted ? WuLuKangKegouStage.SlashIssuing : WuLuKangKegouStage.RepeatChoice } });
            AdvanceEventRulesAndQueueFact(new WuLuKangKegouSlashDecidedEvent(f.Id, r.Iteration, r.OpponentSeat!.Value, accepted));
            if (accepted)
            {
                var adjusted = selected.Parameters["adjusted"] == "true";
                ReplaceRuntimeTop(f = f with { WuLuKangKegou = f.WuLuKangKegou! with { NativeTargets = selected.Targets, NativeTargetsAdjusted = adjusted } });
                for (var i = 0; i < selected.Targets.Count; i++) AdvanceEventRulesAndQueueFact(new WuLuKangKegouSlashTargetEvent(f.Id, r.Iteration, i, selected.Targets[i], adjusted));
                if (adjusted) _selectedNextCardTargetSeats = selected.Targets;
                IssueWuLuKangKegouSlash(f);
            }
            else ContinueWuLuKangKegou(f.Id); return;
        }
        if (r.Stage != WuLuKangKegouStage.RepeatChoice) throw new InvalidOperationException("Kegou cannot answer during a paid native child.");
        AdvanceEventRulesAndQueueFact(new WuLuKangKegouRepeatDecidedEvent(f.Id, r.Iteration, branch == "repeat"));
        if (branch != "repeat") { FinishWuLuKangKegou(f); return; }
        ReplaceRuntimeTop(f = f with { WuLuKangKegou = r with { Stage = WuLuKangKegouStage.OpponentChoice, Iteration = r.Iteration + 1, OpponentSeat = null, PindianId = null,
            Result = null, PindianCancelled = false, SlashAccepted = false, NativeTargets = [], NativeTargetsAdjusted = false, NativeUseId = 0, ActionId = 0, NativeReturned = false, Gains = [], GainIndex = 0 }, PendingMovementContinuation = null });
        PublishWuLuKang(f, f.OwnerSeat, WuLuKangKegouChoices(f), "克构：重新选择一名其他角色继续拼点。");
    }
    private void ContinueWuLuKangKegou(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { WuLuKangKegou: { } r } f || f.Id != id) return;
        AssertWuLuKang(f);
        if (r.PindianCancelled || _winner != Winner.None) { FinishWuLuKangKegou(f); return; }
        if (r.Stage == WuLuKangKegouStage.Pindian)
        {
            if (r.PindianId is not { } pairId || ProgramEventHistory<PindianResultDeterminedEvent>().SingleOrDefault(e => e.FrameId == pairId) is not { } determined ||
                !f.PindianResultBindings.Any(b => b.Name == $"wu-lu-kang-kegou-{r.Iteration}" && b.SourceSeat == f.OwnerSeat && b.OpponentSeat == r.OpponentSeat && b.SourceRank == determined.Result.SourceRank && b.OpponentRank == determined.Result.OpponentRank))
                throw new InvalidOperationException("Kegou must receive its genuine whole pair result after all global effects and claims.");
            ReplaceRuntimeTop(f = f with { WuLuKangKegou = r = r with { Result = determined.Result, Stage = WuLuKangKegouStage.PindianChildren } });
            AdvanceEventRulesAndQueueFact(new WuLuKangKegouPindianReturnedEvent(id, r.Iteration, pairId, determined.Result));
        }
        if (r.Stage is WuLuKangKegouStage.NativeSlash or WuLuKangKegouStage.SlashIssuing) return;
        if (DrainWuLuKangChildren(f)) return;
        f = GetActiveProgramFrame(id); r = f.WuLuKangKegou!;
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive) { FinishWuLuKangKegou(f); return; }
        if (r.Stage == WuLuKangKegouStage.PindianChildren && r.Result!.SourceWon)
        {
            var cards = _cardZones.CardsAt(CardLocation.DrawPile).Reverse().GroupBy(c => c.Rank).OrderBy(g => g.Key).Take(2).Select(g => g.First()).ToArray(); var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { WuLuKangKegou = r with { Stage = WuLuKangKegouStage.GainChildren, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            foreach (var card in cards) MoveCard(card, CardLocation.DrawPile, CardLocation.Hand(f.OwnerSeat), new(WuLuKangKegouGainReason), m =>
            {
                var current = GetActiveProgramFrame(id); var paid = new WuLuKangKegouGain(m.CardId, card.Rank, m.From, m.To, m.Sequence);
                ReplaceRuntimeTop(current with { WuLuKangKegou = current.WuLuKangKegou! with { Gains = current.WuLuKangKegou.Gains.Append(paid).ToArray(), After = m.Sequence } });
                AdvanceEventRulesAndQueueFact(new WuLuKangKegouGainPaidEvent(id, r.Iteration, m.CardId, card.Rank, m.From, m.To, m.Sequence));
            });
            SealWuLuKangMovements(id, before); ContinueWuLuKangKegou(id); return;
        }
        if (r.Stage == WuLuKangKegouStage.GainChildren) { FinishWuLuKangKegou(f); return; }
        if (r.Stage == WuLuKangKegouStage.PindianChildren)
        {
            if (r.OpponentSeat is { } other && _players[other].IsAlive)
            { ReplaceRuntimeTop(f = f with { WuLuKangKegou = r with { Stage = WuLuKangKegouStage.OpponentSlashChoice }, PendingMovementContinuation = null }); PublishWuLuKang(f, other, WuLuKangKegouChoices(f), "克构：你可视为对其使用一张杀。"); return; }
            ReplaceRuntimeTop(f = f with { WuLuKangKegou = r = r with { Stage = WuLuKangKegouStage.RepeatChoice }, PendingMovementContinuation = null });
        }
        if (r.Stage == WuLuKangKegouStage.NativeSlashChildren) ReplaceRuntimeTop(f = f with { WuLuKangKegou = r = r with { Stage = WuLuKangKegouStage.RepeatChoice }, PendingMovementContinuation = null });
        if (r.Stage == WuLuKangKegouStage.RepeatChoice) { PublishWuLuKang(f, f.OwnerSeat, WuLuKangKegouChoices(f), "克构：是否继续拼点？"); return; }
    }
    private void FinishWuLuKangKegou(ProgramSkillFrame f)
    {
        var r = f.WuLuKangKegou!; AdvanceEventRulesAndQueueFact(new WuLuKangKegouCompletedEvent(f.Id, r.Iteration + (r.PindianId is null ? 0 : 1)));
        ReplaceRuntimeTop(f = f with { WuLuKangKegou = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveWuLuKangKegou)]
    private PromptChoice AiWuLuKangKegou(PendingDecision pending, ProgramSkillFrame f)
    {
        var r = f.WuLuKangKegou!;
        if (r.Stage == WuLuKangKegouStage.OpponentChoice) return pending.Choices.OrderBy(c => CreateSnapshot(f.OwnerSeat).Players[c.Targets.Single()].HandCount).ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
        if (r.Stage == WuLuKangKegouStage.OpponentSlashChoice) return pending.Choices.FirstOrDefault(c => c.Parameters["branch"] == "slash") ?? pending.Choices[0];
        var high = GetHand(_players[f.OwnerSeat]).Any(c => EffectivePindianRank(_players[f.OwnerSeat], c) >= 10);
        return pending.Choices.FirstOrDefault(c => c.Parameters["branch"] == (high ? "repeat" : "repeat-decline")) ?? pending.Choices[^1];
    }
}
