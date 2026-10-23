using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IAnniversaryZhaoTongZhaoGuangHost
{
    SkillProgramStepOutcome Execute(ProgramSkillFrame frame, SkillProgramEffectOp op);
}
internal abstract class AnniversaryZhaoTongZhaoGuangDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new AnniversaryZhaoTongZhaoGuangHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy => new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(Op == SkillProgramEffectOp.DrawAnniversaryZhaoTongZhaoGuangQingren ? SkillProgramTriggerWindow.TurnEnding : SkillProgramTriggerWindow.AfterTurnEnded)];
}
internal sealed class DrawAnniversaryZhaoTongZhaoGuangQingrenDescriptor : AnniversaryZhaoTongZhaoGuangDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawAnniversaryZhaoTongZhaoGuangQingren; }
internal sealed class AwakenAnniversaryZhaoTongZhaoGuangLongyuanDescriptor : AnniversaryZhaoTongZhaoGuangDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.AwakenAnniversaryZhaoTongZhaoGuangLongyuan; }
internal sealed class AnniversaryZhaoTongZhaoGuangHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<IAnniversaryZhaoTongZhaoGuangHost>().Execute(f, op);
}

public enum AnniversaryQingrenStage { Drawing, DrawChildren, Completed }
public sealed record AnniversaryQingrenReceipt(CardConversionSource Source, string Hash, long ParentId, int Turn, long CutoffActionId, int Count, AnniversaryQingrenStage Stage)
{
    public NativeDrawOutcome? Draw { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryQingrenReceipt? AnniversaryQingren { get; init; } }

// Counts expose only an accepted action's public source, never unplayed material IDs.
public sealed record AnniversaryYizanInvokedEvent(long ActionId, CardConversionSource Source, string GameplayHash, int ActualTurn, bool Modified, int MaterialCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? NativeUseFrameId = null) : IGameEvent;
public sealed record AnniversaryLongyuanAwakenedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn) : IGameEvent;
public sealed record AnniversaryQingrenIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash, long ParentId, int ActualTurn, long CutoffActionId, int Count) : IGameEvent;
public sealed record AnniversaryQingrenDrawReturnedEvent(long FrameId, int Requested, int Actual, long Before, long After) : IGameEvent;

internal static class AnniversaryZhaoTongZhaoGuangComposition
{
    internal const string Yizan = "anniversary:yizan", Longyuan = "anniversary:longyuan", Qingren = "anniversary:qingren";
    internal static readonly CardKind[] BasicKinds = [CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.Dodge, CardKind.Peach, CardKind.Alcohol];
    [ProgramCatalogValidation(44600)] internal static void Validate(string path, SkillProgram p)
    {
        var local = p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e =>
            e.Op is SkillProgramEffectOp.DrawAnniversaryZhaoTongZhaoGuangQingren or SkillProgramEffectOp.AwakenAnniversaryZhaoTongZhaoGuangLongyuan);
        if (p.Id != Yizan && !local && p.Id is not (Longyuan or Qingren)) return;
        bool Plain(SkillProgramEffect e, SkillProgramEffectOp op) => e.Op == op && e.Target == SkillProgramEffectTarget.Owner && e.Condition.Kind == SkillProgramConditionKind.Always && e.TargetReference is null && e.ActorReference is null;
        var valid = p.Id switch
        {
            Yizan => p.Triggers.Count == 0 && p.Activations.Count == 0 && p.ViewAs.Count == 12 && p.ViewAs.Select(r => r.Id).Distinct().Count() == 12 &&
                BasicKinds.All(k => p.ViewAs.Count(r => r.OutputKind == k) == 2) && p.ViewAs.All(r =>
                    r.Id == (r.InputCount == 2 ? "pair-" : "single-") + JsonKind(r.OutputKind) && r.InputCount is 1 or 2 && r.InputCountExpression is null && !r.VariableInputCount &&
                    r.InputKinds.Count == 0 && r.InputSuits.Count == 0 && r.InputCategories.SequenceEqual(r.InputCount == 2 ? [] : new[] { SkillProgramCardCategory.Basic }) &&
                    r.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) && r.ExtendedUse && r.AllowSameKind && r.ForResponse && r.ForPlay == (r.OutputKind != CardKind.Dodge) &&
                    r.Condition.Kind == SkillProgramConditionKind.Always && !r.AllowChainedInput && !r.SameSuit && !r.InheritPreviousPlaySuit && !r.AllHandCardsOfSelectedSuit && !r.DistanceUnlimited &&
                    r.UsesPerPhase is null && r.UsesPerActualTurn is null && r.UsageGroup is null && r.ActivationUsageGroup is null && r.CostDestination is null && !r.UseOnly && !r.NoDying &&
                    r.DamageBonus == 0 && r.RecoveryBonus == 0 && r.SourcePileSkillId is null && r.ConversionStateId is null && r.DeclarationValidation is null && r.TieredRoundConversion is null &&
                    r.RoundDistinctBasicUse is null && r.DrawFundedDistinctBasic is null && r.LeiluanBasicUse is null && r.XieweiBaitBasicUse is null && r.FieldMountConversion is null && r.UseEffectiveInputSuit is null),
            Longyuan => p.ViewAs.Count == 0 && p.Activations.Count == 0 && p.Triggers is [{ Id: "three-yizan-awakening", Window: SkillProgramTriggerWindow.AfterTurnEnded,
                Subject: SkillProgramTriggerSubject.Owner, TurnOwnerScope: SkillProgramTurnOwnerScope.AnyLiving, Optional: false, IsSkillInvocation: true,
                UsageScope: SkillUsageScope.Game, UsageLimit: 1, Effects: [var draw, var recover, var awaken] }] &&
                Plain(draw, SkillProgramEffectOp.Draw) && draw.Amount == 2 && draw.NumberExpression is null && draw.ResultBind is null &&
                Plain(recover, SkillProgramEffectOp.Recover) && recover.Amount == 1 && Plain(awaken, SkillProgramEffectOp.AwakenAnniversaryZhaoTongZhaoGuangLongyuan),
            Qingren => p.ViewAs.Count == 0 && p.Activations.Count == 0 && p.Triggers is [{ Id: "own-ending-yizan-count", Window: SkillProgramTriggerWindow.TurnEnding,
                Subject: SkillProgramTriggerSubject.Owner, TurnOwnerScope: SkillProgramTurnOwnerScope.Own, Optional: false, IsSkillInvocation: true, UsageScope: null, UsageLimit: null, Effects: [var benefit] }] &&
                Plain(benefit, SkillProgramEffectOp.DrawAnniversaryZhaoTongZhaoGuangQingren),
            _ => false
        };
        if (!valid || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.Contributions.Count != 0 || p.BooleanStates.Count != 0 ||
            p.Triggers.Any(t => t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.MarkerCost is not null))
            throw new InvalidOperationException(path + ": anniversary Zhao Tong/Zhao Guang requires the exact two/basic pair and awakened single-basic native conversions, three-use awakening and actual-turn Qingren draw.");
    }
    private static string JsonKind(CardKind kind) => kind switch { CardKind.FireSlash => "fireSlash", CardKind.ThunderSlash => "thunderSlash", _ => char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..] };
}
