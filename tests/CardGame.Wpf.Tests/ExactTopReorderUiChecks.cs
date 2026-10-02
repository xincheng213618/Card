using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ExactTopReorderUiChecks
{
    public static void ManualLoadAndPartition(string output)
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture());
        var game=GameEngine.CreateStandard(new GameOptions {Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,
            ModeId="identity:ui-exact-top",UseInteractiveSetup=true,AdvanceAfterHumanCommands=false,MaxTurns=5},registry);
        Accept(game,new StartGameCommand());Reach(game,p=>p.Kind==DecisionKind.SelectGeneral);
        Accept(game,new SelectGeneralCommand(0,"fixture:ui-exact-owner",game.Revision,game.PendingDecision!.PromptId));
        Reach(game,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
        using var model=Restore(game,registry);
        var before=Program.Engine(model).Revision;
        model.SelectSkillChoiceCommand.Execute(model.SkillChoices.Single(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
        game=Program.Engine(model);Program.Assert(game.Revision==before+1,"One activation click is exactly one accepted command.");
        var draft=game.ResolutionStack.OfType<ProgramSkillFrame>().Last().TopReorder!;
        var ids=draft.ViewedCardIds.ToArray();
        Program.Assert(ids.Length==4 && draft.RequiredTopCount==2 && model.SkillChoices.Count==4 &&
            model.SkillChoices.All(c=>c.Cards.Count==1 && c.Description.Contains("观看牌")) && model.PrivateRevealTitle.Contains("精确排序") &&
            model.PrivateRevealTitle.Contains("仅你可见") && model.PrivatelyViewedCards.Select(c=>c.Id).SequenceEqual(ids) &&
            model.PrivatelyViewedCards.All(c=>c.IsPrivateReveal&&!c.IsPlayable),"Shared native panel exposes four actual private faces with exact top metadata.");
        Privacy(game);File.WriteAllText(Path.Combine(output,"317-exact-top-start.checkpoint.json"),GameCheckpointJson.Serialize(game.CreateCheckpoint()));Render(model,output,"317-exact-top-start.png");
        Click(model,ids[1],output,"317-exact-top-choice.png");
        using var first=Restore(Program.Engine(model),registry);
        game=Program.Engine(first);
        Program.Assert(game.ResolutionStack.OfType<ProgramSkillFrame>().Last().TopReorder!.TopCardIds.SequenceEqual([ids[1]]) &&
            first.SkillChoices.Count==3 && first.SkillChoices.All(c=>c.Parameters.GetValueOrDefault("action")=="top"),
            "Manual load retains one top selection and exact remaining choices.");
        Privacy(game);Click(first,ids[0],output,"317-exact-top-second-choice.png");
        using var bottom=Restore(Program.Engine(first),registry);
        game=Program.Engine(bottom);draft=game.ResolutionStack.OfType<ProgramSkillFrame>().Last().TopReorder!;
        Program.Assert(draft.ChoosingBottom && draft.TopCardIds.SequenceEqual([ids[1],ids[0]]) &&
            bottom.SkillChoices.Count==2 && bottom.SkillChoices.All(c=>c.Parameters.GetValueOrDefault("action")=="bottom") &&
            game.PendingDecision!.SkillPrompt!.Title.Contains("牌堆底排序"),"The second top choice automatically switches the real shared controls to bottom ordering.");
        Privacy(game);File.WriteAllText(Path.Combine(output,"317-exact-top-restored-bottom.checkpoint.json"),GameCheckpointJson.Serialize(game.CreateCheckpoint()));Render(bottom,output,"317-exact-top-restored-bottom.png");Click(bottom,ids[3],output,"317-exact-bottom-choice.png");
        using var last=Restore(Program.Engine(bottom),registry);
        Program.Assert(last.SkillChoices.Count==1 && last.SkillChoices[0].Cards.SequenceEqual([ids[2]]),"Restored partial bottom order retains only the final legal entity.");
        var moves=Program.Engine(last).CardMovements.Count;Click(last,ids[2],output,"317-exact-bottom-last-choice.png");game=Program.Engine(last);
        for(var step=0;step<32 && game.PendingDecision?.Kind!=DecisionKind.PlayCard;step++)
        {Program.Assert(last.CanStepAi,"Completed exact order must resume normal draw.");last.StepAiCommand.Execute(null);game=Program.Engine(last);}
        Program.Assert(!last.HasPrivatelyViewedCards && game.CardMovements.Skip(moves).Where(m=>m.To==CardLocation.Hand(0))
            .Select(m=>m.CardId).SequenceEqual([ids[1],ids[0]]) && !game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.TopReorder!=null),
            "Native completion clears choices and draws the actual two top entities once through normal Draw.");
        File.WriteAllText(Path.Combine(output,"317-exact-top-completed.checkpoint.json"),GameCheckpointJson.Serialize(game.CreateCheckpoint()));
    }
    private static void Click(MainViewModel model,int id,string output,string file)
    {
        var game=Program.Engine(model);var revision=game.Revision;
        var choice=model.SkillChoices.Single(c=>c.Cards.SequenceEqual([id]));
        var window=new MainWindow(model);window.ApplyTemplate();var root=(FrameworkElement)window.Content;
        Program.Render(root,1120,740,Path.Combine(output,file));
        var controls=Program.Find<ItemsControl>(root).Single(c=>c.Name=="ScrollingSkillChoices");
        var button=Program.Find<Button>(controls).Single(b=>b.Command==model.SelectSkillChoiceCommand &&
            b.CommandParameter is PromptChoice parameter && parameter.Id==choice.Id && b.ActualWidth>0);
        button.BringIntoView();root.UpdateLayout();Program.Render(root,1120,740,Path.Combine(output,file));
        Program.Assert(button.IsEnabled && button.Visibility==Visibility.Visible,"A real enabled generic order button binds the exact remaining entity.");
        button.Command!.Execute(button.CommandParameter);
        Program.Assert(game.Revision==revision+1,"Each shared exact order click accepts exactly one command.");
        window.Content=null;window.Close();
    }
    private static void Privacy(GameEngine game)
    {var revision=game.Revision;for(var viewer=0;viewer<4;viewer++){var s=game.CreateSnapshot(viewer);Program.Assert(viewer==0
        ? s.PendingDecision?.Kind==DecisionKind.ProgramTopReorder && s.PrivateRevealedCards?.Count==4
        : s.PendingDecision==null && s.PrivateRevealedCards==null,"All four viewer projections preserve private order faces and choices.");}
        Program.Assert(game.Revision==revision,"Pure viewer projection never advances the rule state.");}
    private static MainViewModel Restore(GameEngine game,ContentRegistry registry)
    {var store=new MemorySaveStore();store.Write(GameSaveSlot.Manual,new(1,DateTimeOffset.UtcNow,false,game.CreateCheckpoint()));
        var model=new MainViewModel(false,game.Seed,true,store,useExpandedContent:true,contentRegistry:registry){IsMotionEnabled=false};
        model.LoadManualGameCommand.Execute(null);Program.Assert(!model.HasSaveError,model.SaveStatus);return model;}
    private static void Reach(GameEngine game,Func<PendingDecision,bool> goal)
    {for(var n=0;n<64;n++){if(game.CreateSnapshot(0).PendingDecision is{}p&&goal(p))return;Accept(game,new AdvanceOneStepCommand(game.Revision));}throw new InvalidOperationException("Fixed exact-order fixture did not reach prompt.");}
    private static void Accept(GameEngine game,GameCommand command){var result=game.Submit(command);Program.Assert(result.Accepted,result.Error?.Message??"Expected accepted fixture command.");}
    private static void Render(MainViewModel model,string output,string file)
    {var window=new MainWindow(model);window.ApplyTemplate();var root=(FrameworkElement)window.Content;Program.Render(root,1120,740,Path.Combine(output,file));
        Program.Assert(Program.Find<TextBlock>(root).Any(t=>t.Text.Contains("仅你可见")&&t.Visibility==Visibility.Visible&&t.ActualWidth>0),"The native private panel renders its actual privacy title.");window.Content=null;window.Close();}
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-ui-exact-top",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {var skill=SkillProgramCatalog.Load("{\"schemaVersion\":"+SkillProgramCatalog.RulesSchemaVersion+",\"skills\":[{\"id\":\"fixture:ui-exact-top\",\"revision\":1,\"triggers\":[{\"id\":\"order\",\"window\":\"drawPhaseStarting\",\"subject\":\"owner\",\"optional\":true,\"drawPhaseMode\":\"additive\",\"effects\":[{\"op\":\"reorderTopCards\",\"target\":\"owner\",\"amount\":4,\"exactTopCount\":2}]}]}]}",
            "{\"schemaVersion\":3,\"skills\":{\"fixture:ui-exact-top\":{\"name\":\"精确排序\",\"description\":\"两张顶，其余底\"}}}").Programs["fixture:ui-exact-top"];
         b.AddSkill(new("fixture:ui-exact-top","精确排序","两张顶，其余底"){Program=skill});
         b.AddGeneral(new("fixture:ui-exact-owner","排序角色","supporter","fixture:ui-exact-top","wei",3));
         for(var n=1;n<4;n++)b.AddGeneral(new($"fixture:ui-exact-{n}","其他"+n,"supporter","standard:none","wei",5));
         b.AddDeck(new("fixture:ui-exact-deck","固定实体",4,2,[]){PhysicalCards=Enumerable.Range(0,48).Select(i=>new ContentDeckPhysicalCard("standard:dodge",Suit.Heart,i%13+1)).ToArray()});
         b.AddMode(new("identity:ui-exact-top","私看精确排序",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:ui-exact-deck",GeneralCandidateCount:4,
            GeneralPoolIds:["fixture:ui-exact-owner","fixture:ui-exact-1","fixture:ui-exact-2","fixture:ui-exact-3"]));}
    }
}
