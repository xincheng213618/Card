using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum ShiXinXianYingStage { Suit, JiejieChildren, QingshiInvocation, Target, OwnerDiscard, OtherDiscard, QingDiscardChildren, Drawing, DrawChildren, Finished }
public sealed record ShiXinXianYingViewedCard(int CardId, Suit Suit);
public sealed record ShiXinXianYingMaterial(int CardId, CardLocation From);
public sealed record ShiXinXianYingReceipt(CardConversionSource Source, string GameplayHash, int ActualTurn, int Round, int Actor,
    ShiXinXianYingStage Stage)
{
    public int Target { get; init; } = -1;
    public bool Jiejie { get; init; }
    public bool SameCamp { get; init; }
    public bool QingIssued { get; init; }
    public bool DiscardPaid { get; init; }
    public Suit ChosenSuit { get; init; }
    public string QingshiInstance { get; init; } = "";
    public int DrawCursor { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    private readonly IReadOnlyList<ShiXinXianYingViewedCard> _viewed = Array.AsReadOnly(Array.Empty<ShiXinXianYingViewedCard>());
    public IReadOnlyList<ShiXinXianYingViewedCard> Viewed { get => _viewed; init => _viewed = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<ShiXinXianYingMaterial> _selected = Array.AsReadOnly(Array.Empty<ShiXinXianYingMaterial>());
    public IReadOnlyList<ShiXinXianYingMaterial> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShiXinXianYingReceipt? ShiXinXianYing { get; init; }
}
public sealed record ShiXinXianYingViewedEvent(long FrameId, CardConversionSource Source, int Actor, int Round, int SuitMask, int CardCount) : IGameEvent;
public sealed record ShiXinXianYingSuitChosenEvent(long FrameId, int Actor, Suit Suit, bool Included) : IGameEvent;
public sealed record ShiXinXianYingSuitAllowanceEvent(long FrameId, CardConversionSource Source, string GameplayHash, int Actor, int ActualTurn, Suit Suit) : IGameEvent;
public sealed record ShiXinXianYingQingshiIssuedEvent(long FrameId, CardConversionSource Source, int Target, int Round, bool FromJiejie, bool SameCamp) : IGameEvent;
public sealed record ShiXinXianYingDrawIssuedEvent(long FrameId, int Cursor, int Recipient, int Requested) : IGameEvent;
public sealed record ShiXinXianYingDrawReturnedEvent(long FrameId, int Cursor, int Recipient, int Actual) : IGameEvent;
internal interface IShiXinXianYingHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op, int actor); }
internal abstract class ShiXinXianYingDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        var target = reader.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner && (op != SkillProgramEffectOp.ResolveMobileShiXinXianYingJiejie || target != SkillProgramEffectTarget.Activator))
            throw new InvalidOperationException("Shi Xin Xian Ying requires its real owner or shared activator.");
        var effect = new SkillProgramEffect(op, target, 0, reader.Condition()); RequireAlways(effect, reader.Path); return effect;
    }
    public override ISkillProgramEffectHandler Handler => new ShiXinXianYingHandler(op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.NativeActionAppearance, Triggered: ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.NativeActionAppearance);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => op == SkillProgramEffectOp.ResolveMobileShiXinXianYingQingshi
        ? [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)] : [];
}
internal sealed class ResolveMobileShiXinXianYingJiejieDescriptor() : ShiXinXianYingDescriptor(SkillProgramEffectOp.ResolveMobileShiXinXianYingJiejie);
internal sealed class ResolveMobileShiXinXianYingQingshiDescriptor() : ShiXinXianYingDescriptor(SkillProgramEffectOp.ResolveMobileShiXinXianYingQingshi);
internal sealed class ShiXinXianYingHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int actor, ISkillProgramEffectHost host) => host.GetCapability<IShiXinXianYingHost>().Resolve(f, op, actor);
}
internal static class ShiXinXianYingComposition
{
    [ProgramCatalogValidation(48200)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => e.Op is SkillProgramEffectOp.ResolveMobileShiXinXianYingJiejie or SkillProgramEffectOp.ResolveMobileShiXinXianYingQingshi)) return;
        static bool E(SkillProgramEffect e, SkillProgramEffectOp op, SkillProgramEffectTarget target) => e.Op == op && e.Target == target && e.TargetReference is null && e.Condition.Kind == SkillProgramConditionKind.Always;
        var okay = p.Id == "mobile:shi-jiejie" ? p.Triggers.Count == 0 && p.Activations.Count == 2 && p.Activations.All(a =>
            a.MinCards == 0 && a.MaxCards == 0 && a.MinTargets == 0 && a.MaxTargets == 0 && a.TargetKind == SkillProgramTargetKind.AnyLiving &&
            a.UsesPerTurn is null && a.UsesPerGame is null && a.UsesPerPhase == 1 && a.UsageGroup == a.Id && a.Condition.Kind == SkillProgramConditionKind.Always &&
            a.CardKinds.Count == 0 && a.CardSuits.Count == 0 && a.CardCategories.Count == 0 && a.CardCountExpression is null &&
            a.EquipmentSlots.Count == 0 && a.MarkerCost is null && !a.ContinueAfterOwnerDeath && !a.SelectedCardsSameSuit && !a.SelectedCardsDistinctSuits && a.Effects is [var e] &&
            E(e, SkillProgramEffectOp.ResolveMobileShiXinXianYingJiejie, a.OtherCharactersOnly ? SkillProgramEffectTarget.Activator : SkillProgramEffectTarget.Owner) &&
            a.Id == (a.OtherCharactersOnly ? "other-play-phase" : "own-play-phase")) : p.Id == "mobile:shi-qingshi" && p.Activations.Count == 0 &&
            p.Triggers is [{ Id: "damage-qingshi", Window: SkillProgramTriggerWindow.AfterDamageApplied, Subject: SkillProgramTriggerSubject.Owner, Optional: true,
                DamageOccurrence: SkillProgramDamageOccurrence.PerDamage, Effects: [var q] }] && E(q, SkillProgramEffectOp.ResolveMobileShiXinXianYingQingshi, SkillProgramEffectTarget.Owner);
        if (!okay || p.ViewAs.Count != 0 || p.CardPolicies.Count != 0 || p.Modifiers.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": Shi Xin Xian Ying requires complete shared Jiejie and actual after-damage Qingshi.");
    }
}


