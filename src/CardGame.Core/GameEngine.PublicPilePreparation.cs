using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static string PreparationReason(ProgramSkillFrame f, string step) => $"skill-program.{f.SkillId}.public-pile-preparation.{step}";
    private long PreparationSequence => _cardMovements.LastOrDefault()?.Sequence ?? 0;
    private bool PreparationGameEnded() => _status == EngineStatus.Completed && _phase == TurnPhase.Finished && _winner != Winner.None &&
        CompleteProgramEventHistory().OfType<GameEndedEvent>().Count(e => e.Winner == _winner &&
            e.TeamId == (IsTeamMode ? _winnerTeamId : null) && e.FactionId == (IsNationalWarMode ? _winnerFactionId : null)) == 1;
    private bool PreparationSourceAlive(ProgramSkillFrame f) => _players[f.OwnerSeat].IsAlive && _winner == Winner.None &&
        HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId);
    private PublicPersistentPileSource? PreparationReferencedPile(ProgramSkillFrame f, SkillProgramEffect effect)
    {
        var reference = SelectDomainReference(f, effect.SkillIds.Single());
        return reference.Instance is { } instance ? _publicPersistentPiles.GetValueOrDefault((f.OwnerSeat, effect.SkillIds.Single(), instance)) : null;
    }
    private bool ExactPreparationParent(ProgramSkillFrame f, SkillProgramEffectOp op)
    {
        if (f.TriggerId is null || f.InstructionIndex != 1 || f.WindowContext is not { } c || c.OwnerSeat != f.OwnerSeat ||
            GetProgramTrigger(f).Effects is not [var effect] || effect.Op != op) return false;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (index <= 0 || _resolutionStack[index - 1].Id != c.ParentFrameId) return false;
        if (op == SkillProgramEffectOp.RemovePublicPileAfterAttackDamage)
        {
            if (_resolutionStack[index - 1] is not DamageTriggerWindowFrame w || w.TriggerWindow != SkillProgramTriggerWindow.AfterDamageApplied ||
                w.TargetSeat != f.OwnerSeat || c.TargetSeat != f.OwnerSeat || c.Window != w.TriggerWindow || c.Amount <= 0 ||
                w.SourceCard is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.Duel) ||
                w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count ||
                !MountObserverCandidateMatches(f, w.Candidates[w.CandidateIndex].ToProgramCandidate())) return false;
            if (_resolutionStack.OfType<DamageFrame>().SingleOrDefault(d => d.Id == w.ParentFrameId) is not { } applied ||
                c.DamageFrameId != applied.Id || applied.SourceSeat != w.SourceSeat || applied.TargetSeat != f.OwnerSeat || applied.Amount != c.Amount) return false;
            // The original attack may be suspended by a paid movement observer. Resolve
            // its existing owning frame, never the global newest active attack/window.
            var original = _resolutionStack.FirstOrDefault(x => x.Id == applied.ParentFrameId) switch
            {
                CardUseFrame use => use.CardAttack,
                ProgramSkillFrame program => program.CardAttack,
                JudgmentFrame judgment => judgment.CardAttack,
                _ => null
            };
            return original is { DamageWasApplied: true } && original.EffectiveCardKind == w.SourceCard &&
                original.SourceSeat == applied.SourceSeat && original.TargetSeat == applied.TargetSeat &&
                CompleteProgramEventHistory().OfType<DamageRequestedEvent>().Count(e => e.ResolutionId == applied.Id &&
                    e.TargetSeat == applied.TargetSeat && e.SourceSeat == applied.SourceSeat && e.Amount == applied.Amount && e.Nature == applied.Nature) == 1;
        }
        if (f.OwnerSeat != _currentSeat || c.SourceSeat != f.OwnerSeat || c.TargetSeat != f.OwnerSeat) return false;
        if (op == SkillProgramEffectOp.StoreNonBasicOwnedPublicPile)
            return c.Window == SkillProgramTriggerWindow.TurnEnding && _resolutionStack[index - 1] is TurnEndingBoundaryFrame ending &&
                ending.OwnerSeat == f.OwnerSeat && ending.ItemIndex >= 0 && ending.ItemIndex < ending.Items.Count &&
                ending.Items[ending.ItemIndex].Candidate is { } candidate && MountObserverCandidateMatches(f, candidate);
        return c.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && _resolutionStack[index - 1] is ProgramLifecycleTriggerWindowFrame start &&
            start.Window == c.Window && start.Continuation == ProgramLifecycleContinuation.NormalTurnStart && start.OwnerSeat == f.OwnerSeat &&
            start.CandidateIndex >= 0 && start.CandidateIndex < start.Candidates.Count && MountObserverCandidateMatches(f, start.Candidates[start.CandidateIndex]);
    }
    private bool CanRunPublicPilePreparation(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (trigger.Effects is not [{ Op: SkillProgramEffectOp.RemovePublicPileAfterAttackDamage } e]) return true;
        var reference = ReferencedPublicPileSources(candidate.OwnerSeat, e.SkillIds.Single(), candidate.SkillInstanceId);
        return context.Amount > 0 && reference.Any(p => PublicPileCards(p).Count > 0);
    }
    private SkillProgramStepOutcome BeginPublicPilePreparation(SkillProgramEffect effect, ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.PublicPilePreparation is not null || !ExactPreparationParent(f, effect.Op))
            throw new InvalidOperationException("A preparation pile producer requires its exact real owner window.");
        if (!PreparationSourceAlive(f)) return SkillProgramStepOutcome.Continue;
        var pile = effect.Op == SkillProgramEffectOp.StoreNonBasicOwnedPublicPile ? EnsurePublicPileSource(f, int.MaxValue) : PreparationReferencedPile(f, effect);
        if (effect.Op == SkillProgramEffectOp.RemovePublicPileAfterAttackDamage && (pile is null || PublicPileCards(pile).Count == 0)) return SkillProgramStepOutcome.Continue;
        foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
        var issuer = new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
        ReplaceRuntimeTop(f = f with { PublicPilePreparation = new() { InstructionIndex = 1, Operation = effect.Op, Issuer = issuer,
            GameplayHash = f.GameplayHash, ActualTurn = _turnNumber, ParentId = f.WindowContext!.ParentFrameId,
            Stage = PublicPilePreparationStage.Choosing, Pile = pile } });
        AdvanceEventRulesAndQueueFact(new PublicPilePreparationStartedEvent(f.Id, effect.Op, issuer, f.GameplayHash, _turnNumber, f.WindowContext.ParentFrameId, pile));
        PublishPublicPilePreparation(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IEnumerable<Card> PreparationStoreCards(ProgramSkillFrame f) => GetHand(_players[f.OwnerSeat]).Concat(GetEquipment(_players[f.OwnerSeat]))
        .Where(c => !MatchesSkillProgramCardCategory(c.Kind, SkillProgramCardCategory.Basic) && !c.IsGeneralWeapon &&
            !IsActiveProgramSourceEquipmentCard(f.OwnerSeat, f.SkillId, f.SkillInstanceId, c));
    private bool PreparationPileCurrent(PublicPersistentPileSource p) => _publicPersistentPiles.GetValueOrDefault((p.OwnerSeat, p.SkillId, p.SkillInstanceId)) == p;
    private IReadOnlyList<PromptChoice> PublicPilePreparationChoices(ProgramSkillFrame f)
    {
        var r = f.PublicPilePreparation!; var list = new List<PromptChoice>();
        void Add(string token, string text, IReadOnlyList<int> cards, IReadOnlyList<int> targets) => list.Add(new(new($"pile-preparation.{f.Id}.{token}"), text, cards, targets,
            new Dictionary<string, string> { ["program-action"] = "public-pile-preparation", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["pile-action"] = token }));
        if (r.Operation == SkillProgramEffectOp.StoreNonBasicOwnedPublicPile)
        {
            foreach (var c in PreparationStoreCards(f).Where(c => !r.SelectedIds.Contains(c.Id))) Add("store-" + c.Id, "存入【" + PublicPileCardLabel(c) + "】", [c.Id], []);
            Add("finish", "完成存牌", [], []);
        }
        else if (r.Operation == SkillProgramEffectOp.RemovePublicPileAfterAttackDamage)
        {
            if (r.Pile is { } p && PreparationPileCurrent(p)) foreach (var c in PublicPileCards(p)) Add("remove-" + c.Id, "移去【" + PublicPileCardLabel(c) + "】", [c.Id], []);
        }
        else
        {
            Add("discard", "移去所有引兵牌，将手牌摸至体力上限", [], []);
            foreach (var target in _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat && p.Hp <= _players[f.OwnerSeat].Hp))
                Add("gift-" + target.Seat, "令 " + target.Name + " 获得所有引兵牌、回复体力并摸牌", [], [target.Seat]);
        }
        return Array.AsReadOnly(list.ToArray());
    }
    private void PublishPublicPilePreparation(ProgramSkillFrame f)
    {
        if (PreparationGameEnded()) return;
        var r = f.PublicPilePreparation!; var choices = PublicPilePreparationChoices(f);
        if (!PreparationSourceAlive(f) || choices.Count == 0) { FinishPublicPilePreparation(f, false); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, skill.Description, choices.SelectMany(c => c.Cards).Distinct().ToArray(),
            choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = r.Operation == SkillProgramEffectOp.StoreNonBasicOwnedPublicPile,
            TargetSeat = f.OwnerSeat, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    private void ResolvePublicPilePreparationChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("A preparation choice lost its owning frame.");
        AssertPublicPilePreparation(f); var r = f.PublicPilePreparation!;
        if (r.Stage != PublicPilePreparationStage.Choosing || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([choice], [PublicPilePreparationChoices(f).Single(c => c.Id == choice.Id)]))
            throw new InvalidOperationException("A preparation choice must name its exact current entity or beneficiary.");
        ClearPendingDecision();
        if (!PreparationSourceAlive(f)) { FinishPublicPilePreparation(f, false); return; }
        var token = choice.Parameters["pile-action"];
        if (r.Operation == SkillProgramEffectOp.StoreNonBasicOwnedPublicPile && token.StartsWith("store-", StringComparison.Ordinal))
        { ReplaceRuntimeTop(f = f with { PublicPilePreparation = r with { SelectedIds = r.SelectedIds.Append(choice.Cards.Single()).ToArray() } }); PublishPublicPilePreparation(f); return; }
        Card[] cards; CardLocation to; int branch = 0, beneficiary = f.OwnerSeat;
        if (r.Operation == SkillProgramEffectOp.StoreNonBasicOwnedPublicPile)
        {
            if (r.Pile is not { } own || !PreparationPileCurrent(own)) { FinishPublicPilePreparation(f, false); return; }
            cards = r.SelectedIds.Select(id => PreparationStoreCards(f).Single(c => c.Id == id)).ToArray(); to = own.Location;
        }
        else
        {
            cards = r.Pile is { } pile && PreparationPileCurrent(pile) ? PublicPileCards(pile).ToArray() : [];
            if (r.Operation == SkillProgramEffectOp.RemovePublicPileAfterAttackDamage) cards = cards.Where(c => c.Id == choice.Cards.Single()).ToArray();
            else { branch = choice.Targets.Count == 0 ? 1 : 2; beneficiary = branch == 1 ? f.OwnerSeat : choice.Targets.Single(); }
            to = branch == 2 ? CardLocation.Hand(beneficiary) : CardLocation.DiscardPile;
        }
        var ids = cards.Select(c => c.Id).ToArray(); var from = ids.Select(_cardZones.GetLocation).ToArray(); var before = PreparationSequence;
        ReplaceRuntimeTop(f = f with { PublicPilePreparation = r with { Stage = PublicPilePreparationStage.PaymentChildren,
            Branch = branch, BeneficiarySeat = beneficiary, SelectedIds = [], PaidIds = ids, PaidFrom = from, FrozenCount = ids.Length, Before = before, After = before },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (ids.Length > 0) MoveProgramCardsFromMultipleSources(ids, to, new(PreparationReason(f, "payment")));
        f = GetActiveProgramFrame(f.Id); r = f.PublicPilePreparation! with { After = PreparationSequence };
        ReplaceRuntimeTop(f = f with { PublicPilePreparation = r });
        AdvanceEventRulesAndQueueFact(new PublicPilePreparationPaidEvent(f.Id, branch, beneficiary, r.Pile, r.PaidIds, r.PaidFrom, before, r.After));
        AdvanceRuntimeProgram(f.Id);
    }
    private bool DrainPublicPilePreparation(ProgramSkillFrame f, bool movement) =>
        TryBeginQueuedRecoveryReplacement(f.Id, movement ? PostEventContinuation.AwaitedProgramMovement : PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, movement ? PostEventContinuation.AwaitedProgramMovement : PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private bool ResumePublicPilePreparation(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.PublicPilePreparation is not { } r) return false;
        AssertPublicPilePreparation(f); if (PreparationGameEnded()) return true;
        if (r.Stage == PublicPilePreparationStage.Choosing) { if (_pendingDecision is null) PublishPublicPilePreparation(f); return true; }
        if (DrainPublicPilePreparation(f, f.PendingMovementContinuation is not null)) return true;
        f = GetActiveProgramFrame(id); r = f.PublicPilePreparation!;
        if (f.PendingMovementContinuation is not null) ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (r.Operation != SkillProgramEffectOp.ResolvePreparationPublicPile || !_players[r.BeneficiarySeat].IsAlive || r.Stage == PublicPilePreparationStage.DrawChildren)
        { FinishPublicPilePreparation(f, true); return true; }
        if (r.Stage == PublicPilePreparationStage.PaymentChildren && r.Branch == 2)
        {
            ReplaceRuntimeTop(f = f with { PublicPilePreparation = r with { Stage = PublicPilePreparationStage.RecoveryChildren, RecoveryIssued = true } });
            AdvanceEventRulesAndQueueFact(new PublicPilePreparationRecoveryIssuedEvent(id, r.BeneficiarySeat, 1));
            new ProgramSkillHost(this).Recover(id, f.OwnerSeat, r.BeneficiarySeat, 1, null, null);
            AdvanceRuntimeProgram(id); return true;
        }
        var requested = r.Branch == 2 ? r.FrozenCount : Math.Max(0, _players[r.BeneficiarySeat].MaxHp - GetHand(_players[r.BeneficiarySeat]).Count);
        var before = PreparationSequence;
        ReplaceRuntimeTop(f = f with { PublicPilePreparation = r with { Stage = PublicPilePreparationStage.DrawChildren,
            DrawRequested = requested, DrawBefore = before, DrawAfter = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        var actual = DrawCards(_players[r.BeneficiarySeat], requested, true, new(PreparationReason(f, "draw"))).Count;
        f = GetActiveProgramFrame(id); r = f.PublicPilePreparation! with { DrawActual = actual, DrawAfter = PreparationSequence };
        ReplaceRuntimeTop(f = f with { PublicPilePreparation = r });
        AdvanceEventRulesAndQueueFact(new PublicPilePreparationDrawIssuedEvent(id, r.BeneficiarySeat, requested, actual, before, r.DrawAfter));
        AdvanceRuntimeProgram(id); return true;
    }
    private void FinishPublicPilePreparation(ProgramSkillFrame f, bool completed)
    { ReplaceRuntimeTop(f = f with { PublicPilePreparation = null, PendingMovementContinuation = null }); FinishProgramSkill(f, completed); }
    private bool ReturnPublicPilePreparationMovement(ProgramSkillFrame f)
    { if (f.PublicPilePreparation is null || f.PendingMovementContinuation is null) return false; AssertPublicPilePreparation(f); AdvanceRuntimeProgram(f.Id); return true; }
    private PromptChoice SelectAiPublicPilePreparation(PendingDecision p, ProgramSkillFrame f)
    {
        var r = f.PublicPilePreparation!;
        if (r.Operation == SkillProgramEffectOp.StoreNonBasicOwnedPublicPile)
            return p.Choices.Where(c => c.Cards.Count == 1).OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[f.OwnerSeat])).FirstOrDefault() ?? p.Choices.Single();
        if (r.Operation == SkillProgramEffectOp.RemovePublicPileAfterAttackDamage)
            return p.Choices.OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[f.OwnerSeat])).First();
        var fill = p.Choices.Single(c => c.Targets.Count == 0);
        var gifts = p.Choices.Where(c => c.Targets.Count == 1).ToArray();
        var count = r.Pile is { } pile ? PublicPileCards(pile).Count : 0;
        var view = CreateSnapshot(f.OwnerSeat); var brain = _aiBrains[f.OwnerSeat];
        var fillValue = brain.ScoreProgramTarget(view, f.OwnerSeat, new(0, 0, 0,
            Math.Max(0, _players[f.OwnerSeat].MaxHp - GetHand(_players[f.OwnerSeat]).Count), 0, 0, false, false));
        var bestGift = gifts.Select(c => (Choice: c, Value: brain.ScoreProgramTarget(view, c.Targets.Single(),
            new SkillProgramAiHint(0, 0, 0, count > int.MaxValue / 2 ? int.MaxValue : count * 2, 1, 0, false, false))))
            .OrderByDescending(item => item.Value).ThenBy(item => item.Choice.Targets.Single()).FirstOrDefault();
        return bestGift.Choice is not null && bestGift.Value > fillValue ? bestGift.Choice : fill;
    }
    private sealed partial class ProgramSkillHost : IPublicPilePreparationHost
    { public SkillProgramStepOutcome ExecutePublicPilePreparation(SkillProgramEffect e, ProgramSkillFrame f) => engine.BeginPublicPilePreparation(e, f); }
}
