using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string AnniversaryMingReason = "skill-program.anniversary-sun-quan.ming";
    private const string AnniversaryMingDraw = "skill-program.anniversary-sun-quan.colors";
    private static CardConversionSource AnniversarySunQuanSource(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId ?? f.ActivationId, f.OwnerSeat, f.SkillInstanceId);
    private bool HasAnniversaryMing => _contentRegistry.Skills.ContainsKey(AnniversarySunQuanComposition.Cangming);
    private IEnumerable<AnniversaryMingPileCreatedEvent> AnniversaryMingPiles() => !HasAnniversaryMing ? [] :
        ProgramEventHistory<AnniversaryMingPileCreatedEvent>().Where(e => IsValidPlayerSeat(e.HolderSeat) && e.Location == new CardLocation(CardZoneKind.PrivatePersistentPile, e.HolderSeat, $"anniversary-ming:{e.FrameId}:{e.HolderSeat}") &&
            ProgramEventHistory<AnniversarySunQuanStartedEvent>().Any(s => s.FrameId == e.FrameId && s.Source == e.Source && s.GameplayHash == e.GameplayHash && s.Operation is SkillProgramEffectOp.ResolveAnniversarySunQuanCangmingSetup or SkillProgramEffectOp.ResolveAnniversarySunQuanJichao));
    private IReadOnlyList<Card> AnniversaryMingCards(int holder) => AnniversaryMingPiles().Where(p => p.HolderSeat == holder).SelectMany(p => _cardZones.CardsAt(p.Location)).ToArray();
    private int[] AnniversaryJichaoPeers(int owner) => CurrentTableOrder().Where(s => s != owner && _players[s].IsAlive && (GetHand(_players[s]).Count > 0 || GetEquipment(_players[s]).Count > 0)).ToArray();
    private int AnniversaryDamageTotal(int owner) => ProgramEventHistory<DamageAppliedEvent>().Where(e => !e.SourceLess && e.SourceSeat == owner && e.Amount > 0).Sum(e => e.Amount);
    private bool AnniversaryJichaoUnlocked(int owner, string instance)
    {
        var last = ProgramEventHistory<AnniversaryJichaoLockedEvent>().LastOrDefault(e => e.OwnerSeat == owner && e.SkillInstanceId == instance);
        return last is null || AnniversaryDamageTotal(owner) - last.DamageTotal >= 3;
    }
    [ProgramCapability] private sealed class AnniversarySunQuanCapability(GameEngine engine) : IAnniversarySunQuanHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp operation) => engine.BeginAnniversarySunQuan(f, operation); }
    [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart, 40500, SkillProgramEffectOp.ResolveAnniversarySunQuanJichao)]
    private bool CanActivateAnniversaryJichao(ProgramActivationLegalityContext q) => AnniversaryJichaoPeers(q.OwnerSeat).Length > 0 ||
        AnniversaryJichaoUnlocked(q.OwnerSeat, GetRuntimeSkillInstanceId(q.Owner, q.SkillId)) && _players.Any(p => p.IsAlive && p.Seat != q.OwnerSeat && GetJudgment(p).Count > 0);
    private SkillProgramStepOutcome BeginAnniversarySunQuan(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.AnniversarySunQuan is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0)
            throw new InvalidOperationException("Anniversary Sun Quan requires the original single unpaid instruction.");
        if (operation == SkillProgramEffectOp.ResolveAnniversarySunQuanCangmingReturn &&
            (f.WindowContext is not { } context || !IsIssuedAnniversaryMingReturn(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority), context)))
            throw new InvalidOperationException("Cangming return requires its exact living holder and original native window.");
        var r = new AnniversarySunQuanReceipt(AnniversarySunQuanSource(f), f.GameplayHash, operation, f.WindowContext?.ParentFrameId ?? 0)
        { Stage = operation == SkillProgramEffectOp.ResolveAnniversarySunQuanJichao ? AnniversarySunQuanStage.ChoosingBranch : AnniversarySunQuanStage.MovementChildren, Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { AnniversarySunQuan = r });
        AdvanceEventRulesAndQueueFact(new AnniversarySunQuanStartedEvent(f.Id, r.Source, r.GameplayHash, operation, r.ParentId));
        if (operation == SkillProgramEffectOp.ResolveAnniversarySunQuanCangmingSetup)
            MoveAnniversaryMing(f, _players.Where(p => p.IsAlive).SelectMany(GetHand).Select(c => c.Id).ToArray(), true);
        else if (operation == SkillProgramEffectOp.ResolveAnniversarySunQuanCangmingReturn)
            MoveAnniversaryMing(f, AnniversaryMingCards(f.OwnerSeat).Select(c => c.Id).ToArray(), false);
        else if (operation == SkillProgramEffectOp.ResolveAnniversarySunQuanCangmingColors)
        {
            var count = AnniversaryMingBatchColors(f.WindowContext!.MovementBatch!);
            ReplaceRuntimeTop(f = f with { AnniversarySunQuan = r with { ColorCount = count, Stage = AnniversarySunQuanStage.Drawing } });
            BeginOperationProgramDraw(f, f.OwnerSeat, count, new(AnniversaryMingDraw));
        }
        else PublishAnniversarySunQuan(f);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private void MoveAnniversaryMing(ProgramSkillFrame f, IReadOnlyList<int> cards, bool deposit)
    {
        if (cards.Count == 0) { FinishAnniversarySunQuan(f); return; }
        var locations = new Dictionary<int, CardLocation>();
        if (deposit)
            foreach (var holder in cards.Select(id => _cardZones.GetLocation(id).OwnerSeat!.Value).Distinct())
            {
                var location = new CardLocation(CardZoneKind.PrivatePersistentPile, holder, $"anniversary-ming:{f.Id}:{holder}");
                _cardZones.EnsurePrivatePersistentPile(location); locations.Add(holder, location);
                AdvanceEventRulesAndQueueFact(new AnniversaryMingPileCreatedEvent(f.Id, AnniversarySunQuanSource(f), f.GameplayHash, holder, location));
            }
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { AnniversarySunQuan = f.AnniversarySunQuan! with { Stage = AnniversarySunQuanStage.MovementChildren, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramCardsFromMultipleSources(cards, CardLocation.Hand(f.OwnerSeat), new(AnniversaryMingReason), (batch, movements) =>
        {
            var current = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(current with { AnniversarySunQuan = current.AnniversarySunQuan! with { BatchId = batch, After = _movementSequence, Movements = movements } });
            AdvanceEventRulesAndQueueFact(new AnniversarySunQuanMovementPaidEvent(f.Id, batch, movements.Count, before, _movementSequence));
        }, deposit ? (_, from) => locations[from.OwnerSeat!.Value] : null, CardMovementDisclosure.FaceDown);
        ResumeAnniversarySunQuan(f.Id);
    }
    private IReadOnlyList<PromptChoice> AnniversarySunQuanChoices(ProgramSkillFrame f)
    {
        var r = f.AnniversarySunQuan!;
        if (!_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) return [];
        PromptChoice Choice(int branch, int? target, string label) => new(new($"anniversary-surge.{f.Id}.{branch}.{target ?? -1}"), label, [], target is { } s ? [s] : [],
            new Dictionary<string, string> { ["program-action"] = "anniversary-surge", ["branch"] = branch.ToString(CultureInfo.InvariantCulture) });
        if (r.Stage == AnniversarySunQuanStage.ChoosingBranch)
        {
            var choices = new List<PromptChoice>();
            if (AnniversaryJichaoPeers(f.OwnerSeat).Length > 0) choices.Add(Choice(1, null, "令一名其他角色随机将半数手牌及装备牌置入溟"));
            if (AnniversaryJichaoUnlocked(f.OwnerSeat, f.SkillInstanceId) && _players.Any(p => p.IsAlive && p.Seat != f.OwnerSeat && GetHand(p).Count + GetEquipment(p).Count + GetJudgment(p).Count > 0))
                choices.Add(Choice(2, null, "令所有其他角色将所有牌置入溟，然后此项失效直到你累计造成三点伤害"));
            return choices;
        }
        return r.Stage != AnniversarySunQuanStage.ChoosingTarget ? [] : AnniversaryJichaoPeers(f.OwnerSeat).Select(s => Choice(1, s, $"激潮：选择{_players[s].Name}")).ToArray();
    }
    private void PublishAnniversarySunQuan(ProgramSkillFrame f)
    {
        var choices = AnniversarySunQuanChoices(f);
        if (choices.Count == 0) { FinishAnniversarySunQuan(f); return; }
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "激潮：选择一项。", [], choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("anniversary-surge")] private void AnswerAnniversarySunQuan(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversarySunQuan: { } r } f || !AnniversarySunQuanChoices(f).Any(c => AssistedChoicesEqual([c], [selected])))
            throw new InvalidOperationException("Jichao requires its exact current branch or other living participant.");
        RequireAnniversarySunQuan(f);
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var branch = int.Parse(selected.Parameters["branch"], CultureInfo.InvariantCulture);
        if (r.Stage == AnniversarySunQuanStage.ChoosingBranch && branch == 1)
        { ReplaceRuntimeTop(f = f with { AnniversarySunQuan = r with { Stage = AnniversarySunQuanStage.ChoosingTarget, Branch = 1 } }); PublishAnniversarySunQuan(f); return; }
        var target = selected.Targets.Count == 1 ? selected.Targets[0] : -1;
        ReplaceRuntimeTop(f = f with { AnniversarySunQuan = r with { Branch = branch, Target = target } });
        AdvanceEventRulesAndQueueFact(new AnniversarySunQuanBranchChosenEvent(f.Id, branch, target));
        int[] ids;
        if (branch == 2)
        {
            AdvanceEventRulesAndQueueFact(new AnniversaryJichaoLockedEvent(f.Id, f.OwnerSeat, f.SkillInstanceId, f.GameplayHash, AnniversaryDamageTotal(f.OwnerSeat)));
            ids = _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat).SelectMany(p => GetHand(p).Concat(GetEquipment(p)).Concat(GetJudgment(p))).Select(c => c.Id).ToArray();
        }
        else
        {
            var hand = GetHand(_players[target]).Select(c => c.Id).ToArray();
            for (var i = hand.Length - 1; i > 0; i--) { var j = _random.Next(i + 1); (hand[i], hand[j]) = (hand[j], hand[i]); }
            ids = hand.Take((hand.Length + 1) / 2).Concat(GetEquipment(_players[target]).Select(c => c.Id)).ToArray();
        }
        MoveAnniversaryMing(f, ids, true);
    }
    private bool DrainAnniversarySunQuan(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveAnniversarySunQuanCangmingColors)]
    private void ReturnAnniversarySunQuanColors(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.AnniversarySunQuan is not { Stage: AnniversarySunQuanStage.Drawing, DrawOutcome: null } r || outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != r.ColorCount ||
            outcome.SequenceBefore < r.Before || outcome.SequenceAfter != _movementSequence || ProgramEventHistory<AnniversarySunQuanMovementPaidEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Cangming colors lost its exact native draw producer and unique typed outcome.");
        var moves = _cardMovements.Where(m => m.Sequence > outcome.SequenceBefore && m.Sequence <= outcome.SequenceAfter && m.Reason.Value == AnniversaryMingDraw && m.To == CardLocation.Hand(f.OwnerSeat)).ToArray();
        ReplaceRuntimeTop(f = f with { AnniversarySunQuan = r with { Stage = AnniversarySunQuanStage.MovementChildren, DrawOutcome = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceAfter, Movements = moves }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new AnniversarySunQuanMovementPaidEvent(f.Id, 0, moves.Length, outcome.SequenceBefore, outcome.SequenceAfter));
        ResumeAnniversarySunQuan(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 40500)] private bool ResumeAnniversarySunQuan(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversarySunQuan: not null } f || f.Id != id) return false;
        RequireAnniversarySunQuan(f);
        if (f.AnniversarySunQuan.Stage == AnniversarySunQuanStage.Drawing) return true;
        if (f.AnniversarySunQuan.Stage is AnniversarySunQuanStage.ChoosingBranch or AnniversarySunQuanStage.ChoosingTarget) { if (_pendingDecision is null) PublishAnniversarySunQuan(f); return true; }
        if (DrainAnniversarySunQuan(f)) return true;
        f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null }); FinishAnniversarySunQuan(f); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 40500)] private bool ReturnAnniversarySunQuanMovement(ProgramSkillFrame f) => f.AnniversarySunQuan is not null && ResumeAnniversarySunQuan(f.Id);
    private void FinishAnniversarySunQuan(ProgramSkillFrame f)
    {
        if (!ProgramEventHistory<AnniversarySunQuanMovementPaidEvent>().Any(e => e.FrameId == f.Id))
            AdvanceEventRulesAndQueueFact(new AnniversarySunQuanMovementPaidEvent(f.Id, 0, 0, f.AnniversarySunQuan!.Before, f.AnniversarySunQuan.After));
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, AnniversarySunQuan = f.AnniversarySunQuan! with { Stage = AnniversarySunQuanStage.Finished } });
        AdvanceEventRulesAndQueueFact(new AnniversarySunQuanCompletedEvent(f.Id)); FinishProgramSkill(f, true);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversarySunQuanJichao)] private PromptChoice ChooseAnniversarySunQuan(PendingDecision p, ProgramSkillFrame f) => p.Choices.Last();
}
