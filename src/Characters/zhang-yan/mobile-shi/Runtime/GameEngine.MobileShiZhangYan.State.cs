namespace CardGame.Core;
public sealed partial class GameEngine
{
    private IReadOnlyList<ProgramPublicRuleStateSnapshot> MobileShiZhangYanPublicStates(CharacterState owner, string id)
    {
        if (id is not (ShiZyFeijing or ShiZyXiaoge)) return [];
        var result = new List<ProgramPublicRuleStateSnapshot>();
        foreach (var instance in GetSkillBindingShard(owner).ProgramInstances.Where(i => i.SkillId == id))
        {
            void Add(string key, int value) => result.Add(new(instance.SkillInstanceId, key, ProgramPublicRuleStateKind.GameActivationProgress, value));
            if (id == ShiZyFeijing)
            {
                Add("feijing-used", _skillRuntimeState.GetUsage(owner.Seat, id, "sole-slash-path@" + instance.SkillInstanceId, SkillUsageScope.Turn));
                var pending = _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.OwnerSeat == owner.Seat && f.SkillId == id && f.ShiZhangYan is not null)?.ShiZhangYan;
                Add("feijing-path-count", pending?.Plans.Count ?? 0); Add("feijing-committed", pending?.Plans.Count(p => p.Committed) ?? 0);
            }
            else
            {
                Add("xiaoge-prevented", ProgramEventHistory<ShiZyPreventedEvent>().Count(e => ProgramEventHistory<ShiZyStartedEvent>().Any(s => s.FrameId == e.FrameId && s.Source.OwnerSeat == owner.Seat && s.Turn == _turnNumber)));
                Add("xiaoge-duels", ProgramEventHistory<ShiZyDuelIssuedEvent>().Count(e => e.Origin.OwnerSeat == owner.Seat && e.Origin.TurnNumber == _turnNumber));
            }
        }
        return result.ToArray();
    }
}

internal static class ShiZhangYanComposition
{
    [ProgramCatalogValidation(50500)] internal static void Validate(string path, SkillProgram p)
    {
        var operations = new[] { SkillProgramEffectOp.ResolveMobileShiZhangYanFeijing, SkillProgramEffectOp.ResolveMobileShiZhangYanXiaogePrevent, SkillProgramEffectOp.ResolveMobileShiZhangYanXiaogeDuel };
        if (!p.Triggers.SelectMany(t => t.Effects).Any(e => operations.Contains(e.Op))) return;
        static bool One(SkillProgramTrigger t, SkillProgramEffectOp op) => t.Effects is [{ Target: SkillProgramEffectTarget.Owner, TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always } e] && e.Op == op;
        // Canonical pre-Cold lists match all four native Slashes through ProgramCardKindMatch.
        static bool SlashFamily(SkillProgramTrigger t) => t.CardKinds.Count == 3 && ProgramCardKindMatch.IsFullSlashFamily(t.CardKinds);
        CardKind[] damageTricks = [CardKind.Duel, CardKind.FireAttack, CardKind.BarbarianAssault, CardKind.ArrowBarrage, CardKind.Lightning,
            CardKind.QizhengXiangsheng, CardKind.WumiaoFlood, CardKind.UnexpectedAssault];
        var valid = p.Id == "mobile:shi-feijing" ? p.ViewAs is [{ Id: "damage-trick-slash", OutputKind: CardKind.Slash, ForPlay: true, ForResponse: true } view] &&
            view.InputKinds.Count == damageTricks.Length && view.InputKinds.ToHashSet().SetEquals(damageTricks) && view.InputSuits.Count == 0 &&
            view.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) &&
            p.Triggers is [{ Id: "sole-slash-path", Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, Optional: true, UsageScope: SkillUsageScope.Turn, UsageLimit: 2 } t] && One(t, operations[0]) && SlashFamily(t) :
            p.Id == "mobile:shi-xiaoge" && p.ViewAs.Count == 0 && p.Triggers.Count == 2 && p.Triggers.Any(t => t.Id == "extra-target-prevent" && t.Window == SkillProgramTriggerWindow.BeforeDamageApplied && !t.Optional && One(t, operations[1])) &&
            p.Triggers.Any(t => t.Id == "sole-slash-duel" && t.Window == SkillProgramTriggerWindow.CardUseCompleted && !t.Optional && One(t, operations[2]) && SlashFamily(t));
        if (!valid || p.Activations.Count != 0 || p.BooleanStates.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0)
            throw new InvalidOperationException(path + ": Shi Zhang Yan requires damage-trick Slash conversion, twice-turn sole-target paths, exact extra-target prevention and completed-Slash native Duel.");
    }
}
