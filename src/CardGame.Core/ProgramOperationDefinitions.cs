using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;

namespace CardGame.Core;

internal enum ProgramOperationInteraction { Automatic, Choice }
internal enum ProgramOperationAiSemantic
{
    GainCards, Recover, LoseHp, Reveal, Filter, Subset, Move, Gift, SelectTarget,
    TurnOver, SetFaceState, GiveSelected, DiscardSelected,
    InsertPhase, RecoverTo, SelectTargets, SelectSourceCard, ClaimDamageCards,
    TakeRandomHandCards, AdjustNormalDraw, GrantTurnCardDamageModifier,
    GrantTurnCardActionProhibition, GrantTurnRuleModifier, GrantTurnCardTargetRestriction,
    StartJudgment, GrantTurnCardConversion, DiscardOwnedZoneCards, SetChainedState,
    SelectAndMoveOwnedCard, RefundCardUseDebit,
    StartPindian, SetBooleanState, ToggleBooleanState, GrantDirectedTurnCardPolicy,
    Damage, Pindian, ChangeMaximumHp, GrantSkills, ChooseOption, SelectOwnedCards,
    GrantTurnHandColorRestriction, PreventCurrentDamage, CaptureSelectedCards,
    RevealBoundCards, ChooseDifferentCategoryDiscard, UseSelectedCardsAs, GrantTurnSkills,
    ChangeAttributedMarker, CauseDeath, ReplaceJudgment, DyingRescue, ChooseOtherOwnedCardDiscard,
    DistributeOwnedCards, RequestAttackRangeAid, NullifyCurrentCardEffect, NullifySelectedCardEffects,
    RevealUniqueRankForDying, ProhibitCurrentResponse, RedirectCurrentAttack, RedirectCurrentDamage,
    HoldTargetCards, UseBoundCardByTarget, PendExtraTurn, ClaimDeathCleanupCards,
    ChooseOwnCardDiscard,
    ExchangeSelectedTargetHands,
    RequestSlashByTarget,
    BindDiscardPhaseDiscards,
    RestorePhaseHandDiscards,
    RequestNearestSlash,
    TakeRandomCardFromEveryOtherCharacter,
    UsePlacedCardAsDeclared
}
internal sealed record ProgramOperationAiPolicy(
    ProgramOperationAiSemantic Semantic,
    Action<SkillProgramEffect, ProgramAiEstimateContext> Apply);

internal abstract record ProgramResourceOperation;
internal sealed record CreateCardSet(string Name, int MaxCount, bool NeedsCleanup,
    bool AlreadyMoved = false, SkillProgramEffectTarget? CardOwner = null) : ProgramResourceOperation;
internal sealed record CaptureSourceCard(string Name, int MaximumCount = 1, bool OwnerHand = false,
    SkillProgramEffectTarget? CardOwner = null) : ProgramResourceOperation;
internal sealed record CaptureActivationCards(string Name) : ProgramResourceOperation;
internal sealed record ReadSingleCardSet(string Name) : ProgramResourceOperation;
internal sealed record SelectTargetSet(int Minimum, int Maximum) : ProgramResourceOperation;
internal sealed record ConsumeTargetSet : ProgramResourceOperation;
internal sealed record DeriveCardSet(
    string Source, string Result, IReadOnlyList<Suit> Suits, int? SelectionMaximum = null,
    IReadOnlyList<SkillProgramCardCategory>? Categories = null,
    IReadOnlyList<EquipmentSlot>? EquipmentSlots = null,
    IReadOnlyList<CardKind>? CardKinds = null, string? MatchSuitOfBind = null) : ProgramResourceOperation;
internal sealed record ReadCardSet(string Name) : ProgramResourceOperation;
internal sealed record MoveCardSet(string Source, string? Except, SkillProgramCardDestination Destination) : ProgramResourceOperation;
internal sealed record GiftCardSet(string Source) : ProgramResourceOperation;
internal sealed record ReadSelectedTarget : ProgramResourceOperation;
internal sealed record ReadTargetSet(int Minimum, int? Maximum = null) : ProgramResourceOperation;
internal sealed record RequireContext(ProgramContextCapability Capability) : ProgramResourceOperation;
internal sealed record RequireAnyContext(ProgramContextCapability Capabilities) : ProgramResourceOperation;
internal sealed record SelectSingleTarget : ProgramResourceOperation;
internal sealed record ReplaceSingleTarget : ProgramResourceOperation;
internal sealed record ConsumeSelectedCards(int Count) : ProgramResourceOperation;
internal sealed record CreatePindianResult(string Name) : ProgramResourceOperation;
internal sealed record ReadPindianResult(string Name) : ProgramResourceOperation;
internal sealed record CreateChoiceResult(string Name, IReadOnlyList<string> Options) : ProgramResourceOperation;
internal sealed record CreateCoverageResult(string Name) : ProgramResourceOperation;

internal interface IProgramOperationDescriptor
{
    SkillProgramEffectOp Op { get; }
    ISkillProgramEffectHandler Handler { get; }
    ProgramOperationInteraction Interaction { get; }
    ProgramContextCapability RequiredCapabilities { get; }
    ProgramOperationAiPolicy AiPolicy { get; }
    SkillProgramEffect Parse(ProgramOperationNodeReader reader);
    IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect);
}

