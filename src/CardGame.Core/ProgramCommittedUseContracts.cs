using System.Collections.ObjectModel;
using System.Reflection;

namespace CardGame.Core;

/// <summary>Content contracts for programs observing uses made through response windows.</summary>
internal interface IProgramCommittedUseContract
{
    string SkillId { get; }
    bool AllowsSingleActionInstance => false;
    void ValidateTrigger(SkillProgramTrigger trigger);
}

internal static class ProgramCommittedUseContracts
{
    private static readonly Lazy<IReadOnlyDictionary<string, IProgramCommittedUseContract>> BuiltIn = new(() =>
        Build(typeof(ProgramCommittedUseContracts).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && !type.IsInterface && typeof(IProgramCommittedUseContract).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => (IProgramCommittedUseContract)Activator.CreateInstance(type, nonPublic: true)!)), true);

    internal static IReadOnlyDictionary<string, IProgramCommittedUseContract> Build(IEnumerable<IProgramCommittedUseContract> contracts)
    {
        var result = new Dictionary<string, IProgramCommittedUseContract>(StringComparer.Ordinal);
        foreach (var contract in contracts)
        {
            if (contract is null || string.IsNullOrWhiteSpace(contract.SkillId) ||
                contract.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length != 0)
                throw new InvalidOperationException("Committed-use contracts must declare an identity and carry no instance state.");
            if (!result.TryAdd(contract.SkillId, contract))
                throw new InvalidOperationException($"Duplicate committed-use contract '{contract.SkillId}'.");
        }
        return new ReadOnlyDictionary<string, IProgramCommittedUseContract>(result);
    }

    internal static bool TryValidate(string skillId, SkillProgramTrigger trigger)
    {
        if (!BuiltIn.Value.TryGetValue(skillId, out var contract)) return false;
        contract.ValidateTrigger(trigger);
        return true;
    }

    internal static bool AllowsSingleActionInstance(string skillId) =>
        BuiltIn.Value.TryGetValue(skillId, out var contract) && contract.AllowsSingleActionInstance;
}
