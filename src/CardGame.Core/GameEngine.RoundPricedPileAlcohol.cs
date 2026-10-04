namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int NextRoundPileAlcoholPrice(int ownerSeat, string skillId, string stateId) => 1 +
        CompleteProgramEventHistory().OfType<ProgramRoundPileAlcoholIssuedEvent>()
            .Where(e => e.Source.OwnerSeat == ownerSeat && e.Source.SkillId == skillId && e.StateId == stateId && e.ActualRoundNumber == _roundNumber)
            .Select(e => e.Price).DefaultIfEmpty(0).Max();

    private bool CanOfferRoundPileAlcohol(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        var effect = trigger.Effects.SingleOrDefault(e => e.Op == SkillProgramEffectOp.UseRoundPricedPileDyingAlcohol);
        if (effect is null) return true;
        return context.Window == SkillProgramTriggerWindow.DyingResponse && ActiveDying is { } dying &&
            context.ParentFrameId == dying.FrameId && context.TargetSeat == dying.VictimSeat && dying.ResponderSeat == candidate.OwnerSeat &&
            _players[dying.VictimSeat].IsAlive && _players[dying.VictimSeat].Hp <= 0 &&
            _cardZones.Count(new(effect.Zones[0], candidate.OwnerSeat)) >= NextRoundPileAlcoholPrice(candidate.OwnerSeat, candidate.SkillId, effect.StateId!);
    }

    private SkillProgramStepOutcome BeginRoundPileAlcohol(ProgramSkillFrame input, string stateId, CardZoneKind zone)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.RoundPileAlcohol is not null || f.WindowContext is not { Window: SkillProgramTriggerWindow.DyingResponse } context ||
            ActiveDying is not { } dying || context.ParentFrameId != dying.FrameId || context.TargetSeat != dying.VictimSeat ||
            dying.ResponderSeat != f.OwnerSeat)
            throw new InvalidOperationException("Round-priced Alcohol lost its exact dying responder.");
        var price = NextRoundPileAlcoholPrice(f.OwnerSeat, f.SkillId, stateId);
        if (!RoundPileAlcoholUnpaidValid(f, dying.FrameId, dying.VictimSeat, stateId, _roundNumber, price, zone))
        { CancelProgramBindingAndCleanup(f, "本轮费用、濒死角色或酒使用权限已失效，未移去醇。"); return SkillProgramStepOutcome.AwaitChild; }
        ReplaceRuntimeTop(f with { RoundPileAlcohol = new(f.InstructionIndex, dying.FrameId, dying.VictimSeat, stateId,
            _roundNumber, price, zone, RoundPileAlcoholStage.Choosing, [], []) });
        PublishRoundPileAlcohol(GetActiveProgramFrame(f.Id)); return SkillProgramStepOutcome.AwaitChoice;
    }

    private bool RoundPileAlcoholUnpaidValid(ProgramSkillFrame f, long dyingId, int victimSeat, string stateId, int round, int price, CardZoneKind zone) =>
        _winner == Winner.None && _players[f.OwnerSeat].IsAlive && _players[victimSeat].IsAlive && _players[victimSeat].Hp <= 0 &&
        HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) && _roundNumber == round &&
        ActiveDying is { } dying && dying.FrameId == dyingId && dying.VictimSeat == victimSeat && dying.ResponderSeat == f.OwnerSeat &&
        NextRoundPileAlcoholPrice(f.OwnerSeat, f.SkillId, stateId) == price && _cardZones.Count(new(zone, f.OwnerSeat)) >= price &&
        CanUseProgramDyingAlcohol(f, SkillProgramEffectOp.UseRoundPricedPileDyingAlcohol);

    private void PublishRoundPileAlcohol(ProgramSkillFrame f)
    {
        var r = f.RoundPileAlcohol!; var choices = new List<PromptChoice>();
        Dictionary<string, string> Parameters(string branch) => new()
        { ["program-action"] = "round-pile-alcohol", ["frame-id"] = f.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), ["branch"] = branch };
        if (r.SelectedIds.Count < r.Price)
            foreach (var card in _cardZones.CardsAt(new(r.Zone, f.OwnerSeat)).Where(c => !r.SelectedIds.Contains(c.Id)))
                choices.Add(new(new($"round-pile-alcohol.{f.Id}.{card.Id}"), $"选择醇【{card.DisplayName}】", [card.Id], [], Parameters("material")));
        if (r.SelectedIds.Count == r.Price)
            choices.Add(new(new($"round-pile-alcohol.{f.Id}.use"), $"移去{r.Price}张醇，令濒死角色使用酒", [], [], Parameters("use")));
        choices.Add(new(new($"round-pile-alcohol.{f.Id}.pass"), "不使用酒", [], [], Parameters("pass")));
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, $"本轮第{r.Price}次：选择{r.Price}张醇，令{_players[r.VictimSeat].Name}视为使用酒。",
            Array.AsReadOnly(choices.SelectMany(c => c.Cards).ToArray()), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = Array.AsReadOnly(choices.ToArray()), SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveRoundPileAlcohol(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.RoundPileAlcohol is not { Stage: RoundPileAlcoholStage.Choosing } r ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || choice.Targets.Count != 0 ||
            choice.Parameters.GetValueOrDefault("frame-id") != f.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("Round-priced Alcohol choice lost its exact material owner.");
        AssertRoundPileAlcohol(f);
        var branch = choice.Parameters.GetValueOrDefault("branch");
        if (branch == "pass")
        {
            if (choice.Cards.Count != 0) throw new InvalidOperationException("Declining Alcohol cannot carry a material.");
            ClearPendingDecision(); ReplaceRuntimeTop(f with { RoundPileAlcohol = null }); AdvanceRuntimeProgram(f.Id); return;
        }
        if (!RoundPileAlcoholUnpaidValid(f, r.DyingFrameId, r.VictimSeat, r.StateId, r.ActualRoundNumber, r.Price, r.Zone))
        { ClearPendingDecision(); CancelProgramBindingAndCleanup(f, "酒救援的未付材料、来源或实际权限已失效。"); return; }
        if (branch == "material")
        {
            if (choice.Cards.Count != 1 || r.SelectedIds.Count >= r.Price || r.SelectedIds.Contains(choice.Cards[0]) ||
                _cardZones.GetLocation(choice.Cards[0]) != new CardLocation(r.Zone, f.OwnerSeat))
                throw new InvalidOperationException("Alcohol material is no longer in the exact public pile.");
            ClearPendingDecision();
            ReplaceRuntimeTop(f with { RoundPileAlcohol = new(r.InstructionIndex, r.DyingFrameId, r.VictimSeat, r.StateId, r.ActualRoundNumber,
                r.Price, r.Zone, r.Stage, r.SelectedIds.Append(choice.Cards[0]).ToArray(), [], r.CardUseFrameId) });
            PublishRoundPileAlcohol(GetActiveProgramFrame(f.Id)); return;
        }
        if (branch != "use" || choice.Cards.Count != 0 || r.SelectedIds.Count != r.Price ||
            r.SelectedIds.Any(id => _cardZones.GetLocation(id) != new CardLocation(r.Zone, f.OwnerSeat)))
            throw new InvalidOperationException("Alcohol requires exactly the frozen prospective price.");
        var location = new CardLocation(r.Zone, f.OwnerSeat);
        var cards = r.SelectedIds.Select(id => _cardZones.CardsAt(location).Single(c => c.Id == id)).ToArray();
        var materials = cards.Select(c => new RoundPileAlcoholMaterial(c.Id, c.Kind, location, EffectiveSuit(_players[f.OwnerSeat], c) is Suit.Heart or Suit.Diamond)).ToArray();
        var source = new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
        ClearPendingDecision();
        // Actual issuance precedes its cost children, as for an ordinary real card use.
        // Pile movement itself cannot synchronously push a SilverLion removal child.
        var useId = BeginCardUse(cards[0], r.VictimSeat, [r.VictimSeat], CardKind.Alcohol,
            physicalCardIds: Array.AsReadOnly(cards.Select(c => c.Id).ToArray()), conversionSource: source);
        var use = LifecycleCardUse(useId)!;
        ReplaceRuntimeFrame(f.Id, f with { RoundPileAlcohol = new(r.InstructionIndex, r.DyingFrameId, r.VictimSeat, r.StateId, r.ActualRoundNumber,
            r.Price, r.Zone, RoundPileAlcoholStage.Issued, r.SelectedIds, materials, useId) });
        ReplaceRuntimeTop(use with { RoundPileAlcoholReturn = new(f.Id, r.InstructionIndex, r.DyingFrameId, source, r.StateId,
            r.ActualRoundNumber, r.Price, r.VictimSeat) });
        MoveCards(cards, location, CardLocation.Processing, new($"skill-program.{f.SkillId}.round-pile-alcohol.pay"));
        foreach (var card in cards)
        {
            var move = _cardMovements.Last(m => m.CardId == card.Id);
            AdvanceEventRulesAndQueueFact(new ProgramRoundPileAlcoholMaterialPaidEvent(f.Id, card.Id, location, move.Sequence));
        }
        AdvanceEventRulesAndQueueFact(new ProgramRoundPileAlcoholIssuedEvent(f.Id, useId, r.DyingFrameId, source, r.StateId, r.ActualRoundNumber, r.Price, r.VictimSeat));
        ContinueRoundPileAlcoholUse(useId);
    }

    private void ContinueRoundPileAlcoholUse(long id)
    {
        if (_resolutionStack.LastOrDefault() is not CardUseFrame use || use.Id != id || use.RoundPileAlcoholReturn is not { })
            throw new InvalidOperationException("Pile Alcohol lost its exact use cost return.");
        AssertRoundPileAlcoholUse(use);
        if (use.RoundPileAlcoholCostDrained) throw new InvalidOperationException("Pile Alcohol cost attempted to resume twice.");
        if (TryBeginCardsMovedProgramWindow(id)) return;
        ReplaceRuntimeTop(use with { RoundPileAlcoholCostDrained = true });
        var card = GetAttackCard(use.CardId);
        if (_winner != Winner.None || !_players[use.SourceSeat].IsAlive || ActiveDying?.FrameId != use.RoundPileAlcoholReturn.DyingFrameId)
        { FinishCardUse(id, card, CardKind.Alcohol); return; }
        BeginSimpleCardUse(id, new(card.Id, SimpleCardUseEffect.Recovery, 1));
    }

    private void ReturnRoundPileAlcoholUse(CardUseFrame use)
    {
        if (use.RoundPileAlcoholReturn is not { } token) return;
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != token.ProgramFrameId || f.RoundPileAlcohol is not { Stage: RoundPileAlcoholStage.Issued } r ||
            r.CardUseFrameId != use.Id || r.InstructionIndex != token.InstructionIndex || r.DyingFrameId != token.DyingFrameId || !use.RoundPileAlcoholCostDrained)
            throw new InvalidOperationException("Completed pile Alcohol lost its single typed program return.");
        ReplaceRuntimeTop(f with { RoundPileAlcohol = r with { Stage = RoundPileAlcoholStage.Finished } });
        AdvanceRuntimeProgram(f.Id);
    }

    private bool ResumeRoundPileAlcohol(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.RoundPileAlcohol is not { } r) return false;
        AssertRoundPileAlcohol(f);
        if (r.Stage == RoundPileAlcoholStage.Choosing) return true;
        if (r.Stage != RoundPileAlcoholStage.Finished) throw new InvalidOperationException("An issued Alcohol must complete its owning use before returning.");
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(id)) return true;
        ReplaceRuntimeTop(f with { RoundPileAlcohol = null }); return false;
    }

    private void AssertRoundPileAlcohol(ProgramSkillFrame f)
    {
        if (f.RoundPileAlcohol is not { } r) return;
        var effect = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.UseRoundPricedPileDyingAlcohol || effect.StateId != r.StateId || !effect.Zones.SequenceEqual([r.Zone]) ||
            f.InstructionIndex != r.InstructionIndex || r.Price < 1 || r.ActualRoundNumber < 1 || r.ActualRoundNumber != _roundNumber ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.DyingResponse } context || context.ParentFrameId != r.DyingFrameId || context.TargetSeat != r.VictimSeat ||
            r.SelectedIds.Count > r.Price || r.SelectedIds.Distinct().Count() != r.SelectedIds.Count ||
            r.SelectedIds is not System.Collections.IList { IsReadOnly: true } || r.Materials is not System.Collections.IList { IsReadOnly: true } ||
            r.Stage == RoundPileAlcoholStage.Choosing && (r.Materials.Count != 0 || r.CardUseFrameId is not null || r.SelectedIds.Any(id => _cardZones.GetLocation(id) != new CardLocation(r.Zone, f.OwnerSeat))) ||
            r.Stage is RoundPileAlcoholStage.Issued or RoundPileAlcoholStage.Finished &&
                (r.Materials.Count != r.Price || !r.Materials.Select(m => m.CardId).SequenceEqual(r.SelectedIds) || r.Materials.Any(m => m.From != new CardLocation(r.Zone, f.OwnerSeat)) ||
                 !CompleteProgramEventHistory().OfType<ProgramRoundPileAlcoholIssuedEvent>().Any(e => e.ProgramFrameId == f.Id && e.CardUseFrameId == r.CardUseFrameId &&
                     e.DyingFrameId == r.DyingFrameId && e.StateId == r.StateId && e.ActualRoundNumber == r.ActualRoundNumber && e.Price == r.Price && e.VictimSeat == r.VictimSeat &&
                     e.Source == new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId))))
            throw new InvalidOperationException("Round pile Alcohol lost its frozen invoice, actual use or source provenance.");
    }

    private void AssertRoundPileAlcoholUse(CardUseFrame use)
    {
        if (use.RoundPileAlcoholReturn is not { } token) return;
        var f = _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(p => p.Id == token.ProgramFrameId);
        var r = f?.RoundPileAlcohol;
        if (f is null || r is not { Stage: RoundPileAlcoholStage.Issued } || r.CardUseFrameId != use.Id || r.InstructionIndex != token.InstructionIndex ||
            r.DyingFrameId != token.DyingFrameId || r.StateId != token.StateId || r.ActualRoundNumber != token.ActualRoundNumber || r.Price != token.Price || r.VictimSeat != token.VictimSeat ||
            token.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) ||
            r.SelectedIds.Count != r.Price || r.Materials.Count != r.Price || r.Price < 1 ||
            use.CardKind != CardKind.Alcohol || use.SourceSeat != r.VictimSeat || use.DyingResponse is not null ||
            !(use.PhysicalCardIds ?? []).SequenceEqual(r.SelectedIds) || use.CardId != r.SelectedIds[0] || use.Action is not { Type: CardActionType.Use } action ||
            action.ActorSeat != r.VictimSeat || action.ProviderSeat != f.OwnerSeat || action.EffectiveKind != CardKind.Alcohol ||
            !use.TargetSeats.SequenceEqual([r.VictimSeat]) || !action.TargetSeats.SequenceEqual(use.TargetSeats) || !action.EffectiveDesignatedTargetSeats.SequenceEqual(use.TargetSeats) ||
            action.PhysicalCards.Count != r.Price || !action.PhysicalCards.Select(c => c.CardId).SequenceEqual(r.SelectedIds) || !action.ConversionChain.SequenceEqual([token.Source]) ||
            action.PhysicalCards.Where((cost, index) => cost.From != r.Materials[index].From || cost.CardKind != r.Materials[index].CardKind).Any() ||
            r.Materials.Any(m => !CompleteProgramEventHistory().OfType<ProgramRoundPileAlcoholMaterialPaidEvent>().Any(e => e.ProgramFrameId == f.Id && e.CardId == m.CardId && e.From == m.From &&
                _cardMovements.Any(move => move.Sequence == e.MovementSequence && move.CardId == m.CardId && move.From == m.From && move.To == CardLocation.Processing &&
                    move.Reason.Value == $"skill-program.{f.SkillId}.round-pile-alcohol.pay"))))
            throw new InvalidOperationException("Pile Alcohol use lost its exact typed invoice and atomic material payment.");
        AssertRoundPileAlcohol(f);
        foreach (var window in _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().Where(w => w.ResumeRoundPileAlcoholUseFrameId == use.Id))
            if (use.RoundPileAlcoholCostDrained || window.Batch.ParentFrameId != use.Id || window.Batch.AwaitingProgramFrameId is not null ||
                window.Batch.Movements.Count == 0 || window.Batch.Movements.Any(move => !r.SelectedIds.Contains(move.CardId) ||
                    move.From != new CardLocation(r.Zone, f.OwnerSeat) || move.To != CardLocation.Processing ||
                    move.Reason.Value != $"skill-program.{f.SkillId}.round-pile-alcohol.pay" || !_cardMovements.Contains(move)))
                throw new InvalidOperationException("Pile Alcohol cost observer lost its exact actual movement return.");
    }

    private bool IsRoundPricedPileAlcoholRide(int dyingIndex, DyingFrame dying)
    {
        if (dyingIndex < 0 || dyingIndex + 2 >= _resolutionStack.Count || _resolutionStack[dyingIndex + 1] is not ProgramSkillFrame f ||
            f.RoundPileAlcohol is not { Stage: RoundPileAlcoholStage.Issued } r || r.DyingFrameId != dying.Id || r.VictimSeat != dying.VictimSeat || f.OwnerSeat != dying.ResponderSeat ||
            _resolutionStack[dyingIndex + 2] is not CardUseFrame use || use.Id != r.CardUseFrameId || use.RoundPileAlcoholReturn?.ProgramFrameId != f.Id) return false;
        AssertRoundPileAlcoholUse(use);
        for (var index = dyingIndex + 3; index < _resolutionStack.Count; index++)
        {
            var child = _resolutionStack[index]; var parent = _resolutionStack[index - 1];
            if (child is CardsMovedTriggerWindowFrame movement && parent.Id == use.Id && movement.ResumeRoundPileAlcoholUseFrameId == use.Id &&
                movement.Batch.ParentFrameId == use.Id && movement.Batch.AwaitingProgramFrameId is null && movement.Batch.Movements.All(move =>
                    r.SelectedIds.Contains(move.CardId) && move.From == new CardLocation(r.Zone, f.OwnerSeat) && move.To == CardLocation.Processing &&
                    move.Reason.Value == $"skill-program.{f.SkillId}.round-pile-alcohol.pay")) continue;
            // A nested actual dying rescue helper proves its entire remaining suffix.
            // Keep this scoped to the already-proved round invoice and real owning Wine.
            if (parent is DyingFrame nested && (child is CardUseFrame && IsPaidHandRepaymentRescueRide(index - 1, nested) ||
                child is ProgramSkillFrame && (IsPaidHandRepaymentProgramAlcoholRide(index - 1, nested) || PolicyCounterspellVirtualAlcoholRide(index - 1, nested)))) return true;
            if (!ParticipantHandRescueObserverRide(child, parent, use) && !PreventionDrawObserverEdge(index)) return false;
        }
        return true;
    }
}