internal sealed class ProgramOperationCatalog
{
    private static readonly Lazy<ProgramOperationCatalog> BuiltIn = new(
        () => Discover(typeof(ProgramOperationCatalog).Assembly), true);
    private readonly IReadOnlyDictionary<SkillProgramEffectOp, IProgramOperationDescriptor> _descriptors;

    internal ProgramOperationCatalog(IEnumerable<IProgramOperationDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        var byOp = new Dictionary<SkillProgramEffectOp, IProgramOperationDescriptor>();
        foreach (var descriptor in descriptors)
        {
            if (descriptor is null || !Enum.IsDefined(descriptor.Op) ||
                descriptor.Handler is null || descriptor.AiPolicy?.Apply is null ||
                !Enum.IsDefined(descriptor.Interaction))
                throw new InvalidOperationException("A program operation descriptor has an incomplete contract.");
            if (descriptor.Handler.Op != descriptor.Op)
                throw new InvalidOperationException($"Program operation '{descriptor.Op}' has a mismatched handler.");
            if (!byOp.TryAdd(descriptor.Op, descriptor))
                throw new InvalidOperationException($"Duplicate program operation descriptor '{descriptor.Op}'.");
        }
        if (byOp.Count == 0) throw new InvalidOperationException("No program operation descriptors were discovered.");
        _descriptors = new ReadOnlyDictionary<SkillProgramEffectOp, IProgramOperationDescriptor>(byOp);
    }

    internal static ProgramOperationCatalog Default => BuiltIn.Value;

    internal SkillProgramEffect Parse(
        JsonElement node,
        string path,
        Func<JsonElement, string, SkillProgramCondition> conditionParser,
        bool allowZeroDraw = false)
    {
        ArgumentNullException.ThrowIfNull(conditionParser);
        var reader = new ProgramOperationNodeReader(node, path, conditionParser, allowZeroDraw);
        var op = reader.RequiredEnum<SkillProgramEffectOp>("op");
        var descriptor = Resolve(op);
        var effect = descriptor.Parse(reader);
        if (effect.Op != descriptor.Op)
            throw new InvalidOperationException($"Program descriptor '{descriptor.Op}' parsed '{effect.Op}'.");
        return effect;
    }

    internal IProgramOperationDescriptor Resolve(SkillProgramEffectOp op) =>
        _descriptors.TryGetValue(op, out var descriptor)
            ? descriptor
            : throw new InvalidOperationException($"No composition descriptor exists for program operation '{op}'.");

    private static ProgramOperationCatalog Discover(Assembly assembly)
    {
        var descriptors = assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false } &&
                           type.Namespace == typeof(ProgramOperationCatalog).Namespace &&
                           typeof(IProgramOperationDescriptor).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null, Type.EmptyTypes, modifiers: null) is { } constructor
                ? (IProgramOperationDescriptor)constructor.Invoke(null)
                : throw new InvalidOperationException(
                    $"Program operation descriptor '{type.FullName}' requires a parameterless constructor."))
            .ToArray();
        return new ProgramOperationCatalog(descriptors);
    }
}

internal sealed class ProgramOperationNodeReader
{
    private const int MaximumItems = 256;
    private readonly JsonElement _node;
    private readonly Func<JsonElement, string, SkillProgramCondition> _conditionParser;

    internal ProgramOperationNodeReader(JsonElement node, string path,
        Func<JsonElement, string, SkillProgramCondition> conditionParser,
        bool allowZeroDraw = false)
    {
        if (node.ValueKind != JsonValueKind.Object) Fail(path, "must be an object");
        RejectDuplicateProperties(node, path);
        _node = node;
        Path = path;
        _conditionParser = conditionParser;
        AllowZeroDraw = allowZeroDraw;
    }

