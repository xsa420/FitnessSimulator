using System;
using FitnessSimulator.Game.Application;
using UnityEngine;
using UnityEngine.UI;
namespace FitnessSimulator.Game.Presentation
{
 public sealed partial class GameFrontendView
 {
  private Text commitIndicator;
  private CanvasGroup transitionControls;
  private bool awaitingBusinessRender, retainedBusinessPage, wasTransitioning;
  private string observedCommitRun, retainedBusinessRun;
  private long observedCommitRevision;
  private float committedFlashUntil;
  private void BuildCommitIndicator(Transform header)
  {
   transitionControls=safeRoot.gameObject.AddComponent<CanvasGroup>();
   commitIndicator=Label("CommitIndicator",header,16);commitIndicator.alignment=TextAnchor.MiddleCenter;commitIndicator.raycastTarget=false;
   Stretch(commitIndicator.rectTransform,new Vector2(1,0),Vector2.one,new Vector2(-36,0),new Vector2(-8,0));
  }
  private bool BusinessSubmissionBlocked()=>awaitingBusinessRender||frontend.ReadView()?.IsTransitioning==true;
  private void LockUntilNextBusinessRender(){awaitingBusinessRender=true;if(transitionControls)transitionControls.interactable=false;}
  private static bool ClearsBusinessPage(FrontendView candidate)=>candidate.ScreenId=="menu"||(candidate.ScreenId??"").StartsWith("Initial",StringComparison.OrdinalIgnoreCase);
  private bool PrepareTransitionPresentation(FrontendView candidate,ref bool force)
  {
   bool error=candidate.ScreenId=="error";
   bool changedRun=observedCommitRun!=candidate.RunId;
   bool boundary=changedRun||ClearsBusinessPage(candidate);
   if(boundary||error){retainedBusinessPage=false;retainedBusinessRun=null;committedFlashUntil=0;}
   if(changedRun){
    // A draft/confirmation from another run may never act on the replacement root.
    planDraft=null;draftDirty=false;if(modalKind!=null)CloseModal();force=true;
    observedCommitRun=candidate.RunId;observedCommitRevision=candidate.Revision;
   }else if(candidate.Revision>observedCommitRevision){
    observedCommitRevision=candidate.Revision;
    if(!boundary&&!error)committedFlashUntil=Time.unscaledTime+.55f;
   }
   bool transition=candidate.IsTransitioning&&!error;
   commitIndicator.text=error?"":Time.unscaledTime<committedFlashUntil?"✓":transition?"·":"";
   commitIndicator.color=Time.unscaledTime<committedFlashUntil?new Color(.65f,.95f,.83f):new Color(.7f,.8f,.86f);
   transitionControls.interactable=!transition;
   if(transition){
    wasTransitioning=true;
    if(retainedBusinessPage&&retainedBusinessRun==candidate.RunId)return false;
    // On cold restore there is no page to retain. The application supplies the specific business recap.
    force=true;return true;
   }
   force|=wasTransitioning||awaitingBusinessRender;
   wasTransitioning=false;awaitingBusinessRender=false;
   if(!ClearsBusinessPage(candidate)&&!error){retainedBusinessPage=true;retainedBusinessRun=candidate.RunId;}
   return true;
  }
 }
}
