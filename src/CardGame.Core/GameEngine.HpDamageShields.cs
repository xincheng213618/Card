using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome BeginHpDamageShieldPayment(ProgramSkillFrame frame, PlayerMarkerKind marker)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (active.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } ||
            active.HpDamageShieldDraft is not null || active.HpDamageShieldReceipt is not null ||
            !owner.IsAlive || owner.Hp <= 0 || _currentSeat != owner.Seat)
            throw new InvalidOperationException("HP-paid shields require their unpaid owner's turn-ending program.");
        var candidates = _players.Where(p => p.IsAlive && p.Seat != owner.Seat).Select(p => p.Seat).ToArray();
        var maximum = Math.Min(owner.Hp, candidates.Length);
        if (maximum == 0) return SkillProgramStepOutcome.Continue;
        active = active with { HpDamageShieldDraft = new(active.InstructionIndex, marker, owner.Hp,
            maximum, Array.AsReadOnly(candidates), _turnNumber, _currentSeat) };
        ReplaceRuntimeTop(active);
        var skill = _contentRegistry.GetSkill(active.SkillId);
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTrigger, owner.Seat,
            $"【{skill.Name}】选择其他角色；每选择一名角色失去1点体力。", [], candidates, owner.Seat)
        {
            PromptId = CreatePromptId(), IsPrivate = true,
            SkillPrompt = new(active.SkillId, skill.Name, $"{skill.Name} · 失去体力与保护", skill.Description),
            Choices = HpDamageShieldChoices(active)
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> HpDamageShieldChoices(ProgramSkillFrame frame)
    {
        var draft = frame.HpDamageShieldDraft ?? throw new InvalidOperationException("Missing shield quantity draft.");
        var choices = new List<PromptChoice>();
        void AddCombinations(int size, int from, List<int> selected)
        {
            if (selected.Count == size)
            {
                var targets = Array.AsReadOnly(selected.ToArray());
                choices.Add(new(new ChoiceId($"hp-shield.frame-{frame.Id}.{string.Join('-', targets)}"),
                    $"失去{size}点体力，保护{string.Join('、', targets.Select(s => _players[s].Name))}", [], targets,
                    new Dictionary<string, string>
                    {
                        ["program-action"] = "hp-damage-shield",
                        ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture),
                        ["quantity"] = size.ToString(CultureInfo.InvariantCulture)
                    }));
                return;
            }
            for (var i = from; i <= draft.CandidateSeats.Count - (size - selected.Count); i++)
            {
                selected.Add(draft.CandidateSeats[i]);
                AddCombinations(size, i + 1, selected);
                selected.RemoveAt(selected.Count - 1);
            }
        }
        for (var count = 1; count <= draft.Maximum; count++) AddCombinations(count, 0, []);
        return Array.AsReadOnly(choices.ToArray());
    }

    private void ResolveHpDamageShieldPayment(PromptChoice choice)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The shield choice lost its owning program.");
        var draft = frame.HpDamageShieldDraft ?? throw new InvalidOperationException("The shield choice lost its unpaid draft.");
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != frame.OwnerSeat || !AssistedChoicesEqual([choice],
                [HpDamageShieldChoices(frame).Single(c => c.Id == choice.Id)]))
            throw new InvalidOperationException("The shield choice differs from its frozen owning prompt.");
        var owner = _players[frame.OwnerSeat];
        ClearPendingDecision();
        if (!owner.IsAlive || owner.Hp != draft.HpBefore || _turnNumber != draft.TurnNumber ||
            _currentSeat != draft.TurnOwnerSeat || !HasRuntimeSkillInstance(owner, frame.SkillId, frame.SkillInstanceId) ||
            choice.Targets.Any(s => !_players[s].IsAlive))
        {
            CancelProgramBindingAndCleanup(frame, "付款前体力、目标或技能实例失效，剩余结算取消。");
            return;
        }
        var requested = choice.Targets.Count;
        if (requested < 1 || requested > draft.Maximum || requested > owner.Hp)
            throw new InvalidOperationException("The frozen shield targets exceed the affordable HP quantity.");
        var before = owner.Hp;
        owner.Hp -= requested;
        ReplaceRuntimeTop(frame with
        {
            HpDamageShieldDraft = null,
            HpDamageShieldReceipt = new(frame.InstructionIndex, draft.Marker, frame.OwnerSeat,
                frame.SkillId, GetProgramBindingId(frame), frame.SkillInstanceId, frame.GameplayHash,
                before, owner.Hp, before - owner.Hp, Array.AsReadOnly(choice.Targets.ToArray()),
                _turnNumber, _currentSeat)
        });
        RecordHpChange(frame.Id, null, owner.Seat, before, owner.Hp, HpChangeKind.Loss);
        AdvanceEventRulesAndQueueFact(new ProgramSkillHpLostEvent(frame.Id, frame.SkillId, owner.Seat,
            before - owner.Hp, owner.Hp));
        if (owner.Hp == 0) BeginProgramSkillDying(frame.Id, owner);
        else AdvanceRuntimeProgram(frame.Id);
    }

    // Invoked before ordinary executor cancellation. The physical cost is already
    // paid; source death or skill loss cannot invalidate the surviving recipients.
    private bool ResumeHpDamageShieldPayment(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != frameId ||
            frame.HpDamageShieldReceipt is not { Granted: false } receipt) return false;
        AssertHpDamageShieldState(frame);
        if (_winner != Winner.None || _status == EngineStatus.Completed)
        {
            // The completed match cannot acquire new rule effects. Consume only
            // this producer's unobservable HP obligation and cancel its tail.
            _pendingHpChanges.RemoveAll(change => change.ParentFrameId == frame.Id);
            ReplaceRuntimeTop(frame with { HpDamageShieldReceipt = receipt with { Granted = true } });
            FinishProgramSkill(GetActiveProgramFrame(frame.Id), completed: false);
            return true;
        }
        if (TryBeginQueuedRecoveryReplacement(frame.Id, PostEventContinuation.Program) ||
            TryBeginCharacterStateProgramWindow(frame.Id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.Program) ||
            TryBeginCardsMovedProgramWindow(frame.Id) || TryBeginAdvancedSkillsChanged(frame.Id)) return true;
        frame = GetActiveProgramFrame(frame.Id);
        ReplaceRuntimeTop(frame with { HpDamageShieldReceipt = receipt with { Granted = true } });
        foreach (var seat in receipt.TargetSeats)
        {
            var target = _players[seat];
            if (!target.IsAlive) continue;
            var shield = new OneUseDamageShield(frame.Id, receipt.InstructionIndex, receipt.OwnerSeat,
                seat, receipt.Marker, receipt.SkillId, receipt.BindingId, receipt.SkillInstanceId, receipt.GameplayHash);
            target.OneUseDamageShields.Add(shield);
            var source = (receipt.Marker, receipt.OwnerSeat);
            target.MarkerSourceCounts[source] = target.MarkerSourceCounts.GetValueOrDefault(source) + 1;
            target.Markers[receipt.Marker] = target.Markers.GetValueOrDefault(receipt.Marker) + 1;
            AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(frame.Id, seat, receipt.Marker, 1,
                target.Markers[receipt.Marker], receipt.OwnerSeat, "program.hp-paid-damage-shield"));
            AdvanceEventRulesAndQueueFact(new OneUseDamageShieldGrantedEvent(shield));
        }
        return false;
    }

    private bool TryConsumeOneUseDamageShield(IDamageAttempt attack, int amount)
    {
        var target = _players[attack.TargetSeat];
        if (amount <= 0 || target.OneUseDamageShields.FirstOrDefault() is not { } shield) return false;
        var source = (shield.Marker, shield.OwnerSeat);
        var count = target.MarkerSourceCounts.GetValueOrDefault(source);
        var total = target.Markers.GetValueOrDefault(shield.Marker);
        if (shield.TargetSeat != target.Seat || count <= 0 || total <= 0)
            throw new InvalidOperationException("The independent damage shield lost its paid marker.");
        target.OneUseDamageShields.RemoveAt(0);
        if (count == 1) target.MarkerSourceCounts.Remove(source); else target.MarkerSourceCounts[source] = count - 1;
        if (total == 1) target.Markers.Remove(shield.Marker); else target.Markers[shield.Marker] = total - 1;
        AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(attack.ResolutionId, target.Seat,
            shield.Marker, -1, total - 1, shield.OwnerSeat, "program.one-use-damage-shield.consume"));
        AdvanceEventRulesAndQueueFact(new OneUseDamageShieldConsumedEvent(attack.ResolutionId, shield, amount));
        AddLog("DamagePrevented", $"{target.Name} 移除“{PlayerMarkerCatalog.GetDisplayName(shield.Marker)}”并防止{amount}点伤害。",
            attack.SourceSeat, target.Seat);
        return true;
    }

    private int IndependentMarkerCount(CharacterState target, PlayerMarkerKind marker, int source) =>
        target.OneUseDamageShields.Count(s => s.Marker == marker && s.OwnerSeat == source);

    private void AssertHpDamageShieldState(ProgramSkillFrame frame)
    {
        if (frame.HpDamageShieldDraft is null && frame.HpDamageShieldReceipt is null) return;
        var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
        if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } ||
            frame.InstructionIndex != 1 || plan.Instructions.Count != 1 ||
            plan.Instructions[0].Op != SkillProgramEffectOp.PayHpToGrantOneUseDamageShield)
            throw new InvalidOperationException("HP-paid shields lost their standalone owning turn-ending instruction.");
        if (frame.HpDamageShieldDraft is { } draft &&
            (frame.HpDamageShieldReceipt is not null || draft.InstructionIndex != frame.InstructionIndex ||
             draft.Marker != plan.Instructions[0].Marker || draft.HpBefore <= 0 || draft.Maximum <= 0 ||
             draft.CandidateSeats is not System.Collections.ObjectModel.ReadOnlyCollection<int> ||
             draft.Maximum != Math.Min(draft.HpBefore, draft.CandidateSeats.Count) ||
             draft.CandidateSeats.Distinct().Count() != draft.CandidateSeats.Count ||
             draft.CandidateSeats.Any(s => !IsValidPlayerSeat(s) || s == frame.OwnerSeat) ||
             draft.TurnNumber != _turnNumber || draft.TurnOwnerSeat != frame.OwnerSeat ||
             _pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
             decision.PlayerSeat != frame.OwnerSeat || !ReferenceEquals(frame, _resolutionStack.LastOrDefault()) ||
             !AssistedChoicesEqual(decision.Choices, HpDamageShieldChoices(frame))))
            throw new InvalidOperationException("HP-paid shield selection lost its exact frozen prompt.");
        if (frame.HpDamageShieldReceipt is { } receipt &&
            (receipt.InstructionIndex != frame.InstructionIndex || receipt.Marker != plan.Instructions[0].Marker ||
             receipt.OwnerSeat != frame.OwnerSeat || receipt.SkillId != frame.SkillId ||
             receipt.BindingId != GetProgramBindingId(frame) || receipt.SkillInstanceId != frame.SkillInstanceId ||
             receipt.GameplayHash != frame.GameplayHash || receipt.HpBefore <= 0 || receipt.HpAfter < 0 ||
             receipt.ActualLost <= 0 || receipt.ActualLost != receipt.HpBefore - receipt.HpAfter ||
             receipt.TargetSeats is not System.Collections.ObjectModel.ReadOnlyCollection<int> ||
             receipt.TargetSeats.Count != receipt.ActualLost || receipt.TargetSeats.Distinct().Count() != receipt.TargetSeats.Count ||
             receipt.TargetSeats.Any(s => !IsValidPlayerSeat(s) || s == frame.OwnerSeat) ||
             receipt.TurnNumber != _turnNumber || receipt.TurnOwnerSeat != frame.OwnerSeat))
            throw new InvalidOperationException("HP-paid shield receipt lost its exact once-paid source or target quantity.");
    }

    private bool CanContinuePaidHpDamageShield(ProgramSkillFrame frame)
    {
        if (frame.HpDamageShieldReceipt is null) return false;
        AssertHpDamageShieldState(frame);
        return true;
    }

    private PromptChoice SelectAiHpDamageShield(PendingDecision decision)
    {
        var owner = _players[decision.PlayerSeat];
        var count = Math.Max(1, Math.Min(2, owner.Hp - 1));
        var view = CreateSnapshot(owner.Seat);
        var hint = new SkillProgramAiHint(0, 0, 0, 0, 0, 0, false, false)
        { TargetValueAdjustment = 28d };
        return decision.Choices.Where(c => c.Targets.Count <= count)
            .OrderByDescending(c => c.Targets.Sum(s => _aiBrains[owner.Seat].ScoreProgramTarget(view, s, hint)) - c.Targets.Count * 22d)
            .ThenBy(c => c.Targets.Count).ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
    }

    private double GetHpDamageShieldAiTargetValue(CharacterState owner)
    {
        var view = CreateSnapshot(owner.Seat);
        var hint = new SkillProgramAiHint(0, 0, 0, 0, 0, 0, false, false)
        { TargetValueAdjustment = 28d };
        // The optional trigger evaluates the same public relation and HP price
        // as its later quantity choice. The known enemy Lord cannot become a
        // beneficial target merely because the owner has HP to spend.
        return view.Players.Where(p => p.IsAlive && p.Seat != owner.Seat)
            .Select(p => _aiBrains[owner.Seat].ScoreProgramTarget(view, p.Seat, hint))
            .DefaultIfEmpty(-1000d).Max();
    }

    private sealed partial class ProgramSkillHost : IHpDamageShieldProgramHost
    {
        public SkillProgramStepOutcome PayHpToGrantOneUseDamageShield(ProgramSkillFrame frame, PlayerMarkerKind marker) =>
            engine.BeginHpDamageShieldPayment(frame, marker);
        public bool CanContinuePaidDamageShield(ProgramSkillFrame frame) => engine.CanContinuePaidHpDamageShield(frame);
    }
}