    internal string Path { get; }
    internal bool AllowZeroDraw { get; }
    internal bool Has(string name) => _node.TryGetProperty(name, out _);
    internal void AllowOnly(params string[] names)
    {
        var allowed = names.ToHashSet(StringComparer.Ordinal);
        foreach (var property in _node.EnumerateObject())
            if (!allowed.Contains(property.Name)) Fail(Path + "." + property.Name, "unsupported property");
    }
    internal string RequiredIdentifier(string name)
    {
        var value = RequiredString(name);
        if (value.Length > 128) Fail(Path + "." + name, "must not exceed 128 characters");
        return value;
    }
    internal string? OptionalIdentifier(string name) => Has(name) ? RequiredIdentifier(name) : null;
    internal int RequiredInt(string name)
    {
        var value = Required(name, JsonValueKind.Number);
        if (!value.TryGetInt32(out var result)) Fail(Path + "." + name, "must be a 32-bit integer");
        return result;
    }
    internal bool RequiredBool(string name) => Required(name, JsonValueKind.True, JsonValueKind.False).GetBoolean();
    internal T RequiredEnum<T>(string name) where T : struct, Enum => ParseEnum<T>(Required(name, JsonValueKind.String), Path + "." + name);
    internal IReadOnlyList<T> RequiredEnumArray<T>(string name) where T : struct, Enum
    {
        var array = Required(name, JsonValueKind.Array);
        if (array.GetArrayLength() > MaximumItems) Fail(Path + "." + name, $"contains more than {MaximumItems} items");
        var values = array.EnumerateArray().Select((item, index) => ParseEnum<T>(item, $"{Path}.{name}[{index}]")).ToArray();
        if (values.Distinct().Count() != values.Length) Fail(Path + "." + name, "contains duplicate values");
        return Array.AsReadOnly(values);
    }
    internal IReadOnlyList<T>? OptionalEnumArray<T>(string name) where T : struct, Enum =>
        Has(name) ? RequiredEnumArray<T>(name) : null;
    internal IReadOnlyList<string> RequiredIdentifierArray(string name)
    {
        var array = Required(name, JsonValueKind.Array);
        if (array.GetArrayLength() > MaximumItems) Fail(Path + "." + name, $"contains more than {MaximumItems} items");
        var values = array.EnumerateArray().Select((item, index) =>
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()) ||
                item.GetString()!.Length > 128)
                Fail($"{Path}.{name}[{index}]", "must contain 1 to 128 characters");
            return item.GetString()!;
        }).ToArray();
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            Fail(Path + "." + name, "contains duplicate values");
        return Array.AsReadOnly(values);
    }
    internal ProgramParticipantReference RequiredParticipantReference(string name,
        ProgramParticipantRef? defaultKind = null)
    {
        if (!Has(name))
            return defaultKind is { } fallbackKind ? new(fallbackKind) : throw new InvalidOperationException(
                $"Invalid skill program at {Path}: missing required property '{name}'.");
        var value = Required(name, JsonValueKind.Object);
        RejectDuplicateProperties(value, Path + "." + name);
        foreach (var property in value.EnumerateObject())
            if (property.Name is not ("kind" or "resultBind"))
                Fail(Path + "." + name + "." + property.Name, "unsupported property");
        if (!value.TryGetProperty("kind", out var kindNode))
            Fail(Path + "." + name, "missing required property 'kind'");
        var kind = ParseEnum<ProgramParticipantRef>(kindNode, Path + "." + name + ".kind");
        string? bind = null;
        if (value.TryGetProperty("resultBind", out var bindNode))
        {
            if (bindNode.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(bindNode.GetString()) ||
                bindNode.GetString()!.Length > 128)
                Fail(Path + "." + name + ".resultBind", "must contain 1 to 128 characters");
            bind = bindNode.GetString();
        }
        var resultKind = kind is ProgramParticipantRef.ResultSource or ProgramParticipantRef.ResultOpponent;
        if (resultKind != (bind is not null))
            Fail(Path + "." + name, "resultBind is required only for result participants");
        return new(kind, bind);
    }
    internal SkillProgramCondition Condition() => Has("condition")
        ? _conditionParser(_node.GetProperty("condition"), Path + ".condition")
        : new SkillProgramCondition(SkillProgramConditionKind.Always, 0, []);

    internal IReadOnlyList<SkillProgramChoiceOption> ChoiceOptions()
    {
        var array = Required("options", JsonValueKind.Array);
        if (array.GetArrayLength() is < 1 or > 16)
            Fail(Path + ".options", "requires between 1 and 16 options");
        var options = array.EnumerateArray().Select((node, index) =>
        {
            var reader = new ProgramOperationNodeReader(node, $"{Path}.options[{index}]", _conditionParser);
            reader.AllowOnly("id", "condition");
            return new SkillProgramChoiceOption(reader.RequiredIdentifier("id"), reader.Condition());
        }).ToArray();
        if (options.Select(option => option.Id).Distinct(StringComparer.Ordinal).Count() != options.Length)
            Fail(Path + ".options", "option ids must be distinct");
        return Array.AsReadOnly(options);
    }

    private string RequiredString(string name)
    {
        var value = Required(name, JsonValueKind.String).GetString()!;
        if (string.IsNullOrWhiteSpace(value)) Fail(Path + "." + name, "must not be empty");
        return value;
    }
    private JsonElement Required(string name, params JsonValueKind[] kinds)
    {
        if (!_node.TryGetProperty(name, out var value)) Fail(Path, $"missing required property '{name}'");
        if (!kinds.Contains(value.ValueKind)) Fail(Path + "." + name, $"must be {string.Join(" or ", kinds)}");
        return value;
    }
    private static T ParseEnum<T>(JsonElement node, string path) where T : struct, Enum
    {
        if (node.ValueKind != JsonValueKind.String) Fail(path, "must be a camelCase string");
        var text = node.GetString();
        foreach (var value in Enum.GetValues<T>())
        {
            var name = Enum.GetName(value)!;
            if (text == char.ToLowerInvariant(name[0]) + name[1..]) return value;
        }
        Fail(path, $"unsupported {typeof(T).Name} value '{text}'");
        return default;
    }
    private static void RejectDuplicateProperties(JsonElement node, string path)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in node.EnumerateObject())
            {
                if (!names.Add(property.Name)) Fail(path, $"duplicate property '{property.Name}'");
                RejectDuplicateProperties(property.Value, path + "." + property.Name);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in node.EnumerateArray()) RejectDuplicateProperties(item, $"{path}[{index++}]");
        }
    }
    [DoesNotReturn]
    private static void Fail(string path, string message) =>
        throw new InvalidOperationException($"Invalid skill program at {path}: {message}.");
}

