namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasPindianRevealCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.AddRaoshePindianRankAndRestoreGushe);
    private bool TryBeginPairPindianReveal(PindianFrame frame,PindianResult result)
    {
        if (!HasPindianRevealCapability) return false;
        if (_resolutionStack.LastOrDefault()?.Id != frame.Id || frame.Result is not null || frame.OpponentSeat != result.OpponentSeat ||
            frame.SourceSeat != result.SourceSeat || (frame.SourceCardId != result.SourceCardId && !IsDouyuPairAssignment(frame, result)) || result.SourceCardId == result.OpponentCardId ||
            _cardZones.GetLocation(result.SourceCardId) != CardLocation.Processing || _cardZones.GetLocation(result.OpponentCardId) != CardLocation.Processing)
            throw new InvalidOperationException("A reveal window lost its original pair and paid entities.");
        ReplaceRuntimeTop(frame with { Result = result });
        var cards = Array.AsReadOnly(new[] { new PindianRevealedCard(result.SourceSeat,result.SourceCardId,result.SourceRank,result.SourceRank),
            new PindianRevealedCard(result.OpponentSeat,result.OpponentCardId,result.OpponentRank,result.OpponentRank) });
        AdvanceEventRulesAndQueueFact(new PindianCardsRevealedEvent(frame.Id,cards,false));
        BeginPindianRevealWindow(frame.Id,false,cards); return true;
    }
    private void BeginPindianRevealWindow(long parent,bool simultaneous,IReadOnlyList<PindianRevealedCard> cards)
    {
        if (_resolutionStack.LastOrDefault()?.Id != parent || cards.Count < 2 || cards.Select(c => c.Seat).Distinct().Count() != cards.Count ||
            cards.Select(c => c.CardId).Distinct().Count() != cards.Count || cards.Any(c => _cardZones.GetLocation(c.CardId) != CardLocation.Processing))
            throw new InvalidOperationException("A Pindian reveal requires its actual complete paid group.");
        var candidates = cards.SelectMany(c => CollectProgramTriggerCandidates(_players[c.Seat],SkillProgramTriggerWindow.PindianCardsRevealed))
            .Where(c => GetProgramTrigger(c).Effects is [{ Op: SkillProgramEffectOp.AddRaoshePindianRankAndRestoreGushe }])
            .OrderByDescending(c => c.Priority).ThenBy(c => c.OwnerSeat).ThenBy(c => c.SkillId,StringComparer.Ordinal).ThenBy(c => c.SkillInstanceId,StringComparer.Ordinal).ToArray();
        PushRuntimeFrame(new PindianRevealWindowFrame(++_resolutionSequence,parent,simultaneous,cards,candidates));
        ContinuePindianRevealWindow();
    }
    private ProgramSkillWindowContext PindianRevealContext(PindianRevealWindowFrame w,ProgramTriggerCandidate c) =>
        new(SkillProgramTriggerWindow.PindianCardsRevealed,w.Id,c.OwnerSeat,SourceSeat:c.OwnerSeat,TargetSeat:c.OwnerSeat,
            Amount:w.Cards.Single(card => card.Seat == c.OwnerSeat).Rank,Facts:CaptureProgramTriggerFacts(_players[c.OwnerSeat]));
    private bool TryGetPindianRevealTrigger(out ProgramTriggerCandidate candidate,out ProgramSkillWindowContext context)
    {
        candidate = null!; context = null!;
        if (_resolutionStack.LastOrDefault() is not PindianRevealWindowFrame w || w.CandidateIndex >= w.Candidates.Count) return false;
        candidate = w.Candidates[w.CandidateIndex]; context = PindianRevealContext(w,candidate); return true;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 240)]
    private bool CanRunPindianReveal(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if (context.Window != SkillProgramTriggerWindow.PindianCardsRevealed) return true;
        var w = _resolutionStack.OfType<PindianRevealWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId);
        return w is not null && w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count && w.Candidates[w.CandidateIndex] == c &&
            context.SourceSeat == c.OwnerSeat && context.TargetSeat == c.OwnerSeat && w.Cards.Any(card => card.Seat == c.OwnerSeat && card.Rank == context.Amount &&
                card.Rank <= _players[c.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.RaoShe)) && _players[c.OwnerSeat].IsAlive &&
            GetProgramTrigger(c).Effects is [{ Op: SkillProgramEffectOp.AddRaoshePindianRankAndRestoreGushe }];
    }
    private void ContinuePindianRevealWindow()
    {
        while (_resolutionStack.LastOrDefault() is PindianRevealWindowFrame w)
        {
            AssertPindianRevealWindows();
            if (w.CandidateIndex == w.Candidates.Count) { CompletePindianRevealWindow(w); return; }
            var c = w.Candidates[w.CandidateIndex]; var context = PindianRevealContext(w,c);
            if (!CanRunProgramTrigger(c,context)) { ReplaceRuntimeTop(w with { CandidateIndex = w.CandidateIndex + 1 }); continue; }
            ExposeProgramTriggerDecision(c,context); return;
        }
    }
    private void CompletePindianRevealBinding(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if (_resolutionStack.LastOrDefault() is not PindianRevealWindowFrame w || w.Id != context.ParentFrameId ||
            w.CandidateIndex >= w.Candidates.Count || w.Candidates[w.CandidateIndex] != c)
            throw new InvalidOperationException("A reveal binding lost its exact typed parent candidate.");
        ReplaceRuntimeTop(w with { CandidateIndex = w.CandidateIndex + 1 }); ContinuePindianRevealWindow();
    }
    private void CompletePindianRevealBinding(ProgramSkillFrame f)
    {
        if (_resolutionStack.LastOrDefault() is not PindianRevealWindowFrame w || f.WindowContext is not { } c || w.Id != c.ParentFrameId ||
            w.CandidateIndex >= w.Candidates.Count || !MountObserverCandidateMatches(f,w.Candidates[w.CandidateIndex]))
            throw new InvalidOperationException("The completed reveal program lost its exact native candidate.");
        CompletePindianRevealBinding(w.Candidates[w.CandidateIndex],c);
    }
    private void AddRaoshePindianRankAndRestoreGushe(ProgramSkillFrame f,string skillId)
    {
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (f.InstructionIndex != 1 || f.TriggerId is null || f.WindowContext is not { Window: SkillProgramTriggerWindow.PindianCardsRevealed } c ||
            index < 1 || _resolutionStack[index - 1] is not PindianRevealWindowFrame w || w.Id != c.ParentFrameId ||
            w.CandidateIndex >= w.Candidates.Count || !MountObserverCandidateMatches(f,w.Candidates[w.CandidateIndex]))
            throw new InvalidOperationException("JiCi lost its own exact actual reveal.");
        var card = w.Cards.Single(card => card.Seat == f.OwnerSeat); var bonus = _players[f.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.RaoShe);
        if (card.Rank > bonus || !_players[f.OwnerSeat].IsAlive) throw new InvalidOperationException("JiCi's revealed rank no longer qualifies.");
        var program = _contentRegistry.GetSkill(skillId).Program ?? throw new InvalidOperationException("JiCi refund requires an actual paired Gushe definition.");
        var activations = program.Activations.Where(a => a.Effects is [{ Op: SkillProgramEffectOp.StartSimultaneousPindian }]).ToArray();
        if (activations is not [var activation] || activation.UsesPerPhase != 1) throw new InvalidOperationException("JiCi may restore only its exact simultaneous contest phase activation.");
        var key = (f.OwnerSeat,skillId,activation.UsageGroup); var before = _programPhaseUses.GetValueOrDefault(key);
        if (before > 0) _programPhaseUses[key] = 0;
        var after = Math.Min(13,checked(card.Rank + bonus));
        ReplaceRuntimeFrame(w.Id,w with { Cards = w.Cards.Select(c => c.Seat == f.OwnerSeat ? c with { Rank = after } : c).ToArray() });
        AdvanceEventRulesAndQueueFact(new PindianRevealRankAdjustedEvent(w.Id,SimultaneousSource(f),card.CardId,card.Rank,bonus,after,skillId,activation.UsageGroup,before));
    }
    private void CompletePindianRevealWindow(PindianRevealWindowFrame w)
    {
        PopResolutionFrame(w.Id,ResolutionFrameKind.PindianRevealWindow);
        if (w.Simultaneous)
        {
            if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { SimultaneousPindian: { Stage: SimultaneousPindianStage.Revealing } r } f || f.Id != w.ParentFrameId)
                throw new InvalidOperationException("The simultaneous common reveal lost its program return.");
            ReplaceRuntimeTop(f with { SimultaneousPindian = r with { Cards = w.Cards, Stage = SimultaneousPindianStage.Comparing } });
            AdvanceRuntimeProgram(f.Id); return;
        }
        if (_resolutionStack.LastOrDefault() is not PindianFrame { Result: { } old } pair || pair.Id != w.ParentFrameId || w.Cards.Count != 2)
            throw new InvalidOperationException("The single pair reveal lost its unchanged Pindian return.");
        var result = old with { SourceRank = w.Cards.Single(c => c.Seat == pair.SourceSeat).Rank, OpponentRank = w.Cards.Single(c => c.Seat == pair.OpponentSeat).Rank };
        ReplaceRuntimeTop(pair = pair with { Result = result });
        AdvanceEventRulesAndQueueFact(new PindianResultDeterminedEvent(pair.Id,pair.SkillId,result));
        AddLog("Pindian",$"{_players[result.SourceSeat].Name} 以 {result.SourceRank} 点与 {_players[result.OpponentSeat].Name} 的 {result.OpponentRank} 点拼点，" +
            (result.SourceWon ? "发起者获胜。" : "发起者未赢。"),result.SourceSeat,result.OpponentSeat);
        if (!BeginPindianClaims(pair)) CompletePindian((PindianFrame)_resolutionStack[^1]);
    }
    private bool IsPairPindianRevealSubtree(PindianFrame pair) => HasPindianRevealCapability &&
        _resolutionStack.OfType<PindianRevealWindowFrame>().Any(w => !w.Simultaneous && w.ParentFrameId == pair.Id && ExactPairRevealParent(pair,w));
    private bool ExactPairRevealParent(PindianFrame pair,PindianRevealWindowFrame w)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == pair.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame parent || parent.Id != pair.ParentFrameId ||
            pair.PindianStep != PindianStep.ChooseOpponentCard || pair.Result is not { } result || pair.ProgramResultBind is null ||
            parent.PindianResultBindings.Any(b => b.Name == pair.ProgramResultBind) || pair.SourceSeat != result.SourceSeat || pair.OpponentSeat != result.OpponentSeat ||
            (pair.SourceCardId != result.SourceCardId && !IsDouyuPairAssignment(pair, result)) || !w.Cards.Select(c => (c.Seat,c.CardId,c.OriginalRank)).SequenceEqual(new[] {
                (result.SourceSeat,result.SourceCardId,result.SourceRank),(result.OpponentSeat,result.OpponentCardId,result.OpponentRank) })) return false;
        var held = pair.ParentProcessingCardIds ?? [];
        return !held.Contains(result.SourceCardId) && !held.Contains(result.OpponentCardId) &&
            _cardZones.CardsAt(CardLocation.Processing).Select(c => c.Id).Order().SequenceEqual(held.Concat(w.Cards.Select(c => c.CardId)).Order()) &&
            _players[result.SourceSeat].IsAlive && _players[result.OpponentSeat].IsAlive;
    }
    private void AssertPindianRevealWindows()
    {
        foreach (var w in _resolutionStack.OfType<PindianRevealWindowFrame>())
        {
            var index = _resolutionStack.FindIndex(x => x.Id == w.Id);
            if (index < 1 || _resolutionStack[index - 1].Id != w.ParentFrameId || w.CandidateIndex < 0 || w.CandidateIndex > w.Candidates.Count ||
                w.Cards.Count is < 2 or > 4 || w.Cards.Select(c => c.Seat).Distinct().Count() != w.Cards.Count || w.Cards.Select(c => c.CardId).Distinct().Count() != w.Cards.Count ||
                w.Cards.Any(c => _cardZones.GetLocation(c.CardId) != CardLocation.Processing) ||
                CompleteProgramEventHistory().OfType<PindianCardsRevealedEvent>().Count(e => e.ParentFrameId == w.ParentFrameId && e.Simultaneous == w.Simultaneous &&
                    e.Cards.Select(c => (c.Seat,c.CardId,c.OriginalRank)).SequenceEqual(w.Cards.Select(c => (c.Seat,c.CardId,c.OriginalRank)))) != 1 ||
                (w.Simultaneous ? _resolutionStack[index - 1] is not ProgramSkillFrame { SimultaneousPindian.Stage: SimultaneousPindianStage.Revealing } :
                    _resolutionStack[index - 1] is not PindianFrame pair || !ExactPairRevealParent(pair,w)))
                throw new InvalidOperationException("The Pindian reveal window lost its exact paid parent, common entities or public original ranks.");
            foreach (var card in w.Cards)
            {
                var expected = card.OriginalRank;
                foreach (var adjustment in CompleteProgramEventHistory().OfType<PindianRevealRankAdjustedEvent>().Where(e => e.WindowFrameId == w.Id && e.CardId == card.CardId))
                {
                    if (adjustment.Source.OwnerSeat != card.Seat || adjustment.Before != expected || adjustment.Bonus < expected || adjustment.After != Math.Min(13,expected + adjustment.Bonus) ||
                        !w.Candidates.Any(c => c.OwnerSeat == card.Seat && c.SkillId == adjustment.Source.SkillId && c.BindingId == adjustment.Source.BindingId && c.SkillInstanceId == adjustment.Source.SkillInstanceId) ||
                        adjustment.PreviousPhaseUses is < 0 or > 1)
                        throw new InvalidOperationException("A Pindian revealed rank lacks its exact actual JiCi adjustment chain.");
                    expected = adjustment.After;
                }
                if (expected != card.Rank || card.OriginalRank is < 1 or > 13) throw new InvalidOperationException("A Pindian reveal acquired an unissued rank.");
            }
        }
    }
}
