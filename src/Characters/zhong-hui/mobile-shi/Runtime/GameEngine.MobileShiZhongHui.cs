using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShiZhHuiSizi = ShiZhHuiComposition.SiziSkill,
        ShiZhHuiXiezhi = ShiZhHuiComposition.XiezhiSkill, ShiZhHuiYunan = ShiZhHuiComposition.YunanSkill;

    private static CardConversionSource ShiZhHuiSource(ProgramSkillFrame f) =>
        new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool HasShiZhHuiCapability =>
        _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveShiZhHuiSizi);
    private bool HasShiZhHuiKechang(int seat) =>
        _players[seat].SkillGrants.Grants.Any(grant => grant.IsEnabled &&
            grant.SkillId == ShiZhHuiComposition.KeChangSkill) ||
        EnabledContentSkillIds(_players[seat]).Contains(ShiZhHuiComposition.KeChangSkill, StringComparer.Ordinal);
    private int ShiZhHuiKechangLevel(int seat) =>
        ProgramEventHistory<ShiZhHuiYunanAwakenedEvent>().Where(entry => entry.Source.OwnerSeat == seat)
            .Select(entry => entry.Level).LastOrDefault();
    private bool ShiZhHuiRoundHasDeath =>
        ProgramEventHistory<ShiZhHuiDeathRecordedEvent>().Any(death => death.RoundNumber == _roundNumber);
    private ShiZhHuiSiziActivatedEvent? ShiZhHuiActiveSizi =>
        ProgramEventHistory<ShiZhHuiSiziActivatedEvent>().LastOrDefault(activation =>
            !ProgramEventHistory<ShiZhHuiSiziExpiredEvent>().Any(expired => expired.ActivationOrdinal == activation.Ordinal));
    private bool ShiZhHuiSiziActive => ShiZhHuiActiveSizi is not null;
    private IReadOnlyList<int> ShiZhHuiEndingVictims(int turnNumber, int ownerHp, int consumed)
    {
        var users = ProgramEventHistory<ShiZhHuiSlashUsedEvent>().Where(use => use.TurnNumber == turnNumber)
            .Select(use => use.ActorSeat).Distinct().Where(seat => IsValidPlayerSeat(seat) && _players[seat].IsAlive).ToArray();
        if (users.Length > 0 || consumed <= ownerHp || !IsValidPlayerSeat(_currentSeat) || !_players[_currentSeat].IsAlive)
            return Array.AsReadOnly(users);
        // Effect 3: with no slash used and a consumed X above the activation HP, the turn player loses 1 HP.
        return Array.AsReadOnly(new[] { _currentSeat });
    }

    [ProgramCapability]
    private sealed class ShiZhHuiCapability(GameEngine engine) : IShiZhHuiHost
    {
        public SkillProgramStepOutcome ResolveSizi(ProgramSkillFrame frame) => engine.BeginShiZhHui(frame);
        public SkillProgramStepOutcome ResolveSiziEnding(ProgramSkillFrame frame) => engine.BeginShiZhHui(frame);
        public SkillProgramStepOutcome ResolveXiezhi(ProgramSkillFrame frame) => engine.BeginShiZhHui(frame);
        public SkillProgramStepOutcome ResolveYunan(ProgramSkillFrame frame) => engine.BeginShiZhHui(frame);
    }

    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCondition, 67700, SkillProgramEffectOp.ResolveShiZhHuiSizi)]
    private bool CanStartShiZhHuiSizi(CharacterState owner, string skill) =>
        skill != ShiZhHuiSizi || ShiZhHuiCharge(owner.Seat) > 0;

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 67700)]
    private bool CanOfferShiZhHui(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (candidate.SkillId == ShiZhHuiSizi && trigger.Id == ShiZhHuiComposition.SiziEndingTrigger)
            return ShiZhHuiSiziActive;
        if (candidate.SkillId == ShiZhHuiYunan && trigger.Id == ShiZhHuiComposition.YunanTrigger)
            return ActiveDying is { } dying && dying.KillerSeat == candidate.OwnerSeat && ShiZhHuiRoundHasDeath;
        return true;
    }

    private SkillProgramStepOutcome BeginShiZhHui(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.ShiZhHui is not null) throw new InvalidOperationException("Shi Zhong Hui restarted a running operation.");
        var r = f.SkillId == ShiZhHuiSizi && f.ActivationId == ShiZhHuiComposition.SiziActivation && f.TriggerId is null
            ? StartShiZhHuiSizi(f)
            : f.SkillId == ShiZhHuiSizi && f.TriggerId == ShiZhHuiComposition.SiziEndingTrigger ? StartShiZhHuiSiziEnding(f)
            : f.SkillId == ShiZhHuiXiezhi && f.TriggerId == ShiZhHuiComposition.XiezhiTrigger ? StartShiZhHuiXiezhi(f)
            : f.SkillId == ShiZhHuiYunan && f.TriggerId == ShiZhHuiComposition.YunanTrigger ? StartShiZhHuiYunan(f)
            : throw new InvalidOperationException("Shi Zhong Hui instruction has an incompatible owning skill.");
        ReplaceRuntimeTop(f = f with { ShiZhHui = r });
        ContinueShiZhHui(f.Id);
        return r.Operation == "sizi" ? SkillProgramStepOutcome.AwaitChoice : SkillProgramStepOutcome.AwaitChild;
    }

    private ShiZhHuiReceipt StartShiZhHuiSizi(ProgramSkillFrame f)
    {
        if (!_players[f.OwnerSeat].IsAlive || ShiZhHuiCharge(f.OwnerSeat) <= 0)
            throw new InvalidOperationException("Sizi requires its living owner to hold at least one charge point.");
        return new("sizi", _turnNumber) { Source = ShiZhHuiSource(f), GameplayHash = f.GameplayHash };
    }

    private ShiZhHuiReceipt StartShiZhHuiSiziEnding(ProgramSkillFrame f)
    {
        if (ShiZhHuiActiveSizi is not { } activation)
            throw new InvalidOperationException("Sizi ending requires an active effect.");
        return new("sizi-ending", _turnNumber)
        { Source = ShiZhHuiSource(f), GameplayHash = f.GameplayHash, Ordinal = activation.Ordinal };
    }

    private ShiZhHuiReceipt StartShiZhHuiXiezhi(ProgramSkillFrame f)
    {
        _ = f.WindowContext?.HpChange ?? throw new InvalidOperationException("Xiezhi lost its health-change window.");
        return new("xiezhi", _turnNumber) { Source = ShiZhHuiSource(f), GameplayHash = f.GameplayHash };
    }

    private ShiZhHuiReceipt StartShiZhHuiYunan(ProgramSkillFrame f)
    {
        var dying = ActiveDying ?? throw new InvalidOperationException("Yunan lost its dying entry.");
        if (dying.KillerSeat != f.OwnerSeat)
            throw new InvalidOperationException("Yunan requires a dying state caused by its owner.");
        return new("yunan", _turnNumber, TargetSeat: dying.VictimSeat)
        { Source = ShiZhHuiSource(f), GameplayHash = f.GameplayHash, DyingFrameId = dying.FrameId };
    }

    // ---- choice publication -------------------------------------------------

    private IReadOnlyList<PromptChoice> ShiZhHuiSiziChoices(ProgramSkillFrame f)
    {
        var r = f.ShiZhHui!;
        var charge = ShiZhHuiCharge(f.OwnerSeat);
        var choices = new List<PromptChoice>();
        for (var count = 1; count <= Math.Min(charge, ShiZhHuiComposition.ChargeCapacity); count++)
            choices.Add(new(new($"shi-zhong-hui.{f.Id}.{r.Stage}.consume.{count}"),
                $"消耗{count}点蓄力点（效果持续至{count}个回合结束后或你的回合开始时）", [], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = ShiZhHuiComposition.SiziProgramAction,
                    ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture),
                    ["branch"] = "consume", ["count"] = count.ToString(CultureInfo.InvariantCulture),
                }));
        return Array.AsReadOnly(choices.ToArray());
    }

    private void PublishShiZhHui(ProgramSkillFrame f)
    {
        var r = f.ShiZhHui!;
        var choices = r.Operation == "sizi" ? ShiZhHuiSiziChoices(f) : [];
        if (choices.Count == 0 || _winner != Winner.None || !_players[f.OwnerSeat].IsAlive)
        { FinishShiZhHuiOperation(GetActiveProgramFrame(f.Id)); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "肆恣：选择要消耗的蓄力点数。",
            choices.SelectMany(choice => choice.Cards).Distinct().ToArray(),
            choices.SelectMany(choice => choice.Targets).Distinct().ToArray(), f.OwnerSeat)
        {
            PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description)
        };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }

    [ProgramAnswer(ShiZhHuiComposition.SiziProgramAction)]
    private void AnswerShiZhHui(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiZhHui: { Operation: "sizi" } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !ShiZhHuiSiziChoices(f).Any(choice => AssistedChoicesEqual([choice], [selected])))
            throw new InvalidOperationException("Shi Zhong Hui choice lost its current frame, chooser or legal branch.");
        ClearPendingDecision();
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var consumed = int.Parse(selected.Parameters["count"], CultureInfo.InvariantCulture);
        if (consumed is < 1 or > ShiZhHuiComposition.ChargeCapacity || consumed > ShiZhHuiCharge(f.OwnerSeat))
            throw new InvalidOperationException("Sizi consumed an unavailable charge amount.");
        ChangeShiZhHuiCharge(f.Id, f.OwnerSeat, -consumed, ShiZhHuiComposition.SiziChargeReason, out _);
        var ordinal = ProgramEventHistory<ShiZhHuiSiziActivatedEvent>().Count + 1;
        AdvanceEventRulesAndQueueFact(new ShiZhHuiSiziActivatedEvent(f.Id, r.Source!, f.GameplayHash, _turnNumber,
            ordinal, consumed, _players[f.OwnerSeat].Hp));
        ReplaceRuntimeTop(f = f with { ShiZhHui = r with { Consumed = consumed, Ordinal = ordinal } });
        FinishShiZhHuiOperation(GetActiveProgramFrame(f.Id));
    }

    // ---- continuations ------------------------------------------------------

    private void ContinueShiZhHui(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiZhHui: { } r } f || f.Id != id) return;
        if (r.Operation == "sizi-ending" && r.Stage is 6 or 12)
        {
            if (DrainShiZhHui(f)) return;
            r = GetActiveProgramFrame(id).ShiZhHui!;
        }
        switch (r.Operation, r.Stage)
        {
            case ("sizi", 0):
                PublishShiZhHui(GetActiveProgramFrame(id));
                return;
            case ("sizi-ending", 0) or ("sizi-ending", 6) or ("sizi-ending", 12):
                ContinueShiZhHuiEnding(GetActiveProgramFrame(id));
                return;
            case ("xiezhi", 0):
                ContinueShiZhHuiXiezhi(GetActiveProgramFrame(id));
                return;
            case ("yunan", 0):
                ContinueShiZhHuiYunan(GetActiveProgramFrame(id));
                return;
            default:
                throw new InvalidOperationException("Shi Zhong Hui has an unknown continuation stage.");
        }
    }

    private void ContinueShiZhHuiEnding(ProgramSkillFrame f)
    {
        var r = f.ShiZhHui!;
        var activation = ShiZhHuiActiveSizi ?? throw new InvalidOperationException("Sizi ending lost its active effect.");
        if (r.Ordinal != activation.Ordinal)
            throw new InvalidOperationException("Sizi ending resolved a different activation than its receipt.");
        if (r.Stage == 0)
        {
            if (!_players[f.OwnerSeat].IsAlive)
            { FinishShiZhHuiOperation(GetActiveProgramFrame(f.Id)); return; }
            DrawProgramCards(f.Id, f.OwnerSeat, 2, null, null, SkillProgramCardSetVisibility.Private,
                new(ShiZhHuiComposition.SiziEndingDrawReason));
            ReplaceRuntimeTop(f = f with { ShiZhHui = r with { Stage = 6 } });
        }
        r = GetActiveProgramFrame(f.Id).ShiZhHui!;
        if (r.Stage == 6)
        {
            var victims = ShiZhHuiEndingVictims(_turnNumber, activation.OwnerHp, activation.Consumed);
            ReplaceRuntimeTop(f = f with { ShiZhHui = r with { Victims = victims, VictimCursor = 0, Stage = 12 } });
        }
        ContinueShiZhHuiEndingLosses(GetActiveProgramFrame(f.Id), activation);
    }

    private void ContinueShiZhHuiEndingLosses(ProgramSkillFrame f, ShiZhHuiSiziActivatedEvent activation)
    {
        var r = f.ShiZhHui!;
        var victims = r.Victims ?? throw new InvalidOperationException("Sizi ending lost its victim ledger.");
        while (r.VictimCursor < victims.Count)
        {
            var victim = victims[r.VictimCursor];
            if (!IsValidPlayerSeat(victim) || !_players[victim].IsAlive)
            {
                ReplaceRuntimeTop(f = f with { ShiZhHui = r with { VictimCursor = r.VictimCursor + 1 } });
                r = f.ShiZhHui!;
                continue;
            }
            ReplaceRuntimeTop(f = f with { ShiZhHui = r with { VictimCursor = r.VictimCursor + 1 } });
            if (ApplyProgramHpLoss(f.Id, f.SkillId, victim, 1) == SkillProgramStepOutcome.AwaitChild) return;
            r = GetActiveProgramFrame(f.Id).ShiZhHui!;
        }
        var elapsed = ProgramEventHistory<ShiZhHuiSiziElapsedEvent>().Count(entry => entry.ActivationOrdinal == activation.Ordinal) + 1;
        AdvanceEventRulesAndQueueFact(new ShiZhHuiSiziElapsedEvent(activation.Ordinal, elapsed));
        if (elapsed >= activation.Consumed)
            AdvanceEventRulesAndQueueFact(new ShiZhHuiSiziExpiredEvent(activation.Ordinal, "turn-count"));
        FinishShiZhHuiOperation(GetActiveProgramFrame(f.Id));
    }

    private void ContinueShiZhHuiXiezhi(ProgramSkillFrame f)
    {
        var r = f.ShiZhHui!;
        var change = f.WindowContext?.HpChange ??
            throw new InvalidOperationException("Xiezhi lost its health-change window.");
        var delta = Math.Abs(change.HpAfter - change.HpBefore);
        if (delta > 0 && _players[f.OwnerSeat].IsAlive)
        {
            ChangeShiZhHuiCharge(f.Id, f.OwnerSeat, delta, ShiZhHuiComposition.XiezhiChargeReason, out var excess);
            if (excess > 0)
                AdvanceEventRulesAndQueueFact(new ShiZhHuiXiezhiOverflowEvent(f.Id, f.OwnerSeat, excess,
                    ShiZhHuiOverflowCount(f.OwnerSeat) + 1));
        }
        FinishShiZhHuiOperation(GetActiveProgramFrame(f.Id));
    }

    private void ContinueShiZhHuiYunan(ProgramSkillFrame f)
    {
        var r = f.ShiZhHui!;
        var dying = ActiveDying ?? throw new InvalidOperationException("Yunan lost its dying entry.");
        if (dying.FrameId != r.DyingFrameId || dying.KillerSeat != f.OwnerSeat)
            throw new InvalidOperationException("Yunan lost its original dying victim.");
        ApplyShiZhHuiFactionChange(f, "qun");
        var level = Math.Min(2, ShiZhHuiKechangLevel(f.OwnerSeat) + 1);
        if (!HasShiZhHuiKechang(f.OwnerSeat)) GrantProgramSkills(f, [ShiZhHuiComposition.KeChangSkill]);
        AdvanceEventRulesAndQueueFact(new ShiZhHuiYunanAwakenedEvent(f.Id, r.Source!, f.GameplayHash, _turnNumber,
            dying.VictimSeat, level, "qun"));
        FinishShiZhHuiOperation(GetActiveProgramFrame(f.Id));
    }

    private void FinishShiZhHuiOperation(ProgramSkillFrame f)
    {
        FinishProgramSkill(GetActiveProgramFrame(f.Id), true);
    }

    private bool DrainShiZhHui(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) ||
        TryBeginAdvancedSkillsChanged(f.Id);

    [ProgramContinuation(ProgramContinuationKind.Resume, 67700)]
    private bool ResumeShiZhHui(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiZhHui: not null } f || f.Id != id) return false;
        ContinueShiZhHui(id);
        return true;
    }

    [ProgramContinuation(ProgramContinuationKind.Movement, 67700)]
    private bool ReturnShiZhHuiMovement(ProgramSkillFrame f) => f.ShiZhHui is not null && ResumeShiZhHui(f.Id);

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 67700)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 67700)]
    private bool ContinuePaidShiZhHui(ProgramSkillFrame f) => f.ShiZhHui is { Stage: > 0 };

    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 67700)]
    private int ShiZhHuiSiziDamageBonus(IDamageAttempt attack) =>
        attack is CardAttackHandle card && IsSlashCard(card.EffectiveCardKind ?? CardKind.Slash) &&
        ShiZhHuiSiziActive ? 1 : 0;

    [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption, 67700)]
    private bool DistanceCardUseExemption67700ShiZhHuiKechang(ProgramCardDistanceQuery query) =>
        IsSlashCard(query.Kind) &&
        string.Equals(GetEffectiveFactionId(query.Actor), "qun", StringComparison.Ordinal) &&
        _players.Any(player => player.IsAlive && HasShiZhHuiKechang(player.Seat));

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 67700)]
    private void AssertShiZhHui(ProgramSkillFrame f)
    {
        if (f.ShiZhHui is not { } r) return;
        if (r.ActualTurn <= 0 || r.Source?.OwnerSeat != f.OwnerSeat ||
            r.Operation is not ("sizi" or "sizi-ending" or "xiezhi" or "yunan"))
            throw new InvalidOperationException("Shi Zhong Hui lost its original typed operation receipt.");
        if (r.Operation == "sizi" && r.Ordinal != 0 &&
            !ProgramEventHistory<ShiZhHuiSiziActivatedEvent>().Any(entry => entry.FrameId == f.Id &&
                entry.Source == ShiZhHuiSource(f) && entry.Ordinal == r.Ordinal && entry.Consumed == r.Consumed))
            throw new InvalidOperationException("Sizi lost its activation receipt.");
        if (r.Operation == "sizi-ending" && r.Stage >= 12 &&
            (r.Victims is null || r.Victims.Any(victim => !IsValidPlayerSeat(victim))))
            throw new InvalidOperationException("Sizi ending lost its victim ledger.");
        if (r.Operation == "yunan" && !ProgramEventHistory<ShiZhHuiYunanAwakenedEvent>()
            .Any(entry => entry.FrameId == f.Id && entry.Source == ShiZhHuiSource(f)))
            throw new InvalidOperationException("Yunan lost its awakening receipt.");
    }

    [ProgramAiChoice(SkillProgramEffectOp.ResolveShiZhHuiSizi)]
    private PromptChoice ChooseShiZhHuiAi(PendingDecision decision, ProgramSkillFrame f)
    {
        // Prefer the largest consumption that stays at or below the owner's HP so
        // the effect-3 qualification keeps targeting foreign turn players.
        var hp = _players[f.OwnerSeat].Hp;
        return decision.Choices.Where(choice => choice.Parameters["branch"] == "consume")
            .OrderByDescending(choice => int.Parse(choice.Parameters["count"], CultureInfo.InvariantCulture))
            .FirstOrDefault(choice => int.Parse(choice.Parameters["count"], CultureInfo.InvariantCulture) <= hp) ??
            decision.Choices.First();
    }
}
