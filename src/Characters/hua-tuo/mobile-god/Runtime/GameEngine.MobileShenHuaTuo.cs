using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MobileHtWuling = "mobile-god:wuling", MobileHtYouyi = "mobile-god:youyi",
        MobileHtEffects = "mobile-god:wuling-effects", MobileHtDeer = "mobile-god:wuling-deer";
    // Durable private courses, rather than pending choices. Commands rebuild
    // these immutable accepted orders on replay; public facts reveal no future animal.
    private readonly Dictionary<long, MobileWulingCourse> _mobileWulingCourses = [];
    private static CardConversionSource MobileHtSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private MobileWulingTaughtEvent? MobileHtCourse(int seat) => ProgramEventHistory<MobileWulingTaughtEvent>().LastOrDefault(c => c.TargetSeat == seat &&
        ProgramEventHistory<MobileWulingAdvancedEvent>().LastOrDefault(e => e.CourseId == c.CourseId)?.Active == true);
    private int MobileHtCourseIndex(long id) => ProgramEventHistory<MobileWulingAdvancedEvent>().Last(e => e.CourseId == id).Index;
    private MobileWulingAnimal[] MobileHtCourseOrder(long id) => _mobileWulingCourses.TryGetValue(id, out var c)
        ? c.Order.ToArray() : throw new InvalidOperationException("Wuling lost its private accepted persistent course.");
    private bool MobileHtSpirit(int seat, MobileWulingAnimal animal) => HasRuntimeSkill(_players[seat], MobileHtEffects) &&
        MobileHtCourse(seat) is { } c && MobileHtCourseOrder(c.CourseId)[MobileHtCourseIndex(c.CourseId)] == animal;
    private int[] MobileHtTeachingTargets() => _players.Where(p => p.IsAlive && MobileHtCourse(p.Seat) is null &&
        p.Markers.GetValueOrDefault(PlayerMarkerKind.MobileWuling) == 0).OrderBy(p => RelativeTableOrder(_currentSeat, p.Seat)).Select(p => p.Seat).ToArray();
    private DiscardRecoveryEntity[] MobileHtDiscarded(int owner)
    {
        if (CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Discard } phase || phase.ActorSeat != owner) return [];
        var invocations = ProgramEventHistory<NativeLogicalDiscardInvocationRecordedEvent>().Where(e => e.Phase == phase)
            .Select(e => e.InvocationId).ToHashSet();
        return ProgramEventHistory<NativeLogicalDiscardMaterialCompletedEvent>().Where(e => invocations.Contains(e.InvocationId))
            .OrderBy(e => e.CompletionSequence).Select(e => new DiscardRecoveryEntity(e.CardId, e.CompletionSequence, e.From))
            .Where(ActualDiscardEntityStillAvailable).DistinctBy(e => e.CardId).ToArray();
    }
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeOwnerFacts, 39100)]
    private bool CanActivateMobileHt(ProgramActivationLegalityContext gate) => gate.Program.Id switch
    {
        MobileHtWuling => MobileHtTeachingTargets().Length > 0,
        MobileHtYouyi => _cardZones.CardsAt(CardLocation.Benevolence).Count > 0,
        _ => true
    };
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 39100)]
    private bool CanTriggerMobileHt(ProgramTriggerLegalityContext gate) => gate.SkillId switch
    {
        MobileHtYouyi => MobileHtDiscarded(gate.OwnerSeat).Length > 0,
        MobileHtEffects => gate.Trigger.Id == "bear-first-damage" ? MobileHtSpirit(gate.OwnerSeat, MobileWulingAnimal.Bear) : MobileHtCourse(gate.OwnerSeat) is not null,
        _ => true
    };
    [ProgramCapability]
    private sealed class MobileHuaTuoCapability(GameEngine engine) : IMobileHuaTuoHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op) => engine.BeginMobileHt(frame, op); }
    private SkillProgramStepOutcome BeginMobileHt(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MobileHuaTuo is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0)
            throw new InvalidOperationException("Mobile Hua Tuo requires its original unpaid activation or native trigger.");
        var r = new MobileHuaTuoReceipt(MobileHtSource(f), f.GameplayHash, op, f.InstructionIndex, MobileHuaTuoStage.Children);
        if (op == SkillProgramEffectOp.TeachMobileShenHuaTuoWuling)
        {
            if (f.TriggerId is not null || f.SkillId != MobileHtWuling || MobileHtTeachingTargets().Length == 0) throw new InvalidOperationException("Wuling requires a living unmarked recipient.");
            r = r with { Stage = MobileHuaTuoStage.Recipient, CourseId = f.Id };
        }
        else if (op == SkillProgramEffectOp.AdvanceMobileShenHuaTuoWuling)
        {
            if (f.SkillId != MobileHtEffects || f.WindowContext?.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || MobileHtCourse(f.OwnerSeat) is not { } course)
                throw new InvalidOperationException("Wuling rotation requires its actual recipient's preparation.");
            r = r with { CourseId = course.CourseId, TargetSeat = course.TargetSeat, Order = MobileHtCourseOrder(course.CourseId),
                AnimalIndex = MobileHtCourseIndex(course.CourseId) + 1, Stage = MobileHuaTuoStage.Entry };
        }
        else if (op == SkillProgramEffectOp.DepositMobileShenHuaTuoYouyi)
        {
            if (f.SkillId != MobileHtYouyi || f.WindowContext?.Window != SkillProgramTriggerWindow.DiscardPhaseEnded)
                throw new InvalidOperationException("Youyi requires its original actually executed discard phase.");
            r = r with { Cards = MobileHtDiscarded(f.OwnerSeat).Select(e => e.CardId).ToArray(), Before = _movementSequence };
        }
        else
        {
            if (f.SkillId != MobileHtYouyi || f.TriggerId is not null || _cardZones.CardsAt(CardLocation.Benevolence).Count == 0)
                throw new InvalidOperationException("Youyi requires the actual entire nonempty shared Ren area.");
            r = r with { Cards = _cardZones.CardsAt(CardLocation.Benevolence).Select(c => c.Id).ToArray(), Before = _movementSequence,
                Recipients = _players.Where(p => p.IsAlive).OrderBy(p => RelativeTableOrder(_currentSeat, p.Seat)).Select(p => p.Seat).ToArray() };
        }
        ReplaceRuntimeTop(f = f with { MobileHuaTuo = r });
        if (op is SkillProgramEffectOp.DepositMobileShenHuaTuoYouyi or SkillProgramEffectOp.RecoverMobileShenHuaTuoYouyi)
        {
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            foreach (var id in r.Cards)
            {
                var from = _cardZones.GetLocation(id);
                var expected = op == SkillProgramEffectOp.DepositMobileShenHuaTuoYouyi ? CardLocation.DiscardPile : CardLocation.Benevolence;
                if (from != expected) throw new InvalidOperationException("Youyi lost a frozen payable shared-area entity.");
                MoveCard(GetAdvancedCard(id), from, op == SkillProgramEffectOp.DepositMobileShenHuaTuoYouyi ? CardLocation.Benevolence : CardLocation.DiscardPile,
                    new(op == SkillProgramEffectOp.DepositMobileShenHuaTuoYouyi ? "skill.mobile-shen-hua-tuo.youyi.deposit" : "skill.mobile-shen-hua-tuo.youyi.pay"));
            }
            if (op == SkillProgramEffectOp.DepositMobileShenHuaTuoYouyi)
                while (_cardZones.CardsAt(CardLocation.Benevolence).Count > 6)
                    MoveCard(_cardZones.CardsAt(CardLocation.Benevolence)[0], CardLocation.Benevolence, CardLocation.DiscardPile, new("benevolence.overflow"));
            ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { MobileHuaTuo = r with { After = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new MobileHuaTuoPaidEvent(f.Id, 0, op == SkillProgramEffectOp.DepositMobileShenHuaTuoYouyi ? "deposit" : "ren-payment", f.OwnerSeat, r.Before, _movementSequence));
        }
        ContinueMobileHt(f.Id); return SkillProgramStepOutcome.AwaitChoice;
    }
    private PromptChoice MobileHtChoice(ProgramSkillFrame f, string option, string label, int chooser, int? target = null, int? card = null) =>
        new(new($"mobile-hua-tuo.{f.Id}.{f.MobileHuaTuo!.Order.Count}.{option}"), label, card is { } id ? [id] : [], target is { } seat ? [seat] : [],
            new Dictionary<string, string> { ["program-action"] = "mobile-hua-tuo", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["option"] = option, ["chooser"] = chooser.ToString(CultureInfo.InvariantCulture) });
    private IReadOnlyList<PromptChoice> MobileHtChoices(ProgramSkillFrame f)
    {
        var r = f.MobileHuaTuo!;
        if (r.Stage == MobileHuaTuoStage.Recipient) return MobileHtTeachingTargets().Select(s => MobileHtChoice(f, "recipient:" + s, "向" + _players[s].Name + "传授五禽戏", f.OwnerSeat, target: s)).ToArray();
        if (r.Stage == MobileHuaTuoStage.Order) return Enum.GetValues<MobileWulingAnimal>().Except(r.Order).Select(a => MobileHtChoice(f, "animal:" + (int)a,
            $"第{r.Order.Count + 1}种：{MobileHtAnimalName(a)}", f.OwnerSeat)).ToArray();
        if (r.Stage == MobileHuaTuoStage.Ape)
            return _players.Where(p => p.IsAlive && p.Seat != r.TargetSeat).OrderBy(p => RelativeTableOrder(r.TargetSeat, p.Seat)).SelectMany(p => GetEquipment(p)
                .Where(c => !IsForeignEquipmentDiscardPrevented(r.TargetSeat, c, CardLocation.Equipment(p.Seat), OwnedCardMoveIntent.Obtain))
                .Select(c => MobileHtChoice(f, $"ape:{p.Seat}:{c.Id}", "获得" + p.Name + "的【" + c.DisplayName + "】", r.TargetSeat, p.Seat, c.Id))).ToArray();
        return [];
    }
    private static string MobileHtAnimalName(MobileWulingAnimal animal) => animal switch
    { MobileWulingAnimal.Tiger => "虎灵", MobileWulingAnimal.Crane => "鹤灵", MobileWulingAnimal.Bear => "熊灵", MobileWulingAnimal.Ape => "猿灵", _ => "鹿灵" };
    private void PublishMobileHt(ProgramSkillFrame f)
    {
        var choices = MobileHtChoices(f); if (choices.Count == 0) { FinishMobileHt(f); return; }
        var r = f.MobileHuaTuo!; var chooser = r.Stage == MobileHuaTuoStage.Ape ? r.TargetSeat : f.OwnerSeat; var s = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, r.Stage == MobileHuaTuoStage.Order ? "请选择五禽戏的先后顺序。" : "请选择。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray()), SkillPrompt = new(f.SkillId, s.Name, s.Name, s.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("mobile-hua-tuo")]
    private void AnswerMobileHt(PromptChoice choice)
    {
        var f = GetActiveProgramFrame(_resolutionStack[^1].Id); var r = f.MobileHuaTuo ?? throw new InvalidOperationException("Mobile Hua Tuo lost its owning choice.");
        if (!AssistedChoicesEqual([choice], MobileHtChoices(f).Where(c => c.Id == choice.Id).ToArray())) throw new InvalidOperationException("Wuling requires the exact published order or public equipment entity.");
        ClearPendingDecision();
        if (r.Stage == MobileHuaTuoStage.Recipient)
            ReplaceRuntimeTop(f = f with { MobileHuaTuo = r with { TargetSeat = choice.Targets.Single(), Stage = MobileHuaTuoStage.Order } });
        else if (r.Stage == MobileHuaTuoStage.Order)
        {
            var animal = (MobileWulingAnimal)int.Parse(choice.Parameters["option"].Split(':')[1], CultureInfo.InvariantCulture);
            r = r with { Order = r.Order.Append(animal).ToArray() };
            if (r.Order.Count == 5)
            {
                if (!_players[r.TargetSeat].IsAlive || MobileHtCourse(r.TargetSeat) is not null) { FinishMobileHt(f); return; }
                _mobileWulingCourses.Add(f.Id, new(r.Source, r.TargetSeat) { Order = r.Order });
                AdvanceEventRulesAndQueueFact(new MobileWulingTaughtEvent(f.Id, r.Source, r.TargetSeat));
                r = r with { Stage = MobileHuaTuoStage.Entry };
            }
            ReplaceRuntimeTop(f = f with { MobileHuaTuo = r });
        }
        else
        {
            var card = GetAdvancedCard(choice.Cards.Single()); var from = CardLocation.Equipment(choice.Targets.Single()); var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { MobileHuaTuo = r with { Stage = MobileHuaTuoStage.Children, Before = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            MoveCard(card, from, card.IsGeneralWeapon ? CardLocation.OutsideGame : CardLocation.Hand(r.TargetSeat), new("skill.mobile-shen-hua-tuo.wuling.ape"));
            ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { MobileHuaTuo = f.MobileHuaTuo! with { After = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new MobileHuaTuoPaidEvent(f.Id, 0, "ape", r.TargetSeat, before, _movementSequence));
        }
        ContinueMobileHt(f.Id);
    }
    private bool DrainMobileHt(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueMobileHt(long id)
    {
        while (_pendingDecision is null && _resolutionStack.LastOrDefault() is ProgramSkillFrame { MobileHuaTuo: { } r } f && f.Id == id)
        {
            if (r.Stage == MobileHuaTuoStage.Drawing) return;
            if (r.Stage == MobileHuaTuoStage.Entry)
            {
                if (r.AnimalIndex >= r.Order.Count) { EndMobileHtCourse(r.CourseId, f.Id); ReplaceRuntimeTop(f with { MobileHuaTuo = r with { Stage = MobileHuaTuoStage.Children } }); continue; }
                ApplyMobileHtAnimal(f); continue;
            }
            if (r.Stage == MobileHuaTuoStage.EntryChildren)
            {
                if (DrainMobileHt(id)) return;
                BeginMobileHtEntryBenefit(GetActiveProgramFrame(id)); continue;
            }
            if (r.Stage == MobileHuaTuoStage.DeerRecoveryChildren)
            {
                if (DrainMobileHt(id)) return;
                ClearMobileHtDeerJudgments(GetActiveProgramFrame(id)); continue;
            }
            if (r.Stage == MobileHuaTuoStage.Children)
            {
                if (DrainMobileHt(id)) return;
                f = GetActiveProgramFrame(id); r = f.MobileHuaTuo!;
                if (r.Op == SkillProgramEffectOp.RecoverMobileShenHuaTuoYouyi) { ReplaceRuntimeTop(f with { MobileHuaTuo = r with { Stage = MobileHuaTuoStage.RecoverAll } }); continue; }
                FinishMobileHt(f); return;
            }
            if (r.Stage == MobileHuaTuoStage.RecoverAll)
            {
                if (r.Cursor >= r.Recipients.Count || _winner != Winner.None) { FinishMobileHt(f); return; }
                var seat = r.Recipients[r.Cursor];
                ReplaceRuntimeTop(f = f with { MobileHuaTuo = r with { Cursor = r.Cursor + 1, Stage = MobileHuaTuoStage.Children } });
                MobileHtRecoverOne(f, seat); continue;
            }
            PublishMobileHt(f); return;
        }
    }
    private void MobileHtRecoverOne(ProgramSkillFrame f, int seat)
    {
        var p = _players[seat]; if (!p.IsAlive || !p.HasHpValue || p.Hp >= p.MaxHp) return;
        if (!TryQueueRecoveryReplacement(f.Id, f.OwnerSeat, seat, 1, new(RecoveryAttemptProducer.Program, f.InstructionIndex)))
        { var recovery = BeginRecovery(f.Id, f.OwnerSeat, seat, 1); p.Hp++; AdvanceEventRulesAndQueueFact(new RecoveryAppliedEvent(f.OwnerSeat, seat, 1, p.Hp)); PopResolutionFrame(recovery, ResolutionFrameKind.Recovery); }
    }
    private void FinishMobileHt(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f with { MobileHuaTuo = null, PendingMovementContinuation = null }); AdvanceRuntimeProgram(f.Id); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 39100)] private bool ResumeMobileHt(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileHuaTuo: not null } f || f.Id != id) return false; ContinueMobileHt(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 39100)] private bool ReturnMobileHtMovement(ProgramSkillFrame f) => f.MobileHuaTuo is not null && ResumeMobileHt(f.Id);
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 39100)] private bool MobileHtMovementProof(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        f.MobileHuaTuo is { Stage: MobileHuaTuoStage.Children } && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 39100)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 39100)] private bool ContinueIssuedMobileHt(ProgramSkillFrame f) => f.MobileHuaTuo is not null;
    [ProgramAiChoice(SkillProgramEffectOp.TeachMobileShenHuaTuoWuling)] private PromptChoice ChooseTeachMobileHt(PendingDecision p, ProgramSkillFrame f) => p.Choices[0];
    [ProgramAiChoice(SkillProgramEffectOp.AdvanceMobileShenHuaTuoWuling)] private PromptChoice ChooseAdvanceMobileHt(PendingDecision p, ProgramSkillFrame f) => p.Choices[0];
    [ProgramAiChoice(SkillProgramEffectOp.DepositMobileShenHuaTuoYouyi)] private PromptChoice ChooseDepositMobileHt(PendingDecision p, ProgramSkillFrame f) => p.Choices[0];
    [ProgramAiChoice(SkillProgramEffectOp.RecoverMobileShenHuaTuoYouyi)] private PromptChoice ChooseRecoverMobileHt(PendingDecision p, ProgramSkillFrame f) => p.Choices[0];
}
