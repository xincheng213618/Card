using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string BoundaryLiuFengAction = "boundary-liu-feng";
    private static CardConversionSource BoundaryLiuFengSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramCapability] private sealed class BoundaryLiuFengHost(GameEngine e) : IBoundaryLiuFengHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp op) => e.BeginBoundaryLiuFeng(f, op); }
    private bool BoundaryLiuFengPaidThisTurn(int owner) => ProgramEventHistory<BoundaryLiuFengCostPaidEvent>().Any(e => e.Source.OwnerSeat == owner && e.Turn == _turnNumber && e.TurnOwner == _turnProgression.OwnerSeat);
    private int[] BoundaryLiuFengTargets(int actor) => _players.Where(p => CanUseVirtualSlashTarget(_players[actor], p, CardKind.Slash, Suit.None, null, 0, [], conversionSource: null)).Select(p => p.Seat).ToArray();
    private int[] BoundaryLiuFengCurrentTargets(ProgramSkillFrame f) => f.BoundaryLiuFeng?.Need is { } n ? n.Targets.Where(t => BoundaryLiuFengRequestTarget(n, t)).ToArray() : BoundaryLiuFengTargets(f.OwnerSeat);
    private bool BoundaryLiuFengCountsSlash(ProgramSkillFrame f) => _phase == TurnPhase.Play && _currentSeat == f.OwnerSeat && (f.BoundaryLiuFeng?.Need is null || f.BoundaryLiuFeng.Need.Intent == BoundaryLiuFengIntent.BorrowedSword);
    private void CancelBoundaryLiuFeng(ProgramSkillFrame f)
    {
        if (f.BoundaryLiuFeng is not { Need: not null, OriginalDecision: { } original } r) { FinishBoundaryLiuFeng(f); return; }
        CompleteBoundaryLiuFengFrame(f);
        if (r.CostSequence > 0) throw new InvalidOperationException("A paid Qinling cannot return as an unpaid cancellation.");
        _pendingDecision = BoundaryLiuFengNativeDecision(original) with { PromptId = CreatePromptId() };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCondition, 50201, SkillProgramEffectOp.ResolveBoundaryLiuFengQinling)]
    private bool CanActivateBoundaryLiuFeng(ProgramActivationLegalityContext c) => !BoundaryLiuFengPaidThisTurn(c.OwnerSeat) && BoundaryLiuFengTargets(c.OwnerSeat).Length > 0 && GetHand(_players[c.OwnerSeat]).Count + GetEquipment(_players[c.OwnerSeat]).Count > 0;
    private SkillProgramStepOutcome BeginBoundaryLiuFeng(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.BoundaryLiuFeng is { Need: not null } existing)
        { if (op != SkillProgramEffectOp.ResolveBoundaryLiuFengQinling || f.InstructionIndex != 1 || !ValidBoundaryLiuFeng(f) || !BoundaryLiuFengNeedCurrent(existing)) throw new InvalidOperationException("Qinling lost its original native request instruction."); PublishBoundaryLiuFeng(f); return SkillProgramStepOutcome.AwaitChoice; }
        if (f.InstructionIndex != 1 || f.BoundaryLiuFeng is not null || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || !_players[f.OwnerSeat].IsAlive) throw new InvalidOperationException("Liu Feng requires its original empty-selection instruction.");
        var r = new BoundaryLiuFengReceipt(BoundaryLiuFengSource(f), f.GameplayHash, op, f.WindowContext?.ParentFrameId ?? 0, _turnNumber, _turnProgression.OwnerSeat, op == SkillProgramEffectOp.ResolveBoundaryLiuFengXiansi ? BoundaryLiuFengStage.Collect : BoundaryLiuFengStage.Targets);
        ReplaceRuntimeTop(f = f with { BoundaryLiuFeng = r });
        AdvanceEventRulesAndQueueFact(new BoundaryLiuFengStartedEvent(f.Id, r.Source, r.Hash, op, r.ParentId, r.Turn, r.TurnOwner));
        PublishBoundaryLiuFeng(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> BoundaryLiuFengChoices(ProgramSkillFrame f)
    {
        var r = f.BoundaryLiuFeng!; var result = new List<PromptChoice>();
        Dictionary<string,string> Args(string branch) => new() { ["program-action"] = BoundaryLiuFengAction, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch };
        if (r.Stage == BoundaryLiuFengStage.Collect)
        {
            var slots = _players.Where(p => p.IsAlive).SelectMany(p => BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, p.Seat, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Transfer)).ToArray();
            void Add(PromptChoice[] selection)
            {
                var args = Args("collect"); args["count"] = selection.Length.ToString(CultureInfo.InvariantCulture);
                for (var i = 0; i < selection.Length; i++) foreach (var key in new[] { "card-owner-seat", "source-zone", "slot-index" }) args[$"{i}.{key}"] = selection[i].Parameters[key];
                result.Add(new(new($"boundary-liu-feng.{f.Id}.collect.{result.Count}"), string.Join("；", selection.Select(x => _players[int.Parse(x.Parameters["card-owner-seat"], CultureInfo.InvariantCulture)].Name + "：" + x.Description)), selection.SelectMany(x => x.Cards).ToArray(), selection.Select(x => int.Parse(x.Parameters["card-owner-seat"], CultureInfo.InvariantCulture)).Distinct().ToArray(), args));
            }
            for (var i = 0; i < slots.Length; i++) { Add([slots[i]]); for (var j = i + 1; j < slots.Length; j++) Add([slots[i], slots[j]]); }
        }
        else if (r.Stage == BoundaryLiuFengStage.Targets)
            foreach (var target in BoundaryLiuFengCurrentTargets(f)) result.Add(new(new($"boundary-liu-feng.{f.Id}.target.{target}"), "侵凌：对" + _players[target].Name + "使用杀", [], [target], Args("target")));
        else if (r.Stage == BoundaryLiuFengStage.Cost)
            foreach (var old in BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, f.OwnerSeat, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard))
            { if (!BoundaryLiuFengCostLegal(f, old)) continue; var args = new Dictionary<string,string>(old.Parameters); foreach (var pair in Args("cost")) args[pair.Key] = pair.Value; result.Add(old with { Id = new($"boundary-liu-feng.{f.Id}.cost.{old.Id.Value}"), Parameters = args }); }
        if (r.Stage is BoundaryLiuFengStage.Collect or BoundaryLiuFengStage.Targets or BoundaryLiuFengStage.Cost) result.Add(new(new($"boundary-liu-feng.{f.Id}.cancel"), r.Stage == BoundaryLiuFengStage.Collect ? "不放置逆" : "取消侵凌", [], [], Args("cancel")));
        return Array.AsReadOnly(result.Select(PromptChoiceProjection.Freeze).ToArray());
    }
    private bool BoundaryLiuFengCostLegal(ProgramSkillFrame f, PromptChoice choice)
    {
        var r = f.BoundaryLiuFeng!;
        if (r.Targets is not [var target] || !BoundaryLiuFengCurrentTargets(f).Contains(target)) return false;
        var zone = Enum.Parse<CardZoneKind>(choice.Parameters["source-zone"]); var from = new CardLocation(zone, f.OwnerSeat);
        var card = _cardZones.CardsAt(from)[int.Parse(choice.Parameters["slot-index"], CultureInfo.InvariantCulture)];
        if (zone != CardZoneKind.Equipment) return true;
        var actor = _players[f.OwnerSeat]; var victim = _players[target]; var n = r.Need;
        if (n?.Intent is BoundaryLiuFengIntent.BorrowedSword or BoundaryLiuFengIntent.Qinglong && FieldEquipmentSlot(card) == EquipmentSlot.Weapon) return false;
        if (n?.Intent == BoundaryLiuFengIntent.Qinglong) return true;
        if (n?.Intent == BoundaryLiuFengIntent.BorrowedSword) return DistanceRules.AllowsSlashDistance(ProgramDistanceStage.BorrowedSlash, new(actor, victim, CardKind.Slash, PhysicalCardIds: [], PhysicalSuit: Suit.None, EffectiveRank: 0, ExcludedEquipmentId: card.Id));
        var parent = n is null ? null : _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(p => p.Id == n.OwnerFrameId);
        var ignore = n?.Intent is BoundaryLiuFengIntent.ProgramSlash or BoundaryLiuFengIntent.ProgramNearestSlash || parent is not null && (IsQigongRequest(parent) || IsWuJingHejiRequest(parent) || n?.Intent == BoundaryLiuFengIntent.FixedSlash && parent.FixedTargetSlash?.IgnoreDistance == true);
        return CanUseSlashTarget(actor, victim, new(0, CardKind.Slash, Suit.None, 0), ignoreDistance: ignore, noEffectiveRank: true, physicalCardIds: [], excludedEquipmentId: card.Id) && CanSpendSlashUse(actor, victim, ignoresCount: !BoundaryLiuFengCountsSlash(f), effectiveRank: 0, excludedEquipmentId: card.Id, physicalCardIds: []);
    }
    private void PublishBoundaryLiuFeng(ProgramSkillFrame f)
    {
        if (!_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) { CancelBoundaryLiuFeng(f); return; }
        var choices = BoundaryLiuFengChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, f.BoundaryLiuFeng!.Stage == BoundaryLiuFengStage.Collect ? "陷嗣：将至多两张手牌或装备牌置为逆，可以来自同一角色。" : f.BoundaryLiuFeng.Stage == BoundaryLiuFengStage.Targets ? "侵凌：选择杀的目标。" : "侵凌：真实弃置一张手牌或装备牌。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), TargetSeat = f.OwnerSeat, IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer(BoundaryLiuFengAction)] private void AnswerBoundaryLiuFeng(PromptChoice selected)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Liu Feng lost its owning frame.");
        if (f.BoundaryLiuFeng is not { } r || !ValidBoundaryLiuFeng(f) || !BoundaryLiuFengChoices(f).Any(c => AssistedChoicesEqual([c], [selected]))) throw new InvalidOperationException("Liu Feng changed its published real card slots or original target.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect }); var branch = selected.Parameters["branch"];
        if (branch == "cancel") { CancelBoundaryLiuFeng(f); return; }
        if (branch == "target") { ReplaceRuntimeTop(f = f with { BoundaryLiuFeng = r with { Targets = selected.Targets, Stage = BoundaryLiuFengStage.Cost } }); PublishBoundaryLiuFeng(f); return; }
        if (branch == "collect")
        {
            var count = int.Parse(selected.Parameters["count"], CultureInfo.InvariantCulture);
            var ids = Enumerable.Range(0, count).Select(i => { var seat = int.Parse(selected.Parameters[$"{i}.card-owner-seat"], CultureInfo.InvariantCulture); var zone = Enum.Parse<CardZoneKind>(selected.Parameters[$"{i}.source-zone"]); return _cardZones.CardsAt(new(zone, seat))[int.Parse(selected.Parameters[$"{i}.slot-index"], CultureInfo.InvariantCulture)].Id; }).ToArray();
            BeginBoundaryLiuFengMovement(f, ids, CardLocation.Authority(f.OwnerSeat), "collect"); return;
        }
        if (branch != "cost" || BoundaryLiuFengPaidThisTurn(f.OwnerSeat) || !r.Targets.All(BoundaryLiuFengCurrentTargets(f).Contains)) throw new InvalidOperationException("Qinling lost its unpaid actual-turn allowance or native target.");
        var from = new CardLocation(Enum.Parse<CardZoneKind>(selected.Parameters["source-zone"]), f.OwnerSeat); var card = _cardZones.CardsAt(from)[int.Parse(selected.Parameters["slot-index"], CultureInfo.InvariantCulture)];
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { BoundaryLiuFeng = r with { Cards = [card.Id], From = [from], Movement = "cost", Before = before, After = before, Stage = BoundaryLiuFengStage.MovementChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramLogicalDiscard(f, f.OwnerSeat, [card.Id], new("skill-program.boundary:qinling.cost"), (_, moves) =>
        {
            var current = GetActiveProgramFrame(f.Id); var m = moves.Single();
            ReplaceRuntimeTop(current with { BoundaryLiuFeng = current.BoundaryLiuFeng! with { Moves = moves, After = m.Sequence, CostSequence = m.Sequence } });
            AdvanceEventRulesAndQueueFact(new BoundaryLiuFengCostPaidEvent(f.Id, r.Source, r.Hash, r.Turn, r.TurnOwner, card.Id, from, m.Sequence));
        });
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { BoundaryLiuFeng = f.BoundaryLiuFeng! with { After = _movementSequence } }); ContinueBoundaryLiuFeng(f.Id);
    }
    private void BeginBoundaryLiuFengMovement(ProgramSkillFrame f, int[] ids, CardLocation destination, string movement)
    {
        var r = f.BoundaryLiuFeng!; var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { BoundaryLiuFeng = r with { Cards = ids, From = ids.Select(id => _cardZones.GetLocation(id)).ToArray(), Before = before, After = before, Movement = movement, Stage = BoundaryLiuFengStage.MovementChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramCardsFromMultipleSources(ids, destination, new("skill-program.boundary:liu-feng." + movement), (_, moves) => { var current = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(current with { BoundaryLiuFeng = current.BoundaryLiuFeng! with { Moves = moves, After = moves.Max(m => m.Sequence) } }); }, disclosure: CardMovementDisclosure.FaceUp);
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { BoundaryLiuFeng = f.BoundaryLiuFeng! with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new BoundaryLiuFengMovementEvent(f.Id, movement, ids.Length, before, _movementSequence)); ContinueBoundaryLiuFeng(f.Id);
    }
    private bool DrainBoundaryLiuFeng(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginAdvancedSkillsChanged(f.Id) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id);
    private void ContinueBoundaryLiuFeng(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { BoundaryLiuFeng: { } r } f || f.Id != id) return;
        if (!ValidBoundaryLiuFeng(f)) throw new InvalidOperationException("Liu Feng lost its once-paid native continuation.");
        if (r.Stage is BoundaryLiuFengStage.Collect or BoundaryLiuFengStage.Targets or BoundaryLiuFengStage.Cost) { if (_pendingDecision is null) PublishBoundaryLiuFeng(f); return; }
        if (r.Stage is BoundaryLiuFengStage.NativeUse or BoundaryLiuFengStage.RewardDraw) return;
        if (r.Stage is BoundaryLiuFengStage.MovementChildren or BoundaryLiuFengStage.NativeTail)
        {
            if (DrainBoundaryLiuFeng(f)) return;
            f = GetActiveProgramFrame(id); r = f.BoundaryLiuFeng!;
            if (r.Stage == BoundaryLiuFengStage.NativeTail && !r.ConsequencePaid) { SettleBoundaryLiuFeng(f); return; }
            if (r.Movement == "cost" && r.UseId == 0 && r.CostSequence > 0 && _players[f.OwnerSeat].IsAlive && r.Targets.All(BoundaryLiuFengCurrentTargets(f).Contains)) { ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, BoundaryLiuFeng = r with { Stage = BoundaryLiuFengStage.Issuing } }); PrepareBoundaryLiuFengRequest(f); IssueBoundaryLiuFengSlash(GetActiveProgramFrame(id)); return; }
            FinishBoundaryLiuFeng(f);
        }
    }
    private void CompleteBoundaryLiuFengFrame(ProgramSkillFrame f)
    { ClearPendingDecision(); ReplaceRuntimeTop(f = f with { BoundaryLiuFeng = f.BoundaryLiuFeng! with { Stage = BoundaryLiuFengStage.Completed }, PendingMovementContinuation = null }); AdvanceEventRulesAndQueueFact(new BoundaryLiuFengCompletedEvent(f.Id)); AdvanceRuntimeProgram(f.Id); }
    private void FinishBoundaryLiuFeng(ProgramSkillFrame f)
    { var r = f.BoundaryLiuFeng!; CompleteBoundaryLiuFengFrame(f); if (r.Need is not null) { if (r.UseId > 0 && r.Returned) ResumeBoundaryLiuFengRequest(r); else if (r.CostSequence > 0) RepublishBoundaryLiuFengRequest(r); } }
    [ProgramContinuation(ProgramContinuationKind.Resume, 50200)] private bool ResumeBoundaryLiuFeng(long id) { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { BoundaryLiuFeng: { Stage: not BoundaryLiuFengStage.Completed } } f || f.Id != id) return false; ContinueBoundaryLiuFeng(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 50200)] private bool ReturnBoundaryLiuFengMovement(ProgramSkillFrame f) => f.BoundaryLiuFeng is not null && ResumeBoundaryLiuFeng(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 50200)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 50200)] private bool KeepBoundaryLiuFengPaid(ProgramSkillFrame f) => f.BoundaryLiuFeng is { Stage: not (BoundaryLiuFengStage.Collect or BoundaryLiuFengStage.Targets or BoundaryLiuFengStage.Cost) } && ValidBoundaryLiuFeng(f);
}
