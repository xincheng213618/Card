using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string DiouDrawReason = "skill-program.le-diao-chan.diou.draw";
    [ProgramCapability]
    private sealed class LeDiaoChanDiouCapability(GameEngine engine) : ILeDiaoChanDiouHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame) => engine.BeginDiou(frame); }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 29042,
        SkillProgramEffectOp.ResolveDiouTanbanUse)]
    private bool CanOfferDiou(ProgramTriggerLegalityContext gate) =>
        IsIssuedDiouCandidate(gate.Candidate, gate.Context) && DiouShowableCards(gate.Candidate.OwnerSeat,
            new(gate.Candidate.SkillId, gate.Candidate.BindingId, gate.Candidate.OwnerSeat, gate.Candidate.SkillInstanceId)).Count > 0;

    private SkillProgramStepOutcome BeginDiou(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.Diou is not null) { ContinueDiou(f.Id); return SkillProgramStepOutcome.AwaitChild; }
        var c = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId,
            f.GameplayHash, GetProgramTrigger(f).Priority, f.WindowContext?.OccurrenceIndex ?? 0);
        if (f.SkillId != DiouSkill || f.TriggerId != DiouBinding || f.ActivationId != DiouBinding ||
            f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            f.WindowContext is not { } context || !IsIssuedDiouCandidate(c, context) ||
            CompletedDiouClaim(c, context) is not { } claim ||
            _resolutionStack.SingleOrDefault(w => w.Id == context.ParentFrameId) is not ProgramCardTriggerWindowFrame window ||
            !DesignatedExtraTargetCandidateMatches(c, context, window))
            throw new InvalidOperationException("Diou requires its exact completed true-Use Tanban qualification and owning window.");
        ReplaceRuntimeTop(f = f with { Diou = new(claim, window.Id) { Stage = DiouStage.ShowChoice } });
        AdvanceEventRulesAndQueueFact(new DiouStartedEvent(f.Id, claim, window.Id));
        PublishDiou(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<Card> DiouShowableCards(int owner, CardConversionSource source) =>
        Array.AsReadOnly(GetHand(_players[owner]).Where(c => !HasTanbanEntityMark(c.Id)).ToArray());
    private IReadOnlyList<PromptChoice> DiouChoices(ProgramSkillFrame f)
    {
        var r = f.Diou!; var choices = new List<PromptChoice>();
        PromptChoice C(string branch, string suffix, string label, IReadOnlyList<int>? cards = null,
            IReadOnlyList<int>? targets = null, CardKind? output = null) =>
            new(new($"le-diou.{f.Id}.{branch}.{suffix}"), label, cards ?? [], targets ?? [],
                new Dictionary<string, string> { ["program-action"] = "le-diou", ["branch"] = branch,
                    ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["output-kind"] = output?.ToString() ?? "" });
        if (!r.Paid)
        {
            if (_players[f.OwnerSeat].IsAlive)
                choices.AddRange(DiouShowableCards(f.OwnerSeat, DiouSource(f)).Select(c =>
                    C("show", c.Id.ToString(CultureInfo.InvariantCulture), $"展示并视为使用【{c.DisplayName}】",
                        [c.Id], output: AdvancedEffectiveHandKind(_players[f.OwnerSeat], c))));
            choices.Add(C("cancel", "none", "不发动低讴"));
        }
        else if (r.Stage == DiouStage.UseChoice)
        {
            var plans = DiouNativePlans(_players[f.OwnerSeat], r.OutputKind, DiouSource(f));
            for (var i = 0; i < plans.Count; i++)
                choices.Add(C("use", i.ToString(CultureInfo.InvariantCulture), plans[i].Description,
                    targets: plans[i].Targets, output: r.OutputKind));
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishDiou(ProgramSkillFrame f)
    {
        var choices = DiouChoices(f);
        if (choices.Count == 0)
        {
            if (f.Diou!.Paid) { ReplaceRuntimeTop(f = f with { Diou = f.Diou with { Stage = DiouStage.NativeChildren, Returned = true },
                PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); ContinueDiou(f.Id); }
            else FinishDiou(f);
            return;
        }
        PublishParticipantHandChoice(f, f.OwnerSeat, choices, f.Diou!.Stage == DiouStage.ShowChoice
            ? "低讴：展示一张非檀板手牌并视为使用，原牌保留在手中" : "低讴：为展示牌选择当前合法的使用方式", f.OwnerSeat);
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("le-diou")]
    private void AnswerDiou(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Diou: { } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([choice], DiouChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Diou cannot answer a changed original entity or unpublished native option.");
        ClearPendingDecision();
        if (choice.Parameters["branch"] == "cancel") { FinishDiou(f); return; }
        if (choice.Parameters["branch"] == "show")
        {
            var card = DiouShowableCards(f.OwnerSeat, DiouSource(f)).Single(c => c.Id == choice.Cards.Single());
            var kind = AdvancedEffectiveHandKind(_players[f.OwnerSeat], card);
            var first = !DiouWasShownThisTurn(card.Id);
            var same = ProgramBasicCardName(kind) == ProgramBasicCardName(r.Claim.EffectiveKind);
            ReplaceRuntimeTop(f = f with { Diou = r with { Paid = true, ShownCardId = card.Id,
                ShownPrintedKind = card.Kind, OutputKind = kind, FirstShow = first, SameName = same,
                ActualTurn = _turnNumber, TurnOwner = _turnProgression.OwnerSeat,
                ShowSequence = _movementSequence, Stage = DiouStage.UseChoice } });
            AdvanceEventRulesAndQueueFact(new DiouShownEvent(f.Id, f.OwnerSeat, card.Id, card.Kind, kind,
                _turnNumber, _turnProgression.OwnerSeat, _movementSequence, first, same));
            AdvanceEventRulesAndQueueFact(new ProgramCardsRevealedEvent(f.Id, f.SkillId, GetProgramBindingId(f),
                f.OwnerSeat, "diou-retained-hand", [ToSnapshot(card)]));
            PublishDiou(GetActiveProgramFrame(f.Id)); return;
        }
        if (choice.Parameters["branch"] != "use" || r.Stage != DiouStage.UseChoice)
            throw new InvalidOperationException("Diou changed its exact same-name native Use branch.");
        var plans = DiouNativePlans(_players[f.OwnerSeat], r.OutputKind, DiouSource(f));
        var index = int.Parse(choice.Id.Value.Split('.').Last(), CultureInfo.InvariantCulture);
        DispatchDiou(f, plans[index]);
    }
    private void ContinueDiou(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { Diou: { } r } f || f.Id != id) return;
        AssertDiou(f);
        if (r.Stage is DiouStage.ShowChoice or DiouStage.UseChoice) { PublishDiou(f); return; }
        if (r.Stage is DiouStage.NativeIssuing or DiouStage.NativeUse)
            throw new InvalidOperationException("Diou cannot advance past an unreturned native Use.");
        if (r.Stage == DiouStage.Drawing)
        { if (f.NativeDraw is null) BeginOperationProgramDraw(f, f.OwnerSeat, 2, new(DiouDrawReason)); return; }
        if (DrainDiouChildren(f)) return;
        f = GetActiveProgramFrame(id); r = f.Diou!;
        if (r.Stage == DiouStage.NativeChildren && (r.FirstShow || r.SameName) && _players[f.OwnerSeat].IsAlive && _winner == Winner.None)
        {
            ReplaceRuntimeTop(f = f with { Diou = r with { Stage = DiouStage.Drawing }, PendingMovementContinuation = null });
            ContinueDiou(id); return;
        }
        FinishDiou(f);
    }
    private bool DrainDiouChildren(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveDiouTanbanUse)]
    private void ReturnDiouDraw(ProgramSkillFrame f, NativeDrawOutcome o)
    {
        if (f.Diou is not { Stage: DiouStage.Drawing } r || o.RecipientSeat != f.OwnerSeat || o.RequestedCount != 2 ||
            ProgramEventHistory<DiouDrawReturnedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Diou cannot repeat its already issued native Draw2.");
        ReplaceRuntimeTop(f = f with { Diou = r with { Stage = DiouStage.DrawChildren, DrawOutcome = o },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new DiouDrawReturnedEvent(f.Id, 2, o.ActualDrawIds.Count,
            o.ReplacementGainedIds.Count, o.SequenceBefore, o.SequenceAfter, o.AttemptFrameId));
        ContinueDiou(f.Id);
    }
    private void FinishDiou(ProgramSkillFrame f)
    {
        var r = f.Diou!;
        RestoreDiouParentAttack(f);
        AdvanceEventRulesAndQueueFact(new DiouCompletedEvent(f.Id, r.Claim.ActionId, r.Paid,
            r.UseId != 0, r.DrawOutcome is not null));
        ReplaceRuntimeTop(f = f with { Diou = null, PendingMovementContinuation = null }); FinishProgramSkill(f, r.Paid);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 29042)]
    private bool ResumeDiou(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Diou: not null } f || f.Id != id) return false; ContinueDiou(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 29042)]
    private bool ReturnDiouMovement(ProgramSkillFrame f)
    { if (f.Diou is null || f.PendingMovementContinuation is null) return false; ContinueDiou(f.Id); return true; }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveDiouTanbanUse)]
    private PromptChoice ChooseDiou(PendingDecision p, ProgramSkillFrame f)
    {
        var snapshot = CreateSnapshot(p.PlayerSeat);
        var own = snapshot.Players.Single(player => player.Seat == p.PlayerSeat).Hand.ToDictionary(c => c.Id);
        if (!_aiBrains.TryGetValue(p.PlayerSeat, out var brain)) return p.Choices[0];
        double PlanValue(CardKind kind, IReadOnlyList<int> targets)
        {
            var seats = targets.Count == 0 ? new[] { p.PlayerSeat } : targets;
            return seats.Sum(t =>
            {
                var hint = new SkillProgramAiHint(0, 0, 0, 0, 0, 0, false, false);
                if (IsSlashCard(kind) || kind is CardKind.Duel or CardKind.FireAttack or CardKind.BarbarianAssault or CardKind.ArrowBarrage)
                    hint = hint with { TargetDamage = 1, TargetHealthEffects = [new(PublicHealthEffectKind.Damage, 1)] };
                else if (kind is CardKind.Peach or CardKind.PeachGarden or CardKind.Zong) hint = hint with { TargetRecovery = 1 };
                else if (kind is CardKind.DrawTwo or CardKind.FiveGrains) hint = hint with { TargetDraw = kind == CardKind.DrawTwo ? 2 : 1 };
                else if (kind is CardKind.Snatch or CardKind.Dismantlement) hint = hint with { TargetValueAdjustment = -8 };
                else if (kind is CardKind.Indulgence or CardKind.SupplyShortage or CardKind.Intoxication) hint = hint with { TargetValueAdjustment = -12 };
                else if (kind == CardKind.IronChain) hint = hint with { TargetValueAdjustment = snapshot.Players.Single(x => x.Seat == t).IsChained ? 6 : -6 };
                else if (EquipmentCatalog.IsEquipment(kind)) hint = hint with { TargetValueAdjustment = 12 };
                else if (kind is CardKind.Alcohol or CardKind.RealgarWine) hint = hint with { TargetValueAdjustment = 5 };
                return brain.ScoreProgramTarget(snapshot, t, hint);
            });
        }
        double Score(PromptChoice c)
        {
            if (c.Parameters.GetValueOrDefault("branch") == "cancel") return 0;
            if (!Enum.TryParse<CardKind>(c.Parameters.GetValueOrDefault("output-kind"), out var kind)) return -1000;
            if (c.Cards is [var id] && own.ContainsKey(id))
            {
                var plans = DiouNativePlans(_players[f.OwnerSeat], kind, DiouSource(f));
                var reward = !DiouWasShownThisTurn(id) || ProgramBasicCardName(kind) == ProgramBasicCardName(f.Diou!.Claim.EffectiveKind) ? 28 : 0;
                return plans.Select(plan => PlanValue(kind, plan.Targets)).DefaultIfEmpty(0).Max() + reward;
            }
            return PlanValue(kind, c.Targets);
        }
        return p.Choices.OrderByDescending(Score).First();
    }
}
