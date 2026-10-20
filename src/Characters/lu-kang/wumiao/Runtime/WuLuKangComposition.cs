namespace CardGame.Core;

internal static partial class WuLuKangComposition
{
    internal const string Shenduan = "wumiao:shenduan", Kegou = "wumiao:kegou", Dixian = "wumiao:dixian";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveWuLuKangShenduan or SkillProgramEffectOp.ResolveWuLuKangKegou or SkillProgramEffectOp.ResolveWuLuKangDixian;
    private static bool WuPlain(SkillProgramEffect e, SkillProgramEffectOp op) => e.Op == op && e.Target == SkillProgramEffectTarget.Owner &&
        e.Condition.Kind == SkillProgramConditionKind.Always && e.Amount == 0 && e.ActorReference is null && e.TargetReference is null &&
        e.CardOwnerRef is null && e.ChooserRef is null && e.SourceBind is null && e.ResultBind is null && e.NumberExpression is null && e.Zones.Count == 0 &&
        !e.AllowDecline && !e.AwaitMovementTriggers;
    private static bool WuCommon(SkillProgram p) => p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.CardIdentities.Count == 0 && p.Activations.Count == 0 &&
        p.HpSkillGrants.Count == 0 && p.EquipmentSuitSkillGrants.Count == 0 && p.DamageModifiers.Count == 0 && p.BooleanStates.Count == 0 && p.Contributions.Count == 0 &&
        !p.LordSkillProjection && !p.CannotChallengeDeclarations;
    private static bool WuUnfiltered(SkillProgramTrigger t) => t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.OwnerRelation is null &&
        t.UsageScope is null && t.UsageLimit is null && t.DynamicUsageLimit is null && t.NamedUsageGroup is null && t.GainPhaseQualification is null && t.MarkerCost is null &&
        t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 && t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && t.DamageCardKinds.Count == 0 && t.Suits.Count == 0 &&
        t.MovementReasons.Count == 0 && t.ExcludedMovementReasons.Count == 0 && t.ExcludedReasons.Count == 0 && t.JudgmentReasons.Count == 0 && t.SourceSkillId is null &&
        t.SourceViewAsId is null && t.JudgmentSource is null && t.MovementOccurrence is null && t.DamageOccurrence is null && t.HpChangeOccurrence == SkillProgramHpChangeOccurrence.PerEvent &&
        t.DrawPhaseMode == SkillProgramDrawPhaseMode.Additive && t.MinimumRank == 1 && t.MaximumRank == 13 && t.ChoiceGroup is null && t.OrderGroup is null &&
        t.RequireDamageSource is null && t.RequireNoCardConversion is null && !t.AllowNoEventTarget && !t.IncludeResponseUses && !t.SingleActionInstance &&
        !t.NoDyingAtActivation && !t.DeferredTurnEndOnly && !t.OnlyDesignatedCardTargets && !t.AllowOwnDiscardPhaseEnded && !t.EvaluateConditionAtResolution &&
        !t.IgnoreOwnSkillMovements && !t.MovementDiscardOnly && t.DiscardOwnerScope == SkillProgramDiscardOwnerScope.Other;
    internal static bool IsShenduan(SkillProgram p) => p.Id == Shenduan && WuCommon(p) && p.CardPolicies is [var policy] &&
        policy.Id == "maximum-deck-source" && policy.Kind == SkillProgramCardPolicyKind.PindianDiscardForMaximumDeck && policy.CardKinds.Count == 0 &&
        policy.RequiredCardKinds.Count == 0 && policy.Value == 0 && policy.InputSuit is null && policy.OutputSuit is null && policy.FactionId is null && policy.OwnerRole is null &&
        policy.DiscardCost == 0 && policy.ProviderDrawCount == 0 && policy.Condition.Kind == SkillProgramConditionKind.Always && p.Triggers is [var t] &&
        t.Id == "native-pindian" && t.Window == SkillProgramTriggerWindow.WuLuKangPindian && t.Subject == SkillProgramTriggerSubject.Owner && !t.Optional &&
        t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own && WuUnfiltered(t) && t.Effects is [var e] && WuPlain(e, SkillProgramEffectOp.ResolveWuLuKangShenduan);
    internal static bool IsKegou(SkillProgram p) => p.Id == Kegou && WuCommon(p) && p.CardPolicies.Count == 0 && p.Triggers.Count == 3 &&
        KegouTrigger(p.Triggers[0], "play-start", SkillProgramTriggerWindow.PlayPhaseStarting, SkillProgramTurnOwnerScope.Own) &&
        KegouTrigger(p.Triggers[1], "used-own-turn-end", SkillProgramTriggerWindow.AfterTurnEnded, SkillProgramTurnOwnerScope.Own) &&
        KegouTrigger(p.Triggers[2], "used-other-turn-end", SkillProgramTriggerWindow.AfterTurnEnded, SkillProgramTurnOwnerScope.OtherLiving);
    private static bool KegouTrigger(SkillProgramTrigger t, string id, SkillProgramTriggerWindow window, SkillProgramTurnOwnerScope scope) =>
        t.Id == id && t.Window == window && t.TurnOwnerScope == scope && t.Subject == SkillProgramTriggerSubject.Owner && t.Optional && WuUnfiltered(t) &&
        t.Effects is [var e] && WuPlain(e, SkillProgramEffectOp.ResolveWuLuKangKegou);
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if ((p.Id is Shenduan or Kegou or Dixian || p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => Handles(e.Op))) &&
            !IsShenduan(p) && !IsKegou(p) && !IsDixian(p))
            throw new InvalidOperationException(path + ": Wu Lu Kang requires the complete native Pindian source/result protocol, actual-boundary Kegou loop or limited whole-game Dixian contract.");
    }
}
