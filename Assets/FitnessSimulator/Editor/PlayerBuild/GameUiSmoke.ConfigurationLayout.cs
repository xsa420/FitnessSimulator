// Five-screen layout fixture only. No game state, entitlement, payment or probability outcome is produced.
using System;
using System.Linq;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Configuration;
using FitnessSimulator.Game.Planning;
using UnityEngine;
using UnityEngine.UI;

namespace FitnessSimulator.Game.Editor
{
 public static partial class GameUiSmoke
 {
  private static ConfigurationLayoutFrontend configurationLayout;
  private static int configurationLayoutStep;
  private static readonly string[] ConfigurationCaptures={"configuration-template-description","configuration-prefill-confirmation","configuration-story-access-cost-probability","configuration-knowledge-card","configuration-audio-preferences"};
  private sealed class ConfigurationLayoutFrontend:IGameFrontend,IFrontendContentProvider
  {
   public readonly FrontendView View;
   public ConfigurationLayoutFrontend()
   {
    var templates=ContentRuntime.Current.Get<TemplatesDocument>("templates");
    View=new FrontendView{RunId="configuration-layout-only",Revision=1,ScreenId="InitialTemplate",Title="选择新人物 · 布局样本",Status="配置布局夹具 · 非业务验收",CanOpenAuxiliary=true,
     Body="选择新局人物。说明与优势、短板显示在各模板按钮中；此处仅为布局样本。",
     Actions=templates.Templates.OrderBy(t=>t.DisplayOrder).Select(t=>new FrontendOption{Id="new-game",StringValue=t.Id,Label="新游戏 · "+t.Name+"\n"+t.Description+"\n优势："+t.Advantage+"；短板："+t.Drawback,Enabled=t.Id==templates.DefaultId,DisabledReason=t.LockedHint}).ToArray()};
   }
   public ContentPackage ReadContent()=>ContentRuntime.Current;
   public FrontendView ReadView()=>View;
   public FrontendReply Execute(FrontendAction action)=>throw new InvalidOperationException("Configuration layout must not submit gameplay: "+action.ActionId);
   public void Advance(double seconds){} public void SetSuspended(bool value){}
  }
  private static void TickConfigurationLayout(double now)
  {
   if(now<nextAction)return;nextAction=now+.7;var view=configurationLayout.View;
   switch(configurationLayoutStep++)
   {
    case 0:
     inputs.Add("configuration-layout-only:five-screens:no-gameplay-results");
     Capture(ConfigurationCaptures[0],view);
     var draft=PlanCatalog.CreateBenchPressSlice();
     view.ScreenId="plan-layout";view.Title="组合预填布局";view.Body="加载预填前保留动作，取消不替换。";view.Revision++;
     view.PlanAnchorOptions=new[]{new FrontendOption{Id="noon",Label="中午"},new FrontendOption{Id="evening",Label="晚上"}};
     view.Actions=new[]{new FrontendOption{Id="save-plan",Label="编辑计划布局",StringValue=JsonUtility.ToJson(draft)}};break;
    case 1:ClickOption(view,"save-plan");break;
    case 2:Click("动作、组合、时段与排序");break;
    case 3:Click("载入组合预填动作");break;
    case 4:
     if(FindButton("保留动作")==null||FindButton("载入预填")==null)throw new InvalidOperationException("Prefill confirmation missing");
     Capture(ConfigurationCaptures[1],view);Click("保留动作");break;
    case 5:Click("返回计划总览");break;
    case 6:Click("关闭编辑");break;
    case 7:
     view.ScreenId="narrative";view.Title="剧情局部选项 · 布局样本";view.Revision++;
     view.Body="林悦：你想怎样回应？\n以下概率仅为布局占位；正式冻结概率由独立应用短例验证。";
     view.Actions=new[]{new FrontendOption{Id="story-choice",StringValue="listen",Label="认真回应\n费用 20 元\n71.0%"},new FrontendOption{Id="story-choice",StringValue="ask",Label="另一种回应\n费用 400 元",Enabled=false,DisabledReason="余额不足／资格条件未满足"}};break;
    case 8:Capture(ConfigurationCaptures[2],view);Click("知识与声音");break;
    case 9:Click("日常活动也算");break;
    case 10:
     // Show the real sourced card. Its scroll viewport is intentionally not flattened into a fake text field.
     if(!host.GetComponentsInChildren<Text>(false).Any(t=>t.text.Contains("与游戏不同：")&&t.text.Contains("核验：")))throw new InvalidOperationException("Knowledge provenance missing");
     Capture(ConfigurationCaptures[3],view);Click("知识目录");break;
    case 11:Click("音乐：开");break;
    case 12:Click("音效：开");break;
    case 13:
     var music=FindButton("音乐：关");var sfx=FindButton("音效：关");
     if(music==null||sfx==null)throw new InvalidOperationException("Audio switches not applied");
     VisiblePointer(sfx.gameObject,70,true);break;
    case 14:Capture(ConfigurationCaptures[4],view);Click("返回游戏");Finish(true,null);break;
   }
  }
 }
}
