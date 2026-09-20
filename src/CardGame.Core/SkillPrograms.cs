using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CardGame.Core;

public enum SkillRuleQuery { DrawCount, HandLimit, SlashLimit, OutgoingDistance, IncomingDistance }
public enum SkillRuleOperation { Add, Set, Unlimited }
public enum SkillProgramConditionKind { Always, OwnTurn, NotOwnTurn, Wounded, HpAtLeast, HandCountAtLeast, All, Any, Not }
public enum SkillProgramTargetKind { OtherLiving, AnyLiving, OtherWounded, AnyWounded }
public enum SkillProgramEffectOp { Draw, Recover, LoseHp, GiveSelected, DiscardSelected }
public enum SkillProgramEffectTarget { Owner, SelectedTarget }
public enum SkillProgramTriggerWindow { CardUseTargetsFinalized, CardResponseAccepted, JudgmentReplacing, JudgmentFinalized }
public enum SkillProgramTriggerEffectOp { Draw, Recover, ObtainOpponentHandCard, ReplaceJudgment, SelectTarget, Damage, StartJudgment }
public enum SkillProgramTriggerEffectTarget { Owner, Opponent, SelectedTarget }
public enum SkillProgramTriggerSubject { Owner, Any }
public enum SkillProgramOldJudgmentCardDestination { DiscardPile, OwnerHand }

public sealed class SkillProgramCondition
{
    internal SkillProgramCondition(SkillProgramConditionKind kind, int value, IReadOnlyList<SkillProgramCondition> children)
    {
        Kind = kind;
        Value = value;
        Children = children;
    }

    public SkillProgramConditionKind Kind { get; }
    public int Value { get; }
    public IReadOnlyList<SkillProgramCondition> Children { get; }

    public bool Evaluate(PlayerSkillContext context) => Kind switch
    {
        SkillProgramConditionKind.Always => true,
        SkillProgramConditionKind.OwnTurn => context.IsOwnTurn,
        SkillProgramConditionKind.NotOwnTurn => !context.IsOwnTurn,
        SkillProgramConditionKind.Wounded => context.Hp < context.MaxHp,
        SkillProgramConditionKind.HpAtLeast => context.Hp >= Value,
        SkillProgramConditionKind.HandCountAtLeast => context.HandCount >= Value,
        SkillProgramConditionKind.All => Children.All(child => child.Evaluate(context)),
        SkillProgramConditionKind.Any => Children.Any(child => child.Evaluate(context)),
        SkillProgramConditionKind.Not => !Children[0].Evaluate(context),
        _ => throw new InvalidOperationException($"Unsupported condition kind '{Kind}'.")
    };
}

public sealed class SkillProgramModifier
{
    internal SkillProgramModifier(SkillRuleQuery query, SkillRuleOperation operation, int value, SkillProgramCondition condition) =>
        (Query, Operation, Value, Condition) = (query, operation, value, condition);
    public SkillRuleQuery Query { get; }
    public SkillRuleOperation Operation { get; }
    public int Value { get; }
    public SkillProgramCondition Condition { get; }
}

public sealed class SkillProgramViewAs
{
    internal SkillProgramViewAs(string id, IReadOnlyList<CardKind> inputKinds, IReadOnlyList<Suit> inputSuits,
        CardKind outputKind, bool forPlay, bool forResponse, SkillProgramCondition condition) =>
        (Id, InputKinds, InputSuits, OutputKind, ForPlay, ForResponse, Condition) =
        (id, inputKinds, inputSuits, outputKind, forPlay, forResponse, condition);
    public string Id { get; }
    public IReadOnlyList<CardKind> InputKinds { get; }
    public IReadOnlyList<Suit> InputSuits { get; }
    public CardKind OutputKind { get; }
    public bool ForPlay { get; }
    public bool ForResponse { get; }
    public SkillProgramCondition Condition { get; }
}

public sealed class SkillProgramEffect
{
    internal SkillProgramEffect(SkillProgramEffectOp op, SkillProgramEffectTarget target, int amount, SkillProgramCondition condition) =>
        (Op, Target, Amount, Condition) = (op, target, amount, condition);
    public SkillProgramEffectOp Op { get; }
    public SkillProgramEffectTarget Target { get; }
    public int Amount { get; }
    public SkillProgramCondition Condition { get; }
}

public sealed class SkillProgramActivation
{
    internal SkillProgramActivation(string id, int minCards, int maxCards, int minTargets, int maxTargets,
        SkillProgramTargetKind targetKind, int? usesPerTurn, SkillProgramCondition condition,
        IReadOnlyList<SkillProgramEffect> effects) =>
        (Id, MinCards, MaxCards, MinTargets, MaxTargets, TargetKind, UsesPerTurn, Condition, Effects) =
        (id, minCards, maxCards, minTargets, maxTargets, targetKind, usesPerTurn, condition, effects);
    public string Id { get; }
    public int MinCards { get; }
    public int MaxCards { get; }
    public int MinTargets { get; }
    public int MaxTargets { get; }
    public SkillProgramTargetKind TargetKind { get; }
    public int? UsesPerTurn { get; }
    public SkillProgramCondition Condition { get; }
    public IReadOnlyList<SkillProgramEffect> Effects { get; }
}

/// <summary>
/// A play-phase entry granted by this skill to another character. The provider
/// contributes one matching physical hand card to the living skill owner; kind
/// and suit filters form a union so definitions can express "Dodge or Spade".
/// </summary>
public sealed class SkillProgramContribution
{
    internal SkillProgramContribution(string id, IReadOnlyList<string> providerFactions, Role ownerRole,
        IReadOnlyList<CardKind> cardKinds, IReadOnlyList<Suit> cardSuits, int usesPerPlayPhase) =>
        (Id, ProviderFactions, OwnerRole, CardKinds, CardSuits, UsesPerPlayPhase) =
        (id, providerFactions, ownerRole, cardKinds, cardSuits, usesPerPlayPhase);
    public string Id { get; }
    public IReadOnlyList<string> ProviderFactions { get; }
    public Role OwnerRole { get; }
    public IReadOnlyList<CardKind> CardKinds { get; }
    public IReadOnlyList<Suit> CardSuits { get; }
    public int UsesPerPlayPhase { get; }
}

