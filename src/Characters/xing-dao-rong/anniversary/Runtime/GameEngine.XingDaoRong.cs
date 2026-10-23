using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string XingDaoRongAction = "anniversary-xing-dao-rong";
    private static CardMoveReason XingDaoRongDiscardReason => new("skill-program.anniversary:xuhe.discard");
    private static CardMoveReason XingDaoRongDrawReason => new("skill-program.anniversary:xuhe.nearby-draw");
    private static CardConversionSource XingDaoRongSource(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId!, f.OwnerSeat, f.SkillInstanceId);
    [ProgramCapability] private sealed class AnniversaryXingDaoRongHost(GameEngine engine) : IAnniversaryXingDaoRongHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame f) => engine.BeginXingDaoRong(f); }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 45700)] private bool QualifyXingDaoRong(ProgramTriggerLegalityContext g) =>
        g.SkillId != XingDaoRongComposition.Skill || g.Trigger.Id != XingDaoRongComposition.Ending ||
        g.WindowContext is { Window: SkillProgramTriggerWindow.PlayEnding, SourceSeat: { } actor } && actor == g.OwnerSeat &&
        _players.Any(p => p.IsAlive && p.MaxHp > g.Owner.MaxHp);
    private SkillProgramStepOutcome BeginXingDaoRong(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.XingDaoRongXuHe is not null || f.InstructionIndex != 2 || !XingDaoRongOriginal(f))
            throw new InvalidOperationException("Xuhe requires its exact own Play-start frame after the real maximum-HP payment.");
        var started = CompleteProgramEventHistory().ToArray();
        var start = Array.FindLastIndex(started, e => e is ProgramBindingStartedEvent b && b.FrameId == f.Id);
        var paid = started.Skip(start + 1).OfType<MaximumHpChangedEvent>().Where(e => e.PlayerSeat == f.OwnerSeat && e.SkillId == f.SkillId && e.Delta == -1).ToArray();
        if (start < 0 || paid is not [var cost]) throw new InvalidOperationException("Xuhe has no unique native maximum-HP payment in its original binding.");
        var r = new XingDaoRongXuHeReceipt(XingDaoRongSource(f), f.GameplayHash, f.WindowContext!.ParentFrameId, _turnNumber, cost.MaximumHp);
        ReplaceRuntimeTop(f = f with { XingDaoRongXuHe = r });
        AdvanceEventRulesAndQueueFact(new XingDaoRongXuHeStartedEvent(f.Id, r.Source, r.Hash, r.ParentId, r.ActualTurn, r.PaidMaximum));
        PublishXingDaoRong(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private int[] XingDaoRongNearby(int owner) => _players.Where(p => p.IsAlive && (p.Seat == owner || GetCombatDistance(owner, p.Seat) <= 1))
        .OrderBy(p => RelativeTableOrder(_currentSeat, p.Seat)).Select(p => p.Seat).ToArray();
    private IReadOnlyList<PromptChoice> XingDaoRongCards(ProgramSkillFrame f) => BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat,
        f.XingDaoRongXuHe!.Targets[f.XingDaoRongXuHe.Index], [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard);
    private IReadOnlyList<PromptChoice> XingDaoRongChoices(ProgramSkillFrame f)
    {
        var r = f.XingDaoRongXuHe!;
        if (r.Stage == XingDaoRongXuHeStage.Mode) return Array.AsReadOnly(new[] {
            new PromptChoice(new($"xu-he.{f.Id}.discard"), "弃置距离1以内每名角色各一张牌（包括自己）", [], [], new Dictionary<string,string> { ["program-action"] = XingDaoRongAction, ["mode"] = "discard" }),
            new PromptChoice(new($"xu-he.{f.Id}.draw"), "距离1以内每名角色各摸一张牌（包括自己）", [], [], new Dictionary<string,string> { ["program-action"] = XingDaoRongAction, ["mode"] = "draw" }) });
        return r.Stage != XingDaoRongXuHeStage.Card ? [] : Array.AsReadOnly(XingDaoRongCards(f).Select(c => c with {
            Id = new($"xu-he.{f.Id}.{r.Index}.{c.Id.Value}"), Parameters = new Dictionary<string,string>(c.Parameters) { ["program-action"] = XingDaoRongAction, ["mode"] = "card" } }).ToArray());
    }
    private void PublishXingDaoRong(ProgramSkillFrame f)
    {
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        PublishParticipantHandChoice(f, f.OwnerSeat, XingDaoRongChoices(f), "虚猲：统一选择弃牌或摸牌，按实际距离依次结算。",
            f.XingDaoRongXuHe!.Stage == XingDaoRongXuHeStage.Card ? f.XingDaoRongXuHe.Targets[f.XingDaoRongXuHe.Index] : f.OwnerSeat);
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer(XingDaoRongAction)] private void AnswerXingDaoRong(PromptChoice answer)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { XingDaoRongXuHe: { } r } f || !ValidXingDaoRong(f) ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual([answer], XingDaoRongChoices(f).Where(c => c.Id == answer.Id).ToArray()))
            throw new InvalidOperationException("Xuhe lost its current original group choice or opaque discard slot.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (r.Stage == XingDaoRongXuHeStage.Mode)
        {
            var mode = answer.Parameters["mode"]; var targets = XingDaoRongNearby(f.OwnerSeat);
            ReplaceRuntimeTop(f = f with { XingDaoRongXuHe = r with { Mode = mode, Targets = targets, Stage = XingDaoRongXuHeStage.Next } });
            foreach (var (seat, ordinal) in targets.Select((seat, i) => (seat, i))) AdvanceEventRulesAndQueueFact(new XingDaoRongXuHeTargetEvent(f.Id, ordinal, seat));
            AdvanceEventRulesAndQueueFact(new XingDaoRongXuHeModeEvent(f.Id, mode, targets.Length)); ContinueXingDaoRong(f.Id); return;
        }
        if (r.Stage != XingDaoRongXuHeStage.Card || answer.Parameters["mode"] != "card") throw new InvalidOperationException("Xuhe cannot repeat an already paid discard.");
        var target = r.Targets[r.Index]; var from = new CardLocation(Enum.Parse<CardZoneKind>(answer.Parameters["source-zone"]), target);
        var card = _cardZones.CardsAt(from)[int.Parse(answer.Parameters["slot-index"], CultureInfo.InvariantCulture)];
        ReplaceRuntimeTop(f = f with { XingDaoRongXuHe = r with { Stage = XingDaoRongXuHeStage.DiscardChildren, Before = _movementSequence, After = _movementSequence, Discard = null, Draw = null }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramLogicalDiscard(f, f.OwnerSeat, [card.Id], XingDaoRongDiscardReason, (batch, records) => {
            var m = records.Single(); var active = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(active with { XingDaoRongXuHe = active.XingDaoRongXuHe! with { After = _movementSequence, Discard = m } });
            RecordBianFuRenForeignDiscardProducer(m, f.OwnerSeat, f.Id);
            AdvanceEventRulesAndQueueFact(new XingDaoRongXuHeDiscardedEvent(f.Id, r.Index, target, m.CardId, m.From, m.To, m.Sequence, batch));
        }); ContinueXingDaoRong(f.Id);
    }
    private bool DrainXingDaoRong(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginAdvancedSkillsChanged(f.Id) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 45700)] private bool ContinueXingDaoRong(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { XingDaoRongXuHe.Stage: not XingDaoRongXuHeStage.Completed } top || top.Id != id) return false;
        while (_pendingDecision is null && _resolutionStack.LastOrDefault() is ProgramSkillFrame { XingDaoRongXuHe: { } r } f && f.Id == id)
        {
            if (!ValidXingDaoRong(f)) throw new InvalidOperationException("Xuhe lost its original paid nearby group cursor.");
            if (r.Stage is XingDaoRongXuHeStage.DiscardChildren or XingDaoRongXuHeStage.DrawChildren)
            {
                if (DrainXingDaoRong(f)) return true;
                f = GetActiveProgramFrame(id); r = f.XingDaoRongXuHe!;
                ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, XingDaoRongXuHe = r = r with { Index = r.Index + 1, Stage = XingDaoRongXuHeStage.Next, Draw = null, Discard = null } });
            }
            if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None || r.Index >= r.Targets.Count)
            {
                ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, XingDaoRongXuHe = r with { Stage = XingDaoRongXuHeStage.Completed } });
                AdvanceRuntimeProgram(f.Id); return true;
            }
            if (r.Stage == XingDaoRongXuHeStage.Next)
            {
                var target = r.Targets[r.Index];
                if (!_players[target].IsAlive || r.Mode == "discard" && XingDaoRongCards(f).Count == 0)
                { ReplaceRuntimeTop(f with { XingDaoRongXuHe = r with { Index = r.Index + 1 } }); continue; }
                if (r.Mode == "draw")
                {
                    ReplaceRuntimeTop(f = f with { XingDaoRongXuHe = r with { Stage = XingDaoRongXuHeStage.Drawing } });
                    BeginOperationProgramDraw(f, target, 1, XingDaoRongDrawReason); return true;
                }
                ReplaceRuntimeTop(f = f with { XingDaoRongXuHe = r with { Stage = XingDaoRongXuHeStage.Card } });
            }
            PublishXingDaoRong(f); return true;
        }
        return true;
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveAnniversaryXingDaoRongXuHe)] private void ReturnXingDaoRongDraw(ProgramSkillFrame supplied, NativeDrawOutcome outcome)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.XingDaoRongXuHe!;
        if (!ValidXingDaoRong(f) || r.Stage != XingDaoRongXuHeStage.Drawing || f.NativeDraw is not null || outcome.RecipientSeat != r.Targets[r.Index] || outcome.RequestedCount != 1 || outcome.SequenceAfter != _movementSequence)
            throw new InvalidOperationException("Xuhe lost its exact current recipient's native draw return.");
        ReplaceRuntimeTop(f with { XingDaoRongXuHe = r with { Stage = XingDaoRongXuHeStage.DrawChildren, Before = outcome.SequenceBefore, After = outcome.SequenceAfter, Draw = outcome }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new XingDaoRongXuHeDrawnEvent(f.Id, r.Index, outcome.RecipientSeat, outcome.ActualDrawIds.Count, outcome.SequenceBefore, outcome.SequenceAfter));
        ContinueXingDaoRong(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 45700)] private bool ReturnXingDaoRongMovement(ProgramSkillFrame f) => f.XingDaoRongXuHe is not null && ContinueXingDaoRong(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 45700)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 45700)] private bool KeepPaidXingDaoRong(ProgramSkillFrame f) => f.XingDaoRongXuHe is not null && ValidXingDaoRong(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryXingDaoRongXuHe)] private PromptChoice XingDaoRongAi(PendingDecision p, ProgramSkillFrame f) =>
        p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("mode") == "draw") ?? p.Choices[0];
}
