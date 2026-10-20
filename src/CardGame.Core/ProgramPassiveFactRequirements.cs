using System.Reflection;

namespace CardGame.Core;

// Passive rules have no instruction entry. Their native fact demand belongs to
// the loaded skill definition, independently of the skills currently granted.
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
internal sealed class ProgramPassiveFactRequirementsAttribute(string skillId, ProgramTrackedFact facts) : Attribute
{
    internal string SkillId { get; } = skillId;
    internal ProgramTrackedFact Facts { get; } = facts;

    internal static ProgramTrackedFact ForSkills(IEnumerable<ContentSkillDefinition> definitions)
    {
        var ids = definitions.Select(skill => skill.Id).ToHashSet(StringComparer.Ordinal);
        return Catalog.Items.Where(item => ids.Contains(item.SkillId))
            .Aggregate(ProgramTrackedFact.None, (value, item) => value | item.Facts);
    }

    private static class Catalog
    {
        internal static readonly ProgramPassiveFactRequirementsAttribute[] Items = Discover();
        private static ProgramPassiveFactRequirementsAttribute[] Discover()
        {
            var items = typeof(GameEngine).Assembly.GetTypes()
                .SelectMany(type => type.GetCustomAttributes<ProgramPassiveFactRequirementsAttribute>(inherit: false)).ToArray();
            if (items.Any(item => string.IsNullOrWhiteSpace(item.SkillId) || item.Facts == ProgramTrackedFact.None ||
                !new ProgramFactRequirements(Triggered: item.Facts).IsValid))
                throw new InvalidOperationException("Passive fact requirements need a skill identity and known nonempty native facts.");
            return items;
        }
    }
}
