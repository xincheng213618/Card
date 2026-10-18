using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string WuXiongmuSkill = "wumiao:xiongmu";
    private const string XiongmuDrawReason = "skill-program.wu-lu-xun.xiongmu.draw";
    private const string XiongmuPutReason = "skill-program.wu-lu-xun.xiongmu.put";
    private const string XiongmuObtainReason = "skill-program.wu-lu-xun.xiongmu.obtain";
    private bool HasWuXiongmuCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveWuLuXunXiongmu);
    private static CardConversionSource WuXiongmuSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramCapability]
    private sealed class WuLuXunXiongmuCapability(GameEngine engine) : IWuLuXunXiongmuHost
    {
        public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) =>
            op == SkillProgramEffectOp.ResolveWuLuXunXiongmu ? engine.BeginWuXiongmu(f) : engine.ResolveWuXiongmuFirstDamage(f);
    }
    private bool WuXiongmuNativeCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        if (context.OwnerSeat != candidate.OwnerSeat || context.OccurrenceIndex != candidate.OccurrenceIndex ||
            candidate.SkillId != WuXiongmuSkill || _resolutionStack.SingleOrDefault(f => f.Id == context.ParentFrameId) is not { } parent) return false;
        if (parent is ProgramLifecycleTriggerWindowFrame w)
            return candidate.BindingId == "round-start" && context.Window == SkillProgramTriggerWindow.RoundStarting &&
                w.Window == context.Window && w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count &&
                w.Candidates[w.CandidateIndex] == candidate;
        return parent is BeforeDamageProgramWindowFrame before && candidate.BindingId == "first-damage" &&
            context.Window == SkillProgramTriggerWindow.BeforeDamageApplied && before.TargetSeat == candidate.OwnerSeat &&
            context.TargetSeat == before.TargetSeat && before.CandidateIndex >= 0 && before.CandidateIndex < before.Candidates.Count &&
            before.Candidates[before.CandidateIndex].Candidate == candidate;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 29000)]
    private bool CanOfferWuXiongmu(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (!trigger.Effects.Any(e => XiongmuComposition.IsOperation(e.Op))) return true;
        if (!_players[candidate.OwnerSeat].IsAlive || _winner != Winner.None || !WuXiongmuNativeCandidate(candidate, context) ||
            GetSkillBindingShard(_players[candidate.OwnerSeat]).ProgramInstances.Where(i => i.SkillId == WuXiongmuSkill)
                .OrderBy(i => i.SkillInstanceId, StringComparer.Ordinal).FirstOrDefault()?.SkillInstanceId != candidate.SkillInstanceId) return false;
        if (candidate.BindingId == "first-damage") return WuXiongmuFirstDamageEligible(candidate.OwnerSeat, context.ParentFrameId);
        return _roundTurnSeats.Count == 1 && ProgramEventHistory<RoundStartedEvent>().Any(e => e.RoundNumber == _roundNumber && e.ActorSeat == _currentSeat) &&
            !ProgramEventHistory<XiongmuStartedEvent>().Any(e => e.Source.OwnerSeat == candidate.OwnerSeat && e.RoundNumber == _roundNumber);
    }
    private SkillProgramStepOutcome BeginWuXiongmu(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.Xiongmu is not null) { ContinueWuXiongmu(f.Id); return SkillProgramStepOutcome.AwaitChild; }
        if (f.SkillId != WuXiongmuSkill || f.TriggerId != "round-start" || f.ActivationId != f.TriggerId || f.InstructionIndex != 1 ||
            f.WindowContext is not { } c || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            !CanOfferWuXiongmu(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, c.OccurrenceIndex), GetProgramTrigger(f), c))
            throw new InvalidOperationException("Xiongmu requires its original optional round-start candidate and no prior invocation this round.");
        var owner = _players[f.OwnerSeat]; var hand = GetHand(owner).Count;
        var count = owner.HasHpValue ? Math.Max(0, owner.MaxHp - hand) : 0;
        var r = new XiongmuReceipt(WuXiongmuSource(f), f.GameplayHash, c.ParentFrameId, _roundNumber,
            _turnNumber, _turnProgression.OwnerSeat, f.InstructionIndex, count, hand, owner.HasHpValue ? owner.MaxHp : 0, _movementSequence)
        { Stage = XiongmuStage.Drawing, Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { Xiongmu = r });
        AdvanceEventRulesAndQueueFact(new XiongmuStartedEvent(f.Id, r.Source, r.GameplayHash, r.WindowId, r.RoundNumber,
            r.ActualTurn, r.TurnOwner, r.RequestedDraw, r.OriginalHandCount, r.OriginalMaxHp, r.SequenceBefore));
        return BeginOperationProgramDraw(f, f.OwnerSeat, count, new(XiongmuDrawReason));
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveWuLuXunXiongmu)]
    private void ReturnWuXiongmuDraw(ProgramSkillFrame supplied, NativeDrawOutcome outcome)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.Xiongmu!;
        if (!ValidWuXiongmuStarted(f) || r.Stage != XiongmuStage.Drawing || r.DrawOutcome is not null || f.NativeDraw is not null ||
            outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != r.RequestedDraw || outcome.SequenceBefore < r.SequenceBefore ||
            outcome.SequenceAfter < outcome.SequenceBefore || outcome.SequenceAfter > _movementSequence)
            throw new InvalidOperationException("Xiongmu's typed draw returned to a different owner, request or paid stage.");
        ReplaceRuntimeTop(f = f with { Xiongmu = r with { Stage = XiongmuStage.DrawChildren, DrawOutcome = outcome,
            Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new XiongmuDrawReturnedEvent(f.Id, r.RequestedDraw, outcome.ActualDrawIds.Count,
            outcome.ReplacementGainedIds.Count, outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId));
        ContinueWuXiongmu(f.Id);
    }
    private IReadOnlyList<PromptChoice> WuXiongmuChoices(ProgramSkillFrame f)
    {
        if (f.Xiongmu is not { Stage: XiongmuStage.Selection } r || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None) return [];
        PromptChoice C(string branch, string label, int? id = null) => new(new($"wu-xiongmu.{f.Id}.{branch}.{id}.{r.Selected.Count}"), label,
            id is { } cardId ? [cardId] : [], [], new Dictionary<string,string> { ["program-action"] = "wu-lu-xun-xiongmu", ["branch"] = branch,
                ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) });
        var choices = GetHand(_players[f.OwnerSeat]).Concat(GetEquipment(_players[f.OwnerSeat]))
            .Where(c => !r.Selected.Contains(c.Id)).Select(c => C("pick", $"置入牌堆【{c.DisplayName}】", c.Id)).ToList();
        choices.AddRange(r.Selected.Select(id => C("unpick", $"撤回【{GetAdvancedCard(id).DisplayName}】", id)));
        choices.Add(C("finish", $"完成（置入{r.Selected.Count}张）"));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishWuXiongmu(ProgramSkillFrame f)
    {
        var choices = WuXiongmuChoices(f);
        if (choices.Count == 0) { FinishWuXiongmu(f); return; }
        PublishParticipantHandChoice(f, f.OwnerSeat, choices, "雄幕：选择任意张自己的手牌或装备牌随机置入牌堆，再获得等量8点牌。", f.OwnerSeat);
    }
    [ProgramAnswer("wu-lu-xun-xiongmu")]
    private void AnswerWuXiongmu(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Xiongmu: { Stage: XiongmuStage.Selection } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual([choice], WuXiongmuChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Xiongmu cannot substitute a foreign, moved or replayed private material choice.");
        AssertWuXiongmu(f); ClearPendingDecision();
        var branch = choice.Parameters["branch"];
        if (branch != "finish")
        {
            var id = choice.Cards.Single();
            ReplaceRuntimeTop(f = f with { Xiongmu = r with { Selected = branch == "pick" ? r.Selected.Append(id).ToArray() : r.Selected.Where(c => c != id).ToArray() } });
            PublishWuXiongmu(f); return;
        }
        var materials = r.Selected.Select(id => new XiongmuMaterial(id, GetAdvancedCard(id).Kind, _cardZones.GetLocation(id))).ToArray();
        if (materials.Any(m => m.From.OwnerSeat != f.OwnerSeat || m.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)))
            throw new InvalidOperationException("Xiongmu may only place its owner's original real HE entities, never stored grain or public piles.");
        var before = _movementSequence; var oldDeck = _cardZones.CardsAt(CardLocation.DrawPile).Select(c => c.Id).ToArray();
        ReplaceRuntimeTop(f = f with { Xiongmu = r with { Stage = XiongmuStage.InsertChildren, PutMaterials = materials,
            DeckBefore = oldDeck, PutBefore = before, PutAfter = before, Before = before, After = before },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        for (var i = 0; i < oldDeck.Length; i++) AdvanceEventRulesAndQueueFact(new XiongmuDeckEntityFrozenEvent(f.Id, i, oldDeck[i]));
        for (var i = 0; i < materials.Length; i++) AdvanceEventRulesAndQueueFact(new XiongmuPutSelectedEvent(f.Id, i, materials[i].CardId, materials[i].PrintedKind, materials[i].From));
        if (materials.Length == 0) CompleteWuXiongmuPut(f.Id, 0, []);
        else MoveProgramCardsFromMultipleSources(materials.Select(m => m.CardId).ToArray(), CardLocation.DrawPile,
            new(XiongmuPutReason), (batch, moves) => CompleteWuXiongmuPut(f.Id, batch, moves), disclosure: CardMovementDisclosure.FaceDown);
        CaptureWuXiongmuPutTail(f.Id);
        ContinueWuXiongmu(f.Id);
    }
    private void CompleteWuXiongmuPut(long id, long batchId, IReadOnlyList<CardMovementRecord> moves)
    {
        var f = GetActiveProgramFrame(id); var r = f.Xiongmu!;
        if (r.Stage != XiongmuStage.InsertChildren || r.InsertedCount != -1 || moves.Count != r.PutMaterials.Count)
            throw new InvalidOperationException("Xiongmu cannot repeat or invent its real random placement payment.");
        var inserted = moves.Where(m => m.To == CardLocation.DrawPile).Select(m => m.CardId).ToArray();
        var current = _cardZones.CardsAt(CardLocation.DrawPile).Select(c => c.Id).ToArray();
        if (current.Length != r.DeckBefore.Count + inserted.Length || !current.Take(r.DeckBefore.Count).SequenceEqual(r.DeckBefore) ||
            !current.Skip(r.DeckBefore.Count).Order().SequenceEqual(inserted.Order()))
            throw new InvalidOperationException("Xiongmu's just-placed originals no longer form the exact appended draw-pile segment.");
        var order = r.DeckBefore.ToList(); var samples = new List<XiongmuInsertion>(); var randomBefore = _random.State;
        foreach (var cardId in inserted)
        {
            var beforeRandom = _random.State; var position = _random.Next(order.Count + 1);
            samples.Add(new(samples.Count, cardId, order.Count, position, beforeRandom, _random.State)); order.Insert(position, cardId);
        }
        // Exact current partition; only the new entities are interleaved. All
        // pre-existing deck entities retain their relative order, without a shuffle.
        _cardZones.ReorderDrawPileTop(current.Reverse().ToArray(), order.AsEnumerable().Reverse().ToArray(), []);
        var after = _movementSequence;
        ReplaceRuntimeTop(f with { Xiongmu = r with { PutBatchId = batchId, PutAfter = after, After = after,
            PutMovements = moves, InsertedCount = inserted.Length, Insertions = samples } });
        foreach (var sample in samples) AdvanceEventRulesAndQueueFact(new XiongmuInsertedEvent(id, sample.Index, sample.CardId,
            sample.PileCountBefore, sample.Position, sample.RandomBefore, sample.RandomAfter));
        AdvanceEventRulesAndQueueFact(new XiongmuPutPaidEvent(id, batchId, moves.Count, inserted.Length, r.PutBefore, after, randomBefore, _random.State));
    }
    private void CaptureWuXiongmuPutTail(long id)
    {
        var f = GetActiveProgramFrame(id); var r = f.Xiongmu!; var after = _movementSequence;
        if (r.Stage != XiongmuStage.InsertChildren || r.PutTailAfter != -1 || r.InsertedCount < 0)
            throw new InvalidOperationException("Xiongmu lost its completed native equipment-removal tail.");
        var spill = _cardMovements.Where(m => m.Sequence > r.PutAfter && m.Sequence <= after &&
            m.From == CardLocation.WoodenOxGrain(f.OwnerSeat) && m.To == CardLocation.DiscardPile &&
            m.Reason == CardMoveReasons.WoodenOxGrainDiscard && r.PutMovements.Any(p => p.CardKind == CardKind.WoodenOx &&
                p.From == CardLocation.Equipment(f.OwnerSeat))).ToArray();
        ReplaceRuntimeTop(f with { Xiongmu = r with { PutTailAfter = after, After = after, WoodenOxSpill = spill } });
        AdvanceEventRulesAndQueueFact(new XiongmuPutTailCapturedEvent(id, r.PutBatchId, spill.Length, after));
    }
    private bool DrainWuXiongmu(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueWuXiongmu(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Xiongmu: { } r } f || f.Id != id || _pendingDecision is not null || f.NativeDraw is not null) return;
        AssertWuXiongmu(f);
        if (r.Stage == XiongmuStage.Selection) { PublishWuXiongmu(f); return; }
        if (r.Stage == XiongmuStage.Drawing || DrainWuXiongmu(f)) return;
        ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { PendingMovementContinuation = null }); r = f.Xiongmu!;
        if (r.Stage == XiongmuStage.DrawChildren)
        {
            if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishWuXiongmu(f); return; }
            ReplaceRuntimeTop(f = f with { Xiongmu = r with { Stage = XiongmuStage.Selection } }); PublishWuXiongmu(f); return;
        }
        if (r.Stage == XiongmuStage.InsertChildren)
        {
            var candidates = _players[f.OwnerSeat].IsAlive && _winner == Winner.None
                ? _cardZones.CardsAt(CardLocation.DrawPile).Reverse().Concat(_cardZones.CardsAt(CardLocation.DiscardPile))
                    .Where(c => c.Rank == 8).Take(r.InsertedCount).Select(c => new XiongmuMaterial(c.Id, c.Kind, _cardZones.GetLocation(c.Id))).ToArray()
                : Array.Empty<XiongmuMaterial>();
            var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { Xiongmu = r with { Stage = XiongmuStage.ObtainChildren, ObtainMaterials = candidates,
                ObtainBefore = before, ObtainAfter = before, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            for (var i = 0; i < candidates.Length; i++) AdvanceEventRulesAndQueueFact(new XiongmuObtainSelectedEvent(id, i, candidates[i].CardId, candidates[i].PrintedKind, candidates[i].From));
            if (candidates.Length == 0) CompleteWuXiongmuObtain(id, 0, []);
            else MoveProgramCardsFromMultipleSources(candidates.Select(c => c.CardId).ToArray(), CardLocation.Hand(f.OwnerSeat),
                new(XiongmuObtainReason), (batch, moves) => CompleteWuXiongmuObtain(id, batch, moves));
            ContinueWuXiongmu(id); return;
        }
        FinishWuXiongmu(f);
    }
    private void CompleteWuXiongmuObtain(long id, long batchId, IReadOnlyList<CardMovementRecord> moves)
    {
        var f = GetActiveProgramFrame(id); var r = f.Xiongmu!; var after = _movementSequence;
        if (r.Stage != XiongmuStage.ObtainChildren || r.ActualObtained != -1 || moves.Count != r.ObtainMaterials.Count)
            throw new InvalidOperationException("Xiongmu lost its actual rank-eight obtain payment.");
        ReplaceRuntimeTop(f with { Xiongmu = r with { ObtainBatchId = batchId, ObtainAfter = after, After = after,
            ObtainMovements = moves, ActualObtained = moves.Count(m => m.To == CardLocation.Hand(f.OwnerSeat)) } });
        for (var i = 0; i < moves.Count; i++)
        {
            var m = moves[i]; AdvanceEventRulesAndQueueFact(new XiongmuEntityObtainedEvent(id, r.Source, r.GameplayHash, i,
                m.CardId, m.CardKind, m.From, m.To, m.Sequence, batchId));
        }
        AdvanceEventRulesAndQueueFact(new XiongmuObtainPaidEvent(id, batchId, r.InsertedCount,
            moves.Count(m => m.To == CardLocation.Hand(f.OwnerSeat)), r.ObtainBefore, after));
    }
    private void FinishWuXiongmu(ProgramSkillFrame f)
    {
        var r = f.Xiongmu!; AdvanceEventRulesAndQueueFact(new XiongmuCompletedEvent(f.Id, Math.Max(0, r.InsertedCount), Math.Max(0, r.ActualObtained)));
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 29000)]
    private bool ResumeWuXiongmu(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Xiongmu: not null, NativeDraw: null } f || f.Id != id) return false;
        ContinueWuXiongmu(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 29000)]
    private bool ReturnWuXiongmuMovement(ProgramSkillFrame f)
    { if (f.Xiongmu is null) return false; ContinueWuXiongmu(f.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 29000)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 29000)]
    private bool CanContinueIssuedWuXiongmu(ProgramSkillFrame f) => ValidWuXiongmuStarted(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveWuLuXunXiongmu)]
    private PromptChoice AiWuXiongmu(PendingDecision pending, ProgramSkillFrame f)
    {
        var chosen = pending.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "pick" &&
            GetAdvancedCard(c.Cards.Single()).Rank != 8 && GetAdvancedCard(c.Cards.Single()).Kind is not (CardKind.Peach or CardKind.Dodge));
        return chosen ?? pending.Choices.Single(c => c.Parameters.GetValueOrDefault("branch") == "finish");
    }
}
