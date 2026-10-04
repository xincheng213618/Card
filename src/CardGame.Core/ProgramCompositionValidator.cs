namespace CardGame.Core;

/// <summary>
/// Validates resource relationships independently of the operation or entry point.
/// Splitting symbolic card partitions preserves aliases, so consuming a subset and
/// its complement is provably different from moving the same physical cards twice.
/// </summary>
internal static class ProgramCompositionValidator
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects,
        bool initialSelectedTarget = false, int selectedCardCount = 0,
        SkillProgramTriggerWindow? window = null,
        SkillProgramDrawPhaseMode drawPhaseMode = SkillProgramDrawPhaseMode.Additive,
        int initialTargetSetCount = 0,
        int initialTargetSetMaximum = 0,
        bool expandedCardDomain = false, SkillProgramCardActionOwnerRelation? cardActionRelation = null, IReadOnlyList<CardKind>? cardKinds = null,
        SkillProgramTurnOwnerScope? turnOwnerScope = null,
        SkillProgramTargetKind? activationTargetKind = null,
        IReadOnlyList<CardZoneKind>? activationSourceZones = null, int? activationMinimumCards = null,
        IReadOnlyList<SkillProgramCardCategory>? activationCardCategories = null)
    {
        if (effects.Any(e => e.Op == SkillProgramEffectOp.UseSelectedActorDuel) &&
            (window is not null || selectedCardCount != 0 || !initialSelectedTarget || initialTargetSetMaximum != 0 || effects.Count != 1))
            throw Error(path, "Selected actor Duel is a single zero-card one-selected-target activation.");
        TurnDrawDebtComposition.Validate(path, effects, window, drawMode: drawPhaseMode, turnOwnerScope: turnOwnerScope);
        SequentialDiscardComposition.Validate(path, effects, window, selectedCardCount, initialSelectedTarget,
            initialTargetSetMaximum, activationSourceZones, activationCardCategories);
        HalfHandPhaseDebtComposition.Validate(path, effects, window, selectedCardCount, initialTargetSetMaximum, turnOwnerScope);
        EquipmentPairDyingCardComposition.Validate(path, effects, window);
        GrantNextActualUseTargetAdjustmentDescriptor.ValidateComposition(path, effects, window, selectedCardCount,
            initialSelectedTarget, activationTargetKind, activationSourceZones, activationMinimumCards);
        DiscardedProvenanceComposition.Validate(path, effects, window, selectedCardCount, initialSelectedTarget, initialTargetSetMaximum);
        AdjacentDiscardAndRoundAlcoholComposition.Validate(path, effects, window);
        GiveBoundCardThenOfferVirtualSlashOrSharedDrawDescriptor.ValidateComposition(path, effects, window, selectedCardCount,
            initialSelectedTarget, activationTargetKind, initialTargetSetMaximum);
        ChoosePrivateColorsDiscardAndDuelDescriptor.ValidateComposition(path, effects, window, selectedCardCount, initialSelectedTarget, initialTargetSetMaximum);
        PaidHpLossProgram.ValidateComposition(path, effects, window, selectedCardCount, initialSelectedTarget, initialTargetSetMaximum);
        PayHpToGrantOneUseDamageShieldDescriptor.ValidateComposition(path, effects, window, initialSelectedTarget, initialTargetSetMaximum);
        if (effects.Any(effect => effect.Op == SkillProgramEffectOp.UseOwnerSlashAgainstTurnOwner) &&
            (window != SkillProgramTriggerWindow.TurnEnding || turnOwnerScope != SkillProgramTurnOwnerScope.OtherLiving ||
             effects.Count != 1 || selectedCardCount != 0 || initialSelectedTarget || initialTargetSetMaximum != 0))
            throw Error(path, "Fixed-target owner Slash requires one standalone other-living turnEnding operation.");
        if (effects.Any(effect => effect.Op == SkillProgramEffectOp.DeclareDeckCriterionAndGiveMatchingCard) &&
            (window is not null || effects.Count != 1 || selectedCardCount != 0 || initialSelectedTarget ||
             initialTargetSetCount != 0 || initialTargetSetMaximum != 0))
            throw Error(path, "Declared deck criterion requires one standalone zero-input activation.");
        expandedCardDomain |= RequiresExpandedCardDomain(effects);
        var bindings = new Dictionary<string, Binding>(StringComparer.Ordinal);
        var choiceGuardedBindings = new Dictionary<string, SkillProgramCondition>(StringComparer.Ordinal);
        var roots = new List<Root>();
        var topPayments = new HashSet<string>(StringComparer.Ordinal);
        var selectedTarget = initialSelectedTarget;
        var hpPairProduced = false;
        var capturedPlacementBindings = new HashSet<string>(StringComparer.Ordinal);
        var derivedPlacementRoots = new HashSet<Root>();
        var cardsConsumed = false;
        var targetSetAvailable = initialTargetSetCount > 0;
        var targetSetConsumed = false;
        var pindianResults = new HashSet<string>(StringComparer.Ordinal);
        var choiceResults = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var frozenSuitBindings = new HashSet<string>(StringComparer.Ordinal);
        var typedCardAtoms = effects.Any(effect => effect.Op == SkillProgramEffectOp.FilterBoundCards &&
            (effect.CardCategories.Count > 0 || effect.EquipmentSlots.Count > 0 || effect.CardKinds.Count > 0));
        var coverageResults = new HashSet<string>(StringComparer.Ordinal);
        var capabilities = ProgramEntryCapabilities.For(window);
        for (var index = 0; index < effects.Count; index++)
        {
            var effect = effects[index];
            if(effect is {Op:SkillProgramEffectOp.SelectAndMoveOwnedCard,Destination:SkillProgramCardDestination.DrawPileTop,ResultBind:{ } payment,Condition.Kind:SkillProgramConditionKind.Always,AwaitMovementTriggers:true,Zones:[CardZoneKind.Hand],ChooserRef.Kind:ProgramParticipantRef.Owner,CardOwnerRef.Kind:ProgramParticipantRef.Owner}) topPayments.Add(payment);
            var nodePath = $"{path}.effects[{index}]";
            var descriptor = ProgramOperationCatalog.Default.Resolve(effect.Op);
            if (hpPairProduced && effect.Op is not (SkillProgramEffectOp.Draw or SkillProgramEffectOp.Recover))
                throw Error(nodePath, "A frozen HP pair permits only a Draw/Recover tail after its producer.");
            if (effect.Target is SkillProgramEffectTarget.HpPairHigher or SkillProgramEffectTarget.HpPairLower &&
                effect.Op is not (SkillProgramEffectOp.Draw or SkillProgramEffectOp.Recover))
                throw Error(nodePath, "Frozen HP pair targets are limited to Draw and Recover.");
            if (effect.Op == SkillProgramEffectOp.PlaceSelectedEquipment &&
                (window is not null || selectedCardCount != 1 || !initialSelectedTarget ||
                 activationMinimumCards != 1 || activationSourceZones is null ||
                 activationSourceZones.Any(zone => zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)) ||
                 activationCardCategories is null || !activationCardCategories.SequenceEqual([SkillProgramCardCategory.Equipment])))
                throw Error(nodePath, "Equipment placement requires one captured owner HE equipment and one initial target.");
            if (effect.Op == SkillProgramEffectOp.LoseOwnerSkillsAndGrant &&
                (window is not null || index != effects.Count - 1))
                throw Error(nodePath, "Owner skill replacement must terminate an activation.");
            if (effect.Op == SkillProgramEffectOp.DrawTurnOwnerThenDiscardMaximumHandForDodge &&
                (window is not null || effects.Count != 1 || selectedCardCount != 0 || initialSelectedTarget ||
                 initialTargetSetCount != 0 || initialTargetSetMaximum != 0))
                throw Error(nodePath, "Response-only Dodge requires a single zero-card zero-target activation.");
            foreach (var condition in Conditions(effect.Condition)
                         .Concat(effect.Options.SelectMany(option => Conditions(option.Condition))))
            {
                if (condition.Kind is (SkillProgramConditionKind.PindianWon or SkillProgramConditionKind.PindianNotWon) &&
                    !pindianResults.Contains(condition.SourceBind!))
                    Fail($"unknown Pindian result binding '{condition.SourceBind}'");
                if (condition.Kind == SkillProgramConditionKind.ChoiceIs &&
                    (!choiceResults.TryGetValue(condition.SourceBind!, out var options) ||
                     !options.Contains(condition.OptionId!, StringComparer.Ordinal)))
                    Fail($"unknown choice result or option '{condition.SourceBind}/{condition.OptionId}'");
                if (condition.Kind is SkillProgramConditionKind.BoundCardsSameColor or
                    SkillProgramConditionKind.BoundCardsMatchCategories or
                    SkillProgramConditionKind.BoundCardsMatchKinds or
                    SkillProgramConditionKind.BoundCardsMatchSuits)
                    _ = Get(condition.SourceBind!);
                if (condition.Kind == SkillProgramConditionKind.BoundCardsMatchCategories &&
                    effect.Op == SkillProgramEffectOp.ChooseOption)
                {
                    var bound = Get(condition.SourceBind!);
                    var ownerHeld = effect.Target == SkillProgramEffectTarget.Owner &&
                        effect.ChooserRef is null && bound.Root.OwnerHeld && !bound.Root.AlreadyMoved;
                    var revealedRecipient = effect.Target == SkillProgramEffectTarget.SelectedTarget &&
                        effect.ChooserRef is null &&
                        bound.CardOwner == SkillProgramEffectTarget.SelectedTarget &&
                        bound.Root.AlreadyMoved && frozenSuitBindings.Contains(condition.SourceBind!);
                    if (!ownerHeld && !revealedRecipient)
                        Fail("bound-card category option must read stable owner cards or a revealed recipient card");
                }
                if (condition.Kind == SkillProgramConditionKind.BoundCardCountAtLeast)
                {
                    var bound = Get(condition.SourceBind!);
                    if (effect.Op is not (SkillProgramEffectOp.ChooseOption or SkillProgramEffectOp.SetBooleanState) ||
                        bound.CardOwner != effect.Target || bound.Root.AlreadyMoved)
                        Fail("bound-card count option must read the chooser's own stable cards");
                }
                if (condition.Kind == SkillProgramConditionKind.ActivationCardCountAtLeast &&
                    (window is not null || selectedCardCount < condition.Value))
                    Fail("activation card count requires a selectable activation with enough card capacity");
                if (condition.Kind == SkillProgramConditionKind.BoundCardSuitMatchesChoice &&
                    (!frozenSuitBindings.Contains(condition.SourceBind!) ||
                     !choiceResults.TryGetValue(condition.ChoiceBind!, out var suitChoices) ||
                     suitChoices.Count != 4 ||
                     !suitChoices.Order(StringComparer.Ordinal).SequenceEqual(
                         new[] { "club", "diamond", "heart", "spade" })))
                    Fail("suit comparison requires a prior public one-card transfer and four-suit choice");
                if (condition.Kind == SkillProgramConditionKind.AttackRangeCoverageDecreased &&
                    !coverageResults.Contains(condition.SourceBind!))
                    Fail($"unknown attack-range coverage result binding '{condition.SourceBind}'");
            }
            if (effect.Op == SkillProgramEffectOp.AccumulateSelectedCardCount &&
                (window is not null || selectedCardCount < 1))
                Fail("selected-card accumulation requires an activation with selectable cards");
            if ((capabilities & descriptor.RequiredCapabilities) != descriptor.RequiredCapabilities)
                throw Error(nodePath, $"operation requires context {descriptor.RequiredCapabilities}, supplied {capabilities}");
            if (effect.ReplacementSuits.Count > 0 &&
                !capabilities.HasFlag(ProgramContextCapability.JudgmentReplacement))
                throw Error(nodePath, "replacement filters require judgmentReplacing context");
            if (drawPhaseMode == SkillProgramDrawPhaseMode.Replacement &&
                descriptor.RequiredCapabilities.HasFlag(ProgramContextCapability.DrawPlan))
                throw Error(nodePath, "a replacement draw program cannot adjust the replaced normal draw");
            if (descriptor.RequiredCapabilities.HasFlag(ProgramContextCapability.PhaseInsertion) && bindings.Count != 0)
                throw Error(nodePath, "card bindings cannot cross an independently interactive inserted phase");
            foreach (var resource in descriptor.Resources(effect))
            {
                if (hpPairProduced && resource is SelectSingleTarget or ReplaceSingleTarget or SelectTargetSet or
                    NarrowTargetSetToSingle or ConsumeTargetSet)
                    Fail("A frozen HP pair cannot rebuild its selected participants.");
                if (hpPairProduced && effect.Op == SkillProgramEffectOp.InsertPhase)
                    Fail("A frozen HP pair cannot cross an independent inserted phase.");
                switch (resource)
                {
                    case CreateCardSet create:
                    {
                        var root = new Root(create.Name, create.NeedsCleanup, !create.NeedsCleanup,
                            typedCardAtoms, expandedCardDomain, create.AlreadyMoved);
                        roots.Add(root);
                        if (effect.Condition.Kind == SkillProgramConditionKind.ChoiceIs &&
                            (effect.Op == SkillProgramEffectOp.UseRandomDeckEquipment || effect.Op == SkillProgramEffectOp.SelectAndMoveOwnedCard && effect.FreezeMovedCardSuit && effect.CardCategories is [SkillProgramCardCategory.Equipment]))
                            choiceGuardedBindings.Add(create.Name, effect.Condition);
                        Add(create.Name, new(root, root.Atoms.Keys.ToHashSet(), create.MaxCount, create.CardOwner));
                        if (create.AlreadyMoved)
                        {
                            root.Consumed.UnionWith(root.Atoms.Keys);
                            if (!frozenSuitBindings.Add(create.Name) || create.MaxCount != 1)
                                Fail("revealed transfer needs a unique single-card metadata binding");
                        }
                        break;
                    }
                    case CaptureSourceCard sourceCard:
                    {
                        var root = new Root(sourceCard.Name, false, sourceCard.OwnerHand, typedCardAtoms, expandedCardDomain);
                        roots.Add(root);
                        Add(sourceCard.Name, new(root, root.Atoms.Keys.ToHashSet(), sourceCard.MaximumCount,
                            sourceCard.CardOwner));
                        break;
                    }
                    case CaptureActivationCards activationCards:
                    {
                        if (cardsConsumed || selectedCardCount <= 0)
                            Fail("activation input cards must be captured exactly once after selecting at least one card");
                        var root = new Root(activationCards.Name, true, true, typedCardAtoms, expandedCardDomain);
                        roots.Add(root);
                        Add(activationCards.Name, new(root, root.Atoms.Keys.ToHashSet(), selectedCardCount));
                        cardsConsumed = true;
                        if (effect.Condition.Kind == SkillProgramConditionKind.Always)
                            capturedPlacementBindings.Add(activationCards.Name);
                        break;
                    }
                    case ReadCompletedActivationDiscard paid:
                    {
                        var source = Get(paid.Name);
                        if (window is not null || selectedCardCount != 1 || activationMinimumCards != 1 || initialSelectedTarget ||
                            activationSourceZones is null || !activationSourceZones.Order().SequenceEqual(new[] { CardZoneKind.Hand, CardZoneKind.Equipment }.Order()) ||
                            index != 2 || source.MaximumCount != 1 || !source.Root.NeedsCleanup || !source.Root.OwnerHeld ||
                            !source.Root.Consumed.SetEquals(source.Atoms) || effects[0].Op != SkillProgramEffectOp.CaptureSelectedCards ||
                            effects[0].ResultBind != paid.Name || effects[1] is not
                            { Op: SkillProgramEffectOp.MoveBoundCards, Destination: SkillProgramCardDestination.DiscardPile, AwaitMovementTriggers: true, Condition.Kind: SkillProgramConditionKind.Always } ||
                            effects[1].SourceBind != paid.Name)
                            Fail("declaration requires its captured single own HE activation cost, fully discarded and awaited before target selection");
                        break;
                    }
                    case ReadCapturedPlacementCard captured:
                    {
                        var source = Get(captured.Name);
                        if (!capturedPlacementBindings.Contains(captured.Name) || source.Root.Name != captured.Name ||
                            source.MaximumCount != 1 || derivedPlacementRoots.Contains(source.Root))
                            Fail("Placement requires the original unconditional single captured activation binding without derivation.");
                        break;
                    }
                    case ReadSingleCardSet single:
                    {
                        var source = Get(single.Name);
                        if (source.MaximumCount > 1 || source.Atoms.Overlaps(source.Root.Consumed) ||
                            source.Atoms.Overlaps(source.Root.PossiblyGifted))
                            Fail("the operation requires a stable single-card binding before movement");
                        break;
                    }
                    case ReadFrozenSingleCardSet frozen:
                    {
                        var source = Get(frozen.Name);
                        if (source.MaximumCount != 1 || !frozenSuitBindings.Contains(frozen.Name))
                            Fail("the operation requires a previously frozen single-card metadata binding");
                        break;
                    }
                    case DeriveCardSet derive:
                    {
                        if (derive.MatchSuitOfBind is { } frozen && !frozenSuitBindings.Contains(frozen))
                            Fail($"unknown public frozen suit binding '{frozen}'");
                        var source = Get(derive.Source);
                        derivedPlacementRoots.Add(source.Root);
                        if (source.Atoms.Overlaps(source.Root.Consumed) || source.Atoms.Overlaps(source.Root.PossiblyGifted))
                            Fail("a derived set reads cards that may already have moved");
                        HashSet<int> atoms;
                        var maximum = source.MaximumCount;
                        if (derive.SelectionMaximum is { } limit)
                        {
                            maximum = Math.Min(maximum, limit);
                            atoms = [];
                            if (limit > 0)
                            {
                                // Each selected atom and its unselected sibling retain the
                                // same suit. Every earlier alias must include both children.
                                foreach (var atom in source.Atoms.ToArray())
                                {
                                    var selected = source.Root.NextAtom++;
                                    source.Root.Atoms.Add(selected, source.Root.Atoms[atom]);
                                    foreach (var prior in bindings.Values.Where(value =>
                                                 ReferenceEquals(value.Root, source.Root) && value.Atoms.Contains(atom)))
                                        prior.Atoms.Add(selected);
                                    atoms.Add(selected);
                                }
                            }
                        }
                        else
                        {
                            atoms = source.Atoms.Where(atom =>
                                    derive.MatchSuitOfBind is not null ||
                                    ProgramCardSetFilter.Matches(source.Root.Atoms[atom].Kind,
                                        source.Root.Atoms[atom].Suit, derive.Suits,
                                        derive.Categories ?? [], derive.EquipmentSlots ?? [],
                                        derive.CardKinds ?? []))
                                .ToHashSet();
                        }
                        if (source.Root.Atoms.Count > 4096)
                            Fail("symbolic card partitions exceed the bounded composition limit");
                        Add(derive.Result, new(source.Root, atoms, maximum, source.CardOwner));
                        if (derive.SelectionMaximum is not null)
                        {
                            try
                            {
                                CardSubsetSelector.ValidateDefinition(source.MaximumCount,
                                    new(effect.MinimumCards, effect.MaximumCards, effect.MaximumRankSum,
                                        effect.OnePerSuit));
                            }
                            catch (ArgumentException exception) { Fail(exception.Message); }
                        }
                        break;
                    }
                    case RequireOwnedCardSet owned:
                    {
                        var source = Get(owned.Name);
                        var producer = effects.Take(index).SingleOrDefault(prior => prior.ResultBind == owned.Name && prior.Op == SkillProgramEffectOp.SelectOwnedCards);
                        if (producer is null || producer.Target != owned.Owner || producer.TargetReference is not null ||
                            producer.Condition.Kind != SkillProgramConditionKind.Always || producer.NumberExpression is not null ||
                            source.CardOwner != owned.Owner || owned.MaximumCount is { } cap && source.MaximumCount > cap ||
                            producer.Zones.Count == 0 || producer.Zones.Any(zone => !owned.Zones.Contains(zone)) ||
                            source.Root.AlreadyMoved || source.Atoms.Overlaps(source.Root.Consumed) || source.Atoms.Overlaps(source.Root.PossiblyGifted))
                            Fail("an owned card set must be a prior unconditional provider-owned zone selection before movement");
                        break;
                    }
                    case RequirePublicOwnedGiftSet shown:
                    {
                        var producer = effects.Take(index).SingleOrDefault(prior => prior.ResultBind == shown.Name && prior.Op == SkillProgramEffectOp.SelectOwnedCards);
                        if (producer is null || producer.MaximumCards > 0 && producer.MinimumCards < 1 ||
                            !effects.Take(index).Any(prior => prior.Op == SkillProgramEffectOp.RevealBoundCards && prior.SourceBind == shown.Name && prior.Condition.Kind == SkillProgramConditionKind.Always) ||
                            effects.Take(index).Any(prior => prior.Op == SkillProgramEffectOp.GiveShownBoundCardsAndGrantTurnHandLimit))
                            Fail("a shown gift requires a nonempty owned selection, its unconditional reveal and one receipt per program frame");
                        break;
                    }
                    case RetainOwnedCardSet retained:
                    {
                        var source = Get(retained.Name);
                        if (!source.Root.OwnerHeld || source.MaximumCount != 1 || source.Root.Consumed.Overlaps(source.Atoms) || source.Root.PossiblyGifted.Overlaps(source.Atoms))
                            Fail("retention requires one stable owner-held entity");
                        source.Root.Consumed.UnionWith(source.Atoms);
                        break;
                    }
                    case RequireTopHandPayment requiredPayment:
                        if(!topPayments.Contains(requiredPayment.Name)) Fail("replacement requires an unconditional awaited owner-hand top-deck payment");
                        break;
                    case ReadCardSet read:
                        _ = Get(read.Name);
                        break;
                    case MoveCardSet move:
                    {
                        var source = Get(move.Source);
                        if (move.Destination == SkillProgramCardDestination.DrawPileBottom &&
                            (window != SkillProgramTriggerWindow.DrawPhaseStarting ||
                             source.Root.OwnerHeld || !source.Root.NeedsCleanup ||
                             source.MaximumCount > 4))
                            Fail("drawPileBottom requires at most four temporary revealed cards in drawPhaseStarting");
                        if (source.Root.OwnerHeld && move.Destination == SkillProgramCardDestination.OwnerHand)
                            Fail("cards already held by the owner cannot be moved to the same hand zone");
                        var atoms = source.Atoms.ToHashSet();
                        if (move.Except is { } exceptName)
                        {
                            var except = Get(exceptName);
                            if (!ReferenceEquals(source.Root, except.Root) || !except.Atoms.IsSubsetOf(source.Atoms))
                                Fail("excluded cards must be a subset of the same source root");
                            atoms.ExceptWith(except.Atoms);
                        }
                        // Branches of one named choice are mutually exclusive at
                        // play time, so they may partition the same cards; any
                        // other overlap would move physical cards twice.
                        var choiceBind = effect.Condition.Kind == SkillProgramConditionKind.ChoiceIs
                            ? effect.Condition.SourceBind : null;
                        var choiceOption = choiceBind is null ? null : effect.Condition.OptionId;
                        if (source.Root.Consumed.Overlaps(atoms) || source.Root.PossiblyGifted.Overlaps(atoms))
                        {
                            if (choiceBind is null || choiceOption is null ||
                                !source.Root.ChoiceConsumed.TryGetValue(choiceBind, out var choiceAtoms) ||
                                !atoms.IsSubsetOf(choiceAtoms) ||
                                source.Root.ChoiceOptionConsumed.TryGetValue(
                                    $"{choiceBind}\u001f{choiceOption}", out var optionAtoms) &&
                                optionAtoms.Overlaps(atoms))
                                Fail("cards may be moved more than once");
                        }
                        source.Root.Consumed.UnionWith(atoms);
                        if (choiceBind is not null && choiceOption is not null)
                        {
                            if (!source.Root.ChoiceConsumed.TryGetValue(choiceBind, out var choiceAtoms))
                                source.Root.ChoiceConsumed[choiceBind] = choiceAtoms = [];
                            choiceAtoms.UnionWith(atoms);
                            var optionKey = $"{choiceBind}\u001f{choiceOption}";
                            if (!source.Root.ChoiceOptionConsumed.TryGetValue(optionKey, out var optionAtoms))
                                source.Root.ChoiceOptionConsumed[optionKey] = optionAtoms = [];
                            optionAtoms.UnionWith(atoms);
                        }
                        break;
                    }
                    case GiftCardSet gift:
                    {
                        var source = Get(gift.Source);
                        if (!source.Root.OwnerHeld && !source.Root.NeedsCleanup)
                            Fail("optional gifts require cards already held in hand or a temporary reveal binding");
                        if (source.Atoms.Overlaps(source.Root.Consumed))
                            Fail("a gift reads cards that have already been moved");
                        if (!source.Root.OwnerHeld) source.Root.Consumed.UnionWith(source.Atoms);
                        source.Root.PossiblyGifted.UnionWith(source.Atoms);
                        break;
                    }
                    case RequireSingleOwnedActivationGift:
                        if (window is not null || selectedCardCount != 1 || activationMinimumCards != 1 || cardsConsumed || !initialSelectedTarget || activationTargetKind != SkillProgramTargetKind.OtherLivingHighestHand || activationSourceZones is null || !activationSourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment])) Fail("gift damage needs one unconsumed HE activation input and highest-hand other target");
                        cardsConsumed = true;
                        break;
                    case RequireFirstLimitedActivation:
                        if (window is not null || selectedCardCount != 0 || index != 0) Fail("a first-round limited refund must be the first zero-card activation instruction");
                        break;
                    case RequireActivationEntry:
                        if (window is not null || selectedCardCount != 0 || initialSelectedTarget) Fail("operation requires a zero-input activation");
                        break;
                    case RequireActivationHandComparison:
                        if (window is not null || selectedCardCount != 1 || activationMinimumCards != 1 || cardsConsumed || !initialSelectedTarget || activationTargetKind != SkillProgramTargetKind.OtherLiving || activationSourceZones is null || !activationSourceZones.SequenceEqual([CardZoneKind.Hand])) Fail("hand comparison needs exactly one unconsumed activation input");
                        cardsConsumed = true; // The instruction claims its selected input for reveal, without moving it.
                        break;
                    case RequireEquipmentSlotActivation:
                        if (window is not null || selectedCardCount != 0 || initialSelectedTarget ||
                            initialTargetSetCount != 0 || initialTargetSetMaximum != 0 || index != 0 ||
                            effects.Count(e => e.Op == SkillProgramEffectOp.AbolishEquipmentSlotGroup) != 1)
                            Fail("slot payment must be the first instruction of one zero-input activation");
                        break;
                    case RequireEquipmentRecastActivation:
                        if (window is not null || selectedCardCount != 1 || activationMinimumCards != 1 ||
                            initialSelectedTarget || initialTargetSetCount != 0 || initialTargetSetMaximum != 0 ||
                            effects.Count != 1 || activationSourceZones is null || activationSourceZones.Count == 0 ||
                            activationSourceZones.Any(z => z is not (CardZoneKind.Hand or CardZoneKind.Equipment)) ||
                            activationCardCategories is not [SkillProgramCardCategory.Equipment])
                            Fail("equipment recast needs one actual owner H/E equipment input and no targets");
                        break;
                    case ConsumeSelectedCards consume:
                        if (cardsConsumed || selectedCardCount <= 0 ||
                            consume.Count != 0 && consume.Count != selectedCardCount)
                            Fail("activation input cards must be consumed exactly once with their declared count");
                        cardsConsumed = true;
                        break;
                    case CreatePindianResult result:
                        if (roots.Any(root => root.NeedsCleanup && !root.Consumed.SetEquals(root.Atoms.Keys)))
                            Fail("pindian cannot start while an earlier revealed card binding still needs cleanup");
                        if (!pindianResults.Add(result.Name) || bindings.ContainsKey(result.Name) ||
                            choiceResults.ContainsKey(result.Name) || coverageResults.Contains(result.Name))
                            Fail($"duplicate result binding '{result.Name}'");
                        break;
                    case ReadPindianResult result:
                        if (!pindianResults.Contains(result.Name))
                            Fail($"unknown Pindian result binding '{result.Name}'");
                        break;
                    case RequireOwnTurnBoundary:
                        if((turnOwnerScope ?? SkillProgramTurnOwnerScope.Own)!=SkillProgramTurnOwnerScope.Own) Fail("this instruction requires the skill owner's turn boundary");
                        break;
                    case RequireChoiceOptions requiredChoice:
                        if(!choiceResults.TryGetValue(requiredChoice.Name,out var declaredOptions) ||
                            !declaredOptions.Order().SequenceEqual(requiredChoice.Options.Order()))
                            Fail("alternating choice requires exactly its declared two benefit options");
                        break;
                    case CreateChoiceResult choice:
                        if (!choiceResults.TryAdd(choice.Name, choice.Options) ||
                            bindings.ContainsKey(choice.Name) || pindianResults.Contains(choice.Name) ||
                            coverageResults.Contains(choice.Name))
                            Fail($"duplicate result binding '{choice.Name}'");
                        break;
                    case CreateCoverageResult coverage:
                        if (!coverageResults.Add(coverage.Name) || bindings.ContainsKey(coverage.Name) ||
                            choiceResults.ContainsKey(coverage.Name) || pindianResults.Contains(coverage.Name))
                            Fail($"duplicate result binding '{coverage.Name}'");
                        break;
                    case RequireSelectedTargetKind requiredTarget:
                    {
                        var producer = effects.Take(index).LastOrDefault(item => item.Op == SkillProgramEffectOp.SelectTarget);
                        if (producer is null || producer.TargetKind != requiredTarget.Kind || producer.Condition.Kind != SkillProgramConditionKind.Always)
                            Fail("selectedTarget requires an unconditional selection of the declared participant kind");
                        break;
                    }
                    case ReadSelectedTarget:
                        if (!selectedTarget) Fail("selectedTarget must be produced before it is read");
                        break;
                    case CreateHpPairSnapshot:
                        if (hpPairProduced) Fail("An HP pair may have only one unconditional producer.");
                        hpPairProduced = true;
                        break;
                    case ReadHpPairSnapshot:
                        if (!hpPairProduced) Fail("Frozen HP pair target requires its earlier unconditional producer.");
                        break;
                    case ReadTargetSet read:
                    {
                        var selection = effects.Take(index)
                            .LastOrDefault(item => item.Op == SkillProgramEffectOp.SelectTargets);
                        var minimum = selection?.MinimumTargets ?? initialTargetSetCount;
                        var maximum = selection?.MaximumTargets ?? initialTargetSetMaximum;
                        if (!targetSetAvailable || minimum < read.Minimum ||
                            read.Maximum is { } exactMaximum && maximum > exactMaximum)
                            Fail("the required selected target set must be produced before it is read");
                        break;
                    }
                    case RequireMarkerLifecycle markerLifecycle:
                        if (markerLifecycle.Selected ? window != SkillProgramTriggerWindow.PlayPhaseStarting : window is not (SkillProgramTriggerWindow.GameStarting or SkillProgramTriggerWindow.PlayPhaseStarting or SkillProgramTriggerWindow.DrawPhaseStarting)) Fail("marker mutation requires an explicit supported lifecycle window");
                        break;
                    case RequirePublicPileActivation:
                        if (window is not null || index != 0 || selectedCardCount != 0 || initialSelectedTarget || initialTargetSetMaximum != 0)
                            Fail("public pile gain requires the first instruction of a zero-card zero-target activation");
                        break;
                    case RequireAwaitedHandPayment:
                        if (window is not null || index != 2 || effects[0].Op != SkillProgramEffectOp.ObtainPublicPileCard ||
                            effects[1] is not { Op: SkillProgramEffectOp.SelectAndMoveOwnedCard, Target: SkillProgramEffectTarget.Owner,
                                Amount: 1, ChooserRef.Kind: ProgramParticipantRef.Owner, CardOwnerRef.Kind: ProgramParticipantRef.Owner,
                                Zones: [CardZoneKind.Hand], Destination: SkillProgramCardDestination.DiscardPile,
                                AwaitMovementTriggers: true, SkipIfNoCards: false, Condition.Kind: SkillProgramConditionKind.Always })
                            Fail("field discard requires an awaited public-pile gain followed by an unconditional real owner hand discard");
                        break;
                    case ConsumePublicPileCardSet reserve:
                        var reserveSet=Get(reserve.Name);
                        if(reserveSet.Root.Consumed.Overlaps(reserveSet.Atoms)||reserveSet.Root.PossiblyGifted.Overlaps(reserveSet.Atoms)) Fail("reserve entity is already consumed");
                        reserveSet.Root.Consumed.UnionWith(reserveSet.Atoms);
                        break;
                    case RequirePrecedingOwnerDraw:
                        if(index==0 || effects[index-1] is not {Op:SkillProgramEffectOp.Draw,Target:SkillProgramEffectTarget.Owner,TargetReference:null,Condition.Kind:SkillProgramConditionKind.Always}) Fail("await movement requires a preceding unconditional own draw");
                        break;
                    case RequirePublicPileColorActivation:
                        if(window is not null || effects.Count!=1 || selectedCardCount!=0 || initialSelectedTarget || initialTargetSetCount!=0) Fail("public-pile color damage requires a standalone zero-card zero-target activation");
                        break;
                    case RequirePublicPileExchangeBoundary:
                        if(window is not (SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or SkillProgramTriggerWindow.TurnEnding)) Fail("public-pile hand exchange requires preparation or Ending");
                        break;
                    case RequireTriggerWindows allowed:
                        if (window is null || !allowed.Windows.Contains(window.Value)) Fail("operation requires one of its declared trigger windows");
                        break;
                    case RequireTriggerWindow required:
                        if (window != required.Window)
                            Fail($"operation requires trigger window {required.Window}, supplied {window}");
                        break;
                    case RequireCardActionActor:
                        if (cardActionRelation != SkillProgramCardActionOwnerRelation.Actor) Fail("operation requires the actual card-action actor");
                        break;
                    case RequireNonEquipmentAction:
                        if (cardKinds?.Any(EquipmentCatalog.IsEquipment) == true) Fail("operation cannot accept an equipment card action");
                        break;
                    case RequireResponseActionObserver:
                        if (window != SkillProgramTriggerWindow.CardResponseAccepted || cardActionRelation != SkillProgramCardActionOwnerRelation.Observer)
                            Fail("response entity exchange requires a response-action observer");
                        break;
                    case RequireCardActionRelation requiredRelation:
                        if (cardActionRelation != requiredRelation.Relation || cardKinds is not { Count: > 0 } || cardKinds.Any(kind => !requiredRelation.Kinds.Contains(kind)))
                            Fail("operation requires its declared card-action participant relation and supported card kinds");
                        break;
                    case RequireContext required:
                        if ((capabilities & required.Capability) != required.Capability)
                            Fail($"operation requires context {required.Capability}, supplied {capabilities}");
                        break;
                    case RequireAnyContext required:
                        if ((capabilities & required.Capabilities) == 0)
                            Fail($"operation requires one of {required.Capabilities}, supplied {capabilities}");
                        break;
                    case SelectSingleTarget:
                        if (selectedTarget || targetSetAvailable) Fail("a composition may select its target only once");
                        selectedTarget = true;
                        break;
                    case NarrowTargetSetToSingle:
                        if (!targetSetAvailable) Fail("narrowing a target set requires an existing selection");
                        targetSetAvailable = false;
                        selectedTarget = true;
                        break;
                    case ReplaceSingleTarget:
                        if (!selectedTarget || targetSetAvailable)
                            Fail("a replacement target requires one existing single target");
                        break;
                    case SelectTargetSet:
                        if (selectedTarget || targetSetAvailable) Fail("a composition may select its targets only once");
                        targetSetAvailable = true;
                        break;
                    case ConsumeTargetSet:
                        if (!targetSetAvailable || targetSetConsumed)
                            Fail("a target set must be selected before use and can be consumed only once");
                        targetSetConsumed = true;
                        break;
                    default:
                        Fail($"unknown resource contract '{resource.GetType().Name}'");
                        break;
                }
            }

            static bool ImpliesChoiceBranch(SkillProgramCondition condition, SkillProgramCondition guard) =>
                condition.Kind == SkillProgramConditionKind.ChoiceIs && condition.SourceBind == guard.SourceBind && condition.OptionId == guard.OptionId ||
                condition.Kind == SkillProgramConditionKind.All && condition.Children.FirstOrDefault() is { } first && ImpliesChoiceBranch(first, guard);

            Binding Get(string name)
            {
                if (choiceGuardedBindings.TryGetValue(name, out var guard) && !ImpliesChoiceBranch(effect.Condition, guard))
                    throw Error(nodePath, $"conditional card binding '{name}' requires its matching choice branch");
                return bindings.TryGetValue(name, out var value) ? value : throw Error(nodePath, $"unknown card binding '{name}'");
            }
            void Add(string name, Binding value)
            {
                if (!bindings.TryAdd(name, value) || choiceResults.ContainsKey(name) ||
                    pindianResults.Contains(name) || coverageResults.Contains(name))
                    Fail($"duplicate card binding '{name}'");
            }
            void Fail(string message) => throw Error(nodePath, message);
        }
        foreach (var root in roots.Where(root => root.NeedsCleanup))
            if (!root.Consumed.SetEquals(root.Atoms.Keys))
                throw Error(path + ".effects", $"revealed card binding '{root.Name}' is not fully consumed");
        if (selectedCardCount > 0 && !cardsConsumed)
            throw Error(path + ".effects", "activation input cards are never consumed");
        return;

        static IEnumerable<SkillProgramCondition> Conditions(SkillProgramCondition condition)
        {
            yield return condition;
            foreach (var child in condition.Children)
            foreach (var nested in Conditions(child)) yield return nested;
        }
    }

    private static InvalidOperationException Error(string path, string message) =>
        new($"Invalid skill program at {path}: {message}.");

    // Ordinary historical programs were proven over these physical card types.
    // Adding an enum member must not silently alter their standalone load contract.
    private static readonly Suit[] OrdinarySuits = [Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond];
    private static readonly CardKind[] OrdinaryKinds = Enum.GetValues<CardKind>()
        .Where(kind => kind <= CardKind.XingtianAxe).ToArray();
    internal static bool IsExpandedCardKind(CardKind kind) => kind > CardKind.XingtianAxe;

    internal static bool RequiresExpandedCardDomain(IReadOnlyList<SkillProgramEffect> effects) => effects.Any(effect =>
        effect.Op is SkillProgramEffectOp.EquipSampledGenerals or SkillProgramEffectOp.PlaceNamedWeapon ||
        effect.OutputKind is { } output && IsExpandedCardKind(output) ||
        effect.CardKinds.Any(IsExpandedCardKind) || effect.Suits.Contains(Suit.None) ||
        effect.ReplacementSuits.Contains(Suit.None) || ConditionRequiresExpandedDomain(effect.Condition) ||
        effect.Options.Any(option => ConditionRequiresExpandedDomain(option.Condition)));

    private static bool ConditionRequiresExpandedDomain(SkillProgramCondition condition) =>
        condition.Suits.Contains(Suit.None) || condition.CardKinds.Any(IsExpandedCardKind) ||
        condition.Children.Any(ConditionRequiresExpandedDomain);

    private sealed class Root(string name, bool needsCleanup, bool ownerHeld, bool typedCardAtoms,
        bool expandedCardDomain, bool alreadyMoved = false)
    {
        internal string Name { get; } = name;
        internal bool NeedsCleanup { get; } = needsCleanup;
        internal bool OwnerHeld { get; } = ownerHeld;
        internal bool AlreadyMoved { get; } = alreadyMoved;
        internal Dictionary<int, (CardKind Kind, Suit Suit)> Atoms { get; } =
            (typedCardAtoms
                ? (expandedCardDomain ? Enum.GetValues<CardKind>() : OrdinaryKinds)
                    .SelectMany(kind => (expandedCardDomain ? Enum.GetValues<Suit>() : OrdinarySuits).Select(suit => (kind, suit)))
                : (expandedCardDomain ? Enum.GetValues<Suit>() : OrdinarySuits).Select(suit => (CardKind.Slash, suit)))
            .Select((value, index) => (value, index))
            .ToDictionary(item => item.index, item => (item.value.Item1, item.value.Item2));
        internal int NextAtom { get; set; } = typedCardAtoms
            ? (expandedCardDomain ? Enum.GetValues<CardKind>().Length : OrdinaryKinds.Length) *
              (expandedCardDomain ? Enum.GetValues<Suit>().Length : OrdinarySuits.Length)
            : expandedCardDomain ? Enum.GetValues<Suit>().Length : OrdinarySuits.Length;
        internal HashSet<int> Consumed { get; } = [];
        internal HashSet<int> PossiblyGifted { get; } = [];
        internal Dictionary<string, HashSet<int>> ChoiceConsumed { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, HashSet<int>> ChoiceOptionConsumed { get; } = new(StringComparer.Ordinal);
    }

    private sealed record Binding(Root Root, HashSet<int> Atoms, int MaximumCount,
        SkillProgramEffectTarget? CardOwner = null);
}
