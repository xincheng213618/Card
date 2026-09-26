namespace CardGame.Core;

/// <summary>
/// Validates the bounded, linear data flow supported by draw-phase programs.
/// Card-set bindings describe frozen physical cards until they are moved; their
/// historical counts remain available after movement for BoundCardCount.
/// </summary>
internal static class DrawPhaseProgramValidator
{
    private const int AllSuits = 0b1111;

    internal static void Validate(
        string path,
        SkillProgramDrawPhaseMode mode,
        IReadOnlyList<SkillProgramTriggerEffect> effects)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(effects);

        var bindings = new Dictionary<string, Binding>(StringComparer.Ordinal);
        var roots = new List<Root>();
        var selectedTargetsAvailable = false;
        var selectedTargetsConsumed = false;

        for (var index = 0; index < effects.Count; index++)
        {
            var effect = effects[index] ??
                throw Error(path, index, "effect cannot be null");
            var nodePath = $"{path}.effects[{index}]";
            RequireOwner(effect, nodePath);

            switch (effect.Op)
            {
                case SkillProgramTriggerEffectOp.Draw:
                    if (effect.ResultBind is not null ||
                        effect.NumberExpression is null && effect.Amount <= 0 ||
                        effect.NumberExpression is not null and not SkillProgramNumberExpression.LivingFactionCount)
                        throw Error(nodePath,
                            "draw must be a fixed owner draw or livingFactionCount without a result binding");
                    break;

                case SkillProgramTriggerEffectOp.Recover:
                    if (effect.NumberExpression is null)
                    {
                        if (effect.Amount <= 0 || effect.SourceBind is not null)
                            throw Error(nodePath, "constant recover requires a positive amount and no source binding");
                        break;
                    }
                    if (effect.NumberExpression != SkillProgramNumberExpression.BoundCardCount ||
                        effect.SourceBind is null)
                        throw Error(nodePath, "recover supports only a constant or boundCardCount with sourceBind");
                    _ = GetBinding(bindings, effect.SourceBind, nodePath, "recovery source");
                    break;

                case SkillProgramTriggerEffectOp.AdjustNormalDraw:
                    if (mode == SkillProgramDrawPhaseMode.Replacement)
                        throw Error(nodePath, "replacement draw programs cannot adjust the replaced normal draw");
                    if (effect.Amount == 0)
                        throw Error(nodePath, "normal-draw adjustment must be non-zero");
                    break;

                case SkillProgramTriggerEffectOp.GrantTurnCardDamageModifier:
                    if (effect.Amount <= 0 || effect.CardKinds.Count == 0)
                        throw Error(nodePath, "turn card-damage modifier requires a positive amount and card kinds");
                    break;

                case SkillProgramTriggerEffectOp.GrantTurnCardActionProhibition:
                    if (effect.CardKinds.Count == 0 || effect.ActionTypes.Count == 0)
                        throw Error(nodePath,
                            "turn card-action prohibition requires card kinds and action types");
                    break;

                case SkillProgramTriggerEffectOp.GrantTurnRuleModifier:
                    if (effect.RuleQuery == SkillRuleQuery.SlashLimit &&
                        (effect.RuleOperation != SkillRuleOperation.Add || effect.Amount <= 0) ||
                        effect.RuleQuery == SkillRuleQuery.SlashDistanceLimit &&
                        effect.RuleOperation != SkillRuleOperation.Unlimited ||
                        effect.RuleQuery is not (SkillRuleQuery.SlashLimit or SkillRuleQuery.SlashDistanceLimit))
                        throw Error(nodePath, "turn rule modifier has an unsupported query or operation");
                    break;

                case SkillProgramTriggerEffectOp.GrantTurnCardTargetRestriction:
                    if (effect.TargetRestriction != SkillProgramCardTargetRestriction.SelfOnly)
                        throw Error(nodePath, "turn card-target restriction requires selfOnly");
                    break;

                case SkillProgramTriggerEffectOp.StartJudgment:
                {
                    RequireAlways(effect, nodePath);
                    if (string.IsNullOrWhiteSpace(effect.JudgmentReason) || effect.ResultBind is null ||
                        effect.Visibility != SkillProgramCardSetVisibility.Public)
                        throw Error(nodePath,
                            "startJudgment requires a stable reason and public result binding");
                    var root = new Root(effect.ResultBind, index);
                    roots.Add(root);
                    AddBinding(bindings, effect.ResultBind, new Binding(root, AllSuits), nodePath);
                    break;
                }

                case SkillProgramTriggerEffectOp.GrantTurnCardConversion:
                    RequireAlways(effect, nodePath);
                    if (effect.SourceBind is null ||
                        effect.ColorRelation != SkillProgramCardColorRelation.OppositeBoundCard ||
                        effect.OutputKind != CardKind.Duel)
                        throw Error(nodePath,
                            "turn card conversion requires oppositeBoundCard source as Duel");
                    _ = GetBinding(bindings, effect.SourceBind, nodePath, "conversion source");
                    break;

                case SkillProgramTriggerEffectOp.RevealTopCards:
                {
                    RequireAlways(effect, nodePath);
                    if (effect.ResultBind is null)
                        throw Error(nodePath, "revealTopCards requires resultBind");
                    if (effect.NumberExpression is not null and not SkillProgramNumberExpression.OwnerLostHp)
                        throw Error(nodePath, "revealTopCards supports only a fixed amount or ownerLostHp");
                    if (effect.NumberExpression is null && effect.Amount <= 0)
                        throw Error(nodePath, "fixed revealTopCards requires a positive amount");
                    var root = new Root(effect.ResultBind, index);
                    roots.Add(root);
                    AddBinding(bindings, effect.ResultBind, new Binding(root, AllSuits), nodePath);
                    break;
                }

                case SkillProgramTriggerEffectOp.FilterBoundCards:
                {
                    RequireAlways(effect, nodePath);
                    if (effect.SourceBind is null || effect.ResultBind is null || effect.Suits.Count == 0)
                        throw Error(nodePath, "filterBoundCards requires sourceBind, resultBind and suits");
                    var source = GetBinding(bindings, effect.SourceBind, nodePath, "filter source");
                    if ((source.Root.ConsumedMask & source.SuitMask) != 0)
                        throw Error(nodePath, $"filter source '{effect.SourceBind}' includes cards already moved");
                    var filteredMask = source.SuitMask & SuitMask(effect.Suits, nodePath);
                    AddBinding(bindings, effect.ResultBind, new Binding(source.Root, filteredMask), nodePath);
                    break;
                }

                case SkillProgramTriggerEffectOp.MoveBoundCards:
                {
                    RequireAlways(effect, nodePath);
                    if (effect.SourceBind is null || effect.Destination is not
                            (SkillProgramCardDestination.OwnerHand or SkillProgramCardDestination.DiscardPile or
                             SkillProgramCardDestination.DrawPileBottom))
                        throw Error(nodePath, "moveBoundCards requires a source and ownerHand or discardPile destination");
                    var source = GetBinding(bindings, effect.SourceBind, nodePath, "move source");
                    var selectedMask = source.SuitMask;
                    if (effect.ExceptBind is { } exceptName)
                    {
                        var except = GetBinding(bindings, exceptName, nodePath, "move exclusion");
                        if (!ReferenceEquals(source.Root, except.Root))
                            throw Error(nodePath, "move exclusion must originate from the same reveal root");
                        if ((except.SuitMask & ~source.SuitMask) != 0)
                            throw Error(nodePath, "move exclusion must be a subset of its source binding");
                        selectedMask &= ~except.SuitMask;
                    }
                    if ((source.Root.ConsumedMask & selectedMask) != 0)
                        throw Error(nodePath, $"move source '{effect.SourceBind}' consumes cards already moved");
                    source.Root.ConsumedMask |= selectedMask;
                    break;
                }

                case SkillProgramTriggerEffectOp.SelectTargets:
                    RequireAlways(effect, nodePath);
                    if (selectedTargetsAvailable)
                        throw Error(nodePath, "only one target selection is supported");
                    if (effect.TargetKind != SkillProgramTargetKind.OtherLivingWithHand ||
                        effect.MinimumTargets < 1 || effect.MaximumTargets < effect.MinimumTargets ||
                        effect.MaximumTargets > 2 || effect.TargetAiOrder is null)
                        throw Error(nodePath,
                            "selectTargets requires otherLivingWithHand and bounds 1 <= minimum <= maximum <= 2");
                    selectedTargetsAvailable = true;
                    break;

                case SkillProgramTriggerEffectOp.TakeRandomHandCardFromSelectedTargets:
                    RequireAlways(effect, nodePath);
                    if (!selectedTargetsAvailable)
                        throw Error(nodePath, "random hand-card transfer requires selectTargets first");
                    if (selectedTargetsConsumed)
                        throw Error(nodePath, "a selected target set can be consumed by random hand-card transfer only once");
                    if (effect.Amount != 1)
                        throw Error(nodePath, "random hand-card transfer requires exactly one card per target");
                    selectedTargetsConsumed = true;
                    break;

                default:
                    throw Error(nodePath, $"operation '{effect.Op}' is unsupported in drawPhaseStarting");
            }
        }

