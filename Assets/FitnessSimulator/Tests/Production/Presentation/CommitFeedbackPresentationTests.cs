using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
namespace FitnessSimulator.Game.Tests.Presentation
{
 public sealed class CommitFeedbackPresentationTests
 {
  private GameObject host;private GameFrontendView ui;private Stub stub;
  private sealed class Stub:IGameFrontend
  {
   public FrontendView View;public int Commands,Advances;
   public FrontendView ReadView()=>View;
   public FrontendReply Execute(FrontendAction a){Commands++;return new FrontendReply{Accepted=true};}
   public void Advance(double delta){Advances++;}public void SetSuspended(bool suspended){}
  }
  private static FrontendView Page(string screen="meal/test",long revision=4,string run="run",bool transition=false)=>new FrontendView{RunId=run,Revision=revision,ScreenId=screen,Title="具体业务："+screen,Body="具体业务内容",Status="D1 · 上午",CanOpenAuxiliary=true,IsTransitioning=transition,Actions=new[]{new FrontendOption{Id="continue",Label="业务按钮"},new FrontendOption{Id="open-menu",Label="返回主菜单"}}};
  [SetUp]public void Setup(){stub=new Stub{View=Page()};host=new GameObject("CommitFeedbackFixture");ui=host.AddComponent<GameFrontendView>();ui.PreferenceFileOverride=Path.Combine(Path.GetTempPath(),"Fitness-CommitUI",Guid.NewGuid().ToString("N"),"preferences.json");ui.Initialize(stub,AssetDatabase.LoadAssetAtPath<Font>("Assets/FitnessSimulator/Production/Presentation/Fonts/NotoSansCJKsc-Regular.otf"));}
  [TearDown]public void Cleanup(){if(host)UnityEngine.Object.DestroyImmediate(host);}
  private void Invoke(string method,params object[] arguments)=>typeof(GameFrontendView).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ui,arguments);
  private Text Text(string name)=>host.GetComponentsInChildren<Text>(true).Single(t=>t.name==name);
  private Button ActionButton()=>host.GetComponentsInChildren<Button>(false).Single(b=>b.name=="业务按钮");
  private void Render()=>Invoke("Render",false);
  [Test]public void TransitionRetainsBusinessPageBlocksOldActionsAndKeepsAdvancing()
  {
   string title=Text("Heading").text;stub.View=Page("EnterDay",4,transition:true);Render();Assert.That(Text("Heading").text,Is.EqualTo(title));Assert.That(Text("CommitIndicator").text,Is.EqualTo("·"));Assert.That(ActionButton().IsInteractable(),Is.False);
   ActionButton().onClick.Invoke();Invoke("SaveDraft");Assert.That(stub.Commands,Is.Zero);
   Invoke("Update");Assert.That(stub.Advances,Is.EqualTo(1));Assert.That(Text("Heading").text,Is.EqualTo(title));
   stub.View=Page("meal/next",4);Render();Assert.That(Text("Heading").text,Is.EqualTo("具体业务：meal/next"));Assert.That(ActionButton().IsInteractable(),Is.True);Assert.That(Text("CommitIndicator").text,Is.Empty);
  }
  [Test]public void AcceptedWithoutRevisionDoesNotClaimSavedAndSameFrameCannotDoubleSubmit()
  {
   ActionButton().onClick.Invoke();ActionButton().onClick.Invoke();Assert.That(stub.Commands,Is.EqualTo(1));Assert.That(Text("CommitIndicator").text,Is.Empty);Render();Assert.That(Text("CommitIndicator").text,Is.Empty);
   stub.View=Page(revision:5);Render();Assert.That(Text("CommitIndicator").text,Is.EqualTo("✓"));Assert.That(ActionButton().IsInteractable(),Is.True,"Commit flash cannot delay actions");
   typeof(GameFrontendView).GetField("committedFlashUntil",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(ui,-1f);Render();Assert.That(Text("CommitIndicator").text,Is.Empty);
   stub.View=Page("menu",6);Render();Assert.That(Text("CommitIndicator").text,Is.Empty,"Menu navigation is not a saved notification");
  }
  [Test]public void ErrorWinsOverRetainedPageAndNewRunUsesItsOwnTransitionRecap()
  {
   stub.View=Page("EnterDay",4,transition:true);Render();stub.View=Page("error",4);stub.View.Body="真实存储失败，可重试";Render();Assert.That(Text("Story").text,Is.EqualTo("真实存储失败，可重试"));Assert.That(Text("CommitIndicator").text,Is.Empty);Assert.That(ActionButton().IsInteractable(),Is.True);
   stub.View=Page("EnterDay",1,"replacement",true);stub.View.Title="新局早餐已完成";stub.View.Body="正在进入上午事件";Render();Assert.That(Text("Heading").text,Is.EqualTo("新局早餐已完成"));Assert.That(Text("CommitIndicator").text,Is.EqualTo("·"));Assert.That(ActionButton().IsInteractable(),Is.False);
  }
 }
}
