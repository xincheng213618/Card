using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShenDaTuoYu = "anniversary-god:tuoyu", ShenDaXianjin = "anniversary-god:xianjin",
        ShenDaQijing = "anniversary-god:qijing", ShenDaCuixin = "anniversary-god:cuixin";
    private static CardConversionSource ShenDaSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private ShenDengAiDomain[] ShenDaDeveloped(int owner) => ProgramEventHistory<ShenDengAiDevelopedEvent>()
        .Where(e => e.OwnerSeat == owner).Select(e => e.Domain).Distinct().ToArray();
    private int ShenDaDamageCount(int owner) => ProgramEventHistory<DamageAppliedEvent>().Count(e => e.Amount > 0 &&
        (e.TargetSeat == owner || !e.SourceLess && e.SourceSeat == owner));
    private bool ShenDaAwakened(int owner) => ProgramEventHistory<ShenDengAiAwakenedEvent>().Any(e => e.OwnerSeat == owner);
    private ShenDengAiAssignedEvent? ShenDaAssignment(int owner, int card, bool beforePayment = false)
    {
        var a = ProgramEventHistory<ShenDengAiAssignedEvent>().LastOrDefault(e => e.OwnerSeat == owner && e.CardId == card);
        if (a is null) return null;
        var exits = _cardMovements.Where(m => m.CardId == card && m.Sequence > a.MovementSequence && m.From == CardLocation.Hand(owner) && m.To != m.From).ToArray();
        if (exits.Length == 0) return a;
        // A declared use is captured immediately before or after its own real hand payment.
        return beforePayment && exits.Length == 1 && exits[0].To == CardLocation.Processing && exits[0].Reason.Value == "card.use" &&
            !_cardMovements.Any(m => m.CardId == card && m.Sequence > exits[0].Sequence) ? a : null;
    }
    private int[] ShenDaUnassigned(int owner) => GetHand(_players[owner]).Where(c => ShenDaAssignment(owner, c.Id) is null).Select(c => c.Id).ToArray();
    private CardSnapshot ProjectShenDengAiHandCard(CharacterState owner, Card card, CardSnapshot snapshot) =>
        HasRuntimeSkill(owner, ShenDaTuoYu) && ShenDaAssignment(owner.Seat, card.Id) is { } a
            ? snapshot with { SkillMarkLabel = ShenDaDomainName(a.Domain) } : snapshot;
    private static string ShenDaDomainName(ShenDengAiDomain d) => d switch { ShenDengAiDomain.Qingqu => "清渠", ShenDengAiDomain.Fengtian => "丰田", _ => "峻山" };

    [ProgramCapability]
    private sealed class ShenDengAiCapability(GameEngine e) : IShenDengAiHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => e.BeginShenDa(f, op); }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 38500)]
    private bool CanOfferShenDengAi(ProgramTriggerLegalityContext g)
    {
        if (g.SkillId == ShenDaTuoYu) return g.Trigger.Id == "initialize-domains" || ShenDaDeveloped(g.OwnerSeat).Length > 0 && ShenDaUnassigned(g.OwnerSeat).Length > 0;
        if (g.SkillId == ShenDaXianjin)
        {
            var count = ShenDaDamageCount(g.OwnerSeat);
            return count > 0 && count % 2 == 0 &&
                !ProgramEventHistory<ShenDengAiXianjinReservedEvent>().Any(e => e.OwnerSeat == g.OwnerSeat && e.DamageCount >= count) &&
                (g.Context.SourceSeat == g.OwnerSeat || g.Context.TargetSeat == g.OwnerSeat);
        }
        if (g.SkillId == ShenDaQijing) return !ShenDaAwakened(g.OwnerSeat) && ShenDaDeveloped(g.OwnerSeat).Length == 3;
        if (g.SkillId == ShenDaCuixin) return ShenDaCuixinOriginal(g.Context, g.OwnerSeat) is { } use && ShenDaRepeatPlans(g.OwnerSeat, use.CardKind, use.TargetSeats).Count > 0;
        return true;
    }
    private SkillProgramStepOutcome BeginShenDa(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.WindowContext is not { } w || f.ShenDengAi is not null) throw new InvalidOperationException("Shen Deng Ai requires its unpaid native program occurrence.");
        if (op == SkillProgramEffectOp.InitializeShenDengAiTuoYu)
        { AdvanceEventRulesAndQueueFact(new ShenDengAiInitializedEvent(f.Id, f.OwnerSeat)); return SkillProgramStepOutcome.Continue; }
        if (op == SkillProgramEffectOp.AwakenShenDengAiQijing && f.InstructionIndex == 1)
        { AdvanceEventRulesAndQueueFact(new ShenDengAiAwakenedEvent(f.Id, f.OwnerSeat)); return SkillProgramStepOutcome.Continue; }
        var stage = op switch {
            SkillProgramEffectOp.AllocateShenDengAiTuoYu => ShenDengAiStage.Allocate,
            SkillProgramEffectOp.DevelopShenDengAiXianjin => ShenDengAiStage.Develop,
            SkillProgramEffectOp.AwakenShenDengAiQijing => ShenDengAiStage.MoveSeat,
            _ => ShenDengAiStage.RepeatChoice };
        var r = new ShenDengAiReceipt(ShenDaSource(f), f.GameplayHash, w.ParentFrameId, f.InstructionIndex, stage,
            DamageCount: stage == ShenDengAiStage.Develop ? ShenDaDamageCount(f.OwnerSeat) : 0);
        if (stage == ShenDengAiStage.RepeatChoice)
        {
            var original = ShenDaCuixinOriginal(w, f.OwnerSeat) ?? throw new InvalidOperationException("Cuixin lost its genuine completed hand Use.");
            r = r with { OriginalUseId = original.Id, OriginalActionId = original.Action!.ActionId, OutputKind = original.CardKind };
        }
        ReplaceRuntimeTop(f = f with { ShenDengAi = r });
        if (stage == ShenDengAiStage.Develop)
        {
            AdvanceEventRulesAndQueueFact(new ShenDengAiXianjinReservedEvent(f.Id, f.OwnerSeat, r.DamageCount));
            if (ShenDaDeveloped(f.OwnerSeat).Length == 3) { DrawShenDaXianjin(f); return SkillProgramStepOutcome.AwaitChild; }
        }
        ContinueShenDa(f.Id); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> ShenDaChoices(ProgramSkillFrame f)
    {
        var r = f.ShenDengAi!;
        PromptChoice Choice(string key, string label, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) =>
            new(new("shen-deng-ai." + f.Id + "." + key), label, cards ?? [], targets ?? [], new Dictionary<string, string> {
                ["program-action"] = "shen-deng-ai", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["option"] = key });
        if (r.Stage == ShenDengAiStage.Allocate)
        {
            var id = ShenDaUnassigned(f.OwnerSeat).FirstOrDefault();
            return id == 0 ? [] : ShenDaDeveloped(f.OwnerSeat).Select(d => Choice("assign:" + (int)d, "将当前手牌分配至“" + ShenDaDomainName(d) + "”", [id])).ToArray();
        }
        if (r.Stage == ShenDengAiStage.Develop) return Enum.GetValues<ShenDengAiDomain>().Except(ShenDaDeveloped(f.OwnerSeat))
            .Select(d => Choice("develop:" + (int)d, "开发“" + ShenDaDomainName(d) + "”")).ToArray();
        if (r.Stage == ShenDengAiStage.MoveSeat)
        {
            var others = Enumerable.Range(0, _playerCount).Select(i => TableStepSeat(f.OwnerSeat, i)).Where(s => s != f.OwnerSeat && _players[s].IsAlive).ToArray();
            return others.Length < 2 ? [] : others.Select((s, i) => Choice("seat:" + s + ":" + others[(i + 1) % others.Length],
                "移至“" + _players[s].Name + "”与“" + _players[others[(i + 1) % others.Length]].Name + "”之间", targets: [s, others[(i + 1) % others.Length]])).ToArray();
        }
        if (r.Stage == ShenDengAiStage.RepeatChoice)
        {
            var original = LifecycleCardUse(r.OriginalUseId);
            if (original is null) return [];
            return ShenDaRepeatPlans(f.OwnerSeat, r.OutputKind!.Value, original.TargetSeats).Select(p => Choice(p.Id, p.Description, targets: p.TargetSeats))
                .Append(Choice("skip", "不发动摧心")).ToArray();
        }
        return [];
    }
    private void PublishShenDa(ProgramSkillFrame f)
    {
        var choices = ShenDaChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, skill.Name + "：请选择。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("shen-deng-ai")]
    private void AnswerShenDa(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenDengAi: { } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !ShenDaChoices(f).Any(c => AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Shen Deng Ai requires its original private chooser and current legal option.");
        AssertShenDa(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var parts = choice.Parameters["option"].Split(':');
        if (r.Stage == ShenDengAiStage.Allocate)
        {
            AdvanceEventRulesAndQueueFact(new ShenDengAiAssignedEvent(f.Id, f.OwnerSeat, choice.Cards.Single(), (ShenDengAiDomain)int.Parse(parts[1], CultureInfo.InvariantCulture), _movementSequence));
            ContinueShenDa(f.Id); return;
        }
        if (r.Stage == ShenDengAiStage.Develop)
        {
            AdvanceEventRulesAndQueueFact(new ShenDengAiDevelopedEvent(f.Id, f.OwnerSeat, (ShenDengAiDomain)int.Parse(parts[1], CultureInfo.InvariantCulture)));
            DrawShenDaXianjin(f); return;
        }
        if (r.Stage == ShenDengAiStage.MoveSeat)
        {
            MoveTableSeatBetween(f.OwnerSeat, choice.Targets[0], choice.Targets[1], f.Id); PendProgramExtraTurn(f, null); FinishShenDa(f); return;
        }
        if (r.Stage == ShenDengAiStage.RepeatChoice)
        {
            if (parts[0] == "skip") { FinishShenDa(f); return; }
            IssueShenDaCuixin(f, choice.Parameters["option"]); return;
        }
        throw new InvalidOperationException("Shen Deng Ai cannot answer an already issued native child.");
    }
    private void DrawShenDaXianjin(ProgramSkillFrame f)
    {
        var count = GetHand(_players[f.OwnerSeat]).Count;
        var draw = _players.Where(p => p.IsAlive).All(p => GetHand(p).Count <= count) ? 1 : ShenDaDeveloped(f.OwnerSeat).Length;
        ReplaceRuntimeTop(f = f with { ShenDengAi = f.ShenDengAi! with { Stage = ShenDengAiStage.DrawAttempt, DrawCount = draw }, PendingMovementContinuation = null });
        BeginOperationProgramDraw(f, f.OwnerSeat, draw, new("skill-program.shen-deng-ai.xianjin.draw"));
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.DevelopShenDengAiXianjin)]
    private void ReturnShenDaDraw(ProgramSkillFrame supplied, NativeDrawOutcome outcome)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.ShenDengAi!;
        if (r.Stage != ShenDengAiStage.DrawAttempt || f.NativeDraw is not null || outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != r.DrawCount)
            throw new InvalidOperationException("Xianjin lost its once-issued genuine draw request and typed return.");
        ReplaceRuntimeTop(f = f with { ShenDengAi = r with { Stage = ShenDengAiStage.DrawChildren, DrawOutcome = outcome }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new ShenDengAiDrawReturnedEvent(f.Id, f.OwnerSeat, outcome.RequestedCount, outcome.ActualDrawIds.Count, outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId));
        ContinueShenDa(f.Id);
    }
    private void ContinueShenDa(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenDengAi: { } r } f || f.Id != id || _pendingDecision is not null) return;
        AssertShenDa(f);
        if (r.Stage is ShenDengAiStage.NativeUse or ShenDengAiStage.DrawAttempt) return;
        if (r.Stage is ShenDengAiStage.DrawChildren or ShenDengAiStage.NativeChildren)
        {
            if (TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
                TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id)) return;
            FinishShenDa(GetActiveProgramFrame(id)); return;
        }
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishShenDa(f); return; }
        if (ShenDaChoices(f).Count == 0)
        {
            if (r.Stage == ShenDengAiStage.MoveSeat) PendProgramExtraTurn(f, null);
            FinishShenDa(f); return;
        }
        PublishShenDa(f);
    }
    private void FinishShenDa(ProgramSkillFrame f)
    { RestoreShenDaCuixinCursor(f); f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { ShenDengAi = null, PendingMovementContinuation = null }); AdvanceRuntimeProgram(f.Id); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 38500)]
    private bool ResumeShenDa(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenDengAi: not null } f || f.Id != id) return false; ContinueShenDa(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 38500)]
    private bool ReturnShenDaMovement(ProgramSkillFrame f)
    { if (f.ShenDengAi is null) return false; ContinueShenDa(f.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 38500)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 38500)]
    private bool ContinueIssuedShenDa(ProgramSkillFrame f) => f.ShenDengAi is { Stage: ShenDengAiStage.DrawAttempt or ShenDengAiStage.DrawChildren or ShenDengAiStage.NativeUse or ShenDengAiStage.NativeChildren } ||
        f.SkillId == ShenDaQijing && ProgramEventHistory<ShenDengAiAwakenedEvent>().Any(e => e.FrameId == f.Id);
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 38500)]
    private bool ShenDaMovementProof(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) =>
        f.ShenDengAi is { Stage: ShenDengAiStage.DrawChildren or ShenDengAiStage.NativeChildren } && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 38500)]
    private void AssertShenDa(ProgramSkillFrame f)
    {
        if (f.ShenDengAi is not { } r) return;
        if (r.Source != ShenDaSource(f) || r.GameplayHash != f.GameplayHash || r.WindowId != f.WindowContext?.ParentFrameId || r.InstructionIndex != f.InstructionIndex ||
            r.Stage == ShenDengAiStage.DrawChildren && !ProgramEventHistory<ShenDengAiXianjinReservedEvent>().Any(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.DamageCount == r.DamageCount))
            throw new InvalidOperationException("Shen Deng Ai lost its original source, content, owning occurrence or paid damage reservation.");
        if (r.Stage == ShenDengAiStage.DrawChildren && (r.DrawOutcome is not { } draw || f.NativeDraw is not null || draw.RecipientSeat != f.OwnerSeat ||
            draw.RequestedCount != r.DrawCount || draw.SequenceAfter > _movementSequence || draw.SequenceBefore > draw.SequenceAfter ||
            !ProgramEventHistory<ShenDengAiDrawReturnedEvent>().Any(e => e == new ShenDengAiDrawReturnedEvent(f.Id, f.OwnerSeat, draw.RequestedCount,
                draw.ActualDrawIds.Count, draw.SequenceBefore, draw.SequenceAfter, draw.AttemptFrameId)) ||
            !_cardMovements.Where(m => m.Sequence > draw.SequenceBefore && m.Sequence <= draw.SequenceAfter && m.From == CardLocation.DrawPile &&
                m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == "skill-program.shen-deng-ai.xianjin.draw").Select(m => m.CardId).SequenceEqual(draw.ActualDrawIds)))
            throw new InvalidOperationException("Xianjin lost its exact already returned native draw materials.");
        if (r.Stage == ShenDengAiStage.NativeChildren && (!r.Returned || LifecycleCardUse(r.UseId) is not null ||
            ProgramEventHistory<ShenDengAiNativeReturnedEvent>().Count(e => e == new ShenDengAiNativeReturnedEvent(f.Id, r.UseId, r.ActionId)) != 1))
            throw new InvalidOperationException("Cuixin lost its exact once-returned native Use.");
    }
    [ProgramAiChoice(SkillProgramEffectOp.AllocateShenDengAiTuoYu)]
    private PromptChoice ChooseShenDaAllocation(PendingDecision p, ProgramSkillFrame f) => ChooseShenDa(p, f);
    [ProgramAiChoice(SkillProgramEffectOp.DevelopShenDengAiXianjin)]
    private PromptChoice ChooseShenDaDevelopment(PendingDecision p, ProgramSkillFrame f) => ChooseShenDa(p, f);
    [ProgramAiChoice(SkillProgramEffectOp.AwakenShenDengAiQijing)]
    private PromptChoice ChooseShenDaAwakening(PendingDecision p, ProgramSkillFrame f) => ChooseShenDa(p, f);
    [ProgramAiChoice(SkillProgramEffectOp.RepeatShenDengAiCuixin)]
    private PromptChoice ChooseShenDaRepeat(PendingDecision p, ProgramSkillFrame f) => ChooseShenDa(p, f);
    private PromptChoice ChooseShenDa(PendingDecision p, ProgramSkillFrame f) => p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("option") != "skip") ?? p.Choices.First();
}
