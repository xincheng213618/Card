namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static readonly Lazy<ProgramLegalityCatalog> ProgramLegalityMetadata = new(() =>
        ProgramLegalityCatalog.Discover(typeof(GameEngine).GetMethods(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic)), true);
    private ProgramLegalityRouter? _programLegality;
    private ProgramLegalityRouter ProgramLegality => _programLegality ??= new(ProgramLegalityMetadata.Value, this);

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

    // Activation gates can read their current skill instance before a frame exists.
    // Other frame-dependent conditions retain the original evaluator.
    private bool EvaluateProgramActivationCondition(
        SkillProgramCondition condition,
        PlayerSkillContext context,
        string skillId,
        string skillInstanceId) => condition.Kind switch
        {
            SkillProgramConditionKind.BooleanState => condition.Evaluate(context,
                static _ => throw new InvalidOperationException(
                    "A Pindian condition requires a running program frame."),
                stateId => GetProgramBooleanState(context.Seat, skillId, skillInstanceId, stateId)),
            SkillProgramConditionKind.All => condition.Children.All(child =>
                EvaluateProgramActivationCondition(child, context, skillId, skillInstanceId)),
            SkillProgramConditionKind.Any => condition.Children.Any(child =>
                EvaluateProgramActivationCondition(child, context, skillId, skillInstanceId)),
            SkillProgramConditionKind.Not => !condition.MissingEquipmentPresence(context) && !EvaluateProgramActivationCondition(
                condition.Children[0], context, skillId, skillInstanceId),
            _ => condition.Evaluate(context)
        };

    private IEnumerable<LegalAction> BuildProgramActions(CharacterState owner)
    {
        if (!owner.IsAlive || owner.Seat != _currentSeat || _phase != TurnPhase.Play) yield break;
        PlayerSkillContext? ordinaryContext = null;
        PlayerSkillContext? handLimitContext = null;
        foreach (var program in EnabledActivationPrograms(owner))
            foreach (var activation in program.Activations)
            {
                if (activation.OtherCharactersOnly) continue;
                var plan = ProgramInstructionResolver.Default.Resolve(program,
                    ProgramInstructionSourceKind.Activation, activation.Id);
                var features = plan.Features;
                // Usability enumerates native actions. Only conditions that read it
                // need that second action query; ordinary activations do not.
                var context = activation.Condition.RequiresHandLimitContext || features.RequiresHandLimitContext
                    ? handLimitContext ??= CreateSkillContext(owner, includeHandLimit: true)
                    : ordinaryContext ??= CreateSkillContext(owner);
                var legality = new ProgramActivationLegalityContext(owner, program, plan, context);
                var selection = new ProgramSelectionContext(owner.Seat, program, plan) { PlayerContext = context };
                if (!ProgramLegality.CanActivate(ProgramActivationLegalityPhase.BeforeOwnerFacts, legality)) continue;
                var ownerLegality = new ProgramLegalityParticipant(owner.Seat, context.HandCount);
                if (!ProgramLegality.CanActivate(ProgramActivationLegalityPhase.BeforeCardCount, legality)) continue;
                var requiredCards = GetProgramActivationMinimumCards(owner.Seat, program.Id, activation);
                if (!ProgramLegality.CanActivate(ProgramActivationLegalityPhase.BeforeCondition, legality)) continue;
                if (!EvaluateProgramActivationCondition(activation.Condition, context, program.Id, GetRuntimeSkillInstanceId(owner, program.Id)) || !CanPayProgramMarkerCost(owner, activation.MarkerCost) ||
                    activation.UsesPerTurn is { } limit &&
                    _programUses.GetValueOrDefault((owner.Seat, program.Id, activation.UsageGroup)) >= limit ||
                    ProgramSelections.PhaseLimit(selection) is { } phaseLimit &&
                    _programPhaseUses.GetValueOrDefault((owner.Seat, program.Id, activation.UsageGroup)) >= phaseLimit ||
                    activation.UsesPerGame is { } gameLimit &&
                    _skillRuntimeState.GetUsage(
                        owner.Seat,
                        program.Id,
                        activation.UsageGroup,
                        SkillUsageScope.Game) >= gameLimit)
                    continue;
                if (!features.Legality.CanStart(ownerLegality))
                    continue;
                if (!ProgramLegality.CanActivate(ProgramActivationLegalityPhase.AfterCommonStart, legality)) continue;
                if (features.ForOperation(SkillProgramEffectOp.SelectTargets).Any(effect =>
                    effect.TargetKind is { } kind &&
                    GetProgramTargetSeats(owner.Seat, kind).Count < effect.MinimumTargets))
                    continue;
                var candidates = new ProgramSelectionCandidates
                {
                    MinCardCount = requiredCards, MaxTargetCount = activation.MaxTargets
                };
                if (!ProgramSelections.Apply(ProgramSelectionStage.BeforeSources, selection, candidates)) continue;
                if (!ProgramSelections.TryBuildSource(selection, candidates))
                    candidates.Cards = activation.MaxCards == 0 ? [] : activation.SourceZones
                        .SelectMany(zone => _cardZones.CardsAt(new CardLocation(zone, owner.Seat)))
                        .Where(card => CanSelectProgramActivationCard(plan, card, owner.Seat, program.Id))
                        .Where(card => ProgramSelections.AllowsCard(ProgramSelectionStage.CardAfterDeclaration, selection, card))
                        .Where(card => activation.EquipmentSlots.Count == 0 ||
                            EquipmentCatalog.IsEquipment(card.Kind) &&
                            activation.EquipmentSlots.Contains(EquipmentCatalog.Get(card.Kind).Slot))
                        .Select(card => card.Id).Distinct().Order().ToArray();
                if (!ProgramSelections.Apply(ProgramSelectionStage.AfterCards, selection, candidates)) continue;
                if (!ProgramSelections.TryBuildSourceTargets(selection, candidates))
                    candidates.Targets = activation.MaxTargets == 0 ? [] : _players
                    .Where(target => target.IsAlive && ProgramSelections.AllowsTarget(selection, target.Seat) &&
                        features.Legality.CanSelectTarget(ownerLegality,
                            CreateProgramLegalityParticipant(target.Seat)) &&
                        (activation.TargetKind is SkillProgramTargetKind.AnyLiving or SkillProgramTargetKind.AnyWounded or
                         SkillProgramTargetKind.AnyLivingHighestHp or SkillProgramTargetKind.AnyLivingHighestHand or SkillProgramTargetKind.AnyLivingLeastHandCount or SkillProgramTargetKind.AnyLivingMale ||
                         target.Seat != owner.Seat) &&
                        (activation.TargetKind switch
                        {
                            SkillProgramTargetKind.ExclusiveRendeBeneficiary or SkillProgramTargetKind.ExclusiveQingnangBeneficiary =>
                                GetProgramTargetSeats(owner.Seat, activation.TargetKind).Contains(target.Seat),
                            SkillProgramTargetKind.OtherLiving => target.Seat != owner.Seat,
                            SkillProgramTargetKind.OtherLivingMale =>
                                target.Seat != owner.Seat && target.Gender == GeneralGender.Male,
                            SkillProgramTargetKind.OtherWoundedMale =>
                                target.Seat != owner.Seat && target.Gender == GeneralGender.Male &&
                                IsRuleWounded(target),
                            SkillProgramTargetKind.OtherLivingInAttackRange =>
                                target.Seat != owner.Seat &&
                                IsWithinAttackRange(owner.Seat, target.Seat),
                            SkillProgramTargetKind.OtherLivingWhoseAttackRangeIncludesOwner =>
                                target.Seat != owner.Seat &&
                                IsWithinAttackRange(target.Seat, owner.Seat),
                            SkillProgramTargetKind.OtherLivingWithQinggangSword =>
                                target.Seat != owner.Seat &&
                                HasWeaponAbility(target, CardKind.QinggangSword),
                            SkillProgramTargetKind.OtherLivingSlashable =>
                                CanUseProvidedSlashTarget(owner, target),
                            SkillProgramTargetKind.AnyLivingWithHand => GetHand(target).Count > 0,
                            SkillProgramTargetKind.OtherLivingWithHand =>
                                target.Seat != owner.Seat && GetHand(target).Count > 0,
                            SkillProgramTargetKind.OtherLivingWuFactionWithHand =>
                                target.Seat != owner.Seat && GetHand(target).Count > 0 &&
                                GetEffectiveFactionId(target) == "wu",
                            SkillProgramTargetKind.OtherLivingWithHandOrEquipment =>
                                target.Seat != owner.Seat && GetHand(target).Count + GetEquipment(target).Count > 0,
                            SkillProgramTargetKind.OtherLivingWithDiscardableHandOrEquipment =>
                                target.Seat != owner.Seat && HasDiscardableHeBy(owner.Seat, target.Seat),
                            SkillProgramTargetKind.OtherLivingEmptyHand =>
                                target.Seat != owner.Seat && GetHand(target).Count == 0,
                            SkillProgramTargetKind.OtherLivingWithHandHpGreaterThanOwner =>
                                                    target.Seat != owner.Seat && GetHand(target).Count > 0 && target.HasHpValue && owner.HasHpValue && target.Hp > owner.Hp,
                            SkillProgramTargetKind.OtherLivingAtDistanceOne =>
                                                    target.Seat != owner.Seat && GetCombatDistance(owner.Seat, target.Seat) == 1,
                            SkillProgramTargetKind.AnyLiving => true,
                            SkillProgramTargetKind.AnyLivingMale => target.Gender == GeneralGender.Male,
                            SkillProgramTargetKind.AnyLivingHighestHp or SkillProgramTargetKind.AnyLivingHighestHand or SkillProgramTargetKind.AnyLivingLeastHandCount or SkillProgramTargetKind.OtherLivingHighestHand =>
                                GetProgramTargetSeats(owner.Seat, activation.TargetKind).Contains(target.Seat),
                            SkillProgramTargetKind.OtherWounded =>
                                target.Seat != owner.Seat && IsRuleWounded(target),
                            SkillProgramTargetKind.AnyWounded => IsRuleWounded(target),
                            SkillProgramTargetKind.AnyLivingHandBelowMaxHp => target.HasHpValue && GetHand(target).Count < target.MaxHp,
                            SkillProgramTargetKind.OtherLivingPair => target.Seat != owner.Seat &&
                                _players.Count(peer => peer.IsAlive && peer.Seat != owner.Seat) >= 2,
                            SkillProgramTargetKind.EventTarget => false,
                            _ => false
                        }) &&
                        (!features.ProhibitsEquipmentReplacement ||
                         HasFreeEquipmentSlotForOwnedHandEquipment(owner, target)))
                    .Select(target => target.Seat).Order().ToArray();
                if (!ProgramSelections.Apply(ProgramSelectionStage.AfterTargets, selection, candidates)) continue;
                var cards = candidates.Cards;
                var targets = candidates.Targets;
                if (cards.Length < requiredCards || targets.Length < activation.MinTargets ||
                    activation.SelectedCardsDistinctSuits && cards.Select(id => _cardZones.CardsAt(_cardZones.GetLocation(id)).Single(card => card.Id == id).Suit).Distinct().Count() < activation.MinCards ||
                    activation.SelectedCardsSameSuit && !cards
                        .Select(id => _cardZones.CardsAt(_cardZones.GetLocation(id)).Single(card => card.Id == id).Suit)
                        .GroupBy(suit => suit).Any(group => group.Count() >= activation.MinCards)) continue;
                if (!ProgramSelections.Apply(ProgramSelectionStage.Availability, selection, candidates)) continue;
                if (features.FirstInstruction is
                    {
                        Op: SkillProgramEffectOp.SelectTarget,
                        TargetKind: { } dynamicKind
                    } dynamicSelection &&
                    !GetProgramTargetSeats(owner.Seat, dynamicKind, marker: dynamicSelection.Marker)
                        .Any(seat => IsProgramTargetEligible(owner.Seat, dynamicKind,
                            dynamicSelection.Zones, seat, dynamicSelection.Marker)))
                    continue;
                if (!ProgramSelections.Apply(ProgramSelectionStage.AfterAvailability, selection, candidates) ||
                    !ProgramSelections.Apply(ProgramSelectionStage.FinalTargets, selection, candidates)) continue;
                candidates.MaxCardCount = activation.CardCountExpression is not null ? requiredCards :
                    activation.MaxCards == int.MaxValue ? candidates.Cards.Length : activation.MaxCards;
                var activationLabel = _contentRegistry!.Skills[program.Id].ProgramPresentation?.ActivationLabels
                    .GetValueOrDefault(activation.Id);
                if (!ProgramSelections.Apply(ProgramSelectionStage.Relationships, selection, candidates) ||
                    !ProgramSelections.Apply(ProgramSelectionStage.Projection, selection, candidates)) continue;
                yield return new LegalAction(LegalActionKind.UseProgramSkill, null, null,
                    $"发动【{_contentRegistry!.Skills[program.Id].Name}】" +
                    (activationLabel is null ? string.Empty : $" · {activationLabel}"),
                    MinCardCount: candidates.MinCardCount, MaxCardCount: candidates.MaxCardCount,
                    MinTargetCount: activation.MinTargets, MaxTargetCount: candidates.MaxTargetCount)
                {
                    ProgramSkillId = program.Id,
                    ProgramActivationId = activation.Id,
                    SelectedCardsSameSuit = activation.SelectedCardsSameSuit,
                    SelectedCardsDistinctSuits = activation.SelectedCardsDistinctSuits,
                    SelectedCardsSameNameOrAllEquipment = candidates.SelectedCardsSameNameOrAllEquipment,
                    ProgramAiHint = CreateProgramAiHint(program, activation, context, owner.IsFaceDown),
                    SelectableCardIds = Array.AsReadOnly(candidates.Cards),
                    SelectableTargetSeats = Array.AsReadOnly(candidates.Targets),
                    SelectableCardTargets = candidates.CardTargets,
                    SelectableTargetSequences = candidates.TargetSequences
                };
            }
        foreach (var exchange in ProgramActions.Build(ProgramActionSourcePhase.ProgramTail, owner)) yield return exchange;
        foreach (var skillOwner in _players.Where(player => player.IsAlive && player.Seat != owner.Seat).OrderBy(player => player.Seat))
            foreach (var program in EnabledContributionPrograms(skillOwner))
                foreach (var contribution in program.Contributions)
                {
                    var providerFaction = GetEffectiveFactionId(owner);
                    var key = (owner.Seat, skillOwner.Seat, program.Id, contribution.Id);
                    if (providerFaction is null ||
                        !contribution.ProviderFactions.Contains(providerFaction, StringComparer.Ordinal) ||
                        !HasSkillRoleQualification(skillOwner, program.Id, null, contribution.OwnerRole) ||
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

    private int GetProgramActivationMinimumCards(int ownerSeat, string skillId, SkillProgramActivation activation) =>
        activation.CardCountExpression switch
        {
            null => activation.MinCards,
            SkillProgramCardCountExpression.NextPhaseActivationOrdinal =>
                checked(_programPhaseUses.GetValueOrDefault((ownerSeat, skillId, activation.UsageGroup)) + 1),
            _ => throw new InvalidOperationException("Unsupported activation card-count expression.")
        };

    private bool CanSelectProgramActivationCard(ProgramExecutionPlan plan, Card card, int ownerSeat, string skillId)
    {
        var activation = plan.Activation!;
        var features = plan.Features;
        var selection = new ProgramSelectionContext(ownerSeat, _contentRegistry.GetSkill(skillId).Program!, plan);
        if (!ProgramSelections.AllowsCard(ProgramSelectionStage.CardBeforeDiscard, selection, card)) return false;
        if (features.ForOperation(SkillProgramEffectOp.DiscardSelected).Any(effect => effect.Condition.Kind == SkillProgramConditionKind.Always) &&
            IsSelfHandCategoryDiscardForbidden(ownerSeat, card, _cardZones.GetLocation(card.Id), OwnedCardMoveIntent.Discard)) return false;
        if (!ProgramSelections.AllowsCard(ProgramSelectionStage.CardAfterDiscard, selection, card)) return false;
        return (activation.CardKinds.Count == 0 || activation.CardKinds.Contains(card.Kind)) &&
            (activation.CardSuits.Count == 0 || activation.CardSuits.Contains(ProgramSelections.CardSuit(selection, card))) &&
            (activation.CardCategories.Count == 0 || activation.CardCategories.Any(category => MatchesSkillProgramCardCategory(card.Kind, category)));
    }

    private SkillProgramAiHint CreateProgramAiHint(
        SkillProgram program,
        SkillProgramActivation activation,
        PlayerSkillContext context,
        bool faceDown)
    {
        var owner = _players[context.Seat];
        var instanceId = GetRuntimeSkillInstanceId(owner, program.Id);
        var features = ProgramInstructionResolver.Default.Features(activation);
        var hint = ProgramCompositionAi.Estimate(activation.Effects, context, faceDown,
            CreateProgramAiPublicContext(owner) with
            {
                ActivationCardCount = GetProgramActivationMinimumCards(owner.Seat, program.Id, activation),
                EligibleTargetCount = features.First(SkillProgramEffectOp.SelectTargets) is { TargetKind: { } selectedKind }
                    ? GetProgramTargetSeats(owner.Seat, selectedKind).Count
                    : features.UsesArrowBarrageSelection
                        ? _players.Count(player => player.IsAlive && player.Seat != owner.Seat)
                        : null,
                PhaseUsageCount = usageId => _skillRuntimeState.GetUsage(owner.Seat,
                    program.Id, usageId, SkillUsageScope.Phase),
                BooleanState = stateId => GetProgramBooleanState(owner.Seat, program.Id, instanceId, stateId)
            }).Hint;
        return ProgramSelections.DecorateAi(new(context.Seat, program,
            ProgramInstructionResolver.Default.Resolve(program, ProgramInstructionSourceKind.Activation, activation.Id))
            { PlayerContext = context }, hint);
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
        var program = action.ProgramSkillOwnerSeat is null && action.ProgramSkillId is { } skillId
            ? _contentRegistry.GetSkill(skillId).Program : null;
        var plan = program is null ? null : ProgramInstructionResolver.Default.Find(program,
            ProgramInstructionSourceKind.Activation, action.ProgramActivationId);
        var activation = plan?.Activation;
        ProgramSelectionContext? validationContext = program is not null && plan is not null
            ? new(_currentSeat, program, plan) : null;
        if (validationContext is { } beforeSuitContext &&
            ProgramSelectionValidators.Validate(ProgramSelectionValidationStage.BeforeDistinctSuits, beforeSuitContext,
                action, cards, targets) is { } initialSelectionError)
            return initialSelectionError;
        if (activation is { SelectedCardsDistinctSuits: true } && cards.Select(id => _cardZones.CardsAt(_cardZones.GetLocation(id))
                .Single(card => card.Id == id).Suit).Distinct().Count() != cards.Count)
            return new CommandError(CommandErrorCode.InvalidCard, "The selected cards must have distinct printed suits.");
        if (activation is not null && cards.Any(id => !CanSelectProgramActivationCard(plan!,
                    _cardZones.CardsAt(_cardZones.GetLocation(id)).Single(card => card.Id == id), _currentSeat, program!.Id)))
            return new CommandError(CommandErrorCode.InvalidCard, "The selected cards do not satisfy the activation filters.");
        if (validationContext is { } filteredContext &&
            ProgramSelectionValidators.Validate(ProgramSelectionValidationStage.AfterCardFilters, filteredContext,
                action, cards, targets) is { } filteredSelectionError)
            return filteredSelectionError;
        if (activation is { CategoryTargetLedgerId: { } ledgerId } &&
            !CanActivateCategoryTargetLedger(_currentSeat, program!.Id, ledgerId, cards, targets))
            return new CommandError(CommandErrorCode.IllegalAction, "This card category or target was already used in this phase.");
        if (activation is not null && DistinctTurnTargetUsage(activation) is { } actualTurnLedger &&
            (targets.Count != 1 || !CanActivateDistinctTurnTarget(_currentSeat, program!.Id, actualTurnLedger, targets[0])))
            return new CommandError(CommandErrorCode.IllegalAction, "This target was already designated by this ledger in the actual turn.");
        if (activation is { TargetPhaseLedgerId: { } targetLedger } &&
            (targets.Count != 1 || !CanActivateTargetPhaseLedger(_currentSeat, program!.Id, targetLedger, targets[0])))
            return new CommandError(CommandErrorCode.IllegalAction, "This target was already used in this play phase.");
        if (plan is not null)
        {
            if (ProgramSelectionValidators.Validate(ProgramSelectionValidationStage.BeforePublicPrerequisites,
                    new(_currentSeat, program!, plan), action, cards, targets) is { } prerequisiteError)
                return prerequisiteError;
            var owner = new ProgramLegalityParticipant(_currentSeat, GetHand(_players[_currentSeat]).Count);
            if (!plan.Features.Legality.CanStart(owner))
                return new CommandError(CommandErrorCode.IllegalAction, "The program's public prerequisites are no longer satisfied.");
            if (targets.Any(seat => !plan.Features.Legality.CanSelectTarget(owner,
                CreateProgramLegalityParticipant(seat))))
                return new CommandError(CommandErrorCode.InvalidTarget, "The program's target prerequisites are no longer satisfied.");
        }
        return null;
    }

    private CommandResult SubmitUseProgramSkill(UseProgramSkillCommand command)
    {
        if (ProgramActions.TrySubmit(command, out var registeredResult)) return registeredResult;
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
            ExecuteValidatedProgramSkill(owner, action, command.CardIds, command.TargetSeats);
            AdvanceRulesAndPublishState();
            if (_options.AdvanceAfterHumanCommands) AdvanceToHumanBoundary();
        });
    }

    private void ExecuteProgramSkill(CharacterState owner, LegalAction action,
        IReadOnlyList<int> cards, IReadOnlyList<int> targets)
    {
        if (ProgramActions.TryExecute(owner, action, cards, targets)) return;
        var current = BuildProgramActions(owner).SingleOrDefault(candidate =>
            candidate.ProgramSkillId == action.ProgramSkillId &&
            candidate.ProgramActivationId == action.ProgramActivationId &&
            candidate.ProgramSkillOwnerSeat == action.ProgramSkillOwnerSeat)
            ?? throw new InvalidOperationException("The skill activation is no longer available.");
        if (ValidateProgramSelection(current, cards, targets) is { } error)
            throw new InvalidOperationException(error.Message);
        ExecuteValidatedProgramSkill(owner, current, cards, targets);
    }

    private void ExecuteValidatedProgramSkill(CharacterState owner, LegalAction action,
        IReadOnlyList<int> cards, IReadOnlyList<int> targets)
    {
        if (TryExecuteSharedProgramActivation(owner, action, cards, targets)) return;
        if (action.ProgramSkillOwnerSeat is { } skillOwnerSeat)
        {
            if (ProgramActions.TryContribute(owner, _players[skillOwnerSeat], action, cards[0])) return;
            ExecuteProgramContribution(owner, _players[skillOwnerSeat], action, cards[0]);
            return;
        }
        var program = GetEnabledSkillProgram(owner, action.ProgramSkillId ??
            throw new InvalidOperationException("The program action has no skill identity."));
        var plan = ProgramInstructionResolver.Default.Resolve(program,
            ProgramInstructionSourceKind.Activation, action.ProgramActivationId!);
        var activation = plan.Activation!;
        PayProgramMarkerCost(owner, activation.MarkerCost, program.Id, activation.Id);
        if (activation.UsesPerGame is { } gameLimit)
        {
            if (!_skillRuntimeState.TryConsumeUsage(
                    owner.Seat,
                    program.Id,
                    activation.UsageGroup,
                    SkillUsageScope.Game,
                    gameLimit))
                throw new InvalidOperationException("The skill activation already exhausted its game usage limit.");
            AdvanceEventRulesAndQueueFact(new SkillUsageConsumedEvent(
                owner.Seat,
                program.Id,
                activation.UsageGroup,
                SkillUsageScope.Game,
                Count: 1));
        }
        var key = (owner.Seat, program.Id, activation.UsageGroup);
        _programUses[key] = _programUses.GetValueOrDefault(key) + 1;
        if (ProgramSelections.PhaseLimit(new(owner.Seat, program, plan)) is not null || activation.CardCountExpression is not null)
            _programPhaseUses[key] = _programPhaseUses.GetValueOrDefault(key) + 1;
        var frame = new ProgramSkillFrame(++_resolutionSequence, owner.Seat, program.Id, activation.Id,
            program.GameplayHash, 0, Array.AsReadOnly(cards.ToArray()), Array.AsReadOnly(targets.ToArray()))
        {
            SkillInstanceId = GetRuntimeSkillInstanceId(owner, program.Id),
            NextActualUseAdjustment = FreezeNextActualUseProgramSelection(owner, plan, targets),
            HpLossSlashSelection = FreezeHpLossSlashSelection(owner, plan, targets),
            SelectedAllOwnerHandCards = plan.Features.HasOperation(SkillProgramEffectOp.DrawAllHandSelectedBonus)
                ? GetHand(owner).Count > 0 && GetHand(owner).All(card => cards.Contains(card.Id)) : null
        };
        frame = CaptureHpConditionalProgramSource(frame);
        frame = CaptureEquipmentSuitProgramSource(frame);
        PushRuntimeFrame(frame);
        AdvanceEventRulesAndQueueFact(new ProgramSkillStartedEvent(frame.Id, owner.Seat, program.Id, activation.Id));
        AddLog("ActiveSkill", $"{owner.Name} 发动【{_contentRegistry!.Skills[program.Id].Name}】。", owner.Seat);
        AdvanceRuntimeProgram(frame.Id);
    }

    private void ExecuteProgramContribution(CharacterState provider, CharacterState skillOwner,
        LegalAction action, int cardId)
    {
        var program = GetEnabledSkillProgram(skillOwner, action.ProgramSkillId ??
            throw new InvalidOperationException("The contribution action has no skill identity."));
        var contribution = program.Contributions.Single(item => item.Id == action.ProgramActivationId);
        var instanceId = GetRuntimeSkillInstanceId(skillOwner, program.Id);
        if (!HasSkillRoleQualification(skillOwner, program.Id, instanceId, contribution.OwnerRole))
            throw new InvalidOperationException("The contribution's exact skill source no longer qualifies.");
        var projectedInstanceId = skillOwner.SkillGrants.Grants.Any(g => g.SkillId == program.Id &&
            g.SkillInstanceId == instanceId && g.LordProjection is not null) ? instanceId : null;
        var key = (provider.Seat, skillOwner.Seat, program.Id, contribution.Id);
        if (_programContributionUses.GetValueOrDefault(key) >= contribution.UsesPerPlayPhase)
            throw new InvalidOperationException("The contribution was already used in this play phase.");
        var card = GetHand(provider).Single(candidate => candidate.Id == cardId);
        if (!contribution.CardKinds.Contains(card.Kind) && !contribution.CardSuits.Contains(card.Suit))
            throw new InvalidOperationException("The contributed physical hand card no longer matches the binding.");

        _programContributionUses[key] = _programContributionUses.GetValueOrDefault(key) + 1;
        var actionId = ++_resolutionSequence;
        var reason = new CardMoveReason($"skill-program.{program.Id}.{contribution.Id}.contribute");
        MoveCard(card, CardLocation.Hand(provider.Seat), CardLocation.Processing, reason, movement => RecordBianFuRenOwnedTransferProducer(movement,skillOwner.Seat,actionId));
        MoveCard(card, CardLocation.Processing, CardLocation.Hand(skillOwner.Seat), reason);
        AdvanceEventRulesAndQueueFact(new ProgramSkillContributionResolvedEvent(actionId, provider.Seat, skillOwner.Seat,
            program.Id, contribution.Id, card.Id, card.Kind, card.Suit, projectedInstanceId));
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

    private void FinishProgramSkill(ProgramSkillFrame frame, bool completed)
    {
        frame = CompleteFrameNativeLogicalDiscard(frame);
        CleanupProgramBoundCards(frame, completed);
        if (TryCompleteShenwuTransplantProgram(frame, completed)) return;
        if (TryCompleteHellSoulProgram(frame, completed)) return;
        if (TryCompleteWenheChaosProgram(frame, completed)) return;
        // Completion must wait for a quiet boundary: a program skill finishing inside
        // an unwind (death triggers, chained attacks) can coincide with another
        // in-flight response window (Stone Axe, dying rescue), whose pending
        // decision must survive until it resolves.
        if (_winner != Winner.None && _status != EngineStatus.Completed &&
            _pendingDecision is null && _resolutionStack.Count == 0)
        {
            CompleteGame();
        }
        if (frame.TriggerId is not null)
        {
            CompleteRuntimeProgramBinding(frame, completed);
            return;
        }
        AdvanceEventRulesAndQueueFact(new ProgramSkillResolvedEvent(frame.Id, frame.OwnerSeat, frame.SkillId, frame.ActivationId, completed));
        PopResolutionFrame(frame.Id, ResolutionFrameKind.ProgramSkill);
    }

    private void BeginProgramSkillDying(long parentFrameId, CharacterState victim)
    {
        if (ActiveDying is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != parentFrameId)
            throw new InvalidOperationException("A program dying continuation requires its program frame.");
        var responders = Array.AsReadOnly(BuildDyingResponderSeats(victim.Seat).ToArray());
        var id = ++_resolutionSequence;
        PushRuntimeFrame(new DyingFrame(id, parentFrameId, victim.Seat, null, responders, 0,
            DyingContinuationKind.ProgramSkill));
        AdvanceEventRulesAndQueueFact(new PlayerDyingEvent(id, victim.Seat, null));
        _status = EngineStatus.Running;
        if (!TryBeginMandatorySelfDyingProgram(ActiveDying!) && !TryBeginDyingEntryProgramWindow(ActiveDying!)) ExposeHumanDyingPrompt();
    }

    private SkillProgramStepOutcome BeginProgramSkillDamage(
        ProgramSkillFrame frame,
        int targetSeat,
        int amount,
        ProgramParticipantReference? sourceReference = null,
        DamageNature? nature = null,
        bool sourceLess = false,
        int? explicitSourceSeat = null)
    {
        var judgmentNested = frame.WindowContext?.Judgment is { } frozenJudgment &&
            ActiveJudgment is { } pendingJudgment &&
            pendingJudgment.Id == frozenJudgment.JudgmentFrameId &&
            SameAttackOwner(ActiveCardAttack, GetJudgmentAttack(pendingJudgment));
        var damageWindowNested = frame.WindowContext?.Window ==
            SkillProgramTriggerWindow.AfterDamageApplied && ActiveDamageTrigger is { } outerWindow &&
            outerWindow.Id == frame.WindowContext.ParentFrameId;
        var recoveryNested = TryDescribeRecoveryDamage(frame, targetSeat, amount, sourceReference,
            nature, sourceLess, explicitSourceSeat, out var recoveryOrigin, out var recoveryDamageWindowId);
        if (CurrentDamageAttempt is not null && !judgmentNested && !damageWindowNested && !recoveryNested &&
            !AllowsHellKingNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            frame.FactionRecoveryDraft?.SettlingDebts != true && !AllowsSuitPreventionNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsHalfHandSupportNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsDynamicDiscardNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsSignedDamagePaymentNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsRecipientContestNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsRecipientConsequencesNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsCappedConversionNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsFireOrNamedNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsSlashTargetBenefitNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsTargetPenaltyNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsActualEquipmentOrDiscardNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsOffTurnUsedCardGiftNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) && !AllowsOrderedPrintedSkillLossNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) && !AllowsOverflowTargetCancellationNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsRecipientCategoryMarkNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsOwnedDeathBenefitNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsDyingSuitsNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsPhaseHandSeizureNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsPublicPilePreparationNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) && !AllowsPublicPileCashOutNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) && !AllowsGiftedSlashNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) && !AllowsCategoryActivationNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) && !AllowsDrawAdviceNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsLiejieSourceDiscardNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsChainedStateBasicNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsSelectedForeignCardSlashNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsDrawFundedDistinctBasicNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsSourceCurseNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsKuangfuNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsGainGiftNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsUnequalHandHpNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsPhaseStartCardControlNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsEndRefillNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsIncomingDistanceCourtesyNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsNamedHandChoiceNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsExtraEffectsAndRankGuessNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsFirstSlashDuelNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsCaoYingNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsPrivateWhirlNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsBuZhiNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsLianZhuNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsJiqiaoNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsWangYunNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsZhugeGuoNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsXiZhiCaiNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsLiuQiNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsBeiMiHuNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsSimaHuiNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsXuRongNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsLiJueNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsZhangJiNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsSuFeiNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsHuangQuanNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsFanChouNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsGuoSiNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsZhangQiYingNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsCaoChunNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsWangCanNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsSpPangTongNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsNeifaNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsLvQianNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsLvKuangLvXiangNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsXinPiNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsHuaManNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsLuZhiWeiNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsKanZeNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsHeQiNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsZhaoXiangNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsXuanGongZhuNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsDongZhaoNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsDeathGrowthNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsBaoNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsSimultaneousNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsRiceNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsDamageAcquisitionNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsPhaseNamePredictionNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsLastDamageSourceReciprocityNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsMatchingRecastNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsPairedHandRecastNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsOutsidePhaseDrawDiscardNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsOriginalHandEntityNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsPairedColorDispositionNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsJoinedTrickDamageRewardNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsRoundGainedEquipmentDrawNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsSameNameHandNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsCompletedUndamagedTargetRevealNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsFixedDistanceDebtNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsActualHandGainNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsOldHandDispositionNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsFrozenHandRosterNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsPlacedEquipmentBenefitNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsCaoSongLiluNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsCaoShuangNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsSpZhangLiaoNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsSunRuOlNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsLiuPiNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsWoLongFengChuNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsHuoJunFenruiNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsQiaoZhouNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsXunyiNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsDuXiNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsFuRongNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsDailiNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsZhenyingNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsMaWanNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsDuFuRenNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsZhangShiPingNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsFeiYiNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsQianZhaoNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsDingShangWanNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsLiWanNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsHeJinNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsYanRouNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsLuYuShengNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsWangKuangNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsYuanHuanNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsHanFuNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsWuAnGuoNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsZhangYanNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsZhuJunNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsNiuFuNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsRuanHuiNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsMaMiDiNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsDuanJiongNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsTianChouNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsChengGongYingNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsMengDaNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsCaiMaoNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsYaDanNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsCaoTengNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsYangFuNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsWuTuGuNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsSunHaoNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsDaQiaoXiaoQiaoNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsFanJiangZhangDaNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsWenYangNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsMaYuanYiNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
                    !AllowsHuBanNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
                    !AllowsLiuBaNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) && !AllowsLiuPanNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) && !AllowsQuHuangNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) && !AllowsCaoYuNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) && !AllowsRuanYuNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) && !AllowsBaoXinNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) && !AllowsWeijieNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) && !AllowsQushiNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) && !AllowsCuiYanNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) && !AllowsLiYiNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) && !AllowsSunYuanNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) && !(AllowsLiuZhangNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || (AllowsTangTangNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || AllowsMouCaoPiNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess)) || (AllowsYuanJiNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || AllowsKongShuNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || AllowsMaChengNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || (AllowsSunHongNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || (AllowsZhouQunNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || (AllowsGuanHaiNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || (AllowsXieLingYuNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || (AllowsLiFengNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || (AllowsOrdinaryCaiFuRenNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || (AllowsOrdinaryGuYongNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || (AllowsHuanShuJunNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || (AllowsZhaoZhongNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || (AllowsTaoQianNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || (AllowsPangJiNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || (AllowsLeMiHengNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || (AllowsLeZhouFeiXianshuNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || (((AllowsWeiwanNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || AllowsOlMoRumoNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess)) || AllowsZixiEffectsNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess)) || AllowsWuYixianNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || ((AllowsQiqinNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || (AllowsGuYinNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || (AllowsMoQuanyuNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || AllowsMoSunQuanPaidNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess)))) || AllowsZixiPlacementNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess)))))))))))))))))) &&
            !(AllowsKenshangNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || AllowsFulueNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || AllowsYangFengNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || AllowsAHuiNanNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || AllowsLiuYanNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || AllowsWangGuanNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || AllowsQinLangNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || AllowsWuJingNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || AllowsXiangChongNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess)) &&
            !AllowsKeBiNengNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsCaoXiNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsHuJinDingNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsYanPuNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) &&
            !AllowsZhangYiNestedDamage(frame, targetSeat, amount, sourceReference, nature, sourceLess) || ActiveDying is not null && !recoveryNested ||
            _resolutionStack.LastOrDefault() is not ProgramSkillFrame current || current.Id != frame.Id ||
            amount <= 0 || !IsValidPlayerSeat(targetSeat) || !_players[targetSeat].IsAlive)
            throw new InvalidOperationException("A program damage effect requires one active program and living target.");
        var state = new AttackAttemptState(
            explicitSourceSeat ?? (sourceReference is { } reference ? ResolveProgramParticipant(frame, reference) : frame.OwnerSeat),
            targetSeat, amount, nature ?? DamageNature.Normal, sourceLess,
            frame.WindowContext?.Window == SkillProgramTriggerWindow.JudgmentFinalized
                ? frame.WindowContext.ParentFrameId : null);
        var parentAttackId = CurrentDamageAttempt?.ResolutionId;
        ReplaceRuntimeTop(current with { AttackAttempt = state,
            AttackReturn = new(parentAttackId, damageWindowNested ? frame.WindowContext!.ParentFrameId : recoveryDamageWindowId,
                judgmentNested ? frame.WindowContext!.Judgment!.JudgmentFrameId : null)
                { RecoveryOrigin = recoveryOrigin } });
        IDamageAttempt attack = new ProgramAttackHandle(this, frame.Id);
        if (frame.WindowContext?.Judgment is { } judgment)
        {
            AdvanceEventRulesAndQueueFact(new ProgramJudgmentDamageRequestedEvent(
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
        if (!ApplyAttackDamage(attack)) CompleteDamageAttack(attack);
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
            !_players[targetSeat].IsAlive || !CanBePindianTarget(frame.OwnerSeat, targetSeat) ||
            GetHand(_players[targetSeat]).Count == 0 ||
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
            outputKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.Peach or CardKind.ArrowBarrage) ||
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
        if (outputKind == CardKind.Peach)
        {
            var rule = GetEnabledSkillProgram(owner, frame.SkillId).ViewAs.Single(item => item.Id == viewAsId);
            PrepareNextActualUseSelectedRecovery(frame, selection);
            ResolveRecoveryCard(owner, owner, selection.Cards[0], "桃", CardKind.Peach,
                1 + rule.RecoveryBonus, conversionSource: source, physicalCards: selection.Cards);
            return SkillProgramStepOutcome.AwaitChild;
        }
        if (frame.HpLossSlashSelection is not null) return BeginHpLossSelectedSlash(frame, selection);
        if (frame.NextActualUseAdjustment is { Kind: ProgramNextActualUseAdjustmentKind.AddSlashTarget })
            return BeginNextActualUseSelectedSlash(frame, selection);
        var target = _players.SingleOrDefault(player => player.Seat == targetSeat) ??
            throw new InvalidOperationException("The selected card-use target no longer exists.");
        var slashRule = GetEnabledSkillProgram(owner, frame.SkillId).ViewAs.FirstOrDefault(item => item.Id == viewAsId);
        if (!CanUseVirtualSlashTarget(owner, target, physicalSuit:ProgramMultiCardSuit(owner, selection), effectiveColor:ProgramMultiCardColor(owner, selection), physicalCardIds:selection.Cards.Select(c=>c.Id).ToArray(),
                ignoreDistance: slashRule is { DistanceUnlimited: true } || FriendXuanjianIgnoresDistance(owner, source)))            throw new InvalidOperationException("The selected Slash target is no longer legal.");
        ResolveSlashCore(
            owner,
            target,
            IsXieweiSelection(selection) ? ApplyProgramUseAppearance(owner, selection.Cards[0], selection.Source) : selection.Cards[0],
            outputKind,
            owner.Seat,
            physicalCards: selection.Cards,
            conversionSource: selection.Source);
        return SkillProgramStepOutcome.AwaitChild;
    }


    private void PendProgramExtraTurn(ProgramSkillFrame frame, int? targetSeat)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.OwnerSeat != frame.OwnerSeat || active.SkillId != frame.SkillId)
            throw new InvalidOperationException("An extra turn requires the active program frame.");
        var owner = _players[active.OwnerSeat];
        if (!owner.IsAlive || _winner != Winner.None)
            return;
        // The singular typed progression names the beneficiary. Without an
        // explicit target reference (Lianpo) the owner benefits; a later pend
        // overwrites an earlier one ("latest declaration wins").
        var beneficiarySeat = targetSeat ?? active.OwnerSeat;
        var beneficiary = _players[beneficiarySeat];
        if (!beneficiary.IsAlive)
            return;
        if (_turnProgression.OwnerSeat != _currentSeat || _turnProgression.TurnNumber != _turnNumber)
            throw new InvalidOperationException("An extra turn lost its actual turn progression.");
        _turnProgression = _turnProgression with { PendingBeneficiarySeat = beneficiarySeat };
        AddLog("ExtraTurnPended",
            beneficiarySeat == active.OwnerSeat
                ? $"{owner.Name} 将在当前回合结束后获得一个额外回合。"
                : $"{owner.Name} 令 {beneficiary.Name} 将在当前回合结束后获得一个额外回合。",
            active.OwnerSeat, beneficiarySeat);
        AdvanceEventRulesAndQueueFact(new ProgramExtraTurnPendedEvent(active.Id, active.SkillId, beneficiarySeat));
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
        ReplaceRuntimeTop(frame with
        {
            PendingMovementContinuation = new ProgramMovementContinuation(user.Seat, 0, null)
        });
        var reason = new CardMoveReason($"skill-program.{frame.SkillId}.{SkillProgramEffectOp.UseBoundCardByTarget}");
        var resolutionId = BeginCardUse(card, user.Seat, []);
        MoveCard(card, location, CardLocation.Processing, reason);
        CompleteEquipmentUse(user, card, resolutionId);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private static bool BoundEquipmentUseMatches(ProgramSkillFrame frame, SkillProgramEffect? effect,
        ProgramMovementContinuation pending, CardUseFrame use) =>
        effect is { Op: SkillProgramEffectOp.UseBoundCardByTarget } &&
        pending.BeforeCount == 0 && pending.CoverageResultBind is null && pending.SubjectSeat == use.SourceSeat &&
        (effect.Target == SkillProgramEffectTarget.Owner && use.SourceSeat == frame.OwnerSeat ||
         effect.Target == SkillProgramEffectTarget.SelectedTarget && frame.SelectedTargetSeats is [var target] && target == use.SourceSeat) &&
        frame.CardSetBindings.SingleOrDefault(binding => binding.Name == effect.SourceBind) is
            { Visibility: SkillProgramCardSetVisibility.Public, CardIds: [var id] } &&
        use.CardId == id && use.PhysicalCardIds is [var material] && material == id && EquipmentCatalog.IsEquipment(use.CardKind);

    private bool IsBoundEquipmentAwaitedMovement(ProgramSkillFrame frame, SkillProgramEffect? effect,
        ProgramMovementContinuation pending)
    {
        var index = _resolutionStack.FindIndex(item => item.Id == frame.Id);
        return index >= 0 && index + 1 < _resolutionStack.Count && _resolutionStack[index + 1] is CardUseFrame use &&
            BoundEquipmentUseMatches(frame, effect, pending, use);
    }

    [NativeUseReturn(150)]
    private void ContinueProgramAfterBoundEquipmentUse(CardUseFrame completed)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { PendingMovementContinuation: { } pending } frame) return;
        var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
        if (frame.InstructionIndex == 0 || plan.Instructions[frame.InstructionIndex - 1].Op != SkillProgramEffectOp.UseBoundCardByTarget) return;
        if (!BoundEquipmentUseMatches(frame, plan.Instructions[frame.InstructionIndex - 1], pending, completed))
            throw new InvalidOperationException("A bound equipment use lost its exact material, participant or owning instruction.");
        if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(frame.Id);
    }

    [NativeUseReturn(160)]
    private void ContinueAfterSelectedNativeUse(NativeUseReturnContext context)
    {
        var use = context.Use;
        if (use.ChosenAssault is null && !HasExactNextActualUseAdjustedProgramReturn(use) && !context.HpLossMaterialReturn)
            ContinueProgramAfterSelectedCardUse();
    }
    private void ContinueProgramAfterSelectedCardUse()
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.TriggerId is not null)
            return;
        var program = _contentRegistry.Skills.GetValueOrDefault(frame.SkillId)?.Program;
            if (frame.EquipmentSuitSourceReceipt is not null && !ValidateEquipmentSuitProgramSource(frame))
                throw new InvalidOperationException("An equipment-suit program lost its exact issued source receipt.");
        var activation = program is null ? null : ProgramInstructionResolver.Default.FindActivation(program, frame.ActivationId);
        if (activation is null || frame.InstructionIndex == 0 ||
            activation.Effects[frame.InstructionIndex - 1].Op is not
                (SkillProgramEffectOp.UseSelectedCardsAs or
                 SkillProgramEffectOp.UseAllHandCardsAsOrdinaryTrick))
            return;
        AdvanceRuntimeProgram(frame.Id);
    }

    private void CompleteProgramSkillAfterDying(DyingCompletionReceipt dying)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != dying.ParentFrameId)
            throw new InvalidOperationException("Program dying resolution lost its continuation.");
        AdvanceRuntimeProgram(frame.Id);
    }

    private static bool IsAwaitingProgramMovement(ProgramSkillFrame frame) =>
        frame.PendingMovementContinuation is not null ||
        frame.InspectedHandSlash is { Stage: InspectedHandSlashStage.HpChildren or InspectedHandSlashStage.DiscardChildren } ||
        frame.DiamondDelayed is { Stage: ProgramDiamondDelayedStage.Using } ||
        frame.SelectedCardPayment is not null && frame.SelectedCardPaymentResult is null;

    private void AssertProgramSkillState()
    {
        HasActualPhaseEndBoundaryFrame();
        AssertOriginalHandEntityState();
        AssertTurnDefaultStatState();
        AssertOffTurnUsedCardGiftObservers();
        AssertOrderedPrintedSkillLossState();
        AssertOrderedPrintedSkillLossObservers();
        AssertRecipientCategoryMarkState();
        AssertPendingAdjacentDiscardOrigins();
        AssertForeignContestAidPrograms();
        AssertPindianWinnerUses();
        foreach (var use in _resolutionStack.OfType<CardUseFrame>()) AssertOrdinaryTrickCannotNullify(use);
        AssertUniqueHpTargetAnnouncements();
        AssertCappedConversionBenefits();
        AssertTieredRoundConversionUses();
        AssertTieredRoundResponseUses();
        AssertRoundOnceCardlessNativeResponses();
        AssertRoundOnceCardlessProgramRequests();
        AssertHuangQuanNativeUses();
        foreach (var use in _resolutionStack.OfType<CardUseFrame>())
            if (use.RoundOnceCardlessBasicUse is not null && !IsRoundOnceCardlessBasicUse(use.Id))
                throw new InvalidOperationException("Cardless native use lost its exact independently paid source invoice.");
        foreach (var use in _resolutionStack.OfType<CardUseFrame>())
        { AssertCaoChunNativeUse(use); if (IsDenglouNativeUse(use)) AssertDenglouNativeUse(use); if (IsGuoSiNativeUse(use)) AssertGuoSiNativeUse(use); if (use.WangYunNativeOrigin is not null && !ValidWangYunNativeUse(use)) throw new InvalidOperationException("A Wang Yun native use lost its owning invoice."); if (use.ZhangJiNativeOrigin is not null && !ValidZhangJiNativeUse(use)) throw new InvalidOperationException("A Zhang Ji native use lost its owning invoice."); AssertHistoricalEndingUse(use); AssertRoundPileAlcoholUse(use); AssertCurrentSlashFirePolicy(use); AssertComparedBlackSlashReceipt(use); AssertRoundDistinctBasicUses(use); AssertLeiluanPayments(use); }
        var frames = _resolutionStack.OfType<ProgramSkillFrame>().ToArray();
        foreach (var frame in frames)
        {
            var program = _contentRegistry.Skills.GetValueOrDefault(frame.SkillId)?.Program;
            if (frame.EquipmentSuitSourceReceipt is not null && !ValidateEquipmentSuitProgramSource(frame))
                throw new InvalidOperationException("An equipment-suit program lost its exact issued source receipt.");
            if (frame.HpConditionalSourceReceipt is not null && !CanContinueHpConditionalProgram(frame))
                throw new InvalidOperationException("An HP-conditional program lost its exact issued source receipt.");
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
            RuntimeHooks.AssertFrame(ProgramFrameInvariantPhase.BeforeSelectionState, frame, plan);
            if (frame.InstructionIndex < 1 || frame.InstructionIndex > plan.Instructions.Count ||
                frame.SelectedCardIds.Distinct().Count() != frame.SelectedCardIds.Count ||
                frame.SelectedTargetSeats.Any(seat => !IsValidPlayerSeat(seat)) ||
                frame.CardSetBindings.Select(binding => binding.Name).Distinct(StringComparer.Ordinal).Count() !=
                    frame.CardSetBindings.Count ||
                frame.CardSetBindings.Any(binding =>
                    binding.SelectionActorSeat is { } selectionActor &&
                    (!HasForeignDiscardCapability && !TracksNativeLogicalDiscards || !IsValidPlayerSeat(selectionActor) ||
                     !plan.Instructions.Take(frame.InstructionIndex).Any(e => e.ResultBind == binding.Name &&
                        (e.Op is SkillProgramEffectOp.SelectOwnedCards or SkillProgramEffectOp.SelectOwnedHandRankSum or SkillProgramEffectOp.SelectSourceCard or SkillProgramEffectOp.SelectCardSubset or SkillProgramEffectOp.FilterBoundCards or SkillProgramEffectOp.SelectTurnDamageUseDebtPayment or SkillProgramEffectOp.SelectEquipmentPairAndPayment or SkillProgramEffectOp.SelectDyingOwnedCard ||
                         e.Op == SkillProgramEffectOp.SelectFrozenHandExchangeDebtPayment &&
                         selectionActor == frame.OwnerSeat && frame.PhaseHandDebtPayment is { } debt &&
                         debt.ResultBind == binding.Name && binding.CardIds.Count == debt.RequiredPaymentCount &&
                         binding.SourceLocations.All(location => location.OwnerSeat == frame.OwnerSeat &&
                             location.Zone is CardZoneKind.Hand or CardZoneKind.Equipment) && ValidPhaseHandDebtPayment(frame)))) ||
                    binding.CardIds.Count != binding.SourceLocations.Count ||
                    binding.CardIds.Distinct().Count() != binding.CardIds.Count ||
                    plan.Instructions.Any(instruction => instruction is { FreezeMovedCardSuit: true } frozen && frozen.ResultBind == binding.Name) &&
                    (binding.CardIds.Count != 1 || binding.FrozenRevealedSuit is not { } frozenSuit || !Enum.IsDefined(frozenSuit))) ||
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
            if (frame.TopReorder?.RequiredTopCount is not null) ValidateExactTopReorder(frame);
            if (frame.TopReorder?.Population is not null) ValidatePopulationTopReorder(frame);
            if(frame.DomainCrossing is {} domain &&
                (plan.Instructions[frame.InstructionIndex-1].Op!=SkillProgramEffectOp.ResolveFirstGameDomainCrossing ||
                 frame.WindowContext?.Window!=SkillProgramTriggerWindow.FirstGameDomainCrossing ||
                 domain.Stage is not ("target" or "participants" or "discard" or "heal" or "complete") ||
                 domain.ReferenceSkillId!=plan.Instructions[frame.InstructionIndex-1].SkillIds.Single() ||
                 domain.ReferenceSkillInstanceId is not null&&string.IsNullOrWhiteSpace(domain.ReferenceSkillInstanceId) ||
                 domain.ReferencePileLocation is {} referencedLocation&&(domain.ReferenceSkillInstanceId is null||referencedLocation.Zone!=CardZoneKind.PublicPersistentPile||referencedLocation.OwnerSeat!=frame.OwnerSeat) ||
                 domain.ParticipantIndex is <0 or >2 || domain.OtherSeat!=-1&&(!IsValidPlayerSeat(domain.OtherSeat)||domain.OtherSeat==frame.OwnerSeat)))
                throw new InvalidOperationException("A first-domain reward lost its typed participant cursor.");
            if(frame.PileEquipment is {} equipment &&
                (plan.Instructions[frame.InstructionIndex-1].Op is not (SkillProgramEffectOp.StoreArbitraryOwnedPublicPile or SkillProgramEffectOp.UsePublicPileEquipmentSequence) ||
                 equipment.PileLocation.Zone!=CardZoneKind.PublicPersistentPile || equipment.PileLocation.OwnerSeat!=frame.OwnerSeat ||
                 string.IsNullOrWhiteSpace(equipment.PileInstance) || equipment.SelectedIds.Distinct().Count()!=equipment.SelectedIds.Count ||
                 equipment.RecipientSeat is {} recipient&&!IsValidPlayerSeat(recipient) ||
                 equipment.Stage is not ("store-choice" or "gain" or "recipient" or "equip-choice" or "equipment-use" or "hp-paid" or "complete") ||
                 equipment.Stage=="hp-paid"&&!equipment.HadActualEquipmentUse ||
                 equipment.Stage=="equipment-use"&&(equipment.ActiveCardId is null||equipment.ActiveUseFrameId is null||equipment.RecipientSeat is null)))
                throw new InvalidOperationException("A public-pile equipment sequence lost its typed paid-use cursor.");
            if (frame.PublicPileColorPayment is { } colorPayment &&
                (plan.Instructions[frame.InstructionIndex - 1].Op != SkillProgramEffectOp.PublicPileColorDamage ||
                 frame.ConversionPreviousPolarity != SkillPolarity.Yang ||
                 colorPayment.From.OwnerSeat != frame.OwnerSeat ||
                 colorPayment.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
                 colorPayment.PileLocation.Zone != CardZoneKind.PublicPersistentPile ||
                 colorPayment.PileLocation.OwnerSeat != frame.OwnerSeat ||
                 string.IsNullOrWhiteSpace(colorPayment.PileInstance) ||
                 !IsValidPlayerSeat(colorPayment.TargetSeat) || colorPayment.TargetSeat == frame.OwnerSeat))
                throw new InvalidOperationException("A public-pile color payment lost its owning paid cursor.");
            if (frame.SelectedCardPayment is { } selectedPayment)
            {
                if (selectedPayment.InstructionIndex < 1 || selectedPayment.InstructionIndex > frame.InstructionIndex ||
                    plan.Instructions[selectedPayment.InstructionIndex - 1].Op != selectedPayment.Operation ||
                    selectedPayment.Operation is not (SkillProgramEffectOp.GiveSelected or SkillProgramEffectOp.DiscardSelected or SkillProgramEffectOp.PlaceSelectedEquipment) ||
                    !selectedPayment.CardIds.SequenceEqual(frame.SelectedCardIds) ||
                    !IsValidPlayerSeat(selectedPayment.RecipientSeat) ||
                    selectedPayment.Operation == SkillProgramEffectOp.DiscardSelected &&
                    selectedPayment.RecipientSeat != frame.OwnerSeat ||
                    frame.SelectedCardPaymentResult is null && frame.PendingMovementContinuation is not null ||
                    !selectedPayment.MovementCommitted &&
                    (selectedPayment.ActiveChildFrameId is not null || frame.SelectedCardPaymentResult is not null) ||
                    frame.SelectedCardPaymentResult is { } result &&
                    (result.InstructionIndex != selectedPayment.InstructionIndex ||
                     result.Operation != selectedPayment.Operation || !result.Completed ||
                     result.LastChildFrameId != selectedPayment.LastCompletedChildFrameId ||
                     selectedPayment.ActiveChildFrameId is not null))
                    throw new InvalidOperationException("A selected-card payment lost its paid instruction or child result.");
            }
            else if (frame.SelectedCardPaymentResult is not null)
                throw new InvalidOperationException("A selected-card movement result has no paid instruction.");
            RuntimeHooks.AssertFrame(ProgramFrameInvariantPhase.AfterSelectionState, frame, plan);
            if (frame.PendingMovementContinuation is { } pendingMovement)
            {
                var paidEffect = frame.InstructionIndex > 0
                    ? plan.Instructions[frame.InstructionIndex - 1]
                    : null;
                var awaitsSuitPlacement = pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.BeforeCount == 0 && pendingMovement.CoverageResultBind is null &&
                    (paidEffect?.Op == SkillProgramEffectOp.DiscardOwnedCardToAdjustCurrentDamage && frame.SignedDamagePayment is { Stage: not SignedDamagePaymentStage.ChoosingCost } ||
                     paidEffect?.Op == SkillProgramEffectOp.DiscardSuitPreventDamageAndBenefit && frame.SuitPreventionBenefit is not null ||
                     paidEffect?.Op == SkillProgramEffectOp.PlaceMatchedJudgmentCard && frame.MatchedJudgmentPlacement is not null);
                var awaitsShownGift = paidEffect?.Op == SkillProgramEffectOp.GiveShownBoundCardsAndGrantTurnHandLimit &&
                    frame.ShownGiftReceipt?.InstructionIndex == frame.InstructionIndex - 1 &&
                    pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null;
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
                var awaitsLostHpOwnedGift = paidEffect?.Op == SkillProgramEffectOp.DrawLostHpThenOfferOwnedCardsUpTo &&
                    frame.ReexecuteParticipantInstruction &&
                    frame.LostHpOwnedGift is { Stage: LostHpOwnedGiftStage.Drawing or LostHpOwnedGiftStage.Moving } &&
                    pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.BeforeCount == 0 &&
                    pendingMovement.CoverageResultBind is null && LostHpOwnedGiftObserverRoot()?.Id == frame.Id;
                var awaitsOwnedMovement = paidEffect is
                {
                    Op: SkillProgramEffectOp.SelectAndMoveOwnedCard,
                    AwaitMovementTriggers: true
                } &&
                    paidEffect.CoverageResultBind == pendingMovement.CoverageResultBind;
                // A hand-exchange effect suspends its program behind the nested
                // cards-moved trigger window opened by the exchanged cards.
                var awaitsExchangedMovement =
                    paidEffect?.Op is (SkillProgramEffectOp.ExchangeSelectedTargetHands or SkillProgramEffectOp.ExchangeSelectedTargetEquipment) &&
                    pendingMovement.CoverageResultBind is null &&
                    pendingMovement.SubjectSeat == frame.OwnerSeat;
                var awaitsConvertingGift = paidEffect?.Op is (SkillProgramEffectOp.GiveOwnedCardToOtherFinalTargetAndDraw or SkillProgramEffectOp.GiveSelectedOwnedCardAndDamage or SkillProgramEffectOp.ObserveDamageSourceHandAndGive or SkillProgramEffectOp.DrawToHandCount) && pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null;
                var awaitsQuotaTop = paidEffect?.Op == SkillProgramEffectOp.PeekTurnQuotaTop &&
                    frame.QuotaTop is { Stage: "gain-movement" } &&
                    pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null;
                var awaitsBudgetOrGift = paidEffect?.Op is (SkillProgramEffectOp.ResolveDiscardBudgetParticipants or
                    SkillProgramEffectOp.DiscardOutsideRangeAfterInsufficientUses or SkillProgramEffectOp.OfferCompletedFactionCostGift) &&
                    pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null;
                var awaitsAlternatingSuitTop = paidEffect?.Op is SkillProgramEffectOp.AlternatingSuitDrawDiscard or SkillProgramEffectOp.FirstCategoryCompletedTop && frame.AlternatingSuitTop is { Stage: "draw" or "discard-movement" or "top-movement" or "reward-draw" } && pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null;
                var awaitsNamedTurnFlow = paidEffect?.Op is SkillProgramEffectOp.DiscardNonFinalTargetCardThenDraw or SkillProgramEffectOp.DiscardHandToNamedTurnCount && frame.NamedTurnCountFlow is { Stage: "target-movement" or "target-draw" or "hand-movement" } && pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null;
                var awaitsFinalTargetPublicPile = paidEffect?.Op is SkillProgramEffectOp.CollectFinalTargetCardInPublicPile or SkillProgramEffectOp.ExchangePublicPileHand or SkillProgramEffectOp.ObtainPublicPileCard or SkillProgramEffectOp.DiscardPublicZoneAfterHandPayment && pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null;
                var awaitsPrivateHold = paidEffect?.Op == SkillProgramEffectOp.HoldOwnerHandUntilTurnEnd && frame.PrivateTurnHoldDraft is {Paid:true} hold &&
                    hold.Location.PrivateTurnHold?.HoldId == frame.Id && hold.Location.OwnerSeat == frame.OwnerSeat &&
                    hold.Location.PrivateTurnHold.SkillId == frame.SkillId && hold.Location.PrivateTurnHold.SkillInstanceId == frame.SkillInstanceId &&
                    pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null;
                var awaitsAppliedDamageBenefit = paidEffect?.Op == SkillProgramEffectOp.DrawOwnerAtAppliedDamage &&
                    frame.AppliedDamageBenefit is { } appliedBenefit && appliedBenefit.InstructionIndex == frame.InstructionIndex &&
                    pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null;
                var awaitsParticipantDiscard = paidEffect?.Op==SkillProgramEffectOp.DiscardDistinctFactionParticipants && frame.DistinctFactionDiscards is {Stage:"discard-children" or "reward-children"} && pendingMovement.SubjectSeat==frame.OwnerSeat && pendingMovement.CoverageResultBind is null;
                var awaitsDeclaredDeck = paidEffect?.Op == SkillProgramEffectOp.DeclareDeckCriterionAndGiveMatchingCard &&
                    frame.DeckCriterion is { Stage: "search" or "recipient" or "gift" } && pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null;
                var awaitsCappedHandRefresh = paidEffect?.Op == SkillProgramEffectOp.DrawThenDiscardHandToMaximumHp &&
                    frame.CappedHandRefresh is { Stage: ProgramCappedHandRefreshStage.Drawing or ProgramCappedHandRefreshStage.Discarding } refresh &&
                    pendingMovement.SubjectSeat == refresh.TargetSeat && pendingMovement.CoverageResultBind is null;
                if (!(paidEffect?.Op == SkillProgramEffectOp.SuppressCurrentSlashTargetAndJudgeSuitDiscard && frame.SlashSuitDiscard is { Stage: ProgramSlashSuitDiscardStage.JudgmentMovement or ProgramSlashSuitDiscardStage.PaymentMovement } && pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null) && !(paidEffect?.Op == SkillProgramEffectOp.UseDiamondDelayedOrDiscard && frame.DiamondDelayed is { Stage: ProgramDiamondDelayedStage.Moving or ProgramDiamondDelayedStage.Drawing } && pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null) && !(awaitsParticipantDiscard || awaitsPrivateHold || IsUnequalHandHpMovement(frame, paidEffect, pendingMovement) || IsPhaseStartCardControlMovement(frame, paidEffect, pendingMovement) || IsEndRefillNextTurnMovement(frame, paidEffect, pendingMovement) || IsDeathGrowthMovement(frame, paidEffect, pendingMovement) || IsBaoMovement(frame, paidEffect, pendingMovement) || IsDongZhaoMovement(frame, paidEffect, pendingMovement) || IsCaoYingMovement(frame, paidEffect, pendingMovement) || IsPrivateWhirlMovement(frame, paidEffect, pendingMovement) || IsBuZhiMovement(frame, paidEffect, pendingMovement) || IsLianZhuMovement(frame, paidEffect, pendingMovement) || IsJiqiaoMovement(frame, paidEffect, pendingMovement) || IsXiashuMovement(frame, paidEffect, pendingMovement) || IsWangYunMovement(frame, paidEffect, pendingMovement) || IsZhugeGuoMovement(frame, paidEffect, pendingMovement) || IsXiZhiCaiMovement(frame, paidEffect, pendingMovement) || IsLiuQiMovement(frame, paidEffect, pendingMovement) || IsBeiMiHuMovement(frame, paidEffect, pendingMovement) || IsSimaHuiMovement(frame, paidEffect, pendingMovement) || IsXuRongMovement(frame, paidEffect, pendingMovement) || IsLiJueMovement(frame, paidEffect, pendingMovement) || IsZhangJiMovement(frame, paidEffect, pendingMovement) || IsSuFeiMovement(frame, paidEffect, pendingMovement) || IsHuangQuanMovement(frame, paidEffect, pendingMovement) || IsFanChouMovement(frame, paidEffect, pendingMovement) || IsGuoSiMovement(frame, paidEffect, pendingMovement) || IsZhangQiYingMovement(frame, paidEffect, pendingMovement) || IsCaoChunMovement(frame, paidEffect, pendingMovement) || IsWangCanMovement(frame, paidEffect, pendingMovement) || IsSpPangTongMovement(frame, paidEffect, pendingMovement) || IsNeifaMovement(frame, paidEffect, pendingMovement) || IsLvQianMovement(frame, paidEffect, pendingMovement) || IsLvKuangLvXiangMovement(frame, paidEffect, pendingMovement) || IsXinPiMovement(frame, paidEffect, pendingMovement) || IsHuaManMovement(frame, paidEffect, pendingMovement) || IsLuZhiWeiMovement(frame, paidEffect, pendingMovement) || IsHeQiMovement(frame, paidEffect, pendingMovement) || IsZhaoXiangMovement(frame, paidEffect, pendingMovement) || IsQimeiMovement(frame, paidEffect, pendingMovement) || IsZhuijiMovement(frame, paidEffect, pendingMovement) || IsFirstSlashDuelMovement(frame, paidEffect, pendingMovement) || IsExtraEffectsAndRankGuessMovement(frame, paidEffect, pendingMovement) || IsIncomingDistanceCourtesyMovement(frame, paidEffect, pendingMovement) || paidEffect?.Op is SkillProgramEffectOp.ResolveFirstGameDomainCrossing or SkillProgramEffectOp.StoreArbitraryOwnedPublicPile or SkillProgramEffectOp.UsePublicPileEquipmentSequence && pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null) && !awaitsAppliedDamageBenefit && !awaitsNamedTurnFlow && !awaitsConvertingGift && !(paidEffect?.Op is SkillProgramEffectOp.AwaitOwnedCardMovement or SkillProgramEffectOp.StoreBoundHandInPublicPile or SkillProgramEffectOp.PublicPileColorDamage or SkillProgramEffectOp.RewardDiscardedActionColor && pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null) && !awaitsAlternatingSuitTop && !awaitsFinalTargetPublicPile && !awaitsQuotaTop && !awaitsBudgetOrGift && !(paidEffect?.Op == SkillProgramEffectOp.DrawOnFirstProgramTargetEncounter && pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null) && !(paidEffect?.Op is SkillProgramEffectOp.ExchangeOwnedCardThroughDeckEnd or SkillProgramEffectOp.UseDeckSlashesThenShuffle && pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null) && !(paidEffect?.Op is SkillProgramEffectOp.ExchangeRespondedCardEntities or SkillProgramEffectOp.DrawPublicSuitThenEscalatingDiscard && pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null) && !(paidEffect?.Op == SkillProgramEffectOp.CompareSelectedHandWithHpHand && pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null) && !(paidEffect?.Op is SkillProgramEffectOp.DeclareNameForTargetDefense or SkillProgramEffectOp.DrawAndDraftLowHandPopulation or SkillProgramEffectOp.ObtainDeckCardWithConsecutiveTarget && pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null) && !(paidEffect?.Op == SkillProgramEffectOp.ChooseDifferentActionCategoryGift && pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null) && !(paidEffect?.Op is SkillProgramEffectOp.DepositBoundCardsUntilNextTurn or SkillProgramEffectOp.ObtainDeferredPile or SkillProgramEffectOp.ViewTopCardsAndObtainMatchingCards or SkillProgramEffectOp.StoreTopCardInPublicPile or SkillProgramEffectOp.ExchangePublicPile or SkillProgramEffectOp.DistributePublicPileIfAllSuits && pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null) && !awaitsRandomTransfer &&
                    !(paidEffect?.Op == SkillProgramEffectOp.ObtainDamageTargetCardAndResolveCategory && frame.DamageTargetObtain is not null && pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null) &&
                    !(paidEffect?.Op == SkillProgramEffectOp.DiscardDamageTargetAndClaimMount && frame.DamageTargetMount is { Receipt: not null } && pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null) &&
                    !(frame.FriendEventFlow is { } friendFlow && paidEffect?.Op == friendFlow.Operation && pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null) &&
                    !IsLiejieAwaitedMovement(frame, paidEffect, pendingMovement) &&
                    !IsNianRuizhiMovement(frame, paidEffect, pendingMovement) &&
                    !awaitsSuitPlacement && !awaitsCappedHandRefresh && !awaitsDeclaredDeck && !awaitsShownGift && !awaitsJudgmentClaim && !awaitsRepeatedJudgment && !awaitsOwnedMovement && !awaitsLostHpOwnedGift &&
                    !(paidEffect?.Op == SkillProgramEffectOp.MouZhijueUse && frame.MouZhijue is not null && pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null) && !IsPhaseHandSeizureMovement(frame, paidEffect, pendingMovement) && !IsPublicPilePreparationMovement(frame, paidEffect, pendingMovement) && !IsSourceCurseMovement(frame, paidEffect, pendingMovement) && !IsSelectedForeignCardSlashMovement(frame, paidEffect, pendingMovement) && !IsKuangfuMovement(frame, paidEffect, pendingMovement) && !IsGainGiftMovement(frame, paidEffect, pendingMovement) && !IsPhaseNamePredictionMovement(frame, paidEffect, pendingMovement) && !IsFriendTrioMovement(frame, paidEffect, pendingMovement) && !IsFriendPangTongMovement(frame, paidEffect, pendingMovement) && !IsLastDamageSourceReciprocityMovement(frame, paidEffect, pendingMovement) && !IsPairedColorDispositionMovement(frame, paidEffect, pendingMovement) && !IsJoinedTrickDamageRewardAwaitedMovement(frame, paidEffect, pendingMovement) && !IsRoundGainedEquipmentDrawAwaitedMovement(frame, paidEffect, pendingMovement) && !IsSameNameHandDiscardOrDamageMovement(frame, paidEffect, pendingMovement) && !IsCompletedUndamagedTargetRevealMovement(frame, paidEffect, pendingMovement) && !IsRecipientCategoryMarkMovement(frame, paidEffect, pendingMovement) && !IsOffTurnUsedCardGiftMovement(frame, paidEffect, pendingMovement) && !IsOrderedPrintedSkillLossMovement(frame, paidEffect, pendingMovement) && !IsOverflowTargetCancellationAwaitedMovement(frame, paidEffect, pendingMovement) && !IsOriginalHandEntityMovement(frame, paidEffect, pendingMovement) && !IsOutsidePhaseDrawDiscardMovement(frame, paidEffect, pendingMovement) && !IsMatchingRecastMovement(frame, paidEffect, pendingMovement) && !IsPairedHandRecastMovement(frame, paidEffect, pendingMovement) && !IsFixedDistanceDebtMovement(frame, paidEffect, pendingMovement) && !IsActualHandGainMovement(frame, paidEffect, pendingMovement) && !IsOldHandDispositionMovement(frame, paidEffect, pendingMovement) && !IsFrozenHandRosterMovement(frame, paidEffect, pendingMovement) && !IsPrivateOfferMovement(frame, paidEffect, pendingMovement) && !IsGameHandHpMovement(frame, paidEffect, pendingMovement) && !IsDyingSuitsMovement(frame, paidEffect, pendingMovement) && !IsFireTargetBenefitMovement(frame, paidEffect, pendingMovement) && !IsNamedCardAcquisitionMovement(frame, paidEffect, pendingMovement) && !IsCappedConversionAwaitedMovement(frame, pendingMovement) && !IsForeignContestOrAidMovement(frame, paidEffect, pendingMovement) && !IsPlacedEquipmentBenefitMovement(frame, paidEffect, pendingMovement) && !IsDynamicDiscardDamageMovement(frame, paidEffect, pendingMovement) && !IsConditionalDuelAwaitedMovement(frame, paidEffect, pendingMovement) && !IsInspectedHandMovement(frame, paidEffect, pendingMovement) && !IsRecipientContestMovement(frame, paidEffect, pendingMovement) && !IsRecipientConsequencesMovement(frame, paidEffect, pendingMovement) && !IsPrepDiscardMovement(frame, paidEffect, pendingMovement) && !IsSlashTargetBenefitMovement(frame, paidEffect, pendingMovement) && !IsTargetPenaltyMovement(frame, paidEffect, pendingMovement) && !IsActualEquipmentOrDiscardMovement(frame, paidEffect, pendingMovement) && !IsPaidColorDamageClaimMovement(frame, paidEffect, pendingMovement) && !(IsEquipmentDonationMovement(frame, paidEffect, pendingMovement) || (IsOrdinaryCaiFuRenMovement(frame, paidEffect, pendingMovement) || (IsOrdinaryGuYongMovement(frame, paidEffect, pendingMovement) || (IsHuanShuJunMovement(frame, paidEffect, pendingMovement) || (IsZhaoZhongMovement(frame, paidEffect, pendingMovement) || (IsTaoQianMovement(frame, paidEffect, pendingMovement) || (IsPangJiMovement(frame, paidEffect, pendingMovement) || (IsLeMiHengMovement(frame, paidEffect, pendingMovement) || (IsLeZhouFeiXianshuMovement(frame, paidEffect, pendingMovement) || (((IsWeiwanMovement(frame, paidEffect, pendingMovement) || IsZixiEffectsMovement(frame, paidEffect, pendingMovement)) || IsWuYixianMovement(frame, paidEffect, pendingMovement)) || ((IsQiqinMovement(frame, paidEffect, pendingMovement) || (IsGuYinMovement(frame, paidEffect, pendingMovement) || (IsMoQuanyuMovement(frame, paidEffect, pendingMovement) || IsMoTianenMovement(frame, paidEffect, pendingMovement) || IsMoQiangangMovement(frame, paidEffect, pendingMovement)))) || IsZixiPlacementMovement(frame, paidEffect, pendingMovement)))))))))))) && !IsHalfHandPhaseMovement(frame, paidEffect, pendingMovement) && !IsPairBenefitMovement(frame, paidEffect, pendingMovement) && !IsEndingPairMovement(frame, paidEffect, pendingMovement) && !IsExtraDrawDebtMovement(frame, paidEffect, pendingMovement) && !IsDrawDiscardCategoryMovement(frame, paidEffect, pendingMovement) && !IsOwnTrickDrawMovement(frame, paidEffect, pendingMovement) && !IsPreventionDrawMovement(frame, paidEffect, pendingMovement) && !IsDamageJudgmentSuitPaymentMovement(frame, paidEffect, pendingMovement) && !awaitsExchangedMovement && !(paidEffect?.Op == SkillProgramEffectOp.ResolveDeferredHandAlignment && frame.DeferredHandAlignmentResolution is { } deferredAlignment && pendingMovement.SubjectSeat == deferredAlignment.TargetSeat && pendingMovement.CoverageResultBind is null) && !(paidEffect?.Op == SkillProgramEffectOp.UseRandomDeckEquipment && frame.SelectedTargetSeats is [var equipmentUser] && pendingMovement.SubjectSeat == equipmentUser && pendingMovement.CoverageResultBind is null) && !(paidEffect?.Op is SkillProgramEffectOp.DiscardSelectedParticipantCards or SkillProgramEffectOp.OfferBoundCardsForDamagePrevention && pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null) && !(paidEffect?.Op == SkillProgramEffectOp.TakeSelectedTargetCards && pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null) &&
                    !(paidEffect is { Op: SkillProgramEffectOp.MoveBoundCards, AwaitMovementTriggers: true } && pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.CoverageResultBind is null) &&
                    !(paidEffect?.Op == SkillProgramEffectOp.ObtainBoundCardsAndArmNextRevealBonus &&
                        pendingMovement.SubjectSeat == frame.OwnerSeat && pendingMovement.BeforeCount == 0 && pendingMovement.CoverageResultBind is null) &&
                    !(paidEffect?.Op is SkillProgramEffectOp.ChooseCategoryAlternativeDiscard or SkillProgramEffectOp.EscalatingDiscardOrDamage && frame.DiscardChallenge is { } challenge && challenge.ChooserSeat == pendingMovement.SubjectSeat && pendingMovement.CoverageResultBind is null) &&
                    !(paidEffect is { } sequentialEffect && IsSequentialDiscardOp(sequentialEffect.Op) &&
                        frame.SequentialDiscard is { Stage: ProgramSequentialDiscardStage.AwaitingMovement, Payment: { } sequentialPayment } &&
                        ValidSequentialDiscard(frame) && sequentialPayment.ChooserSeat == pendingMovement.SubjectSeat &&
                        pendingMovement.BeforeCount == 0 && pendingMovement.CoverageResultBind is null) &&
                    !IsBoundEquipmentAwaitedMovement(frame, paidEffect, pendingMovement) &&
                    !(paidEffect is { } exchangeEffect && IsCurrentTurnHandExchangeMovement(frame, exchangeEffect, pendingMovement)) &&
                    !IsPublicPileCashOutMovement(frame, paidEffect, pendingMovement) &&
                    !IsGiftedSlashAwaitedMovement(frame, paidEffect, pendingMovement) &&
                    !IsNamedHandChoiceMovement(frame, paidEffect, pendingMovement) &&
                    !IsRiceMovement(frame, paidEffect, pendingMovement) &&
                    !IsDamageAcquisitionMovement(frame, paidEffect, pendingMovement) &&
                    !IsSimultaneousPindianMovement(frame,paidEffect,pendingMovement) &&
                    !IsCategoryTalismanMovement(frame, paidEffect, pendingMovement) &&
                    !IsEquipmentSlotNamedUseMovement(frame, paidEffect, pendingMovement) &&
                    !IsFuRongAwaitedMovement(frame, paidEffect, pendingMovement) &&
                    !IsXunyiMovement(frame, paidEffect, pendingMovement) &&
                    !IsDailiMovement(frame, paidEffect, pendingMovement) &&
                    !IsZhenyingMovement(frame, paidEffect, pendingMovement) &&
                    !IsDuFuRenAwaitedMovement(frame, paidEffect, pendingMovement) &&
                    !IsFeiYiAwaitedMovement(frame, paidEffect, pendingMovement) &&
                    !IsQianZhaoMovement(frame, paidEffect, pendingMovement) &&
                    !IsDingShangWanAwaitedMovement(frame, paidEffect, pendingMovement) &&
                    !IsLiWanMovement(frame, paidEffect, pendingMovement) &&
                    !IsYanRouAwaitedMovement(frame, paidEffect, pendingMovement) &&
                    !IsHuJinDingMovement(frame, paidEffect, pendingMovement) &&
                    !IsLuYuShengAwaitedMovement(frame, paidEffect, pendingMovement) &&
                    !IsYuanHuanMovement(frame, paidEffect, pendingMovement) &&
                    !IsHanFuMovement(frame, paidEffect, pendingMovement) &&
                    !IsZhangYanMovement(frame, paidEffect, pendingMovement) &&
                    !IsZhuJunMovement(frame, paidEffect, pendingMovement) &&
                    !IsNiuFuMovement(frame, paidEffect, pendingMovement) &&
                    !IsRuanHuiMovement(frame, paidEffect, pendingMovement) &&
                    !IsChengGongYingMovement(frame, paidEffect, pendingMovement) &&
                    !IsMengDaMovement(frame, paidEffect, pendingMovement) &&
                    !IsYaDanMovement(frame, paidEffect, pendingMovement) &&
                    !IsYangFuMovement(frame, paidEffect, pendingMovement) &&
                    !IsSunHaoMovement(frame, paidEffect, pendingMovement) &&
                    !IsMaYuanYiMovement(frame, paidEffect, pendingMovement) &&
                    !IsHuBanMovement(frame, paidEffect, pendingMovement) &&
                    !IsLiuBaMovement(frame, paidEffect, pendingMovement) && !IsLiuPanMovement(frame, paidEffect, pendingMovement) && !IsQuHuangMovement(frame, paidEffect, pendingMovement) && !IsCaoYuMovement(frame, paidEffect, pendingMovement) && !IsRuanYuMovement(frame, paidEffect, pendingMovement) && !IsBaoXinMovement(frame, paidEffect, pendingMovement) && !IsWeijieMovement(frame, paidEffect, pendingMovement) && !IsQushiMovement(frame, paidEffect, pendingMovement) && !IsCuiYanMovement(frame, paidEffect, pendingMovement) && !IsLiYiMovement(frame, paidEffect, pendingMovement) && !IsSunYuanMovement(frame, paidEffect, pendingMovement) && !(IsLiuZhangMovement(frame, paidEffect, pendingMovement) || IsTangTangMovement(frame, paidEffect, pendingMovement) || (IsYuanJiMovement(frame, paidEffect, pendingMovement) || IsKongShuMovement(frame, paidEffect, pendingMovement) || (IsMaChengMovement(frame, paidEffect, pendingMovement) || (IsSunHongMovement(frame, paidEffect, pendingMovement) || (IsZhouQunMovement(frame, paidEffect, pendingMovement) || (IsGuanHaiMovement(frame, paidEffect, pendingMovement) || (IsXieLingYuMovement(frame, paidEffect, pendingMovement) || IsLiFengMovement(frame, paidEffect, pendingMovement)))))))) &&
                    !IsMouCaoPiMovement(frame, paidEffect, pendingMovement) &&
                    !IsKenshangMovement(frame, paidEffect, pendingMovement) && !IsFulueMovement(frame, paidEffect, pendingMovement) && !IsYangFengMovement(frame, paidEffect, pendingMovement) && !IsWangGuanMovement(frame, paidEffect, pendingMovement) && !IsWuJingMovement(frame, paidEffect, pendingMovement) &&
                    !IsMaMiDiMovement(frame, paidEffect, pendingMovement) &&
                    !IsBeifenMovement(frame, paidEffect, pendingMovement) &&
                    !IsWuyouMovement(frame, paidEffect, pendingMovement) &&
                    !IsPingluMovement(frame, paidEffect, pendingMovement) &&
                    !IsYanPuAwaitedMovement(frame, paidEffect, pendingMovement) &&
                    !IsDrawAdviceAwaitedMovement(frame, paidEffect, pendingMovement))
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

            if (plan.Instructions.Take(frame.InstructionIndex).Any(effect =>
                    effect is { Op: SkillProgramEffectOp.SelectTarget, Condition.Kind: SkillProgramConditionKind.ChoiceIs } &&
                    !frame.ChoiceBindings.Any(binding => binding.Name == effect.Condition.SourceBind)))
                throw new InvalidOperationException("A choice-guarded target selection lost its committed choice.");
            var executedSelection = plan.Instructions.Take(frame.InstructionIndex)
                .LastOrDefault(effect =>
                    (effect.Op != SkillProgramEffectOp.SelectTarget || effect.Condition.Kind != SkillProgramConditionKind.ChoiceIs ||
                     frame.ChoiceBindings.Any(binding => binding.Name == effect.Condition.SourceBind &&
                         binding.OptionId == effect.Condition.OptionId)) &&
                    effect.Op is (
                    SkillProgramEffectOp.SelectRelativeZoneDemandTarget or SkillProgramEffectOp.SelectTarget or SkillProgramEffectOp.SelectTargets or SkillProgramEffectOp.SelectAllPublicTeamTargets or SkillProgramEffectOp.SelectRandomPublicTeamTarget or
                    SkillProgramEffectOp.SelectChainedByMarker or SkillProgramEffectOp.SelectOneSelectedTarget or
                    SkillProgramEffectOp.DamageFarthestCharacter or SkillProgramEffectOp.OfferCompletedCardGift or
                    SkillProgramEffectOp.RequestLegalSlashByNearest or SkillProgramEffectOp.OfferUnlimitedVirtualSlash or
                    SkillProgramEffectOp.SelectIssuedFixedRecipientWithDeathReturn or SkillProgramEffectOp.SelectEquipmentPairAndPayment or
                    SkillProgramEffectOp.SelectDyingOwnedCard or SkillProgramEffectOp.RequestFulueZeroDamageCounterSlash));
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
                        : executedSelection.Op == SkillProgramEffectOp.SelectRelativeZoneDemandTarget ? "relative-zone-target"
                        : executedSelection.Op == SkillProgramEffectOp.SelectEquipmentPairAndPayment ? "equipment-pair-payment"
                        : executedSelection.Op == SkillProgramEffectOp.SelectDyingOwnedCard ? "dying-owned-card"
                        : executedSelection.Op == SkillProgramEffectOp.SelectTargets ? "select-targets" :
                            executedSelection.Op == SkillProgramEffectOp.DamageFarthestCharacter ? "advanced-lifecycle" : "strategic-choice"));
            var dynamicTargetsValid = executedSelection is null || executedSelection.Op switch
            {
                SkillProgramEffectOp.SelectAllPublicTeamTargets or SkillProgramEffectOp.SelectRandomPublicTeamTarget => IsExactPublicTeamTargetSelection(frame, executedSelection, plan),
                SkillProgramEffectOp.RequestFulueZeroDamageCounterSlash => HasIssuedFulueRequest(frame),
                SkillProgramEffectOp.RequestLegalSlashByNearest or SkillProgramEffectOp.OfferUnlimitedVirtualSlash =>
                    IsValidNearestSlashProgramSelection(frame, executedSelection, plan.Instructions),
                SkillProgramEffectOp.SelectIssuedFixedRecipientWithDeathReturn =>
                    ExactOwnedDeathBenefitReturn(frame) && ValidFixedRecipientReceipt(frame),
                SkillProgramEffectOp.SelectEquipmentPairAndPayment =>
                    IsValidEquipmentPairProgramSelection(frame, awaitingCurrentSelection),
                SkillProgramEffectOp.SelectDyingOwnedCard =>
                    IsValidDyingOwnedCardProgramSelection(frame, awaitingCurrentSelection),
                SkillProgramEffectOp.SelectRelativeZoneDemandTarget => frame.SelectedTargetSeats.Count == 1 || awaitingCurrentSelection && frame.SelectedTargetSeats.Count == 0,
                SkillProgramEffectOp.OfferCompletedCardGift => frame.SelectedTargetSeats.Count == 0 && frame.CompletedCardGiftDraft?.RecipientSeat is null || IsValidCompletedGiftTargetSelection(frame),
                SkillProgramEffectOp.SelectTarget =>
                    frame.SelectedTargetSeats.Count == 1 || awaitingCurrentSelection && frame.SelectedTargetSeats.Count == 0,
                SkillProgramEffectOp.DamageFarthestCharacter =>
                    frame.SelectedTargetSeats.Count == 1 || awaitingCurrentSelection && frame.SelectedTargetSeats.Count == 0,
                SkillProgramEffectOp.SelectTargets =>
                    awaitingCurrentSelection && frame.SelectedTargetSeats.Count == 0 ||
                    frame.SelectedTargetSeats.Count >= executedSelection.MinimumTargets &&
                    frame.SelectedTargetSeats.Count <= executedSelection.MaximumTargets &&
                    frame.SelectedTargetSeats.Distinct().Count() == frame.SelectedTargetSeats.Count,
                SkillProgramEffectOp.SelectChainedByMarker => frame.SelectedTargetSeats.Count <= _players.Count &&
                    frame.SelectedTargetSeats.Distinct().Count() == frame.SelectedTargetSeats.Count,
                SkillProgramEffectOp.SelectOneSelectedTarget => frame.SelectedTargetSeats.Count <= 1 || awaitingCurrentSelection,
                _ => false
            };
            if (!dynamicTargetsValid)
                throw new InvalidOperationException("An active skill program has an invalid cursor or selection.");

            if (frame.ChoiceBindings.Select(binding => binding.Name).Distinct(StringComparer.Ordinal).Count() != frame.ChoiceBindings.Count)
                throw new InvalidOperationException("An active program contains duplicate named choices.");
            foreach (var binding in frame.ChoiceBindings)
            {
                if (IsNearestFactionChoiceResult(frame, binding)) continue;
                if (IsShenwuChoiceResult(frame, binding)) continue;
                if (IsHellKingChoiceResult(frame, binding)) continue;
                if (IsGuanduChoiceResult(frame, binding)) continue;
                var producer = plan.Instructions.Take(frame.InstructionIndex).SingleOrDefault(effect =>
                    (effect.Op is SkillProgramEffectOp.AccumulatePaidPhaseGift or SkillProgramEffectOp.SelectRelativeZoneDemandTarget or SkillProgramEffectOp.ObtainDeckCardWithConsecutiveTarget or SkillProgramEffectOp.ChooseDifferentActionCategoryGift or SkillProgramEffectOp.ChooseOption or SkillProgramEffectOp.ChooseDifferentCategoryDiscard or
                        SkillProgramEffectOp.RequestSlashByTarget or SkillProgramEffectOp.RequestSlashAgainstChosenTarget or
                        SkillProgramEffectOp.RevealSelectedHandAgainstTarget) &&
                    effect.ResultBind == binding.Name || effect.Op == SkillProgramEffectOp.OfferCompletedCardGift && binding.Name == CompletedGiftFactionResultBind && IsValidCompletedGiftTargetSelection(frame) ||
                    effect.Op == SkillProgramEffectOp.UseOwnerSlashAgainstTurnOwner && binding.Name == FixedTargetFactionResultBind && frame.FixedTargetSlash is { Issued: true });
                var validOption = producer?.Op switch
                {
                    SkillProgramEffectOp.AccumulatePaidPhaseGift => binding.OptionId is "crossed" or "not-crossed",
                    SkillProgramEffectOp.SelectRelativeZoneDemandTarget => binding.OptionId is "hand" or "equipment",
                    SkillProgramEffectOp.ObtainDeckCardWithConsecutiveTarget => binding.OptionId is "repeat" or "new",
                    SkillProgramEffectOp.ChooseDifferentActionCategoryGift => binding.OptionId is "gifted" or "declined",
                    SkillProgramEffectOp.ChooseOption => producer.Options.Any(option => option.Id == binding.OptionId),
                    SkillProgramEffectOp.ChooseDifferentCategoryDiscard => binding.OptionId is
                        ChooseDifferentCategoryDiscardProgramOperationDescriptor.DiscardedOption or
                        ChooseDifferentCategoryDiscardProgramOperationDescriptor.DeclinedOption,
                    SkillProgramEffectOp.RequestSlashByTarget => binding.OptionId is
                        RequestSlashByTargetProgramOperationDescriptor.UsedSlashOption or
                        RequestSlashByTargetProgramOperationDescriptor.DeclinedOption,
                    SkillProgramEffectOp.RequestSlashAgainstChosenTarget => binding.OptionId is "used-slash" or "declined",
                    SkillProgramEffectOp.UseOwnerSlashAgainstTurnOwner => binding.OptionId is "used-slash" or "declined",
                    SkillProgramEffectOp.OfferCompletedCardGift => binding.OptionId is "used-slash" or "declined",
                    SkillProgramEffectOp.RevealSelectedHandAgainstTarget => binding.OptionId is "damage" or "obtain" or "none",
                    _ => false
                };
                var chooserSeat = producer?.ChooserRef is { } producerChooser
                    ? ResolveProgramParticipant(frame, producerChooser)
                    : producer?.Op == SkillProgramEffectOp.OfferCompletedCardGift ? frame.CompletedCardGiftDraft!.RecipientSeat!.Value
                    : producer is null ? -1 : ResolveProgramEffectTarget(frame, producer.Target);
                if (producer is null || !validOption || binding.ChooserSeat != chooserSeat)
                    throw new InvalidOperationException("An active program choice does not match its committed producer.");
            }
            var paused = plan.Instructions[frame.InstructionIndex - 1];
            AssertProgramNamedBoundTargetSelection(frame, paused);
            if (ReferenceEquals(frame, _resolutionStack.LastOrDefault())) AssertProgramCompoundCardTarget(frame, paused);
            AssertDistinctFactionDiscardDraft(frame,paused);
            AssertParticipantReserveDraft(frame, paused);
            AssertYujuePending(frame, paused);
            AssertLiejieSourceDiscard(frame, paused);
            AssertPublicPileDraft(frame, paused);
            AssertHandComparison(frame, paused);
            AssertDeckProgramDrafts(frame, paused);
            AssertFixedSlashAndDeclaredDeckDrafts(frame, paused);
            AssertMouZhijueProgram(frame, paused);
            AssertCappedHandRefresh(frame, paused);
            AssertAppliedDamageBenefitReceipt(frame);
            AssertFireTargetBenefit(frame);
            AssertNamedCardAcquisition(frame);
            AssertLostHpChainedPayment(frame);
            AssertProgramTargetDraft(frame, paused);
            ValidateProgramDiscardChallengeState(frame);
            AssertPrepDiscardReceipts(frame);
            AssertSlashTargetBenefitProgram(frame);
            AssertSequentialDiscard(frame);
            AssertHalfHandPhaseDebt(frame);
            AssertPhaseHandSeizure(frame);
            AssertProgramDiscardTopPlacement(frame);
            AssertStrategicProgramSelection(frame, paused);
            AssertConfiguredCardDeclaration(frame, paused);
            AssertParticipantTopView(frame, paused);
            AssertDifferentActionCategoryGift(frame, paused);
            AssertAlternatingSuitTop(frame, paused);
            AssertProgramSlashSuitDiscard(frame, paused);
            AssertNamedTurnFlow(frame, paused);
        AssertQuotaTop(frame, paused);
            AssertDiamondDelayed(frame, paused);
            AssertConvertingGift(frame, paused);
            AssertFinalTargetGift(frame, paused);
            AssertNamedDefenseAndPublicDraft(frame, paused);
            AssertResponseEntityExchange(frame, paused);
            AssertProgramOwnedCardSelection(frame, paused);
            AssertProgramHoldCardSelection(frame, paused);
            AssertProgramRevealCardSelection(frame, paused);
            AssertProgramOwnedCardDistribution(frame, paused);
            AssertProgramAttackRangeAid(frame, paused);
            AssertProvenanceClaim(frame);
            AssertProvenanceAlcohol(frame);
            AssertAdjacentDiscardStorage(frame); AssertCompletedUsePayment(frame); AssertRoundPileAlcohol(frame); AssertCurrentSlashFireDraft(frame);
            AssertVirtualBasicDraft(frame, paused);
            AssertProgramVirtualSlashOffer(frame, paused);
            if (!frame.CardSetBindings.Any(binding => binding.Name == paused.ResultBind))
                AssertProgramEquipmentColorDiscardChoice(frame, paused);
            AssertAssistedPhysicalCardDrafts(frame, paused);
            AssertNearestLegalSlashState(frame, paused);
            ValidateParticipantHandDrafts(frame, paused);
            AssertPrivateOfferDrafts(frame, paused);
            AssertBudgetGiftDraft(frame, paused);
            AssertRedDiscardRecoveryDraft(frame, paused);
            AssertCompletedCardGiftDraft(frame, paused);
            AssertCurrentCardEnhancementDraft(frame, paused);
            AssertOriginalTargetAdditionDraft(frame, paused);
            ValidateFactionRecoveryDraft(frame);
            ValidateWeaponDamageDraft(frame);
            AssertProgramHandControlDraft(frame, paused);
            AssertPairedHandRevealChoice(frame, paused);
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
                var trigger = plan.Trigger;
                if (trigger is null || frame.WindowContext is not { } context ||
                    context.OwnerSeat != frame.OwnerSeat || context.Window != trigger.Window ||
                    frame.ActivationId != triggerId || frame.SelectedCardIds.Count != 0 ||
                    executedSelection is null && frame.SelectedTargetSeats.Count != 0 && !(trigger.DeferredTurnEndOnly && IsExactDeferredChild(frame)) &&
                    !HasExactShenwuBoundParticipantSelection(frame) && !HasExactHellKingParticipantSelection(frame))
                    throw new InvalidOperationException("An active trigger program has an invalid cursor or context.");
                continue;
            }

            var activation = plan.Activation;
            if (activation is null ||
                frame.SelectedCardIds.Count < activation.MinCards ||
                frame.SelectedCardIds.Count > activation.MaxCards ||
                frame.WangYunPlan is not { CostPaid: true } && frame.FriendEventFlow is null && frame.MatchingRecast is null && frame.PhaseHandSeizure is null && frame.SourceCurseReceipt is null && frame.DiamondDelayed is null && frame.BlackGiftContest is null && !HasPaidWeifuInitialSelection(frame) && !HasPaidShuziInitialSelection(frame) && !HasPaidLiuYanInitialSelection(frame) && !HasPaidZixiPlacement(frame) && !HasPaidWeiwanSelection(frame) && !HasPaidLeZhouFeiXianshuSelection(frame) && frame.SelectedCardIds.Any(id => !CanSelectProgramActivationCard(plan,
                    _cardZones.CardsAt(_cardZones.GetLocation(id)).Single(card => card.Id == id), frame.OwnerSeat, frame.SkillId)) ||
                activation.CardCountExpression is not null && frame.SelectedCardIds.Count !=
                    _programPhaseUses.GetValueOrDefault((frame.OwnerSeat, frame.SkillId, activation.UsageGroup)) ||
                executedSelection is null &&
                (frame.SelectedTargetSeats.Count < activation.MinTargets ||
                 frame.SelectedTargetSeats.Count > activation.MaxTargets) && !HasExactNextActualUseProgramSelection(frame, plan) && !HasExactHpLossSlashSelection(frame, plan) && !HasExactKuangfuProgramSelection(frame, plan))
                throw new InvalidOperationException("An active skill program has an invalid cursor or selection.");
        }
        foreach (var scopeId in _pendingCardsMovedBatches
                     .Select(batch => batch.AwaitingProgramFrameId)
                     .Concat(_resolutionStack.OfType<CardsMovedTriggerWindowFrame>()
                         .Select(window => window.Batch.AwaitingProgramFrameId))
                     .OfType<long>())
        {
            if (frames.Count(frame => frame.Id == scopeId && IsAwaitingProgramMovement(frame)) != 1)
                throw new InvalidOperationException("A scoped card-movement batch lost its waiting program frame.");
        }
        for (var index = 0; index < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not CardsMovedTriggerWindowFrame
                { Batch.AwaitingProgramFrameId: { } scopeId }) continue;
            if (!_resolutionStack.Take(index).OfType<ProgramSkillFrame>().Any(frame =>
                    frame.Id == scopeId && IsAwaitingProgramMovement(frame)))
                throw new InvalidOperationException("A scoped movement window lost its waiting ancestor.");
        }

        foreach (var dying in _resolutionStack.OfType<DyingFrame>().Where(dying => dying.ResumesProgramSkill &&
                     (ActiveDying?.FrameId == dying.Id || (IsOriginalDyingSuspendedByDyingSuits(dying) || IsOriginalDyingSuspendedByRecipientCategoryMark(dying)))))
        {
            var parentIndex = _resolutionStack.FindLastIndex(item => item is ProgramSkillFrame program &&
                program.Id == dying.ParentFrameId);
            if (parentIndex < 0 || parentIndex + 1 >= _resolutionStack.Count ||
                _resolutionStack[parentIndex + 1] is not DyingFrame child || child.Id != dying.FrameId ||
                child.ParentFrameId != dying.ParentFrameId || !dying.ResumesProgramSkill ||
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
    string SkillId, string ContributionId, int CardId, CardKind CardKind, Suit CardSuit,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? SkillInstanceId = null) : IGameEvent;
