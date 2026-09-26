using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ProgramToolCommands
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0 || args[0] is not
            ("--program-capabilities" or "--program-operation" or "--validate-skill-program"))
            return false;
        try
        {
            switch (args[0])
            {
                case "--program-capabilities" when args.Length == 1:
                    WriteCapabilities();
                    break;
                case "--program-operation" when args.Length == 2:
                    WriteOperation(args[1]);
                    break;
                case "--validate-skill-program" when args.Length == 3:
                    Validate(args[1], args[2]);
                    break;
                default:
                    Console.Error.WriteLine(args[0] switch
                    {
                        "--program-capabilities" => "Usage: --program-capabilities",
                        "--program-operation" => "Usage: --program-operation <op>",
                        _ => "Usage: --validate-skill-program <rules.json> <presentation.json>"
                    });
                    exitCode = 2;
                    break;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Skill program inspection failed: {exception.Message}");
            exitCode = 1;
        }
        return true;
    }

    private static void WriteOperation(string operationName)
    {
        if (!Enum.TryParse<SkillProgramEffectOp>(operationName, ignoreCase: true, out var op) ||
            !Enum.IsDefined(op))
            throw new InvalidOperationException($"Unknown program operation '{operationName}'.");
        var descriptor = ProgramOperationCatalog.Default.Resolve(op);
        var availableEntries = EntryPoints().Where(entry =>
        {
            var available = ProgramEntryCapabilities.For(entry.Window);
            return (available & descriptor.RequiredCapabilities) == descriptor.RequiredCapabilities;
        }).Select(entry => entry.Name).ToArray();
        var samples = FindEmbeddedSamples(op);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            operation = op.ToString(),
            descriptor = descriptor.GetType().Name,
            handler = descriptor.Handler.GetType().Name,
            interaction = descriptor.Interaction.ToString(),
            aiSemantic = descriptor.AiPolicy.Semantic.ToString(),
            requiredCapabilities = Names(descriptor.RequiredCapabilities),
            availableEntries,
            examples = samples,
            exampleStatus = samples.Length == 0 ? "empty: no formal example for this operation" : "embedded formal examples",
            note = "Examples are copyable starting points only. A node may depend on bindings produced elsewhere in its composition; load the complete composition and verify it in a scenario."
        }, JsonOptions));
    }

    private static object[] FindEmbeddedSamples(SkillProgramEffectOp op)
    {
        const string prefix = "CardGame.Content.Standard.SkillPrograms.";
        const string rulesSuffix = ".rules.json";
        var assembly = typeof(StandardContentPackage).Assembly;
        var wanted = char.ToLowerInvariant(op.ToString()[0]) + op.ToString()[1..];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var found = new List<object>();
        foreach (var resourceName in assembly.GetManifestResourceNames()
                     .Where(name => name.StartsWith(prefix, StringComparison.Ordinal) &&
                                    name.EndsWith(rulesSuffix, StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resourceName) ??
                               throw new InvalidOperationException($"Missing embedded resource '{resourceName}'.");
            using var reader = new StreamReader(stream);
            var rulesText = reader.ReadToEnd();
            using var document = JsonDocument.Parse(rulesText);
            var root = document.RootElement;
            if (!root.TryGetProperty("schemaVersion", out var schemaNode) ||
                schemaNode.ValueKind != JsonValueKind.Number || schemaNode.GetInt32() != SkillProgramCatalog.RulesSchemaVersion)
                continue;
            var presentationName = resourceName[..^rulesSuffix.Length] + ".presentation.json";
            using var presentationStream = assembly.GetManifestResourceStream(presentationName);
            if (presentationStream is null)
                throw new InvalidOperationException($"Missing embedded resource pair '{resourceName}' and '{presentationName}'.");
            using var presentationReader = new StreamReader(presentationStream);
            var catalog = SkillProgramCatalog.Load(rulesText, presentationReader.ReadToEnd());
            var bundle = resourceName[prefix.Length..^rulesSuffix.Length];
            foreach (var skillNode in root.GetProperty("skills").EnumerateArray())
            {
                var skillId = skillNode.GetProperty("id").GetString()!;
                if (!catalog.Programs.TryGetValue(skillId, out var program))
                    continue;
                CollectEntries(skillNode, "activations", "active", program.Activations.Select(item => item.Id).ToHashSet(StringComparer.Ordinal));
                CollectEntries(skillNode, "triggers", "trigger", program.Triggers
                    .Select(item => item.Id).ToHashSet(StringComparer.Ordinal));
                if (found.Count == 3) return found.ToArray();

                void CollectEntries(JsonElement skill, string property, string kind, HashSet<string> allowedIds)
                {
                    if (!skill.TryGetProperty(property, out var entries) || entries.ValueKind != JsonValueKind.Array) return;
                    foreach (var entry in entries.EnumerateArray())
                    {
                        var entryId = entry.GetProperty("id").GetString()!;
                        if (!allowedIds.Contains(entryId) || !entry.TryGetProperty("effects", out var effects)) continue;
                        foreach (var node in DescendantObjects(effects))
                        {
                            if (!node.TryGetProperty("op", out var opNode) || opNode.ValueKind != JsonValueKind.String ||
                                !string.Equals(opNode.GetString(), wanted, StringComparison.Ordinal)) continue;
                            var json = node.GetRawText();
                            if (!seen.Add(json)) continue;
                            found.Add(new
                            {
                                bundle,
                                schemaVersion = SkillProgramCatalog.RulesSchemaVersion,
                                skillId,
                                entryKind = kind,
                                entryId,
                                node = node.Clone()
                            });
                            if (found.Count == 3) return;
                        }
                    }
                }
            }
        }
        return found.ToArray();
    }

    private static IEnumerable<JsonElement> DescendantObjects(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            yield return node;
            foreach (var property in node.EnumerateObject())
                foreach (var child in DescendantObjects(property.Value)) yield return child;
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray())
                foreach (var child in DescendantObjects(item)) yield return child;
        }
    }

    private static void WriteCapabilities()
    {
        var operations = Enum.GetValues<SkillProgramEffectOp>().Select(op =>
        {
            var descriptor = ProgramOperationCatalog.Default.Resolve(op);
            return new
            {
                operation = op.ToString(),
                descriptor = descriptor.GetType().Name,
                handler = descriptor.Handler.GetType().Name,
                aiSemantic = descriptor.AiPolicy.Semantic.ToString(),
                requiredCapabilities = Names(descriptor.RequiredCapabilities)
            };
        }).ToArray();
        var entries = EntryPoints().Select(entry =>
        {
            var capabilities = ProgramEntryCapabilities.For(entry.Window);
            return new
            {
                entry = entry.Name,
                capabilities = Names(capabilities),
                availableOperations = operations
                    .Where(operation =>
                    {
                        var required = ProgramOperationCatalog.Default
                            .Resolve(Enum.Parse<SkillProgramEffectOp>(operation.operation)).RequiredCapabilities;
                        return (capabilities & required) == required;
                    })
                    .Select(operation => operation.operation).ToArray()
            };
        }).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            currentRulesVersion = GameCheckpoint.CurrentRulesVersion,
            latestCompositionSchema = SkillProgramCatalog.RulesSchemaVersion,
            operations,
            entries
        }, JsonOptions));
    }

    private static void Validate(string rulesPath, string presentationPath)
    {
        var rules = File.ReadAllText(Path.GetFullPath(rulesPath));
        var presentation = File.ReadAllText(Path.GetFullPath(presentationPath));
        var catalog = SkillProgramCatalog.Load(rules, presentation);
        var skills = catalog.Programs.Values.OrderBy(program => program.Id, StringComparer.Ordinal)
            .Select(program => new
            {
                id = program.Id,
                minimumRulesVersion = program.MinimumRulesVersion,
                gameplayHash = program.GameplayHash,
                entries = program.Activations.Select(activation => new
                    {
                        kind = "active",
                        id = activation.Id,
                        nodes = activation.Effects.Select(effect => effect.Op.ToString()).ToArray()
                    })
                    .Concat(program.Triggers.Select(trigger => new
                    {
                        kind = trigger.Window.ToString(),
                        id = trigger.Id,
                        nodes = trigger.Effects.Select(effect => effect.Op.ToString()).ToArray()
                    })).ToArray()
            }).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            validation = "load-and-resource-contracts-only",
            note = "Successful loading is not a behavior test.",
            skills
        }, JsonOptions));
    }

    private static string[] Names(ProgramContextCapability capabilities) =>
        Enum.GetValues<ProgramContextCapability>()
            .Where(value => value != ProgramContextCapability.None && capabilities.HasFlag(value))
            .Select(value => value.ToString()).ToArray();

    private static IEnumerable<(string Name, SkillProgramTriggerWindow? Window)> EntryPoints()
    {
        yield return ("Active", null);
        foreach (var window in Enum.GetValues<SkillProgramTriggerWindow>().Where(ProgramEntryCapabilities.SupportsWindow))
            yield return (window.ToString(), window);
    }
}
