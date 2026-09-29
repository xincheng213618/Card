namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string SimpleProgramPindianResultBind = "__active-pindian-result";

    // The journal rebuilds these counters through accepted commands. They are
    // keyed by content identities, never by translated presentation text.
    private readonly Dictionary<(int Seat, string Skill, string Activation), int> _programUses = new();
    private readonly Dictionary<(int Seat, string Skill, string Activation), int> _programPhaseUses = new();
    private readonly Dictionary<(int ProviderSeat, int SkillOwnerSeat, string Skill, string Contribution), int>
        _programContributionUses = new();

    // The match-local index supplies the owner's distinct enabled programs.
    private IReadOnlyList<SkillProgram> EnabledSkillPrograms(CharacterState player) =>
        GetSkillBindingShard(player).Programs;

    private IReadOnlyList<SkillProgram> EnabledActivationPrograms(CharacterState player) =>
        GetSkillBindingShard(player).ActivationPrograms;

    private IReadOnlyList<SkillProgram> EnabledContributionPrograms(CharacterState player) =>
        GetSkillBindingShard(player).ContributionPrograms;

    private IReadOnlyList<SkillProgram> EnabledViewAsPrograms(CharacterState player) =>
        GetSkillBindingShard(player).ViewAsPrograms;

    private IReadOnlyList<SkillProgram> EnabledCardIdentityPrograms(CharacterState player) =>
        GetSkillBindingShard(player).CardIdentityPrograms;

    private IReadOnlyList<SkillProgram> EnabledPassiveRulePrograms(CharacterState player) =>
        GetSkillBindingShard(player).PassiveRulePrograms;

    private IReadOnlyList<IndexedSkillProgramTrigger> EnabledUniqueProgramTriggers(
        CharacterState player,
        SkillProgramTriggerWindow window) =>
        GetSkillBindingShard(player).GetUniqueTriggers(window);

    private SkillProgram GetEnabledSkillProgram(CharacterState player, string skillId) =>
        GetSkillBindingShard(player).GetProgram(skillId) ??
        throw new InvalidOperationException(
            $"Player {player.Seat} does not own enabled skill program '{skillId}'.");

    private IEnumerable<LegalAction> BuildProgramActions(CharacterState owner)
    {
        if (!owner.IsAlive || owner.Seat != _currentSeat || _phase != TurnPhase.Play) yield break;
        var context = CreateSkillContext(owner);
        foreach (var program in EnabledActivationPrograms(owner))
            foreach (var activation in program.Activations)
            {
                if (!activation.Condition.Evaluate(context) ||
                    activation.UsesPerTurn is { } limit &&
                    _programUses.GetValueOrDefault((owner.Seat, program.Id, activation.UsageGroup)) >= limit ||
                    activation.UsesPerPhase is { } phaseLimit &&
                    _programPhaseUses.GetValueOrDefault((owner.Seat, program.Id, activation.UsageGroup)) >= phaseLimit ||
                    activation.UsesPerGame is { } gameLimit &&
                    _skillRuntimeState.GetUsage(
                        owner.Seat,
                        program.Id,
                        activation.UsageGroup,
                        SkillUsageScope.Game) >= gameLimit)
                    continue;
                if (activation.Effects.Any(effect => effect.Op == SkillProgramEffectOp.StartPindian) &&
                    GetHand(owner).Count == 0)
                    continue;
                if (activation.Effects.Any(effect => effect.Op == SkillProgramEffectOp.SelectTargets &&
                    effect.TargetKind is { } kind &&
                    GetProgramTargetSeats(owner.Seat, kind).Count < effect.MinimumTargets))
                    continue;
                if (activation.Effects.FirstOrDefault(effect => effect.Op == SkillProgramEffectOp.RequestFactionCard)
                        is { } factionRequest &&
                    !CanUseFactionSlashRequest(owner, factionRequest.ProviderFactionId!))
                    continue;
                if (activation.Effects.Any(effect => effect is
                    {
                        Op: SkillProgramEffectOp.SelectTargets,
                        TargetKind: SkillProgramTargetKind.OtherLivingUnequalHandPair
                    }) &&
                    !_players.Where(player => player.IsAlive && player.Seat != owner.Seat)
                        .Select(player => GetHand(player).Count).Distinct().Skip(1).Any())
                    continue;
                var selectedCardUse = activation.Effects.SingleOrDefault(effect =>
                    effect.Op == SkillProgramEffectOp.UseSelectedCardsAs);
                var allHandTrickUse = activation.Effects.SingleOrDefault(effect =>
                    effect.Op == SkillProgramEffectOp.UseAllHandCardsAsOrdinaryTrick);
                var multiCardUses = selectedCardUse is null
                    ? Array.Empty<ProgramMultiCardViewAsSelection>()
                    : GetProgramMultiCardViewAsSelections(
                        owner,
                        selectedCardUse.OutputKind!.Value,
                        forResponse: false)
                        .Where(item => item.Source.SkillId == program.Id &&
                                       item.Source.BindingId == selectedCardUse.SourceBind)
                        .ToArray();
                var cards = selectedCardUse is not null
                    ? multiCardUses.SelectMany(item => item.Cards).Select(card => card.Id).Distinct().Order().ToArray()
                    : allHandTrickUse is not null
                        ? GetHand(owner).Any(card => IsTurnHandCardRestricted(owner, card))
                            ? []
                            : GetHand(owner).Select(card => card.Id).Order().ToArray()
                    : activation.MaxCards == 0 ? [] : activation.SourceZones
                        .SelectMany(zone => _cardZones.CardsAt(new CardLocation(zone, owner.Seat)))
                        .Where(card => activation.EquipmentSlots.Count == 0 ||
                            EquipmentCatalog.IsEquipment(card.Kind) &&
                            activation.EquipmentSlots.Contains(EquipmentCatalog.Get(card.Kind).Slot))
                        .Select(card => card.Id).Distinct().Order().ToArray();
                var targets = selectedCardUse is { OutputKind: CardKind.ArrowBarrage } ? []
                    : selectedCardUse is not null
                    ? _players.Where(target => CanUseVirtualSlashTarget(owner, target))
                        .Select(target => target.Seat).Order().ToArray()
                    : allHandTrickUse is not null ? []
                    : activation.MaxTargets == 0 ? [] : _players
                    .Where(target => target.IsAlive &&
                        (!activation.Effects.Any(effect => effect.Op == SkillProgramEffectOp.RequestFactionCard) ||
                         CanUseProvidedSlashTarget(owner, target)) &&
                        (target.Seat != owner.Seat || !activation.Effects.Any(effect => effect.Op == SkillProgramEffectOp.GiveSelected)) &&
                        (activation.TargetKind is SkillProgramTargetKind.AnyLiving or SkillProgramTargetKind.AnyWounded ||
                         target.Seat != owner.Seat) &&
                        (activation.TargetKind switch
                        {
                            SkillProgramTargetKind.OtherLiving => target.Seat != owner.Seat,
                            SkillProgramTargetKind.OtherLivingMale =>
                                target.Seat != owner.Seat && target.Gender == GeneralGender.Male,
                            SkillProgramTargetKind.OtherWoundedMale =>
                                target.Seat != owner.Seat && target.Gender == GeneralGender.Male &&
                                target.Hp < target.MaxHp,
                            SkillProgramTargetKind.OtherLivingInAttackRange =>
                                target.Seat != owner.Seat &&
                                GetCombatDistance(owner.Seat, target.Seat) <= GetAttackRange(owner.Seat),
                            SkillProgramTargetKind.OtherLivingWhoseAttackRangeIncludesOwner =>
                                target.Seat != owner.Seat &&
                                GetCombatDistance(target.Seat, owner.Seat) <= GetAttackRange(target.Seat),
                            SkillProgramTargetKind.OtherLivingWithQinggangSword =>
                                target.Seat != owner.Seat &&
                                GetEquipment(target).Any(card => card.Kind == CardKind.QinggangSword),
                            SkillProgramTargetKind.OtherLivingSlashable =>
                                CanUseProvidedSlashTarget(owner, target),
                            SkillProgramTargetKind.OtherLivingWithHand =>
                                target.Seat != owner.Seat && GetHand(target).Count > 0,
                            SkillProgramTargetKind.OtherLivingWithHandHpGreaterThanOwner =>
                                                    target.Seat != owner.Seat && GetHand(target).Count > 0 && target.Hp > owner.Hp,
                            SkillProgramTargetKind.OtherLivingAtDistanceOne =>
                                                    target.Seat != owner.Seat && GetCombatDistance(owner.Seat, target.Seat) == 1,
                            SkillProgramTargetKind.AnyLiving => true,
                            SkillProgramTargetKind.OtherWounded =>
                                target.Seat != owner.Seat && target.Hp < target.MaxHp,
                            SkillProgramTargetKind.AnyWounded => target.Hp < target.MaxHp,
                            SkillProgramTargetKind.AnyLivingHandBelowMaxHp => GetHand(target).Count < target.MaxHp,
                            SkillProgramTargetKind.OtherLivingPair => target.Seat != owner.Seat &&
                                _players.Count(peer => peer.IsAlive && peer.Seat != owner.Seat) >= 2,
                            SkillProgramTargetKind.EventTarget => false,
                            _ => false
                        }) &&
                        (!activation.Effects.Any(effect => effect is
                             {
                                 Op: SkillProgramEffectOp.SelectAndMoveOwnedCard,
                                 ProhibitReplacingEquipment: true
                             }) ||
                         HasFreeEquipmentSlotForOwnedHandEquipment(owner, target)) &&
                        (!activation.Effects.Any(effect => effect.Op == SkillProgramEffectOp.StartPindian) ||
                         GetHand(target).Count > 0))
                    .Select(target => target.Seat).Order().ToArray();
                if (cards.Length < activation.MinCards || targets.Length < activation.MinTargets ||
                    activation.SelectedCardsSameSuit && !cards
                        .Select(id => _cardZones.CardsAt(_cardZones.GetLocation(id))
                            .Single(card => card.Id == id).Suit)
                        .GroupBy(suit => suit).Any(group => group.Count() >= activation.MinCards) ||
                    selectedCardUse is not null && multiCardUses.Length == 0 ||
                    selectedCardUse is { OutputKind: CardKind.ArrowBarrage } &&
                    !CanUseGlobalCard(owner, CardKind.ArrowBarrage) ||
                    allHandTrickUse is not null &&
                    (cards.Length == 0 || BuildProgramOrdinaryTrickUseOptions(owner).Count == 0)) continue;
                if (activation.Effects.FirstOrDefault() is
                    {
                        Op: SkillProgramEffectOp.SelectTarget,
                        TargetKind: { } dynamicKind
                    } dynamicSelection &&
                    !GetProgramTargetSeats(owner.Seat, dynamicKind, marker: dynamicSelection.Marker)
                        .Any(seat => IsProgramTargetEligible(owner.Seat, dynamicKind,
                            dynamicSelection.Zones, seat, dynamicSelection.Marker)))
                    continue;
                var minCardCount = allHandTrickUse is null ? activation.MinCards : cards.Length;
                var maxCardCount = allHandTrickUse is not null || activation.MaxCards == int.MaxValue
                    ? cards.Length : activation.MaxCards;
                yield return new LegalAction(LegalActionKind.UseProgramSkill, null, null,
                    $"发动【{_contentRegistry!.Skills[program.Id].Name}】",
                    MinCardCount: minCardCount, MaxCardCount: maxCardCount,
                    MinTargetCount: activation.MinTargets, MaxTargetCount: activation.MaxTargets)
                {
                    ProgramSkillId = program.Id,
                    ProgramActivationId = activation.Id,
                    SelectedCardsSameSuit = activation.SelectedCardsSameSuit,
                    ProgramAiHint = CreateProgramAiHint(program, activation, context, owner.IsFaceDown),
                    SelectableCardIds = Array.AsReadOnly(cards),
                    SelectableTargetSeats = Array.AsReadOnly(targets)
                };
            }
        foreach (var skillOwner in _players.Where(player => player.IsAlive && player.Seat != owner.Seat).OrderBy(player => player.Seat))
            foreach (var program in EnabledContributionPrograms(skillOwner))
                foreach (var contribution in program.Contributions)
                {
                    var providerFaction = GetEffectiveFactionId(owner);
                    var key = (owner.Seat, skillOwner.Seat, program.Id, contribution.Id);
                    if (providerFaction is null ||
                        !contribution.ProviderFactions.Contains(providerFaction, StringComparer.Ordinal) ||
                        skillOwner.Role != contribution.OwnerRole ||
                        _programContributionUses.GetValueOrDefault(key) >= contribution.UsesPerPlayPhase)
                        continue;
                    var cards = GetHand(owner)
                        .Where(card => contribution.CardKinds.Contains(card.Kind) || contribution.CardSuits.Contains(card.Suit))
                        .Select(card => card.Id).Order().ToArray();
                    if (cards.Length == 0) continue;
                    yield return new LegalAction(LegalActionKind.UseProgramSkill, null, skillOwner.Seat,
                        $"响应【{_contentRegistry!.Skills[program.Id].Name}】，将一张牌交给 {skillOwner.Name}",
                        MinCardCount: 1, MaxCardCount: 1, MinTargetCount: 1, MaxTargetCount: 1)
                    {
                        ProgramSkillId = program.Id,
                        ProgramActivationId = contribution.Id,
                        ProgramSkillOwnerSeat = skillOwner.Seat,
                        ProgramAiHint = new SkillProgramAiHint(0, 0, 0, 0, 0, 0, true, false),
                        SelectableCardIds = Array.AsReadOnly(cards),
                        SelectableTargetSeats = Array.AsReadOnly(new[] { skillOwner.Seat })
                    };
                }
    }

    private bool HasFreeEquipmentSlotForOwnedHandEquipment(CharacterState owner, CharacterState target)
    {
        var occupiedSlots = GetEquipment(target)
            .Select(item => EquipmentCatalog.Get(item.Kind).Slot).ToHashSet();
        return GetHand(owner).Any(card => EquipmentCatalog.IsEquipment(card.Kind) &&
            !occupiedSlots.Contains(EquipmentCatalog.Get(card.Kind).Slot));
    }

    private SkillProgramAiHint CreateProgramAiHint(
        SkillProgram program,
        SkillProgramActivation activation,
        PlayerSkillContext context,
        bool faceDown)
    {
        var owner = _players[context.Seat];
        var instanceId = GetRuntimeSkillInstanceId(owner, program.Id);
        return ProgramCompositionAi.Estimate(activation.Effects, context, faceDown,
            CreateProgramAiPublicContext(owner) with
            {
                ActivationCardCount = activation.MinCards,
                EligibleTargetCount = activation.Effects.FirstOrDefault(effect =>
                    effect.Op == SkillProgramEffectOp.SelectTargets) is { TargetKind: { } selectedKind }
                    ? GetProgramTargetSeats(owner.Seat, selectedKind).Count
                    : activation.Effects.Any(effect => effect is
                    {
                        Op: SkillProgramEffectOp.UseSelectedCardsAs,
                        OutputKind: CardKind.ArrowBarrage
                    })
                        ? _players.Count(player => player.IsAlive && player.Seat != owner.Seat)
                        : null,
                PhaseUsageCount = usageId => _skillRuntimeState.GetUsage(owner.Seat,
                    program.Id, usageId, SkillUsageScope.Phase),
                BooleanState = stateId => GetProgramBooleanState(owner.Seat, program.Id, instanceId, stateId)
            }).Hint;
    }

    private CommandError? ValidateProgramSelection(LegalAction action,
        IReadOnlyList<int> cards, IReadOnlyList<int> targets)
    {
        if (cards.Count < action.MinCardCount || cards.Count > action.MaxCardCount ||
            cards.Distinct().Count() != cards.Count || cards.Any(id => !action.SelectableCardIds.Contains(id)))
            return new CommandError(CommandErrorCode.InvalidCard, "The program's card selection is invalid.");
        if (targets.Count < action.MinTargetCount || targets.Count > action.MaxTargetCount ||
            targets.Distinct().Count() != targets.Count || targets.Any(seat => !action.SelectableTargetSeats.Contains(seat)))
            return new CommandError(CommandErrorCode.InvalidTarget, "The program's target selection is invalid.");
        if (action.SelectedCardsSameSuit && cards.Select(id =>
                _cardZones.CardsAt(_cardZones.GetLocation(id)).Single(card => card.Id == id).Suit)
                .Distinct().Skip(1).Any())
            return new CommandError(CommandErrorCode.InvalidCard,
                "The selected physical cards must share one printed suit.");
        return null;
    }

    private CommandResult SubmitUseProgramSkill(UseProgramSkillCommand command)
    {
        var error = ValidateHumanPrompt(command.ActorSeat, DecisionKind.PlayCard, command.PromptId,
            CommandErrorCode.IllegalAction);
        if (error is not null) return Reject(error.Code, error.Message);
        var owner = _players[command.ActorSeat];
        var candidates = BuildProgramActions(owner).Where(candidate =>
            candidate.ProgramSkillId == command.SkillId &&
            candidate.ProgramActivationId == command.ActivationId).ToArray();
        var action = command.SkillOwnerSeat is { } skillOwnerSeat
            ? candidates.SingleOrDefault(candidate => candidate.ProgramSkillOwnerSeat == skillOwnerSeat)
            : candidates.Length == 1
                ? candidates[0]
                : candidates.SingleOrDefault(candidate => candidate.ProgramSkillOwnerSeat is { } candidateOwner &&
                    command.TargetSeats.Count == 1 && command.TargetSeats[0] == candidateOwner);
        if (action is null)
            return Reject(CommandErrorCode.IllegalAction, "The skill activation is not currently available.");
        error = ValidateProgramSelection(action, command.CardIds, command.TargetSeats);
        if (error is not null) return Reject(error.Code, error.Message);
        return Accept(() =>
        {
            ClearPendingDecision();
            ExecuteProgramSkill(owner, action, command.CardIds, command.TargetSeats);
            PublishState();
            return _options.AdvanceAfterHumanCommands ? AdvanceToHumanBoundary() : BuildResult();
        });
    }

    private void ExecuteProgramSkill(CharacterState owner, LegalAction action,
        IReadOnlyList<int> cards, IReadOnlyList<int> targets)
    {
        var current = BuildProgramActions(owner).SingleOrDefault(candidate =>
            candidate.ProgramSkillId == action.ProgramSkillId &&
            candidate.ProgramActivationId == action.ProgramActivationId &&
            candidate.ProgramSkillOwnerSeat == action.ProgramSkillOwnerSeat)
            ?? throw new InvalidOperationException("The skill activation is no longer available.");
        if (ValidateProgramSelection(current, cards, targets) is { } error)
            throw new InvalidOperationException(error.Message);
        if (current.ProgramSkillOwnerSeat is { } skillOwnerSeat)
        {
            ExecuteProgramContribution(owner, _players[skillOwnerSeat], current, cards[0]);
            return;
        }
        var program = GetEnabledSkillProgram(owner, action.ProgramSkillId ??
            throw new InvalidOperationException("The program action has no skill identity."));
        var activation = program.Activations.Single(item => item.Id == action.ProgramActivationId);
        if (activation.UsesPerGame is { } gameLimit)
        {
            if (!_skillRuntimeState.TryConsumeUsage(
                    owner.Seat,
                    program.Id,
                    activation.UsageGroup,
                    SkillUsageScope.Game,
                    gameLimit))
                throw new InvalidOperationException("The skill activation already exhausted its game usage limit.");
            QueueGameEvent(new SkillUsageConsumedEvent(
                owner.Seat,
                program.Id,
                activation.UsageGroup,
                SkillUsageScope.Game,
                Count: 1));
        }
        var key = (owner.Seat, program.Id, activation.UsageGroup);
        _programUses[key] = _programUses.GetValueOrDefault(key) + 1;
        if (activation.UsesPerPhase is not null)
            _programPhaseUses[key] = _programPhaseUses.GetValueOrDefault(key) + 1;
        var frame = new ProgramSkillFrame(++_resolutionSequence, owner.Seat, program.Id, activation.Id,
            program.GameplayHash, 0, Array.AsReadOnly(cards.ToArray()), Array.AsReadOnly(targets.ToArray()))
        {
            SkillInstanceId = GetRuntimeSkillInstanceId(owner, program.Id)
        };
        _resolutionStack.Add(frame);
        QueueGameEvent(new ProgramSkillStartedEvent(frame.Id, owner.Seat, program.Id, activation.Id));
        AddLog("ActiveSkill", $"{owner.Name} 发动【{_contentRegistry!.Skills[program.Id].Name}】。", owner.Seat);
        ContinueProgramSkill(frame.Id);
    }

    private void ExecuteProgramContribution(CharacterState provider, CharacterState skillOwner,
        LegalAction action, int cardId)
    {
        var program = GetEnabledSkillProgram(skillOwner, action.ProgramSkillId ??
            throw new InvalidOperationException("The contribution action has no skill identity."));
        var contribution = program.Contributions.Single(item => item.Id == action.ProgramActivationId);
        var key = (provider.Seat, skillOwner.Seat, program.Id, contribution.Id);
        if (_programContributionUses.GetValueOrDefault(key) >= contribution.UsesPerPlayPhase)
            throw new InvalidOperationException("The contribution was already used in this play phase.");
        var card = GetHand(provider).Single(candidate => candidate.Id == cardId);
        if (!contribution.CardKinds.Contains(card.Kind) && !contribution.CardSuits.Contains(card.Suit))
            throw new InvalidOperationException("The contributed physical hand card no longer matches the binding.");

        _programContributionUses[key] = _programContributionUses.GetValueOrDefault(key) + 1;
        var actionId = ++_resolutionSequence;
        var reason = new CardMoveReason($"skill-program.{program.Id}.{contribution.Id}.contribute");
        MoveCard(card, CardLocation.Hand(provider.Seat), CardLocation.Processing, reason);
        MoveCard(card, CardLocation.Processing, CardLocation.Hand(skillOwner.Seat), reason);
        QueueGameEvent(new ProgramSkillContributionResolvedEvent(actionId, provider.Seat, skillOwner.Seat,
            program.Id, contribution.Id, card.Id, card.Kind, card.Suit));
        AddLog("ActiveSkill",
            $"{provider.Name} 响应【{_contentRegistry!.Skills[program.Id].Name}】，将【{card.DisplayName}】交给 {skillOwner.Name}。",
            provider.Seat, skillOwner.Seat);
    }

    private void ResetProgramContributionUsesForPlayPhase(int providerSeat)
    {
        foreach (var key in _programContributionUses.Keys
                     .Where(key => key.ProviderSeat == providerSeat).ToArray())
            _programContributionUses.Remove(key);
    }

    private void ContinueProgramSkill(long frameId)
    {
        var host = new ProgramSkillHost(this);
        new SkillProgramExecutor().Run(frameId, host, host);
    }

    private void FinishProgramSkill(ProgramSkillFrame frame, bool completed)
    {
        CleanupProgramBoundCards(frame, completed);
        if (frame.TriggerId is not null)
        {
            CompleteProgramBinding(frame, completed);
            if (_winner != Winner.None && _status != EngineStatus.Completed) CompleteGame();
            return;
        }
        QueueGameEvent(new ProgramSkillResolvedEvent(frame.Id, frame.OwnerSeat, frame.SkillId, frame.ActivationId, completed));
        PopResolutionFrame(frame.Id, ResolutionFrameKind.ProgramSkill);
        if (_winner != Winner.None && _status != EngineStatus.Completed) CompleteGame();
    }

    private void BeginProgramSkillDying(long parentFrameId, CharacterState victim)
    {
        if (_pendingDying is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != parentFrameId)
            throw new InvalidOperationException("A program dying continuation requires its program frame.");
        var responders = Array.AsReadOnly(BuildDyingResponderSeats(victim.Seat).ToArray());
        var id = ++_resolutionSequence;
        _resolutionStack.Add(new DyingFrame(id, parentFrameId, victim.Seat, null, responders, 0));
        _pendingDying = new DyingResolution(id, null, null, victim.Seat, null, responders, parentFrameId,
            DyingContinuation.ProgramSkill);
        QueueGameEvent(new PlayerDyingEvent(id, victim.Seat, null));
        _status = EngineStatus.Running;
        if (!TryBeginMandatorySelfDyingProgram(_pendingDying!)) ExposeHumanDyingPrompt();
    }

    private SkillProgramStepOutcome BeginProgramSkillDamage(
        ProgramSkillFrame frame,
        int targetSeat,
        int amount,
        ProgramParticipantReference? sourceReference = null,
        DamageNature? nature = null)
    {
        var judgmentNested = frame.WindowContext?.Judgment is { } frozenJudgment &&
            _pendingJudgment is { } pendingJudgment &&
            pendingJudgment.FrameId == frozenJudgment.JudgmentFrameId &&
            ReferenceEquals(_pendingAttack, pendingJudgment.Attack);
        var damageWindowNested = frame.WindowContext?.Window ==
            SkillProgramTriggerWindow.AfterDamageApplied && _pendingDamageTrigger is not null;
        if (_pendingAttack is not null && !judgmentNested && !damageWindowNested || _pendingDying is not null ||
            _resolutionStack.LastOrDefault() is not ProgramSkillFrame current || current.Id != frame.Id ||
            amount <= 0 || !IsValidPlayerSeat(targetSeat) || !_players[targetSeat].IsAlive)
            throw new InvalidOperationException("A program damage effect requires one active program and living target.");
        var attack = new AttackResolution(
            frame.Id,
            sourceReference is { } reference ? ResolveProgramParticipant(frame, reference) : frame.OwnerSeat,
            targetSeat,
            card: null,
            damageAmount: amount,
            damageNatureOverride: nature ?? DamageNature.Normal,
            programJudgmentFrameId: frame.WindowContext?.Window == SkillProgramTriggerWindow.JudgmentFinalized
                ? frame.WindowContext.ParentFrameId : null,
            programSkillFrameId: frame.Id);
        if (frame.WindowContext?.Judgment is { } judgment)
        {
            QueueGameEvent(new ProgramJudgmentDamageRequestedEvent(
                frame.WindowContext.ParentFrameId,
                judgment.JudgmentFrameId,
                frame.SkillId,
                frame.TriggerId!,
                attack.SourceSeat,
                targetSeat,
                amount,
                nature ?? DamageNature.Normal));
            AddLog("SkillTriggered",
                $"{_players[attack.SourceSeat].Name} 的【{_contentRegistry!.Skills[frame.SkillId].Name}】将对 {_players[targetSeat].Name} 造成 {amount} 点{GetDamageNatureLabel(nature ?? DamageNature.Normal)}伤害。",
                attack.SourceSeat, targetSeat);
        }
        if (damageWindowNested && !TrySuspendProgramDamageParent(frame, attack))
            throw new InvalidOperationException("A nested program damage lost its parent damage window.");
        _pendingAttack = attack;
        if (!ApplyAttackDamage(attack)) CompleteAttack(attack);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private SkillProgramStepOutcome BeginProgramSkillPindian(
        ProgramSkillFrame frame,
        int targetSeat)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame current || current.Id != frame.Id ||
            frame.PindianResultBindings.Any(item => item.Name == SimpleProgramPindianResultBind) ||
            frame.SelectedCardIds.Count != 1 ||
            frame.SelectedTargetSeats.Count != 1 || frame.SelectedTargetSeats[0] != targetSeat ||
            !IsValidPlayerSeat(targetSeat) || targetSeat == frame.OwnerSeat ||
            !_players[targetSeat].IsAlive || GetHand(_players[targetSeat]).Count == 0 ||
            !GetHand(_players[frame.OwnerSeat]).Any(card => card.Id == frame.SelectedCardIds[0]))
        {
            throw new InvalidOperationException(
                "A program Pindian requires one current owner hand card and one living other target with hand cards.");
        }
        var definition = _contentRegistry!.Skills[frame.SkillId];
        BeginSharedPindian(
            frame.Id,
            new(frame.SkillId, definition.Name, definition.Name, "选择拼点牌"),
            frame.OwnerSeat,
            targetSeat,
            frame.SelectedCardIds[0],
            programResultBind: SimpleProgramPindianResultBind,
            programResultVisibility: SkillProgramCardSetVisibility.Public);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private SkillProgramStepOutcome UseProgramSelectedCardsAs(
        ProgramSkillFrame frame,
        int targetSeat,
        string viewAsId,
        CardKind outputKind)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.TriggerId is not null || active.OwnerSeat != frame.OwnerSeat ||
            active.SkillId != frame.SkillId ||
            outputKind is not (CardKind.Slash or CardKind.ArrowBarrage) ||
            _resolutionStack.LastOrDefault() is not ProgramSkillFrame current || current.Id != frame.Id)
            throw new InvalidOperationException(
                "A selected-card use requires the current active program activation.");
        var owner = _players[frame.OwnerSeat];
        var source = new CardConversionSource(
            frame.SkillId,
            viewAsId,
            owner.Seat,
            frame.SkillInstanceId);
        var selection = FindProgramMultiCardViewAsSelection(
            owner,
            frame.SelectedCardIds,
            outputKind,
            forResponse: false,
            source) ?? throw new InvalidOperationException(
            "The selected physical cards no longer satisfy the configured view-as rule.");
        if (outputKind == CardKind.ArrowBarrage)
            return UseProgramSelectedCardsAsGlobal(frame, selection);
        var target = _players.SingleOrDefault(player => player.Seat == targetSeat) ??
            throw new InvalidOperationException("The selected card-use target no longer exists.");
        if (!CanUseVirtualSlashTarget(owner, target))
            throw new InvalidOperationException("The selected Slash target is no longer legal.");
        ResolveSlashCore(
            owner,
            target,
            selection.Cards[0],
            outputKind,
            owner.Seat,
            physicalCards: selection.Cards,
            conversionSource: selection.Source);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private void PendProgramExtraTurn(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.OwnerSeat != frame.OwnerSeat || active.SkillId != frame.SkillId)
            throw new InvalidOperationException("An extra turn requires the active program frame.");
        var owner = _players[active.OwnerSeat];
        if (!owner.IsAlive || _winner != Winner.None)
            return;
        _pendingExtraTurnSeat = active.OwnerSeat;
        AddLog("ExtraTurnPended",
            $"{owner.Name} 将在当前回合结束后获得一个额外回合。", active.OwnerSeat);
        QueueGameEvent(new ProgramExtraTurnPendedEvent(active.Id, active.SkillId, active.OwnerSeat));
    }

    private SkillProgramStepOutcome UseProgramBoundCardByTarget(
        ProgramSkillFrame frame,
        int userSeat,
        string sourceBind)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.OwnerSeat != frame.OwnerSeat || active.SkillId != frame.SkillId ||
            _resolutionStack.LastOrDefault() is not ProgramSkillFrame current || current.Id != frame.Id)
            throw new InvalidOperationException(
                "A bound-card use requires the current active program frame.");
        var user = _players[userSeat];
        var binding = frame.CardSetBindings.SingleOrDefault(item => item.Name == sourceBind) ??
            throw new InvalidOperationException("A bound-card use references a missing card set.");
        var cancel = (string message) =>
        {
            ClearPendingDecision();
            CancelProgramBindingAndCleanup(GetActiveProgramFrame(frame.Id), message);
        };
        if (binding.CardIds.Count != 1 || binding.Visibility != SkillProgramCardSetVisibility.Public)
            throw new InvalidOperationException(
                "A bound-card use requires exactly one public card.");
        if (!user.IsAlive)
        {
            cancel("用牌角色已失效，技能剩余结算已取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var cardId = binding.CardIds[0];
        var location = _cardZones.GetLocation(cardId);
        var card = _cardZones.CardsAt(location).SingleOrDefault(item => item.Id == cardId);
        if (location.OwnerSeat != user.Seat || location.Zone != CardZoneKind.Hand || card is null)
        {
            cancel("赠出的牌已离开用牌者手牌，使用步骤取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        if (!EquipmentCatalog.IsEquipment(card.Kind))
            throw new InvalidOperationException(
                "A bound-card use requires an equipment card.");
        ClearPendingDecision();
        _resolutionStack[^1] = frame with
        {
            PendingMovementContinuation = new ProgramMovementContinuation(frame.OwnerSeat, 0, null)
        };
        var reason = new CardMoveReason($"skill-program.{frame.SkillId}.{SkillProgramEffectOp.UseBoundCardByTarget}");
        var resolutionId = BeginCardUse(card, user.Seat, []);
        MoveCard(card, location, CardLocation.Processing, reason);
        CompleteEquipmentUse(user, card, resolutionId);
        if (_resolutionStack.LastOrDefault() is ProgramSkillFrame)
        {
            if (!TryBeginCardsMovedProgramWindow())
                CompleteAwaitedProgramMovement(frame.Id);
        }
        return SkillProgramStepOutcome.AwaitChild;
    }

    private void ContinueProgramAfterSelectedCardUse()
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.TriggerId is not null)
            return;
        var program = _contentRegistry.Skills.GetValueOrDefault(frame.SkillId)?.Program;
        var activation = program?.Activations.SingleOrDefault(item => item.Id == frame.ActivationId);
        if (activation is null || frame.InstructionIndex == 0 ||
            activation.Effects[frame.InstructionIndex - 1].Op is not
                (SkillProgramEffectOp.UseSelectedCardsAs or
                 SkillProgramEffectOp.UseAllHandCardsAsOrdinaryTrick))
            return;
        ContinueProgramSkill(frame.Id);
    }

    private void CompleteProgramSkillAfterDying(DyingResolution dying)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != dying.ParentFrameId)
            throw new InvalidOperationException("Program dying resolution lost its continuation.");
        ContinueProgramSkill(frame.Id);
    }

    private void AssertProgramSkillState()
    {
        var frames = _resolutionStack.OfType<ProgramSkillFrame>().ToArray();
        foreach (var frame in frames)
        {
            var program = _contentRegistry.Skills.GetValueOrDefault(frame.SkillId)?.Program;
            if (!IsValidPlayerSeat(frame.OwnerSeat) ||
                string.IsNullOrWhiteSpace(frame.SkillInstanceId) ||
                program?.GameplayHash != frame.GameplayHash)
                throw new InvalidOperationException("An active skill program has an invalid cursor or selection.");

            ProgramExecutionPlan plan;
            try
            {
                plan = ProgramInstructionResolver.Default.Resolve(frame, program);
            }
            catch (InvalidOperationException)
            {
                throw new InvalidOperationException("An active skill program has an invalid cursor or selection.");
            }
            if (frame.InstructionIndex < 1 || frame.InstructionIndex > plan.Instructions.Count ||
                frame.SelectedCardIds.Distinct().Count() != frame.SelectedCardIds.Count ||
                frame.SelectedTargetSeats.Any(seat => !IsValidPlayerSeat(seat)) ||
                frame.CardSetBindings.Select(binding => binding.Name).Distinct(StringComparer.Ordinal).Count() !=
                    frame.CardSetBindings.Count ||
                frame.CardSetBindings.Any(binding =>
                    binding.CardIds.Count != binding.SourceLocations.Count ||
                    binding.CardIds.Distinct().Count() != binding.CardIds.Count) ||
                frame.PindianResultBindings.Select(binding => binding.Name)
                    .Distinct(StringComparer.Ordinal).Count() != frame.PindianResultBindings.Count ||
                frame.AttackRangeCoverageBindings.Select(binding => binding.Name)
                    .Distinct(StringComparer.Ordinal).Count() != frame.AttackRangeCoverageBindings.Count ||
                frame.AttackRangeCoverageBindings.Any(binding =>
                    !IsValidPlayerSeat(binding.SubjectSeat) || binding.BeforeCount < 0 ||
                    binding.AfterCount < 0 || binding.BeforeCount >= _players.Count ||
                    binding.AfterCount >= _players.Count) ||
                frame.PendingMovementContinuation is { } movement &&
                    (!IsValidPlayerSeat(movement.SubjectSeat) || movement.BeforeCount < 0 ||
                     movement.BeforeCount >= _players.Count))
                throw new InvalidOperationException("An active skill program has an invalid cursor or selection.");
            if (frame.PendingMovementContinuation is { } pendingMovement)
            {
                var paidEffect = frame.InstructionIndex > 0
                    ? plan.Instructions[frame.InstructionIndex - 1]
                    : null;
                var awaitsSelectedMovement = paidEffect?.Op is
                    (SkillProgramEffectOp.GiveSelected or SkillProgramEffectOp.DiscardSelected) &&
                    pendingMovement.CoverageResultBind is null &&
                    pendingMovement.SubjectSeat == frame.OwnerSeat;
                var awaitsRandomTransfer = paidEffect?.Op == SkillProgramEffectOp.TransferRandomOwnedCard &&
                    pendingMovement.CoverageResultBind is null &&
                    frame.SelectedTargetSeats.Count == 1 &&
                    pendingMovement.SubjectSeat == frame.SelectedTargetSeats[0];
                var awaitsJudgmentClaim = paidEffect?.Op == SkillProgramEffectOp.ClaimJudgmentCard &&
                    frame.WindowContext?.Window == SkillProgramTriggerWindow.JudgmentFinalized &&
                    pendingMovement.CoverageResultBind is null &&
                    pendingMovement.SubjectSeat == frame.OwnerSeat;
                var awaitsRepeatedJudgment = paidEffect?.Op == SkillProgramEffectOp.RepeatJudgment &&
                    frame.RepeatedJudgment is { LastMatched: not null } &&
                    pendingMovement.CoverageResultBind is null &&
                    pendingMovement.SubjectSeat == frame.OwnerSeat;
                var awaitsOwnedMovement = paidEffect is
                {
                    Op: SkillProgramEffectOp.SelectAndMoveOwnedCard,
                    AwaitMovementTriggers: true
                } &&
                    paidEffect.CoverageResultBind == pendingMovement.CoverageResultBind;
                if (!awaitsSelectedMovement && !awaitsRandomTransfer &&
                    !awaitsJudgmentClaim && !awaitsRepeatedJudgment && !awaitsOwnedMovement)
                    throw new InvalidOperationException("A movement continuation lost its paid instruction.");
            }
            foreach (var coverage in frame.AttackRangeCoverageBindings)
            {
                if (plan.Instructions.Take(frame.InstructionIndex).Count(effect =>
                        effect.Op == SkillProgramEffectOp.SelectAndMoveOwnedCard &&
                        effect.CoverageResultBind == coverage.Name) != 1 ||
                    frame.PendingMovementContinuation?.CoverageResultBind == coverage.Name)
                    throw new InvalidOperationException("An attack-range coverage result has no completed producer.");
            }

            var executedSelection = plan.Instructions.Take(frame.InstructionIndex)
                .LastOrDefault(effect => effect.Op is
                    SkillProgramEffectOp.SelectTarget or SkillProgramEffectOp.SelectTargets);
            var awaitingCurrentSelection = executedSelection is not null &&
                ReferenceEquals(executedSelection, plan.Instructions[frame.InstructionIndex - 1]) &&
                ReferenceEquals(frame, _resolutionStack.LastOrDefault()) &&
                _pendingDecision is { Kind: DecisionKind.ProgramTrigger } pending &&
                pending.Choices.Count > 0 && pending.Choices.All(choice =>
                    choice.Parameters.GetValueOrDefault("frame-id") ==
                    frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                    choice.Parameters.GetValueOrDefault("program-action") ==
                    (executedSelection.Op == SkillProgramEffectOp.SelectTarget
                        ? "select-target"
                        : "select-targets"));
            var dynamicTargetsValid = executedSelection is null || executedSelection.Op switch
            {
                SkillProgramEffectOp.SelectTarget =>
                    frame.SelectedTargetSeats.Count == 1 || awaitingCurrentSelection && frame.SelectedTargetSeats.Count == 0,
                SkillProgramEffectOp.SelectTargets =>
                    awaitingCurrentSelection && frame.SelectedTargetSeats.Count == 0 ||
                    frame.SelectedTargetSeats.Count >= executedSelection.MinimumTargets &&
                    frame.SelectedTargetSeats.Count <= executedSelection.MaximumTargets &&
                    frame.SelectedTargetSeats.Distinct().Count() == frame.SelectedTargetSeats.Count,
                _ => false
            };
            if (!dynamicTargetsValid)
                throw new InvalidOperationException("An active skill program has an invalid cursor or selection.");

            if (frame.ChoiceBindings.Select(binding => binding.Name).Distinct(StringComparer.Ordinal).Count() != frame.ChoiceBindings.Count)
                throw new InvalidOperationException("An active program contains duplicate named choices.");
            foreach (var binding in frame.ChoiceBindings)
            {
                var producer = plan.Instructions.Take(frame.InstructionIndex).SingleOrDefault(effect =>
                    (effect.Op is SkillProgramEffectOp.ChooseOption or SkillProgramEffectOp.ChooseDifferentCategoryDiscard or
                        SkillProgramEffectOp.RequestSlashByTarget) &&
                    effect.ResultBind == binding.Name);
                var validOption = producer?.Op switch
                {
                    SkillProgramEffectOp.ChooseOption => producer.Options.Any(option => option.Id == binding.OptionId),
                    SkillProgramEffectOp.ChooseDifferentCategoryDiscard => binding.OptionId is
                        ChooseDifferentCategoryDiscardProgramOperationDescriptor.DiscardedOption or
                        ChooseDifferentCategoryDiscardProgramOperationDescriptor.DeclinedOption,
                    SkillProgramEffectOp.RequestSlashByTarget => binding.OptionId is
                        RequestSlashByTargetProgramOperationDescriptor.UsedSlashOption or
                        RequestSlashByTargetProgramOperationDescriptor.DeclinedOption,
                    _ => false
                };
                var chooserSeat = producer?.ChooserRef is { } producerChooser
                    ? ResolveProgramParticipant(frame, producerChooser)
                    : producer is null ? -1 : ResolveProgramEffectTarget(frame, producer.Target);
                if (producer is null || !validOption || binding.ChooserSeat != chooserSeat)
                    throw new InvalidOperationException("An active program choice does not match its committed producer.");
            }
            var paused = plan.Instructions[frame.InstructionIndex - 1];
            AssertProgramOwnedCardSelection(frame, paused);
            AssertProgramHoldCardSelection(frame, paused);
            AssertProgramRevealCardSelection(frame, paused);
            AssertProgramOwnedCardDistribution(frame, paused);
            AssertProgramAttackRangeAid(frame, paused);
            if (paused.Op == SkillProgramEffectOp.ChooseOption && ReferenceEquals(frame, _resolutionStack.LastOrDefault()) &&
                !frame.ChoiceBindings.Any(binding => binding.Name == paused.ResultBind) &&
                (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } choiceDecision ||
                 choiceDecision.PlayerSeat != (paused.ChooserRef is { } chooser
                     ? ResolveProgramParticipant(frame, chooser)
                     : ResolveProgramEffectTarget(frame, paused.Target)) ||
                 choiceDecision.Choices.Count == 0 || choiceDecision.Choices.Any(choice =>
                     choice.Parameters.GetValueOrDefault("program-action") != "choose-option" ||
                     choice.Parameters.GetValueOrDefault("result-bind") != paused.ResultBind ||
                     choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))))
                throw new InvalidOperationException("An unanswered program choice lost its matching prompt.");
            if (paused.Op == SkillProgramEffectOp.ChooseDifferentCategoryDiscard &&
                ReferenceEquals(frame, _resolutionStack.LastOrDefault()) &&
                !frame.ChoiceBindings.Any(binding => binding.Name == paused.ResultBind) &&
                (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } categoryDecision ||
                 categoryDecision.PlayerSeat != ResolveProgramParticipant(frame, paused.ChooserRef!) ||
                 categoryDecision.Choices.Count == 0 || categoryDecision.Choices.Any(choice =>
                     choice.Parameters.GetValueOrDefault("program-action") is not
                         ("different-category-discard" or "different-category-decline") ||
                     choice.Parameters.GetValueOrDefault("result-bind") != paused.ResultBind ||
                     choice.Parameters.GetValueOrDefault("frame-id") !=
                         frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))))
                throw new InvalidOperationException("An unanswered category challenge lost its matching prompt.");
            if (paused.Op == SkillProgramEffectOp.UseAllHandCardsAsOrdinaryTrick &&
                ReferenceEquals(frame, _resolutionStack.LastOrDefault()) &&
                (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } trickDecision ||
                 trickDecision.PlayerSeat != frame.OwnerSeat || trickDecision.Choices.Count == 0 ||
                 trickDecision.Choices.Any(choice =>
                     choice.Parameters.GetValueOrDefault("program-action") != "use-all-hand-as-ordinary-trick" ||
                     choice.Parameters.GetValueOrDefault("view-as-id") != paused.SourceBind ||
                     choice.Parameters.GetValueOrDefault("frame-id") !=
                         frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))))
                throw new InvalidOperationException("An unanswered ordinary-trick conversion lost its matching prompt.");

            if (frame.TriggerId is { } triggerId)
            {
                var trigger = program.Triggers.SingleOrDefault(item => item.Id == triggerId);
                if (trigger is null || frame.WindowContext is not { } context ||
                    context.OwnerSeat != frame.OwnerSeat || context.Window != trigger.Window ||
                    frame.ActivationId != triggerId || frame.SelectedCardIds.Count != 0 ||
                    executedSelection is null && frame.SelectedTargetSeats.Count != 0)
                    throw new InvalidOperationException("An active trigger program has an invalid cursor or context.");
                continue;
            }

            var activation = program.Activations.SingleOrDefault(item => item.Id == frame.ActivationId);
            if (activation is null ||
                frame.SelectedCardIds.Count < activation.MinCards ||
                frame.SelectedCardIds.Count > activation.MaxCards ||
                executedSelection is null &&
                (frame.SelectedTargetSeats.Count < activation.MinTargets ||
                 frame.SelectedTargetSeats.Count > activation.MaxTargets))
                throw new InvalidOperationException("An active skill program has an invalid cursor or selection.");
        }
        foreach (var scopeId in _pendingCardsMovedBatches
                     .Select(batch => batch.AwaitingProgramFrameId)
                     .Concat(_resolutionStack.OfType<CardsMovedTriggerWindowFrame>()
                         .Select(window => window.Batch.AwaitingProgramFrameId))
                     .OfType<long>())
        {
            if (frames.Count(frame => frame.Id == scopeId && frame.PendingMovementContinuation is not null) != 1)
                throw new InvalidOperationException("A scoped card-movement batch lost its waiting program frame.");
        }
        for (var index = 0; index < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not CardsMovedTriggerWindowFrame
                { Batch.AwaitingProgramFrameId: { } scopeId }) continue;
            if (!_resolutionStack.Take(index).OfType<ProgramSkillFrame>().Any(frame =>
                    frame.Id == scopeId && frame.PendingMovementContinuation is not null))
                throw new InvalidOperationException("A scoped movement window lost its waiting ancestor.");
        }

        if (_pendingDying is { ResumesProgramSkill: true } dying)
        {
            var parentIndex = _resolutionStack.FindLastIndex(item => item is ProgramSkillFrame program &&
                program.Id == dying.ParentFrameId);
            if (parentIndex < 0 || parentIndex + 1 >= _resolutionStack.Count ||
                _resolutionStack[parentIndex + 1] is not DyingFrame child || child.Id != dying.FrameId ||
                child.ParentFrameId != dying.ParentFrameId || dying.Attack is not null ||
                dying.DamageFrameId is not null)
                throw new InvalidOperationException("A program dying continuation is missing its parent program frame.");
        }
    }

    private static PromptChoice CreateProgramPlayChoice(LegalAction action)
    {
        var parameters = new Dictionary<string, string>
        {
            ["action"] = "use-program-skill",
            ["skill-id"] = action.ProgramSkillId!,
            ["activation-id"] = action.ProgramActivationId!,
            ["min-card-count"] = action.MinCardCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["max-card-count"] = action.MaxCardCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["min-target-count"] = action.MinTargetCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["max-target-count"] = action.MaxTargetCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (action.ProgramSkillOwnerSeat is { } ownerSeat)
            parameters["skill-owner-seat"] = ownerSeat.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new PromptChoice(
            new ChoiceId($"play.program.{action.ProgramSkillId!.Length}:{action.ProgramSkillId}.{action.ProgramActivationId}" +
                (action.ProgramSkillOwnerSeat is { } seat ? $".owner-{seat}" : string.Empty)),
            action.Description, [], [], parameters);
    }
}

public sealed record ProgramSkillStartedEvent(long FrameId, int OwnerSeat, string SkillId, string ActivationId) : IGameEvent;
public sealed record ProgramSkillResolvedEvent(long FrameId, int OwnerSeat, string SkillId, string ActivationId, bool Completed) : IGameEvent;
public sealed record ProgramSkillHpLostEvent(long FrameId, string SkillId, int TargetSeat, int Amount, int RemainingHp) : IGameEvent;
public sealed record ProgramSkillContributionResolvedEvent(long ActionId, int ProviderSeat, int SkillOwnerSeat,
    string SkillId, string ContributionId, int CardId, CardKind CardKind, Suit CardSuit) : IGameEvent;
