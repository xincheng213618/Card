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
        int initialTargetSetMaximum = 0)
    {
        var bindings = new Dictionary<string, Binding>(StringComparer.Ordinal);
        var roots = new List<Root>();
        var selectedTarget = initialSelectedTarget;
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
            var nodePath = $"{path}.effects[{index}]";
            var descriptor = ProgramOperationCatalog.Default.Resolve(effect.Op);
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
                    if (effect.Target != SkillProgramEffectTarget.Owner ||
                        effect.ChooserRef is not null || !bound.Root.OwnerHeld || bound.Root.AlreadyMoved)
                        Fail("bound-card category option must read the owner's stable held cards");
                }
                if (condition.Kind == SkillProgramConditionKind.BoundCardCountAtLeast)
                {
                    var bound = Get(condition.SourceBind!);
                    if (effect.Op != SkillProgramEffectOp.ChooseOption ||
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
                switch (resource)
                {
                    case CreateCardSet create:
                    {
                        var root = new Root(create.Name, create.NeedsCleanup, !create.NeedsCleanup,
                            typedCardAtoms, create.AlreadyMoved);
                        roots.Add(root);
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
                        var root = new Root(sourceCard.Name, false, sourceCard.OwnerHand, typedCardAtoms);
                        roots.Add(root);
                        Add(sourceCard.Name, new(root, root.Atoms.Keys.ToHashSet(), sourceCard.MaximumCount,
                            sourceCard.CardOwner));
                        break;
                    }
                    case CaptureActivationCards activationCards:
                    {
                        if (cardsConsumed || selectedCardCount <= 0)
                            Fail("activation input cards must be captured exactly once after selecting at least one card");
                        var root = new Root(activationCards.Name, true, true, typedCardAtoms);
                        roots.Add(root);
                        Add(activationCards.Name, new(root, root.Atoms.Keys.ToHashSet(), selectedCardCount));
                        cardsConsumed = true;
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
                    case DeriveCardSet derive:
                    {
                        if (derive.MatchSuitOfBind is { } frozen && !frozenSuitBindings.Contains(frozen))
                            Fail($"unknown public frozen suit binding '{frozen}'");
                        var source = Get(derive.Source);
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
                                    new(effect.MinimumCards, effect.MaximumCards, effect.MaximumRankSum));
                            }
                            catch (ArgumentException exception) { Fail(exception.Message); }
                        }
                        break;
                    }
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
                        if (source.Root.Consumed.Overlaps(atoms) || source.Root.PossiblyGifted.Overlaps(atoms))
                            Fail("cards may be moved more than once");
                        source.Root.Consumed.UnionWith(atoms);
                        break;
                    }
                    case GiftCardSet gift:
                    {
                        var source = Get(gift.Source);
                        if (!source.Root.OwnerHeld)
                            Fail("optional gifts require cards already held in hand, not unconsumed revealed cards");
                        if (source.Atoms.Overlaps(source.Root.Consumed))
                            Fail("a gift reads cards that have already been moved");
                        source.Root.PossiblyGifted.UnionWith(source.Atoms);
                        break;
                    }
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
                    case ReadSelectedTarget:
                        if (!selectedTarget) Fail("selectedTarget must be produced before it is read");
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

            Binding Get(string name) => bindings.TryGetValue(name, out var value)
                ? value : throw Error(nodePath, $"unknown card binding '{name}'");
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

    private sealed class Root(string name, bool needsCleanup, bool ownerHeld, bool typedCardAtoms,
        bool alreadyMoved = false)
    {
        internal string Name { get; } = name;
        internal bool NeedsCleanup { get; } = needsCleanup;
        internal bool OwnerHeld { get; } = ownerHeld;
        internal bool AlreadyMoved { get; } = alreadyMoved;
        internal Dictionary<int, (CardKind Kind, Suit Suit)> Atoms { get; } =
            (typedCardAtoms
                ? Enum.GetValues<CardKind>()
                    .SelectMany(kind => Enum.GetValues<Suit>().Select(suit => (kind, suit)))
                : Enum.GetValues<Suit>().Select(suit => (CardKind.Slash, suit)))
            .Select((value, index) => (value, index))
            .ToDictionary(item => item.index, item => (item.value.Item1, item.value.Item2));
        internal int NextAtom { get; set; } = typedCardAtoms
            ? Enum.GetValues<CardKind>().Length * Enum.GetValues<Suit>().Length : 4;
        internal HashSet<int> Consumed { get; } = [];
        internal HashSet<int> PossiblyGifted { get; } = [];
    }

    private sealed record Binding(Root Root, HashSet<int> Atoms, int MaximumCount,
        SkillProgramEffectTarget? CardOwner = null);
}
