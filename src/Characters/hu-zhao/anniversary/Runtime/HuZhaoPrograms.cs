using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryHuZhaoHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal sealed class ResolveAnniversaryHuZhaoMiduDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryHuZhaoMidu;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new ResolveAnniversaryHuZhaoMiduHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(4));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireActivationEntry(), new CreateChoiceResult("midu-branch", ["abolish", "restore"])];
}
internal sealed class ResolveAnniversaryHuZhaoMiduHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryHuZhaoMidu;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IAnniversaryHuZhaoHost>().Resolve(f);
}
public enum HuZhaoMiduStage { Areas, Recipient, CostChildren, Drawing, DrawChildren, Completed }
public sealed record HuZhaoMiduReceipt(CardConversionSource Source, string GameplayHash, int Turn, int TurnOwner,
    HuZhaoMiduStage Stage, int AvailableMask, int SelectedMask = 0, bool Restore = false, int Recipient = -1,
    bool Paid = false, long Before = 0, long After = 0, NativeDrawOutcome? Draw = null);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HuZhaoMiduReceipt? HuZhaoMidu { get; init; }
}
public sealed record HuZhaoMiduStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int Turn, int TurnOwner, int AvailableMask) : IGameEvent;
public sealed record HuZhaoMiduPaidEvent(long FrameId, CardConversionSource Source, string GameplayHash, int SelectedMask, bool Restore, int Recipient, long Before, long After) : IGameEvent;
public sealed record HuZhaoMiduCompletedEvent(long FrameId, bool Restore, int Recipient, int DrawCount) : IGameEvent;
internal static class HuZhaoComposition
{
    internal const string Midu = "anniversary:midu", Xianwang = "anniversary:xianwang";
    [ProgramCatalogValidation(43000)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => e.Op == SkillProgramEffectOp.ResolveAnniversaryHuZhaoMidu)) return;
        if (p.Id != Midu || p.Activations is not [{ Id: "abolish-or-restore", MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0,
                TargetKind: SkillProgramTargetKind.OtherLiving, UsesPerTurn: null, UsesPerPhase: 1, Effects: [
                { Op: SkillProgramEffectOp.ResolveAnniversaryHuZhaoMidu, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always },
                { Op: SkillProgramEffectOp.GrantTurnSkills, Target: SkillProgramEffectTarget.Owner, TargetReference: null, SkillIds: ["classic:huomo"],
                  TurnSkillExpiry: SkillProgramTurnSkillExpiry.RecipientNextActualTurnStart,
                  Condition: { Kind: SkillProgramConditionKind.ChoiceIs, SourceBind: "midu-branch", OptionId: "restore" } }]}] ||
            p.Triggers.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.Contributions.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": Hu Zhao requires one zero-input phase activation, native area payment/draw, and exactly next-actual-turn-start Huomo on restoration.");
    }
}
