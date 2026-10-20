namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string YouqueDiscardReason = "skill-program.guan-hai.youque.discard", YouqueDrawReason = "skill-program.guan-hai.youque.draw";
    private static CardConversionSource GuanHaiSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_guan_hai_ol_GuanHai.GuanHaiSource(f);
    private IEnumerable<PublicPersistentPileSource> XieweiPiles(int owner) => PublicPileSources(owner).Where(s => s.SkillId == "ol:xiewei");
    [ProgramCapability] private sealed class GuanHaiCapability(GameEngine engine) : IGuanHaiHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginGuanHai(f, op); }
    private SkillProgramStepOutcome BeginGuanHai(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.GuanHai is not null || f.InstructionIndex != 1 || GetPausedPrivateOfferEffect(f)?.Op != op) throw new InvalidOperationException("Guan Hai requires one original owning instruction.");
        var r = new GuanHaiReceipt(GuanHaiSource(f), f.GameplayHash, op, _turnNumber, _turnProgression.OwnerSeat, GuanHaiStage.ChooseBait);
        if (op == SkillProgramEffectOp.ResolveXieweiTwoHandBaitBasicUse)
        {
            if (f.TriggerId is null || f.WindowContext is not { } c || !IsIssuedXieweiCandidate(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, c.OccurrenceIndex), c) || _resolutionStack.Single(w => w.Id == c.ParentFrameId) is not ProgramCardTriggerWindowFrame w || IssuedXieweiPayment(w.Action) is not { } p) throw new InvalidOperationException("Xiewei cannot commit an unpaid or different native basic use.");
            r = r with { Paid = true, Stage = GuanHaiStage.XieweiChildren, ParentId = c.ParentFrameId, PaymentId = p.Id, NativeUseId = p.UseId, ActionId = p.ActionId, Before = p.Before, After = p.After, Materials = p.Materials, Pile = p.Pile };
        }
        else if (op == SkillProgramEffectOp.ResolveYouqueMinimumBaitPindian)
        {
            if (f.TriggerId is not null || f.ActivationId != "minimum-bait-pindian" || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats is not [var peer] || !CanBePindianTarget(f.OwnerSeat, peer) || !HasWuLuKangPindianInput(_players[f.OwnerSeat]) || !YouquePhaseActive(f.OwnerSeat) || !YouqueBaits(f.OwnerSeat).Any() || !_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) throw new InvalidOperationException("Youque requires a real Play, original hand-bearing participants and actual bait.");
            r = r with { OpponentSeat = peer, Phase = CurrentActualDiscardRecoveryPhase() };
        }
        else
        {
            if (f.TriggerId is null || f.WindowContext is not { } c || !IsIssuedGuanHaiYouqueCandidate(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, c.OccurrenceIndex), c) || YouqueRewardUse(c) is not { YouqueSlash: { } origin } use) throw new InvalidOperationException("Youque rewards require this issued Slash's actual positive damage.");
            r = r with { Paid = true, Stage = GuanHaiStage.RewardDraw, ParentId = origin.ProgramFrameId, NativeUseId = use.Id, ActionId = use.Action!.ActionId, DamageFrameId = c.DamageFrameId!.Value, Phase = origin.Phase, Before = _movementSequence, After = _movementSequence };
        }
        ReplaceRuntimeTop(f = f with { GuanHai = r });
        AdvanceEventRulesAndQueueFact(new GuanHaiStartedEvent(f.Id, r.Source, f.GameplayHash, op, r.ActualTurn, r.TurnOwner, r.ParentId));
        if (op == SkillProgramEffectOp.ResolveXieweiTwoHandBaitBasicUse) { AdvanceEventRulesAndQueueFact(new XieweiCommittedEvent(f.Id, r.PaymentId, r.NativeUseId, r.ActionId)); ReplaceRuntimeTop(f = f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); }
        if (op == SkillProgramEffectOp.ResolveYouqueIssuedSlashDamageBenefit) AdvanceEventRulesAndQueueFact(new YouqueBenefitIssuedEvent(f.Id, r.ParentId, r.NativeUseId, r.DamageFrameId, f.OwnerSeat));
        ContinueGuanHai(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    [ProgramAnswer("guan-hai")]
    private void AnswerGuanHai(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { GuanHai: { Stage: GuanHaiStage.ChooseBait } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat || !YouqueBaitChoices(f).Any(c => AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Youque requires the actual current public bait choice.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (choice.Parameters["branch"] == "cancel") { FinishGuanHai(f, false); return; }
        var selected = YouqueBaits(f.OwnerSeat).Single(b => b.Card.Id == choice.Cards.Single()); var id = _resolutionSequence + 1;
        r = r with { Paid = true, Stage = GuanHaiStage.Pindian, Pile = selected.Pile, BaitCardId = selected.Card.Id, BaitRank = selected.Card.Rank, PindianId = id, Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { GuanHai = r, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new YouquePindianIssuedEvent(f.Id, id, r.Source, r.GameplayHash, selected.Pile, r.BaitCardId, r.BaitRank, r.OpponentSeat, r.Phase!, r.Before));
        var skill = _contentRegistry.GetSkill(f.SkillId);
        BeginSharedPindian(f.Id, new(f.SkillId, skill.Name, skill.Name, skill.Description), f.OwnerSeat, r.OpponentSeat, r.BaitCardId, YouqueResultBind(f), SkillProgramCardSetVisibility.Public, new(f.Id, r.Source, selected.Pile, r.BaitCardId, r.BaitRank, r.Before));
    }
    private bool IsGuanHaiPindianSource(PindianFrame p, bool revealed)
    {
        if (p.OwnedPileSource is not { } s || _resolutionStack.SingleOrDefault(f => f.Id == s.OriginProgramFrameId) is not ProgramSkillFrame { GuanHai: { Paid: true, Stage: GuanHaiStage.Pindian } r } parent || r.Op != SkillProgramEffectOp.ResolveYouqueMinimumBaitPindian || p.Id != r.PindianId || p.ParentFrameId != parent.Id || p.SourceSeat != parent.OwnerSeat || p.OpponentSeat != r.OpponentSeat || p.SourceCardId != r.BaitCardId || s.Origin != r.Source || s.Pile != r.Pile || s.CardId != r.BaitCardId || s.PrintedRank != r.BaitRank || s.SequenceBefore != r.Before || p.ProgramResultBind != YouqueResultBind(parent) || ProgramEventHistory<YouquePindianIssuedEvent>().Count(e => e == new YouquePindianIssuedEvent(parent.Id, p.Id, r.Source, r.GameplayHash, s.Pile, s.CardId, s.PrintedRank, r.OpponentSeat, r.Phase!, r.Before)) != 1) return false;
        if (!revealed) return _cardZones.GetLocation(s.CardId) == s.Pile.Location && GetAdvancedCard(s.CardId).Rank == s.PrintedRank && !YouqueBaits(parent.OwnerSeat).Any(b => b.Card.Rank < s.PrintedRank);
        return HasPaidOwnedPilePindianSource(p) && _cardMovements.Count(m => m.Sequence > s.SequenceBefore && m.CardId == s.CardId && m.From == s.Pile.Location && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.PindianReveal) == 1;
    }
    private bool DrainGuanHai(ProgramSkillFrame f) => TryBeginAdvancedSkillsChanged(f.Id) || TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id);
    private void ContinueGuanHai(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { GuanHai: { } r } f || f.Id != id) return;
        AssertGuanHai(f);
        if (r.Stage == GuanHaiStage.ChooseBait) { if (_pendingDecision is null) PublishYouque(f); return; }
        if (r.Stage is GuanHaiStage.NativeSlash or GuanHaiStage.SlashIssuing || f.NativeDraw is not null) return;
        if (DrainGuanHai(f)) return;
        f = GetActiveProgramFrame(id); r = f.GuanHai!; if (f.PendingMovementContinuation is not null) ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (r.Stage == GuanHaiStage.Pindian)
        {
            var facts = ProgramEventHistory<PindianResultDeterminedEvent>().Where(e => e.FrameId == r.PindianId && e.SkillId == f.SkillId).ToArray();
            if (facts is not [var fact] || (fact.Result.SourceCardId != r.BaitCardId && !IsDouyuHistoricalPairAssignment(r.PindianId, fact.Result, r.BaitCardId) && !IsWuLuKangHistoricalPairAssignment(r.PindianId, fact.Result, r.BaitCardId)) || fact.Result.SourceSeat != f.OwnerSeat || fact.Result.OpponentSeat != r.OpponentSeat || f.PindianResultBindings.SingleOrDefault(b => b.Name == YouqueResultBind(f)) is not { } binding || binding.SourceRank != fact.Result.SourceRank || binding.OpponentRank != fact.Result.OpponentRank || binding.SourceWon != fact.Result.SourceWon) throw new InvalidOperationException("Youque requires its exact completed native Pindian result.");
            var result = fact.Result; AdvanceEventRulesAndQueueFact(new YouquePindianReturnedEvent(id, r.PindianId, result.SourceCardId, result.OpponentCardId, result.SourceRank, result.OpponentRank));
            if (result.SourceRank == result.OpponentRank) { FinishGuanHai(f, true); return; }
            var winner = result.SourceWon ? result.SourceSeat : result.OpponentSeat; var loser = result.SourceWon ? result.OpponentSeat : result.SourceSeat; var rank = loser == result.SourceSeat ? result.SourceRank : result.OpponentRank;
            var cards = _players[loser].IsAlive ? GetHand(_players[loser]).Where(c => c.Rank > rank).ToArray() : [];
            r = r with { Result = result, Stage = GuanHaiStage.DiscardChildren, LoserSeat = loser, WinnerSeat = winner, Before = _movementSequence, After = _movementSequence, Materials = cards.Select(c => new GuanHaiMaterial(c.Id, c.Kind, CardLocation.Hand(loser))).ToArray() };
            ReplaceRuntimeTop(f = f with { GuanHai = r, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            for (var i = 0; i < r.Materials.Count; i++) { var m = r.Materials[i]; AdvanceEventRulesAndQueueFact(new YouqueDiscardMaterialEvent(id, i, loser, m.CardId, m.Kind, m.From)); }
            if (cards.Length > 0) MoveProgramLogicalDiscard(f, loser, cards.Select(c => c.Id).ToArray(), new(YouqueDiscardReason), (batch, records) => { foreach (var m in records) { var index = Array.FindIndex(cards, c => c.Id == m.CardId); AdvanceEventRulesAndQueueFact(new YouqueDiscardPaidEvent(id, index, batch, m.CardId, m.Sequence)); } });
            f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f with { GuanHai = f.GuanHai! with { After = _movementSequence } }); ContinueGuanHai(id); return;
        }
        if (r.Stage == GuanHaiStage.DiscardChildren) { IssueYouqueSlash(f); return; }
        if (r.Stage == GuanHaiStage.RewardDraw)
        {
            if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishGuanHai(f, true); return; }
            ReplaceRuntimeTop(f = f with { GuanHai = r with { Before = _movementSequence, After = _movementSequence } }); BeginOperationProgramDraw(f, f.OwnerSeat, 2, new(YouqueDrawReason)); return;
        }
        if (r.Stage == GuanHaiStage.RewardChildren)
        {
            if (r.Phase is { } phase && !YouqueBaits(f.OwnerSeat).Any(b => b.Card.Suit == Suit.Spade) && !ProgramEventHistory<YouquePhaseDisabledEvent>().Any(e => e.OwnerSeat == f.OwnerSeat && e.Phase == phase)) AdvanceEventRulesAndQueueFact(new YouquePhaseDisabledEvent(f.Id, f.OwnerSeat, phase, r.Source));
            FinishGuanHai(f, true); return;
        }
        if (r.Stage is GuanHaiStage.XieweiChildren or GuanHaiStage.NativeChildren) { FinishGuanHai(f, true); return; }
        throw new InvalidOperationException("Guan Hai resumed outside an issued native stage.");
    }
    private void FinishGuanHai(ProgramSkillFrame f, bool applied) { AdvanceEventRulesAndQueueFact(new GuanHaiCompletedEvent(f.Id, applied)); ReplaceRuntimeTop(f = f with { GuanHai = null, PendingMovementContinuation = null }); FinishProgramSkill(f, applied); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 27578)] private bool ResumeGuanHai(long id) { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { GuanHai: not null } f || f.Id != id) return false; ContinueGuanHai(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 27578)] private bool ReturnGuanHaiMovement(ProgramSkillFrame f) { if (f.GuanHai is null) return false; ContinueGuanHai(f.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 27578)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 27578)]
    private bool ContinuePaidGuanHai(ProgramSkillFrame f) => CanContinueIssuedGuanHai(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveYouqueMinimumBaitPindian)] private PromptChoice ChooseGuanHai(PendingDecision p, ProgramSkillFrame f) => p.Choices.First(c => c.Parameters.GetValueOrDefault("branch") != "cancel");
}