internal abstract class ProgramOperationDescriptorBase : IProgramOperationDescriptor
{
    public abstract SkillProgramEffectOp Op { get; }
    public abstract ISkillProgramEffectHandler Handler { get; }
    public virtual ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public virtual ProgramContextCapability RequiredCapabilities => ProgramContextCapability.None;
    public abstract ProgramOperationAiPolicy AiPolicy { get; }
    public abstract SkillProgramEffect Parse(ProgramOperationNodeReader reader);
    public abstract IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect);
    protected static void RequireAlways(SkillProgramEffect effect, string path)
    {
        if (effect.Condition.Kind != SkillProgramConditionKind.Always)
            throw new InvalidOperationException($"Invalid skill program at {path}.condition: resource operations must be always.");
    }
    protected static bool IsCardActionCategoryBranch(SkillProgramCondition condition) =>
        condition.Kind == SkillProgramConditionKind.BoundCardCategoryMatchesCardAction ||
        (condition.Kind == SkillProgramConditionKind.Not && condition.Children.Count == 1 &&
         condition.Children[0].Kind == SkillProgramConditionKind.BoundCardCategoryMatchesCardAction);
    protected static IReadOnlyList<ProgramResourceOperation> WithSelectedTarget(
        SkillProgramEffect effect, IEnumerable<ProgramResourceOperation>? resources = null) =>
        Array.AsReadOnly((resources ?? []).Concat(effect.Target switch
        {
            SkillProgramEffectTarget.SelectedTarget => [new ReadSelectedTarget()],
            SkillProgramEffectTarget.SelectedTargets => [new ReadTargetSet(1)],
            SkillProgramEffectTarget.Actor =>
                [new RequireContext(ProgramContextCapability.CardAction)],
            _ => []
        }).ToArray());

    protected static IReadOnlyList<ProgramResourceOperation> ParticipantResources(
        params ProgramParticipantReference?[] references) => references
        .Where(reference => reference is not null)
        .SelectMany(reference => reference!.Kind switch
        {
            ProgramParticipantRef.Actor =>
                new ProgramResourceOperation[] { new RequireContext(ProgramContextCapability.CardAction) },
            ProgramParticipantRef.EventTarget =>
                new ProgramResourceOperation[] { new RequireAnyContext(ProgramContextCapability.CardAction |
                    ProgramContextCapability.Damage | ProgramContextCapability.Judgment) },
            ProgramParticipantRef.EventSource =>
                new ProgramResourceOperation[] { new RequireContext(ProgramContextCapability.Damage) },
            ProgramParticipantRef.SelectedTarget =>
                new ProgramResourceOperation[] { new ReadSelectedTarget() },
            ProgramParticipantRef.SelectedFirst =>
                new ProgramResourceOperation[] { new ReadTargetSet(1) },
            ProgramParticipantRef.SelectedSecond =>
                new ProgramResourceOperation[] { new ReadTargetSet(2) },
            ProgramParticipantRef.ResultSource or ProgramParticipantRef.ResultOpponent =>
                new ProgramResourceOperation[] { new ReadPindianResult(reference.ResultBind!) },
            _ => Array.Empty<ProgramResourceOperation>()
        }).ToArray();
}

internal sealed class DrawProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.Draw;
    public override ISkillProgramEffectHandler Handler { get; } = new DrawSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "numberExpression", "sourceBind", "resultBind", "targetRef", "condition",
            "replacementSuits", "minimumReplacementRank", "maximumReplacementRank");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target == SkillProgramEffectTarget.SelectedTargets &&
            (r.Has("numberExpression") || r.Has("sourceBind") || r.Has("resultBind")))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: selectedTargets draws require a fixed amount and no card binding.");
        var expression = r.Has("numberExpression")
            ? r.RequiredEnum<SkillProgramNumberExpression>("numberExpression") : (SkillProgramNumberExpression?)null;
        if (expression is not null && (r.Has("amount") || expression is not
                (SkillProgramNumberExpression.LivingFactionCount or SkillProgramNumberExpression.TargetMaxHpMinusHandCount or
                 SkillProgramNumberExpression.OwnerLostHp or SkillProgramNumberExpression.BoundCardCount or
                 SkillProgramNumberExpression.CurrentAttackRange or SkillProgramNumberExpression.HandLimitMinusHandCount)))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: draw accepts a constant or a supported public-state expression.");
        var source = r.OptionalIdentifier("sourceBind");
        if ((expression == SkillProgramNumberExpression.BoundCardCount) != (source is not null))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: sourceBind is required only for boundCardCount draws.");
        var amount = expression is null ? Amount(r, 20, allowZero: r.AllowZeroDraw) : 0;
        var bind = r.OptionalIdentifier("resultBind");
        var targetRef = r.Has("targetRef") ? r.RequiredParticipantReference("targetRef") : null;
        if (targetRef is not null && (target != SkillProgramEffectTarget.Owner ||
            expression is not null || source is not null || bind is not null))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.targetRef: participant draws require owner placeholder, fixed amount and no card binding.");
        if (targetRef is not null && targetRef.Kind is not
            (ProgramParticipantRef.EventSource or ProgramParticipantRef.EventTarget))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.targetRef: draw requires eventSource or eventTarget.");
        var replacementSuits = r.OptionalEnumArray<Suit>("replacementSuits") ?? [];
        var minimumReplacementRank = r.Has("minimumReplacementRank") ? r.RequiredInt("minimumReplacementRank") : 0;
        var maximumReplacementRank = r.Has("maximumReplacementRank") ? r.RequiredInt("maximumReplacementRank") : 0;
        if ((replacementSuits.Count > 0) != (minimumReplacementRank > 0 && maximumReplacementRank > 0) ||
            replacementSuits.Count > 0 && (minimumReplacementRank > maximumReplacementRank || maximumReplacementRank > 13))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: replacement suits and valid rank bounds must appear together.");
        var effect = new SkillProgramEffect(Op, target, amount, r.Condition(), numberExpression: expression,
            sourceBind: source, resultBind: bind, targetReference: targetRef,
            replacementSuits: replacementSuits, minimumReplacementRank: minimumReplacementRank,
            maximumReplacementRank: maximumReplacementRank);
        if (bind is not null && target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: bound draws require owner target.");
        if (bind is not null) RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [.. WithSelectedTarget(effect,
            (effect.NumberExpression == SkillProgramNumberExpression.BoundCardCount
                ? new ProgramResourceOperation[] { new ReadCardSet(effect.SourceBind!) }
                : Array.Empty<ProgramResourceOperation>())
            .Concat(effect.ResultBind is { } bind
                ? new ProgramResourceOperation[] { new CreateCardSet(bind, effect.NumberExpression is null ? effect.Amount : int.MaxValue, false) }
                : Array.Empty<ProgramResourceOperation>())),
            .. ParticipantResources(effect.TargetReference)];
    internal static int Amount(ProgramOperationNodeReader r, int maximum, bool allowZero = false)
    {
        var amount = r.RequiredInt("amount");
        if (amount < 0 || amount > maximum || (amount == 0 && !allowZero))
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.amount: must be between {(allowZero ? 0 : 1)} and {maximum}.");
        return amount;
    }
}

