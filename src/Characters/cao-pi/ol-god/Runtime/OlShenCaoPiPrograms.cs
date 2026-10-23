using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum OlShenCaoPiStage { Drawing, DrawChildren, DepositChoice, DepositChildren, AwakeningChildren, SkillChoice, SkillChildren }
public sealed record OlShenCaoPiReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId,
    int TargetSeat, OlShenCaoPiStage Stage, PublicPersistentPileSource? Pile = null, long Before = 0, long After = 0, int? DepositedId = null, string? AcquiredSkillId = null)
{
    private IReadOnlyList<int> _cards = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public NativeDrawOutcome? DrawOutcome { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public OlShenCaoPiReceipt? OlShenCaoPi { get; init; } }
public sealed record OlChuyuanDrawReturnedEvent(long FrameId, int RecipientSeat, int Actual, long Before, long After) : IGameEvent;
public sealed record OlChuyuanDepositedEvent(long FrameId, CardConversionSource Source, int RecipientSeat, int CardId, CardLocation Pile, long Before, long After) : IGameEvent;
public sealed record OlCaoPiPileObtainedEvent(long FrameId, CardConversionSource Source, int Count, long Before, long After) : IGameEvent;
public sealed record OlTianxingGrantedEvent(long FrameId, int OwnerSeat, string AcquiredSkillId) : IGameEvent;

internal interface IOlShenCaoPiHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffect effect); }
internal abstract class OlShenCaoPiDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override ISkillProgramEffectHandler Handler { get; } = new OlShenCaoPiHandler(op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        if (op == SkillProgramEffectOp.StoreOlChuyuanAfterDamage) return ParseOwnerAlways(r);
        r.AllowOnly("op", "target", "skillIds", "condition");
        var ids = r.RequiredIdentifierArray("skillIds");
        if (!ids.SequenceEqual(op == SkillProgramEffectOp.AwakenOlDengji ? ["ol:chuyuan"] : OlShenCaoPiComposition.TianxingReferences))
            throw new InvalidOperationException(r.Path + ": the historical awakening requires its exact Chu source and named formal boundary skills.");
        var e = new SkillProgramEffect(op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), skillIds: ids); RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(op == SkillProgramEffectOp.StoreOlChuyuanAfterDamage
        ? SkillProgramTriggerWindow.AfterDamageApplied : SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}
internal sealed class StoreOlChuyuanAfterDamageDescriptor() : OlShenCaoPiDescriptor(SkillProgramEffectOp.StoreOlChuyuanAfterDamage);
internal sealed class AwakenOlDengjiDescriptor() : OlShenCaoPiDescriptor(SkillProgramEffectOp.AwakenOlDengji);
internal sealed class AwakenOlTianxingDescriptor() : OlShenCaoPiDescriptor(SkillProgramEffectOp.AwakenOlTianxing);
internal sealed class OlShenCaoPiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{ public SkillProgramEffectOp Op => op; public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IOlShenCaoPiHost>().Resolve(f, e); }
internal static class OlShenCaoPiComposition
{
    internal static readonly string[] TianxingReferences = ["ol:chuyuan", "boundary:rende", "boundary:zhiheng", "boundary:luanji-current"];
    [ProgramCatalogValidation(40550)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Any(e => e.Op is SkillProgramEffectOp.StoreOlChuyuanAfterDamage or SkillProgramEffectOp.AwakenOlDengji or SkillProgramEffectOp.AwakenOlTianxing)) return;
        var common = p.Activations.Count == 0 && p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.CardPolicies.Count == 0 && p.BooleanStates.Count == 0 && p.Triggers.Count == 1;
        var t = p.Triggers.Single();
        var valid = common && (p.Id == "ol:chuyuan" && t is { Id: "draw-and-store-after-damage", Window: SkillProgramTriggerWindow.AfterDamageApplied,
            Subject: SkillProgramTriggerSubject.Any, DamageOccurrence: SkillProgramDamageOccurrence.PerDamage, Optional: true, Condition.Kind: SkillProgramTriggerConditionKind.Always,
            UsageScope: null, Effects: [{ Op: SkillProgramEffectOp.StoreOlChuyuanAfterDamage }] } ||
            p.Id is "ol:dengji" or "ol:tianxing" && t is { Id: "awakening", Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, Subject: SkillProgramTriggerSubject.Owner,
                Optional: false, UsageScope: SkillUsageScope.Game, UsageLimit: 1 } && t.Condition.Kind == SkillProgramTriggerConditionKind.Compare &&
            (p.Id == "ol:dengji" && t.Effects is [{ Op: SkillProgramEffectOp.ChangeMaximumHp, Amount: -1 }, { Op: SkillProgramEffectOp.AwakenOlDengji }, { Op: SkillProgramEffectOp.GrantSkills } grant] && grant.SkillIds.SequenceEqual(["ol:jianxiong", "ol:tianxing"]) ||
             p.Id == "ol:tianxing" && t.Effects is [{ Op: SkillProgramEffectOp.ChangeMaximumHp, Amount: -1 }, { Op: SkillProgramEffectOp.AwakenOlTianxing } ]));
        if (!valid) throw new InvalidOperationException(path + ": OL Shen Cao Pi requires the complete 2020-05-29 event rules and their exact native lifecycle boundaries.");
    }
}
