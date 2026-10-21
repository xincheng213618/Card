using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record DinghanNameChangedEvent(long FrameId, int Owner, string Instance, CardKind Kind, bool Added) : IGameEvent;
public sealed record TianzuoCardCreatedEvent(long FrameId, int Owner, int CardId, Suit Suit, int Rank) : IGameEvent;
public sealed record TianzuoDeckShuffledEvent(long FrameId, ulong Before, ulong After, int Count) : IGameEvent;
public sealed record LingceDrawIssuedEvent(long FrameId, int Owner, long ActionId, CardKind Kind) : IGameEvent;
public sealed record DinghanTargetCanceledEvent(long FrameId, long UseId, long ActionId, int Owner, CardKind Kind,
    CardActionContext OriginalAction) : IGameEvent;

internal interface IShenXunYuHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class ShenXunYuDescriptor(SkillProgramEffectOp operation) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => operation;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler { get; } = new ShenXunYuHandler(operation);
    public override ProgramOperationInteraction Interaction => operation == SkillProgramEffectOp.EditDinghanNames ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(1));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}
internal sealed class AddTianzuoCardsDescriptor() : ShenXunYuDescriptor(SkillProgramEffectOp.AddTianzuoCards);
internal sealed class DrawLingceActualTrickDescriptor() : ShenXunYuDescriptor(SkillProgramEffectOp.DrawLingceActualTrick);
internal sealed class RecordAndCancelDinghanTargetDescriptor() : ShenXunYuDescriptor(SkillProgramEffectOp.RecordAndCancelDinghanTarget);
internal sealed class EditDinghanNamesDescriptor() : ShenXunYuDescriptor(SkillProgramEffectOp.EditDinghanNames);
internal sealed class LingceCommittedUseContract : IProgramCommittedUseContract
{
    public string SkillId => "mobile:lingce";
    public void ValidateTrigger(SkillProgramTrigger t)
    {
        if (t.Id != "real-trick-draw" || t.Window != SkillProgramTriggerWindow.CardUseCommitted || t.Optional || !t.IncludeResponseUses ||
            t.OwnerRelation != SkillProgramCardActionOwnerRelation.Observer || t.Subject is not null || t.SingleActionInstance ||
            t.SourceSkillId is not null || t.SourceViewAsId is not null || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            t.UsageScope is not null || t.UsageLimit is not null || t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null ||
            t.MarkerCost is not null || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.RequireNoCardConversion is not null ||
            t.Effects is not [{ Op: SkillProgramEffectOp.DrawLingceActualTrick, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always },
                { Op: SkillProgramEffectOp.Draw, Target: SkillProgramEffectTarget.Owner, Amount: 1, NumberExpression: null, SourceBind: null, ResultBind: null, TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException("Lingce requires its exact mandatory actual committed trick qualification followed by one native owner draw.");
    }
}
internal sealed class ShenXunYuHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => host.GetCapability<IShenXunYuHost>().Resolve(frame, operation);
}
internal static class ShenXunYuComposition
{
    [ProgramCatalogValidation(38300)]
    internal static void Validate(string path, SkillProgram p)
    {
        static bool Owned(SkillProgramEffect e) => e.Op is SkillProgramEffectOp.AddTianzuoCards or SkillProgramEffectOp.DrawLingceActualTrick or SkillProgramEffectOp.RecordAndCancelDinghanTarget or SkillProgramEffectOp.EditDinghanNames;
        if (!p.Triggers.SelectMany(t => t.Effects).Any(Owned)) return;
        if (p.Activations.Count != 0 || p.Modifiers.Count != 0 || p.ViewAs.Count != 0 || p.CardPolicies.Count != 0 || p.Contributions.Count != 0 || p.BooleanStates.Count != 0 || p.CardIdentities.Count != 0 ||
            p.Triggers.Any(t => t.Optional || t.UsageLimit is not null || t.Effects.Any(e => e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always)))
            throw new InvalidOperationException(path + ": Shen Xun Yu requires his exact native trigger shape.");
        var valid = p.Id switch
        {
            "mobile:tianzuo" => p.Triggers is [{ Id: "eight-qizheng", Window: SkillProgramTriggerWindow.GameStarting, Effects: [{ Op: SkillProgramEffectOp.AddTianzuoCards }] },
                { Id: "qizheng-ineffective", Window: SkillProgramTriggerWindow.CardUseBeforeTargetEffects, Effects: [{ Op: SkillProgramEffectOp.NullifyCurrentCardEffect }] } immunity] && immunity.CardKinds.SequenceEqual([CardKind.QizhengXiangsheng]),
            "mobile:lingce" => p.Triggers is [{ Id: "real-trick-draw", Window: SkillProgramTriggerWindow.CardUseCommitted, Effects: [{ Op: SkillProgramEffectOp.DrawLingceActualTrick }, { Op: SkillProgramEffectOp.Draw, Amount: 1 }] }],
            "mobile:dinghan" => p.Triggers is [{ Id: "new-trick-name", Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, Effects: [{ Op: SkillProgramEffectOp.RecordAndCancelDinghanTarget }] },
                { Id: "edit-trick-name", Window: SkillProgramTriggerWindow.TurnBeforeStart, Effects: [{ Op: SkillProgramEffectOp.EditDinghanNames }] }],
            _ => false
        };
        if (!valid) throw new InvalidOperationException(path + ": Shen Xun Yu requires all official bindings, without approximations.");
    }
}
