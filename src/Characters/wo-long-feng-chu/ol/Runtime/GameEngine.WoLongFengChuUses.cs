using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string EquipmentSlotNamedTier = "equipment-slot-named-tier";
    private static string EquipmentSlotNamedLabel(EquipmentSlot slot) => slot switch
    { EquipmentSlot.Weapon => "武器", EquipmentSlot.Armor => "防具", EquipmentSlot.OffensiveHorse => "进攻坐骑", EquipmentSlot.DefensiveHorse => "防御坐骑", _ => "宝物" };
    private bool IsEquipmentSlotNamedSkill(string skill) => _contentRegistry.Skills.GetValueOrDefault(skill)?.Program?.Activations
        .Any(a => ProgramInstructionResolver.Default.Features(a).HasOperation(SkillProgramEffectOp.PayEquipmentSlotForNamedUse)) == true;
    private EquipmentSlotNamedUsePayment? UnconsumedEquipmentSlotPayment(CardConversionSource source) => CompleteProgramEventHistory()
        .OfType<EquipmentSlotNamedUsePaidEvent>().Select(e => e.Payment).LastOrDefault(p => p.Source == source &&
            p.RoundNumber == _roundNumber && p.ActualTurn == _turnNumber && p.ActualTurnOwner == _turnProgression.OwnerSeat &&
            !CompleteProgramEventHistory().OfType<EquipmentSlotNamedUseConsumedEvent>().Any(e => e.PaymentFrameId == p.FrameId) &&
            !CompleteProgramEventHistory().OfType<EquipmentSlotNamedUseCancelledEvent>().Any(e => e.PaymentFrameId == p.FrameId));
    private bool EquipmentSlotNamedYin(int seat, string skill, string instance) =>
        _programBooleanStates.GetValueOrDefault(new ProgramBooleanStateKey(seat, skill, instance, "yin"));
    private IEnumerable<(CardConversionSource Source, SkillProgramViewAs Rule, string Hash)> PaidEquipmentSlotNamedZeroRules(
        CharacterState owner, CardKind? output, bool responseUse, bool dyingUse)
    {
        if (!owner.IsAlive || _winner != Winner.None) yield break;
        foreach (var fact in CompleteProgramEventHistory().OfType<EquipmentSlotNamedUsePaidEvent>())
        {
            var p = fact.Payment;
            if (p.Source.OwnerSeat != owner.Seat || UnconsumedEquipmentSlotPayment(p.Source) != p ||
                _resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == p.FrameId) ||
                GetSkillBindingShard(owner).ProgramInstances.Any(i => i.SkillId == p.Source.SkillId && i.SkillInstanceId == p.Source.SkillInstanceId) ||
                ViewAsRule(p.Source) is not { InputCount: 0, UseOnly: true, TieredRoundConversion: not null } rule ||
                output is { } kind && kind != rule.OutputKind || IsCardUseForbidden(owner.Seat, rule.OutputKind, CardActionType.Use) ||
                (dyingUse ? rule.NoDying || !(rule.ForResponse || rule.ForPlay && rule.UseOnly) : responseUse ? !rule.ForResponse : !rule.ForPlay)) continue;
            yield return (p.Source, rule, p.GameplayHash);
        }
    }
    private bool EquipmentSlotNamedNameUsed(int seat, CardKind kind) => CompleteProgramEventHistory().Reverse()
        .TakeWhile(e => e is not EquipmentSlotNamedNamesResetEvent reset || reset.OwnerSeat != seat)
        .OfType<EquipmentSlotNamedUseIssuedEvent>().Any(e => CompleteProgramEventHistory().OfType<EquipmentSlotNamedUsePaidEvent>()
            .Any(p => p.Payment.FrameId == e.PaymentFrameId && p.Payment.Source.OwnerSeat == seat && p.Payment.EffectiveKind == kind));

    private bool TryGetEquipmentSlotNamedConversionAvailability(CharacterState actor, IndexedSkillProgramInstance instance,
        SkillProgramViewAs rule, bool responseUse, bool dyingUse, out bool available)
    {
        available = false; if (!IsEquipmentSlotNamedSkill(instance.SkillId)) return false;
        var source = new CardConversionSource(instance.SkillId, rule.Id, actor.Seat, instance.SkillInstanceId);
        if (_winner != Winner.None || !actor.IsAlive || _roundNumber < 1 || rule.TieredRoundConversion is not { } policy ||
            rule.InputCount != 0 || !rule.UseOnly || TieredRoundTier(actor.Seat, policy.StateId) != 2 ||
            IsCardUseForbidden(actor.Seat, rule.OutputKind, CardActionType.Use)) return true;
        var paid = UnconsumedEquipmentSlotPayment(source);
        if (paid is not null && !_resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == paid.FrameId))
        { available = true; return true; }
        var yin = EquipmentSlotNamedYin(actor.Seat, source.SkillId, source.SkillInstanceId);
        var basic = CardUseCategoryCatalog.Get(rule.OutputKind) == CardUseCategories.Basic;
        available = yin == basic && !EquipmentSlotNamedNameUsed(actor.Seat, rule.OutputKind) &&
            _skillRuntimeState.GetUsage(actor.Seat, source.SkillId, TieredRoundUsage(policy), SkillUsageScope.Round) == 0 &&
            Enum.GetValues<EquipmentSlot>().Any(slot => actor.EquipmentSlotCapacity(slot) > 0);
        return true;
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish, 26100)]
    private void InitializeEquipmentSlotNamedNativeTier()
    {
        foreach (var actor in _players)
            if (GetSkillBindingShard(actor).ProgramInstances.Any(i => IsEquipmentSlotNamedSkill(i.SkillId)) &&
                _configuredConversionTiers.GetValueOrDefault((actor.Seat, EquipmentSlotNamedTier)) != 2)
            {
                _configuredConversionTiers[(actor.Seat, EquipmentSlotNamedTier)] = 2;
                AdvanceEventRulesAndQueueFact(new ConfiguredConversionTierChangedEvent(actor.Seat, EquipmentSlotNamedTier, 2));
            }
    }
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCondition, 26100)]
    private bool HideInternalEquipmentSlotPayment(CharacterState owner, SkillProgramActivation activation) =>
        !ProgramInstructionResolver.Default.Features(activation).HasOperation(SkillProgramEffectOp.PayEquipmentSlotForNamedUse);
    private bool TryBeginEquipmentSlotNamedPlay(CharacterState actor, LegalAction action)
    {
        if (action.ConversionSource is not { } source || !IsEquipmentSlotNamedSkill(source.SkillId) || UnconsumedEquipmentSlotPayment(source) is { } paid &&
            !_resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == paid.FrameId)) return false;
        var original = _pendingDecision ?? new PendingDecision(DecisionKind.PlayCard, actor.Seat, "游龙", [], []);
        BeginEquipmentSlotNamedUse(actor, source, action.PlayedCardKind!.Value, EquipmentSlotNamedUseIntent.Play, original, action, null); return true;
    }
    private bool TryBeginEquipmentSlotNamedResponse(CharacterState actor, CardConversionSource source, PromptChoice choice, bool dyingUse)
    {
        if (!IsEquipmentSlotNamedSkill(source.SkillId) || UnconsumedEquipmentSlotPayment(source) is { } paid &&
            !_resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == paid.FrameId)) return false;
        var kind = ViewAsRule(source)!.OutputKind;
        var intent = dyingUse ? kind == CardKind.Peach ? EquipmentSlotNamedUseIntent.DyingPeach : EquipmentSlotNamedUseIntent.DyingAlcohol :
            kind == CardKind.Nullification ? EquipmentSlotNamedUseIntent.Nullification : EquipmentSlotNamedUseIntent.Dodge;
        BeginEquipmentSlotNamedUse(actor, source, kind, intent, _pendingDecision!, null, choice); return true;
    }
    private bool TryBeginEquipmentSlotNamedForcedSlash(CharacterState actor, CardConversionSource source, CardKind kind, PromptChoice choice, bool qinglong)
    {
        if (!IsEquipmentSlotNamedSkill(source.SkillId) || UnconsumedEquipmentSlotPayment(source) is { } paid &&
            !_resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == paid.FrameId)) return false;
        BeginEquipmentSlotNamedUse(actor, source, kind, qinglong ? EquipmentSlotNamedUseIntent.Qinglong : EquipmentSlotNamedUseIntent.BorrowedSword,
            _pendingDecision!, null, choice); return true;
    }
    private static string EquipmentSlotNamedTargetSignature(LegalAction? action, PromptChoice? choice) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join(',', action?.TargetSeats ?? choice?.Targets ?? []) + ":" + action?.TargetCardId)));
    private static string EquipmentSlotNamedDecisionSignature(PendingDecision decision) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(decision))));
    private bool EquipmentSlotNamedChoiceIsPayable(CharacterState actor, CardConversionSource source, CardKind kind,
        EquipmentSlotNamedUseIntent intent, IReadOnlyList<int> targets, EquipmentSlot slot)
    {
        if (actor.EquipmentSlotCapacity(slot) <= 0 || intent is EquipmentSlotNamedUseIntent.BorrowedSword or EquipmentSlotNamedUseIntent.Qinglong && slot == EquipmentSlot.Weapon) return false;
        if (kind == CardKind.Snatch)
            return targets.Count == 1 && (HasPhaseSuitAllowance(actor.Seat, Suit.None) || HasCardDistanceExemption(actor, _players[targets[0]], kind) ||
                HasProvenanceUseDistance(actor, []) || HasCardPolicy(actor, SkillProgramCardPolicyKind.IgnoreUseDistance, kind) ||
                ConvertRuleValue(EvaluateDistance(actor, _players[targets[0]], excludedEquipmentSlot: slot)) == 1);
        if (!IsSlashCard(kind)) return true;
        var logical = new Card(0, kind, Suit.None, 0);
        return targets.Count > 0 && targets.All(target => CanUseSlashTarget(actor, _players[target], logical, source, kind,
            noEffectiveRank: true, physicalCardIds: [], excludedEquipmentSlot: slot) &&
            (intent is EquipmentSlotNamedUseIntent.Qinglong or EquipmentSlotNamedUseIntent.BorrowedSword or EquipmentSlotNamedUseIntent.SayingSlash ||
             CanSpendSlashUse(actor, _players[target], false, kind, effectiveRank: 0, excludedEquipmentSlot: slot, physicalCardIds: [])));
    }
    private bool IsEquipmentSlotNamedActionPayable(CharacterState actor, LegalAction action) =>
        action.ConversionSource is not { } source || !IsEquipmentSlotNamedSkill(source.SkillId) ||
        Enum.GetValues<EquipmentSlot>().Any(slot => EquipmentSlotNamedChoiceIsPayable(actor, source, action.PlayedCardKind!.Value,
            EquipmentSlotNamedUseIntent.Play, action.TargetSeats, slot));
    private bool IsEquipmentSlotNamedForcedSlashPayable(CharacterState actor, CardConversionSource source, CardKind kind, CharacterState target, bool qinglong) =>
        !IsEquipmentSlotNamedSkill(source.SkillId) ||
        UnconsumedEquipmentSlotPayment(source) is { } paid && !_resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == paid.FrameId) ||
        Enum.GetValues<EquipmentSlot>().Any(slot => EquipmentSlotNamedChoiceIsPayable(actor, source, kind,
            qinglong ? EquipmentSlotNamedUseIntent.Qinglong : EquipmentSlotNamedUseIntent.BorrowedSword, [target.Seat], slot));
    private void BeginEquipmentSlotNamedUse(CharacterState actor, CardConversionSource source, CardKind kind,
        EquipmentSlotNamedUseIntent intent, PendingDecision original, LegalAction? action, PromptChoice? choice)
    {
        var program = _contentRegistry.GetSkill(source.SkillId).Program!;
        var rule = ViewAsRule(source)!;
        var instance = GetSkillBindingShard(actor).ProgramInstances.Single(i => i.SkillId == source.SkillId && i.SkillInstanceId == source.SkillInstanceId);
        if (!TryGetEquipmentSlotNamedConversionAvailability(actor, instance, rule, intent != EquipmentSlotNamedUseIntent.Play,
                intent is EquipmentSlotNamedUseIntent.DyingPeach or EquipmentSlotNamedUseIntent.DyingAlcohol, out var available) || !available)
            throw new InvalidOperationException("Youlong lost its actual current face, unused name, round allowance or remaining equipment slot.");
        var parent = _resolutionStack.LastOrDefault()?.Id;
        var frame = new ProgramSkillFrame(++_resolutionSequence, actor.Seat, source.SkillId, "equipment-slot-payment", program.GameplayHash, 1, [], [])
        { SkillInstanceId = source.SkillInstanceId, EquipmentSlotNamedUse = new()
            { Source = source, GameplayHash = program.GameplayHash, Intent = intent, EffectiveKind = kind,
                Yin = EquipmentSlotNamedYin(actor.Seat, source.SkillId, source.SkillInstanceId), RoundNumber = _roundNumber,
                ActualTurn = _turnNumber, ActualTurnOwner = _turnProgression.OwnerSeat, PhaseInstance = _cardUseDebitPhaseInstanceId,
                OriginalDecision = original, PlayAction = action, ResponseChoice = choice, OriginalParentId = parent,
                TargetSignature = EquipmentSlotNamedTargetSignature(action, choice), OriginalDecisionSignature = EquipmentSlotNamedDecisionSignature(original) } };
        ClearPendingDecision(); PushRuntimeFrame(frame);
        AdvanceEventRulesAndQueueFact(new EquipmentSlotNamedUseStartedEvent(frame.Id, source, program.GameplayHash, intent, kind,
            frame.EquipmentSlotNamedUse.Yin, _roundNumber, original.PromptId, original.Revision, parent,
            frame.EquipmentSlotNamedUse.TargetSignature, _turnNumber, _turnProgression.OwnerSeat, _cardUseDebitPhaseInstanceId)
            { OriginalDecisionSignature = frame.EquipmentSlotNamedUse.OriginalDecisionSignature });
        PublishEquipmentSlotNamedChoice(frame);
    }
    private void PublishEquipmentSlotNamedChoice(ProgramSkillFrame frame)
    {
        var actor = _players[frame.OwnerSeat]; var draft = frame.EquipmentSlotNamedUse!;
        var targets = draft.PlayAction?.TargetSeats ?? draft.ResponseChoice?.Targets ?? [];
        var choices = Enum.GetValues<EquipmentSlot>().Where(s => EquipmentSlotNamedChoiceIsPayable(actor, draft.Source, draft.EffectiveKind, draft.Intent, targets, s)).Select(slot =>
            new PromptChoice(new($"equipment-slot-use.{frame.Id}.{slot}"), $"废除{EquipmentSlotNamedLabel(slot)}栏，视为使用【{CardCatalog.Get(draft.EffectiveKind).DisplayName}】", [], [],
                new Dictionary<string, string> { ["program-action"] = "equipment-slot-named-use", ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture), ["equipment-slot"] = slot.ToString() })).ToArray();
        if (choices.Length == 0) throw new InvalidOperationException("An accepted slot conversion lost every payable equipment slot before payment.");
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat, "游龙：选择废除的装备栏。", [], [])
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = Array.AsReadOnly(choices), SkillPrompt = new(frame.SkillId, "游龙", "游龙", _contentRegistry.GetSkill(frame.SkillId).Description) };
        _status = actor.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAiChoice(SkillProgramEffectOp.PayEquipmentSlotForNamedUse)]
    private PromptChoice SelectAiEquipmentSlotNamedUse(PendingDecision prompt, ProgramSkillFrame frame) => prompt.Choices.First();
    [ProgramAnswer("equipment-slot-named-use")]
    private void PayEquipmentSlotNamedUse(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { EquipmentSlotNamedUse: { Stage: EquipmentSlotNamedUseStage.ChoosingSlot } draft } frame ||
            _pendingDecision?.PlayerSeat != frame.OwnerSeat || choice.Cards.Count != 0 || choice.Targets.Count != 0 ||
            choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(CultureInfo.InvariantCulture) ||
            !Enum.TryParse<EquipmentSlot>(choice.Parameters.GetValueOrDefault("equipment-slot"), out var slot) ||
            !EquipmentSlotNamedChoiceIsPayable(_players[frame.OwnerSeat], draft.Source, draft.EffectiveKind, draft.Intent,
                draft.PlayAction?.TargetSeats ?? draft.ResponseChoice?.Targets ?? [], slot)) throw new InvalidOperationException("Youlong requires its exact remaining slot choice on its original unpaid frame.");
        var actor = _players[frame.OwnerSeat]; var policy = ViewAsRule(draft.Source)!.TieredRoundConversion!;
        if (!_skillRuntimeState.TryConsumeUsage(actor.Seat, draft.Source.SkillId, TieredRoundUsage(policy), SkillUsageScope.Round, 1))
            throw new InvalidOperationException("A Youlong face cannot pay its actual-round allowance twice.");
        var before = _movementSequence; var previous = actor.EquipmentSlotCapacity(slot);
        var payment = new EquipmentSlotNamedUsePayment(frame.Id, draft.Source, draft.GameplayHash, draft.RoundNumber, draft.ActualTurn,
            draft.ActualTurnOwner, draft.PhaseInstance, draft.Yin, draft.EffectiveKind, slot, previous, before, before,
            draft.Intent, draft.OriginalDecision.PromptId, draft.OriginalDecision.Revision, draft.OriginalParentId, draft.TargetSignature);
        ClearPendingDecision();
        ReplaceRuntimeTop(frame = frame with { EquipmentSlotNamedUse = draft with { Stage = EquipmentSlotNamedUseStage.MovementChildren, Payment = payment } });
        SetProgramBooleanState(actor.Seat, draft.Source.SkillId, draft.Source.SkillInstanceId, "yin", !draft.Yin);
        SetEquipmentSlotCapacity(actor, slot, 0);
        payment = payment with { After = _movementSequence };
        ReplaceRuntimeTop(frame = GetActiveProgramFrame(frame.Id) with { EquipmentSlotNamedUse = frame.EquipmentSlotNamedUse! with { Payment = payment },
            PendingMovementContinuation = payment.After > payment.Before ? new(frame.OwnerSeat, 0, null) : null });
        AdvanceEventRulesAndQueueFact(new EquipmentSlotNamedUsePaidEvent(payment));
        ResumeEquipmentSlotNamedUse(frame.Id);
    }
    private bool TryConsumePaidEquipmentSlotNamedConversion(CardConversionSource source)
    {
        if (!IsEquipmentSlotNamedSkill(source.SkillId)) return false;
        var paid = UnconsumedEquipmentSlotPayment(source) ?? throw new InvalidOperationException("The native Youlong use has no exact unconsumed paid slot invoice.");
        var policy = ViewAsRule(source)!.TieredRoundConversion!;
        if (_resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == paid.FrameId) ||
            _skillRuntimeState.GetUsage(source.OwnerSeat, source.SkillId, TieredRoundUsage(policy), SkillUsageScope.Round) != 1)
            throw new InvalidOperationException("The native Youlong use cannot start before its once-paid cost frame returns.");
        AdvanceEventRulesAndQueueFact(new EquipmentSlotNamedUseConsumedEvent(paid.FrameId)); return true;
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 26100)]
    private void ObserveEquipmentSlotNamedNativeIssue(IGameEvent payload)
    {
        if (payload is not TieredRoundConversionUseIssuedEvent { Receipt: var receipt } || !IsEquipmentSlotNamedSkill(receipt.Source.SkillId)) return;
        var paid = CompleteProgramEventHistory().OfType<EquipmentSlotNamedUsePaidEvent>().Select(e => e.Payment).LastOrDefault(p =>
            p.Source == receipt.Source && p.RoundNumber == receipt.RoundNumber && p.EffectiveKind == receipt.EffectiveKind &&
            p.GameplayHash == receipt.GameplayHash && p.ActualTurn == receipt.ActualTurnNumber && p.ActualTurnOwner == receipt.ActualTurnOwnerSeat &&
            p.PhaseInstance == receipt.PhaseInstanceId && receipt.ActorSeat == p.Source.OwnerSeat && receipt.MaterialCount == 0 && receipt.FrozenTier == 2 &&
            receipt.StateId == EquipmentSlotNamedTier && receipt.UsageId == (p.Yin ? "yin" : "yang") &&
            CompleteProgramEventHistory().OfType<EquipmentSlotNamedUseConsumedEvent>().Any(e => e.PaymentFrameId == p.FrameId) &&
            !CompleteProgramEventHistory().OfType<EquipmentSlotNamedUseIssuedEvent>().Any(e => e.PaymentFrameId == p.FrameId)) ??
            throw new InvalidOperationException("An issued native Youlong use lost its exact consumed slot payment.");
        AdvanceEventRulesAndQueueFact(new EquipmentSlotNamedUseIssuedEvent(paid.FrameId, receipt.OwnerFrameId, receipt.CardActionId));
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 26100)]
    private bool ResumeEquipmentSlotNamedUse(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { EquipmentSlotNamedUse: { } draft } frame || frame.Id != id) return false;
        if (draft.Stage == EquipmentSlotNamedUseStage.ChoosingSlot) { if (_pendingDecision is null) PublishEquipmentSlotNamedChoice(frame); return true; }
        if (draft.Payment is not { } paid) throw new InvalidOperationException("The paid slot stage lost its own invoice.");
        if (DrainEquipmentSlotNamedChildren(frame.Id)) return true;
        FinishProgramSkill(frame with { PendingMovementContinuation = null }, true);
        var actor = _players[frame.OwnerSeat];
        if (!actor.IsAlive || _winner != Winner.None)
        {
            AdvanceEventRulesAndQueueFact(new EquipmentSlotNamedUseCancelledEvent(paid.FrameId));
            if (draft.Intent == EquipmentSlotNamedUseIntent.SayingSlash && draft.OriginalParentId is { } sayingParent &&
                _resolutionStack.LastOrDefault() is ProgramSkillFrame saying && saying.Id == sayingParent)
            { FinishSayingSuppliedSlashFailure(saying); return true; }
            if (draft.OriginalParentId is { } pingduanParent) TryCancelKeBiNengPingduanBasicPayment(pingduanParent);
            return true;
        }
        if (draft.Intent is EquipmentSlotNamedUseIntent.DyingPeach or EquipmentSlotNamedUseIntent.DyingAlcohol &&
            ActiveDying is { } rescued && _players[rescued.VictimSeat].Hp > 0)
        {
            AdvanceEventRulesAndQueueFact(new EquipmentSlotNamedUseCancelledEvent(paid.FrameId));
            ClearPendingDecision(); CompleteDying(rescued, survived: true); return true;
        }
        if (draft.Intent == EquipmentSlotNamedUseIntent.Play && !EquipmentSlotNamedPlayStillLegal(actor, draft))
        {
            AdvanceEventRulesAndQueueFact(new EquipmentSlotNamedUseCancelledEvent(paid.FrameId));
            ClearPendingDecision(); _status = EngineStatus.Running;
            if (draft.OriginalParentId is { } pingduanParent && TryCancelKeBiNengPingduanBasicPayment(pingduanParent)) return true;
            if (actor.IsHuman && _phase == TurnPhase.Play && _currentSeat == actor.Seat) RequestHumanPlay();
            return true;
        }
        if (draft.Intent == EquipmentSlotNamedUseIntent.SayingSlash && !SayingEquipmentSlotUseStillLegal(actor, draft))
        {
            AdvanceEventRulesAndQueueFact(new EquipmentSlotNamedUseCancelledEvent(paid.FrameId));
            ClearPendingDecision(); _status = EngineStatus.Running;
            if (draft.OriginalParentId is { } sayingParent && _resolutionStack.LastOrDefault() is ProgramSkillFrame saying && saying.Id == sayingParent)
                FinishSayingSuppliedSlashFailure(saying);
            return true;
        }
        _pendingDecision = draft.OriginalDecision;
        if (draft.Intent == EquipmentSlotNamedUseIntent.SayingSlash) ResolveSayingTieredSlash(draft.ResponseChoice!);
        else if (draft.Intent == EquipmentSlotNamedUseIntent.Play)
        { ClearPendingDecision(); ExecuteTieredRoundZeroPlay(actor, draft.PlayAction!); }
        else if (draft.Intent == EquipmentSlotNamedUseIntent.Dodge) ResolveTieredRoundZeroDodge(actor, draft.Source);
        else if (draft.Intent == EquipmentSlotNamedUseIntent.Nullification) ResolveTieredRoundZeroCounterspell(actor, draft.Source);
        else if (draft.Intent is EquipmentSlotNamedUseIntent.DyingPeach or EquipmentSlotNamedUseIntent.DyingAlcohol)
            ResolveTieredRoundZeroResponse(draft.ResponseChoice!, false);
        else TryResolveTieredRoundZeroForcedSlash(draft.ResponseChoice!, false);
        return true;
    }
    private bool EquipmentSlotNamedPlayStillLegal(CharacterState actor, EquipmentSlotNamedUseDraft draft)
    {
        if (IsCardUseForbidden(actor.Seat, draft.EffectiveKind, CardActionType.Use) || draft.PlayAction is not { } action) return false;
        var kind = draft.EffectiveKind;
        if (IsOrdinaryTrick(kind)) return BuildProgramOrdinaryTrickUseOptions(actor, kind, Suit.None, enforceUsePermission: true,
            beneficiaryShieldSuit: Suit.None, actualEffectiveColor: null, hasActualColor: true, physicalCardIds: [], includeNextActualUseAdjustment: true)
            .Any(o => o.ActionKind == action.Kind && o.TargetCardId == action.TargetCardId && o.TargetSeats.SequenceEqual(action.TargetSeats));
        if (IsSlashCard(kind)) return action.TargetSeats.Count > 0 && action.TargetSeats.All(t => CanUseSlashTarget(actor, _players[t], new(0, kind, Suit.None, 0), draft.Source,
            kind, noEffectiveRank: true, physicalCardIds: []) && CanSpendSlashUse(actor, _players[t], IgnoresProgramSlashLimit(actor, draft.Source), kind, effectiveRank: 0, physicalCardIds: []));
        if (kind == CardKind.Peach) return IsRuleWounded(actor) && !HasSelfCardTargetProhibition(actor.Seat) && !HasBeneficiarySuitShield(actor.Seat, actor.Seat, Suit.None);
        return kind == CardKind.Alcohol && !actor.HasAlcoholEffect && !HasSelfCardTargetProhibition(actor.Seat) && !HasBeneficiarySuitShield(actor.Seat, actor.Seat, Suit.None);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 26100)]
    private bool ReturnEquipmentSlotNamedUseMovement(ProgramSkillFrame frame)
    {
        if (frame.EquipmentSlotNamedUse is null) return false;
        return ResumeEquipmentSlotNamedUse(frame.Id);
    }
    private bool IsEquipmentSlotNamedUseMovement(ProgramSkillFrame frame, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        pending.SubjectSeat == frame.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null &&
        (frame.EquipmentSlotNamedUse is { Stage: EquipmentSlotNamedUseStage.MovementChildren, Payment: { } p } && p.FrameId == frame.Id && effect?.Op == SkillProgramEffectOp.PayEquipmentSlotForNamedUse ||
         frame.Luanfeng is { Stage: LuanfengStage.DrawChildren } && effect?.Op == SkillProgramEffectOp.ResolveLuanfengDyingRecovery);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 26100)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 26100)]
    private bool ContinuePaidWoLongFengChu(ProgramSkillFrame frame) => frame.EquipmentSlotNamedUse?.Payment is not null || frame.Luanfeng is not null;
    private bool DrainEquipmentSlotNamedChildren(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
}
