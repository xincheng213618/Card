using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;
using static PopulationTopReorderScenario;

internal static class PopulationTopReorderUiChecks
{
    internal static void PrivateFacesRestoreAndNativeOrder(string output)
    {
        var(game,registry)=Start();using var model=Restore(game,registry);
        Click(model,c=>Action(c)=="activate",output,"440-population-start.png");game=Program.Engine(model);
        var ids=Frame(game).TopReorder!.ViewedCardIds.ToArray();var faces=game.CreateSnapshot(0).PrivateRevealedCards!;
        Program.Assert(ids.Length==5&&model.PrivatelyViewedCards.Select(c=>c.Id).SequenceEqual(ids)&&
            model.PrivatelyViewedCards.All(c=>c.IsPrivateReveal&&!c.IsPlayable&&c.Rank.Length>0&&c.SuitGlyph.Length>0)&&
            model.PrivateRevealTitle.Contains("观星")&&model.PrivateRevealTitle.Contains("仅你可见")&&model.SkillChoices.Any(c=>c.Parameters.GetValueOrDefault("action")=="finish-top"),"Native population free panel exposes one complete owner-only frozen five-card view and full free order choices.");
        Privacy(game);File.WriteAllText(Path.Combine(output,"440-population-view.checkpoint.json"),GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        Click(model,c=>c.Cards.SequenceEqual(new[]{ids[1]}),output,"440-population-top-choice.png");
        using var partial=Restore(Program.Engine(model),registry);game=Program.Engine(partial);
        Program.Assert(Frame(game).TopReorder!.TopCardIds.SequenceEqual(new[]{ids[1]})&&partial.PrivatelyViewedCards.Select(c=>c.Id).SequenceEqual(ids)&&
            partial.PrivatelyViewedCards.Select(c=>c.Rank).SequenceEqual(faces.Select(c=>c.RankText)),"Manual load keeps partial top draft and full originally-viewed faces, ranks and original order.");Privacy(game);
        Click(partial,c=>c.Parameters.GetValueOrDefault("action")=="finish-top",output,"440-population-bottom-start.png");
        using var bottom=Restore(Program.Engine(partial),registry);game=Program.Engine(bottom);
        Program.Assert(Frame(game).TopReorder!.ChoosingBottom&&bottom.SkillChoices.Count==4&&bottom.SkillChoices.All(c=>c.Parameters.GetValueOrDefault("action")=="bottom")&&game.PendingDecision!.SkillPrompt!.Instructions.Contains("最靠牌堆底"),"Restored bottom stage explains the real bottom-first order through shared skill controls.");Privacy(game);
        File.WriteAllText(Path.Combine(output,"440-population-bottom.checkpoint.json"),GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        foreach(var id in ids.Where(id=>id!=ids[1]).Reverse())Click(bottom,c=>c.Cards.SequenceEqual(new[]{id}),output,"440-population-bottom-choice.png");
        game=Program.Engine(bottom);for(var i=0;i<32&&Pending(game)?.Kind!=DecisionKind.PlayCard;i++){Program.Assert(bottom.CanStepAi,"Committed order resumes native preparation.");bottom.StepAiCommand.Execute(null);game=Program.Engine(bottom);}
        Program.Assert(!bottom.HasPrivatelyViewedCards&&!game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.TopReorder!=null)&&!Eligible(game),"Native completion clears private faces and a real nonempty top partition earns no all-bottom ending state.");
        File.WriteAllText(Path.Combine(output,"440-population-complete.checkpoint.json"),GameCheckpointJson.Serialize(game.CreateCheckpoint()));Replay(game,registry);
    }
    private static void Click(MainViewModel model,Func<PromptChoice,bool> choose,string output,string file)
    {
        var game=Program.Engine(model);var revision=game.Revision;var choice=model.SkillChoices.Single(choose);
        var window=new MainWindow(model);window.ApplyTemplate();var root=(FrameworkElement)window.Content;Program.Render(root,1120,740,Path.Combine(output,file));
        var controls=Program.Find<ItemsControl>(root).Single(c=>c.Name=="ScrollingSkillChoices");
        var button=Program.Find<Button>(controls).Single(b=>b.Command==model.SelectSkillChoiceCommand&&b.CommandParameter is PromptChoice p&&p.Id==choice.Id&&b.ActualWidth>0);
        button.BringIntoView();root.UpdateLayout();Program.Assert(button.IsEnabled&&button.Visibility==Visibility.Visible,"Real enabled shared choice button carries the exact current prompt entity/action.");button.Command!.Execute(button.CommandParameter);
        Program.Assert(game.Revision==revision+1,"Each native control accepts exactly one real command.");window.Content=null;window.Close();
    }
    private static void Privacy(GameEngine game)
    {var rev=game.Revision;foreach(var viewer in Enumerable.Range(0,4).Append(-1)){var s=game.CreateSnapshot(viewer);Program.Assert(viewer==0?s.PendingDecision?.Kind==DecisionKind.ProgramTopReorder&&s.PrivateRevealedCards?.Count==5:s.PendingDecision==null&&s.PrivateRevealedCards==null,"Four seats and spectator preserve full-view privacy.");}Program.Assert(game.Revision==rev,"Pure viewer projections do not advance.");}
    private static MainViewModel Restore(GameEngine game,ContentRegistry registry)
    {var store=new MemorySaveStore();store.Write(GameSaveSlot.Manual,new(1,DateTimeOffset.UtcNow,false,game.CreateCheckpoint()));var model=new MainViewModel(false,game.Seed,true,store,useExpandedContent:false,contentRegistry:registry){IsMotionEnabled=false};model.LoadManualGameCommand.Execute(null);Program.Assert(!model.HasSaveError,model.SaveStatus);return model;}
}
