using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed partial record ProgramSkillFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public ZonghuReceipt? Zonghu {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public ZonghuProgramReturn? ZonghuReturn {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public ZonghuPaidOrigin? ZonghuResponseOrigin {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public ZonghuPaidOrigin? ZonghuKeAcceptedOrigin {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public FengweiReceipt? Fengwei {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public FengweiDamageReceipt? FengweiDamage {get;init;}
}
public sealed partial record CardUseFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public ZonghuPaidOrigin? ZonghuOrigin {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public ZonghuPaidOrigin? ZonghuResponseOrigin {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public FengweiDamageReceipt? FengweiDamage {get;init;}
}
public sealed partial record JudgmentFrame {[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public FengweiDamageReceipt? FengweiDamage {get;init;}}
internal interface ILiuZhangHost {SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal abstract class LiuZhangDescriptor:ProgramOperationDescriptorBase
{
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
 public override ISkillProgramEffectHandler Handler=>new LiuZhangHandler(Op);
 public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(1));
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[];
}
internal sealed class LiuZhangHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
 public SkillProgramEffectOp Op=>op;
 public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost host)=>host.GetCapability<ILiuZhangHost>().Resolve(f,op);
}
internal static class LiuZhangComposition
{
 [ProgramCatalogValidation(680)]
 internal static void ValidateProgram(string path,SkillProgram p)
 {
  var ops=p.Activations.SelectMany(a=>a.Effects).Concat(p.Triggers.SelectMany(t=>t.Effects)).Where(e=>e.Op is SkillProgramEffectOp.ResolveFengweiRoundStartDraw or SkillProgramEffectOp.ResolveFengweiActualDrawSkip or SkillProgramEffectOp.ResolveZonghuRequiredUseRequest).ToArray();if(ops.Length==0)return;
  if(p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.HpSkillGrants.Count!=0||ops.Any(e=>e.Target!=SkillProgramEffectTarget.Owner||e.Condition.Kind!=SkillProgramConditionKind.Always))throw new InvalidOperationException(path+": LiuZhang requires complete native owner operations.");
  if(p.Id=="ol:fengwei"&&p.Activations.Count==0&&p.Triggers is[var first,var skip]&&first.Id=="round-draw"&&first.Window==SkillProgramTriggerWindow.RoundStarting&&!first.Optional&&first.Effects is[{Op:SkillProgramEffectOp.ResolveFengweiRoundStartDraw}]&&skip.Id=="draw-skip"&&skip.Window==SkillProgramTriggerWindow.DrawPhaseStarting&&!skip.Optional&&skip.Effects is[{Op:SkillProgramEffectOp.ResolveFengweiActualDrawSkip}])return;
  if(p.Id=="ol:zonghu"&&p.Triggers.Count==0&&p.Activations is[var a]&&a.Id=="native-use-request"&&a.MinCards==0&&a.MaxCards==0&&a.MinTargets==0&&a.MaxTargets==0&&a.UsesPerTurn is null&&a.Effects is[{Op:SkillProgramEffectOp.ResolveZonghuRequiredUseRequest}])return;
  throw new InvalidOperationException(path+": LiuZhang must retain complete round draw, actual draw skip and native true-use request bindings.");
 }
}