internal sealed class RecoverProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.Recover;
    public override ISkillProgramEffectHandler Handler { get; } = new RecoverSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Recover,
        static (effect, context) => context.Recover(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "numberExpression", "sourceBind", "targetRef", "condition",
            "replacementSuits", "minimumReplacementRank", "maximumReplacementRank");
        var replacementSuits = r.OptionalEnumArray<Suit>("replacementSuits") ?? [];
        var minimumReplacementRank = r.Has("minimumReplacementRank") ? r.RequiredInt("minimumReplacementRank") : 0;
        var maximumReplacementRank = r.Has("maximumReplacementRank") ? r.RequiredInt("maximumReplacementRank") : 0;
        if ((replacementSuits.Count > 0) != (minimumReplacementRank > 0 && maximumReplacementRank > 0) ||
            replacementSuits.Count > 0 && (minimumReplacementRank > maximumReplacementRank || maximumReplacementRank > 13))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: replacement suits and valid rank bounds must appear together.");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        var targetRef = r.Has("targetRef") ? r.RequiredParticipantReference("targetRef") : null;
        if (targetRef is not null && (target != SkillProgramEffectTarget.Owner ||
            targetRef.Kind is not (ProgramParticipantRef.EventSource or ProgramParticipantRef.EventTarget)))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.targetRef: recovery requires an event participant and owner placeholder.");
        if (target == SkillProgramEffectTarget.SelectedTargets &&
            (r.Has("numberExpression") || r.Has("sourceBind")))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: selectedTargets recovery requires a fixed amount.");
        if (!r.Has("numberExpression"))
        {
            if (r.Has("sourceBind"))
                throw new InvalidOperationException($"Invalid skill program at {r.Path}: constant recovery cannot read a card binding.");
            return new(Op, target, DrawProgramOperationDescriptor.Amount(r, 20), r.Condition(),
                targetReference: targetRef,
                replacementSuits: replacementSuits, minimumReplacementRank: minimumReplacementRank,
                maximumReplacementRank: maximumReplacementRank);
        }
        if (r.Has("amount")) throw new InvalidOperationException($"Invalid skill program at {r.Path}: recover accepts amount or numberExpression.");
        var expression = r.RequiredEnum<SkillProgramNumberExpression>("numberExpression");
        if (expression != SkillProgramNumberExpression.BoundCardCount)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.numberExpression: only boundCardCount is supported.");
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition(), numberExpression: expression,
            sourceBind: r.RequiredIdentifier("sourceBind"), replacementSuits: replacementSuits,
            minimumReplacementRank: minimumReplacementRank, maximumReplacementRank: maximumReplacementRank);
        if (target != SkillProgramEffectTarget.Owner || targetRef is not null)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: bound-card recovery requires owner.");
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        WithSelectedTarget(effect, effect.NumberExpression == SkillProgramNumberExpression.BoundCardCount
            ? new ProgramResourceOperation[] { new ReadCardSet(effect.SourceBind!) }
            : Array.Empty<ProgramResourceOperation>())
            .Concat(ParticipantResources(effect.TargetReference)).ToArray();
}

internal sealed class LoseHpProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.LoseHp;
    public override ISkillProgramEffectHandler Handler { get; } = new LoseHpSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.LoseHp,
        static (effect, context) => context.LoseHp(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "condition");
        return new(Op, r.RequiredEnum<SkillProgramEffectTarget>("target"),
            DrawProgramOperationDescriptor.Amount(r, 20), r.Condition());
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => WithSelectedTarget(effect);
}

