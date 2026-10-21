using System.Text.Json;
using System.Text.Json.Nodes;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    // This is a derived, immutable per-engine view. Original registry programs,
    // source hashes, source binding IDs and executable instructions stay intact.
    private readonly Dictionary<SkillProgram, SkillProgram> _activityOrdinaryPrograms = [];
    private static readonly IReadOnlySet<CardKind> TraditionalProactiveOrdinaryTricks = new HashSet<CardKind>
    {
        CardKind.Duel, CardKind.DrawTwo, CardKind.BarbarianAssault, CardKind.ArrowBarrage,
        CardKind.PeachGarden, CardKind.FiveGrains, CardKind.Dismantlement, CardKind.Snatch,
        CardKind.FireAttack, CardKind.IronChain, CardKind.BorrowedSword
    };
    private bool HasActivityOrdinaryTrickProjection => _modeDefinition.ShenwuActivity is not null || _modeDefinition.HulaoActivity is not null || _modeDefinition.HellActivity is not null || _modeDefinition.WenheActivity is not null || _modeDefinition.DragonBoatActivity is not null || IsGuanduActivity;
    private CardKind? ActivityOrdinaryTrickReplacement(CardKind kind) => _modeDefinition.ShenwuActivity is not null ? kind switch
    {
        CardKind.DrawTwo => CardKind.SowBeans, CardKind.BorrowedSword => CardKind.TransplantWood, _ => kind
    } : _modeDefinition.HulaoActivity is not null ? kind switch
    {
        CardKind.FiveGrains => CardKind.AllianceFeast, CardKind.PeachGarden => null, _ => kind
    } : IsGuanduActivity ? kind switch
    {
        CardKind.FiveGrains => CardKind.StoreGrain, CardKind.PeachGarden => CardKind.Reinforcements,
        CardKind.BarbarianAssault => CardKind.UnitedEffort, _ => kind
    } : _modeDefinition.DragonBoatActivity is not null ? kind switch
    {
        CardKind.DrawTwo => CardKind.SameBoat,
        CardKind.FiveGrains => CardKind.StriveUpstream, CardKind.PeachGarden => null,
        CardKind.BarbarianAssault => CardKind.AgainstCurrent, CardKind.ArrowBarrage => null,
        _ => kind
    } : kind;
    private bool IsActivityOrdinaryTrickNameAvailable(CardKind kind) =>
        (!IsWushuangMode || WushuangCatalogue.IsCardAllowed(kind)) &&
        (!HasActivityOrdinaryTrickProjection || ActivityOrdinaryTrickReplacement(kind) == kind);

    /// <summary>Returns immutable rule metadata for a published conversion source in this match's mode.</summary>
    public SkillProgramViewAs? GetCardConversionDefinition(CardConversionSource source) =>
        _contentRegistry.Skills.GetValueOrDefault(source.SkillId)?.Program is { } original
            ? ActivityRuntimeProgram(original).ViewAs.FirstOrDefault(rule => rule.Id == source.BindingId)
            : null;

    private ContentSkillDefinition ActivityRuntimeSkillDefinition(string id)
    {
        var original = _contentRegistry.GetSkill(id);
        if (original.Program is not { } source) return original;
        var projected = ActivityRuntimeProgram(source);
        return ReferenceEquals(source, projected) ? original : original with { Program = projected };
    }

    private SkillProgram ActivityRuntimeProgram(SkillProgram original)
    {
        if (!HasActivityOrdinaryTrickProjection || original.Id == MoSimaYiComposition.Guifu) return original;
        if (_activityOrdinaryPrograms.TryGetValue(original, out var cached)) return cached;
        var families = original.ViewAs.Where(rule => TraditionalProactiveOrdinaryTricks.Contains(rule.OutputKind))
            .GroupBy(ActivityViewAsFamily, StringComparer.Ordinal)
            .Where(group => TraditionalProactiveOrdinaryTricks.IsSubsetOf(group.Select(rule => rule.OutputKind)))
            .SelectMany(group => group).ToHashSet();
        var changed = false;
        var basicFamilies = _modeDefinition.DragonBoatActivity is null && !IsGuanduActivity ? new HashSet<SkillProgramViewAs>() :
            original.ViewAs.Where(rule => DragonBoatTraditionalBasicKinds.Contains(rule.OutputKind))
                .GroupBy(DragonBoatBasicViewAsFamily, StringComparer.Ordinal)
                .Where(group => DragonBoatTraditionalBasicKinds.IsSubsetOf(group.Select(rule => rule.OutputKind)))
                .SelectMany(group => group).ToHashSet();
        var views = new List<SkillProgramViewAs>();
        foreach (var rule in original.ViewAs)
        {
            if (basicFamilies.Contains(rule) && (IsGuanduActivity ? rule.OutputKind == CardKind.Alcohol : rule.OutputKind is CardKind.Peach or CardKind.Alcohol))
            {
                changed = true;
                views.Add(CloneActivityViewAs(rule, IsGuanduActivity ? CardKind.Intoxication : rule.OutputKind == CardKind.Peach ? CardKind.Zong : CardKind.RealgarWine, forResponse: false));
                continue;
            }
            if (!families.Contains(rule)) { views.Add(rule); continue; }
            var output = ActivityOrdinaryTrickReplacement(rule.OutputKind);
            if (output == rule.OutputKind) { views.Add(rule); continue; }
            changed = true;
            if (output is { } kind) views.Add(CloneActivityViewAs(rule, kind));
        }
        if (_modeDefinition.HellActivity is not null)
            foreach (var family in families.GroupBy(ActivityViewAsFamily, StringComparer.Ordinal))
            {
                var template = family.First();
                views.Add(CloneActivityViewAs(template, CardKind.ReturnSoul, template.Id + ".activity-return-soul"));
                changed = true;
            }
        if (_modeDefinition.WenheActivity is not null)
            foreach (var family in families.GroupBy(ActivityViewAsFamily, StringComparer.Ordinal))
            {
                var template = family.First();
                foreach (var kind in WenheOrdinaryTrickKinds)
                    views.Add(CloneActivityViewAs(template, kind, template.Id + ".activity-" + kind));
                changed = true;
            }
        var triggers = original.Triggers.Select(trigger =>
        {
            if (!TraditionalProactiveOrdinaryTricks.IsSubsetOf(trigger.CardKinds)) return trigger;
            var kinds = trigger.CardKinds.Select(ActivityOrdinaryTrickReplacement).Where(kind => kind is not null)
                .Concat(_modeDefinition.HellActivity is null ? [] : new CardKind?[] { CardKind.ReturnSoul })
                .Concat(_modeDefinition.WenheActivity is null ? [] : WenheOrdinaryTrickKinds.Select(kind => (CardKind?)kind))
                .Select(kind => kind!.Value).Distinct().ToArray();
            if (kinds.SequenceEqual(trigger.CardKinds)) return trigger;
            changed = true; return CloneActivityTrigger(trigger, Array.AsReadOnly(kinds));
        }).ToArray();
        var result = !changed ? original : new SkillProgram(original.Id, original.Revision, original.GameplayHash,
            original.RuntimeVersion, original.MinimumRulesVersion, original.Modifiers, Array.AsReadOnly(views.ToArray()),
            original.Activations, Array.AsReadOnly(triggers), original.Contributions, original.CardIdentities,
            original.BooleanStates, original.DamageModifiers, original.CardPolicies, original.LordSkillProjection,
            original.CannotChallengeDeclarations, original.HpSkillGrants, original.EquipmentSuitSkillGrants);
        _activityOrdinaryPrograms.Add(original, result);
        return result;
    }

    private static string ActivityViewAsFamily(SkillProgramViewAs rule) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ActivityOrdinaryTricks.ActivityViewAsFamily(rule);

    private static SkillProgramViewAs CloneActivityViewAs(SkillProgramViewAs r, CardKind kind, string? bindingId = null, bool? forResponse = null) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ActivityOrdinaryTricks.CloneActivityViewAs(r,kind,bindingId,forResponse);

    private static SkillProgramTrigger CloneActivityTrigger(SkillProgramTrigger t, IReadOnlyList<CardKind> kinds) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ActivityOrdinaryTricks.CloneActivityTrigger(t,kinds);
}
