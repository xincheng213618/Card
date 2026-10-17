using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string WuyouBinding = "hand-contribution";
    private const string WuyouContributionReason = "skill-program.wumiao.wuyou.contribute";
    private const string WuyouReturnReason = "skill-program.wumiao.wuyou.return";
    private bool HasWuyouCapability => _contentRegistry.ProgramDependencies
        .HasActivationOperation(SkillProgramEffectOp.ResolveWuyouContributionAndNamedReturn);
    private ActualDiscardRecoveryPhaseKey? WuyouPlay(int donor) => HasWuyouCapability && _setupComplete &&
        _winner == Winner.None && IsValidPlayerSeat(donor) && _players[donor].IsAlive &&
        _phase == TurnPhase.Play && _currentSeat == donor && CurrentActualDiscardRecoveryPhase() is
            { Kind: ActualDiscardRecoveryPhaseKind.Play } phase && phase.ActorSeat == donor &&
        IsRecordedActualDiscardRecoveryPhase(phase) ? phase : null;
    private IEnumerable<(CardConversionSource Source, string Hash)> WuyouSources(CharacterState owner) =>
        GetSkillBindingShard(owner).ProgramInstances.Where(i => i.SkillId == "wumiao:wuyou" &&
            i.Program.Activations.Any(a => a.Id == WuyouBinding && a.Effects is
                [{ Op: SkillProgramEffectOp.ResolveWuyouContributionAndNamedReturn }]))
            .OrderBy(i => i.SkillInstanceId, StringComparer.Ordinal).Take(1)
            .Select(i => (new CardConversionSource(i.SkillId, WuyouBinding, owner.Seat, i.SkillInstanceId), i.Program.GameplayHash));
    private bool WuyouPhaseSpent(int issuer, int donor, ActualDiscardRecoveryPhaseKey phase) =>
        ProgramEventHistory<WuyouStartedEvent>().Any(e => e.Source.OwnerSeat == issuer && e.DonorSeat == donor && e.Phase == phase);

    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 28520,
        SkillProgramEffectOp.ResolveWuyouContributionAndNamedReturn)]
    private bool WuyouHasNativeContributionOnly(ProgramActivationLegalityContext context) => false;

    [ProgramActionSource(ProgramActionSourcePhase.ProgramTail, 28520)]
    private IEnumerable<LegalAction> BuildWuyouContributions(CharacterState donor)
    {
        if (WuyouPlay(donor.Seat) is not { } phase) yield break;
        var cards = GetHand(donor).Select(c => c.Id).Order().ToArray();
        if (cards.Length == 0) yield break;
        foreach (var owner in _players.Where(p => p.IsAlive).OrderBy(p => p.Seat))
        foreach (var (source, _) in WuyouSources(owner))
        {
            if (WuyouPhaseSpent(owner.Seat, donor.Seat, phase) || HasPrintedGeneralSkillTargetBan(source, donor.Seat)) continue;
            yield return new(LegalActionKind.UseProgramSkill, null, owner.Seat,
                $"武佑：交给 {owner.Name} 一张手牌", MinCardCount: 1, MaxCardCount: 1,
                MinTargetCount: 1, MaxTargetCount: 1)
            {
                ProgramSkillId = source.SkillId, ProgramActivationId = WuyouBinding,
                ProgramSkillOwnerSeat = owner.Seat, SelectableCardIds = Array.AsReadOnly(cards),
                SelectableTargetSeats = Array.AsReadOnly(new[] { owner.Seat }),
                ProgramAiHint = new(1, 0, 0, 0, 0, 0, true, false)
            };
        }
    }

    private LegalAction? CurrentWuyouContribution(CharacterState donor, string? skill, string? binding, int? owner) =>
        BuildWuyouContributions(donor).SingleOrDefault(a => a.ProgramSkillId == skill &&
            a.ProgramActivationId == binding && a.ProgramSkillOwnerSeat == owner);

    [ProgramActionHandler(ProgramActionHandlerKind.Submit, 28520)]
    private bool SubmitWuyouContribution(UseProgramSkillCommand command, out CommandResult result)
    {
        result = null!;
        if (command.SkillId != "wumiao:wuyou" || command.ActivationId != WuyouBinding) return false;
        if (ValidateHumanPrompt(command.ActorSeat, DecisionKind.PlayCard, command.PromptId, CommandErrorCode.IllegalAction) is { } error)
        { result = Reject(error.Code, error.Message); return true; }
        var donor = _players[command.ActorSeat];
        var action = CurrentWuyouContribution(donor, command.SkillId, command.ActivationId, command.SkillOwnerSeat);
        if (action is null)
        { result = Reject(CommandErrorCode.IllegalAction, "武佑必须来自当前出牌阶段的真实贡献菜单。"); return true; }
        if (ValidateProgramSelection(action, command.CardIds, command.TargetSeats) is { } invalid)
        { result = Reject(invalid.Code, invalid.Message); return true; }
        result = Accept(() =>
        {
            ClearPendingDecision(); ExecuteWuyouContribution(donor, action, command.CardIds, command.TargetSeats);
            AdvanceRulesAndPublishState(); if (_options.AdvanceAfterHumanCommands) AdvanceToHumanBoundary();
        });
        return true;
    }

    [ProgramActionHandler(ProgramActionHandlerKind.Execute, 28520)]
    private bool ExecuteWuyouContribution(CharacterState donor, LegalAction supplied,
        IReadOnlyList<int> cards, IReadOnlyList<int> targets)
    {
        if (supplied.ProgramSkillId != "wumiao:wuyou" || supplied.ProgramActivationId != WuyouBinding) return false;
        var action = CurrentWuyouContribution(donor, supplied.ProgramSkillId, supplied.ProgramActivationId,
            supplied.ProgramSkillOwnerSeat) ?? throw new InvalidOperationException("Wuyou's contribution source or actual Play changed.");
        if (ValidateProgramSelection(action, cards, targets) is { } invalid) throw new InvalidOperationException(invalid.Message);
        var owner = _players[action.ProgramSkillOwnerSeat!.Value];
        var source = WuyouSources(owner).Single();
        var card = GetHand(donor).Single(c => c.Id == cards.Single());
        var phase = WuyouPlay(donor.Seat)!;
        var receipt = new WuyouReceipt(source.Source, source.Hash, phase, donor.Seat,
            card.Id, card.Kind, _movementSequence)
        { Stage = WuyouStage.ContributionChildren, After = _movementSequence };
        var f = new ProgramSkillFrame(++_resolutionSequence, owner.Seat, source.Source.SkillId, WuyouBinding,
            source.Hash, 0, [], []) { SkillInstanceId = source.Source.SkillInstanceId, Wuyou = receipt };
        f = CaptureHpConditionalProgramSource(f); f = CaptureEquipmentSuitProgramSource(f);
        PushRuntimeFrame(f);
        AdvanceEventRulesAndQueueFact(new ProgramSkillStartedEvent(f.Id, f.OwnerSeat, f.SkillId, f.ActivationId));
        AdvanceEventRulesAndQueueFact(new WuyouStartedEvent(f.Id, receipt.Source, receipt.GameplayHash, phase,
            donor.Seat, card.Id, card.Kind, receipt.Before));
        AddLog("SkillTriggered", $"{donor.Name} 发起了 {owner.Name} 的【武佑】。", donor.Seat);
        AdvanceRuntimeProgram(f.Id); return true;
    }

    [ProgramCapability]
    private sealed class WuGuanYuWuyouCapability(GameEngine engine) : IWuGuanYuWuyouHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame) => engine.BeginWuyou(frame); }

    private SkillProgramStepOutcome BeginWuyou(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.Wuyou;
        if (r is null || !ValidWuyouFrame(f, false) || f.InstructionIndex != 1 || r.ContributionPaid ||
            WuyouPlay(r.DonorSeat) != r.Phase || !_players[f.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            _cardZones.GetLocation(r.ContributionCardId) != CardLocation.Hand(r.DonorSeat))
            throw new InvalidOperationException("Wuyou requires its once-only original donor Hand entity and actual Play.");
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (r.DonorSeat == f.OwnerSeat)
        {
            // A self gift changes no zone and must not fabricate gain/loss events.
            ReplaceRuntimeTop(f = f with { Wuyou = r with { ContributionPaid = true } });
            AdvanceEventRulesAndQueueFact(new WuyouContributionPaidEvent(f.Id, r.DonorSeat, f.OwnerSeat,
                r.ContributionCardId, r.ContributionPrintedKind, 0, r.Before, r.Before, true));
        }
        else MoveProgramCardsFromMultipleSources([r.ContributionCardId], CardLocation.Hand(f.OwnerSeat),
            new(WuyouContributionReason), (batch, moves) =>
            {
                var move = moves.Single(); var current = GetActiveProgramFrame(f.Id);
                ReplaceRuntimeTop(current with { Wuyou = current.Wuyou! with
                    { ContributionPaid = true, ContributionBatchId = batch, ContributionSequence = move.Sequence, After = move.Sequence } });
                AdvanceEventRulesAndQueueFact(new WuyouContributionPaidEvent(f.Id, r.DonorSeat, f.OwnerSeat,
                    move.CardId, move.CardKind, batch, r.Before, move.Sequence, false));
            });
        ContinueWuyou(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }

    private void ContinueWuyou(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Wuyou: { } r } f || f.Id != id ||
            _pendingDecision is not null) return;
        if (!ValidWuyouFrame(f, true)) throw new InvalidOperationException("Wuyou lost its original source, phase or once-paid Hand transfers.");
        if (new ProgramSkillHost(this).TryStartPostInstructionWindow(id)) return;
        f = GetActiveProgramFrame(id); r = f.Wuyou!;
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (r.Stage == WuyouStage.ReturnChildren)
        { CompleteWuyou(f); return; }
        if (!_players[f.OwnerSeat].IsAlive || !_players[r.DonorSeat].IsAlive || GetHand(_players[f.OwnerSeat]).Count == 0 || _winner != Winner.None)
        { CompleteWuyou(f); return; }
        if (r.Names.Count == 0)
        {
            var pool = WuyouComposition.Names.ToList(); var names = new List<CardKind>(); var before = _random.State;
            for (var i = 0; i < 5; i++)
            {
                var previous = _random.State; var index = _random.Next(pool.Count); var name = pool[index];
                pool.RemoveAt(index); names.Add(name);
                AdvanceEventRulesAndQueueFact(new WuyouNameSampledEvent(id, i, name, previous, _random.State));
            }
            r = r with { Stage = WuyouStage.ReturnChoice, RandomBefore = before, RandomAfter = _random.State, Names = names };
            ReplaceRuntimeTop(f = f with { Wuyou = r });
        }
        PublishWuyouReturn(f);
    }

    private IReadOnlyList<PromptChoice> WuyouReturnChoices(ProgramSkillFrame f)
    {
        var r = f.Wuyou!;
        Dictionary<string, string> Parameters(string branch) => new()
        { ["program-action"] = "wuyou-return", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch };
        var choices = _players[f.OwnerSeat].IsAlive && _players[r.DonorSeat].IsAlive
            ? r.Names.SelectMany(kind => GetHand(_players[f.OwnerSeat]).OrderBy(c => c.Id).Select(card =>
                new PromptChoice(new($"wuyou.{f.Id}.{kind}.card-{card.Id}"),
                    $"将【{card.DisplayName}】交还并视为【{CardCatalog.Get(kind).DisplayName}】", [card.Id], [r.DonorSeat],
                    new Dictionary<string, string>(Parameters("give")) { ["card-kind"] = kind.ToString() }))).ToList()
            : new List<PromptChoice>();
        choices.Add(new(new($"wuyou.{f.Id}.decline"), "不交还手牌", [], [], Parameters("decline")));
        return Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray());
    }
    private void PublishWuyouReturn(ProgramSkillFrame f)
    {
        var choices = WuyouReturnChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "武佑：可选择一个随机牌名并交还一张手牌。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), [f.Wuyou!.DonorSeat], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("wuyou-return")]
    private void AnswerWuyouReturn(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Wuyou: { Stage: WuyouStage.ReturnChoice } r } f ||
            !ValidWuyouFrame(f, true) || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([selected], WuyouReturnChoices(f).Where(c => c.Id == selected.Id).ToArray()))
            throw new InvalidOperationException("Wuyou's private return must match its exact five names, current Hand and original donor.");
        ClearPendingDecision();
        if (selected.Parameters.GetValueOrDefault("branch") == "decline") { CompleteWuyou(f); return; }
        var card = GetHand(_players[f.OwnerSeat]).Single(c => c.Id == selected.Cards.Single());
        var kind = Enum.Parse<CardKind>(selected.Parameters["card-kind"]); var before = _movementSequence;
        r = r with { Stage = WuyouStage.ReturnChildren, ChosenKind = kind, ReturnCardId = card.Id,
            ReturnPrintedKind = card.Kind, ReturnBefore = before, After = before };
        ReplaceRuntimeTop(f = f with { Wuyou = r, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new WuyouReturnSelectedEvent(f.Id, card.Id, card.Kind, kind, before));
        if (f.OwnerSeat == r.DonorSeat) CommitWuyouReturn(f, 0, before, true);
        else MoveProgramCardsFromMultipleSources([card.Id], CardLocation.Hand(r.DonorSeat), new(WuyouReturnReason),
            (batch, moves) => CommitWuyouReturn(GetActiveProgramFrame(f.Id), batch, moves.Single().Sequence, false));
        ContinueWuyou(f.Id);
    }
    private void CommitWuyouReturn(ProgramSkillFrame f, long batch, long sequence, bool self)
    {
        var r = f.Wuyou!;
        if (r.ReturnPaid || r.ReturnCardId is null || r.ReturnPrintedKind is null || r.ChosenKind is null)
            throw new InvalidOperationException("Wuyou cannot issue an unpaid, different or duplicate returned entity lease.");
        var lease = new WuyouHandLease(f.Id, r.Source, r.GameplayHash, r.DonorSeat, r.ReturnCardId.Value,
            r.ReturnPrintedKind.Value, r.ChosenKind.Value, batch, sequence, self);
        ReplaceRuntimeTop(f with { Wuyou = r with { ReturnPaid = true, ReturnBatchId = batch, ReturnSequence = sequence, After = sequence } });
        AdvanceEventRulesAndQueueFact(new WuyouReturnPaidEvent(f.Id, lease.CardId, batch, r.ReturnBefore, sequence, self));
        AdvanceEventRulesAndQueueFact(new WuyouLeaseIssuedEvent(lease));
    }
    private void CompleteWuyou(ProgramSkillFrame f)
    {
        AdvanceEventRulesAndQueueFact(new WuyouCompletedEvent(f.Id, f.Wuyou!.DonorSeat, f.Wuyou.ReturnPaid));
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 28520)]
    private bool ResumeWuyou(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Wuyou: { ContributionPaid: true } } f || f.Id != id) return false;
        ContinueWuyou(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 28520)]
    private bool ReturnWuyouMovement(ProgramSkillFrame f)
    { if (f.Wuyou is null) return false; ContinueWuyou(f.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 28520)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 28520)]
    private bool ContinuePaidWuyou(ProgramSkillFrame f) => f.Wuyou is { ContributionPaid: true } && ValidWuyouFrame(f, true);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveWuyouContributionAndNamedReturn)]
    private PromptChoice ChooseWuyouReturn(PendingDecision prompt, ProgramSkillFrame f) => prompt.Choices
        .OrderBy(c => c.Cards.Count == 0 ? int.MaxValue : GetKeepValue(GetAdvancedCard(c.Cards[0]), _players[f.OwnerSeat]))
        .ThenByDescending(c => c.Parameters.TryGetValue("card-kind", out var kind) ? CardCatalog.Get(Enum.Parse<CardKind>(kind)).AiPlayValue : 0)
        .ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
}