internal sealed class RevealTopCardsProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RevealTopCards;
    public override ISkillProgramEffectHandler Handler { get; } = new RevealTopCardsSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Reveal,
        static (effect, context) => context.Reveal(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "numberExpression", "resultBind", "visibility", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner) throw new InvalidOperationException($"Invalid skill program at {r.Path}: reveal requires owner.");
        var expression = r.Has("numberExpression")
            ? r.RequiredEnum<SkillProgramNumberExpression>("numberExpression") : (SkillProgramNumberExpression?)null;
        if (expression is not null && (r.Has("amount") || expression != SkillProgramNumberExpression.OwnerLostHp))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: reveal accepts a constant or ownerLostHp.");
        var effect = new SkillProgramEffect(Op, target, expression is null ? DrawProgramOperationDescriptor.Amount(r, 16) : 0, r.Condition(),
            numberExpression: expression,
            resultBind: r.RequiredIdentifier("resultBind"),
            visibility: r.RequiredEnum<SkillProgramCardSetVisibility>("visibility"));
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new CreateCardSet(effect.ResultBind!, effect.NumberExpression is null ? effect.Amount : int.MaxValue, true)];
}

internal sealed class FilterBoundCardsProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.FilterBoundCards;
    public override ISkillProgramEffectHandler Handler { get; } = new FilterBoundCardsSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Filter,
        static (effect, context) => context.Filter(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "resultBind", "suits", "categories", "equipmentSlots", "cardKinds", "effectiveSuitForRef", "matchSuitOfBind", "condition");
        var target = Owner(r);
        var source = r.RequiredIdentifier("sourceBind"); var result = r.RequiredIdentifier("resultBind");
        if (source == result) throw new InvalidOperationException($"Invalid skill program at {r.Path}: sourceBind and resultBind must differ.");
        var suits = r.OptionalEnumArray<Suit>("suits") ?? [];
        var categories = r.OptionalEnumArray<SkillProgramCardCategory>("categories") ?? [];
        var equipmentSlots = r.OptionalEnumArray<EquipmentSlot>("equipmentSlots") ?? [];
        var cardKinds = r.OptionalEnumArray<CardKind>("cardKinds") ?? [];
        var matchSuitOfBind = r.Has("matchSuitOfBind") ? r.RequiredIdentifier("matchSuitOfBind") : null;
        if (suits.Count + categories.Count + equipmentSlots.Count + cardKinds.Count == 0 && matchSuitOfBind is null ||
            r.Has("suits") && suits.Count == 0 || r.Has("categories") && categories.Count == 0 ||
            r.Has("equipmentSlots") && equipmentSlots.Count == 0 || r.Has("cardKinds") && cardKinds.Count == 0)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: filter arrays must be nonempty and at least one selector is required.");
        if (matchSuitOfBind is not null && (r.Has("suits") || categories.Count + equipmentSlots.Count + cardKinds.Count > 0 ||
            matchSuitOfBind == source || matchSuitOfBind == result))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: dynamic suit reference must be distinct from source/result and cannot mix with static filters.");
        if ((categories.Count + equipmentSlots.Count + cardKinds.Count) > 0 && r.Has("effectiveSuitForRef"))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: effectiveSuitForRef cannot be mixed with card-class filters.");
        var effectiveSuitForRef = r.Has("effectiveSuitForRef")
            ? r.RequiredParticipantReference("effectiveSuitForRef") : null;
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition(), sourceBind: source, resultBind: result,
            suits: suits, cardCategories: categories, equipmentSlots: equipmentSlots,
            cardKinds: cardKinds, targetReference: effectiveSuitForRef, matchSuitOfBind: matchSuitOfBind);
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new DeriveCardSet(effect.SourceBind!, effect.ResultBind!, effect.Suits,
            Categories: effect.CardCategories, EquipmentSlots: effect.EquipmentSlots,
            CardKinds: effect.CardKinds, MatchSuitOfBind: effect.MatchSuitOfBind),
            .. ParticipantResources(effect.TargetReference)];
    internal static SkillProgramEffectTarget Owner(ProgramOperationNodeReader r)
    {
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner) throw new InvalidOperationException($"Invalid skill program at {r.Path}: operation requires owner.");
        return target;
    }
}

internal sealed class SelectCardSubsetProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectCardSubset;
    public override ISkillProgramEffectHandler Handler { get; } = new SelectCardSubsetSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Subset,
        static (effect, context) => context.Subset(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "resultBind", "minimumCards", "maximumCards", "maximumRankSum", "aiOrder", "allowFewerWhenInsufficient", "onePerSuit", "condition");
        var target = FilterBoundCardsProgramOperationDescriptor.Owner(r);
        var source = r.RequiredIdentifier("sourceBind"); var result = r.RequiredIdentifier("resultBind");
        if (source == result) throw new InvalidOperationException($"Invalid skill program at {r.Path}: sourceBind and resultBind must differ.");
        var min = r.RequiredInt("minimumCards"); var max = r.RequiredInt("maximumCards");
        if (min < 0 || max < min || max > CardSubsetSelector.MaximumCandidateCount)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: invalid card-count bounds.");
        var rank = r.RequiredInt("maximumRankSum");
        if (rank is < 1 or > 208) throw new InvalidOperationException($"Invalid skill program at {r.Path}.maximumRankSum: must be 1..208.");
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition(), sourceBind: source, resultBind: result,
            minimumCards: min, maximumCards: max, maximumRankSum: rank,
            aiOrder: r.RequiredEnum<SkillProgramSubsetAiOrder>("aiOrder"),
            allowFewerWhenInsufficient: r.Has("allowFewerWhenInsufficient") && r.RequiredBool("allowFewerWhenInsufficient"),
            onePerSuit: r.Has("onePerSuit") && r.RequiredBool("onePerSuit"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new DeriveCardSet(effect.SourceBind!, effect.ResultBind!, [], effect.MaximumCards)];
}