public sealed class SkillProgramTriggerEffect
{
    internal SkillProgramTriggerEffect(SkillProgramTriggerEffectOp op, SkillProgramTriggerEffectTarget target,
        int amount, SkillProgramCondition condition, IReadOnlyList<CardZoneKind> zones,
        IReadOnlyList<Suit> suits, SkillProgramOldJudgmentCardDestination? oldCardDestination,
        IReadOnlyList<Suit> replacementSuits, int minimumReplacementRank, int maximumReplacementRank,
        SkillProgramTargetKind? targetKind, DamageNature? damageNature, string? judgmentReason) =>
        (Op, Target, Amount, Condition, Zones, Suits, OldCardDestination, ReplacementSuits,
            MinimumReplacementRank, MaximumReplacementRank, TargetKind, DamageNature, JudgmentReason) =
        (op, target, amount, condition, zones, suits, oldCardDestination, replacementSuits,
            minimumReplacementRank, maximumReplacementRank, targetKind, damageNature, judgmentReason);
    public SkillProgramTriggerEffectOp Op { get; }
    public SkillProgramTriggerEffectTarget Target { get; }
    public int Amount { get; }
    public SkillProgramCondition Condition { get; }
    public IReadOnlyList<CardZoneKind> Zones { get; }
    public IReadOnlyList<Suit> Suits { get; }
    public SkillProgramOldJudgmentCardDestination? OldCardDestination { get; }
    public IReadOnlyList<Suit> ReplacementSuits { get; }
    public int MinimumReplacementRank { get; }
    public int MaximumReplacementRank { get; }
    public SkillProgramTargetKind? TargetKind { get; }
    public DamageNature? DamageNature { get; }
    public string? JudgmentReason { get; }
}

public sealed class SkillProgramTrigger
{
    internal SkillProgramTrigger(string id, SkillProgramTriggerWindow window, string? sourceSkillId,
        string? sourceViewAsId, SkillProgramTriggerSubject? subject, IReadOnlyList<Suit> suits,
        int minimumRank, int maximumRank, IReadOnlyList<string> excludedReasons, IReadOnlyList<CardKind> cardKinds,
        bool optional, IReadOnlyList<SkillProgramTriggerEffect> effects) =>
        (Id, Window, SourceSkillId, SourceViewAsId, Subject, Suits, MinimumRank, MaximumRank,
            ExcludedReasons, CardKinds, Optional, Effects) =
        (id, window, sourceSkillId, sourceViewAsId, subject, suits, minimumRank, maximumRank,
            excludedReasons, cardKinds, optional, effects);
    public string Id { get; }
    public SkillProgramTriggerWindow Window { get; }
    public string? SourceSkillId { get; }
    public string? SourceViewAsId { get; }
    public SkillProgramTriggerSubject? Subject { get; }
    public IReadOnlyList<Suit> Suits { get; }
    public int MinimumRank { get; }
    public int MaximumRank { get; }
    public IReadOnlyList<string> ExcludedReasons { get; }
    public IReadOnlyList<CardKind> CardKinds { get; }
    public bool Optional { get; }
    public IReadOnlyList<SkillProgramTriggerEffect> Effects { get; }
}

public sealed class SkillProgram
{
    internal SkillProgram(string id, int revision, string gameplayHash, string runtimeVersion, int minimumRulesVersion,
        IReadOnlyList<SkillProgramModifier> modifiers, IReadOnlyList<SkillProgramViewAs> viewAs,
        IReadOnlyList<SkillProgramActivation> activations, IReadOnlyList<SkillProgramTrigger> triggers,
        IReadOnlyList<SkillProgramContribution> contributions) =>
        (Id, Revision, GameplayHash, RuntimeVersion, MinimumRulesVersion, Modifiers, ViewAs, Activations, Triggers,
            Contributions) =
        (id, revision, gameplayHash, runtimeVersion, minimumRulesVersion, modifiers, viewAs, activations, triggers,
            contributions);
    public string Id { get; }
    public int Revision { get; }
    public string GameplayHash { get; }
    public string RuntimeVersion { get; }
    public int MinimumRulesVersion { get; }
    public IReadOnlyList<SkillProgramModifier> Modifiers { get; }
    public IReadOnlyList<SkillProgramViewAs> ViewAs { get; }
    public IReadOnlyList<SkillProgramActivation> Activations { get; }
    public IReadOnlyList<SkillProgramTrigger> Triggers { get; }
    public IReadOnlyList<SkillProgramContribution> Contributions { get; }
}

public sealed class SkillPresentation
{
    internal SkillPresentation(string name, string description) => (Name, Description) = (name, description);
    public string Name { get; }
    public string Description { get; }
}

public sealed class SkillProgramCatalog
{
    public const string RuntimeVersion = "skill-program-v1";
    private const int MaximumDepth = 16;
    private const int MaximumItems = 256;
    private static readonly SkillProgramCondition Always = new(
        SkillProgramConditionKind.Always, 0, Array.Empty<SkillProgramCondition>());

    private SkillProgramCatalog(IReadOnlyDictionary<string, SkillProgram> programs,
        IReadOnlyDictionary<string, SkillPresentation> presentations) =>
        (Programs, Presentations) = (programs, presentations);

    public IReadOnlyDictionary<string, SkillProgram> Programs { get; }
    public IReadOnlyDictionary<string, SkillPresentation> Presentations { get; }

    public static SkillProgramCatalog Load(string rulesJson, string presentationJson)
    {
        ArgumentNullException.ThrowIfNull(rulesJson);
        ArgumentNullException.ThrowIfNull(presentationJson);
        try
        {
            using var rules = Parse(rulesJson, "rules");
            using var presentation = Parse(presentationJson, "presentation");
            var programs = LoadPrograms(rules.RootElement);
            var presentations = LoadPresentations(presentation.RootElement, programs);
            return new SkillProgramCatalog(ReadOnly(programs), ReadOnly(presentations));
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception exception) when (exception is JsonException or FormatException or OverflowException)
        {
            throw new InvalidOperationException($"Invalid skill program JSON: {exception.Message}", exception);
        }
    }

