using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string GuoZhaoObtainReason = "skill-program.anniversary:pianchong.obtain", GuoZhaoColorDrawReason = "skill-program.anniversary:pianchong.draw", GuoZhaoZunweiDrawReason = "skill-program.anniversary:zunwei.draw";
    [ProgramCapability] private sealed class AnniversaryGuoZhaoHost(GameEngine engine) : IAnniversaryGuoZhaoHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginGuoZhao(f, op); }
    private CardConversionSource GuoZhaoSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private int GuoZhaoOwnerStart(int owner) => ProgramEventHistory<TurnStartedEvent>().LastOrDefault(e => e.ActorSeat == owner)?.TurnNumber ?? 0;
    private IEnumerable<AnniversaryGuoZhaoLeaseEvent> GuoZhaoLeases(int owner) => ProgramEventHistory<AnniversaryGuoZhaoLeaseEvent>().Where(e =>
        e.Source.OwnerSeat == owner && e.OwnerStartTurn == GuoZhaoOwnerStart(owner) && e.Turn <= _turnNumber);
    private AnniversaryGuoZhaoLoss? CaptureAnniversaryGuoZhaoLoss(Card card, CardLocation from, CardLocation to, int sequence)
    {
        if (!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DrawAnniversaryGuoZhaoLostColor) ||
            from.OwnerSeat is not { } owner || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || from == to) return null;
        var leases = GuoZhaoLeases(owner).ToArray(); if (leases.Length == 0 || SuitColor(EffectiveSuit(_players[owner], card)) is not { } red) return null;
        return new(owner, red, GuoZhaoOwnerStart(owner), leases.Max(l => l.FrameId), sequence);
    }
    private IReadOnlyList<AnniversaryGuoZhaoBenefit> GuoZhaoLostBenefits(int owner, string instance, string hash, CardMovementBatchContext? batch)
    {
        if (batch is null) return Array.AsReadOnly(Array.Empty<AnniversaryGuoZhaoBenefit>());
        var result = new List<AnniversaryGuoZhaoBenefit>();
        foreach (var lease in ProgramEventHistory<AnniversaryGuoZhaoLeaseEvent>().Where(e => e.Source.OwnerSeat == owner && e.Source.SkillInstanceId == instance && e.Hash == hash))
        {
            var count = batch.Movements.Where(m => m.AnniversaryGuoZhaoLoss is { } loss && loss.OwnerSeat == owner && loss.Sequence == m.Sequence &&
                loss.OwnerStartTurn == lease.OwnerStartTurn && loss.LastLeaseFrameId >= lease.FrameId && loss.IsRed == lease.LoseRed && lease.Turn <= m.TurnNumber &&
                m.From.OwnerSeat == owner && m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment && _cardMovements.Contains(m)).Select(m => m.CardId).Distinct().Count();
            if (count > 0) result.Add(new(lease.FrameId, count, !lease.LoseRed));
        }
        return Array.AsReadOnly(result.OrderBy(b => b.LeaseId).ToArray());
    }
    private bool GuoZhaoOptionUsed(int owner, string option) => ProgramEventHistory<AnniversaryGuoZhaoOptionPaidEvent>().Any(e => e.Source.OwnerSeat == owner && e.Option == option);
    private bool GuoZhaoHasAvailableOption(int owner) => new[] { "hand", "equipment", "hp" }.Any(o => !GuoZhaoOptionUsed(owner, o));
    [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart, 44300, SkillProgramEffectOp.ResolveAnniversaryGuoZhaoZunwei)]
    private bool CanStartGuoZhaoZunwei(ProgramActivationLegalityContext q) => GuoZhaoHasAvailableOption(q.OwnerSeat) && _players.Any(p => p.IsAlive && p.Seat != q.OwnerSeat);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 44300)]
    private bool QualifyGuoZhao(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context) =>
        t.Effects is not [{ Op: SkillProgramEffectOp.DrawAnniversaryGuoZhaoLostColor }] ||
        GuoZhaoLostBenefits(c.OwnerSeat, c.SkillInstanceId, c.GameplayHash, context.MovementBatch).Count > 0;
    private SkillProgramStepOutcome BeginGuoZhao(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.AnniversaryGuoZhao is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || !GuoZhaoOriginal(f))
            throw new InvalidOperationException($"Guo Zhao needs her exact original unstarted program ({f.Id}/{f.SkillId}/{f.TriggerId}/{f.ActivationId}/{f.InstructionIndex}).");
        var r = new AnniversaryGuoZhaoReceipt(GuoZhaoSource(f), f.GameplayHash, op, f.WindowContext?.ParentFrameId ?? 0, _turnNumber,
            op == SkillProgramEffectOp.ResolveAnniversaryGuoZhaoZunwei ? AnniversaryGuoZhaoStage.OptionChoice : AnniversaryGuoZhaoStage.GainChildren);
        if (op == SkillProgramEffectOp.DrawAnniversaryGuoZhaoLostColor) r = r with { Benefits = GuoZhaoLostBenefits(f.OwnerSeat, f.SkillInstanceId, f.GameplayHash, f.WindowContext!.MovementBatch) };
        ReplaceRuntimeTop(f = f with { AnniversaryGuoZhao = r });
        AdvanceEventRulesAndQueueFact(new AnniversaryGuoZhaoStartedEvent(f.Id, r.Source, r.Hash, op, r.ParentId, r.Turn));
        if (op == SkillProgramEffectOp.ResolveAnniversaryGuoZhaoPianchong)
        {
            var before = _movementSequence; var selected = new List<AnniversaryGuoZhaoGain>();
            ReplaceRuntimeTop(f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            foreach (var red in new[] { true, false })
            {
                var card = _cardZones.CardsAt(CardLocation.DrawPile).LastOrDefault(c => SuitColor(EffectiveSuit(_players[f.OwnerSeat], c)) == red);
                if (card is null) continue;
                MoveCard(card, CardLocation.DrawPile, CardLocation.Hand(f.OwnerSeat), new(GuoZhaoObtainReason));
                var move = _cardMovements.Single(m => m.CardId == card.Id && m.Sequence > before && m.Reason.Value == GuoZhaoObtainReason);
                selected.Add(new(card.Id, red, move.Sequence));
            }
            f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { AnniversaryGuoZhao = r with { Before = before, After = _movementSequence, Gains = selected } });
            ContinueGuoZhao(f.Id);
        }
        else if (op == SkillProgramEffectOp.DrawAnniversaryGuoZhaoLostColor) BeginGuoZhaoColorBenefit(f);
        else PublishGuoZhao(f);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private IReadOnlyList<PromptChoice> GuoZhaoChoices(ProgramSkillFrame f)
    {
        var r = f.AnniversaryGuoZhao!; var result = new List<PromptChoice>();
        PromptChoice Choice(string option, string label, int? target = null) => new(new($"guo-zhao.{f.Id}.{r.Stage}.{option}.{target}"), label, [], target is { } seat ? [seat] : [],
            new Dictionary<string, string> { ["program-action"] = "anniversary-guo-zhao", ["option"] = option });
        if (r.Stage == AnniversaryGuoZhaoStage.ColorChoice)
        {
            result.Add(Choice("lose-red", "失去红色牌时摸黑色牌")); result.Add(Choice("lose-black", "失去黑色牌时摸红色牌"));
        }
        if (r.Stage == AnniversaryGuoZhaoStage.OptionChoice)
            foreach (var target in _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat))
            {
                if (!GuoZhaoOptionUsed(f.OwnerSeat, "hand")) result.Add(Choice("hand", $"向{target.Name}看齐手牌数（最多摸五张）", target.Seat));
                if (!GuoZhaoOptionUsed(f.OwnerSeat, "equipment")) result.Add(Choice("equipment", $"随机使用装备至与{target.Name}相同", target.Seat));
                if (!GuoZhaoOptionUsed(f.OwnerSeat, "hp") && (target.HasHpValue || target.HappyQiaoHealth is not null)) result.Add(Choice("hp", $"回复体力至与{target.Name}相同", target.Seat));
            }
        return Array.AsReadOnly(result.ToArray());
    }
    private void PublishGuoZhao(ProgramSkillFrame f)
    {
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishGuoZhao(f); return; }
        var choices = GuoZhaoChoices(f); if (choices.Count == 0) { FinishGuoZhao(f); return; }
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        PublishParticipantHandChoice(f, f.OwnerSeat, choices, rText(f), f.OwnerSeat);
        static string rText(ProgramSkillFrame frame) => frame.AnniversaryGuoZhao!.Stage == AnniversaryGuoZhaoStage.ColorChoice ? "偏宠：选择失牌与摸牌颜色，直到你的下个实际回合开始。" : "尊位：选择一名其他角色及一项本局尚未移除的效果。";
    }
    [ProgramAnswer("anniversary-guo-zhao")]
    private void AnswerGuoZhao(PromptChoice c)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryGuoZhao: { } r } f || !ValidGuoZhao(f) || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([c], GuoZhaoChoices(f).Where(x => x.Id == c.Id).ToArray())) throw new InvalidOperationException("Guo Zhao lost her exact original color or permanent-option menu.");
        ClearPendingDecision(); var option = c.Parameters["option"];
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishGuoZhao(f); return; }
        if (r.Stage == AnniversaryGuoZhaoStage.ColorChoice)
        {
            if (option is not ("lose-red" or "lose-black") || ProgramEventHistory<AnniversaryGuoZhaoLeaseEvent>().Any(e => e.FrameId == f.Id)) throw new InvalidOperationException("Pianchong cannot issue the same color lease twice.");
            AdvanceEventRulesAndQueueFact(new AnniversaryGuoZhaoLeaseEvent(f.Id, r.Source, r.Hash, r.Turn, GuoZhaoOwnerStart(f.OwnerSeat), option == "lose-red"));
            ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, AnniversaryGuoZhao = r with { Option = option, Paid = true } }); FinishGuoZhao(f); return;
        }
        var target = c.Targets.Single();
        if (r.Stage != AnniversaryGuoZhaoStage.OptionChoice || option is not ("hand" or "equipment" or "hp") || GuoZhaoOptionUsed(f.OwnerSeat, option) || !_players[target].IsAlive || target == f.OwnerSeat)
            throw new InvalidOperationException("Zunwei cannot repeat an option or replace its living other participant.");
        var amount = option == "hand" ? Math.Min(5, Math.Max(0, GetHand(_players[target]).Count - GetHand(_players[f.OwnerSeat]).Count)) : option == "equipment" ? GetEquipment(target).Count : 0;
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, AnniversaryGuoZhao = r with { Paid = true, Target = target, Option = option, Amount = amount,
            Stage = option == "hand" ? AnniversaryGuoZhaoStage.Drawing : option == "equipment" ? AnniversaryGuoZhaoStage.EquipmentNext : AnniversaryGuoZhaoStage.RecoveryChildren } });
        AdvanceEventRulesAndQueueFact(new AnniversaryGuoZhaoOptionPaidEvent(f.Id, r.Source, r.Hash, target, option, amount));
        if (option == "hand") BeginOperationProgramDraw(f, f.OwnerSeat, amount, new(GuoZhaoZunweiDrawReason));
        else ContinueGuoZhao(f.Id);
    }
    private bool DrainGuoZhao(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueGuoZhao(long id)
    {
        while (_pendingDecision is null && _resolutionStack.LastOrDefault() is ProgramSkillFrame { AnniversaryGuoZhao: { } r } f && f.Id == id)
        {
            if (!ValidGuoZhao(f)) throw new InvalidOperationException($"Guo Zhao lost original candidate, paid option or native child return ({id}/{r.Stage}).");
            if (r.Stage is AnniversaryGuoZhaoStage.Drawing or AnniversaryGuoZhaoStage.NativeEquipment or AnniversaryGuoZhaoStage.Completed) return;
            if (r.Stage is AnniversaryGuoZhaoStage.ColorChoice or AnniversaryGuoZhaoStage.OptionChoice) { PublishGuoZhao(f); return; }
            if (DrainGuoZhao(id)) return;
            f = GetActiveProgramFrame(id); r = f.AnniversaryGuoZhao!;
            if (f.PendingMovementContinuation is not null) ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
            if (r.Stage == AnniversaryGuoZhaoStage.GainChildren) { ReplaceRuntimeTop(f = f with { AnniversaryGuoZhao = r with { Stage = AnniversaryGuoZhaoStage.ColorChoice } }); PublishGuoZhao(f); return; }
            if (r.Stage == AnniversaryGuoZhaoStage.DrawChildren)
            {
                if (r.Op == SkillProgramEffectOp.DrawAnniversaryGuoZhaoLostColor)
                { ReplaceRuntimeTop(f = f with { AnniversaryGuoZhao = r with { Cursor = r.Cursor + 1, Draw = null } }); BeginGuoZhaoColorBenefit(f); return; }
                FinishGuoZhao(f); return;
            }
            if (r.Stage == AnniversaryGuoZhaoStage.RecoveryChildren)
            {
                if (r.Cursor == 0)
                {
                    if (TryBeginHappyQiaoQuery(f, r.Target, "guo-zhao-zunwei-target")) return;
                    f = GetActiveProgramFrame(id);
                    var targetHp = HasHappyQiaoHealth(r.Target) ? HappyQiaoQueryValue(f, r.Target, "guo-zhao-zunwei-target") : UnambiguousHp(_players[r.Target]) ?? 0;
                    if (TryBeginHappyQiaoQuery(f, f.OwnerSeat, "guo-zhao-zunwei-owner")) return;
                    f = GetActiveProgramFrame(id);
                    var ownHp = HasHappyQiaoHealth(f.OwnerSeat) ? HappyQiaoQueryValue(f, f.OwnerSeat, "guo-zhao-zunwei-owner") : UnambiguousHp(_players[f.OwnerSeat]) ?? 0;
                    var amount = Math.Max(0, targetHp - ownHp); var before = _movementSequence;
                    ReplaceRuntimeTop(f = f with { AnniversaryGuoZhao = r with { Amount = amount, Cursor = 1, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
                    AdvanceEventRulesAndQueueFact(new AnniversaryGuoZhaoRecoveryIssuedEvent(f.Id, amount, ownHp, targetHp));
                    if (amount > 0) new ProgramSkillHost(this).Recover(f.Id, f.OwnerSeat, f.OwnerSeat, amount, null, null);
                    continue;
                }
                FinishGuoZhao(f); return;
            }
            if (r.Stage is AnniversaryGuoZhaoStage.EquipmentNext or AnniversaryGuoZhaoStage.EquipmentChildren)
            { BeginGuoZhaoEquipment(f); return; }
        }
    }
    private void BeginGuoZhaoColorBenefit(ProgramSkillFrame f)
    {
        var r = f.AnniversaryGuoZhao!;
        if (r.Cursor >= r.Benefits.Count || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishGuoZhao(f); return; }
        var benefit = r.Benefits[r.Cursor];
        ReplaceRuntimeTop(f = f with { AnniversaryGuoZhao = r with { Stage = AnniversaryGuoZhaoStage.Drawing, DrawRed = benefit.DrawRed, Amount = benefit.Count } });
        AdvanceEventRulesAndQueueFact(new AnniversaryGuoZhaoBenefitIssuedEvent(f.Id, r.Cursor, benefit.LeaseId, benefit.Count, benefit.DrawRed));
        BeginOperationProgramDraw(f, f.OwnerSeat, benefit.Count, new(GuoZhaoColorDrawReason));
    }
    private bool TryChooseAnniversaryGuoZhaoDrawMaterial(CharacterState player, CardMoveReason reason, IReadOnlyList<Card> drawPile, out Card? material)
    {
        material = null;
        if (reason.Value != GuoZhaoColorDrawReason || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryGuoZhao: { Op: SkillProgramEffectOp.DrawAnniversaryGuoZhaoLostColor, Stage: AnniversaryGuoZhaoStage.Drawing, DrawRed: { } red } r,
                NativeDraw: { ReturnKind: NativeDrawAttemptReturnKind.ProgramOperation, Operation: SkillProgramEffectOp.DrawAnniversaryGuoZhaoLostColor } paid } f) return false;
        if (!ValidGuoZhao(f) || f.OwnerSeat != player.Seat || paid.Recipients is not [var recipient] || recipient != player.Seat || paid.RequestedCount != r.Amount || paid.Reason != reason)
            throw new InvalidOperationException("Pianchong colored draw lost its exact paid native request.");
        var candidates = drawPile.Where(c => SuitColor(EffectiveSuit(player, c)) == red);
        material = HasCardPolicy(player, SkillProgramCardPolicyKind.DrawFromBottom) ? candidates.FirstOrDefault() : candidates.LastOrDefault();
        return true;
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.DrawAnniversaryGuoZhaoLostColor)] private void ReturnGuoZhaoColoredDraw(ProgramSkillFrame f, NativeDrawOutcome outcome) => ReturnGuoZhaoDraw(f, outcome);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveAnniversaryGuoZhaoZunwei)] private void ReturnGuoZhaoZunweiDraw(ProgramSkillFrame f, NativeDrawOutcome outcome) => ReturnGuoZhaoDraw(f, outcome);
    private void ReturnGuoZhaoDraw(ProgramSkillFrame supplied, NativeDrawOutcome outcome)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.AnniversaryGuoZhao!;
        if (!ValidGuoZhao(f) || r.Stage != AnniversaryGuoZhaoStage.Drawing || f.NativeDraw is not null || outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != r.Amount || outcome.SequenceAfter != _movementSequence)
            throw new InvalidOperationException("Guo Zhao lost her exact once-only native draw return.");
        ReplaceRuntimeTop(f with { AnniversaryGuoZhao = r with { Stage = AnniversaryGuoZhaoStage.DrawChildren, Before = outcome.SequenceBefore, After = outcome.SequenceAfter, Draw = outcome }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new AnniversaryGuoZhaoDrawReturnedEvent(f.Id, r.Cursor, r.Amount, outcome.ActualDrawIds.Count, outcome.SequenceBefore, outcome.SequenceAfter));
        ContinueGuoZhao(f.Id);
    }
    private void FinishGuoZhao(ProgramSkillFrame f)
    {
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, PendingMovementContinuation = null, AnniversaryGuoZhao = f.AnniversaryGuoZhao! with { Stage = AnniversaryGuoZhaoStage.Completed } });
        AdvanceEventRulesAndQueueFact(new AnniversaryGuoZhaoCompletedEvent(f.Id)); AdvanceRuntimeProgram(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 44300)] private bool ResumeGuoZhao(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryGuoZhao: { Stage: not AnniversaryGuoZhaoStage.Completed } } f || f.Id != id) return false; ContinueGuoZhao(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 44300)] private bool ReturnGuoZhaoMovement(ProgramSkillFrame f) => f.AnniversaryGuoZhao is not null && ResumeGuoZhao(f.Id);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryGuoZhaoPianchong, SkillProgramEffectOp.ResolveAnniversaryGuoZhaoZunwei)]
    private PromptChoice ChooseGuoZhao(PendingDecision p, ProgramSkillFrame f) => p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("option") == "hand") ?? p.Choices[0];
}
