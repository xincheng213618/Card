namespace CardGame.Core;

internal static class MoSimaYiComposition
{
    internal const string Guifu = "ol:mo-guifu", Moubian = "ol:mo-moubian", Zhouxi = "ol:mo-zhouxi", Lieshi = "ol:mo-lieshi";
    internal static readonly IReadOnlyList<string> Pool = Array.AsReadOnly(new[]
    {
        "ol:zhendu", "ol:tianjie", "boundary:juece-current", "boundary:leiji", Lieshi,
        "classic:sanyao", "ol:zhefu", "ol:jianhe", "ol:shuzi", "boundary:ganglie",
        "boundary:qiangxi", "classic:kuizhu", "ol:duwu", "boundary:quhu-current", "ol:xuehen", "ol:daoshu"
    });
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.AcquireMoSimaYiGuifuDodge or
        SkillProgramEffectOp.ResolveMoSimaYiMoubian or SkillProgramEffectOp.ResolveMoSimaYiZhouxi or
        SkillProgramEffectOp.ResolveMoSimaYiRetaliation or SkillProgramEffectOp.ResolveMoSimaYiLieshi;
    internal static bool Ordinary(CardKind k) => CardUseCategoryCatalog.Get(k) == CardUseCategories.InstantTrick && k != CardKind.Nullification;
    internal static IReadOnlyList<CardKind> Names => Array.AsReadOnly(Enum.GetValues<CardKind>().Where(k => k != CardKind.MantianGuohai &&
        CardUseCategoryCatalog.Get(k) is CardUseCategories.Basic or CardUseCategories.InstantTrick or CardUseCategories.DelayedTrick).ToArray());

    [ProgramCatalogValidation(1610)]
    internal static void Validate(string path, SkillProgram p)
    {
        if (p.Id is not (Guifu or Moubian or Zhouxi or Lieshi))
        {
            if (p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => Handles(e.Op)))
                throw new InvalidOperationException(path + ": Magic Sima Yi operations require their original complete source.");
            return;
        }
        static bool T(SkillProgramTrigger t, string id, SkillProgramTriggerWindow window, bool optional, SkillProgramEffectOp op) =>
            t.Id == id && t.Window == window && t.Subject == SkillProgramTriggerSubject.Owner && t.Optional == optional &&
            (window == SkillProgramTriggerWindow.RoundStarting
                ? t.Condition is { Kind: SkillProgramTriggerConditionKind.Compare, Children.Count: 0,
                    Left.Kind: SkillProgramTriggerValueKind.RoundTurnSeatCount, Comparison: SkillProgramComparisonOperator.Equal,
                    Right: { Kind: SkillProgramTriggerValueKind.IntegerConstant, Value: 1 } }
                : t.Condition.Kind == SkillProgramTriggerConditionKind.Always) &&
            t.Effects is [var e] && e.Op == op && e.Target == SkillProgramEffectTarget.Owner && e.Condition.Kind == SkillProgramConditionKind.Always;
        var valid = p.Id switch
        {
            Guifu => p.Activations.Count == 0 && p.Triggers is [var round, var hp] &&
                T(round, "acquire-round", SkillProgramTriggerWindow.RoundStarting, false, SkillProgramEffectOp.AcquireMoSimaYiGuifuDodge) &&
                T(hp, "acquire-health", SkillProgramTriggerWindow.AfterHealthChanged, false, SkillProgramEffectOp.AcquireMoSimaYiGuifuDodge) &&
                p.ViewAs.Count == Names.Count && p.ViewAs.Select(v => v.OutputKind).Distinct().Count() == Names.Count &&
                p.ViewAs.All(v => Names.Contains(v.OutputKind) && v.Id == "recorded-" + v.OutputKind && v.InputCount == 1 &&
                    v.InputKinds.Count == 0 && v.InputCategories.Count == 0 && v.InputSuits.Count == 0 &&
                    v.SourceZones.SequenceEqual([CardZoneKind.Hand]) && v.AllowSameKind && v.ExtendedUse &&
                    v.SingleCardTrickUse == Ordinary(v.OutputKind) && v.ForPlay == (v.OutputKind is not (CardKind.Dodge or CardKind.Nullification)) &&
                    v.ForResponse == (v.OutputKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.ColdSlash or CardKind.Dodge or CardKind.Peach or CardKind.Alcohol or CardKind.Nullification) &&
                    v.DamageBonus == 0 && v.RecoveryBonus == 0 && !v.DistanceUnlimited && !v.UseOnly && v.CostDestination is null &&
                    v.UsesPerPhase is null && v.UsesPerActualTurn is null && v.Condition.Kind == SkillProgramConditionKind.Always),
            Moubian => p.Activations.Count == 0 && p.ViewAs.Count == 0 && p.Triggers is [var prep] &&
                T(prep, "learn-recorded", SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, true, SkillProgramEffectOp.ResolveMoSimaYiMoubian),
            Zhouxi => p.Activations.Count == 0 && p.ViewAs.Count == 0 && p.Triggers is [var prep, var ending] &&
                T(prep, "sample-skill", SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, false, SkillProgramEffectOp.ResolveMoSimaYiZhouxi) &&
                T(ending, "damaged-round-retaliation", SkillProgramTriggerWindow.RoundEnding, false, SkillProgramEffectOp.ResolveMoSimaYiRetaliation),
            _ => p.ViewAs.Count == 0 && p.Triggers.Count == 0 && p.Activations is [var a] && a.Id == "two-different-oaths" &&
                a.MinCards == 0 && a.MaxCards == 0 && a.MinTargets == 1 && a.MaxTargets == 1 &&
                a.TargetKind == SkillProgramTargetKind.AnyLiving && a.UsesPerTurn is null && a.UsesPerGame is null &&
                a.Effects is [var e] && e.Op == SkillProgramEffectOp.ResolveMoSimaYiLieshi && e.Target == SkillProgramEffectTarget.Owner
        };
        if (!valid || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.BooleanStates.Count != 0 || p.DamageModifiers.Count != 0 ||
            p.Contributions.Count != 0 || p.HpSkillGrants.Count != 0)
            throw new InvalidOperationException(path + ": Magic Sima Yi requires complete actual-damage recording, native one-entity aliases and its unbounded preparation learning.");
    }
}