    private static JsonDocument Parse(string json, string path)
    {
        try
        {
            var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = MaximumDepth
            });
            RejectDuplicateProperties(document.RootElement, path);
            return document;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Invalid JSON at {path}: {exception.Message}", exception);
        }
    }

    private static Dictionary<string, SkillProgram> LoadPrograms(JsonElement root)
    {
        RequireObject(root, "rules");
        CheckProperties(root, "rules", "schemaVersion", "skills");
        var schemaVersion = RequireVersion(root, "rules", 1, 2, 3, 4, 5, 6, 7);
        var runtimeVersion = schemaVersion switch
        {
            1 => RuntimeVersion,
            2 => "skill-program-v2",
            3 => "skill-program-v3",
            4 => "skill-program-v4",
            5 => "skill-program-v5",
            6 => "skill-program-v6",
            _ => "skill-program-v7"
        };
        var minimumRulesVersion = schemaVersion switch
        {
            1 => 79,
            2 => 80,
            3 => 81,
            4 => 82,
            5 => 83,
            6 => 84,
            _ => 85
        };
        var skills = Required(root, "skills", JsonValueKind.Array, "rules");
        CheckCount(skills.GetArrayLength(), "rules.skills");
        var result = new Dictionary<string, SkillProgram>(StringComparer.Ordinal);
        var index = 0;
        foreach (var skill in skills.EnumerateArray())
        {
            var path = $"rules.skills[{index++}]";
            RequireObject(skill, path);
            CheckProperties(skill, path, schemaVersion switch
            {
                1 => ["id", "revision", "modifiers", "viewAs", "activations"],
                < 7 => ["id", "revision", "modifiers", "viewAs", "activations", "triggers"],
                _ => ["id", "revision", "modifiers", "viewAs", "activations", "triggers", "contributions"]
            });
            var id = Identifier(skill, "id", path);
            var skillPath = $"skill '{id}' ({path})";
            if (result.ContainsKey(id)) Fail(skillPath, $"duplicate skill id '{id}'");
            var revision = PositiveInt(skill, "revision", skillPath);
            var modifiers = ReadArray(skill, "modifiers", skillPath, ParseModifier, schemaVersion >= 2);
            var viewAs = ReadArray(skill, "viewAs", skillPath, ParseViewAs, schemaVersion >= 2);
            var activations = ReadArray(skill, "activations", skillPath, ParseActivation, schemaVersion >= 2);
            var triggers = schemaVersion >= 2
                ? ReadArray(skill, "triggers", skillPath,
                    (node, triggerPath) => ParseTrigger(node, triggerPath, schemaVersion), optional: true)
                : Array.Empty<SkillProgramTrigger>();
            var contributions = schemaVersion >= 7
                ? ReadArray(skill, "contributions", skillPath, ParseContribution, optional: true)
                : Array.Empty<SkillProgramContribution>();
            if (modifiers.Count == 0 && viewAs.Count == 0 && activations.Count == 0 && triggers.Count == 0 &&
                contributions.Count == 0)
                Fail(skillPath, "must define at least one modifier, viewAs rule, activation, trigger, or contribution");
            EnsureUniqueIds(viewAs.Select(item => item.Id), skillPath + ".viewAs");
            EnsureUniqueIds(activations.Select(item => item.Id), skillPath + ".activations");
            EnsureUniqueIds(triggers.Select(item => item.Id), skillPath + ".triggers");
            EnsureUniqueIds(contributions.Select(item => item.Id), skillPath + ".contributions");
            EnsureUniqueIds(activations.Select(item => item.Id).Concat(contributions.Select(item => item.Id)),
                skillPath + ".playBindings");
            var hashInput = runtimeVersion + "\n" + Canonicalize(skill);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput))).ToLowerInvariant();
            result.Add(id, new SkillProgram(id, revision, hash, runtimeVersion, minimumRulesVersion,
                modifiers, viewAs, activations, triggers, contributions));
        }
        if (schemaVersion >= 2) ValidateTriggerSources(result);
        return result;
    }

    private static Dictionary<string, SkillPresentation> LoadPresentations(JsonElement root,
        IReadOnlyDictionary<string, SkillProgram> programs)
    {
        RequireObject(root, "presentation");
        CheckProperties(root, "presentation", "schemaVersion", "skills");
        RequireVersion(root, "presentation", 1);
        var skills = Required(root, "skills", JsonValueKind.Object, "presentation");
        CheckCount(skills.EnumerateObject().Count(), "presentation.skills");
        var result = new Dictionary<string, SkillPresentation>(StringComparer.Ordinal);
        foreach (var property in skills.EnumerateObject())
        {
            var id = property.Name;
            var path = $"presentation.skills.{id}";
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128) Fail(path, "skill id must contain 1 to 128 characters");
            if (!programs.ContainsKey(id)) Fail(path, $"presentation references unknown skill '{id}'");
            RequireObject(property.Value, path);
            CheckProperties(property.Value, path, "name", "description");
            result.Add(id, new SkillPresentation(NonEmptyString(property.Value, "name", path),
                NonEmptyString(property.Value, "description", path)));
        }
        foreach (var id in programs.Keys)
            if (!result.ContainsKey(id)) Fail("presentation.skills", $"missing presentation for skill '{id}'");
        return result;
    }

    private static SkillProgramModifier ParseModifier(JsonElement node, string path)
    {
        RequireObject(node, path);
        CheckProperties(node, path, "query", "operation", "value", "condition");
        var query = EnumValue<SkillRuleQuery>(node, "query", path);
        var operation = EnumValue<SkillRuleOperation>(node, "operation", path);
        var value = RequiredInt(node, "value", path);
        if (value is < -1024 or > 1024)
            Fail(path + ".value", "modifier value must be between -1024 and 1024");
        if (operation == SkillRuleOperation.Add && value == 0)
            Fail(path + ".value", "add requires a non-zero value");
        if (operation == SkillRuleOperation.Unlimited)
        {
            if (query != SkillRuleQuery.SlashLimit) Fail(path, "unlimited is supported only for slashLimit");
            if (value != 0) Fail(path + ".value", "unlimited requires value 0");
        }
        return new SkillProgramModifier(query, operation, value, OptionalCondition(node, path));
    }

    private static SkillProgramViewAs ParseViewAs(JsonElement node, string path)
    {
        RequireObject(node, path);
        CheckProperties(node, path, "id", "inputKinds", "inputSuits", "outputKind", "forPlay", "forResponse", "condition");
        var id = Identifier(node, "id", path);
        var inputs = EnumArray<CardKind>(node, "inputKinds", path);
        var suits = EnumArray<Suit>(node, "inputSuits", path);
        var output = EnumValue<CardKind>(node, "outputKind", path);
        if (output is not (CardKind.Slash or CardKind.Dodge)) Fail(path + ".outputKind", "only slash or dodge is supported");
        var forPlay = RequiredBool(node, "forPlay", path);
        var forResponse = RequiredBool(node, "forResponse", path);
        if (!forPlay && !forResponse) Fail(path, "at least one of forPlay or forResponse must be true");
        if (output == CardKind.Dodge && forPlay)
            Fail(path + ".forPlay", "dodge is response-only and cannot be played proactively");
        if (inputs.Count > 0 && inputs.All(kind => kind == output))
            Fail(path + ".inputKinds", "viewAs must change at least one accepted input kind");
        return new SkillProgramViewAs(id, inputs, suits, output, forPlay, forResponse, OptionalCondition(node, path));
    }

    private static SkillProgramActivation ParseActivation(JsonElement node, string path)
    {
        RequireObject(node, path);
        CheckProperties(node, path, "id", "minCards", "maxCards", "minTargets", "maxTargets", "targetKind", "usesPerTurn", "condition", "effects");
        var id = Identifier(node, "id", path);
        var minCards = NonNegativeInt(node, "minCards", path);
        var maxCards = NonNegativeInt(node, "maxCards", path);
        var minTargets = NonNegativeInt(node, "minTargets", path);
        var maxTargets = NonNegativeInt(node, "maxTargets", path);
        if (minCards > maxCards) Fail(path, "minCards cannot exceed maxCards");
        if (maxCards > 64) Fail(path + ".maxCards", "must not exceed 64");
        if (minTargets > maxTargets) Fail(path, "minTargets cannot exceed maxTargets");
        if (maxTargets > 1) Fail(path + ".maxTargets", "must not exceed 1");
        var targetKind = EnumValue<SkillProgramTargetKind>(node, "targetKind", path);
        int? uses = null;
        if (node.TryGetProperty("usesPerTurn", out var usesNode))
        {
            if (usesNode.ValueKind == JsonValueKind.Null) uses = null;
            else { uses = GetInt(usesNode, path + ".usesPerTurn"); if (uses <= 0) Fail(path + ".usesPerTurn", "must be positive or null"); }
        }
        else Fail(path, "missing required property 'usesPerTurn'");
        var effects = ReadArray(node, "effects", path, ParseEffect);
        if (effects.Count == 0) Fail(path + ".effects", "must contain at least one effect");
        ValidateActivation(path, minCards, maxCards, minTargets, maxTargets, effects);
        return new SkillProgramActivation(id, minCards, maxCards, minTargets, maxTargets, targetKind, uses,
            OptionalCondition(node, path), effects);
    }

    private static SkillProgramContribution ParseContribution(JsonElement node, string path)
    {
        RequireObject(node, path);
        CheckProperties(node, path, "id", "providerFactions", "ownerRole", "cardKinds", "cardSuits",
            "usesPerPlayPhase");
        var id = Identifier(node, "id", path);
        var providerFactions = StringArray(node, "providerFactions", path);
        if (providerFactions.Count == 0)
            Fail(path + ".providerFactions", "must contain at least one faction id");
        var ownerRole = EnumValue<Role>(node, "ownerRole", path);
        var cardKinds = EnumArray<CardKind>(node, "cardKinds", path);
        var cardSuits = EnumArray<Suit>(node, "cardSuits", path);
        if (cardKinds.Count == 0 && cardSuits.Count == 0)
            Fail(path, "must accept at least one physical card kind or suit");
        var usesPerPlayPhase = PositiveInt(node, "usesPerPlayPhase", path);
        if (usesPerPlayPhase > 64)
            Fail(path + ".usesPerPlayPhase", "must not exceed 64");
        return new SkillProgramContribution(id, providerFactions, ownerRole, cardKinds, cardSuits,
            usesPerPlayPhase);
    }

    private static SkillProgramEffect ParseEffect(JsonElement node, string path)
    {
        RequireObject(node, path);
        CheckProperties(node, path, "op", "target", "amount", "condition");
        var op = EnumValue<SkillProgramEffectOp>(node, "op", path);
        var target = EnumValue<SkillProgramEffectTarget>(node, "target", path);
        var amount = PositiveInt(node, "amount", path);
        if (amount > 1024) Fail(path + ".amount", "must not exceed 1024");
        if (op == SkillProgramEffectOp.GiveSelected && target != SkillProgramEffectTarget.SelectedTarget)
            Fail(path + ".target", "giveSelected requires selectedTarget");
        if (op == SkillProgramEffectOp.DiscardSelected && target != SkillProgramEffectTarget.Owner)
            Fail(path + ".target", "discardSelected requires owner");
        var condition = OptionalCondition(node, path);
        if ((op is SkillProgramEffectOp.GiveSelected or SkillProgramEffectOp.DiscardSelected) &&
            condition.Kind != SkillProgramConditionKind.Always)
            Fail(path + ".condition", "selected-card consumption must be unconditional after activation validation");
        return new SkillProgramEffect(op, target, amount, condition);
    }

    private static SkillProgramTrigger ParseTrigger(JsonElement node, string path, int schemaVersion)
    {
        RequireObject(node, path);
        CheckProperties(node, path, schemaVersion switch
        {
            2 => ["id", "window", "sourceSkillId", "sourceViewAsId", "optional", "effects"],
            < 6 => ["id",
                "window",
                "sourceSkillId",
                "sourceViewAsId",
                "subject",
                "suits",
                "minimumRank",
                "maximumRank",
                "excludedReasons",
                "optional",
                "effects"],
            _ => ["id",
                "window",
                "sourceSkillId",
                "sourceViewAsId",
                "cardKinds",
                "subject",
                "suits",
                "minimumRank",
                "maximumRank",
                "excludedReasons",
                "optional",
                "effects"]
        });
        var id = Identifier(node, "id", path);
        var window = EnumValue<SkillProgramTriggerWindow>(node, "window", path);
        string? sourceSkillId = null;
        string? sourceViewAsId = null;
        SkillProgramTriggerSubject? subject = null;
        IReadOnlyList<Suit> suits = Array.Empty<Suit>();
        IReadOnlyList<string> excludedReasons = Array.Empty<string>();
        IReadOnlyList<CardKind> cardKinds = Array.Empty<CardKind>();
        var minimumRank = 1;
        var maximumRank = 13;
        if (window == SkillProgramTriggerWindow.JudgmentFinalized)
        {
            if (schemaVersion < 3) Fail(path + ".window", "judgmentFinalized requires schema version 3");
            if (node.TryGetProperty("sourceSkillId", out _) || node.TryGetProperty("sourceViewAsId", out _) ||
                node.TryGetProperty("cardKinds", out _))
                Fail(path, "judgmentFinalized does not accept card-conversion source fields");
            subject = EnumValue<SkillProgramTriggerSubject>(node, "subject", path);
            if (subject != SkillProgramTriggerSubject.Owner)
                Fail(path + ".subject", "judgmentFinalized currently supports only owner judgments");
            suits = EnumArray<Suit>(node, "suits", path);
            if (suits.Count == 0) Fail(path + ".suits", "must contain at least one final suit");
            minimumRank = RequiredInt(node, "minimumRank", path);
            maximumRank = RequiredInt(node, "maximumRank", path);
            if (minimumRank is < 1 or > 13 || maximumRank is < 1 or > 13 || minimumRank > maximumRank)
                Fail(path, "judgment rank bounds must satisfy 1 <= minimumRank <= maximumRank <= 13");
            excludedReasons = StringArray(node, "excludedReasons", path);
        }
        else if (window == SkillProgramTriggerWindow.JudgmentReplacing)
        {
            if (schemaVersion < 4) Fail(path + ".window", "judgmentReplacing requires schema version 4");
            if (node.TryGetProperty("sourceSkillId", out _) || node.TryGetProperty("sourceViewAsId", out _) ||
                node.TryGetProperty("cardKinds", out _) ||
                node.TryGetProperty("suits", out _) || node.TryGetProperty("minimumRank", out _) ||
                node.TryGetProperty("maximumRank", out _))
                Fail(path, "judgmentReplacing accepts subject and excludedReasons, not card-action or final-result fields");
            subject = EnumValue<SkillProgramTriggerSubject>(node, "subject", path);
            excludedReasons = StringArray(node, "excludedReasons", path);
        }
        else
        {
            if (node.TryGetProperty("subject", out _) || node.TryGetProperty("suits", out _) ||
                node.TryGetProperty("minimumRank", out _) || node.TryGetProperty("maximumRank", out _) ||
                node.TryGetProperty("excludedReasons", out _))
                Fail(path, "card-action trigger windows do not accept judgment fields");
            if (schemaVersion >= 6 && node.TryGetProperty("cardKinds", out _))
            {
                if (node.TryGetProperty("sourceSkillId", out _) || node.TryGetProperty("sourceViewAsId", out _))
                    Fail(path, "cardKinds cannot be combined with card-conversion source fields");
                cardKinds = EnumArray<CardKind>(node, "cardKinds", path);
                if (cardKinds.Count == 0) Fail(path + ".cardKinds", "must contain at least one effective card kind");
                var supported = window == SkillProgramTriggerWindow.CardUseTargetsFinalized
                    ? cardKinds.All(kind => kind is CardKind.Slash or CardKind.FireSlash or
                        CardKind.ThunderSlash or CardKind.Lightning)
                    : cardKinds.All(kind => kind is CardKind.Slash or CardKind.FireSlash or
                        CardKind.ThunderSlash or CardKind.Dodge);
                if (!supported)
                    Fail(path + ".cardKinds", $"contains a card kind unsupported by {Camel(window)}");
            }
            else
            {
                sourceSkillId = Identifier(node, "sourceSkillId", path);
                if (node.TryGetProperty("sourceViewAsId", out var sourceViewAs))
                {
                    if (sourceViewAs.ValueKind == JsonValueKind.String)
                    {
                        sourceViewAsId = sourceViewAs.GetString();
                        if (string.IsNullOrWhiteSpace(sourceViewAsId) || sourceViewAsId.Length > 128)
                            Fail(path + ".sourceViewAsId", "must be null or contain 1 to 128 characters");
                    }
                    else if (sourceViewAs.ValueKind != JsonValueKind.Null)
                        Fail(path + ".sourceViewAsId", "must be a string or null");
                }
            }
        }
        var optional = RequiredBool(node, "optional", path);
        var effects = ReadArray(node, "effects", path,
            (effect, effectPath) => ParseTriggerEffect(effect, effectPath, window, schemaVersion));
        if (effects.Count == 0) Fail(path + ".effects", "must contain at least one effect");
        if (window == SkillProgramTriggerWindow.JudgmentReplacing &&
            (effects[0].Op != SkillProgramTriggerEffectOp.ReplaceJudgment ||
             effects.Skip(1).Any(effect => effect.Op is not
                 (SkillProgramTriggerEffectOp.Draw or SkillProgramTriggerEffectOp.Recover))))
            Fail(path + ".effects",
                "judgmentReplacing requires replaceJudgment first, followed only by draw or recover effects");
        if (window == SkillProgramTriggerWindow.JudgmentFinalized)
            ValidateFinalJudgmentEffects(path, schemaVersion, effects);
        return new SkillProgramTrigger(id, window, sourceSkillId, sourceViewAsId, subject, suits,
            minimumRank, maximumRank, excludedReasons, cardKinds, optional, effects);
    }

    private static SkillProgramTriggerEffect ParseTriggerEffect(
        JsonElement node, string path, SkillProgramTriggerWindow window, int schemaVersion)
    {
        RequireObject(node, path);
        CheckProperties(node, path, schemaVersion switch
        {
            < 4 => ["op", "target", "amount", "condition"],
            4 => ["op",
                "target",
                "amount",
                "condition",
                "zones",
                "suits",
                "oldCardDestination",
                "replacementSuits",
                "minimumReplacementRank",
                "maximumReplacementRank"],
            5 => ["op",
                "target",
                "amount",
                "condition",
                "zones",
                "suits",
                "oldCardDestination",
                "replacementSuits",
                "minimumReplacementRank",
                "maximumReplacementRank",
                "targetKind",
                "nature"],
            _ => ["op",
                "target",
                "amount",
                "condition",
                "zones",
                "suits",
                "oldCardDestination",
                "replacementSuits",
                "minimumReplacementRank",
                "maximumReplacementRank",
                "targetKind",
                "nature",
                "judgmentReason"]
        });
        var op = EnumValue<SkillProgramTriggerEffectOp>(node, "op", path);
        var target = EnumValue<SkillProgramTriggerEffectTarget>(node, "target", path);
        var zones = (IReadOnlyList<CardZoneKind>)Array.Empty<CardZoneKind>();
        var suits = (IReadOnlyList<Suit>)Array.Empty<Suit>();
        SkillProgramOldJudgmentCardDestination? oldCardDestination = null;
        var replacementSuits = (IReadOnlyList<Suit>)Array.Empty<Suit>();
        var minimumReplacementRank = 1;
        var maximumReplacementRank = 13;
        SkillProgramTargetKind? targetKind = null;
        DamageNature? damageNature = null;
        string? judgmentReason = null;
        var amount = 0;
        if (op == SkillProgramTriggerEffectOp.ReplaceJudgment)
        {
            if (window != SkillProgramTriggerWindow.JudgmentReplacing)
                Fail(path + ".op", "replaceJudgment is supported only in judgmentReplacing");
            if (node.TryGetProperty("amount", out _) || node.TryGetProperty("replacementSuits", out _) ||
                node.TryGetProperty("minimumReplacementRank", out _) ||
                node.TryGetProperty("maximumReplacementRank", out _))
                Fail(path, "replaceJudgment does not accept amount or replacement-result filters");
            if (target != SkillProgramTriggerEffectTarget.Owner)
                Fail(path + ".target", "replaceJudgment requires target owner");
            zones = EnumArray<CardZoneKind>(node, "zones", path);
            if (zones.Count == 0 || zones.Any(zone => zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)))
                Fail(path + ".zones", "replaceJudgment requires hand and/or equipment zones");
            suits = EnumArray<Suit>(node, "suits", path);
            if (suits.Count == 0) Fail(path + ".suits", "replaceJudgment requires at least one effective suit");
            oldCardDestination = EnumValue<SkillProgramOldJudgmentCardDestination>(
                node, "oldCardDestination", path);
        }
        else if (op == SkillProgramTriggerEffectOp.SelectTarget)
        {
            if (schemaVersion < 5 || window != SkillProgramTriggerWindow.JudgmentFinalized)
                Fail(path + ".op", "selectTarget requires schema version 5 and judgmentFinalized");
            if (target != SkillProgramTriggerEffectTarget.SelectedTarget)
                Fail(path + ".target", "selectTarget requires selectedTarget");
            if (node.TryGetProperty("amount", out _) || node.TryGetProperty("nature", out _))
                Fail(path, "selectTarget accepts targetKind, not amount or nature");
            targetKind = EnumValue<SkillProgramTargetKind>(node, "targetKind", path);
            if (OptionalCondition(node, path).Kind != SkillProgramConditionKind.Always)
                Fail(path + ".condition", "selectTarget must remain unconditional after trigger activation");
        }
        else if (op == SkillProgramTriggerEffectOp.Damage)
        {
            if (schemaVersion < 5 || window != SkillProgramTriggerWindow.JudgmentFinalized)
                Fail(path + ".op", "damage requires schema version 5 and judgmentFinalized");
            if (target != SkillProgramTriggerEffectTarget.SelectedTarget)
                Fail(path + ".target", "judgment damage requires selectedTarget");
            if (node.TryGetProperty("targetKind", out _))
                Fail(path, "damage uses the previously selected target and does not accept targetKind");
            amount = PositiveInt(node, "amount", path);
            if (amount > 20) Fail(path + ".amount", "judgment damage amount must be between 1 and 20");
            damageNature = EnumValue<DamageNature>(node, "nature", path);
        }
        else if (op == SkillProgramTriggerEffectOp.StartJudgment)
        {
            if (schemaVersion < 6 || window is not
                (SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.CardResponseAccepted))
                Fail(path + ".op", "startJudgment requires schema version 6 and a card-action window");
            if (target != SkillProgramTriggerEffectTarget.Owner)
                Fail(path + ".target", "startJudgment requires owner");
            if (node.TryGetProperty("amount", out _) || node.TryGetProperty("targetKind", out _) ||
                node.TryGetProperty("nature", out _) || node.TryGetProperty("zones", out _) ||
                node.TryGetProperty("suits", out _) || node.TryGetProperty("oldCardDestination", out _) ||
                node.TryGetProperty("replacementSuits", out _) ||
                node.TryGetProperty("minimumReplacementRank", out _) ||
                node.TryGetProperty("maximumReplacementRank", out _))
                Fail(path, "startJudgment accepts only target, condition and judgmentReason");
            judgmentReason = NonEmptyString(node, "judgmentReason", path);
            if (judgmentReason.Length > 128)
                Fail(path + ".judgmentReason", "must not exceed 128 characters");
        }
        else
        {
            amount = PositiveInt(node, "amount", path);
            if (node.TryGetProperty("zones", out _) || node.TryGetProperty("suits", out _) ||
                node.TryGetProperty("oldCardDestination", out _))
                Fail(path, "only replaceJudgment accepts card-selection and old-card fields");
            if (window == SkillProgramTriggerWindow.JudgmentReplacing)
            {
                if (op is not (SkillProgramTriggerEffectOp.Draw or SkillProgramTriggerEffectOp.Recover) ||
                    target != SkillProgramTriggerEffectTarget.Owner)
                    Fail(path, "judgmentReplacing follow-up effects support only owner draw or recover");
                replacementSuits = EnumArray<Suit>(node, "replacementSuits", path);
                if (replacementSuits.Count == 0)
                    Fail(path + ".replacementSuits", "must contain at least one committed replacement suit");
                minimumReplacementRank = RequiredInt(node, "minimumReplacementRank", path);
                maximumReplacementRank = RequiredInt(node, "maximumReplacementRank", path);
                if (minimumReplacementRank is < 1 or > 13 || maximumReplacementRank is < 1 or > 13 ||
                    minimumReplacementRank > maximumReplacementRank)
                    Fail(path, "replacement rank bounds must satisfy 1 <= minimum <= maximum <= 13");
            }
            else if (node.TryGetProperty("replacementSuits", out _) ||
                     node.TryGetProperty("minimumReplacementRank", out _) ||
                     node.TryGetProperty("maximumReplacementRank", out _))
                Fail(path, "replacement-result filters are supported only in judgmentReplacing");
            if (node.TryGetProperty("targetKind", out _) || node.TryGetProperty("nature", out _))
                Fail(path, "targetKind and nature are supported only by selectTarget and damage");
        }
        if (op != SkillProgramTriggerEffectOp.StartJudgment && node.TryGetProperty("judgmentReason", out _))
            Fail(path, "judgmentReason is supported only by startJudgment");
        if (window == SkillProgramTriggerWindow.JudgmentFinalized &&
            op is not (SkillProgramTriggerEffectOp.Draw or SkillProgramTriggerEffectOp.Recover or
                SkillProgramTriggerEffectOp.SelectTarget or SkillProgramTriggerEffectOp.Damage))
            Fail(path,
                "judgmentFinalized currently supports only owner draw or recover effects, or a selected-target damage sequence");
        if (window == SkillProgramTriggerWindow.JudgmentFinalized &&
            (op is SkillProgramTriggerEffectOp.Draw or SkillProgramTriggerEffectOp.Recover) &&
            target != SkillProgramTriggerEffectTarget.Owner)
            Fail(path, "judgmentFinalized currently supports only owner draw or recover effects");
        if (op is SkillProgramTriggerEffectOp.Draw or SkillProgramTriggerEffectOp.Recover)
        {
            if (amount > 20) Fail(path + ".amount", "draw and recover amount must be between 1 and 20");
        }
        else if (op == SkillProgramTriggerEffectOp.ObtainOpponentHandCard &&
                 (amount != 1 || target != SkillProgramTriggerEffectTarget.Owner))
            Fail(path, "obtainOpponentHandCard requires amount 1 and target owner");
        return new SkillProgramTriggerEffect(op, target, amount, OptionalCondition(node, path),
            zones, suits, oldCardDestination, replacementSuits,
            minimumReplacementRank, maximumReplacementRank, targetKind, damageNature, judgmentReason);
    }

    private static void ValidateFinalJudgmentEffects(
        string path,
        int schemaVersion,
        IReadOnlyList<SkillProgramTriggerEffect> effects)
    {
        var selectionIndexes = effects
            .Select((effect, index) => (effect, index))
            .Where(item => item.effect.Op == SkillProgramTriggerEffectOp.SelectTarget)
            .Select(item => item.index)
            .ToArray();
        var damageIndexes = effects
            .Select((effect, index) => (effect, index))
            .Where(item => item.effect.Op == SkillProgramTriggerEffectOp.Damage)
            .Select(item => item.index)
            .ToArray();
        if (selectionIndexes.Length == 0 && damageIndexes.Length == 0) return;
        if (schemaVersion < 5)
            Fail(path + ".effects", "selected-target judgment damage requires schema version 5");
        if (selectionIndexes.Length != 1 || selectionIndexes[0] != 0 || damageIndexes.Length == 0 ||
            damageIndexes.Any(index => index <= selectionIndexes[0]))
            Fail(path + ".effects",
                "judgment damage requires exactly one selectTarget first and at least one later damage effect");
    }

    private static void ValidateTriggerSources(IReadOnlyDictionary<string, SkillProgram> programs)
    {
        foreach (var owner in programs.Values)
            foreach (var trigger in owner.Triggers)
            {
                var path = $"skill '{owner.Id}'.triggers.{trigger.Id}";
                if (trigger.Window is SkillProgramTriggerWindow.JudgmentFinalized or
                    SkillProgramTriggerWindow.JudgmentReplacing) continue;
                if (trigger.CardKinds.Count > 0) continue;
                var sourceSkillId = trigger.SourceSkillId;
                if (sourceSkillId is null)
                    Fail(path + ".sourceSkillId", $"references unknown skill '{trigger.SourceSkillId}'");
                if (!programs.TryGetValue(sourceSkillId!, out var source))
                    Fail(path + ".sourceSkillId", $"references unknown skill '{sourceSkillId}'");
                if (source.ViewAs.Count == 0)
                    Fail(path + ".sourceSkillId", $"skill '{trigger.SourceSkillId}' has no viewAs rules");
                var candidates = trigger.SourceViewAsId is null
                    ? source.ViewAs
                    : source.ViewAs.Where(rule => rule.Id == trigger.SourceViewAsId).ToArray();
                if (trigger.SourceViewAsId is not null && candidates.Count == 0)
                    Fail(path + ".sourceViewAsId", $"references unknown viewAs '{trigger.SourceViewAsId}'");
                var supported = trigger.Window switch
                {
                    SkillProgramTriggerWindow.CardUseTargetsFinalized => candidates.Any(rule => rule.ForPlay),
                    SkillProgramTriggerWindow.CardResponseAccepted => candidates.Any(rule => rule.ForResponse),
                    _ => false
                };
                if (!supported)
                    Fail(path, $"source viewAs does not support window '{Camel(trigger.Window)}'");
            }
    }

    private static string Camel<T>(T value) where T : struct, Enum
    {
        var name = Enum.GetName(value)!;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static void ValidateActivation(string path, int minCards, int maxCards, int minTargets, int maxTargets,
        IReadOnlyList<SkillProgramEffect> effects)
    {
        if (effects.Any(effect => effect.Target == SkillProgramEffectTarget.SelectedTarget) && minTargets != 1)
            Fail(path, "selectedTarget effects require exactly one required target");
        if (maxTargets == 0 && effects.Any(effect => effect.Target == SkillProgramEffectTarget.SelectedTarget))
            Fail(path, "an effect uses selectedTarget but the activation selects no target");
        var consumers = effects.Where(effect => effect.Op is SkillProgramEffectOp.GiveSelected or SkillProgramEffectOp.DiscardSelected).ToArray();
        if (consumers.Length > 1) Fail(path + ".effects", "selected cards may be consumed only once");
        if (maxCards == 0 && consumers.Length != 0) Fail(path, "selected-card effect requires selected cards");
        if (maxCards > 0 && consumers.Length != 1) Fail(path, "selected cards require exactly one giveSelected or discardSelected effect");
        if (consumers.Length == 1 && (minCards != maxCards || consumers[0].Amount != minCards))
            Fail(path, "selected-card consumption amount must equal the exact selected card count");
    }

    private static SkillProgramCondition OptionalCondition(JsonElement owner, string path) =>
        owner.TryGetProperty("condition", out var condition) ? ParseCondition(condition, path + ".condition", 0) : Always;

    private static SkillProgramCondition ParseCondition(JsonElement node, string path, int depth)
    {
        if (depth >= MaximumDepth) Fail(path, $"condition nesting exceeds {MaximumDepth}");
        RequireObject(node, path);
        CheckProperties(node, path, "kind", "value", "children");
        var kind = EnumValue<SkillProgramConditionKind>(node, "kind", path);
        var hasValue = node.TryGetProperty("value", out var valueNode);
        var hasChildren = node.TryGetProperty("children", out var childrenNode);
        var value = hasValue ? GetInt(valueNode, path + ".value") : 0;
        var children = new List<SkillProgramCondition>();
        if (hasChildren)
        {
            if (childrenNode.ValueKind != JsonValueKind.Array) Fail(path + ".children", "must be an array");
            CheckCount(childrenNode.GetArrayLength(), path + ".children");
            var index = 0;
            foreach (var child in childrenNode.EnumerateArray()) children.Add(ParseCondition(child, $"{path}.children[{index++}]", depth + 1));
        }
        var needsValue = kind is SkillProgramConditionKind.HpAtLeast or SkillProgramConditionKind.HandCountAtLeast;
        if (needsValue != hasValue) Fail(path, needsValue ? "this condition requires value" : "this condition does not accept value");
        if (needsValue && value < 0) Fail(path + ".value", "must be non-negative");
        var composite = kind is SkillProgramConditionKind.All or SkillProgramConditionKind.Any or SkillProgramConditionKind.Not;
        if (composite != hasChildren) Fail(path, composite ? "this condition requires children" : "this condition does not accept children");
        if (kind == SkillProgramConditionKind.Not && children.Count != 1) Fail(path + ".children", "not requires exactly one child");
        if (kind is SkillProgramConditionKind.All or SkillProgramConditionKind.Any && children.Count == 0)
            Fail(path + ".children", "all and any require at least one child");
        return new SkillProgramCondition(kind, value, new ReadOnlyCollection<SkillProgramCondition>(children));
    }

    private static IReadOnlyList<T> ReadArray<T>(JsonElement owner, string name, string path,
        Func<JsonElement, string, T> parser, bool optional = false)
    {
        if (optional && !owner.TryGetProperty(name, out _)) return Array.Empty<T>();
        var array = Required(owner, name, JsonValueKind.Array, path);
        CheckCount(array.GetArrayLength(), path + "." + name);
        var result = new List<T>();
        var index = 0;
        foreach (var node in array.EnumerateArray()) result.Add(parser(node, $"{path}.{name}[{index++}]"));
        return new ReadOnlyCollection<T>(result);
    }

    private static IReadOnlyList<T> EnumArray<T>(JsonElement owner, string name, string path) where T : struct, Enum
    {
        var values = ReadArray(owner, name, path, (node, itemPath) => ParseEnum<T>(node, itemPath));
        if (values.Distinct().Count() != values.Count) Fail(path + "." + name, "contains duplicate values");
        return values;
    }

    private static IReadOnlyList<string> StringArray(JsonElement owner, string name, string path)
    {
        var values = ReadArray(owner, name, path, (node, itemPath) =>
        {
            if (node.ValueKind != JsonValueKind.String) Fail(itemPath, "must be a string");
            var value = node.GetString()!;
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
                Fail(itemPath, "must contain 1 to 128 characters");
            return value;
        });
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Count)
            Fail(path + "." + name, "contains duplicate values");
        return values;
    }

    private static T EnumValue<T>(JsonElement owner, string name, string path) where T : struct, Enum =>
        ParseEnum<T>(Required(owner, name, JsonValueKind.String, path), path + "." + name);

    private static T ParseEnum<T>(JsonElement node, string path) where T : struct, Enum
    {
        if (node.ValueKind != JsonValueKind.String) Fail(path, "must be a camelCase string");
        var text = node.GetString()!;
        foreach (var value in Enum.GetValues<T>())
        {
            var name = Enum.GetName(value)!;
            var camel = char.ToLowerInvariant(name[0]) + name[1..];
            if (text == camel) return value;
        }
        Fail(path, $"unsupported {typeof(T).Name} value '{text}'");
        return default;
    }

    private static string Canonicalize(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, element);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                { writer.WritePropertyName(property.Name); WriteCanonical(writer, property.Value); }
                writer.WriteEndObject(); break;
            case JsonValueKind.Array:
                writer.WriteStartArray(); foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item); writer.WriteEndArray(); break;
            case JsonValueKind.String: writer.WriteStringValue(element.GetString()); break;
            case JsonValueKind.Number: writer.WriteRawValue(element.GetRawText(), skipInputValidation: false); break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            case JsonValueKind.Null: writer.WriteNullValue(); break;
            default: throw new InvalidOperationException("Unsupported JSON token in canonical skill program.");
        }
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

    private static void CheckProperties(JsonElement node, string path, params string[] allowed)
    {
        var set = allowed.ToHashSet(StringComparer.Ordinal);
        foreach (var property in node.EnumerateObject())
            if (!set.Contains(property.Name)) Fail(path + "." + property.Name, "unsupported property");
    }

    private static int RequireVersion(JsonElement root, string path, params int[] supported)
    {
        var version = RequiredInt(root, "schemaVersion", path);
        if (!supported.Contains(version))
            Fail(path + ".schemaVersion", $"unsupported schema version {version}; expected {string.Join(" or ", supported)}");
        return version;
    }

    private static JsonElement Required(JsonElement owner, string name, JsonValueKind kind, string path)
    {
        if (!owner.TryGetProperty(name, out var value)) { Fail(path, $"missing required property '{name}'"); return default; }
        if (value.ValueKind != kind) Fail(path + "." + name, $"must be {kind}");
        return value;
    }

    private static string Identifier(JsonElement owner, string name, string path)
    {
        var value = NonEmptyString(owner, name, path);
        if (value.Length > 128) Fail(path + "." + name, "must not exceed 128 characters");
        return value;
    }

    private static string NonEmptyString(JsonElement owner, string name, string path)
    {
        var value = Required(owner, name, JsonValueKind.String, path).GetString()!;
        if (string.IsNullOrWhiteSpace(value)) Fail(path + "." + name, "must not be empty");
        return value;
    }

    private static int RequiredInt(JsonElement owner, string name, string path) => GetInt(Required(owner, name, JsonValueKind.Number, path), path + "." + name);
    private static int PositiveInt(JsonElement owner, string name, string path) { var value = RequiredInt(owner, name, path); if (value <= 0) Fail(path + "." + name, "must be positive"); return value; }
    private static int NonNegativeInt(JsonElement owner, string name, string path) { var value = RequiredInt(owner, name, path); if (value < 0) Fail(path + "." + name, "must be non-negative"); return value; }
    private static int GetInt(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Number) Fail(path, "must be a 32-bit integer");
        if (!value.TryGetInt32(out var result)) Fail(path, "must be a 32-bit integer");
        return result;
    }
    private static bool RequiredBool(JsonElement owner, string name, string path)
    {
        if (!owner.TryGetProperty(name, out var value)) { Fail(path, $"missing required property '{name}'"); return false; }
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) Fail(path + "." + name, "must be a boolean");
        return value.GetBoolean();
    }
    private static void RequireObject(JsonElement node, string path) { if (node.ValueKind != JsonValueKind.Object) Fail(path, "must be an object"); }
    private static void CheckCount(int count, string path) { if (count > MaximumItems) Fail(path, $"contains more than {MaximumItems} items"); }
    private static void EnsureUniqueIds(IEnumerable<string> ids, string path) { var seen = new HashSet<string>(StringComparer.Ordinal); foreach (var id in ids) if (!seen.Add(id)) Fail(path, $"duplicate id '{id}'"); }
    private static IReadOnlyDictionary<string, T> ReadOnly<T>(Dictionary<string, T> source) => new ReadOnlyDictionary<string, T>(source);
    [DoesNotReturn]
    private static void Fail(string path, string message) => throw new InvalidOperationException($"Invalid skill program at {path}: {message}.");
}
