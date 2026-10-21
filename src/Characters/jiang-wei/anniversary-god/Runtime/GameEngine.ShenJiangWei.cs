namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShenJwAction = "shen-jiang-wei", ShenJwReveal = "jiufa-revealed";
    private static CardConversionSource ShenJwSource(ProgramSkillFrame f) => new(f.SkillId, f.ActivationId, f.OwnerSeat, f.SkillInstanceId);
    private bool HasShenJw => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveShenJiangWeiTianren);
    [ProgramCapability] private sealed class ShenJwHost(GameEngine e) : IShenJiangWeiHost
    { public SkillProgramStepOutcome Execute(ProgramSkillFrame f, SkillProgramEffectOp op) => e.BeginShenJw(f, op); }
    [ProgramCapability] private sealed class ShenJwSelection(GameEngine e) : IShenJiangWeiSelection
    { public bool CanPay(int owner) => e._players[owner].IsAlive && e._players[owner].HasHpValue && e._players[owner].MaxHp > 9 && !e.ProgramEventHistory<ShenJiangWeiPingxiangPaidEvent>().Any(p => p.Source.OwnerSeat == owner); }

    // Capture while the native Use/response still owns its exact processing
    // invoice. Later discard windows can run after that native producer retires.
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.CaptureMovement, 38400)]
    private void CaptureShenJwDiscardOrigins(CardMovementBatchContext b)
    {
        if (!HasShenJw) return;
        foreach (var m in b.Movements.Where(m => m.To == CardLocation.DiscardPile && m.From != m.To && CardUseCategoryCatalog.Get(m.CardKind) is CardUseCategories.Basic or CardUseCategories.InstantTrick))
            AdvanceEventRulesAndQueueFact(new ShenJiangWeiDiscardOriginEvent(b.Id, m.CardId, m.Sequence, IsNativeTrueUseDiscardMovement(b, m)));
    }
    private int[] ShenJwDiscardIndexes(CardMovementBatchContext b) => b.Movements.Select((m, i) => (m, i)).Where(x =>
        x.m.To == CardLocation.DiscardPile && x.m.From != x.m.To && CardUseCategoryCatalog.Get(x.m.CardKind) is CardUseCategories.Basic or CardUseCategories.InstantTrick &&
        ProgramEventHistory<ShenJiangWeiDiscardOriginEvent>().Count(e => e == new ShenJiangWeiDiscardOriginEvent(b.Id, x.m.CardId, x.m.Sequence, false)) == 1).Select(x => x.i).ToArray();
    [ProgramPipelineHook(ProgramPipelinePhase.SelectDiscardBatchIndexes, 38400)]
    private int[]? SelectShenJwDiscards(CardMovementBatchContext b, ProgramTriggerCandidate c, ProgramInstructionFeatures features) =>
        features.HasOperation(SkillProgramEffectOp.ResolveShenJiangWeiTianren) ? ShenJwDiscardIndexes(b) : null;

    private ProgramCardTriggerWindowFrame? ShenJwActionParent(ProgramSkillWindowContext w) =>
        _resolutionStack.SingleOrDefault(f => f.Id == w.ParentFrameId) is ProgramCardTriggerWindowFrame p && w.CardUse?.CardActionId == p.Action.ActionId ? p : null;
    private static CardActionContext ShenJwCompletedAction(ProgramCardTriggerWindowFrame p) => p.ResponseCompletion is null ? p.Action : ResponseCompletionActorAction(p);
    [ProgramPipelineHook(ProgramPipelinePhase.CollectIssuedCardTrigger, 38400)]
    private void CaptureShenJwCompletedName(CardActionContext action, SkillProgramTriggerWindow window, List<ProgramCardTriggerCandidate> candidates)
    {
        if (window is not (SkillProgramTriggerWindow.CardUseCompleted or SkillProgramTriggerWindow.CardResponseCompleted)) return;
        foreach (var c in candidates.Where(c => c.SkillId == ShenJiangWeiComposition.Jiufa && c.OwnerSeat == action.ActorSeat).DistinctBy(c => c.SkillInstanceId))
        {
            var history = ProgramEventHistory<ShenJiangWeiNameRecordedEvent>().Where(e => e.Source.OwnerSeat == c.OwnerSeat && e.Source.SkillInstanceId == c.SkillInstanceId).ToArray();
            if (history.Any(e => e.ActionId == action.ActionId)) continue;
            var cycle = history.LastOrDefault() is { } last ? last.Cycle + (last.DistinctCount == 9 ? 1 : 0) : 0;
            var names = history.Where(e => e.Cycle == cycle).Select(e => e.Name).ToHashSet(); var name = ProgramBasicCardName(action.EffectiveKind); names.Add(name);
            var native = window == SkillProgramTriggerWindow.CardResponseCompleted
                ? _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault(p => p.ResponseCompletion is not null && p.Action.ActionId == action.ActionId)?.Id
                : _resolutionStack.OfType<CardUseFrame>().LastOrDefault(u => u.Action?.ActionId == action.ActionId)?.Id;
            if (native is null) throw new InvalidOperationException("Jiufa's name ledger requires its actual completed native action owner.");
            AdvanceEventRulesAndQueueFact(new ShenJiangWeiNameRecordedEvent(native.Value, new(c.SkillId, c.TriggerId, c.OwnerSeat, c.SkillInstanceId), action.ActionId, name, cycle, names.Count));
        }
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 38400, SkillProgramEffectOp.ResolveShenJiangWeiTianren, SkillProgramEffectOp.ResolveShenJiangWeiJiufa)]
    private bool CanRunShenJw(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w)
    {
        if (t.Effects.Single().Op == SkillProgramEffectOp.ResolveShenJiangWeiJiufa)
            return ShenJwActionParent(w) is { } p && ShenJwCompletedAction(p) is { } a && a.ActorSeat == c.OwnerSeat &&
                ProgramEventHistory<ShenJiangWeiNameRecordedEvent>().Any(e => e.Source.OwnerSeat == c.OwnerSeat && e.Source.SkillInstanceId == c.SkillInstanceId && e.ActionId == a.ActionId && e.DistinctCount == 9) &&
                !ProgramEventHistory<ShenJiangWeiStartedEvent>().Any(e => e.Operation == SkillProgramEffectOp.ResolveShenJiangWeiJiufa && e.Source.OwnerSeat == c.OwnerSeat && e.Source.SkillInstanceId == c.SkillInstanceId && e.ActionId == a.ActionId);
        if (w.Window == SkillProgramTriggerWindow.AfterHealthChanged)
            return w.HpChange?.Kind == HpChangeKind.MaximumHp && _players[c.OwnerSeat].MaxHp > 0 && _players[c.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.Tianren) >= _players[c.OwnerSeat].MaxHp;
        return w.MovementBatch is { } b && w.MovementIndex is { } i && ShenJwDiscardIndexes(b).Contains(i) &&
            !ProgramEventHistory<ShenJiangWeiMarkReceivedEvent>().Any(e => e.OwnerSeat == c.OwnerSeat && e.MovementSequence == b.Movements[i].Sequence);
    }
    private SkillProgramStepOutcome BeginShenJw(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.ShenJiangWei is not null || f.InstructionIndex != 1 || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash)
            throw new InvalidOperationException("Shen Jiang Wei requires the current unpaid instruction.");
        var action = op == SkillProgramEffectOp.ResolveShenJiangWeiJiufa ? ShenJwCompletedAction(ShenJwActionParent(f.WindowContext!)!) : null;
        var movement = op == SkillProgramEffectOp.ResolveShenJiangWeiTianren && f.WindowContext?.MovementBatch is { } b ? b.Movements[f.WindowContext.MovementIndex!.Value].Sequence : 0;
        var r = new ShenJiangWeiReceipt(ShenJwSource(f), f.GameplayHash, op, f.InstructionIndex, f.WindowContext?.ParentFrameId ?? 0,
            op == SkillProgramEffectOp.ResolveShenJiangWeiPingxiang ? ShenJiangWeiStage.MaximumChildren : ShenJiangWeiStage.Threshold) { ActionId = action?.ActionId ?? 0, MovementSequence = movement, Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { ShenJiangWei = r });
        AdvanceEventRulesAndQueueFact(new ShenJiangWeiStartedEvent(f.Id, r.Source, r.GameplayHash, op, r.ParentId, r.ActionId, movement));
        if (op == SkillProgramEffectOp.ResolveShenJiangWeiTianren && movement > 0)
        { MutateParticipantMarker(f, f.OwnerSeat, PlayerMarkerKind.Tianren, 1); AdvanceEventRulesAndQueueFact(new ShenJiangWeiMarkReceivedEvent(f.Id, f.OwnerSeat, movement)); }
        if (op == SkillProgramEffectOp.ResolveShenJiangWeiJiufa)
        {
            var recorded = ProgramEventHistory<ShenJiangWeiNameRecordedEvent>().Single(e => e.Source == r.Source && e.ActionId == action!.ActionId && e.DistinctCount == 9);
            ReplaceRuntimeTop(f with { ShenJiangWei = r with { Stage = ShenJiangWeiStage.JiufaOffer, Cycle = recorded.Cycle } });
        }
        if (op == SkillProgramEffectOp.ResolveShenJiangWeiPingxiang)
        {
            var p = _players[f.OwnerSeat]; var before = p.MaxHp;
            if (!p.HasHpValue || before <= 9 || ProgramEventHistory<ShenJiangWeiPingxiangPaidEvent>().Any(e => e.Source.OwnerSeat == f.OwnerSeat)) throw new InvalidOperationException("Pingxiang requires nine genuinely payable maximum HP and its unused game limit.");
            ChangeProgramMaximumHp(f, -9);
            ReplaceRuntimeTop(f with { ShenJiangWei = r with { MaximumBefore = before, MaximumAfter = p.MaxHp } });
            AdvanceEventRulesAndQueueFact(new ShenJiangWeiPingxiangPaidEvent(f.Id, r.Source, r.GameplayHash, before, p.MaxHp));
        }
        ContinueShenJw(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private bool DrainShenJw(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 38400)] private bool ResumeShenJw(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenJiangWei: not null } f || f.Id != id) return false; ContinueShenJw(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 38400)] private bool ReturnShenJwMovement(ProgramSkillFrame f) => f.ShenJiangWei is not null && ResumeShenJw(f.Id);
    private void ContinueShenJw(long id)
    {
        while (_pendingDecision is null && _resolutionStack.LastOrDefault() is ProgramSkillFrame { ShenJiangWei: { } r } f && f.Id == id)
        {
            RequireShenJw(f);
            if (r.Stage is ShenJiangWeiStage.DrawAttempt or ShenJiangWeiStage.NativeIssuing or ShenJiangWeiStage.NativeUse) return;
            if (DrainShenJw(id)) return;
            f = GetActiveProgramFrame(id); r = f.ShenJiangWei!;
            if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishShenJw(f); return; }
            if (r.Operation == SkillProgramEffectOp.ResolveShenJiangWeiTianren)
            {
                if (r.Stage == ShenJiangWeiStage.GrowthChildren) { IssueShenJwDraw(f); return; }
                var p = _players[f.OwnerSeat]; var marks = p.Markers.GetValueOrDefault(PlayerMarkerKind.Tianren);
                if (!HasRuntimeSkillInstance(p, f.SkillId, f.SkillInstanceId) || p.MaxHp <= 0 || marks < p.MaxHp) { FinishShenJw(f); return; }
                var max = p.MaxHp;
                ReplaceRuntimeTop(f = f with { ShenJiangWei = r with { Stage = ShenJiangWeiStage.GrowthChildren, Cycle = r.Cycle + 1, MaximumBefore = max } });
                MutateParticipantMarker(f, f.OwnerSeat, PlayerMarkerKind.Tianren, -max); ChangeProgramMaximumHp(f, 1);
                ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { ShenJiangWei = GetActiveProgramFrame(id).ShenJiangWei! with { MaximumAfter = p.MaxHp } });
                AdvanceEventRulesAndQueueFact(new ShenJiangWeiGrowthPaidEvent(id, f.ShenJiangWei!.Cycle, marks, max, max, p.MaxHp)); continue;
            }
            if (r.Stage == ShenJiangWeiStage.JiufaOffer || r.Stage == ShenJiangWeiStage.RankChoice || r.Stage == ShenJiangWeiStage.SlashChoice)
            { PublishShenJw(f); return; }
            if (r.Stage == ShenJiangWeiStage.RevealChildren)
            {
                var cards = r.Revealed.Select(GetAdvancedCard).ToArray();
                var ranks = cards.GroupBy(c => c.Rank).Where(g => g.Count() > 1).Select(g => g.Key).Order().ToArray();
                ReplaceRuntimeTop(f with { ShenJiangWei = r with { Stage = ShenJiangWeiStage.RankChoice, Ranks = ranks }, PendingMovementContinuation = null }); continue;
            }
            if (r.Stage == ShenJiangWeiStage.GainChildren) { MoveShenJwJiufa(f, false); continue; }
            if (r.Stage == ShenJiangWeiStage.DiscardChildren) { FinishShenJw(f); return; }
            if (r.Stage is ShenJiangWeiStage.MaximumChildren or ShenJiangWeiStage.SlashChildren)
            { ReplaceRuntimeTop(f with { ShenJiangWei = r with { Stage = ShenJiangWeiStage.SlashChoice }, PendingMovementContinuation = null }); continue; }
            if (r.Stage == ShenJiangWeiStage.LostSkillChildren) { FinishShenJw(f); return; }
            throw new InvalidOperationException("Shen Jiang Wei has an unsupported continuation stage.");
        }
    }
    private void FinishShenJw(ProgramSkillFrame supplied)
    { var f = GetActiveProgramFrame(supplied.Id); ReplaceRuntimeTop(f with { ShenJiangWei = null, PendingMovementContinuation = null }); AdvanceRuntimeProgram(f.Id); }
    private void IssueShenJwDraw(ProgramSkillFrame f)
    {
        ReplaceRuntimeTop(f = f with { ShenJiangWei = f.ShenJiangWei! with { Stage = ShenJiangWeiStage.DrawAttempt } });
        BeginOperationProgramDraw(f, f.OwnerSeat, 2, new("skill-program.anniversary:tianren.draw"));
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveShenJiangWeiTianren)] private void ReturnShenJwDraw(ProgramSkillFrame supplied, NativeDrawOutcome o)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.ShenJiangWei!;
        if (r.Stage != ShenJiangWeiStage.DrawAttempt || f.NativeDraw is not null || o.RecipientSeat != f.OwnerSeat || o.RequestedCount != 2 || o.SequenceAfter != _movementSequence || ProgramEventHistory<ShenJiangWeiDrawReturnedEvent>().Any(e => e.FrameId == f.Id && e.Cycle == r.Cycle)) throw new InvalidOperationException("Tianren needs its exact once-only two-card native draw return.");
        ReplaceRuntimeTop(f with { ShenJiangWei = r with { Stage = ShenJiangWeiStage.DrawChildren, Before = o.SequenceBefore, After = o.SequenceAfter } });
        AdvanceEventRulesAndQueueFact(new ShenJiangWeiDrawReturnedEvent(f.Id, r.Cycle, o.ActualDrawIds.Count, o.AttemptFrameId, o.SequenceBefore, o.SequenceAfter)); ContinueShenJw(f.Id);
    }
    private IReadOnlyList<PromptChoice> ShenJwChoices(ProgramSkillFrame f)
    {
        var r = f.ShenJiangWei!;
        PromptChoice Choice(string id, string label, IReadOnlyList<int> cards, IReadOnlyList<int> targets) => new(new($"shen-jw.{f.Id}.{id}"), label, cards, targets, new Dictionary<string,string> { ["program-action"] = ShenJwAction, ["option"] = id });
        if (r.Stage == ShenJiangWeiStage.JiufaOffer) return [Choice("reveal", "亮出九张牌", [], []), Choice("decline", "不发动九伐", [], [])];
        if (r.Stage == ShenJiangWeiStage.RankChoice && r.RankIndex < r.Ranks.Count)
            return Array.AsReadOnly(r.Revealed.Where(id => _cardZones.GetLocation(id) == CardLocation.Processing && GetAdvancedCard(id).Rank == r.Ranks[r.RankIndex]).Select(id => Choice($"card-{id}", $"获得【{GetAdvancedCard(id).DisplayName}】", [id], [])).ToArray());
        if (r.Stage == ShenJiangWeiStage.SlashChoice && r.SlashCount < 9)
            return Array.AsReadOnly(ShenJwSlashTargets(f).Select(seat => Choice($"target-{seat}", $"对 {_players[seat].Name} 使用第 {r.SlashCount + 1} 张火【杀】", [], [seat])).Append(Choice("stop", "结束连续火杀", [], [])).ToArray());
        return [];
    }
    private void PublishShenJw(ProgramSkillFrame f)
    {
        var choices = ShenJwChoices(f); var r = f.ShenJiangWei!;
        if (choices.Count == 0 || r.Stage == ShenJiangWeiStage.SlashChoice && choices.Count == 1)
        {
            if (r.Stage == ShenJiangWeiStage.RankChoice && r.RankIndex < r.Ranks.Count)
            { ReplaceRuntimeTop(f with { ShenJiangWei = r with { RankIndex = r.RankIndex + 1 } }); ContinueShenJw(f.Id); return; }
            if (r.Stage == ShenJiangWeiStage.RankChoice) MoveShenJwJiufa(f, true); else CompleteShenJwPingxiang(f);
            ContinueShenJw(f.Id); return;
        }
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse }); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, r.Stage == ShenJiangWeiStage.RankChoice ? $"九伐：选择点数 {r.Ranks[r.RankIndex]} 的一张牌。" : skill.Description, choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), Choices = choices, IsPrivate = false, SkillPrompt = new(skill.Id, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer(ShenJwAction)] private void AnswerShenJw(PromptChoice c)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenJiangWei: { } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat || !ShenJwChoices(f).Any(x => AssistedChoicesEqual([x], [c]))) throw new InvalidOperationException("Shen Jiang Wei requires an exact current native choice.");
        RequireShenJw(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect }); var option = c.Parameters["option"];
        if (r.Stage == ShenJiangWeiStage.JiufaOffer)
        {
            if (option == "decline") { FinishShenJw(f); return; }
            ReplaceRuntimeTop(f = f with { ShenJiangWei = r with { Stage = ShenJiangWeiStage.RevealChildren, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            var before = _movementSequence; RevealProgramTopCards(f.Id, f.OwnerSeat, 9, null, ShenJwReveal, SkillProgramCardSetVisibility.Public);
            f = GetActiveProgramFrame(f.Id); var ids = GetProgramCardSet(f, ShenJwReveal).CardIds;
            ReplaceRuntimeTop(f with { ShenJiangWei = f.ShenJiangWei! with { Revealed = ids, After = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new ShenJiangWeiMovementPaidEvent(f.Id, ShenJiangWeiStage.RevealChildren, before, _movementSequence, ids.Count));
        }
        else if (r.Stage == ShenJiangWeiStage.RankChoice)
        {
            var id = c.Cards.Single(); AdvanceEventRulesAndQueueFact(new ShenJiangWeiRankChosenEvent(f.Id, r.Ranks[r.RankIndex], id));
            ReplaceRuntimeTop(f with { ShenJiangWei = r with { Selected = r.Selected.Append(id).ToArray(), RankIndex = r.RankIndex + 1 } });
        }
        else if (option == "stop") CompleteShenJwPingxiang(f);
        else { IssueShenJwSlash(f, c.Targets.Single()); return; }
        ContinueShenJw(f.Id);
    }
    private void MoveShenJwJiufa(ProgramSkillFrame f, bool gain)
    {
        var r = f.ShenJiangWei!; var stage = gain ? ShenJiangWeiStage.GainChildren : ShenJiangWeiStage.DiscardChildren;
        var ids = (gain ? r.Selected : r.Revealed.Except(r.Selected)).Where(id => _cardZones.GetLocation(id) == CardLocation.Processing).ToArray(); var before = _movementSequence;
        ReplaceRuntimeTop(f with { ShenJiangWei = r with { Stage = stage, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (ids.Length > 0) MoveProgramCardsFromMultipleSources(ids, gain ? CardLocation.Hand(f.OwnerSeat) : CardLocation.DiscardPile, new(gain ? "skill-program.anniversary:jiufa.obtain" : "skill-program.anniversary:jiufa.discard"));
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { ShenJiangWei = f.ShenJiangWei! with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new ShenJiangWeiMovementPaidEvent(f.Id, stage, before, _movementSequence, ids.Length));
    }
    private void CompleteShenJwPingxiang(ProgramSkillFrame f)
    {
        var r = f.ShenJiangWei!; var p = _players[f.OwnerSeat];
        if (r.Operation != SkillProgramEffectOp.ResolveShenJiangWeiPingxiang || r.Stage != ShenJiangWeiStage.SlashChoice || ProgramEventHistory<ShenJiangWeiPingxiangCompletedEvent>().Any(e => e.FrameId == f.Id)) throw new InvalidOperationException("Pingxiang's then-clause requires its completed native Slash sequence.");
        foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
        var grants = p.SkillGrants.Grants.Where(g => g.SkillId == ShenJiangWeiComposition.Jiufa).ToArray(); foreach (var g in grants) p.SkillGrants.RemoveGrant(g.GrantId);
        ReplaceRuntimeTop(f with { ShenJiangWei = r with { Stage = ShenJiangWeiStage.LostSkillChildren } });
        AdvanceEventRulesAndQueueFact(new ShenJiangWeiPingxiangCompletedEvent(f.Id, f.OwnerSeat, r.SlashCount, grants.Length));
    }
    private IEnumerable<RuleQueryContribution> ShenJiangWeiPingxiangHandLimitContributions(CharacterState p)
    {
        if (ProgramEventHistory<ShenJiangWeiPingxiangCompletedEvent>().Any(e => e.OwnerSeat == p.Seat && ProgramEventHistory<ShenJiangWeiPingxiangPaidEvent>().Any(paid => paid.FrameId == e.FrameId && paid.Source.OwnerSeat == p.Seat && paid.Source.SkillId == ShenJiangWeiComposition.Pingxiang && paid.Source.BindingId == "nine-fire-slashes" && _contentRegistry.GetSkill(paid.Source.SkillId).Program?.GameplayHash == paid.GameplayHash && paid.MaximumBefore - paid.MaximumAfter == 9)))
            yield return new FiniteRuleQueryContribution($"issued:{p.Seat}:anniversary:pingxiang:hand-limit", SkillRuleOperation.Set, Math.Max(0, p.MaxHp), 384);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveShenJiangWeiJiufa, SkillProgramEffectOp.ResolveShenJiangWeiPingxiang)]
    private PromptChoice AiShenJw(PendingDecision d, ProgramSkillFrame f)
    {
        if (f.ShenJiangWei!.Stage == ShenJiangWeiStage.JiufaOffer) return d.Choices.First(c => c.Parameters["option"] == "reveal");
        if (f.ShenJiangWei.Stage == ShenJiangWeiStage.RankChoice) return d.Choices.MaxBy(c => GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[f.OwnerSeat]))!;
        return d.Choices.OrderByDescending(c => c.Targets.Count == 1 ? -_aiBrains[f.OwnerSeat].ScoreProgramTarget(CreateSnapshot(f.OwnerSeat), c.Targets[0], new(0,0,0,1,0,0,false,false)) : 0).First();
    }
}