        foreach (var root in roots)
        {
            if (root.ConsumedMask != AllSuits)
                throw Error(
                    $"{path}.effects[{root.ProducerIndex}]",
                    $"card-set binding '{root.Name}' does not explicitly consume every card exactly once");
        }
    }

    private static void RequireOwner(SkillProgramTriggerEffect effect, string nodePath)
    {
        if (effect.Target != SkillProgramTriggerEffectTarget.Owner)
            throw Error(nodePath, "drawPhaseStarting effects require owner target");
    }

    private static void RequireAlways(SkillProgramTriggerEffect effect, string nodePath)
    {
        if (effect.Condition.Kind != SkillProgramConditionKind.Always)
            throw Error(nodePath, "card-set and target data-flow operations must be unconditional");
    }

    private static Binding GetBinding(
        IReadOnlyDictionary<string, Binding> bindings,
        string name,
        string nodePath,
        string use)
    {
        if (!bindings.TryGetValue(name, out var binding))
            throw Error(nodePath, $"unknown {use} binding '{name}'");
        return binding;
    }

    private static void AddBinding(
        IDictionary<string, Binding> bindings,
        string name,
        Binding binding,
        string nodePath)
    {
        if (!bindings.TryAdd(name, binding))
            throw Error(nodePath, $"duplicate card-set binding '{name}'");
    }

    private static int SuitMask(IEnumerable<Suit> suits, string nodePath)
    {
        var mask = 0;
        foreach (var suit in suits)
        {
            mask |= suit switch
            {
                Suit.Spade => 1 << 0,
                Suit.Heart => 1 << 1,
                Suit.Club => 1 << 2,
                Suit.Diamond => 1 << 3,
                _ => throw Error(nodePath, $"unsupported filter suit '{suit}'")
            };
        }
        return mask;
    }

    private static InvalidOperationException Error(string path, string message) =>
        new($"Invalid skill program at {path}: {message}.");

    private static InvalidOperationException Error(string path, int index, string message) =>
        Error($"{path}.effects[{index}]", message);

    private sealed class Root(string name, int producerIndex)
    {
        public string Name { get; } = name;
        public int ProducerIndex { get; } = producerIndex;
        public int ConsumedMask { get; set; }
    }

    private sealed record Binding(Root Root, int SuitMask);
}
