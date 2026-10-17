using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ZhaoZhongAction = "zhao-zhong";
    private const string DiciGiveReason = "skill-program.zhao-zhong.dici.give";
    private const string DiciNextGiveReason = "skill-program.zhao-zhong.dici.next-give";
    private const string BiriDrawReason = "skill-program.zhao-zhong.biri.draw";
    [ProgramCapability] private sealed class ZhaoZhongCapability(GameEngine engine) : IZhaoZhongHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginZhaoZhong(f, op); }
    private SkillProgramStepOutcome BeginZhaoZhong(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.ZhaoZhong is not null) { ContinueZhaoZhong(f.Id); return SkillProgramStepOutcome.AwaitChoice; }
        var c = f.WindowContext; var stage = op == SkillProgramEffectOp.ResolvePengbiFirstActualTurn ? ZhaoZhongStage.PengbiSkill : op == SkillProgramEffectOp.ResolveDiciGiftRecoveryAndNeighborChain ? ZhaoZhongStage.InitialGive : op == SkillProgramEffectOp.ResolveIssuedDiciNextChainChoice ? ZhaoZhongStage.NextChoice : ZhaoZhongStage.Drawing;
        var r = new ZhaoZhongReceipt(ZhaoZhongSource(f), f.GameplayHash, op, c?.ParentFrameId ?? 0, _turnNumber, _turnProgression.OwnerSeat, stage);
        if (op == SkillProgramEffectOp.ResolveDiciGiftRecoveryAndNeighborChain)
        {
            if (f.SelectedTargetSeats is not [var target] || target == f.OwnerSeat || !_players[target].IsAlive || GetHand(_players[target]).Count == 0 || CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || phase.ActorSeat != f.OwnerSeat || ProgramEventHistory<DiciAcceptedEvent>().Any(e => e.Source.OwnerSeat == f.OwnerSeat && e.Phase == phase)) throw new InvalidOperationException("Dici requires a living other true Hand donor and an unspent actual Play phase.");
            r = r with { TargetSeat = target, Phase = phase };
        }
        else if (op == SkillProgramEffectOp.ResolveIssuedDiciNextChainChoice)
        {
            var candidate = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, c!.OccurrenceIndex);
            if (!IsIssuedDiciChainCandidate(candidate, c) || DiciCandidateLease(candidate) is not { } lease) throw new InvalidOperationException("Dici's next-chain choice lost its independent issued lease.");
            r = r with { Paid = true, TargetSeat = lease.TargetSeat, LeaseId = lease.LeaseId };
        }
        else if (op == SkillProgramEffectOp.ResolveBiriFirstNonDrawGain)
        {
            var candidate = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, c!.OccurrenceIndex);
            if (!IsIssuedBiriCandidate(candidate, c) || c.MovementBatch is not { } batch || ProgramEventHistory<BiriPotentialGainEvent>().FirstOrDefault(e => e.BatchId == batch.Id) is not { } potential || FirstBiriGain(potential.ActualTurn, potential.TurnOwner) is not { } first) throw new InvalidOperationException("Biri lost the first actual non-draw acquisition of this actual turn.");
            r = r with { Paid = true, ActualTurn = first.ActualTurn, TurnOwner = first.TurnOwner, Before = _movementSequence, After = _movementSequence };
        }
        ReplaceRuntimeTop(f = f with { ZhaoZhong = r });
        AdvanceEventRulesAndQueueFact(new ZhaoZhongStartedEvent(f.Id, r.Source, r.GameplayHash, op, r.WindowId, r.ActualTurn, r.TurnOwner, r.TargetSeat));
        if (op == SkillProgramEffectOp.ResolveIssuedDiciNextChainChoice) AdvanceEventRulesAndQueueFact(new DiciLeaseStartedEvent(r.LeaseId, r.WindowId, f.Id));
        if (op == SkillProgramEffectOp.ResolveBiriFirstNonDrawGain)
        {
            var potential = ProgramEventHistory<BiriPotentialGainEvent>().First(e => e.BatchId == r.WindowId); var first = FirstBiriGain(potential.ActualTurn, potential.TurnOwner)!;
            AdvanceEventRulesAndQueueFact(new BiriDrawIssuedEvent(f.Id, r.WindowId, first.Sequence, r.Source, r.GameplayHash, r.ActualTurn, r.TurnOwner));
        }
        ContinueZhaoZhong(f.Id); return SkillProgramStepOutcome.AwaitChoice;
    }
    private int ZhaoZhongChooser(ProgramSkillFrame f) => f.ZhaoZhong!.Stage is ZhaoZhongStage.InitialGive or ZhaoZhongStage.NextChoice ? f.ZhaoZhong.TargetSeat : f.OwnerSeat;
    private IReadOnlyList<PromptChoice> ZhaoZhongChoices(ProgramSkillFrame f)
    {
        var r = f.ZhaoZhong!; var result = new List<PromptChoice>();
        PromptChoice Choice(string branch, string label, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null, string? skill = null)
        {
            var p = new Dictionary<string, string> { ["program-action"] = ZhaoZhongAction, [ZhaoZhongAction] = branch, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) };
            if (skill is not null) p["skill-id"] = skill;
            return new(new($"zhao-zhong.{f.Id}.{r.Stage}.{branch}.{skill}.{string.Join('-', cards ?? [])}.{string.Join('-', targets ?? [])}"), label, cards ?? [], targets ?? [], p);
        }
        if (r.Stage == ZhaoZhongStage.PengbiSkill)
            foreach (var skill in new[] { "ol:yintian", "ol:biri" }) result.Add(Choice("pengbi-skill", "获得" + _contentRegistry.GetSkill(skill).Name, skill: skill));
        else if (r.Stage == ZhaoZhongStage.PengbiTarget)
        {
            var other = r.ChosenSkill == "ol:yintian" ? "ol:biri" : "ol:yintian";
            foreach (var target in _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat)) result.Add(Choice("pengbi-target", "令" + target.Name + "获得" + _contentRegistry.GetSkill(other).Name, targets: [target.Seat]));
            result.Add(Choice("pengbi-skip", "不令其他角色获得技能"));
        }
        else if (r.Stage is ZhaoZhongStage.InitialGive or ZhaoZhongStage.NextChoice)
        {
            if (_players[r.TargetSeat].IsAlive && _players[f.OwnerSeat].IsAlive)
                foreach (var card in GetHand(_players[r.TargetSeat])) result.Add(Choice(r.Stage == ZhaoZhongStage.InitialGive ? "initial-give" : "next-give", "交给" + _players[f.OwnerSeat].Name + "：" + card.DisplayName, [card.Id]));
            if (r.Stage == ZhaoZhongStage.NextChoice && _players[r.TargetSeat].IsAlive) result.Add(Choice("next-damage", "受到其造成的1点雷电伤害"));
        }
        return Array.AsReadOnly(result.Select(PromptChoiceProjection.Freeze).ToArray());
    }
    private void PublishZhaoZhong(ProgramSkillFrame f)
    {
        var r = f.ZhaoZhong!;
        if (!r.Paid && (!_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))) { FinishZhaoZhong(f); return; }
        var choices = ZhaoZhongChoices(f);
        if (choices.Count == 0) { FinishZhaoZhong(f); return; }
        var chooser = ZhaoZhongChooser(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, r.Stage == ZhaoZhongStage.PengbiSkill ? "朋比：选择自身获得的技能。" : r.Stage == ZhaoZhongStage.PengbiTarget ? "朋比：可令一名其他角色获得另一个技能。" : r.Stage == ZhaoZhongStage.InitialGive ? "抵慈：交给其一张手牌。" : "抵慈：交给其一张手牌，或受到其造成的1点雷电伤害。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { IsPrivate = true, PromptId = CreatePromptId(), Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("zhao-zhong")]
    private void AnswerZhaoZhong(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ZhaoZhong: { } r } f || _pendingDecision?.PlayerSeat != ZhaoZhongChooser(f) || !ZhaoZhongChoices(f).Any(c => AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Zhao Zhong requires the actual chooser's exact published private choice.");
        AssertZhaoZhong(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var branch = choice.Parameters[ZhaoZhongAction];
        if (branch == "pengbi-skill")
        {
            var skill = choice.Parameters["skill-id"];
            ReplaceRuntimeTop(f = f with { ZhaoZhong = r with { Paid = true, ChosenSkill = skill, Stage = ZhaoZhongStage.SelfGrantChildren } });
            AdvanceEventRulesAndQueueFact(new PengbiCommittedEvent(f.Id, r.Source, r.GameplayHash, r.ActualTurn, r.TurnOwner, skill));
            GrantPengbiSkill(f, f.OwnerSeat, skill); ContinueZhaoZhong(f.Id); return;
        }
        if (branch == "pengbi-skip") { AdvanceEventRulesAndQueueFact(new PengbiOtherChosenEvent(f.Id, null)); FinishZhaoZhong(f); return; }
        if (branch == "pengbi-target")
        {
            var target = choice.Targets.Single(); AdvanceEventRulesAndQueueFact(new PengbiOtherChosenEvent(f.Id, target));
            ReplaceRuntimeTop(f = f with { ZhaoZhong = r with { TargetSeat = target, Stage = ZhaoZhongStage.OtherGrantChildren } });
            GrantPengbiSkill(f, target, r.ChosenSkill == "ol:yintian" ? "ol:biri" : "ol:yintian"); ContinueZhaoZhong(f.Id); return;
        }
        if (branch is "initial-give" or "next-give") { PayDiciGift(f, choice.Cards.Single(), branch == "next-give"); return; }
        if (branch == "next-damage")
        {
            ReplaceRuntimeTop(f = f with { ZhaoZhong = r with { Stage = ZhaoZhongStage.NextDamageChildren } });
            AdvanceEventRulesAndQueueFact(new DiciNextChoiceEvent(f.Id, r.LeaseId, "damage")); AdvanceEventRulesAndQueueFact(new DiciThunderIssuedEvent(f.Id, r.LeaseId, f.OwnerSeat, r.TargetSeat));
            BeginProgramSkillDamage(f, r.TargetSeat, 1, nature: DamageNature.Thunder); return;
        }
        throw new InvalidOperationException("Unknown Zhao Zhong choice.");
    }
    private void GrantPengbiSkill(ProgramSkillFrame f, int recipient, string skill)
    {
        foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
        var sourceId = $"zhao-zhong.pengbi:{f.Id}:{recipient}"; var grantId = sourceId + ":" + skill;
        _players[recipient].SkillGrants.Grant(new(grantId, skill, grantId, sourceId));
        AdvanceEventRulesAndQueueFact(new PengbiSkillGrantedEvent(f.Id, recipient, skill, grantId, grantId, sourceId));
    }
    private void PayDiciGift(ProgramSkillFrame f, int cardId, bool next)
    {
        var r = f.ZhaoZhong!; var from = CardLocation.Hand(r.TargetSeat); var card = _cardZones.CardsAt(from).Single(c => c.Id == cardId);
        if (!_players[r.TargetSeat].IsAlive || !_players[f.OwnerSeat].IsAlive || r.Stage != (next ? ZhaoZhongStage.NextChoice : ZhaoZhongStage.InitialGive)) throw new InvalidOperationException("Dici cannot give another person's card or give to a dead recipient.");
        if (!next)
        {
            if (r.Paid || r.Phase is not { } phase || ProgramEventHistory<DiciAcceptedEvent>().Any(e => e.Source.OwnerSeat == f.OwnerSeat && e.Phase == phase)) throw new InvalidOperationException("Dici repeated its actual Play-phase payment.");
            AdvanceEventRulesAndQueueFact(new DiciAcceptedEvent(f.Id, r.Source, r.GameplayHash, r.TargetSeat, phase));
        }
        else AdvanceEventRulesAndQueueFact(new DiciNextChoiceEvent(f.Id, r.LeaseId, "give"));
        ReplaceRuntimeTop(f = f with { ZhaoZhong = r with { Paid = true, CardId = card.Id, CardKind = card.Kind, From = from, Before = _movementSequence, After = _movementSequence, Stage = next ? ZhaoZhongStage.NextGiftChildren : ZhaoZhongStage.GiftChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveCard(card, from, CardLocation.Hand(f.OwnerSeat), new(next ? DiciNextGiveReason : DiciGiveReason), m => AdvanceEventRulesAndQueueFact(new DiciGiftEntityEvent(f.Id, r.LeaseId, m.CardId, m.CardKind, m.From, m.To, m.Sequence)));
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { ZhaoZhong = f.ZhaoZhong! with { After = _movementSequence } }); ContinueZhaoZhong(f.Id);
    }
    private int[] DiciNeighbors(int target)
    {
        var living = _players.Where(p => p.IsAlive).Select(p => p.Seat).Order().ToArray(); var index = Array.IndexOf(living, target);
        return index < 0 || living.Length < 2 ? [] : new[] { living[(index + 1) % living.Length], living[(index + living.Length - 1) % living.Length] }.Distinct().ToArray();
    }
    private bool DrainZhaoZhong(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginAdvancedSkillsChanged(id) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id);
    private void ContinueZhaoZhong(long id)
    {
        while (_resolutionStack.LastOrDefault() is ProgramSkillFrame { ZhaoZhong: { } r } f && f.Id == id && _pendingDecision is null)
        {
            AssertZhaoZhong(f); if (f.NativeDraw is not null || f.AttackAttempt is not null) return;
            if (r.Stage is ZhaoZhongStage.SelfGrantChildren or ZhaoZhongStage.OtherGrantChildren or ZhaoZhongStage.GiftChildren or ZhaoZhongStage.RecoveryChildren or ZhaoZhongStage.UnlinkChildren or ZhaoZhongStage.NeighborChildren or ZhaoZhongStage.NextGiftChildren or ZhaoZhongStage.NextDamageChildren or ZhaoZhongStage.DrawChildren)
            { if (DrainZhaoZhong(id)) return; f = GetActiveProgramFrame(id); r = f.ZhaoZhong!; ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null }); }
            if (_winner != Winner.None) { FinishZhaoZhong(f); return; }
            if (r.Stage == ZhaoZhongStage.SelfGrantChildren)
            { if (!_players[f.OwnerSeat].IsAlive) { FinishZhaoZhong(f); return; } ReplaceRuntimeTop(f = f with { ZhaoZhong = r with { Stage = ZhaoZhongStage.PengbiTarget } }); PublishZhaoZhong(f); return; }
            if (r.Stage is ZhaoZhongStage.OtherGrantChildren or ZhaoZhongStage.NextGiftChildren or ZhaoZhongStage.NextDamageChildren or ZhaoZhongStage.DrawChildren) { FinishZhaoZhong(f); return; }
            if (r.Stage == ZhaoZhongStage.Drawing)
            { if (!_players[f.OwnerSeat].IsAlive) { FinishZhaoZhong(f); return; } BeginOperationProgramDraw(f, f.OwnerSeat, 1, new(BiriDrawReason)); return; }
            if (r.Paid && r.Op == SkillProgramEffectOp.ResolveDiciGiftRecoveryAndNeighborChain && !_players[r.TargetSeat].IsAlive) { FinishZhaoZhong(f); return; }
            if (r.Stage == ZhaoZhongStage.GiftChildren)
            {
                ReplaceRuntimeTop(f = f with { ZhaoZhong = r with { Stage = ZhaoZhongStage.RecoveryChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); AdvanceEventRulesAndQueueFact(new DiciRecoveryIssuedEvent(f.Id, r.TargetSeat));
                new ProgramSkillHost(this).Recover(f.Id, f.OwnerSeat, r.TargetSeat, 1, null, null); continue;
            }
            if (r.Stage == ZhaoZhongStage.RecoveryChildren)
            {
                ReplaceRuntimeTop(f = f with { ZhaoZhong = r with { Stage = ZhaoZhongStage.UnlinkChildren } }); AdvanceEventRulesAndQueueFact(new DiciUnlinkIssuedEvent(f.Id, r.TargetSeat)); SetProgramChainedState(f, false, r.TargetSeat); continue;
            }
            if (r.Stage == ZhaoZhongStage.UnlinkChildren)
            {
                var neighbors = DiciNeighbors(r.TargetSeat); ReplaceRuntimeTop(f = f with { ZhaoZhong = r with { Stage = ZhaoZhongStage.Neighbors, Neighbors = neighbors, Cursor = 0 } });
                AdvanceEventRulesAndQueueFact(new DiciNeighborsFrozenEvent(f.Id, neighbors.Length)); for (var i = 0; i < neighbors.Length; i++) AdvanceEventRulesAndQueueFact(new DiciNeighborFrozenEvent(f.Id, i, neighbors[i])); continue;
            }
            if (r.Stage == ZhaoZhongStage.NeighborChildren) { ReplaceRuntimeTop(f with { ZhaoZhong = r with { Stage = ZhaoZhongStage.Neighbors, Cursor = r.Cursor + 1 } }); continue; }
            if (r.Stage == ZhaoZhongStage.Neighbors)
            {
                if (r.Cursor < r.Neighbors.Count)
                {
                    var seat = r.Neighbors[r.Cursor]; ReplaceRuntimeTop(f = f with { ZhaoZhong = r with { Stage = ZhaoZhongStage.NeighborChildren } }); AdvanceEventRulesAndQueueFact(new DiciNeighborIssuedEvent(f.Id, r.Cursor, seat));
                    if (_players[seat].IsAlive) SetProgramChainedState(f, true, seat); continue;
                }
                var ordinal = ProgramEventHistory<DiciLeaseIssuedEvent>().Count(); AdvanceEventRulesAndQueueFact(new DiciLeaseIssuedEvent(f.Id, r.Source, r.GameplayHash, f.OwnerSeat, r.TargetSeat, ordinal, r.ActualTurn)); FinishZhaoZhong(f); return;
            }
            PublishZhaoZhong(f); return;
        }
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveBiriFirstNonDrawGain)]
    private void ReturnBiriDraw(ProgramSkillFrame supplied, NativeDrawOutcome outcome)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.ZhaoZhong!;
        if (!r.Paid || r.Stage != ZhaoZhongStage.Drawing || outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != 1) throw new InvalidOperationException("Biri returned to another issued native draw.");
        ReplaceRuntimeTop(f = f with { ZhaoZhong = r with { Stage = ZhaoZhongStage.DrawChildren, DrawOutcome = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new BiriDrawReturnedEvent(f.Id, 1, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId)); ContinueZhaoZhong(f.Id);
    }
    private void FinishZhaoZhong(ProgramSkillFrame f)
    { var r = f.ZhaoZhong!; ClearPendingDecision(); AdvanceEventRulesAndQueueFact(new ZhaoZhongCompletedEvent(f.Id, r.Op, r.Paid)); ReplaceRuntimeTop(f = f with { ZhaoZhong = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 28110)] private bool ResumeZhaoZhong(long id) { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ZhaoZhong: not null } f || f.Id != id || f.InstructionIndex == 0) return false; ContinueZhaoZhong(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 28110)] private bool ReturnZhaoZhongMovement(ProgramSkillFrame f) { if (f.ZhaoZhong is null) return false; ContinueZhaoZhong(f.Id); return true; }
    [ProgramAiChoice(SkillProgramEffectOp.ResolvePengbiFirstActualTurn, SkillProgramEffectOp.ResolveDiciGiftRecoveryAndNeighborChain, SkillProgramEffectOp.ResolveIssuedDiciNextChainChoice, SkillProgramEffectOp.ResolveBiriFirstNonDrawGain)]
    private PromptChoice ChooseZhaoZhongAi(PendingDecision prompt, ProgramSkillFrame f)
    {
        if (f.ZhaoZhong!.Stage == ZhaoZhongStage.PengbiTarget)
        {
            var view = CreateSnapshot(f.OwnerSeat); return prompt.Choices.OrderByDescending(c => c.Targets.Count == 1 ? _aiBrains[f.OwnerSeat].ScoreProgramTarget(view, c.Targets[0], new(1, 0, 0, 0, 0, 0, false, false)) : 0).ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
        }
        return prompt.Choices.First();
    }
}
