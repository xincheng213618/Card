namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string WushuangZengou = "wushuang:skill-3287";
    [ProgramCapability]
    private sealed class WushuangZengouCapability(GameEngine engine) : IWushuangZengouHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f) => engine.BeginWushuangZengou(f); }

    private ProgramCardTriggerWindowFrame? WushuangZengouWindow(ProgramSkillWindowContext? context)
    {
        var w = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == context?.ParentFrameId);
        if (w is not { ResponseCompletion: { CostsDrained: true, OriginalContinuation: ProgramCardContinuation.Dodge } completion } ||
            w.Action is not { EffectiveKind: CardKind.Dodge, Type: CardActionType.Response } action ||
            action.ActorSeat != action.ProviderSeat || action.RequesterSeat is not null || action.ResponderSeat != action.ActorSeat ||
            w.AttackOwnerFrameId is not { } parent || completion.AttackOwnerFrameId != parent || completion.ActionId != action.ActionId ||
            LifecycleCardUse(parent) is not { CardAttack: { EffectiveCardKind: { } kind } attack, Action: { } original } ||
            !IsSlashCard(kind) || attack.TargetSeat != action.ActorSeat || action.ParentActionId != original.ActionId ||
            action.OpponentSeat != attack.SourceSeat || !ValidResponseCompletionWindow(w)) return null;
        return w;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeCondition, 61220)]
    private bool CanTriggerWushuangZengou(ProgramTriggerCandidate c, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (c.SkillId != WushuangZengou) return true;
        return WushuangZengouWindow(context) is { } w && w.Action.ActorSeat != c.OwnerSeat && _players[w.Action.ActorSeat].IsAlive &&
            IsWithinAttackRange(c.OwnerSeat, w.Action.ActorSeat) &&
            LifecycleCardUse(w.AttackOwnerFrameId!.Value)?.WushuangZengouInvalidation?.Payment.ActionId != w.Action.ActionId;
    }
    private Card[] WushuangZengouCosts(ProgramSkillFrame f) => GetHand(_players[f.OwnerSeat]).Concat(GetEquipment(f.OwnerSeat)).Where(c =>
        CardCatalog.Get(_cardZones.GetLocation(c.Id) == CardLocation.Hand(f.OwnerSeat) ? AdvancedEffectiveHandKind(_players[f.OwnerSeat], c) : c.Kind).CategoryName != "基本牌" && !c.IsGeneralWeapon &&
        !IsForeignEquipmentDiscardPrevented(f.OwnerSeat, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard) &&
        !IsActiveProgramSourceEquipmentCard(f.OwnerSeat, f.SkillId, f.SkillInstanceId, c)).ToArray();
    private IReadOnlyList<PromptChoice> WushuangZengouChoices(ProgramSkillFrame f)
    {
        PromptChoice Choice(string key, string label, int[] cards) => new(new($"wushuang-zengou.{f.Id}.{key}"), label, cards, [],
            new Dictionary<string, string> { ["program-action"] = "wushuang-zengou", ["cost"] = key });
        var choices = WushuangZengouCosts(f).Select(c => Choice(c.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), $"弃置【{c.DisplayName}】", [c.Id])).ToList();
        if (_players[f.OwnerSeat].Hp > 0) choices.Add(Choice("hp", "失去1点体力", []));
        return Array.AsReadOnly(choices.ToArray());
    }
    private SkillProgramStepOutcome BeginWushuangZengou(ProgramSkillFrame f)
    {
        if (f.WushuangZengou is not null) throw new InvalidOperationException("Zengou cannot repeat a paid instruction.");
        var w = WushuangZengouWindow(f.WindowContext); if (w is null) return SkillProgramStepOutcome.Continue;
        var attack = LifecycleCardUse(w.AttackOwnerFrameId!.Value)!;
        ReplaceRuntimeTop(f = f with { WushuangZengou = new(f.InstructionIndex, w.Id, attack.Id, w.Action.ActionId,
            w.Action.ActorSeat, attack.CardAttack!.SuccessfulDodgeResponses)
            { Cards = w.Action.PhysicalCards.Where(c => w.ResponseCompletion!.NativeCosts.Any(m => m.CardId == c.CardId && m.To == CardLocation.DiscardPile)).Select(c => c.CardId).ToArray() } });
        PublishWushuangZengou(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private void PublishWushuangZengou(ProgramSkillFrame f)
    {
        var choices = WushuangZengouChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        if (choices.Count == 0) { CompleteWushuangZengou(f); return; }
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "谮构：支付代价令此张闪无效并获得之。",
            choices.SelectMany(c => c.Cards).ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("wushuang-zengou")]
    private void AnswerWushuangZengou(PromptChoice selected)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Zengou lost its program parent.");
        if (f.WushuangZengou is not { Stage: "choosing" } r || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([selected], WushuangZengouChoices(f).Where(c => c.Id == selected.Id).ToArray()) ||
            WushuangZengouWindow(f.WindowContext)?.Action.ActionId != r.ActionId)
            throw new InvalidOperationException("Zengou lost its exact unpaid Dodge use or published choice.");
        ClearPendingDecision();
        if (!_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            !IsWithinAttackRange(f.OwnerSeat, r.DodgerSeat)) { CompleteWushuangZengou(f); return; }
        var before = _movementSequence;
        var cardId = selected.Cards.Count == 1 ? selected.Cards[0] : (int?)null;
        var from = cardId is { } id ? _cardZones.GetLocation(id) : (CardLocation?)null;
        ReplaceRuntimeTop(f = f with { WushuangZengou = r with { Stage = "paid", CostCardId = cardId, CostFrom = from, Before = before, After = before } });
        if (cardId is { } paid)
        {
            MoveLogicalDiscardCards(f.OwnerSeat, f.OwnerSeat, [GetAdvancedCard(paid)], from!.Value, new("skill-program.wushuang-zengou.cost"));
            f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { WushuangZengou = f.WushuangZengou! with { After = _movementSequence } });
            PublishWushuangZengouPayment(f); AdvanceRuntimeProgram(f.Id); return;
        }
        // The receipt is already paid before genuine HP/Dying publishes; a resumed child never pays twice.
        PublishWushuangZengouPayment(f);
        var outcome = ApplyProgramHpLoss(f.Id, f.SkillId, f.OwnerSeat, 1);
        if (outcome == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(f.Id);
    }
    private WushuangZengouPaidEvent WushuangZengouPayment(ProgramSkillFrame f)
    {
        var r = f.WushuangZengou!;
        return new(f.Id, r.ResponseWindowId, r.AttackFrameId, r.ActionId, f.OwnerSeat, r.DodgerSeat, r.DodgeCursor, r.CostCardId, r.CostFrom, r.Before, r.After);
    }
    private void PublishWushuangZengouPayment(ProgramSkillFrame f) => AdvanceEventRulesAndQueueFact(WushuangZengouPayment(f));
    [ProgramContinuation(ProgramContinuationKind.Resume, 61220)]
    private bool ResumeWushuangZengou(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { WushuangZengou: { } r } f || f.Id != id) return false;
        if (_pendingDecision is not null) return true;
        if (r.Stage == "choosing") { PublishWushuangZengou(f); return true; }
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) ||
            TryBeginCardsMovedProgramWindow(id)) return true;
        f = GetActiveProgramFrame(id); r = f.WushuangZengou!;
        if (r.Stage == "paid")
        {
            var native = LifecycleCardUse(r.AttackFrameId) ?? throw new InvalidOperationException("Paid Zengou lost its actual Slash parent.");
            var proof = new WushuangZengouInvalidatedEvent(WushuangZengouPayment(f), BSource(f), f.GameplayHash);
            AdvanceEventRulesAndQueueFact(proof);
            UpdateLifecycleCardUse(native.Id, current => current with { WushuangZengouInvalidation = proof });
            ReplaceRuntimeTop(f = f with { WushuangZengou = r with { Stage = "gained" } });
            if (_players[f.OwnerSeat].IsAlive)
                foreach (var cardId in r.Cards.Where(cardId => _cardZones.GetLocation(cardId) == CardLocation.DiscardPile))
                {
                    MoveCard(GetAdvancedCard(cardId), CardLocation.DiscardPile, CardLocation.Hand(f.OwnerSeat), new("skill-program.wushuang-zengou.gain"));
                    AdvanceEventRulesAndQueueFact(new WushuangZengouGainedCardEvent(f.Id, r.ActionId, f.OwnerSeat, cardId, _movementSequence));
                }
            AdvanceRuntimeProgram(id); return true;
        }
        CompleteWushuangZengou(f); return true;
    }
    private void CompleteWushuangZengou(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f with { WushuangZengou = null }); AdvanceRuntimeProgram(f.Id); }

    private bool ContinueWushuangZengouDodge(CardAttackHandle attack, CardActionContext action, ProgramCardContinuation continuation)
    {
        if (continuation != ProgramCardContinuation.Dodge || LifecycleCardUse(attack.ResolutionId)?.WushuangZengouInvalidation is not { } proof ||
            proof.Payment.ActionId != action.ActionId) return false;
        if (proof.Payment.AttackFrameId != attack.ResolutionId || proof.Payment.DodgerSeat != attack.TargetSeat || action.ActorSeat != attack.TargetSeat ||
            action.EffectiveKind != CardKind.Dodge || action.ParentActionId != LifecycleCardUse(attack.ResolutionId)?.Action?.ActionId ||
            proof.Payment.DodgeCursor != attack.SuccessfulDodgeResponses || ProgramEventHistory<WushuangZengouPaidEvent>().Count(e => e == proof.Payment) != 1 ||
            ProgramEventHistory<WushuangZengouInvalidatedEvent>().Count(e => e == proof) != 1)
            throw new InvalidOperationException("An invalidated Dodge changed its exact native response cursor.");
        ClearHuanpeiDodgeResponse(attack); ClearRoundOnceCardlessDodgeResponse(attack); ClearTieredRoundDodgeResponse(attack);
        ClearChainedStateBasicDodgeResponse(attack); ClearDrawFundedDistinctBasicDodgeResponse(attack);
        SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        if (!ApplyAttackDamage(attack)) CompleteAttack(attack); return true;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 61220)]
    private void AssertWushuangZengou(ProgramSkillFrame f)
    {
        if (f.WushuangZengou is not { } r) return;
        var effect = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
        var w = WushuangZengouWindow(f.WindowContext);
        if (f.SkillId != WushuangZengou || effect.Op != SkillProgramEffectOp.ResolveWushuangZengou || r.InstructionIndex != f.InstructionIndex ||
            w is null || w.Id != r.ResponseWindowId || w.Action.ActionId != r.ActionId || w.AttackOwnerFrameId != r.AttackFrameId ||
            w.Action.ActorSeat != r.DodgerSeat || LifecycleCardUse(r.AttackFrameId)?.CardAttack?.SuccessfulDodgeResponses != r.DodgeCursor ||
            r.Cards.Distinct().Count() != r.Cards.Count || r.Cards.Any(card => !w.Action.PhysicalCards.Any(c => c.CardId == card)) ||
            r.Stage is not ("choosing" or "paid" or "gained") || r.Before < 0 || r.After < r.Before)
            throw new InvalidOperationException("Zengou lost its owning once-paid response return.");
        if (r.Stage != "choosing" && ProgramEventHistory<WushuangZengouPaidEvent>().Count(e => e == WushuangZengouPayment(f)) != 1)
            throw new InvalidOperationException("Zengou must retain exactly one cost invoice.");
        if (r.Stage != "choosing" && r.CostCardId is null &&
            ProgramEventHistory<ProgramSkillHpLostEvent>().Count(e => e.FrameId == f.Id && e.SkillId == f.SkillId && e.TargetSeat == f.OwnerSeat && e.Amount == 1) != 1 &&
            !ProgramEventHistory<IdentityGeneralAppearedEvent>().Any(e => e.Receipt.ParentFrameId == f.Id && e.Receipt.OwnerSeat == f.OwnerSeat && e.Receipt.Entry == IdentityAppearanceEntry.ProgramHpLoss && e.Receipt.RequestedDeduction == 1))
            throw new InvalidOperationException("Zengou lost its exact native HP-loss cost or identity replacement.");
        if (r.Stage != "choosing" && r.CostCardId is { } cardId && _cardMovements.Count(m => m.CardId == cardId && m.Sequence > r.Before &&
            m.Sequence <= r.After && GetProgramDiscardSource(m) == r.CostFrom && m.To == CardLocation.DiscardPile && m.Reason.Value.StartsWith("skill-program.wushuang-zengou.cost", StringComparison.Ordinal)) != 1)
            throw new InvalidOperationException("Zengou lost its exact physical non-basic discard cost.");
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveWushuangZengou)]
    private PromptChoice ChooseWushuangZengou(PendingDecision prompt, ProgramSkillFrame frame) => prompt.Choices[0];
}
