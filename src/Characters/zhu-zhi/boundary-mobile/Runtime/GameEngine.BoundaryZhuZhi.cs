using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string BoundaryAnguoAction = "boundary-zhu-zhi-anguo";
    private static CardConversionSource BoundaryAnguoSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);
    private bool HasBoundaryAnguoMark(int seat) => IsValidPlayerSeat(seat) && _players[seat].IsAlive &&
        _players[seat].Markers.GetValueOrDefault(PlayerMarkerKind.MobileBoundaryAnguo) > 0;
    private bool HadBoundaryAnguoMark(int seat) => ProgramEventHistory<PlayerMarkerChangedEvent>().Any(e =>
        e.Marker == PlayerMarkerKind.MobileBoundaryAnguo && e.PlayerSeat == seat && e.Delta > 0);
    private int[] BoundaryAnguoNewRecipients(int owner) => _players.Where(p => p.IsAlive && p.Seat != owner &&
        !HadBoundaryAnguoMark(p.Seat)).Select(p => p.Seat).ToArray();

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 53200)]
    private bool CanOfferBoundaryAnguo(ProgramTriggerLegalityContext gate)
    {
        if (gate.SkillId != BoundaryZhuZhiComposition.Skill) return true;
        var context = gate.Context;
        return gate.Trigger.Id switch
        {
            "initial-mark" => _players.Any(p => p.IsAlive && p.Seat != gate.OwnerSeat),
            "move-mark" => _players.Any(p => HasBoundaryAnguoMark(p.Seat)) && BoundaryAnguoNewRecipients(gate.OwnerSeat).Length > 0,
            "prevent-lethal" => context.TargetSeat == gate.OwnerSeat && gate.Owner.HasHpValue && context.Amount >= gate.Owner.Hp &&
                _players.Any(p => HasBoundaryAnguoMark(p.Seat)) && (context.SourceSeat is not { } source || !HasBoundaryAnguoMark(source)),
            "rescue-marked" => context.TargetSeat is { } victim && HasBoundaryAnguoMark(victim) && _players[victim].Hp <= 0 ||
                _resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.OwnerSeat == gate.OwnerSeat && f.SkillId == gate.SkillId &&
                    f.SkillInstanceId == gate.Candidate.SkillInstanceId && f.WindowContext == context && HasExactBoundaryAnguoRescueTarget(f)),
            _ => false
        };
    }

    [ProgramCapability]
    private sealed class BoundaryZhuZhiHost(GameEngine engine) : IBoundaryZhuZhiHost
    {
        public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation) =>
            operation == SkillProgramEffectOp.PayBoundaryZhuZhiAnguo ? engine.BeginBoundaryAnguoPayment(frame) :
                engine.BeginBoundaryAnguo(frame, operation == SkillProgramEffectOp.BeginBoundaryZhuZhiRescue);
    }

    private SkillProgramStepOutcome BeginBoundaryAnguo(ProgramSkillFrame supplied, bool rescue)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.SkillId != BoundaryZhuZhiComposition.Skill || frame.InstructionIndex != 1 || frame.BoundaryZhuZhi is not null ||
            frame.WindowContext is not { } context) throw new InvalidOperationException("Anguo needs its original mark or dying-entry binding.");
        var victim = -1; long dyingId = 0;
        if (rescue)
        {
            if (frame.TriggerId != "rescue-marked" || !ExactDyingOwnedCardEntry(frame, out var dying, out _) || !HasBoundaryAnguoMark(dying.VictimSeat))
                throw new InvalidOperationException("Anguo rescue requires its exact still-marked original dying participant.");
            victim = dying.VictimSeat; dyingId = dying.Id;
        }
        else if (frame.TriggerId is not ("initial-mark" or "move-mark"))
            throw new InvalidOperationException("Anguo marks require the game-start or owner's Play-start binding.");
        var receipt = new BoundaryZhuZhiReceipt(BoundaryAnguoSource(frame), frame.GameplayHash, context.ParentFrameId,
            frame.TriggerId!, rescue ? BoundaryZhuZhiStage.Recovery : BoundaryZhuZhiStage.MarkTarget, victim, dyingId);
        ReplaceRuntimeTop(frame = frame with { BoundaryZhuZhi = receipt,
            SelectedTargetSeats = rescue ? Array.AsReadOnly(new[] { victim }) : frame.SelectedTargetSeats });
        AdvanceEventRulesAndQueueFact(new BoundaryZhuZhiStartedEvent(frame.Id, receipt.Source, receipt.GameplayHash,
            receipt.WindowId, receipt.Binding, victim, dyingId));
        if (!rescue) { PublishBoundaryAnguo(frame); return SkillProgramStepOutcome.AwaitChoice; }
        var count = _players[victim].Markers.GetValueOrDefault(PlayerMarkerKind.MobileBoundaryAnguo);
        MutateParticipantMarker(frame, victim, PlayerMarkerKind.MobileBoundaryAnguo, -count);
        AdvanceEventRulesAndQueueFact(new BoundaryZhuZhiRescueIssuedEvent(frame.Id, victim, dyingId, count));
        return SkillProgramStepOutcome.Continue;
    }

    private PromptChoice BoundaryAnguoChoice(ProgramSkillFrame frame, string branch, int previous, int target, string label) =>
        new(new($"anguo.{frame.Id}.{branch}.{previous}.{target}"), label, [], target < 0 ? [] : [target],
            new Dictionary<string,string> { ["program-action"] = BoundaryAnguoAction, ["branch"] = branch,
                ["previous"] = previous.ToString(CultureInfo.InvariantCulture), ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture) });
    private IReadOnlyList<PromptChoice> BoundaryAnguoChoices(ProgramSkillFrame frame)
    {
        var receipt = frame.BoundaryZhuZhi!; var choices = new List<PromptChoice>();
        if (receipt.Stage == BoundaryZhuZhiStage.MarkTarget)
        {
            var sources = receipt.Binding == "initial-mark" ? new[] { -1 } : _players.Where(p => HasBoundaryAnguoMark(p.Seat)).Select(p => p.Seat).ToArray();
            var targets = receipt.Binding == "initial-mark" ? _players.Where(p => p.IsAlive && p.Seat != frame.OwnerSeat).Select(p => p.Seat).ToArray()
                : BoundaryAnguoNewRecipients(frame.OwnerSeat);
            foreach (var source in sources)
                foreach (var target in targets)
                    choices.Add(BoundaryAnguoChoice(frame, "mark", source, target, source < 0
                        ? $"令 {_players[target].Name} 获得“安国”" : $"将 {_players[source].Name} 的“安国”移至 {_players[target].Name}"));
        }
        else if (receipt.Stage == BoundaryZhuZhiStage.PaymentChoice)
        {
            var owner = _players[frame.OwnerSeat];
            if (owner.IsAlive && owner.HasHpValue && owner.Hp > 1)
                choices.Add(BoundaryAnguoChoice(frame, "hp", -1, -1, $"失去 {owner.Hp - 1} 点体力，体力降至1"));
            if (owner.IsAlive && owner.MaxHp > 1)
                choices.Add(BoundaryAnguoChoice(frame, "maximum", -1, -1, $"减少 {owner.MaxHp - 1} 点体力上限，上限降至1"));
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishBoundaryAnguo(ProgramSkillFrame frame)
    {
        var choices = BoundaryAnguoChoices(frame);
        if (choices.Count == 0)
        {
            ReplaceRuntimeTop(frame = frame with { BoundaryZhuZhi = frame.BoundaryZhuZhi! with { Stage = BoundaryZhuZhiStage.Paid } });
            AdvanceRuntimeProgram(frame.Id); return;
        }
        ReplaceRuntimeTop(frame = frame with { Step = ResolutionFrameStep.AwaitingResponse });
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat,
            frame.BoundaryZhuZhi!.Stage == BoundaryZhuZhiStage.MarkTarget ? "安国：选择本局从未获得过标记的其他角色。" : "安国：受救角色已回复，选择自身代价。",
            [], Array.AsReadOnly(choices.SelectMany(c => c.Targets).Distinct().ToArray()), frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer(BoundaryAnguoAction)]
    private void AnswerBoundaryAnguo(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { BoundaryZhuZhi: { } receipt } frame ||
            !ValidBoundaryAnguo(frame) || _pendingDecision?.PlayerSeat != frame.OwnerSeat ||
            !AssistedChoicesEqual([choice], BoundaryAnguoChoices(frame).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Anguo requires its actual fresh recipient or currently payable original rescue choice.");
        ClearPendingDecision(); ReplaceRuntimeTop(frame = frame with { Step = ResolutionFrameStep.ResolvingEffect });
        if (receipt.Stage == BoundaryZhuZhiStage.MarkTarget)
        {
            var previous = int.Parse(choice.Parameters["previous"], CultureInfo.InvariantCulture); var target = choice.Targets.Single();
            if (previous >= 0) MutateParticipantMarker(frame, previous, PlayerMarkerKind.MobileBoundaryAnguo, -1);
            MutateParticipantMarker(frame, target, PlayerMarkerKind.MobileBoundaryAnguo, 1);
            AdvanceEventRulesAndQueueFact(new BoundaryZhuZhiMarkEvent(frame.Id, receipt.Source, previous, target));
            ReplaceRuntimeTop(frame with { BoundaryZhuZhi = receipt with { Stage = BoundaryZhuZhiStage.Paid } });
            AdvanceRuntimeProgram(frame.Id); return;
        }
        var owner = _players[frame.OwnerSeat]; var branch = choice.Parameters["branch"];
        ReplaceRuntimeTop(frame = frame with { BoundaryZhuZhi = receipt with { Payment = branch, Stage = BoundaryZhuZhiStage.Paying,
            HpBefore = owner.Hp, MaximumBefore = owner.MaxHp, Requested = branch == "hp" ? owner.Hp - 1 : owner.MaxHp - 1 } });
        ApplyBoundaryAnguoPayment(frame);
    }

    private SkillProgramStepOutcome BeginBoundaryAnguoPayment(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (!ValidBoundaryAnguo(frame) || frame.InstructionIndex != 3 || frame.BoundaryZhuZhi is not { Stage: BoundaryZhuZhiStage.Recovery } receipt)
            throw new InvalidOperationException("Anguo payment must follow its original native recovery exactly once.");
        ReplaceRuntimeTop(frame = frame with { BoundaryZhuZhi = receipt with { Stage = BoundaryZhuZhiStage.PaymentChoice } });
        PublishBoundaryAnguo(frame); return SkillProgramStepOutcome.AwaitChoice;
    }
    private void ApplyBoundaryAnguoPayment(ProgramSkillFrame frame)
    {
        var receipt = frame.BoundaryZhuZhi!;
        if (receipt.Stage != BoundaryZhuZhiStage.Paying || receipt.Requested <= 0) throw new InvalidOperationException("Anguo lost its affordable original payment.");
        if (receipt.Payment == "hp")
        {
            var result = ApplyProgramHpLoss(frame.Id, frame.SkillId, frame.OwnerSeat, receipt.Requested,
                (_, _, _) => RecordBoundaryAnguoPayment(frame.Id));
            if (result == SkillProgramStepOutcome.AwaitChild) return;
        }
        else
        {
            ChangeProgramMaximumHp(frame, -receipt.Requested);
            if (_resolutionStack.LastOrDefault()?.Id != frame.Id || _pendingDecision is not null) return;
            RecordBoundaryAnguoPayment(frame.Id);
        }
        ContinueBoundaryAnguoPayment(frame.Id);
    }
    private void RecordBoundaryAnguoPayment(long id)
    {
        var frame = GetActiveProgramFrame(id); var receipt = frame.BoundaryZhuZhi!; var owner = _players[frame.OwnerSeat];
        if (receipt.Stage != BoundaryZhuZhiStage.Paying) throw new InvalidOperationException("Anguo cannot repeat a completed payment.");
        ReplaceRuntimeTop(frame with { BoundaryZhuZhi = receipt with { Stage = BoundaryZhuZhiStage.PaymentChildren },
            PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new BoundaryZhuZhiPaymentEvent(id, frame.OwnerSeat, receipt.Payment!, receipt.HpBefore,
            owner.Hp, receipt.MaximumBefore, owner.MaxHp));
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 53200)]
    private bool ContinueBoundaryAnguoPayment(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { BoundaryZhuZhi: { } receipt } frame || frame.Id != id ||
            receipt.Stage is not (BoundaryZhuZhiStage.Paying or BoundaryZhuZhiStage.PaymentChildren)) return false;
        if (!ValidBoundaryAnguo(frame)) throw new InvalidOperationException("Anguo lost its original issued rescue payment.");
        if (receipt.Stage == BoundaryZhuZhiStage.Paying) { ApplyBoundaryAnguoPayment(frame); return true; }
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginAdvancedSkillsChanged(id) || TryBeginCardsMovedProgramWindow(id)) return true;
        ReplaceRuntimeTop(frame = GetActiveProgramFrame(id) with { BoundaryZhuZhi = receipt with { Stage = BoundaryZhuZhiStage.Paid }, PendingMovementContinuation = null });
        AdvanceRuntimeProgram(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 53200)]
    private bool ReturnBoundaryAnguoPayment(ProgramSkillFrame frame) => frame.BoundaryZhuZhi is not null && ContinueBoundaryAnguoPayment(frame.Id);
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 53200)]
    private bool IsBoundaryAnguoPayment(ProgramSkillFrame frame, SkillProgramEffect? effect, ProgramMovementContinuation pending)
    {
        if (effect?.Op != SkillProgramEffectOp.PayBoundaryZhuZhiAnguo || frame.PendingMovementContinuation != pending ||
            pending.SubjectSeat != frame.OwnerSeat || pending.BeforeCount != 0 || pending.CoverageResultBind is not null ||
            frame.BoundaryZhuZhi is not { Stage: BoundaryZhuZhiStage.PaymentChildren } receipt || !ValidBoundaryAnguo(frame)) return false;
        var payments = ProgramEventHistory<BoundaryZhuZhiPaymentEvent>().Where(e => e.FrameId == frame.Id &&
            e.Owner == frame.OwnerSeat && e.Choice == receipt.Payment && e.HpBefore == receipt.HpBefore &&
            e.MaximumBefore == receipt.MaximumBefore).ToArray();
        if (payments is not [var payment]) return false;
        if (receipt.Payment == "hp")
            return receipt.Requested == receipt.HpBefore - 1 && payment.HpAfter == 1 && payment.MaximumAfter == receipt.MaximumBefore &&
                ProgramEventHistory<ProgramSkillHpLostEvent>().Count(loss => loss.FrameId == frame.Id && loss.SkillId == frame.SkillId &&
                    loss.TargetSeat == frame.OwnerSeat && loss.Amount == receipt.Requested && loss.RemainingHp == 1) == 1;
        return receipt.Payment == "maximum" && receipt.Requested == receipt.MaximumBefore - 1 &&
            payment.MaximumAfter == 1 && payment.HpAfter == Math.Min(1, receipt.HpBefore);
    }
    private bool ValidBoundaryAnguo(ProgramSkillFrame frame) => frame.BoundaryZhuZhi is { } receipt &&
        frame.SkillId == BoundaryZhuZhiComposition.Skill && receipt.Source == BoundaryAnguoSource(frame) && receipt.GameplayHash == frame.GameplayHash &&
        receipt.WindowId == frame.WindowContext?.ParentFrameId && receipt.Binding == frame.TriggerId &&
        (receipt.Binding != "rescue-marked" || frame.SelectedTargetSeats.SequenceEqual([receipt.Victim]) &&
            ExactDyingOwnedCardEntry(frame, out var dying, out _) && dying.Id == receipt.DyingId && dying.VictimSeat == receipt.Victim) &&
        ProgramEventHistory<BoundaryZhuZhiStartedEvent>().Count(e => e.FrameId == frame.Id && e.Source == receipt.Source &&
            e.GameplayHash == receipt.GameplayHash && e.WindowId == receipt.WindowId && e.Binding == receipt.Binding && e.Victim == receipt.Victim && e.DyingId == receipt.DyingId) == 1;

    private bool HasExactBoundaryAnguoRescueTarget(ProgramSkillFrame frame)
    {
        if (frame.BoundaryZhuZhi is not { Binding: "rescue-marked" } receipt || frame.TriggerId != "rescue-marked" ||
            frame.InstructionIndex is < 1 or > 4 || frame.SelectedCardIds.Count != 0 || !ValidBoundaryAnguo(frame)) return false;
        var issues = ProgramEventHistory<BoundaryZhuZhiRescueIssuedEvent>().Where(e => e.FrameId == frame.Id &&
            e.Victim == receipt.Victim && e.DyingId == receipt.DyingId && e.RemovedMarks > 0).ToArray();
        if (issues is not [var issue]) return false;
        var removals = ProgramEventHistory<PlayerMarkerChangedEvent>().Where(e => e.ResolutionId == frame.Id &&
            e.PlayerSeat == receipt.Victim && e.Marker == PlayerMarkerKind.MobileBoundaryAnguo && e.Delta < 0).ToArray();
        return removals.Length > 0 && removals.Sum(e => -e.Delta) == issue.RemovedMarks && removals[^1].Count == 0;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 53200)]
    private void AssertBoundaryAnguo(ProgramSkillFrame frame)
    { if (frame.BoundaryZhuZhi is not null && !ValidBoundaryAnguo(frame)) throw new InvalidOperationException("Anguo changed its original mark or rescue frame."); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 53200)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 53200)]
    private bool KeepIssuedBoundaryAnguo(ProgramSkillFrame frame) => frame.BoundaryZhuZhi is { Binding:"rescue-marked" } && ValidBoundaryAnguo(frame);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveBoundaryZhuZhiAnguo, SkillProgramEffectOp.PayBoundaryZhuZhiAnguo)]
    private PromptChoice BoundaryAnguoAi(PendingDecision decision, ProgramSkillFrame frame) => decision.Choices.OrderByDescending(c =>
        c.Targets.Count == 1 ? _aiBrains[frame.OwnerSeat].ScoreProgramTarget(CreateSnapshot(frame.OwnerSeat), c.Targets[0], new(0,0,0,1,0,0,false,false)) :
        c.Parameters.GetValueOrDefault("branch") == "hp" ? 1 : 0).First();

    private RuleQueryEvaluation ApplyBoundaryZhuZhiHandLimit(CharacterState player, RuleQueryEvaluation ordinary)
    {
        if (!HasBoundaryAnguoMark(player.Seat)) return ordinary;
        var set = new FiniteRuleQueryContribution($"marker:{player.Seat}:boundary:anguo-mobile:maximum-hp", SkillRuleOperation.Set, player.MaxHp, 53200);
        var terms = Array.AsReadOnly(ordinary.Value.Contributions.Append(set).OrderBy(c => c.SourceId, StringComparer.Ordinal).ToArray());
        var stage = new RuleQueryStageSnapshot("anguo-maximum-hp", ordinary.Value is FiniteRuleQueryValue finite ? finite.Value : 0,
            false, player.MaxHp, Array.AsReadOnly<RuleQueryContribution>([set]));
        return new(new FiniteRuleQueryValue(player.MaxHp, terms), ordinary.BaseTerms, Array.AsReadOnly(ordinary.Stages.Append(stage).ToArray()));
    }
    private IReadOnlyList<ProgramPublicRuleStateSnapshot> BoundaryZhuZhiPublicStates(CharacterState owner, string skillId)
    {
        if (skillId != BoundaryZhuZhiComposition.Skill) return [];
        var marked = _players.Count(p => HasBoundaryAnguoMark(p.Seat));
        var unused = BoundaryAnguoNewRecipients(owner.Seat).Length;
        return GetSkillBindingShard(owner).ProgramInstances.Where(i => i.SkillId == skillId).SelectMany(i => new[]
        {
            new ProgramPublicRuleStateSnapshot(i.SkillInstanceId, "anguo-marked", ProgramPublicRuleStateKind.GameActivationProgress, marked),
            new ProgramPublicRuleStateSnapshot(i.SkillInstanceId, "anguo-unused", ProgramPublicRuleStateKind.GameActivationProgress, unused)
        }).ToArray();
    }
}