internal sealed class MoveBoundCardsProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.MoveBoundCards;
    public override ISkillProgramEffectHandler Handler { get; } = new MoveBoundCardsSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Move,
        static (effect, context) => context.Move(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "exceptBind", "destination", "destinationZone", "condition");
        var target = FilterBoundCardsProgramOperationDescriptor.Owner(r);
        var source = r.RequiredIdentifier("sourceBind"); var except = r.OptionalIdentifier("exceptBind");
        if (source == except) throw new InvalidOperationException($"Invalid skill program at {r.Path}: exceptBind must differ.");
        var destination = r.RequiredEnum<SkillProgramCardDestination>("destination");
        if (destination is not (SkillProgramCardDestination.OwnerHand or SkillProgramCardDestination.DiscardPile or
                SkillProgramCardDestination.OwnerPersistentZone or SkillProgramCardDestination.DrawPileBottom or
                SkillProgramCardDestination.PhaseOwnerHand or SkillProgramCardDestination.SelectedTargetHand or
                SkillProgramCardDestination.DrawPileTop))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.destination: unsupported destination.");
        var destinationZone = r.Has("destinationZone") ? r.RequiredEnum<CardZoneKind>("destinationZone") : (CardZoneKind?)null;
        if ((destination == SkillProgramCardDestination.OwnerPersistentZone) != (destinationZone is not null))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: destinationZone is required only for ownerPersistentZone.");
        if (destinationZone is not null && destinationZone is not
                (CardZoneKind.WoodenOxGrain or CardZoneKind.BuquWound or CardZoneKind.Authority or CardZoneKind.Chunlao))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.destinationZone: must be a persistent owner zone.");
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition(), sourceBind: source,
            exceptBind: except, destination: destination, destinationZone: destinationZone);
        // A named-choice branch may gate a discard, a gain into the owner's own
        // hand (Pindian winnings, or Guzheng's optional claim of the remaining
        // cards), or a placement onto the draw pile top (Gongxin).
        if (effect.Condition.Kind != SkillProgramConditionKind.Always &&
            !(effect.Condition.Kind == SkillProgramConditionKind.ChoiceIs &&
              destination is SkillProgramCardDestination.DiscardPile or SkillProgramCardDestination.OwnerHand or
                SkillProgramCardDestination.DrawPileTop) &&
            !(effect.Condition.Kind == SkillProgramConditionKind.PindianWon &&
              destination == SkillProgramCardDestination.OwnerHand))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: conditional card movement requires a named-choice discard or gain branch.");
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        effect.Destination == SkillProgramCardDestination.PhaseOwnerHand
            ? [new RequireContext(ProgramContextCapability.PhaseOwner),
                new MoveCardSet(effect.SourceBind!, effect.ExceptBind, effect.Destination.Value)]
            : [new MoveCardSet(effect.SourceBind!, effect.ExceptBind, effect.Destination!.Value)];
}

internal sealed class GiveBoundCardProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GiveBoundCard;
    public override ISkillProgramEffectHandler Handler { get; } = new GiveBoundCardSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift,
        static (effect, context) => context.Gift(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "targetKind", "condition");
        var target = FilterBoundCardsProgramOperationDescriptor.Owner(r);
        var kind = r.RequiredEnum<SkillProgramTargetKind>("targetKind");
        if (kind is not (SkillProgramTargetKind.OtherLiving or SkillProgramTargetKind.AnyLiving))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.targetKind: unsupported gift target.");
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition(),
            sourceBind: r.RequiredIdentifier("sourceBind"), targetKind: kind);
        if (effect.Condition.Kind != SkillProgramConditionKind.Always &&
            !IsCardActionCategoryBranch(effect.Condition))
            RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new GiftCardSet(effect.SourceBind!)];
}

internal sealed class DistributeOwnedCardsProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DistributeOwnedCards;
    public override ISkillProgramEffectHandler Handler { get; } = new DistributeOwnedCardsSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.DistributeOwnedCards,
        static (effect, context) => context.Gift(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "zones", "numberExpression", "sourceBind", "targetKind",
            "allowDeclineBeforeFirst", "condition");
        var target = FilterBoundCardsProgramOperationDescriptor.Owner(r);
        var zones = r.RequiredEnumArray<CardZoneKind>("zones");
        if (zones.Count == 0 || zones.Any(zone => zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)))
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.zones: distribution supports hand and equipment only.");
        var expression = r.RequiredEnum<SkillProgramNumberExpression>("numberExpression");
        if (expression != SkillProgramNumberExpression.BoundCardCount)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.numberExpression: distribution requires boundCardCount.");
        var kind = r.RequiredEnum<SkillProgramTargetKind>("targetKind");
        if (kind is not (SkillProgramTargetKind.OtherLiving or SkillProgramTargetKind.AnyLiving))
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.targetKind: unsupported distribution target.");
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition(),
            numberExpression: expression,
            sourceBind: r.RequiredIdentifier("sourceBind"),
            zones: zones,
            targetKind: kind,
            allowDeclineBeforeFirst: r.RequiredBool("allowDeclineBeforeFirst"));
        RequireAlways(effect, r.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadCardSet(effect.SourceBind!)];
}

