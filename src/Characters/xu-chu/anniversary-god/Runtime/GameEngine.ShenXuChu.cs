using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShenXuChuAction = "shen-xu-chu", ZhuangpoDiscardReason = "skill-program.anniversary:zhuangpo.discard";
    private static CardConversionSource ShenXuChuSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramCapability] private sealed class ShenXuChuHost(GameEngine engine) : IShenXuChuHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginShenXuChu(f, op); }

    private ProgramCardTriggerWindowFrame? ZhuangpoParent(ProgramSkillWindowContext? w) => w?.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized ? null :
        _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(p => p.Id == w.ParentFrameId);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 38800, SkillProgramEffectOp.ResolveZhuangpoTarget)]
    private bool CanResolveZhuangpo(ProgramTriggerLegalityContext g) => ZhuangpoParent(g.WindowContext) is { Action: { EffectiveKind: CardKind.Duel } a } &&
        a.ActorSeat == g.OwnerSeat && a.ConversionChain.Contains(new(g.SkillId, ShenXuChuComposition.ViewAs, g.OwnerSeat, g.Candidate.SkillInstanceId)) &&
        a.TargetSeats.Count > 0 && !ProgramEventHistory<ZhuangpoTargetIssuedEvent>().Any(e => e.ActionId == a.ActionId && e.Source.OwnerSeat == g.OwnerSeat && e.Source.SkillInstanceId == g.Candidate.SkillInstanceId);

    // Damage belongs to its source even when dealt outside that source's own
    // turn. Extra turns have distinct TurnStarted numbers; extra phases do not.
    private (int Damage, int[] Seats) ZhengqingMaximum()
    {
        var totals = new Dictionary<(int Turn, int Source), int>(); var actualTurn = 0;
        foreach (var fact in CompleteProgramEventHistory())
        {
            if (fact is RoundStartedEvent) { totals.Clear(); actualTurn = 0; }
            else if (fact is TurnStartedEvent t) actualTurn = t.TurnNumber;
            else if (fact is DamageAppliedEvent { Amount: > 0, SourceLess: false } d && actualTurn > 0 && IsValidPlayerSeat(d.SourceSeat))
            { var key = (actualTurn, d.SourceSeat); totals[key] = checked(totals.GetValueOrDefault(key) + d.Amount); }
        }
        var best = _players.Where(p => p.IsAlive).Select(p => (p.Seat, Damage: totals.Where(t => t.Key.Source == p.Seat).Select(t => t.Value).DefaultIfEmpty().Max())).ToArray();
        var maximum = best.Select(p => p.Damage).DefaultIfEmpty().Max();
        return (maximum, maximum == 0 ? [] : best.Where(p => p.Damage == maximum).Select(p => p.Seat).OrderBy(s => RelativeTableOrder(_currentSeat, s)).ToArray());
    }
    private static int ZhengqingWinner(IReadOnlyList<int> seats, int owner, DeterministicRandom random) =>
        seats.Contains(owner) ? owner : seats.Count == 0 ? -1 : seats.Count == 1 ? seats[0] : seats[random.Next(seats.Count)];
    private SkillProgramStepOutcome BeginShenXuChu(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.ShenXuChu is not null || f.InstructionIndex != 1 || f.WindowContext is not { } w ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash)
            throw new InvalidOperationException("Shen Xu Chu requires his original unpaid trigger instruction.");
        var r = new ShenXuChuReceipt(ShenXuChuSource(f), f.GameplayHash, w.ParentFrameId, f.InstructionIndex, op, ShenXuChuStage.MarkChildren);
        if (op == SkillProgramEffectOp.SettleZhengqingRound)
        {
            if (f.SkillId != ShenXuChuComposition.Zhengqing || w.Window != SkillProgramTriggerWindow.RoundEnding ||
                _resolutionStack.FirstOrDefault() is not RoundEndingBoundaryFrame boundary || boundary.RoundNumber != _roundNumber ||
                ProgramEventHistory<ZhengqingSettledEvent>().Any(e => e.Source == r.Source && e.Round == _roundNumber))
                throw new InvalidOperationException("Zhengqing requires its once-only actual round ending.");
            var (maximum, seats) = ZhengqingMaximum(); var before = _random.State;
            var winner = ZhengqingWinner(seats, f.OwnerSeat, _random);
            var draws = winner < 0 ? [] : winner == f.OwnerSeat ? new[] { new ShenXuChuDraw(winner, Math.Min(5, maximum)) } :
                new[] { new ShenXuChuDraw(winner, 1), new ShenXuChuDraw(f.OwnerSeat, 1) };
            r = r with { TargetSeat = winner, PaidMarks = maximum, Draws = draws, MaximumSeats = seats, RandomBefore = before, RandomAfter = _random.State };
            ReplaceRuntimeTop(f = f with { ShenXuChu = r });
            AdvanceEventRulesAndQueueFact(new ZhengqingSettledEvent(f.Id, r.Source, _roundNumber, winner, maximum));
            foreach (var p in _players)
            {
                var old = p.Markers.GetValueOrDefault(PlayerMarkerKind.QingBanner);
                if (old == 0) continue;
                if (p.IsAlive) MutateParticipantMarker(f, p.Seat, PlayerMarkerKind.QingBanner, -old);
                else
                {
                    foreach (var key in p.MarkerSourceCounts.Keys.Where(k => k.Marker == PlayerMarkerKind.QingBanner).ToArray()) p.MarkerSourceCounts.Remove(key);
                    p.Markers.Remove(PlayerMarkerKind.QingBanner);
                    AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(f.Id, p.Seat, PlayerMarkerKind.QingBanner, -old, 0, f.OwnerSeat, "skill-program.anniversary:zhengqing.clear"));
                }
            }
            if (winner >= 0) MutateParticipantMarker(f, winner, PlayerMarkerKind.QingBanner, maximum);
        }
        else
        {
            if (op != SkillProgramEffectOp.ResolveZhuangpoTarget || f.SkillId != ShenXuChuComposition.Zhuangpo || ZhuangpoParent(w) is not { } parent ||
                LifecycleCardUse(parent.ParentFrameId) is not { Action: { EffectiveKind: CardKind.Duel } action } use || action.ActorSeat != f.OwnerSeat ||
                !action.ConversionChain.Contains(new(f.SkillId, ShenXuChuComposition.ViewAs, f.OwnerSeat, f.SkillInstanceId)))
                throw new InvalidOperationException("Zhuangpo requires the exact converted Duel's designation parent.");
            r = r with { UseId = use.Id, ActionId = action.ActionId, Targets = action.TargetSeats, TargetSeat = action.TargetSeats[0], TargetMarked = action.TargetSeats.Any(s => _players[s].Markers.GetValueOrDefault(PlayerMarkerKind.QingBanner) > 0), Stage = ShenXuChuStage.MarkChoice };
            r = r with { Draws = r.TargetMarked ? [new(f.OwnerSeat, 1)] : [] };
            ReplaceRuntimeTop(f = f with { ShenXuChu = r });
            AdvanceEventRulesAndQueueFact(new ZhuangpoTargetIssuedEvent(f.Id, r.Source, r.GameplayHash, use.Id, action.ActionId, r.TargetMarked ? action.TargetSeats.First(s => _players[s].Markers.GetValueOrDefault(PlayerMarkerKind.QingBanner) > 0) : r.TargetSeat, r.TargetMarked));
        }
        ContinueShenXuChu(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private bool DrainShenXuChu(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 38800)] private bool ResumeShenXuChu(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenXuChu: not null } f || f.Id != id) return false; ContinueShenXuChu(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 38800)] private bool ReturnShenXuChuMovement(ProgramSkillFrame f) => f.ShenXuChu is not null && ResumeShenXuChu(f.Id);
    private void ContinueShenXuChu(long id)
    {
        while (_pendingDecision is null && _resolutionStack.LastOrDefault() is ProgramSkillFrame { ShenXuChu: { } r } f && f.Id == id)
        {
            RequireShenXuChu(f);
            if (r.Stage == ShenXuChuStage.DrawAttempt) return;
            if (DrainShenXuChu(id)) return;
            f = GetActiveProgramFrame(id); r = f.ShenXuChu!;
            if (_winner != Winner.None) { FinishShenXuChu(f); return; }
            if (r.Stage == ShenXuChuStage.MarkChoice)
            {
                if (!_players[r.TargetSeat].IsAlive || !_players[f.OwnerSeat].IsAlive || _players[f.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.QingBanner) == 0 || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
                { NextZhuangpoTarget(f); continue; }
                PublishShenXuChu(f); return;
            }
            if (r.Stage == ShenXuChuStage.DiscardChoice)
            {
                if (!_players[r.TargetSeat].IsAlive || ZhuangpoDiscardOptions(f).Count == 0) { NextZhuangpoTarget(f); continue; }
                PublishShenXuChu(f); return;
            }
            if (r.Stage == ShenXuChuStage.DiscardChildren) { NextZhuangpoTarget(f); continue; }
            if (r.Stage == ShenXuChuStage.DrawChildren)
                ReplaceRuntimeTop(f = f with { ShenXuChu = r = r with { Stage = ShenXuChuStage.MarkChildren, DrawIndex = r.DrawIndex + 1 }, PendingMovementContinuation = null });
            if (r.Stage == ShenXuChuStage.MarkChildren)
            {
                while (r.DrawIndex < r.Draws.Count && (!_players[r.Draws[r.DrawIndex].Seat].IsAlive || r.Draws[r.DrawIndex].Count == 0))
                    ReplaceRuntimeTop(f = f with { ShenXuChu = r = r with { DrawIndex = r.DrawIndex + 1 } });
                if (r.DrawIndex >= r.Draws.Count) { FinishShenXuChu(f); return; }
                var draw = r.Draws[r.DrawIndex];
                ReplaceRuntimeTop(f = f with { ShenXuChu = r with { Stage = ShenXuChuStage.DrawAttempt } });
                BeginOperationProgramDraw(f, draw.Seat, draw.Count, new("skill-program." + f.SkillId + ".draw")); return;
            }
        }
    }
    private void NextZhuangpoTarget(ProgramSkillFrame f)
    {
        var r = f.ShenXuChu!; var next = r.TargetIndex + 1;
        ReplaceRuntimeTop(f with { ShenXuChu = r with { Stage = next < r.Targets.Count ? ShenXuChuStage.MarkChoice : ShenXuChuStage.MarkChildren,
            TargetIndex = next, TargetSeat = next < r.Targets.Count ? r.Targets[next] : r.TargetSeat, PaidMarks = 0, RequiredDiscards = 0, Selected = [] }, PendingMovementContinuation = null });
    }
    private void FinishShenXuChu(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f with { ShenXuChu = null, PendingMovementContinuation = null }); AdvanceRuntimeProgram(f.Id); }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.SettleZhengqingRound)]
    private void ReturnZhengqingDraw(ProgramSkillFrame f, NativeDrawOutcome outcome) => ReturnShenXuChuDraw(f, outcome);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveZhuangpoTarget)]
    private void ReturnZhuangpoDraw(ProgramSkillFrame f, NativeDrawOutcome outcome) => ReturnShenXuChuDraw(f, outcome);
    private void ReturnShenXuChuDraw(ProgramSkillFrame supplied, NativeDrawOutcome outcome)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.ShenXuChu ?? throw new InvalidOperationException("Shen Xu Chu draw lost its frame.");
        if (r.Stage != ShenXuChuStage.DrawAttempt || r.DrawIndex >= r.Draws.Count || r.Draws[r.DrawIndex] != new ShenXuChuDraw(outcome.RecipientSeat, outcome.RequestedCount) ||
            outcome.SequenceAfter != _movementSequence || ProgramEventHistory<ShenXuChuDrawReturnedEvent>().Any(e => e.FrameId == f.Id && e.Index == r.DrawIndex))
            throw new InvalidOperationException("Shen Xu Chu requires the once-only exact native draw return.");
        ReplaceRuntimeTop(f with { ShenXuChu = r with { Stage = ShenXuChuStage.DrawChildren, Before = outcome.SequenceBefore, After = outcome.SequenceAfter } });
        AdvanceEventRulesAndQueueFact(new ShenXuChuDrawReturnedEvent(f.Id, r.DrawIndex, outcome.RecipientSeat, outcome.RequestedCount, outcome.ActualDrawIds.Count, outcome.SequenceBefore, outcome.SequenceAfter));
        ContinueShenXuChu(f.Id);
    }
    private IReadOnlyList<PromptChoice> ZhuangpoDiscardOptions(ProgramSkillFrame f) => BuildOwnedCardPaymentChoices(f.Id, f.ShenXuChu!.TargetSeat,
        f.ShenXuChu.TargetSeat, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard, canSelect: (_, c) => !f.ShenXuChu.Selected.Contains(c.Id));
    private IReadOnlyList<PromptChoice> ShenXuChuChoices(ProgramSkillFrame f)
    {
        var r = f.ShenXuChu!;
        PromptChoice Choice(string option, string text, IReadOnlyList<int> cards) => new(new($"shen-xu-chu.{f.Id}.{r.TargetIndex}.{option}"), text, cards, [],
            new Dictionary<string,string> { ["program-action"] = ShenXuChuAction, ["option"] = option });
        return r.Stage == ShenXuChuStage.MarkChoice ? Array.AsReadOnly(Enumerable.Range(0, checked(_players[f.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.QingBanner) + 1))
            .Select(n => Choice(n.ToString(CultureInfo.InvariantCulture), n == 0 ? "不移去擎" : $"移去 {n} 个擎，令 {_players[r.TargetSeat].Name} 弃置 {n} 张牌", [])).ToArray()) :
            Array.AsReadOnly(ZhuangpoDiscardOptions(f).Select(c => Choice("card-" + c.Cards.Single(), c.Label, c.Cards)).ToArray());
    }
    private void PublishShenXuChu(ProgramSkillFrame f)
    {
        var r = f.ShenXuChu!; var chooser = r.Stage == ShenXuChuStage.MarkChoice ? f.OwnerSeat : r.TargetSeat;
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        PublishParticipantHandChoice(f, chooser, ShenXuChuChoices(f), r.Stage == ShenXuChuStage.MarkChoice ? "壮魄：选择移去的擎数量。" : $"壮魄：还需弃置 {r.RequiredDiscards - r.Selected.Count} 张牌。", r.TargetSeat);
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer(ShenXuChuAction)] private void AnswerShenXuChu(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenXuChu: { } r } f ||
            _pendingDecision?.PlayerSeat != (r.Stage == ShenXuChuStage.MarkChoice ? f.OwnerSeat : r.TargetSeat) ||
            !AssistedChoicesEqual([selected], ShenXuChuChoices(f).Where(c => c.Id == selected.Id).ToArray()))
            throw new InvalidOperationException("Zhuangpo requires the current participant's exact private answer.");
        RequireShenXuChu(f); ClearPendingDecision();
        if (r.Stage == ShenXuChuStage.MarkChoice)
        {
            var count = int.Parse(selected.Parameters["option"], CultureInfo.InvariantCulture);
            var before = _players[f.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.QingBanner);
            if (count == 0) { NextZhuangpoTarget(f); ContinueShenXuChu(f.Id); return; }
            if (!HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) || count < 1 || count > before) throw new InvalidOperationException("Zhuangpo's unspent mark cost is no longer available.");
            var required = Math.Min(count, ZhuangpoDiscardOptions(f).Count);
            ReplaceRuntimeTop(f = f with { ShenXuChu = r with { Stage = ShenXuChuStage.DiscardChoice, PaidMarks = count, RequiredDiscards = required } });
            MutateParticipantMarker(f, f.OwnerSeat, PlayerMarkerKind.QingBanner, -count);
            AdvanceEventRulesAndQueueFact(new ZhuangpoMarksPaidEvent(f.Id, f.OwnerSeat, r.TargetSeat, before, count, before - count));
            if (required == 0) NextZhuangpoTarget(f); ContinueShenXuChu(f.Id); return;
        }
        if (r.Stage != ShenXuChuStage.DiscardChoice) throw new InvalidOperationException("Zhuangpo cannot pay a completed discard twice.");
        var ids = r.Selected.Append(selected.Cards.Single()).ToArray();
        ReplaceRuntimeTop(f = f with { ShenXuChu = r = r with { Selected = ids } });
        if (ids.Length < r.RequiredDiscards) { PublishShenXuChu(f); return; }
        var start = _movementSequence;
        ReplaceRuntimeTop(f = f with { ShenXuChu = r with { Stage = ShenXuChuStage.DiscardChildren, Before = start, After = start }, PendingMovementContinuation = new(r.TargetSeat, 0, null) });
        MoveProgramLogicalDiscard(f, r.TargetSeat, ids, new(ZhuangpoDiscardReason), (_, records) =>
        {
            var active = GetActiveProgramFrame(f.Id); var end = records.Select(m => (long)m.Sequence).DefaultIfEmpty(start).Max();
            ReplaceRuntimeTop(active with { ShenXuChu = active.ShenXuChu! with { After = end } });
            AdvanceEventRulesAndQueueFact(new ZhuangpoDiscardPaidEvent(f.Id, r.TargetSeat, ids.Length, start, end));
        });
        AdvanceRuntimeProgram(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveZhuangpoTarget)] private PromptChoice ChooseShenXuChu(PendingDecision p, ProgramSkillFrame f) =>
        f.ShenXuChu!.Stage == ShenXuChuStage.MarkChoice ? p.Choices.First(c => c.Parameters["option"] == "0") : p.Choices[0];
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 38800)] private int ZhuangpoDuelBonus(IDamageAttempt attack)
    {
        if (attack.EffectiveCardKind != CardKind.Duel || LifecycleCardUse(attack.ResolutionId) is not { Action: { } action } use) return 0;
        return ProgramEventHistory<ZhuangpoTargetIssuedEvent>().Any(e => e.UseId == use.Id && e.ActionId == action.ActionId && e.TargetMarked &&
            e.Source.SkillId == ShenXuChuComposition.Zhuangpo && e.Source.OwnerSeat == action.ActorSeat &&
            action.ConversionChain.Contains(new(e.Source.SkillId, ShenXuChuComposition.ViewAs, e.Source.OwnerSeat, e.Source.SkillInstanceId))) ? 1 : 0;
    }
}
