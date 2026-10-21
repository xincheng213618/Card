using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MouZuifengSkill = "mou-ol:zuifeng";
    private const string MouZuifengBinding = "unique-suit-view-and-wine";
    private const string MouZuifengDiscardReason = "skill-program.mou-zhou-yu.zuifeng.discard";
    private static CardConversionSource MouZuifengSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_zhou_yu_ol_mou_MouZhouYuZuifeng.MouZuifengSource(f);
    private bool HasMouZuifengCapability => _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveMouZhouYuZuifeng);
    [ProgramCapability]
    private sealed class MouZhouYuZuifengCapability(GameEngine engine) : IMouZhouYuZuifengHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f) => engine.BeginMouZuifeng(f); }
    private ActualDiscardRecoveryPhaseKey? MouZuifengPlay(int seat)
    {
        var phase = CurrentActualDiscardRecoveryPhase();
        return _setupComplete && _winner == Winner.None && _phase == TurnPhase.Play && _currentSeat == seat && _players[seat].IsAlive &&
            _players[seat].HasHpValue && phase is { Kind: ActualDiscardRecoveryPhaseKind.Play } && phase.ActorSeat == seat && IsRecordedActualDiscardRecoveryPhase(phase) ? phase : null;
    }
    private int MouZuifengUses(int owner, ActualDiscardRecoveryPhaseKey phase) => ProgramEventHistory<MouZuifengCostPaidEvent>()
        .Count(e => e.Source.OwnerSeat == owner && e.Phase == phase);
    private IReadOnlyList<Card> MouZuifengCosts(int owner)
    {
        var hand = GetHand(_players[owner]);
        var counts = hand.GroupBy(c => EffectiveSuit(_players[owner], c)).ToDictionary(g => g.Key, g => g.Count());
        return Array.AsReadOnly(hand.Where(c => EffectiveSuit(_players[owner], c) is Suit.Spade or Suit.Heart or Suit.Club or Suit.Diamond &&
            counts[EffectiveSuit(_players[owner], c)] == 1 && !IsSelfHandCategoryDiscardForbidden(owner, c, CardLocation.Hand(owner), OwnedCardMoveIntent.Discard)).ToArray());
    }
    private int[] MouZuifengTargets(ProgramSkillFrame f) => _players.Where(p => p.IsAlive && GetHand(p).Count > 0 &&
        !HasPrintedGeneralSkillTargetBan(f.OwnerSeat, f.SkillId, p.Seat, f.SkillInstanceId)).Select(p => p.Seat).ToArray();
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 29020, SkillProgramEffectOp.ResolveMouZhouYuZuifeng)]
    private bool CanActivateMouZuifeng(ProgramActivationLegalityContext gate) => MouZuifengPlay(gate.OwnerSeat) is { } phase &&
        MouZuifengUses(gate.OwnerSeat, phase) < gate.Owner.MaxHp && MouZuifengCosts(gate.OwnerSeat).Count > 0 &&
        _players.Any(p => p.IsAlive && GetHand(p).Count > 0 && !HasPrintedGeneralSkillTargetBan(gate.OwnerSeat, gate.SkillId, p.Seat));
    private SkillProgramStepOutcome BeginMouZuifeng(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MouZuifeng is not null) { ContinueMouZuifeng(f.Id); return SkillProgramStepOutcome.AwaitChild; }
        if (f.SkillId != MouZuifengSkill || f.ActivationId != MouZuifengBinding || f.TriggerId is not null || f.WindowContext is not null ||
            f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || MouZuifengPlay(f.OwnerSeat) is not { } phase ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) || MouZuifengUses(f.OwnerSeat, phase) >= _players[f.OwnerSeat].MaxHp ||
            MouZuifengCosts(f.OwnerSeat).Count == 0 || MouZuifengTargets(f).Length == 0)
            throw new InvalidOperationException("Zuifeng requires its current true Play, dynamic MaxHP allowance and one legally discardable unique-suit Hand entity.");
        var r = new MouZuifengReceipt(MouZuifengSource(f), f.GameplayHash, phase, f.InstructionIndex, _turnNumber, _turnProgression.OwnerSeat)
            { Stage = MouZuifengStage.CostChoice, Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { MouZuifeng = r });
        AdvanceEventRulesAndQueueFact(new MouZuifengStartedEvent(f.Id, r.Source, r.GameplayHash, r.Phase, r.ActualTurn, r.TurnOwner));
        PublishMouZuifeng(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> MouZuifengChoices(ProgramSkillFrame f)
    {
        var r = f.MouZuifeng!;
        PromptChoice C(string branch, string label, int? card = null, int? target = null) => new(new($"mou-zuifeng.{f.Id}.{r.Stage}.{branch}.{card}.{target}"),
            label, card is { } id ? [id] : [], target is { } seat ? [seat] : [], new Dictionary<string,string>
            { ["program-action"] = "mou-zuifeng", ["branch"] = branch, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) });
        var choices = new List<PromptChoice>();
        if (r.Stage is MouZuifengStage.CostChoice or MouZuifengStage.TargetChoice)
        {
            if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
                MouZuifengPlay(f.OwnerSeat) != r.Phase || MouZuifengUses(f.OwnerSeat, r.Phase) >= _players[f.OwnerSeat].MaxHp) return [];
            if (r.Stage == MouZuifengStage.CostChoice)
                choices.AddRange(MouZuifengCosts(f.OwnerSeat).Select(c => C("cost", $"弃置【{c.DisplayName}】", c.Id)));
            else if (MouZuifengCosts(f.OwnerSeat).Any(c => c.Id == r.CostId && c.Kind == r.CostKind && EffectiveSuit(_players[f.OwnerSeat], c) == r.CostSuit))
                choices.AddRange(MouZuifengTargets(f).Select(seat => C("target", $"观看{_players[seat].Name}的手牌", target: seat)));
            choices.Add(C("cancel", "取消醉锋"));
        }
        else if (r.Stage == MouZuifengStage.HandViewChoice && _players[f.OwnerSeat].IsAlive && _players[r.TargetSeat].IsAlive && _winner == Winner.None)
            choices.AddRange(r.Viewed.Where(v => _cardZones.GetLocation(v.CardId) == CardLocation.Hand(r.TargetSeat))
                .Select(v => C("show", $"展示【{GetAdvancedCard(v.CardId).DisplayName}】", v.CardId)));
        else if (r.Stage == MouZuifengStage.AlcoholChoice)
        {
            if (CanUseMouZuifengAlcohol(f)) choices.Add(C("alcohol", $"将展示牌当【酒】对{_players[f.OwnerSeat].Name}使用", r.ShownId));
            choices.Add(C("decline", "不使用酒"));
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private int MouZuifengChooser(ProgramSkillFrame f) => f.MouZuifeng!.Stage == MouZuifengStage.AlcoholChoice ? f.MouZuifeng.TargetSeat : f.OwnerSeat;
    private void PublishMouZuifeng(ProgramSkillFrame f)
    {
        var r = f.MouZuifeng!; var choices = MouZuifengChoices(f);
        if (choices.Count == 0 || !_players[MouZuifengChooser(f)].IsAlive || _winner != Winner.None) { FinishMouZuifeng(f); return; }
        PublishParticipantHandChoice(f, MouZuifengChooser(f), choices,
            r.Stage == MouZuifengStage.HandViewChoice ? "醉锋：观看其完整手牌并公开其中一张。" :
            r.Stage == MouZuifengStage.AlcoholChoice ? "醉锋：你可将刚才展示的原牌当酒对其使用。" : "醉锋：弃置一个花色的唯一手牌，选择一名有手牌的角色。", r.TargetSeat < 0 ? f.OwnerSeat : r.TargetSeat);
    }
    [ProgramViewContributor(ProgramViewContribution.PrivateCards, 90)]
    private IEnumerable<CardSnapshot> MouZuifengPrivatelyViewedCards(int viewerSeat) => _resolutionStack.OfType<ProgramSkillFrame>()
        .Where(f => f.OwnerSeat == viewerSeat && f.MouZuifeng is { Stage: MouZuifengStage.HandViewChoice })
        .SelectMany(f => f.MouZuifeng!.Viewed.Where(v => _cardZones.GetLocation(v.CardId) == CardLocation.Hand(f.MouZuifeng.TargetSeat))
            .Select(v => ToSnapshot(GetAdvancedCard(v.CardId))));
    [ProgramViewContributor(ProgramViewContribution.PublicCardsAfterResolution, 40)]
    private IEnumerable<CardSnapshot> MouZuifengPublicCards() => _resolutionStack.OfType<ProgramSkillFrame>()
        .Where(f => f.MouZuifeng?.ShownId is not null).Select(f => ToSnapshot(GetAdvancedCard(f.MouZuifeng!.ShownId!.Value)));
    [ProgramAnswer("mou-zuifeng")]
    private void AnswerMouZuifeng(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouZuifeng: { } r } f ||
            _pendingDecision is not { IsPrivate: true } prompt || prompt.PlayerSeat != MouZuifengChooser(f) ||
            !AssistedChoicesEqual([choice], MouZuifengChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Zuifeng lost its private actual chooser or exact original published command.");
        AssertMouZuifeng(f); ClearPendingDecision(); var branch = choice.Parameters["branch"];
        if (branch == "cancel") { FinishMouZuifeng(f); return; }
        if (branch == "cost")
        {
            var card = MouZuifengCosts(f.OwnerSeat).Single(c => c.Id == choice.Cards.Single()); var suit = EffectiveSuit(_players[f.OwnerSeat], card);
            ReplaceRuntimeTop(f = f with { MouZuifeng = r with { Stage = MouZuifengStage.TargetChoice, CostId = card.Id, CostKind = card.Kind, CostSuit = suit } });
            AdvanceEventRulesAndQueueFact(new MouZuifengCostChosenEvent(f.Id, card.Id, card.Kind, suit)); PublishMouZuifeng(f); return;
        }
        if (branch == "target") { PayMouZuifengCost(f, choice.Targets.Single()); return; }
        if (branch == "show")
        {
            var card = GetHand(_players[r.TargetSeat]).Single(c => c.Id == choice.Cards.Single());
            if (!GetHand(_players[r.TargetSeat]).Select(c => c.Id).SequenceEqual(r.Viewed.Select(v => v.CardId)))
                throw new InvalidOperationException("Zuifeng must publicly show one of the exact complete privately viewed Hand entities.");
            var suit = EffectiveSuit(_players[r.TargetSeat], card); var same = suit == r.CostSuit;
            ReplaceRuntimeTop(f = f with { MouZuifeng = r with { Stage = MouZuifengStage.DamageChildren, ShownId = card.Id,
                ShownKind = card.Kind, ShownSuit = suit, Before = _movementSequence, After = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new MouZuifengShownEvent(f.Id, f.OwnerSeat, r.TargetSeat, card.Id, card.Kind, suit, same));
            AdvanceEventRulesAndQueueFact(new CardsRevealedEvent(f.Id, Array.AsReadOnly(new[] { ToSnapshot(card) })));
            if(TryBeginPublicHandRevealWindow(f.Id,"zuifeng-selected-hand"))return;
            ContinueZuifengAfterPublicReveal(GetActiveProgramFrame(f.Id));return;
        }
        if (branch is "alcohol" or "decline")
        {
            AdvanceEventRulesAndQueueFact(new MouZuifengAlcoholChosenEvent(f.Id, r.TargetSeat, f.OwnerSeat, r.ShownId!.Value, branch == "alcohol"));
            if (branch == "decline") { FinishMouZuifeng(f); return; }
            DispatchMouZuifengAlcohol(f); return;
        }
        throw new InvalidOperationException("Zuifeng cannot fabricate a branch outside its original private stage.");
    }
    private void PayMouZuifengCost(ProgramSkillFrame f, int target)
    {
        var r = f.MouZuifeng!; var card = MouZuifengCosts(f.OwnerSeat).Single(c => c.Id == r.CostId && c.Kind == r.CostKind && EffectiveSuit(_players[f.OwnerSeat], c) == r.CostSuit);
        if (r.Stage != MouZuifengStage.TargetChoice || r.Paid || !MouZuifengTargets(f).Contains(target) || MouZuifengPlay(f.OwnerSeat) != r.Phase ||
            MouZuifengUses(f.OwnerSeat, r.Phase) >= _players[f.OwnerSeat].MaxHp) throw new InvalidOperationException("Zuifeng cannot repeat a quota or pay a changed unique-suit original Hand card.");
        var max = _players[f.OwnerSeat].MaxHp; var ordinal = MouZuifengUses(f.OwnerSeat, r.Phase) + 1; var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { MouZuifeng = r with { Stage = MouZuifengStage.CostChildren, TargetSeat = target, Paid = true, Before = before, After = before },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramLogicalDiscard(f, f.OwnerSeat, [card.Id], new(MouZuifengDiscardReason), (batch, moves) =>
        {
            var move = moves.Single(); var current = GetActiveProgramFrame(f.Id); var now = current.MouZuifeng!;
            if (move.CardId != card.Id || move.From != CardLocation.Hand(f.OwnerSeat) || move.Sequence <= before || move.Reason.Value != MouZuifengDiscardReason)
                throw new InvalidOperationException("Zuifeng requires one actual logical Hand discard payment.");
            ReplaceRuntimeTop(current with { MouZuifeng = now with { CostBatchId = batch, CostSequence = move.Sequence, After = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new MouZuifengCostPaidEvent(f.Id, r.Source, r.GameplayHash, r.Phase, target, card.Id, card.Kind, r.CostSuit,
                batch, move.From, move.To, move.Sequence, before, _movementSequence, max, ordinal));
        });
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { MouZuifeng = f.MouZuifeng! with { After = _movementSequence } }); ContinueMouZuifeng(f.Id);
    }
    private bool DrainMouZuifeng(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueMouZuifeng(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouZuifeng: { } r } f || f.Id != id || f.AttackAttempt is not null) return;
        AssertMouZuifeng(f);
        if (r.Stage is MouZuifengStage.CostChoice or MouZuifengStage.TargetChoice or MouZuifengStage.HandViewChoice or MouZuifengStage.AlcoholChoice)
        { PublishMouZuifeng(f); return; }
        if (r.Stage == MouZuifengStage.NativeUse || r.Stage == MouZuifengStage.NativeIssuing) return;
        if (r.Stage == MouZuifengStage.DamageChildren && r.FireIssued && !r.FireReturned)
        {
            ReplaceRuntimeTop(f = f with { MouZuifeng = r = r with { FireReturned = true, After = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new MouZuifengFireReturnedEvent(id, r.TargetSeat));
        }
        if (DrainMouZuifeng(f)) return;
        ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { PendingMovementContinuation = null }); r = f.MouZuifeng!;
        if (r.Stage == MouZuifengStage.CostChildren)
        {
            if (!_players[f.OwnerSeat].IsAlive || !_players[r.TargetSeat].IsAlive || _winner != Winner.None || GetHand(_players[r.TargetSeat]).Count == 0)
            { FinishMouZuifeng(f); return; }
            var hand = GetHand(_players[r.TargetSeat]).Select((c, index) => new MouZuifengViewedEntity(index, c.Id, c.Kind)).ToArray();
            ReplaceRuntimeTop(f = f with { MouZuifeng = r with { Stage = MouZuifengStage.HandViewChoice, Viewed = hand } });
            AdvanceEventRulesAndQueueFact(new MouZuifengHandViewedEvent(id, f.OwnerSeat, r.TargetSeat, hand.Length, _movementSequence));
            foreach (var entity in hand) AdvanceEventRulesAndQueueFact(new MouZuifengViewedEntityEvent(id, entity.Index, entity.CardId, entity.PrintedKind));
            PublishMouZuifeng(f); return;
        }
        if (r.Stage == MouZuifengStage.DamageChildren)
        {
            if (!_players[r.TargetSeat].IsAlive || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None || r.ShownId is not { } shown || _cardZones.GetLocation(shown) != CardLocation.Hand(r.TargetSeat))
            { FinishMouZuifeng(f); return; }
            ReplaceRuntimeTop(f = f with { MouZuifeng = r with { Stage = MouZuifengStage.AlcoholChoice } }); PublishMouZuifeng(f); return;
        }
        FinishMouZuifeng(f);
    }
    private void FinishMouZuifeng(ProgramSkillFrame f)
    {
        var r = f.MouZuifeng!; AdvanceEventRulesAndQueueFact(new MouZuifengCompletedEvent(f.Id, r.Paid, r.ShownId, r.UseId));
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 29020)]
    private bool ResumeMouZuifeng(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouZuifeng: not null } f || f.Id != id) return false; ContinueMouZuifeng(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 29020)]
    private bool ReturnMouZuifengMovement(ProgramSkillFrame f)
    { if (f.MouZuifeng is null) return false; ContinueMouZuifeng(f.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 29020)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 29020)]
    private bool ContinuePaidMouZuifeng(ProgramSkillFrame f) => IsPaidMouZuifeng(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouZhouYuZuifeng)]
    private PromptChoice AiMouZuifeng(PendingDecision p, ProgramSkillFrame f)
    {
        if (f.MouZuifeng!.Stage == MouZuifengStage.AlcoholChoice)
        {
            var alcohol = p.Choices.FirstOrDefault(c => c.Parameters["branch"] == "alcohol");
            var decline = p.Choices.Single(c => c.Parameters["branch"] == "decline");
            var snapshot = CreateSnapshot(p.PlayerSeat);
            var beneficiary = snapshot.Players.Single(player => player.Seat == f.OwnerSeat);
            // Use the same viewer-safe support estimate as other native aid
            // choices; raw hidden identities must not decide this private offer.
            // An existing ordinary Alcohol effect does not gain another bonus.
            if (alcohol is null || beneficiary.HasAlcoholEffect || !_aiBrains.TryGetValue(p.PlayerSeat, out var brain)) return decline;
            var benefit = brain.ScoreProgramTarget(snapshot, beneficiary.Seat,
                new SkillProgramAiHint(0, 0, 0, 0, 0, 0, false, false) { TargetValueAdjustment = 1 });
            return benefit > 0 ? alcohol : decline;
        }
        return p.Choices.First(c => c.Parameters["branch"] != "cancel");
    }
    [PublicHandRevealReturn("zuifeng-selected-hand")]
    private void ContinueZuifengAfterPublicReveal(ProgramSkillFrame f)
    {
        var r=f.MouZuifeng??throw new InvalidOperationException("Zuifeng lost its paid cost and shown entity.");
        var same=r.ShownSuit==r.CostSuit;var card=GetAdvancedCard(r.ShownId!.Value);
            if (same && _players[r.TargetSeat].IsAlive && _winner == Winner.None)
            {
                ReplaceRuntimeTop(f = f with { MouZuifeng = f.MouZuifeng! with { FireIssued = true } });
                AdvanceEventRulesAndQueueFact(new MouZuifengFireIssuedEvent(f.Id, f.OwnerSeat, r.TargetSeat, card.Id));
                BeginProgramSkillDamage(f, r.TargetSeat, 1, nature: DamageNature.Fire); return;
            }
            ContinueMouZuifeng(f.Id); return;
    }

}
