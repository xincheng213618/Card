using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static CardConversionSource ShenZhSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private int LinCold(int seat) => _players[seat].Markers.GetValueOrDefault(PlayerMarkerKind.LinCold);
    private bool HasLishiCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ChooseLishiPunishments);
    [ProgramCapability] private sealed class ShenZhongHuiCapability(GameEngine engine) : IShenZhongHuiHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginShenZh(f, op); }
    private CardActionContext? DuzhangAction(ProgramSkillWindowContext? c) => c is null ? null : _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId)?.Action;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.Collection, 39300, SkillProgramEffectOp.ResolveLinjieRound, SkillProgramEffectOp.AwakenJianghuo)]
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 39300, SkillProgramEffectOp.ResolveLinjieRound, SkillProgramEffectOp.ResolveLinjieDamage, SkillProgramEffectOp.ResolveDuzhang, SkillProgramEffectOp.AwakenJianghuo, SkillProgramEffectOp.ResolveLishiPhase)]
    private bool CanTriggerShenZh(ProgramTriggerLegalityContext gate)
    {
        var t = GetProgramTrigger(gate.Candidate); var c = gate.WindowContext;
        return t.Effects[0].Op switch
        {
            SkillProgramEffectOp.ResolveLinjieRound => _players.Any(p => p.IsAlive && LinCold(p.Seat) == 0),
            SkillProgramEffectOp.ResolveLinjieDamage => c is { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0, TargetSeat: { } seat } && seat != gate.OwnerSeat && _players[seat].IsAlive && LinCold(seat) > 0 && LinjieDiscardCards(gate.OwnerSeat, seat).Count > 0,
            SkillProgramEffectOp.ResolveDuzhang => DuzhangAction(c) is { EffectiveIsRed: false, TargetSeats: [var target] } a && (a.ActorSeat == gate.OwnerSeat || target == gate.OwnerSeat),
            SkillProgramEffectOp.AwakenJianghuo => HasShenGuoJiaAwakeningCondition(gate.OwnerSeat, gate.SkillId, gate.Candidate.BindingId) || _players.Where(p => p.IsAlive).All(p => ProgramEventHistory<DamageAppliedEvent>().Any(d => d.TargetSeat == p.Seat && d.Amount > 0)),
            SkillProgramEffectOp.ResolveLishiPhase => CurrentActualDiscardRecoveryPhase() is { } phase && phase.ActorSeat == gate.OwnerSeat && LishiActiveEffects(phase).Any(e => e.Option == (c?.Window == SkillProgramTriggerWindow.JudgmentPhaseStarting ? 2 : 3)),
            _ => true
        };
    }
    private IReadOnlyList<Card> LinjieDiscardCards(int owner, int target) => Array.AsReadOnly(GetHand(_players[target]).Where(card =>
        !IsForeignEquipmentDiscardPrevented(owner, card, CardLocation.Hand(target), OwnedCardMoveIntent.Discard)).ToArray());
    private SkillProgramStepOutcome BeginShenZh(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.ShenZhongHui is not null || f.InstructionIndex != 1 || f.WindowContext is not { } w) throw new InvalidOperationException("Shen Zhong Hui lost his original unpaid native binding.");
        var target = op == SkillProgramEffectOp.ResolveLinjieDamage ? w.TargetSeat!.Value : f.OwnerSeat;
        var r = new ShenZhongHuiReceipt(ShenZhSource(f), f.GameplayHash, w.ParentFrameId, op, ShenZhongHuiStage.Complete, target);
        r = r with { Stage = op switch
        {
            SkillProgramEffectOp.ResolveLinjieRound => ShenZhongHuiStage.RoundChoice,
            SkillProgramEffectOp.ResolveLinjieDamage => ShenZhongHuiStage.DamageChildren,
            SkillProgramEffectOp.ResolveDuzhang => ShenZhongHuiStage.DrawAttempt,
            SkillProgramEffectOp.AwakenJianghuo => ShenZhongHuiStage.AwakenFinish,
            SkillProgramEffectOp.ChooseLishiPunishments => LinCold(f.OwnerSeat) > 0 ? ShenZhongHuiStage.LishiChoice : ShenZhongHuiStage.Complete,
            _ => w.Window == SkillProgramTriggerWindow.JudgmentPhaseStarting ? ShenZhongHuiStage.PhaseChoice : ShenZhongHuiStage.Complete
        }};
        if (op == SkillProgramEffectOp.ResolveDuzhang) r = r with { Count = 1, ActionId = DuzhangAction(w)!.ActionId };
        if (op == SkillProgramEffectOp.ResolveLishiPhase) r = r with { Phase = CurrentActualDiscardRecoveryPhase() ?? throw new InvalidOperationException("Lishi lost its actual phase token.") };
        ReplaceRuntimeTop(f = f with { ShenZhongHui = r });
        AdvanceEventRulesAndQueueFact(new ShenZhongHuiStartedEvent(f.Id, r.Source, r.GameplayHash, op, r.ParentId, target));
        if (op == SkillProgramEffectOp.ResolveLinjieDamage)
        {
            var choices = LinjieDiscardCards(f.OwnerSeat, target);
            if (choices.Count == 0) { FinishShenZh(f); return SkillProgramStepOutcome.AwaitChild; }
            var card = choices[_random.Next(choices.Count)]; var chosen = card.Id;
            var before = _movementSequence; var last = GetHand(_players[target]).Count == 1;
            ReplaceRuntimeTop(f = f with { ShenZhongHui = r = r with { LastHandDiscarded = last, Before = before, After = before }, PendingMovementContinuation = new(target, 0, null) });
            MoveLogicalDiscard(f.OwnerSeat, target, card, CardLocation.Hand(target), new($"skill-program.{ShenZhongHuiComposition.SameEdition(f.SkillId, ShenZhongHuiComposition.Linjie)}.random-discard"));
            var paid = _cardMovements.Any(m => m.Sequence > before && m.CardId == chosen && m.From == CardLocation.Hand(target) && m.To == CardLocation.DiscardPile);
            last &= paid && GetHand(_players[target]).Count == 0;
            ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { ShenZhongHui = r with { LastHandDiscarded = last, After = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new LinjieRandomDiscardedEvent(f.Id, target, chosen, last, before, _movementSequence));
        }
        else if (op == SkillProgramEffectOp.ResolveDuzhang) { BeginOperationProgramDraw(f, f.OwnerSeat, 1, new($"skill-program.{f.SkillId}.draw")); return SkillProgramStepOutcome.AwaitChild; }
        else if (op == SkillProgramEffectOp.AwakenJianghuo)
        {
            var count = _players.Sum(p => LinCold(p.Seat));
            foreach (var p in _players) if (LinCold(p.Seat) is > 0 and var marks) MutateParticipantMarker(f, p.Seat, PlayerMarkerKind.LinCold, -marks);
            if (count > 0) MutateParticipantMarker(f, f.OwnerSeat, PlayerMarkerKind.LinCold, count);
            ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { ShenZhongHui = r with { Count = count, Stage = count > 0 ? ShenZhongHuiStage.DrawAttempt : ShenZhongHuiStage.AwakenFinish } });
            if (count > 0) { BeginOperationProgramDraw(f, f.OwnerSeat, count, new($"skill-program.{f.SkillId}.draw")); return SkillProgramStepOutcome.AwaitChild; }
        }
        else if (op == SkillProgramEffectOp.ChooseLishiPunishments && LinCold(f.OwnerSeat) == 0)
        { BeginProgramSkillDamage(f, f.OwnerSeat, 1, nature: DamageNature.Thunder, sourceLess: !ShenZhongHuiComposition.IsHappy(f.SkillId)); return SkillProgramStepOutcome.AwaitChild; }
        else if (op == SkillProgramEffectOp.ResolveLishiPhase && w.Window == SkillProgramTriggerWindow.DrawPhaseEnded) SettleLishiDrawPhase(f);
        ContinueShenZh(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private bool DrainShenZh(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 39300)] private bool ResumeShenZh(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenZhongHui: not null } f || f.Id != id) return false; ContinueShenZh(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 39300)] private bool ReturnShenZhMovement(ProgramSkillFrame f) => f.ShenZhongHui is not null && ResumeShenZh(f.Id);
    private void ContinueShenZh(long id)
    {
        while (_pendingDecision is null && _resolutionStack.LastOrDefault() is ProgramSkillFrame { ShenZhongHui: { } r } f && f.Id == id)
        {
            RequireShenZh(f);
            if (r.Stage is ShenZhongHuiStage.DrawAttempt or ShenZhongHuiStage.PhaseJudging) return;
            if (DrainShenZh(id)) return;
            f = GetActiveProgramFrame(id); r = f.ShenZhongHui!;
            if (f.PendingMovementContinuation is not null) ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
            if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive) { FinishShenZh(f); return; }
            if (r.Stage is ShenZhongHuiStage.RoundChoice or ShenZhongHuiStage.LishiChoice or ShenZhongHuiStage.PhaseChoice) { PublishShenZh(f); return; }
            if (r.Stage == ShenZhongHuiStage.RoundMark)
            { if (_players[r.TargetSeat].IsAlive) MutateParticipantMarker(f, r.TargetSeat, PlayerMarkerKind.LinCold, 1); FinishShenZh(GetActiveProgramFrame(id)); return; }
            if (r.Stage == ShenZhongHuiStage.DamageChildren)
            {
                if (r.LastHandDiscarded && _players[r.TargetSeat].IsAlive)
                { ReplaceRuntimeTop(f = f with { ShenZhongHui = r with { Stage = ShenZhongHuiStage.DamageFollowup } }); BeginProgramSkillDamage(f, r.TargetSeat, 1); return; }
                FinishShenZh(f); return;
            }
            if (r.Stage == ShenZhongHuiStage.DamageFollowup)
            { if (LinCold(r.TargetSeat) > 0) MutateParticipantMarker(f, r.TargetSeat, PlayerMarkerKind.LinCold, -1); FinishShenZh(GetActiveProgramFrame(id)); return; }
            if (r.Stage == ShenZhongHuiStage.DrawChildren)
            {
                if (r.Operation == SkillProgramEffectOp.ResolveDuzhang)
                { MutateParticipantMarker(f, f.OwnerSeat, PlayerMarkerKind.LinCold, 1); FinishShenZh(GetActiveProgramFrame(id)); return; }
                ReplaceRuntimeTop(f = f with { ShenZhongHui = r = r with { Stage = ShenZhongHuiStage.AwakenFinish } });
            }
            if (r.Stage == ShenZhongHuiStage.AwakenFinish)
            {
                ReplaceRuntimeTop(f = f with { ShenZhongHui = r with { Stage = ShenZhongHuiStage.Complete } });
                var linjie = ShenZhongHuiComposition.SameEdition(f.SkillId, ShenZhongHuiComposition.Linjie);
                var lishi = ShenZhongHuiComposition.SameEdition(f.SkillId, ShenZhongHuiComposition.Lishi);
                ChangeProgramMaximumHp(f, 1); GrantProgramSkills(f, [lishi]);
                var owner = _players[f.OwnerSeat];
                foreach (var grant in owner.SkillGrants.Grants.Where(g => g.IsEnabled && g.SkillId == linjie).ToArray()) owner.SkillGrants.RemoveGrant(grant.GrantId);
                AdvanceEventRulesAndQueueFact(new ProgramOwnerSkillsReplacedEvent(f.Id, f.SkillId, f.OwnerSeat, Array.AsReadOnly(new[] { linjie }), lishi));
                continue;
            }
            if (r.Stage is ShenZhongHuiStage.PhaseResult or ShenZhongHuiStage.PhaseCleanup or ShenZhongHuiStage.PhaseNext) { ContinueLishiJudgments(f); return; }
            FinishShenZh(f); return;
        }
    }
    private void FinishShenZh(ProgramSkillFrame f) { ReplaceRuntimeTop(f with { ShenZhongHui = null, PendingMovementContinuation = null }); AdvanceRuntimeProgram(f.Id); }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveDuzhang, SkillProgramEffectOp.AwakenJianghuo)]
    private void ReturnShenZhDraw(ProgramSkillFrame supplied, NativeDrawOutcome o)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.ShenZhongHui ?? throw new InvalidOperationException("Shen Zhong Hui's native draw lost its original receipt.");
        if (r.Stage != ShenZhongHuiStage.DrawAttempt || o.RecipientSeat != f.OwnerSeat || o.RequestedCount != r.Count || o.SequenceAfter != _movementSequence || ProgramEventHistory<ShenZhongHuiDrawReturnedEvent>().Any(e => e.FrameId == f.Id)) throw new InvalidOperationException("Shen Zhong Hui's draw return cannot change its paid count.");
        ReplaceRuntimeTop(f with { ShenZhongHui = r with { Stage = ShenZhongHuiStage.DrawChildren, Before = o.SequenceBefore, After = o.SequenceAfter } });
        AdvanceEventRulesAndQueueFact(new ShenZhongHuiDrawReturnedEvent(f.Id, f.OwnerSeat, o.RequestedCount, o.ActualDrawIds.Count, o.SequenceBefore, o.SequenceAfter)); ContinueShenZh(f.Id);
    }
    private IReadOnlyList<PromptChoice> ShenZhChoices(ProgramSkillFrame f)
    {
        var r = f.ShenZhongHui!;
        PromptChoice C(string value, string text, IReadOnlyList<int>? targets = null) => new(new($"shen-zh.{f.Id}.{r.Stage}.{value}"), text, [], targets ?? [], new Dictionary<string, string> { ["program-action"] = "shen-zh-choice", ["value"] = value });
        if (r.Stage == ShenZhongHuiStage.RoundChoice) return Array.AsReadOnly(_players.Where(p => p.IsAlive && LinCold(p.Seat) == 0).OrderBy(p => RelativeTableOrder(_currentSeat, p.Seat)).Select(p => C(p.Seat.ToString(CultureInfo.InvariantCulture), $"对{p.Name}造成1点伤害，然后令其获得凛", [p.Seat])).ToArray());
        if (r.Stage == ShenZhongHuiStage.LishiChoice) return Array.AsReadOnly(Enumerable.Range(0, 32).Where(mask => System.Numerics.BitOperations.PopCount((uint)mask) <= LinCold(f.OwnerSeat)).Select(mask => C(mask.ToString(CultureInfo.InvariantCulture), mask == 0 ? "不移去凛" : "移去" + System.Numerics.BitOperations.PopCount((uint)mask) + "个凛，执行第" + string.Join("、", Enumerable.Range(1, 5).Where(n => (mask & (1 << (n - 1))) != 0)) + "项")).ToArray());
        if (r.Stage == ShenZhongHuiStage.PhaseChoice) return Array.AsReadOnly(new[] { CardKind.Lightning, CardKind.Indulgence, CardKind.SupplyShortage }.Where(k => !r.Judgments.Contains(k)).Select(k => C(((int)k).ToString(CultureInfo.InvariantCulture), "选择【" + CardCatalog.Get(k).DisplayName + "】判定")).ToArray());
        return [];
    }
    private void PublishShenZh(ProgramSkillFrame f)
    {
        var choices = ShenZhChoices(f); if (choices.Count == 0) { FinishShenZh(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, skill.Name + "：选择结算。", [], choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = false, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("shen-zh-choice")] private void AnswerShenZh(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Shen Zhong Hui lost his original choice owner."); RequireShenZh(f);
        if (!ShenZhChoices(f).Any(c => c.Id == choice.Id && c.Targets.SequenceEqual(choice.Targets) && c.Cards.SequenceEqual(choice.Cards) && c.Parameters.OrderBy(x => x.Key).SequenceEqual(choice.Parameters.OrderBy(x => x.Key)))) throw new InvalidOperationException("Shen Zhong Hui rejects a stale or forged original choice.");
        var r = f.ShenZhongHui!; var value = int.Parse(choice.Parameters["value"], CultureInfo.InvariantCulture); ClearPendingDecision();
        if (r.Stage == ShenZhongHuiStage.RoundChoice)
        { ReplaceRuntimeTop(f = f with { ShenZhongHui = r with { TargetSeat = value, Stage = ShenZhongHuiStage.RoundMark } }); AdvanceEventRulesAndQueueFact(new LinjieTargetSelectedEvent(f.Id, value)); BeginProgramSkillDamage(f, value, 1); return; }
        if (r.Stage == ShenZhongHuiStage.LishiChoice)
        {
            var count = System.Numerics.BitOperations.PopCount((uint)value);
            ReplaceRuntimeTop(f = f with { ShenZhongHui = r with { Mask = value, Count = count, Stage = ShenZhongHuiStage.LishiIssue } });
            if (count > 0) MutateParticipantMarker(f, f.OwnerSeat, PlayerMarkerKind.LinCold, -count);
            var token = CurrentActualDiscardRecoveryPhase()?.Token ?? 0;
            foreach (var p in _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat).OrderBy(p => RelativeTableOrder(_currentSeat, p.Seat)))
            foreach (var option in Enumerable.Range(1, 5).Where(n => (value & (1 << (n - 1))) != 0))
            {
                AdvanceEventRulesAndQueueFact(new LishiPunishmentIssuedEvent(f.Id, r.Source, r.GameplayHash, p.Seat, option, token));
                AcquireRuntimeSkills(p, f.SkillId, [ShenZhongHuiComposition.SameEdition(f.SkillId, ShenZhongHuiComposition.Curse)]);
            }
        }
        else if (r.Stage == ShenZhongHuiStage.PhaseChoice)
        {
            var judgments = r.Judgments.Append((CardKind)value).ToArray();
            ReplaceRuntimeTop(f = f with { ShenZhongHui = r with { Judgments = judgments } });
            if (judgments.Length == 2) { BeginLishiJudgment(f); return; }
        }
        ContinueShenZh(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveLinjieRound, SkillProgramEffectOp.ChooseLishiPunishments, SkillProgramEffectOp.ResolveLishiPhase)]
    private PromptChoice ChooseShenZhAi(PendingDecision prompt, ProgramSkillFrame f) => f.ShenZhongHui?.Stage == ShenZhongHuiStage.LishiChoice
        ? prompt.Choices.OrderByDescending(c => System.Numerics.BitOperations.PopCount((uint)int.Parse(c.Parameters["value"], CultureInfo.InvariantCulture))).First()
        : prompt.Choices.FirstOrDefault(c => c.Targets.Count == 1 && c.Targets[0] != f.OwnerSeat) ?? prompt.Choices[0];
    private bool ShenZhWindowMatches(ProgramSkillFrame f)
    {
        if (f.WindowContext is not { } w) return false;
        bool Matches(ProgramTriggerCandidate c) => c.OwnerSeat == f.OwnerSeat && c.SkillId == f.SkillId && c.BindingId == f.TriggerId && c.SkillInstanceId == f.SkillInstanceId && c.GameplayHash == f.GameplayHash;
        return _resolutionStack.SingleOrDefault(x => x.Id == w.ParentFrameId) switch
        {
            ProgramLifecycleTriggerWindowFrame l => l.Window == w.Window && l.CandidateIndex >= 0 && l.CandidateIndex < l.Candidates.Count && Matches(l.Candidates[l.CandidateIndex]),
            TurnEndingBoundaryFrame e => w.Window == SkillProgramTriggerWindow.TurnEnding && e.OwnerSeat == f.OwnerSeat && e.OwnerSeat == _currentSeat && e.TurnNumber == _turnNumber &&
                w.OwnerSeat == f.OwnerSeat && w.SourceSeat == e.OwnerSeat && w.TargetSeat == e.OwnerSeat && e.ItemIndex >= 0 && e.ItemIndex < e.Items.Count &&
                e.Items[e.ItemIndex] is { Kind: TurnEndingBoundaryItemKind.Program, Candidate: { } endingCandidate } item && Matches(endingCandidate) &&
                w.OccurrenceIndex == endingCandidate.OccurrenceIndex && w.Facts == (item.Facts ?? e.Facts),
            DamageTriggerWindowFrame d => d.TriggerWindow == w.Window && d.TargetSeat == w.TargetSeat && d.CandidateIndex >= 0 && d.CandidateIndex < d.Candidates.Count && Matches(d.Candidates[d.CandidateIndex].ToProgramCandidate()),
            ProgramCardTriggerWindowFrame c => c.CandidateIndex >= 0 && c.CandidateIndex < c.Candidates.Count && c.Candidates[c.CandidateIndex] is { } b && b.OwnerSeat == f.OwnerSeat && b.SkillId == f.SkillId && b.TriggerId == f.TriggerId && b.SkillInstanceId == f.SkillInstanceId && b.GameplayHash == f.GameplayHash && w.CardUse?.CardActionId == c.Action.ActionId,
            _ => false
        };
    }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 39300)] private bool ShenZhMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p)
    {
        if (f.ShenZhongHui is not { } r || effect?.Op != r.Operation || p != f.PendingMovementContinuation || p.SubjectSeat != r.TargetSeat || p.BeforeCount != 0 || p.CoverageResultBind is not null || r.After <= r.Before || r.After > _movementSequence || !ShenZhWindowMatches(f)) return false;
        var moves = _cardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= r.After).ToArray();
        if (r.Operation == SkillProgramEffectOp.ResolveLinjieDamage && r.Stage == ShenZhongHuiStage.DamageChildren)
        {
            var paid = ProgramEventHistory<LinjieRandomDiscardedEvent>().Where(e => e.FrameId == f.Id).ToArray();
            return paid is [var e] && e.TargetSeat == r.TargetSeat && e.Before == r.Before && e.After == r.After && e.LastHand == r.LastHandDiscarded &&
                moves.Count(m => m.CardId == e.CardId && m.From == CardLocation.Hand(r.TargetSeat) && m.Reason.Value == $"skill-program.{ShenZhongHuiComposition.SameEdition(f.SkillId, ShenZhongHuiComposition.Linjie)}.random-discard" &&
                    (m.To == CardLocation.DiscardPile || IsLishiPaidDiscardMovement(m, CardLocation.Hand(r.TargetSeat), m.Reason))) == 1;
        }
        if (r.Operation != SkillProgramEffectOp.ResolveLishiPhase || r.Phase is not { } phase || !IsRecordedActualDiscardRecoveryPhase(phase)) return false;
        if (r.Stage == ShenZhongHuiStage.PhaseCleanup && r.ResultId is { } id)
            return ProgramEventHistory<LishiJudgmentReturnedEvent>().Count(e => e.FrameId == f.Id && e.JudgmentId == r.JudgmentId && e.Index == r.JudgmentIndex && e.CardId == id) == 1 &&
                moves.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.anniversary:lishi.judgment-finish") == 1;
        if (r.Stage != ShenZhongHuiStage.Complete) return false;
        var settled = ProgramEventHistory<LishiDrawPhaseSettledEvent>().Where(e => e.FrameId == f.Id && e.Phase == phase).ToArray();
        var cards = ProgramEventHistory<LishiDrawDiscardedEvent>().Where(e => e.FrameId == f.Id && e.Phase == phase).ToArray();
        return settled is [{ DiscardedCount: > 0 } receipt] && cards.Length == receipt.DiscardedCount && cards.Select(e => e.CardId).Distinct().Count() == cards.Length &&
            cards.All(e => moves.Count(m => m.Sequence == e.MovementSequence && m.CardId == e.CardId && m.From == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == "skill-program.anniversary:lishi.draw-discard" &&
                (m.To == CardLocation.DiscardPile || IsLishiPaidDiscardMovement(m, CardLocation.Hand(f.OwnerSeat), m.Reason))) == 1);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 39300)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 39300)] private bool RetainShenZh(ProgramSkillFrame f) => f.ShenZhongHui is not null;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 39300)] private void RequireShenZh(ProgramSkillFrame f)
    {
        if (f.ShenZhongHui is not { } r) return;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (r.Source != ShenZhSource(f) || r.GameplayHash != f.GameplayHash || r.ParentId != f.WindowContext?.ParentFrameId || index < 1 || _resolutionStack[index - 1].Id != r.ParentId || !ShenZhWindowMatches(f) ||
            !Enum.IsDefined(r.Stage) || !IsValidPlayerSeat(r.TargetSeat) || r.Count < 0 || r.Mask is < 0 or > 31 || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            r.Judgments is not System.Collections.IList { IsReadOnly: true } || r.Judgments.Count > 2 || r.Judgments.Distinct().Count() != r.Judgments.Count || r.JudgmentIndex < 0 || r.JudgmentIndex > r.Judgments.Count ||
            ProgramEventHistory<ShenZhongHuiStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.ParentId == r.ParentId && e.Operation == r.Operation) != 1)
            throw new InvalidOperationException("Shen Zhong Hui's original-frame dynamic receipt is inconsistent.");
        if (r.Operation == SkillProgramEffectOp.ResolveLinjieRound && r.Stage != ShenZhongHuiStage.RoundChoice && ProgramEventHistory<LinjieTargetSelectedEvent>().Count(e => e.FrameId == f.Id && e.TargetSeat == r.TargetSeat) != 1)
            throw new InvalidOperationException("Linjie lost its actually selected unmarked damage target.");
        if (r.Stage is ShenZhongHuiStage.DrawChildren or ShenZhongHuiStage.DrawAttempt && r.Count < 1)
            throw new InvalidOperationException("Shen Zhong Hui's draw must retain its positive frozen native entitlement.");
    }
}
