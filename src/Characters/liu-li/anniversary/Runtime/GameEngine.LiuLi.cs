using System.Globalization;
using System.Text;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string LiuLiDiscardReason = "skill-program.anniversary:fuli.discard", LiuLiDrawReason = "skill-program.anniversary:fuli.draw";
    private CardConversionSource LiuLiSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramCapability] private sealed class AnniversaryLiuLiHost(GameEngine e) : IAnniversaryLiuLiHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp op) => e.BeginLiuLi(f, op); }
    private static int LiuLiNameLength(Card c) => (IsSlashCard(c.Kind) ? "杀" : c.DisplayName).EnumerateRunes().Count();
    private Card[][] LiuLiCategories(int seat) => new[] { SkillProgramCardCategory.Basic, SkillProgramCardCategory.Trick, SkillProgramCardCategory.Equipment }
        .Select(category => GetHand(_players[seat]).Where(c => GetProgramCardCategory(c.Kind) == category).ToArray()).ToArray();
    private bool LiuLiCanDiscard(int seat, IReadOnlyList<Card> cards) => cards.Count > 0 && cards.All(c => !IsSelfHandCategoryDiscardForbidden(seat, c, CardLocation.Hand(seat), OwnedCardMoveIntent.Discard));
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 43300, SkillProgramEffectOp.ResolveAnniversaryLiuLiFuli)]
    private bool CanStartLiuLiFuli(CharacterState owner) => LiuLiCategories(owner.Seat).Any(cards => LiuLiCanDiscard(owner.Seat, cards));
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 43300, SkillProgramEffectOp.ResolveAnniversaryLiuLiDehua)]
    private bool CanStartLiuLiDehua(ProgramTriggerLegalityContext g) => g.SkillId == AnniversaryLiuLiComposition.Dehua &&
        !ProgramEventHistory<AnniversaryLiuLiStartedEvent>().Any(e => e.Source.OwnerSeat == g.OwnerSeat && e.Source.SkillId == g.SkillId && e.Source.BindingId == g.Candidate.BindingId && e.Round == _roundNumber);
    private SkillProgramStepOutcome BeginLiuLi(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id); var fuli = op == SkillProgramEffectOp.ResolveAnniversaryLiuLiFuli;
        if (f.AnniversaryLiuLi is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            (fuli ? f.SkillId != AnniversaryLiuLiComposition.Fuli || f.TriggerId is not null || f.ActivationId != "reveal-discard-draw" || _currentSeat != f.OwnerSeat || _phase != TurnPhase.Play :
            f.SkillId != AnniversaryLiuLiComposition.Dehua || f.TriggerId != "round-damage-name" || f.ActivationId != f.TriggerId || f.WindowContext?.Window != SkillProgramTriggerWindow.RoundStarting))
            throw new InvalidOperationException("Liu Li requires his original unpaid zero-input activation or exact round candidate.");
        var r = new AnniversaryLiuLiReceipt(LiuLiSource(f), f.GameplayHash, f.WindowContext?.ParentFrameId ?? 0, _turnNumber, _roundNumber, fuli ? AnniversaryLiuLiStage.Revealing : AnniversaryLiuLiStage.UseChoice);
        ReplaceRuntimeTop(f = f with { AnniversaryLiuLi = r });
        AdvanceEventRulesAndQueueFact(new AnniversaryLiuLiStartedEvent(f.Id, r.Source, r.Hash, r.ParentId, r.Turn, r.Round));
        if (fuli)
        {
            AdvanceEventRulesAndQueueFact(new CardsRevealedEvent(f.Id, Array.AsReadOnly(GetHand(_players[f.OwnerSeat]).Select(ToSnapshot).ToArray())));
            if (!TryBeginPublicHandRevealWindow(f.Id, "anniversary-liu-li-fuli")) CompleteLiuLiReveal(GetActiveProgramFrame(f.Id));
        }
        else { EnsureLiuLiDamagePool(f.OwnerSeat); PublishLiuLi(f); }
        return SkillProgramStepOutcome.AwaitChild;
    }
    [PublicHandRevealReturn("anniversary-liu-li-fuli")] private void CompleteLiuLiReveal(ProgramSkillFrame f)
    {
        if (!ValidLiuLi(f) || f.AnniversaryLiuLi!.Stage != AnniversaryLiuLiStage.Revealing) throw new InvalidOperationException("Fuli lost its original complete public Hand reveal.");
        ReplaceRuntimeTop(f = f with { AnniversaryLiuLi = f.AnniversaryLiuLi with { Stage = AnniversaryLiuLiStage.Category } }); PublishLiuLi(f);
    }
    private IReadOnlyList<PromptChoice> LiuLiChoices(ProgramSkillFrame f)
    {
        var r = f.AnniversaryLiuLi!; var choices = new List<PromptChoice>();
        PromptChoice Choice(string key, string label, IReadOnlyList<int> cards, IReadOnlyList<int> targets) => new(new($"liu-li.{f.Id}.{r.Stage}.{key}"), label, cards, targets,
            new Dictionary<string, string> { ["program-action"] = "anniversary-liu-li", ["option"] = key });
        if (r.Stage == AnniversaryLiuLiStage.Category)
        {
            var groups = LiuLiCategories(f.OwnerSeat); var labels = new[] { "基本牌", "锦囊牌", "装备牌" };
            for (var i = 0; i < groups.Length; i++) if (LiuLiCanDiscard(f.OwnerSeat, groups[i])) choices.Add(Choice("category-" + i, "弃置所有" + labels[i], groups[i].Select(c => c.Id).ToArray(), []));
        }
        else if (r.Stage == AnniversaryLiuLiStage.RangeTarget)
            foreach (var p in _players.Where(p => p.IsAlive)) choices.Add(Choice("range-" + p.Seat, $"令{p.Name}的攻击范围" + (r.DamageDiscarded ? "减至0" : "-1"), [], [p.Seat]));
        else if (r.Stage == AnniversaryLiuLiStage.UseChoice)
            foreach (var (plan, i) in LiuLiPlans(f.OwnerSeat).Select((p, i) => (p, i))) choices.Add(Choice("use-" + i, "视为使用【" + CardCatalog.Get(plan.Kind).DisplayName + "】" + string.Join("、", plan.Targets.Select(t => _players[t].Name)), [], plan.Targets));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishLiuLi(ProgramSkillFrame f)
    {
        var choices = LiuLiChoices(f);
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None || choices.Count == 0) { FinishLiuLi(f); return; }
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        PublishParticipantHandChoice(f, f.OwnerSeat, choices, f.SkillId == AnniversaryLiuLiComposition.Fuli ? "抚黎：选择弃牌类别或受影响角色。" : "德化：选择尚可使用的伤害牌及其原生目标。", f.OwnerSeat);
    }
    [ProgramAnswer("anniversary-liu-li")] private void AnswerLiuLi(PromptChoice c)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryLiuLi: { } r } f || !ValidLiuLi(f) ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !LiuLiChoices(f).Any(choice => AssistedChoicesEqual([choice], [c]))) throw new InvalidOperationException("Liu Li rejects a stale category, native use plan or range recipient.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var option = c.Parameters["option"];
        if (r.Stage == AnniversaryLiuLiStage.Category)
        {
            var category = int.Parse(option[9..], CultureInfo.InvariantCulture); var cards = LiuLiCategories(f.OwnerSeat)[category];
            if (!LiuLiCanDiscard(f.OwnerSeat, cards) || !cards.Select(card => card.Id).SequenceEqual(c.Cards)) throw new InvalidOperationException("Fuli must pay the complete live category of discardable Hand cards.");
            var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { AnniversaryLiuLi = r with { Stage = AnniversaryLiuLiStage.CostChildren, Category = category, Cards = cards.Select(card => card.Id).ToArray(), NameSum = cards.Sum(LiuLiNameLength), DamageDiscarded = cards.Any(card => IsDamageCardKind(card.Kind)), Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            AdvanceEventRulesAndQueueFact(new AnniversaryLiuLiDiscardPaidEvent(f.Id, category, f.AnniversaryLiuLi.Cards.Count, f.AnniversaryLiuLi.NameSum, f.AnniversaryLiuLi.DamageDiscarded, before, before));
            MoveProgramLogicalDiscard(f, f.OwnerSeat, f.AnniversaryLiuLi.Cards, new(LiuLiDiscardReason), (_, _) =>
            { var current = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(current with { AnniversaryLiuLi = current.AnniversaryLiuLi! with { After = _movementSequence } }); });
            f = GetActiveProgramFrame(f.Id); r = f.AnniversaryLiuLi!;
            ContinueLiuLi(f.Id); return;
        }
        if (r.Stage == AnniversaryLiuLiStage.RangeTarget)
        {
            var target = c.Targets.Single();
            AdvanceEventRulesAndQueueFact(new AnniversaryLiuLiRangeIssuedEvent(f.Id, r.Source, r.Hash, target, ProgramEventHistory<TurnStartedEvent>().Count(e => e.ActorSeat == f.OwnerSeat), r.DamageDiscarded));
            FinishLiuLi(f); return;
        }
        var plan = LiuLiPlans(f.OwnerSeat)[int.Parse(option[4..], CultureInfo.InvariantCulture)];
        ReplaceRuntimeTop(f = f with { AnniversaryLiuLi = r with { Stage = AnniversaryLiuLiStage.IssuingUse, Plan = plan } }); IssueLiuLiUse(f);
    }
    private bool DrainLiuLi(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.Program) || TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 43300)] private bool ContinueLiuLi(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryLiuLi: { } r } f || f.Id != id || _pendingDecision is not null || r.Stage == AnniversaryLiuLiStage.Completed) return false;
        if (!ValidLiuLi(f)) throw new InvalidOperationException("Liu Li lost his original paid native continuation.");
        if (r.Stage is AnniversaryLiuLiStage.Category or AnniversaryLiuLiStage.RangeTarget or AnniversaryLiuLiStage.UseChoice) { PublishLiuLi(f); return true; }
        if (r.Stage == AnniversaryLiuLiStage.NativeUse && f.VirtualBasicDraft is null && IsSlashCard(r.Plan!.Kind))
        { CompleteLiuLiNative(GetActiveProgramFrame(id)); return true; }
        if (r.Stage is not (AnniversaryLiuLiStage.CostChildren or AnniversaryLiuLiStage.DrawChildren or AnniversaryLiuLiStage.SkillChildren)) return true;
        if (DrainLiuLi(f)) return true;
        f = GetActiveProgramFrame(id); r = f.AnniversaryLiuLi!; ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (r.Stage == AnniversaryLiuLiStage.SkillChildren) { FinishLiuLi(f); return true; }
        if (r.Stage == AnniversaryLiuLiStage.CostChildren)
        {
            var maximum = _players.Where(p => p.IsAlive).Select(p => GetHand(p).Count).DefaultIfEmpty(0).Max(); var count = Math.Min(r.NameSum, maximum);
            AdvanceEventRulesAndQueueFact(new AnniversaryLiuLiDrawIssuedEvent(f.Id, r.NameSum, maximum, count));
            ReplaceRuntimeTop(f = f with { AnniversaryLiuLi = r = r with { Stage = count == 0 ? AnniversaryLiuLiStage.RangeTarget : AnniversaryLiuLiStage.Drawing, DrawCount = count } });
            if (count > 0 && _players[f.OwnerSeat].IsAlive && _winner == Winner.None) { BeginOperationProgramDraw(f, f.OwnerSeat, count, new(LiuLiDrawReason)); return true; }
        }
        else ReplaceRuntimeTop(f = f with { AnniversaryLiuLi = r with { Stage = AnniversaryLiuLiStage.RangeTarget } });
        PublishLiuLi(f); return true;
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveAnniversaryLiuLiFuli)] private void ReturnLiuLiDraw(ProgramSkillFrame supplied, NativeDrawOutcome draw)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.AnniversaryLiuLi!;
        if (!ValidLiuLi(f) || r.Stage != AnniversaryLiuLiStage.Drawing || draw.RecipientSeat != f.OwnerSeat || draw.RequestedCount != r.DrawCount || f.NativeDraw is not null) throw new InvalidOperationException("Fuli requires its once-issued native draw.");
        ReplaceRuntimeTop(f with { AnniversaryLiuLi = r with { Stage = AnniversaryLiuLiStage.DrawChildren, Draw = draw, Before = draw.SequenceBefore, After = draw.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new AnniversaryLiuLiDrawReturnedEvent(f.Id, draw.RequestedCount, draw.ActualDrawIds.Count, draw.SequenceBefore, draw.SequenceAfter)); ContinueLiuLi(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 43300)] private bool ReturnLiuLiMovement(ProgramSkillFrame f) => f.AnniversaryLiuLi is not null && ContinueLiuLi(f.Id);
    private void FinishLiuLi(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f = f with { AnniversaryLiuLi = f.AnniversaryLiuLi! with { Stage = AnniversaryLiuLiStage.Completed }, PendingMovementContinuation = null }); AdvanceRuntimeProgram(f.Id); }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryLiuLiFuli, SkillProgramEffectOp.ResolveAnniversaryLiuLiDehua)] private PromptChoice LiuLiAi(PendingDecision p) => p.Choices[0];
}
