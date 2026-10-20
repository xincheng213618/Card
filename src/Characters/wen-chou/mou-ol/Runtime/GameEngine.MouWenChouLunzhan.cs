using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string MouWenChouDrawReason = "skill-program.mou-ol:lunzhan.draw", MouWenChouDiscardReason = "skill-program.mou-ol:juejue.discard";
    private bool HasMouWenChouOl => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouWenChouJuejue) || _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.UseMouWenChouLunzhan);
    private static CardConversionSource MouWenChouSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool MouWenChouCountUsed(int owner, int count) => ProgramEventHistory<MouWenChouLunzhanIssuedEvent>().Any(e => e.Origin.Source.OwnerSeat == owner && e.Origin.ActualTurn == _turnNumber && e.Origin.MaterialCount == count);
    private bool MouWenChouTargetBanned(int owner, int target) => ProgramEventHistory<MouWenChouRewardAcceptedEvent>().Any(e => e.OwnerSeat == owner && e.TargetSeat == target && e.ActualTurn == _turnNumber);
    [ProgramCapability] private sealed class MouWenChouOlCapability(GameEngine engine) : IMouWenChouOlHost, IMouWenChouOlSelection
    {
        public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => op == SkillProgramEffectOp.UseMouWenChouLunzhan ? engine.BeginMouWenChouLunzhan(f) : engine.BeginMouWenChouTrigger(f, op);
        public bool Target(int owner, int target) => !engine.MouWenChouTargetBanned(owner, target);
    }
    [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart, 33100, SkillProgramEffectOp.UseMouWenChouLunzhan)]
    private bool CanActivateMouWenChouLunzhan(ProgramActivationLegalityContext c) => HasOngoingWholeActualUseTurn && c.OwnerSeat == _currentSeat && _phase == TurnPhase.Play && !MouWenChouCountUsed(c.OwnerSeat, c.Activation.MinCards) && _players.Any(p => p.IsAlive && p.Seat != c.OwnerSeat && !MouWenChouTargetBanned(c.OwnerSeat, p.Seat));
    private bool ValidMouWenChouOl(ProgramSkillFrame f) => f.MouWenChouOl is { } r && r.Source == MouWenChouSource(f) && r.GameplayHash == f.GameplayHash && f.InstructionIndex == 1 && Enum.IsDefined(r.Stage) &&
        _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash == f.GameplayHash && GetPausedPrivateOfferEffect(f).Op == r.Operation &&
        ProgramEventHistory<MouWenChouStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == f.GameplayHash && e.Operation == r.Operation && e.UseId == (r.Operation == SkillProgramEffectOp.UseMouWenChouLunzhan ? 0 : r.UseId)) == 1 &&
        (r.Operation == SkillProgramEffectOp.UseMouWenChouLunzhan ? f.TriggerId is null && f.ActivationId == r.Source.BindingId && f.WindowContext is null && f.SelectedCardIds.Count is >= 0 and <= 5 && f.SelectedTargetSeats.Count == 1 : f.ActivationId == f.TriggerId && f.WindowContext?.CardUse?.ParentCardUseFrameId == r.UseId);
    private Card[] MouWenChouMaterials(ProgramSkillFrame f) => f.SelectedCardIds.Select(id =>
    {
        var location = _cardZones.GetLocation(id);
        if (location.OwnerSeat != f.OwnerSeat || location.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)) throw new InvalidOperationException("Lunzhan requires its actual owned HE materials.");
        var card = _cardZones.CardsAt(location).Single(c => c.Id == id);
        if (IsTurnHandCardRestricted(_players[f.OwnerSeat], card) || IsTurnPhysicalUseForbidden(f.OwnerSeat, [id])) throw new InvalidOperationException("Lunzhan cannot bypass a native material restriction.");
        return card;
    }).ToArray();
    private IReadOnlyList<ProgramOrdinaryTrickUseOption> MouWenChouOptions(ProgramSkillFrame f)
    {
        var cards = MouWenChouMaterials(f); var owner = _players[f.OwnerSeat];
        var suits = cards.Select(c => EffectiveSuit(owner, c)).Distinct().ToArray();
        var suit = suits.Length == 1 ? suits[0] : Suit.None;
        var colors = cards.Select(c => CapturePhysicalCardColor(f.OwnerSeat, c)).Distinct().ToArray();
        var color = colors.Length == 1 ? colors[0] : null;
        return Array.AsReadOnly(BuildProgramOrdinaryTrickUseOptions(owner, CardKind.Duel, suit, enforceUsePermission: true, beneficiaryShieldSuit: suit,
            actualEffectiveColor: color, hasActualColor: true, physicalCardIds: f.SelectedCardIds, includeNextActualUseAdjustment: true, conversionSource: MouWenChouSource(f))
            .Where(o => o.TargetSeats.Count > 0 && o.TargetSeats[0] == f.SelectedTargetSeats[0] && o.TargetSeats.All(t => !MouWenChouTargetBanned(f.OwnerSeat, t))).ToArray());
    }
    private SkillProgramStepOutcome BeginMouWenChouLunzhan(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id); var amount = GetPausedPrivateOfferEffect(f).Amount;
        if (f.MouWenChouOl is not null || f.TriggerId is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != amount || f.SelectedCardIds.Distinct().Count() != amount || f.SelectedTargetSeats.Count != 1 ||
            !HasOngoingWholeActualUseTurn || _currentSeat != f.OwnerSeat || _phase != TurnPhase.Play || MouWenChouCountUsed(f.OwnerSeat, amount)) throw new InvalidOperationException("Lunzhan requires an unused actual-turn material count and its own live Play activation.");
        var r = new MouWenChouOlReceipt(MouWenChouSource(f), f.GameplayHash, SkillProgramEffectOp.UseMouWenChouLunzhan, MouWenChouOlStage.Targets) { ActualTurn = _turnNumber, TargetSeat = f.SelectedTargetSeats[0] };
        ReplaceRuntimeTop(f = f with { MouWenChouOl = r }); AdvanceEventRulesAndQueueFact(new MouWenChouStartedEvent(f.Id, r.Source, r.GameplayHash, r.Operation, 0));
        var options = MouWenChouOptions(f);
        if (options.Count == 0) { FinishMouWenChouOl(f); return SkillProgramStepOutcome.AwaitChild; }
        if (options.Count == 1) { IssueMouWenChouDuel(f, options[0]); return SkillProgramStepOutcome.AwaitChild; }
        PublishMouWenChouOl(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private Card[] MouWenChouDiscardable(int owner) => GetHand(_players[owner]).Concat(GetEquipment(owner)).Where(c =>
        !IsForeignEquipmentDiscardPrevented(owner, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard) && !IsSelfHandCategoryDiscardForbidden(owner, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard)).ToArray();
    private IReadOnlyList<PromptChoice> MouWenChouChoices(ProgramSkillFrame f)
    {
        var r = f.MouWenChouOl!;
        PromptChoice Choice(string branch, string label, int? card = null, ProgramOrdinaryTrickUseOption? option = null) => new(new($"mou-wen-chou.{f.Id}.{r.Stage}.{branch}.{card}.{option?.Id.Value}"), label, card is { } id ? [id] : [], option?.TargetSeats ?? [],
            new Dictionary<string, string> { ["program-action"] = "mou-wen-chou-ol", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch, ["option-id"] = option?.Id.Value ?? "" });
        if (r.Stage == MouWenChouOlStage.Targets) return Array.AsReadOnly(MouWenChouOptions(f).Select(o => Choice("duel", o.Description, option: o)).ToArray());
        if (r.Stage == MouWenChouOlStage.Reward) return [Choice("draw", "摸X张牌，本回合不能再轮战此目标"), Choice("decline", "不摸牌")];
        return r.Stage == MouWenChouOlStage.Discard && _players[r.TargetSeat].IsAlive ? Array.AsReadOnly(MouWenChouDiscardable(r.TargetSeat).Select(c => Choice("discard", $"弃置【{c.DisplayName}】", c.Id)).ToArray()) : [];
    }
    private void PublishMouWenChouOl(ProgramSkillFrame f)
    {
        var r = f.MouWenChouOl!; var choices = MouWenChouChoices(f); if (choices.Count == 0) { FinishMouWenChouOl(f); return; }
        var chooser = r.Stage == MouWenChouOlStage.Discard ? r.TargetSeat : f.OwnerSeat; var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, r.Stage == MouWenChouOlStage.Discard ? $"决绝：还需弃置{r.Amount - r.Discarded}张牌。" : r.Stage == MouWenChouOlStage.Reward ? "轮战：是否摸牌并停止本回合轮战此目标？" : "轮战：选择本次决斗的原生目标调整。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = chooser, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("mou-wen-chou-ol")]
    private void AnswerMouWenChouOl(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouWenChouOl: { } r } f || !ValidMouWenChouOl(f) || _pendingDecision?.PlayerSeat != (r.Stage == MouWenChouOlStage.Discard ? r.TargetSeat : f.OwnerSeat) || !MouWenChouChoices(f).Any(c => AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Mou Wen Chou requires its current chooser and exact live native option.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect }); var branch = choice.Parameters["branch"];
        if (branch == "decline") { FinishMouWenChouOl(f); return; }
        if (branch == "duel") { IssueMouWenChouDuel(f, MouWenChouOptions(f).Single(o => o.Id.Value == choice.Parameters["option-id"])); return; }
        if (branch == "draw")
        {
            var x = MouWenChouTargetCount(f.OwnerSeat, r.TargetSeat);
            ReplaceRuntimeTop(f = f with { MouWenChouOl = r with { Amount = x, Stage = MouWenChouOlStage.Drawing } });
            AdvanceEventRulesAndQueueFact(new MouWenChouRewardAcceptedEvent(f.Id, r.UseId, f.OwnerSeat, _turnNumber, r.TargetSeat, x));
            BeginOperationProgramDraw(f, f.OwnerSeat, x, new(MouWenChouDrawReason)); return;
        }
        if (branch != "discard" || r.Stage != MouWenChouOlStage.Discard || !MouWenChouDiscardable(r.TargetSeat).Any(c => c.Id == choice.Cards.Single())) throw new InvalidOperationException("Juejue must discard the current recipient's real discardable HE entity.");
        var before = _movementSequence; var card = choice.Cards.Single();
        ReplaceRuntimeTop(f = f with { MouWenChouOl = r with { Stage = MouWenChouOlStage.DiscardChildren, Before = before, After = before, Discarded = r.Discarded + 1 }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramLogicalDiscard(f, r.TargetSeat, [card], new(MouWenChouDiscardReason));
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { MouWenChouOl = f.MouWenChouOl! with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new MouWenChouDiscardPaidEvent(f.Id, r.TargetSeat, card, before, _movementSequence)); ContinueMouWenChouOl(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.UseMouWenChouLunzhan, SkillProgramEffectOp.RewardMouWenChouLunzhan, SkillProgramEffectOp.ResolveMouWenChouJuejue)]
    private PromptChoice ChooseMouWenChouOl(PendingDecision pending, ProgramSkillFrame frame)
    {
        var options = pending.Choices!;
        if (options.Any(c => c.Parameters.GetValueOrDefault("branch") == "draw")) return options.First(c => c.Parameters["branch"] == "draw");
        return options.OrderBy(c => c.Cards.Count == 1 ? GetKeepValue(EntityAtCurrentLocation(c.Cards[0]), _players[pending.PlayerSeat]) : 0).First();
    }
    private bool DrainMouWenChouOl(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueMouWenChouOl(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouWenChouOl: { } r } f || f.Id != id) return;
        if (!ValidMouWenChouOl(f)) throw new InvalidOperationException("Mou Wen Chou lost its owning frame or signed continuation.");
        if (r.Stage is MouWenChouOlStage.Targets or MouWenChouOlStage.Reward or MouWenChouOlStage.Discard) { PublishMouWenChouOl(f); return; }
        if (r.Stage is MouWenChouOlStage.Issuing or MouWenChouOlStage.NativeUse or MouWenChouOlStage.Drawing) return;
        if (DrainMouWenChouOl(id)) return; f = GetActiveProgramFrame(id); r = f.MouWenChouOl!;
        if (r.Stage == MouWenChouOlStage.DiscardChildren && r.Discarded < r.Amount && _players[r.TargetSeat].IsAlive && MouWenChouDiscardable(r.TargetSeat).Length > 0)
        { ReplaceRuntimeTop(f = f with { MouWenChouOl = r with { Stage = MouWenChouOlStage.Discard }, PendingMovementContinuation = null }); PublishMouWenChouOl(f); return; }
        FinishMouWenChouOl(f);
    }
    private void FinishMouWenChouOl(ProgramSkillFrame f)
    {
        var r = f.MouWenChouOl!; AdvanceEventRulesAndQueueFact(new MouWenChouCompletedEvent(f.Id, r.Operation));
        ReplaceRuntimeTop(f = f with { MouWenChouOl = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 33100)]
    private bool ResumeMouWenChouOl(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouWenChouOl: not null } f || f.Id != id) return false;
        if (_pendingDecision is null) ContinueMouWenChouOl(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 33100)] private bool ReturnMouWenChouMovement(ProgramSkillFrame f) => f.MouWenChouOl is not null && ResumeMouWenChouOl(f.Id);
}
internal interface IMouWenChouOlSelection : IProgramSelectionQuery { bool Target(int owner, int target); }
[ProgramSelectionModule(ProgramSelectionStage.OrdinaryTarget, 33100, SkillProgramEffectOp.UseMouWenChouLunzhan)]
internal sealed class MouWenChouTargetFilter : IProgramSelectionTargetFilter
{ public bool Allows(ProgramSelectionContext c, int target, IProgramSelectionQueries queries) => queries.Get<IMouWenChouOlSelection>().Target(c.OwnerSeat, target); }