internal sealed class RequestAttackRangeAidProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RequestAttackRangeAid;
    public override ISkillProgramEffectHandler Handler { get; } = new RequestAttackRangeAidSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.RequestAttackRangeAid,
        static (_, _) => { });

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.target: attack-range aid requires selectedTarget.");
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition());
        RequireAlways(effect, r.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSelectedTarget()];
}

internal sealed class SelectTargetProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectTarget;
    public override ISkillProgramEffectHandler Handler { get; } = new SelectTargetSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.SelectTarget,
        static (effect, context) => context.SelectTarget(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "targetKind", "marker", "zones", "actorRef", "skipIfNoTarget", "condition");
        var zones = r.Has("zones") ? r.RequiredEnumArray<CardZoneKind>("zones") : [];
        if (zones.Any(zone => zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment)))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.zones: selectTarget supports hand, equipment and judgment only.");
        var targetKind = r.RequiredEnum<SkillProgramTargetKind>("targetKind");
        var marker = r.Has("marker") ? r.RequiredEnum<PlayerMarkerKind>("marker") : (PlayerMarkerKind?)null;
        var actorRef = r.Has("actorRef") ? r.RequiredParticipantReference("actorRef") : null;
        var skipIfNoTarget = r.Has("skipIfNoTarget") && r.RequiredBool("skipIfNoTarget");
        if ((targetKind == SkillProgramTargetKind.OtherLivingInBoundParticipantAttackRange) !=
            (actorRef?.Kind is ProgramParticipantRef.ResultSource or ProgramParticipantRef.ResultOpponent))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: bound-participant attack-range targets require actorRef.");
        if ((targetKind == SkillProgramTargetKind.MaximumAttributedMarker) != (marker is not null))
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}: maximumAttributedMarker requires marker, and other target kinds forbid it.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0,
            r.Condition(), zones: zones, targetKind: targetKind, marker: marker,
            actorReference: actorRef, skipIfNoTarget: skipIfNoTarget);
        if (effect.Condition.Kind != SkillProgramConditionKind.Always &&
            !(skipIfNoTarget && (effect.Condition.Kind == SkillProgramConditionKind.PindianWon ||
                IsCardActionCategoryBranch(effect.Condition))))
            RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        effect.ActorReference is { } reference
            ? [new ReadPindianResult(reference.ResultBind!), new ReplaceSingleTarget()]
            : [new SelectSingleTarget()];
}

internal sealed class TurnOverProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.TurnOver;
    public override ISkillProgramEffectHandler Handler { get; } = new TurnOverSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.TurnOver,
        static (effect, context) => context.TurnOver(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "targetRef", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        var targetRef = r.Has("targetRef") ? r.RequiredParticipantReference("targetRef") : null;
        if (targetRef is not null && (target != SkillProgramEffectTarget.Owner ||
            targetRef.Kind is not (ProgramParticipantRef.EventSource or ProgramParticipantRef.EventTarget)))
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.targetRef: turnOver participant targets require an owner placeholder and eventSource or eventTarget.");
        return new(Op, target, 0, r.Condition(), targetReference: targetRef);
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        effect.TargetReference is { } reference
            ? ParticipantResources(reference)
            : WithSelectedTarget(effect);
}

internal sealed class SetFaceStateProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SetFaceState;
    public override ISkillProgramEffectHandler Handler { get; } = new SetFaceStateSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.SetFaceState,
        static (effect, context) => context.SetFaceState(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    { r.AllowOnly("op", "target", "faceDown", "condition"); return new(Op, r.RequiredEnum<SkillProgramEffectTarget>("target"), 0, r.Condition(), faceDown: r.RequiredBool("faceDown")); }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => WithSelectedTarget(effect);
}

internal sealed class GiveSelectedProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GiveSelected;
    public override ISkillProgramEffectHandler Handler { get; } = new GiveSelectedSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GiveSelected,
        static (effect, context) => context.GiveSelected(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.SelectedTarget) throw new InvalidOperationException($"Invalid skill program at {r.Path}: giveSelected requires selectedTarget.");
        // Zero is the established resource-contract sentinel for all selected cards.
        // The runtime already moves the complete frozen selection; an explicit amount
        // retains the old exact-count definition for fixed-size activations.
        var amount = r.Has("amount") ? DrawProgramOperationDescriptor.Amount(r, 20) : 0;
        var effect = new SkillProgramEffect(Op, target, amount, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSelectedTarget(), new ConsumeSelectedCards(effect.Amount)];
}

internal sealed class DiscardSelectedProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardSelected;
    public override ISkillProgramEffectHandler Handler { get; } = new DiscardSelectedSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.DiscardSelected,
        static (effect, context) => context.DiscardSelected(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "condition");
        var target = FilterBoundCardsProgramOperationDescriptor.Owner(r);
        var effect = new SkillProgramEffect(Op, target, DrawProgramOperationDescriptor.Amount(r, 20), r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ConsumeSelectedCards(effect.Amount)];
}
