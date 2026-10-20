using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string SimultaneousDiscardReason = "skill-program.simultaneous-pindian.discard", SimultaneousDrawReason = "skill-program.simultaneous-pindian.draw";
    private CardConversionSource SimultaneousSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private SkillProgramStepOutcome StartSimultaneousPindian(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.TriggerId is not null || f.InstructionIndex != 1 || f.SimultaneousPindian is not null || f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play ||
            f.SelectedCardIds.Count > 1 || f.SelectedCardIds.Count == 0 && !CanWuLuKangDiscardForPindian(_players[f.OwnerSeat]) || f.SelectedTargetSeats.Count is < 1 or > 3 || f.SelectedTargetSeats.Distinct().Count() != f.SelectedTargetSeats.Count ||
            f.SelectedTargetSeats.Any(s => !CanBePindianTarget(f.OwnerSeat, s)) || f.SelectedCardIds.Count == 1 && !GetHand(_players[f.OwnerSeat]).Any(c => c.Id == f.SelectedCardIds[0]))
            throw new InvalidOperationException("A simultaneous contest lost its exact own card, other hand participants or phase activation.");
        var id = f.SelectedCardIds.SingleOrDefault(); var card = id == 0 ? null : GetHand(_players[f.OwnerSeat]).Single(c => c.Id == id); var rank = card is null ? 0 : EffectivePindianRank(_players[f.OwnerSeat], card);
        var r = new SimultaneousPindianReceipt { ProgramFrameId = f.Id, Source = SimultaneousSource(f), GameplayHash = f.GameplayHash,
            TurnNumber = _turnNumber, PhaseInstanceId = _cardUseDebitPhaseInstanceId, Opponents = f.SelectedTargetSeats,
            OriginalSourceCardId = id, Cards = [new(f.OwnerSeat, id, rank, rank)], Stage = SimultaneousPindianStage.RandomPolicies,
            RandomSelection = new(Array.AsReadOnly(f.SelectedTargetSeats.SelectMany(s => CollectPindianRandomPolicies(f.OwnerSeat,s)).ToArray()),Array.AsReadOnly(Array.Empty<PindianRandomReceipt>())) };
        ReplaceRuntimeTop(f = f with { SimultaneousPindian = r });
        AdvanceEventRulesAndQueueFact(new SimultaneousPindianStartedEvent(f.Id, r.Source, r.Opponents));
        ContinueSimultaneousRandomPolicies(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private int SimultaneousChooser(ProgramSkillFrame f)
    {
        var r = f.SimultaneousPindian!;
        if (r.Stage == SimultaneousPindianStage.Selecting) return r.Opponents[r.SelectionIndex];
        if (r.Stage == SimultaneousPindianStage.RandomPolicies) return r.RandomSelection!.Candidates[r.RandomSelection.Index].OwnerSeat;
        if (r.Stage == SimultaneousPindianStage.Claiming) return r.Claims!.Claims[r.Claims.Index].OwnerSeat;
        var pair = SimultaneousResult(f); return SimultaneousNonwinners(pair)[r.ConsequenceIndex];
    }
    private static int[] SimultaneousNonwinners(PindianResult p) => p.SourceRank == p.OpponentRank ? [p.SourceSeat, p.OpponentSeat] :
        p.SourceWon ? [p.OpponentSeat] : [p.SourceSeat];
    private PindianResult SimultaneousResult(ProgramSkillFrame f)
    {
        var r = f.SimultaneousPindian!; var source = r.Cards.Single(c => c.Seat == f.OwnerSeat); var opponent = r.Cards.Single(c => c.Seat == r.Opponents[r.ComparisonIndex]);
        return new(source.Seat, opponent.Seat, source.CardId, opponent.CardId, source.Rank, opponent.Rank);
    }
    private IReadOnlyList<PromptChoice> SimultaneousChoices(ProgramSkillFrame f)
    {
        var r = f.SimultaneousPindian!; var seat = SimultaneousChooser(f);
        Dictionary<string,string> P(string branch) => new() { ["program-action"] = "simultaneous-pindian", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture),
            ["branch"] = branch, ["comparison"] = r.ComparisonIndex.ToString(CultureInfo.InvariantCulture), ["consequence"] = r.ConsequenceIndex.ToString(CultureInfo.InvariantCulture), ["selection"] = r.SelectionIndex.ToString(CultureInfo.InvariantCulture) };
        if (r.Stage == SimultaneousPindianStage.Selecting)
        {
            var selection = GetHand(_players[seat]).OrderBy(c => c.Id).Select(c => new PromptChoice(new($"simultaneous.{f.Id}.select.{seat}.{c.Id}"),
                $"以【{c.DisplayName}】（{EffectivePindianRank(_players[seat],c)}）参与拼点",[c.Id],[],P("commit"))).ToList();
            if (CanWuLuKangDiscardForPindian(_players[seat])) selection.Add(new(new($"simultaneous.{f.Id}.select.{seat}.maximum"),"弃置牌后使用牌堆最大点数的牌拼点",[],[],P("maximum")));
            if (CanOfferSimultaneousTop(f,seat)) selection.Add(new(new($"simultaneous.{f.Id}.select.{seat}.top"),"使用牌堆顶的牌拼点",[],[],P("top")));
            return Array.AsReadOnly(selection.ToArray());
        }
        if (r.Stage == SimultaneousPindianStage.RandomPolicies)
            return Array.AsReadOnly(new[] { new PromptChoice(new($"simultaneous.{f.Id}.random.{r.RandomSelection!.Index}"),"令对方使用随机手牌拼点",[],[],P("random")),
                new PromptChoice(new($"simultaneous.{f.Id}.random-decline.{r.RandomSelection.Index}"),"不发动",[],[],P("random-decline")) });
        if (r.Stage == SimultaneousPindianStage.Claiming)
        {
            var claim = r.Claims!.Claims[r.Claims.Index]; var ids = AvailableSimultaneousClaimCards(claim);
            return Array.AsReadOnly(new[] {
                new PromptChoice(new($"simultaneous.{f.Id}.{r.ComparisonIndex}.claim.{r.Claims.Index}"),$"获得拼点牌（{ids.Length}张）",ids,[],P("claim")),
                new PromptChoice(new($"simultaneous.{f.Id}.{r.ComparisonIndex}.claim-decline.{r.Claims.Index}"),"不获得拼点牌",[],[],P("claim-decline")) });
        }
        var choices = BuildOwnedCardPaymentChoices(f.Id, seat, seat, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard)
            .Select(c => {
                var parameters = c.Parameters.ToDictionary(p => p.Key,p => p.Value,StringComparer.Ordinal);
                foreach (var (key,value) in P("discard")) parameters[key] = value;
                return new PromptChoice(new($"simultaneous.{f.Id}.{r.ComparisonIndex}.{r.ConsequenceIndex}.discard.{c.Parameters["source-zone"]}.{c.Parameters["slot-index"]}"),c.Label,c.Cards,[],parameters);
            }).ToList();
        choices.Add(new(new($"simultaneous.{f.Id}.{r.ComparisonIndex}.{r.ConsequenceIndex}.draw"), "令拼点发起者摸一张牌", [], [], P("draw")));
        return Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray());
    }
    private void PublishSimultaneousChoice(ProgramSkillFrame f)
    {
        var choices = SimultaneousChoices(f); var seat = SimultaneousChooser(f); var skillId = f.SimultaneousPindian!.Stage == SimultaneousPindianStage.Claiming ? f.SimultaneousPindian.Claims!.Claims[f.SimultaneousPindian.Claims.Index].Source.SkillId :
            f.SimultaneousPindian.Stage == SimultaneousPindianStage.RandomPolicies ? f.SimultaneousPindian.RandomSelection!.Candidates[f.SimultaneousPindian.RandomSelection.Index].Source.SkillId : f.SkillId;
        var skill = _contentRegistry.GetSkill(skillId);
        if (choices.Count == 0) throw new InvalidOperationException("A committed contest participant lost its hand before the common reveal.");
        _pendingDecision = new(DecisionKind.ProgramTrigger, seat, f.SimultaneousPindian!.Stage == SimultaneousPindianStage.Selecting ?
            "请选择一张手牌参与同时拼点；所有角色选定后共同亮出。" : f.SimultaneousPindian.Stage == SimultaneousPindianStage.RandomPolicies ? "请选择是否令对方使用随机手牌拼点；共同亮牌前不公开身份。" : f.SimultaneousPindian.Stage == SimultaneousPindianStage.Claiming ? "请选择是否获得本次比较的拼点牌。" : "拼点未赢，请选择弃置一张牌或令发起者摸一张牌。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = seat, Choices = choices, SkillPrompt = new(skillId,skill.Name,skill.Name,skill.Description) };
        _status = _players[seat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("simultaneous-pindian")]
    private void ResolveSimultaneousPindianChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("A simultaneous answer lost its owning program.");
        AssertSimultaneousPindian(f); var r = f.SimultaneousPindian!; var seat = SimultaneousChooser(f);
        if (_pendingDecision?.PlayerSeat != seat || !SimultaneousChoices(f).Any(c => AssistedChoicesEqual([choice], [c])))
            throw new InvalidOperationException("A simultaneous answer is no longer its exact private published choice.");
        ClearPendingDecision();
        if (r.Stage == SimultaneousPindianStage.RandomPolicies) { ResolveSimultaneousRandomPolicy(f,choice); return; }
        if (r.Stage == SimultaneousPindianStage.Claiming) { ResolveSimultaneousClaim(f,choice); return; }
        if (r.Stage == SimultaneousPindianStage.Selecting)
        {
            CommitSimultaneousSelection(f,seat,choice); return;
        }
        var branch = choice.Parameters["branch"]; var beforePayment = _movementSequence; int? paidCard = null; CardLocation? from = null;
        if (branch == "discard")
        {
            if (!Enum.TryParse<CardZoneKind>(choice.Parameters.GetValueOrDefault("source-zone"),out var zone) || zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
                !int.TryParse(choice.Parameters.GetValueOrDefault("slot-index"),NumberStyles.Integer,CultureInfo.InvariantCulture,out var slot) ||
                choice.Parameters.GetValueOrDefault("card-owner-seat") != seat.ToString(CultureInfo.InvariantCulture))
                throw new InvalidOperationException("A simultaneous discard lost its actual payer, source zone or published slot.");
            from = new(zone,seat); var cards = _cardZones.CardsAt(from.Value);
            if (slot < 0 || slot >= cards.Count || choice.Cards.Count > 1 || choice.Cards.Count == 1 && choice.Cards[0] != cards[slot].Id ||
                IsForeignEquipmentDiscardPrevented(seat,cards[slot],from.Value,OwnedCardMoveIntent.Discard))
                throw new InvalidOperationException("A simultaneous discard cannot replace or forbid its exact current entity.");
            paidCard = cards[slot].Id;
        }
        ReplaceRuntimeTop(f = f with { SimultaneousPindian = r = r with { Stage = SimultaneousPindianStage.MovementChildren, PaymentSeat = seat,
            PaidCardId = paidCard, PaidFrom = from, PaymentKind = branch, MovementBefore = beforePayment, MovementAfter = beforePayment }, PendingMovementContinuation = new(seat,0,null) });
        if (paidCard is { } discard) MoveLogicalDiscard(f.OwnerSeat, seat, GetAdvancedCard(discard), from!.Value, new(SimultaneousDiscardReason));
        else if (_players[f.OwnerSeat].IsAlive) DrawCards(_players[f.OwnerSeat],1,true,new(SimultaneousDrawReason));
        f = GetActiveProgramFrame(f.Id); r = f.SimultaneousPindian! with { MovementAfter = _movementSequence };
        ReplaceRuntimeTop(f = f with { SimultaneousPindian = r });
        AdvanceEventRulesAndQueueFact(new SimultaneousPindianConsequencePaidEvent(f.Id,r.ComparisonIndex,r.ConsequenceIndex,seat,branch,paidCard,beforePayment,r.MovementAfter));
        if (!DrainSimultaneousChildren(f)) AdvanceRuntimeProgram(f.Id);
    }
    private bool DrainSimultaneousChildren(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramContinuation(ProgramContinuationKind.Resume,21500)]
    private bool ResumeSimultaneousPindian(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { SimultaneousPindian: { } r } f || f.Id != id) return false;
        AssertSimultaneousPindian(f);
        if (r.Stage == SimultaneousPindianStage.RandomPolicies) { ContinueSimultaneousRandomPolicies(f); return true; }
        if (r.Stage == SimultaneousPindianStage.Selecting) { ContinueSimultaneousSelection(f); return true; }
        if (r.Stage == SimultaneousPindianStage.TopRefillChildren)
        {
            if (DrainSimultaneousChildren(f)) return true;
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, SimultaneousPindian = r with { Stage = SimultaneousPindianStage.Selecting, PaymentKind = null } });
            ContinueSimultaneousSelection(f); return true;
        }
        if (r.Stage is SimultaneousPindianStage.Consequence or SimultaneousPindianStage.Claiming)
        { if (_pendingDecision is null) PublishSimultaneousChoice(f); return true; }
        if (r.Stage == SimultaneousPindianStage.Revealing) return true;
        if (r.Stage == SimultaneousPindianStage.RevealChildren)
        {
            if (DrainSimultaneousChildren(f)) return true;
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, SimultaneousPindian = r = r with { Stage = SimultaneousPindianStage.Revealing, PaymentKind = null } });
            BeginPindianRevealWindow(f.Id,true,r.Cards); return true;
        }
        if (r.Stage == SimultaneousPindianStage.Cleanup)
        {
            if (DrainSimultaneousChildren(f)) return true;
            AdvanceEventRulesAndQueueFact(new SimultaneousPindianCompletedEvent(f.Id,SimultaneousSource(f),r.ComparisonIndex));
            ReplaceRuntimeTop(f = f with { SimultaneousPindian = null, PendingMovementContinuation = null }); FinishProgramSkill(f,true); return true;
        }
        if (r.Stage == SimultaneousPindianStage.DeathCleanup)
        {
            if (DrainSimultaneousChildren(f)) return true;
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, SimultaneousPindian = r = r with { Stage = SimultaneousPindianStage.DeathChildren, PaymentKind = null } });
            if (_players[f.OwnerSeat].IsAlive)
            {
                AdvanceEventRulesAndQueueFact(new ProgramSkillCauseDeathDeclaredEvent(f.Id,f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.OwnerSeat));
                BeginPlayerDeath(f.Id,_players[f.OwnerSeat],null,null,null,causingProgramSkillFrameId:f.Id); return true;
            }
        }
        if (r.Stage == SimultaneousPindianStage.MovementChildren)
        {
            if (DrainSimultaneousChildren(f)) return true;
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, SimultaneousPindian = r = r with { Stage = SimultaneousPindianStage.Comparing,
                ConsequenceIndex = r.ConsequenceIndex + 1, PaymentSeat = null, PaidCardId = null, PaidFrom = null, PaymentKind = null } });
        }
        if (r.Stage == SimultaneousPindianStage.ClaimMovementChildren)
        {
            if (DrainSimultaneousChildren(f)) return true;
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, SimultaneousPindian = r = r with { Stage = SimultaneousPindianStage.Comparing,
                Claims = r.Claims! with { Index = r.Claims.Index + 1 }, PaymentSeat = null, PaymentKind = null } });
        }
        if (r.Stage == SimultaneousPindianStage.DeathChildren)
            ReplaceRuntimeTop(f = f with { SimultaneousPindian = r = r with { Stage = SimultaneousPindianStage.Comparing } });
        if (_winner != Winner.None) { FinishSimultaneousPindian(f); return true; }
        while (r.ComparisonIndex < r.Opponents.Count)
        {
            var pair = SimultaneousResult(f);
            if (!r.ComparisonIssued)
            {
                r = r with { ComparisonIssued = true };
                ReplaceRuntimeTop(f = f with { SimultaneousPindian = r });
                AdvanceEventRulesAndQueueFact(new PindianResultDeterminedEvent(f.Id,f.SkillId,pair));
            }
            if (TryBeginDouyuSimultaneousWinDraws(f, pair)) return true;
            if (TryBeginWuLuKangPostComparison(f, pair, r.ComparisonIndex)) return true;
            f = GetActiveProgramFrame(f.Id); r = f.SimultaneousPindian!;
            if (r.Claims is null)
            {
                var claims = FreezeSimultaneousClaims(f,pair); ReplaceRuntimeTop(f = f with { SimultaneousPindian = r = r with { Claims = claims } });
                AdvanceEventRulesAndQueueFact(new SimultaneousPindianClaimsIssuedEvent(f.Id,r.ComparisonIndex,claims));
            }
            if (OfferSimultaneousClaim(f)) return true;
            f = GetActiveProgramFrame(f.Id); r = f.SimultaneousPindian!;
            var losers = SimultaneousNonwinners(pair);
            while (r.ConsequenceIndex < losers.Length)
            {
                var loser = losers[r.ConsequenceIndex];
                if (!_players[loser].IsAlive) { ReplaceRuntimeTop(f = f with { SimultaneousPindian = r = r with { ConsequenceIndex = r.ConsequenceIndex + 1 } }); continue; }
                if (loser == f.OwnerSeat && !r.SelfMarkerIssued)
                {
                    r = r with { SelfMarkerIssued = true };
                    ReplaceRuntimeTop(f = f with { SimultaneousPindian = r });
                    MutateParticipantMarker(f,f.OwnerSeat,PlayerMarkerKind.RaoShe,1);
                    if (_players[f.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.RaoShe) == 7)
                    {
                        var before = _movementSequence;
                        ReplaceRuntimeTop(f = f with { SimultaneousPindian = r with { Stage = SimultaneousPindianStage.DeathCleanup, PaymentKind = "cleanup", MovementBefore = before, MovementAfter = before }, PendingMovementContinuation = new(f.OwnerSeat,0,null) });
                        CleanupSimultaneousPindianCards(f);
                        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { SimultaneousPindian = f.SimultaneousPindian! with { MovementAfter = _movementSequence } });
                        if (!DrainSimultaneousChildren(f)) ResumeSimultaneousPindian(f.Id); return true;
                    }
                }
                ReplaceRuntimeTop(f = f with { SimultaneousPindian = r with { Stage = SimultaneousPindianStage.Consequence } });
                PublishSimultaneousChoice(f); return true;
            }
            ReplaceRuntimeTop(f = f with { SimultaneousPindian = r = r with { ComparisonIndex = r.ComparisonIndex + 1, ConsequenceIndex = 0, ComparisonIssued = false, SelfMarkerIssued = false, Claims = null } });
        }
        FinishSimultaneousPindian(f); return true;
    }
    private void CleanupSimultaneousPindianCards(ProgramSkillFrame f)
    {
        foreach (var c in f.SimultaneousPindian!.Cards.Where(c => _cardZones.GetLocation(c.CardId) == CardLocation.Processing))
            MoveCard(GetAdvancedCard(c.CardId),CardLocation.Processing,CardLocation.DiscardPile,CardMoveReasons.PindianFinish);
    }
    private void FinishSimultaneousPindian(ProgramSkillFrame f)
    {
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { SimultaneousPindian = f.SimultaneousPindian! with { Stage = SimultaneousPindianStage.Cleanup, PaymentKind = "cleanup", MovementBefore = before, MovementAfter = before }, PendingMovementContinuation = new(f.OwnerSeat,0,null) });
        CleanupSimultaneousPindianCards(f);
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { SimultaneousPindian = f.SimultaneousPindian! with { MovementAfter = _movementSequence } });
        if (!DrainSimultaneousChildren(f)) ResumeSimultaneousPindian(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement,21500)]
    private bool ReturnSimultaneousMovement(ProgramSkillFrame f) => f.SimultaneousPindian is not null && ResumeSimultaneousPindian(f.Id);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 190)]
    private void AssertSimultaneousPindian(ProgramSkillFrame f)
    {
        if (f.SimultaneousPindian is not { } r) return;
        if (r.ProgramFrameId != f.Id || r.Source != SimultaneousSource(f) || r.GameplayHash != f.GameplayHash || f.TriggerId is not null || f.InstructionIndex != 1 ||
            r.Opponents.Count is < 1 or > 3 || r.Opponents.Distinct().Count() != r.Opponents.Count || r.Opponents.Contains(f.OwnerSeat) || !r.Opponents.SequenceEqual(f.SelectedTargetSeats) ||
            r.Cards.Count < 1 || r.Cards[0].Seat != f.OwnerSeat || !(f.SelectedCardIds.Count == 0 && r.OriginalSourceCardId == 0 || f.SelectedCardIds.SequenceEqual([r.OriginalSourceCardId])) ||
            r.Cards.Select(c => c.Seat).Distinct().Count() != r.Cards.Count || r.Cards.Where(c => c.CardId > 0).Select(c => c.CardId).Distinct().Count() != r.Cards.Count(c => c.CardId > 0) ||
            !Enum.IsDefined(r.Stage) || r.Cards.Any(c => c.CardId <= 0 ? r.Revealed || c.OriginalRank != 0 || c.Rank != 0 : c.OriginalRank is < 1 or > 13 || c.Rank is < 1 or > 13) ||
            r.TurnNumber != _turnNumber || r.PhaseInstanceId != _cardUseDebitPhaseInstanceId || r.ConsequenceIndex is < 0 or > 2 ||
            r.SelectionIndex < 0 || r.SelectionIndex > r.Opponents.Count || r.ComparisonIndex < 0 || r.ComparisonIndex > r.Opponents.Count ||
            !r.Cards.Skip(1).Select(c => c.Seat).SequenceEqual(r.Opponents.Take(r.SelectionIndex)) ||
            CompleteProgramEventHistory().OfType<SimultaneousPindianStartedEvent>().Count(e => e.ProgramFrameId == f.Id && e.Source == r.Source && e.Opponents.SequenceEqual(r.Opponents)) != 1)
            throw new InvalidOperationException("The simultaneous contest lost its exact source, ordered private commitments or original activation.");
        if (!r.Revealed && !IsWuLuKangPrivateGroup(f) && (r.Stage is not (SimultaneousPindianStage.Selecting or SimultaneousPindianStage.RandomPolicies or SimultaneousPindianStage.TopRefillChildren) || r.Cards.Count != r.SelectionIndex + 1 ||
            r.Cards.Any(c => c.CardId > 0 && _cardZones.GetLocation(c.CardId) != SimultaneousOriginalLocation(r,c) || c.Rank != c.OriginalRank)))
            throw new InvalidOperationException("A simultaneous private commitment moved before the shared reveal.");
        var originalCards = GetDouyuOriginalSimultaneousCards(f);
        if (r.Revealed && !ValidWuLuKangGroupReveal(f) && (r.Stage == SimultaneousPindianStage.Selecting || r.SelectionIndex != r.Opponents.Count || r.Cards.Count != r.Opponents.Count + 1 ||
            r.RevealAfter - r.RevealBefore != r.Cards.Count - r.TopReceipts.Count || originalCards.Where(c => !r.TopReceipts.Any(t => t.CardId == c.CardId)).Any(c => _cardMovements.Count(m => m.Sequence > r.RevealBefore && m.Sequence <= r.RevealAfter &&
                m.CardId == c.CardId && m.From == SimultaneousOriginalLocation(r,c) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.PindianReveal) != 1) ||
            CompleteProgramEventHistory().OfType<PindianCardsRevealedEvent>().Count(e => e.ParentFrameId == f.Id && e.Simultaneous &&
                e.Cards.Select(c => (c.Seat,c.CardId,c.OriginalRank)).SequenceEqual(r.Cards.Select(c => (c.Seat,c.CardId,c.OriginalRank)))) != 1))
            throw new InvalidOperationException("A simultaneous reveal lacks its exact once-paid complete common group.");
        var comparisons = CompleteProgramEventHistory().OfType<PindianResultDeterminedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (comparisons.Length != r.ComparisonIndex + (r.ComparisonIssued ? 1 : 0) || comparisons.Where((e,i) =>
            e.SkillId != f.SkillId || e.Result.SourceSeat != f.OwnerSeat || e.Result.OpponentSeat != r.Opponents[i] ||
            e.Result.SourceCardId != r.Cards[0].CardId || e.Result.OpponentCardId != r.Cards[i + 1].CardId ||
            e.Result.SourceRank != r.Cards[0].Rank || e.Result.OpponentRank != r.Cards[i + 1].Rank).Any())
            throw new InvalidOperationException("A simultaneous contest comparison cursor lost its exact ordered public results.");
        AssertSimultaneousClaims(f);
        AssertSimultaneousSelectionPolicies(f);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 20)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 20)]
    private bool CanContinueSimultaneousPindian(ProgramSkillFrame f) => f.SimultaneousPindian is { Revealed: true } && IsSimultaneousReceiptSource(f);
    private bool IsSimultaneousReceiptSource(ProgramSkillFrame f) => f.SimultaneousPindian is { } r && r.ProgramFrameId == f.Id && r.Source == SimultaneousSource(f) && r.GameplayHash == f.GameplayHash;
    [ProgramAiChoice(SkillProgramEffectOp.StartSimultaneousPindian)]
    private PromptChoice SelectAiSimultaneousChoice(PendingDecision p,ProgramSkillFrame f)
    {
        var seat = SimultaneousChooser(f);
        if (f.SimultaneousPindian!.Stage == SimultaneousPindianStage.Selecting)
            return p.Choices.OrderByDescending(c => c.Parameters["branch"] == "maximum" ? 10 : c.Parameters["branch"] == "top" ? 9 : EffectivePindianRank(_players[seat],GetHand(_players[seat]).Single(card => card.Id == c.Cards.Single()))).ThenBy(c => c.Cards.FirstOrDefault()).First();
        if (f.SimultaneousPindian.Stage == SimultaneousPindianStage.RandomPolicies) return p.Choices.Single(c => c.Parameters["branch"] == "random");
        if (f.SimultaneousPindian.Stage == SimultaneousPindianStage.Claiming) return p.Choices.Single(c => c.Parameters["branch"] == "claim");
        return AreProgramDistributionAllies(_players[seat],_players[f.OwnerSeat]) || !p.Choices.Any(c => c.Parameters["branch"] == "discard") ?
            p.Choices.Single(c => c.Parameters["branch"] == "draw") : p.Choices.First(c => c.Parameters["branch"] == "discard");
    }
    [ProgramCapability]
    private sealed class SimultaneousPindianCapability(GameEngine engine) : ISimultaneousPindianHost
    {
        public SkillProgramStepOutcome StartSimultaneousPindian(ProgramSkillFrame f) => engine.StartSimultaneousPindian(f);
        public void AddRaoshePindianRankAndRestoreGushe(ProgramSkillFrame f,string skillId) => engine.AddRaoshePindianRankAndRestoreGushe(f,skillId);
    }
}
