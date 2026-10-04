namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IGrantedEntityPhaseProgramHost
    {
        public SkillProgramStepOutcome InsertGrantedEntityPlayPhase(ProgramSkillFrame f) => engine.BeginGrantedEntityPlayPhase(f);
        public SkillProgramStepOutcome ClaimGrantedPhaseSlash(ProgramSkillFrame f) => engine.BeginGrantedPhaseSlashClaim(f);
    }
    private SkillProgramStepOutcome BeginGrantedEntityPlayPhase(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.IssuedEntityPhase is not null || f.InstructionIndex != 1)
            throw new InvalidOperationException("An inserted entity phase cannot be issued twice.");
        f = f with { IssuedEntityPhase = new(new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId),
            _turnNumber, 0, GrantedEntityPhaseStage.Scheduled) };
        ReplaceRuntimeTop(f);
        return ScheduleProgramPhase(f, TurnPhase.Play, SkillProgramPhaseContinuation.BeforeNormalPreparation);
    }
    private void IssueGrantedEntityPhaseBoundary(CharacterState actor)
    {
        if (_programPhaseSchedule is not { Phase: TurnPhase.Play } schedule ||
            schedule.Frame.IssuedEntityPhase is not { Stage: GrantedEntityPhaseStage.Scheduled } receipt) return;
        if (actor.Seat != schedule.Frame.OwnerSeat || actor.Seat != _currentSeat || receipt.TurnNumber != _turnNumber)
            throw new InvalidOperationException("The granted entity phase lost its actual inserted actor/turn.");
        receipt = receipt with { Stage = GrantedEntityPhaseStage.Active, PhaseInstanceId = _cardUseDebitPhaseInstanceId };
        _programPhaseSchedule = schedule with { Frame = schedule.Frame with { IssuedEntityPhase = receipt } };
        AdvanceEventRulesAndQueueFact(new GrantedEntityPhaseStartedEvent(schedule.Frame.Id, receipt.Source,
            receipt.TurnNumber, receipt.PhaseInstanceId));
    }
    private bool IsGrantedEntityPhaseActive(int owner) => _programPhaseSchedule is { Phase: TurnPhase.Play } schedule &&
        schedule.Frame.OwnerSeat == owner && schedule.Frame.IssuedEntityPhase is { Stage: GrantedEntityPhaseStage.Active } receipt &&
        _phase == TurnPhase.Play && _currentSeat == owner && receipt.TurnNumber == _turnNumber && receipt.PhaseInstanceId == _cardUseDebitPhaseInstanceId;
    private bool CanOfferGrantedPhaseSlash(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ClaimGrantedPhaseSlash)) return true;
        return IsGrantedEntityPhaseActive(candidate.OwnerSeat) && _programPhaseSchedule!.Frame.IssuedEntityPhase is { CardId: null } receipt &&
            receipt.Source.SkillId == candidate.SkillId && receipt.Source.SkillInstanceId == candidate.SkillInstanceId;
    }
    private SkillProgramStepOutcome BeginGrantedPhaseSlashClaim(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.GrantedPhaseSlashClaim is not null || f.WindowContext?.Window != SkillProgramTriggerWindow.PlayPhaseStarting ||
            !IsGrantedEntityPhaseActive(f.OwnerSeat) || _programPhaseSchedule!.Frame.IssuedEntityPhase is not { } phase ||
            phase.Source.SkillId != f.SkillId || phase.Source.SkillInstanceId != f.SkillInstanceId)
            throw new InvalidOperationException("A phase Slash choice requires its exact newly issued extra phase.");
        ReplaceRuntimeTop(f = f with { GrantedPhaseSlashClaim = new(f.InstructionIndex, _programPhaseSchedule.Frame.Id,
            phase.PhaseInstanceId, false) });
        PublishGrantedPhaseSlash(f);
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> GrantedPhaseSlashChoices(ProgramSkillFrame f)
    {
        var choices = new List<PromptChoice>();
        PromptChoice Choice(string option, string text, IReadOnlyList<int>? ids = null) => new(
            new($"granted-phase-slash.{f.Id}.{option}"), text, ids ?? [], [],
            new Dictionary<string,string> { ["program-action"] = "granted-phase-slash", ["frame-id"] = f.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), ["option"] = option });
        // The deck offers only a source, never its unrevealed identity set.
        if (_cardZones.CardsAt(CardLocation.DrawPile).Any(c => IsSlashCard(c.Kind)))
            choices.Add(Choice("deck", "从牌堆获得一张杀"));
        foreach (var card in _cardZones.CardsAt(CardLocation.DiscardPile).Where(c => IsSlashCard(c.Kind)))
            choices.Add(Choice("discard-" + card.Id, $"获得弃牌堆的【{card.DisplayName}】（{GetSuitDisplayName(card.Suit)} {card.RankText}）", [card.Id]));
        choices.Add(Choice("skip", "不获得杀"));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishGrantedPhaseSlash(ProgramSkillFrame f)
    {
        var choices = GrantedPhaseSlashChoices(f);
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "可以从牌堆或弃牌堆获得一张杀。本额外阶段未造成伤害，结束时仍对自己造成1点伤害。",
            choices.SelectMany(c => c.Cards).ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private void ResolveGrantedPhaseSlashChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("A phase claim lost its current program.");
        var receipt = f.GrantedPhaseSlashClaim ?? throw new InvalidOperationException("A phase claim lost its unpaid receipt.");
        if (receipt.Paid || choice.Parameters.GetValueOrDefault("frame-id") != f.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            !GrantedPhaseSlashChoices(f).Any(c => c.Id == choice.Id))
            throw new InvalidOperationException("A phase claim choice is not currently published.");
        ClearPendingDecision();
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || !IsGrantedEntityPhaseActive(f.OwnerSeat) ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) || choice.Parameters["option"] == "skip")
        {
            ReplaceRuntimeTop(f with { GrantedPhaseSlashClaim = null }); AdvanceRuntimeProgram(f.Id); return;
        }
        var from = choice.Parameters["option"] == "deck" ? CardLocation.DrawPile : CardLocation.DiscardPile;
        var card = from == CardLocation.DrawPile ? _cardZones.CardsAt(from).Reverse().FirstOrDefault(c => IsSlashCard(c.Kind)) :
            choice.Cards is [var id] ? _cardZones.CardsAt(from).SingleOrDefault(c => c.Id == id && IsSlashCard(c.Kind)) : null;
        if (card is null) { ReplaceRuntimeTop(f with { GrantedPhaseSlashClaim = null }); AdvanceRuntimeProgram(f.Id); return; }
        ReplaceRuntimeTop(f with { GrantedPhaseSlashClaim = receipt with { Paid = true, CardId = card.Id } });
        MoveCard(card, from, CardLocation.Hand(f.OwnerSeat), new("program.granted-phase-slash.claim"), beforeFact: moved =>
        {
            var active = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(active with { GrantedPhaseSlashClaim = active.GrantedPhaseSlashClaim! with { ClaimMovementSequence = moved.Sequence } });
            var schedule = _programPhaseSchedule!;
            _programPhaseSchedule = schedule with { Frame = schedule.Frame with { IssuedEntityPhase = schedule.Frame.IssuedEntityPhase! with
                { CardId = card.Id, ClaimMovementSequence = moved.Sequence } } };
            AdvanceEventRulesAndQueueFact(new GrantedPhaseSlashClaimedEvent(schedule.Frame.Id, f.Id,
                schedule.Frame.IssuedEntityPhase!.Source, receipt.PhaseInstanceId, card.Id, moved.Sequence));
        });
        AdvanceRuntimeProgram(f.Id);
    }
    private bool ResumeGrantedPhaseSlashClaim(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != frameId || f.GrantedPhaseSlashClaim is not { } receipt) return false;
        if (!receipt.Paid) return true;
        if (TryBeginQueuedRecoveryReplacement(frameId, PostEventContinuation.Program) ||
            TryBeginCharacterStateProgramWindow(frameId, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(frameId, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(frameId)) return true;
        ReplaceRuntimeTop(f with { GrantedPhaseSlashClaim = null }); return false;
    }
    private ProgramSkillFrame FreezeGrantedEntityPhaseEnd(ProgramSkillFrame f)
    {
        if (f.IssuedEntityPhase is not { Stage: GrantedEntityPhaseStage.Active } receipt) return f;
        if (_phase != TurnPhase.Play || receipt.PhaseInstanceId != _cardUseDebitPhaseInstanceId || receipt.TurnNumber != _turnNumber || f.OwnerSeat != _currentSeat)
            throw new InvalidOperationException("A granted extra phase cannot end through another phase's counter.");
        var dealt = _playPhaseDamageDealtByCurrentPlayer;
        receipt = receipt with { Stage = GrantedEntityPhaseStage.Returning, DamageDealt = dealt };
        AdvanceEventRulesAndQueueFact(new GrantedEntityPhaseEndedEvent(f.Id, receipt.Source, receipt.TurnNumber, receipt.PhaseInstanceId, dealt));
        return f with { IssuedEntityPhase = receipt };
    }
    private bool IsReturningGrantedEntityPhase(ProgramSkillFrame f) => f.IssuedEntityPhase is { Stage: GrantedEntityPhaseStage.Returning } r &&
        r.DamageDealt is not null && CompleteProgramEventHistory().OfType<GrantedEntityPhaseEndedEvent>().Any(e =>
            e.FrameId == f.Id && e.Source == r.Source && e.TurnNumber == r.TurnNumber && e.PhaseInstanceId == r.PhaseInstanceId && e.DamageDealt == r.DamageDealt);
    private bool ResumeGrantedEntityPhaseTrailer(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != frameId || f.IssuedEntityPhase is not { } receipt ||
            receipt.Stage is not (GrantedEntityPhaseStage.Returning or GrantedEntityPhaseStage.DamagePaid)) return false;
        if (receipt.Stage == GrantedEntityPhaseStage.Returning)
        {
            if (!IsReturningGrantedEntityPhase(f)) throw new InvalidOperationException("The exact completed extra phase lost its issued trailer.");
            ReplaceRuntimeTop(f = f with { IssuedEntityPhase = receipt with { Stage = GrantedEntityPhaseStage.DamagePaid } });
            if (receipt.DamageDealt == 0 && _winner == Winner.None && _players[f.OwnerSeat].IsAlive)
            { BeginProgramSkillDamage(f, f.OwnerSeat, 1); return true; }
        }
        if (f.AttackAttempt is not null || new ProgramSkillHost(this).TryStartPostInstructionWindow(f.Id)) return true;
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { IssuedEntityPhase = GetActiveProgramFrame(f.Id).IssuedEntityPhase! with { Stage = GrantedEntityPhaseStage.Complete } });
        FinishProgramSkill(f, completed: _players[f.OwnerSeat].IsAlive); return true;
    }
    private bool HasGrantedPhaseEntityDistance(CharacterState owner, IReadOnlyList<int>? ids)
    {
        if (ids is not [var id] || !IsGrantedEntityPhaseActive(owner.Seat) || _programPhaseSchedule!.Frame.IssuedEntityPhase is not { CardId: { } claimed, ClaimMovementSequence: { } sequence } receipt || claimed != id)
            return false;
        if (!_cardMovements.Any(m => m.Sequence == sequence && m.CardId == id && m.To == CardLocation.Hand(owner.Seat))) return false;
        var outside = _cardMovements.Where(m => m.CardId == id && m.Sequence > sequence && !IsProvenanceOwnedZone(m.To, owner.Seat)).ToArray();
        if (IsProvenanceOwnedZone(_cardZones.GetLocation(id), owner.Seat)) return outside.Length == 0;
        // An already-paid successful declaration retains its exact original Hand
        // material; no unrelated Processing entity can revive the phase grant.
        if (UnclaimedDeclarationPayment(owner.Seat, id) is not { } payment || payment.ActorSeat != owner.Seat ||
            payment.Source.OwnerSeat != owner.Seat || payment.Cost.From != CardLocation.Hand(owner.Seat) ||
            outside is not [var entry] || entry.From != payment.Cost.From || entry.To != CardLocation.Processing ||
            entry.Reason.Value != "conversion.declaration.pay" || entry.CardKind != payment.Cost.CardKind ||
            _cardMovements.Last(m => m.CardId == id).Sequence != entry.Sequence ||
            !CompleteProgramEventHistory().OfType<CardDeclarationCommittedEvent>().Any(e => e.DeclarationId == payment.DeclarationId &&
                e.OwnerSeat == owner.Seat && e.ActorSeat == owner.Seat && e.DeclaredKind == payment.DeclaredKind)) return false;
        return _resolutionStack.Any(frame => frame.Id == payment.OwnerFrameId && (frame.AcceptedDeclarationPayment == payment ||
            frame is CardDeclarationFrame { Stage: CardDeclarationStage.Applying, Succeeded: true } declaration &&
            declaration.Id == payment.DeclarationId && declaration.Payment == payment));
    }
    private void IssueGrantedPhaseEntityUseDistance(long frameId, CardActionContext? action)
    {
        if (action is not { Type: CardActionType.Use } || action.ActorSeat != action.ProviderSeat || !IsSlashCard(action.EffectiveKind) ||
            !HasGrantedPhaseEntityDistance(_players[action.ActorSeat], action.PhysicalCards.Select(c => c.CardId).ToArray())) return;
        var schedule = _programPhaseSchedule!; var receipt = schedule.Frame.IssuedEntityPhase!;
        var policy = new GrantedEntityDistanceUseIssued(frameId, action.ActionId, schedule.Frame.Id, receipt.Source, receipt.PhaseInstanceId, receipt.CardId!.Value);
        UpdateLifecycleCardUse(frameId, f => f with { GrantedEntityDistance = policy });
        AdvanceEventRulesAndQueueFact(new GrantedEntityDistanceUseIssuedEvent(policy));
    }
    private bool HasIssuedGrantedPhaseEntityDistance(long? useId, int actor) => useId is { } id &&
        LifecycleCardUse(id) is { GrantedEntityDistance: { } policy, Action: { } action } && policy.CardUseFrameId == id && action.ActorSeat == actor &&
        action.ProviderSeat == actor && policy.Source.OwnerSeat == actor && action.ActionId == policy.CardActionId &&
        action.PhysicalCards is [var cost] && cost.CardId == policy.CardId && IsSlashCard(action.EffectiveKind);
    private bool HasPotentialGrantedPhaseSlash(CharacterState owner) => IsGrantedEntityPhaseActive(owner.Seat) &&
        GetSlashUseCards(owner).Any(card => HasGrantedPhaseEntityDistance(owner, [card.Id]));
    private long? GrantedEntityTrailerHealthOwner(IGameEvent payload)
    {
        if (payload is not DamageAppliedEvent applied || _resolutionStack.Count < 2 ||
            _resolutionStack[^1] is not DamageFrame damage ||
            _resolutionStack[^2] is not ProgramSkillFrame
                { IssuedEntityPhase: { Stage: GrantedEntityPhaseStage.DamagePaid, DamageDealt: 0 } phase,
                  AttackAttempt: { DamageWasApplied: true } attack } owner || damage.ParentFrameId != owner.Id ||
            phase.Source.OwnerSeat != owner.OwnerSeat || attack.SourceSeat != owner.OwnerSeat || attack.SourceLess ||
            applied.SourceLess || damage.SourceSeat != applied.SourceSeat || damage.TargetSeat != applied.TargetSeat ||
            damage.Amount != applied.Amount || damage.Nature != applied.Nature ||
            attack.SourceSeat != applied.SourceSeat || attack.TargetSeat != applied.TargetSeat)
            return null;
        AssertGrantedEntityPhase(owner);
        return owner.Id;
    }
    private PromptChoice SelectAiGrantedPhaseSlash(PendingDecision decision) =>
        decision.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("option") == "deck") ??
        decision.Choices.FirstOrDefault(c => c.Cards.Count == 1) ?? decision.Choices.Single(c => c.Parameters.GetValueOrDefault("option") == "skip");
    private void AssertGrantedEntityPhase(ProgramSkillFrame f)
    {
        if (f.IssuedEntityPhase is { } phase && (phase.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) ||
            f.WindowContext?.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || f.InstructionIndex != 1 ||
            ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).Instructions.Single().Op != SkillProgramEffectOp.InsertGrantedEntityPlayPhase ||
            phase.PhaseInstanceId < 0 || phase.CardId is not null && phase.ClaimMovementSequence is null))
            throw new InvalidOperationException("An issued entity phase lost its typed exact insertion.");
        if (f.IssuedEntityPhase is { } issued && issued.Stage != GrantedEntityPhaseStage.Scheduled &&
            (!CompleteProgramEventHistory().OfType<GrantedEntityPhaseStartedEvent>().Any(e => e.FrameId == f.Id &&
                e.Source == issued.Source && e.TurnNumber == issued.TurnNumber && e.PhaseInstanceId == issued.PhaseInstanceId) ||
             (issued.Stage is GrantedEntityPhaseStage.Returning or GrantedEntityPhaseStage.DamagePaid or GrantedEntityPhaseStage.Complete) &&
                (issued.DamageDealt is null or < 0 || !CompleteProgramEventHistory().OfType<GrantedEntityPhaseEndedEvent>().Any(e =>
                    e.FrameId == f.Id && e.Source == issued.Source && e.TurnNumber == issued.TurnNumber &&
                    e.PhaseInstanceId == issued.PhaseInstanceId && e.DamageDealt == issued.DamageDealt))))
            throw new InvalidOperationException("The granted entity phase lost its actual start/end boundary facts.");
        if (f.GrantedPhaseSlashClaim is { } claim && (f.InstructionIndex != claim.InstructionIndex ||
            ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).Instructions[f.InstructionIndex - 1].Op != SkillProgramEffectOp.ClaimGrantedPhaseSlash ||
            f.WindowContext?.Window != SkillProgramTriggerWindow.PlayPhaseStarting || claim.PhaseInstanceId != _cardUseDebitPhaseInstanceId ||
            !IsGrantedEntityPhaseActive(f.OwnerSeat) || _programPhaseSchedule!.Frame.Id != claim.PhaseProducerFrameId ||
            claim.Paid && (claim.CardId is null || claim.ClaimMovementSequence is null ||
                !_cardMovements.Any(m => m.Sequence == claim.ClaimMovementSequence && m.CardId == claim.CardId && m.To == CardLocation.Hand(f.OwnerSeat)))))
            throw new InvalidOperationException("A phase entity claim lost its one paid material/phase receipt.");
        if (f.GrantedPhaseSlashClaim is { Paid: false } && ReferenceEquals(f, _resolutionStack.LastOrDefault()))
        {
            var expected = GrantedPhaseSlashChoices(f);
            if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } prompt ||
                prompt.PlayerSeat != f.OwnerSeat || prompt.SkillPrompt?.SkillId != f.SkillId ||
                !prompt.ValidCardIds.SequenceEqual(expected.SelectMany(c => c.Cards)) ||
                prompt.Choices.Count != expected.Count || prompt.Choices.Where((choice, index) =>
                    choice.Id != expected[index].Id || !choice.Cards.SequenceEqual(expected[index].Cards) ||
                    choice.Targets.Count != 0 || choice.Parameters.GetValueOrDefault("program-action") != "granted-phase-slash" ||
                    choice.Parameters.GetValueOrDefault("frame-id") != f.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                    choice.Parameters.GetValueOrDefault("option") != expected[index].Parameters["option"]).Any())
                throw new InvalidOperationException("The unpaid granted phase source lost its exact private published choices.");
        }
    }
}
