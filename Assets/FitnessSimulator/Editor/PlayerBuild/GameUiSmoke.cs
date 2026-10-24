using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FitnessSimulator.Game.Editor
{
    // Editor-only automation: real application + actual uGUI callbacks, no result injection.
    [InitializeOnLoad]
    public static partial class GameUiSmoke
    {
        private const string PendingKey = "FitnessSimulator.UiSmoke.Pending";
        private const string OutputKey = "FitnessSimulator.UiSmoke.Output";
        private static GameSession session;
        private static GameObject host, cameraObject;
        private static Camera camera;
        private static RenderTexture target;
        private static double started, nextAction, lastPointerTick;
        private static string output;
        private static bool planOpened, leftDown, rightDown, finishing;
        private static int verificationPhase;
        private static string preservedRun;
        private static readonly string[] RequiredCaptures = { "menu", "initial-plan", "meal", "missed-training", "bench-preset", "bench-running", "group-result", "day-end", "existing-new-confirm", "existing-cancel", "existing-continued", "corrupt-exit", "corrupt-confirm", "corrupt-recovered" };
        private static readonly HashSet<string> captured = new HashSet<string>();
        private static readonly List<CaptureEvidence> captures = new List<CaptureEvidence>();
        private static readonly List<string> inputs = new List<string>();

        [Serializable] private sealed class CaptureEvidence { public string name, path, sha256; public int width, height; public long revision; public float canvasWidth, canvasHeight; public int[] renderedFontSizes; }
        [Serializable] private sealed class Evidence
        {
            public string status, source = "Test", inputSource = "ScriptReplay", platform = "EditorGraphicsProxy";
            public string renderer = "Actual uGUI rendered by Camera into RenderTexture (not device screen capture)";
            public string focusPolicy = "Editor-only OS focus bypass; this run is NOT lifecycle evidence";
            public string fixtureDirectory, error, startedUtc, finishedUtc;
            public string scenario = "ActualGameplay";
            public bool layoutFixture, businessRulesVerified = false;
            public uint seed = 17;
            public bool injectedResult = false, androidTouchVerified = false;
            public CaptureEvidence[] captures;
            public string[] inputEvents;
        }
        private static Evidence evidence;

        static GameUiSmoke()
        {
            EditorApplication.playModeStateChanged += ChangedPlayMode;
            if (UnityEditor.SessionState.GetBool(PendingKey, false) && EditorApplication.isPlaying)
                EditorApplication.delayCall += StartInPlayMode;
        }

        [MenuItem("Fitness Simulator/Player/Run real UI smoke (isolated test save)")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start UI smoke from edit mode.");
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException("UI smoke requires graphics; omit -nographics.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save or discard the current scene explicitly before UI smoke.");
            var arguments = Environment.GetCommandLineArgs(); int stageIndex = Array.IndexOf(arguments, "-fitnessEvidenceStage");
            string stage = stageIndex >= 0 ? arguments[stageIndex + 1] : "M1";
            if (stage != "M1" && stage != "ConfigR1" && stage != "SaveR1") throw new ArgumentException("Unsupported evidence stage");
            string directory = Path.GetFullPath("TestResults/" + stage + "/Captures/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(directory);
            UnityEditor.SessionState.SetString(OutputKey, directory);
            UnityEditor.SessionState.SetBool(PendingKey, true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        private static void ChangedPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode && UnityEditor.SessionState.GetBool(PendingKey, false))
                StartInPlayMode();
        }

        private static void StartInPlayMode()
        {
            if (session != null || finishing) return;
            try
            {
                output = UnityEditor.SessionState.GetString(OutputKey, "");
                string project = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..")) + Path.DirectorySeparatorChar;
                if (string.IsNullOrEmpty(output) || !Path.GetFullPath(output).StartsWith(project, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid smoke output path.");
                evidence = new Evidence { status = "Running", fixtureDirectory = Path.Combine(output, "FixtureSave"), startedUtc = DateTime.UtcNow.ToString("O") };
                saveFeedbackSmoke = Environment.GetCommandLineArgs().Contains("-fitnessSaveFeedbackSmoke");
                if (saveFeedbackSmoke) CopySaveFeedbackSource(evidence.fixtureDirectory);
                session = GameSession.OpenTest(evidence.fixtureDirectory, evidence.seed);
                host = new GameObject("UiSmokeFrontend");
                var frontendView = host.AddComponent<GameFrontendView>();
                frontendView.PreferenceFileOverride = Path.Combine(output, "presentation-preferences.json");
                if (saveFeedbackSmoke) PresentationPreferenceStore.Save(frontendView.PreferenceFileOverride, new PresentationPreferences { Audio = AudioContent.Defaults(), CardsEnabled = false });
                frontendView.IgnoreHostFocusForEditorSmoke = true;
                relationshipLayout = Environment.GetCommandLineArgs().Contains("-fitnessRelationshipLayoutSmoke") ? new RelationshipLayoutFrontend() : null;
                configurationLayout = Environment.GetCommandLineArgs().Contains("-fitnessConfigurationLayoutSmoke") ? new ConfigurationLayoutFrontend() : null;
                configurationLayoutStep = 0;
                relationshipLayoutStep = 0;
                evidence.layoutFixture = relationshipLayout != null || configurationLayout != null;
                evidence.scenario = saveFeedbackSmoke ? "SaveFeedbackLayoutFromActualArchive" : configurationLayout != null ? "ConfigurationLayoutOnly" : relationshipLayout != null ? "RelationshipLayoutOnly" : "ActualGameplay";
                frontendView.Initialize(configurationLayout != null ? (IGameFrontend)configurationLayout : relationshipLayout != null ? (IGameFrontend)relationshipLayout : session, AssetDatabase.LoadAssetAtPath<Font>(GamePlayerBuild.FontPath));
                if (saveFeedbackSmoke) frontendView.enabled = false; // Capture driver controls the exact actual transition frame.
                cameraObject = new GameObject("UiSmokeCaptureCamera");
                camera = cameraObject.AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.035f, .065f, .09f); camera.orthographic = true;
                camera.nearClipPlane = .1f; camera.farClipPlane = 100;
                target = new RenderTexture(evidence.layoutFixture || saveFeedbackSmoke ? 640 : 1280, evidence.layoutFixture || saveFeedbackSmoke ? 320 : 720, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
                var canvas = host.GetComponentInChildren<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                captured.Clear(); captures.Clear(); inputs.Clear(); planOpened = leftDown = rightDown = false; verificationPhase = 0;
                started = EditorApplication.timeSinceStartup; nextAction = started + 1;
                EditorApplication.update += Tick;
                UnityEngine.Application.runInBackground = true;
                EditorApplication.isPaused = false;
            }
            catch (Exception exception) { Finish(false, exception.ToString()); }
        }

        private static void Tick()
        {
            if (session == null || finishing) return;
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (now - started > 240) { Finish(false, "Timed out waiting for required UI captures."); return; }
                if (saveFeedbackSmoke) { TickSaveFeedback(); return; }
                if (relationshipLayout != null) { TickRelationshipLayout(now); return; }
                if (configurationLayout != null) { TickConfigurationLayout(now); return; }
                var view = session.ReadView();
                if (verificationPhase > 0)
                {
                    if (now < nextAction) return;
                    nextAction = now + .7;
                    VerifyRecovery(view); return;
                }
                if (view.ScreenId == "error") { Finish(false, view.Body); return; }
                if (view.BenchPress != null && !view.BenchPress.IsComplete)
                {
                    if (now >= nextAction && !captured.Contains("bench-running")) Capture("bench-running", view);
                    if (now - lastPointerTick >= .02)
                    {
                        // Feedback controller feeds actual down/up events; it does not set reps/results.
                        SetHold(true, view.BenchPress.Balance > .06);
                        SetHold(false, view.BenchPress.Balance < -.06);
                        lastPointerTick = now;
                    }
                    return;
                }
                leftDown = rightDown = false;
                if (now < nextAction) return;
                nextAction = now + .5;
                if ((view.Body ?? "").Contains("本次训练没有开始")) Capture("missed-training", view);
                if (view.ScreenId == "menu")
                {
                    if (FindButton("确认") != null) { Click("确认"); return; }
                    Capture("menu", view);
                    ClickOption(view, view.Actions.Any(x => x.Id == "continue-game" && x.Enabled) ? "continue-game" : "new-game"); return;
                }
                if (view.ScreenId == "InitialPlan")
                {
                    if (!planOpened) { ClickOption(view, "save-plan"); planOpened = true; return; }
                    Capture("initial-plan", view); Click("保存并退出"); return;
                }
                // All choices go through the visible confirmation button, including meals.
                if (FindButton("确认") != null) { Click("确认"); return; }
                if (view.ScreenId == "AwaitNextDay")
                {
                    if (captured.Contains("bench-running")) { Capture("day-end", view); preservedRun = view.RunId; ClickOption(view, "open-menu"); verificationPhase = 1; }
                    else ClickOption(view, "next-day");
                    return;
                }
                if ((view.ScreenId ?? "").StartsWith("meal/", StringComparison.Ordinal))
                { Capture("meal", view); ClickOption(view, "meal"); return; }
                if (view.Actions.Any(x => x.Id == "begin-set" && x.Enabled))
                {
                    if (captured.Contains("bench-running")) ClickOption(view, "end-training");
                    else { Capture("bench-preset", view); ClickOption(view, "begin-set", "manual"); nextAction = now + 1; }
                    return;
                }
                if (view.Actions.Any(x => x.Id == "choose-entry" && x.Enabled))
                { ClickOption(view, "choose-entry", "training-choice"); return; }
                foreach (string id in new[] { "settle", "continue", "finish-anchor" })
                    if (view.Actions.Any(x => x.Id == id && x.Enabled)) { if (id == "continue" && captured.Contains("bench-running")) Capture("group-result", view); ClickOption(view, id); return; }
                // System transitions are driven by GameFrontendView.Update / GameSession.Advance.
            }
            catch (Exception exception) { Finish(false, exception.ToString()); }
        }

        private static void VerifyRecovery(FrontendView view)
        {
            switch (verificationPhase)
            {
                case 1: ClickOption(view, "new-game"); break;
                case 2: Capture("existing-new-confirm", view); Click("返回选择"); break;
                case 3:
                    if (view.RunId != preservedRun) throw new InvalidOperationException("Cancel replaced the run.");
                    Capture("existing-cancel", view); ClickOption(view, "continue-game"); break;
                case 4:
                    if (view.RunId != preservedRun || view.ScreenId != "AwaitNextDay") throw new InvalidOperationException("Continue did not preserve the day end.");
                    Capture("existing-continued", view);
                    long generation = session.InspectSnapshot().Generation;
                    session.Dispose();
                    File.WriteAllBytes(Path.Combine(evidence.fixtureDirectory, generation % 2 == 0 ? "state-a.bin" : "state-b.bin"), new byte[] { 7, 8, 9 });
                    host.SetActive(false); UnityEngine.Object.Destroy(host);
                    session = GameSession.OpenTest(evidence.fixtureDirectory, evidence.seed);
                    host = new GameObject("UiSmokeRecoveryFrontend");
                    var frontendView = host.AddComponent<GameFrontendView>(); frontendView.IgnoreHostFocusForEditorSmoke = true;
                    frontendView.PreferenceFileOverride = Path.Combine(output, "presentation-preferences.json");
                    frontendView.Initialize(session, AssetDatabase.LoadAssetAtPath<Font>(GamePlayerBuild.FontPath));
                    var canvas = host.GetComponentInChildren<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                    inputs.Add("fixture:corrupt-current-generation-in-isolated-test-save");
                    break;
                case 5:
                    if (view.ScreenId != "error") throw new InvalidOperationException("Corrupt fixture did not expose error screen.");
                    Capture("corrupt-exit", view); ClickOption(view, "new-game"); break;
                case 6: Capture("corrupt-confirm", view); Click("确认"); break;
                case 7:
                    if (view.ScreenId != "InitialPlan") throw new InvalidOperationException("New-game confirmation did not open the initial plan draft.");
                    ClickOption(view, "save-plan"); break;
                case 8:
                    Click("保存并退出"); break;
                case 9:
                    if (view.ScreenId == "InitialPlan" || view.ScreenId == "error")
                        throw new InvalidOperationException("Legal plan save did not commit the replacement run.");
                    if (string.IsNullOrEmpty(view.RunId) || view.RunId == preservedRun || session.InspectSnapshot()?.RunId != view.RunId)
                        throw new InvalidOperationException("Replacement run was not committed after saving the plan.");
                    if (!(view.ScreenId ?? "").StartsWith("meal/", StringComparison.Ordinal)) return;
                    inputs.Add("verified:replacement-run-committed-and-first-meal-visible-after-plan-save");
                    Capture("corrupt-recovered", view); Finish(true, null); return;
            }
            verificationPhase++;
        }

        private static void ClickOption(FrontendView view, string id, string preferredValue = null)
        {
            var options = view.Actions.Where(x => x.Id == id && x.Enabled).ToArray();
            var option = options.FirstOrDefault(x => preferredValue != null && x.StringValue == preferredValue) ?? options.FirstOrDefault();
            if (option == null) throw new InvalidOperationException("No legal visible action " + id);
            Click(option.Label);
        }
        private static Button FindButton(string caption) => host.GetComponentsInChildren<Button>(false).FirstOrDefault(x => x.gameObject.name == caption && x.interactable);
        private static void Click(string caption)
        {
            var button = FindButton(caption);
            if (!button) throw new InvalidOperationException("Visible uGUI button missing: " + caption);
            var data = VisiblePointer(button.gameObject, 70, true);
            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerClickHandler);
            inputs.Add("click:" + caption);
        }
        private static void SetHold(bool isLeft, bool down)
        {
            bool previous = isLeft ? leftDown : rightDown;
            if (previous == down) return;
            var button = host.GetComponentsInChildren<HoldInputButton>(false).Single(x => x.name == (isLeft ? "LeftHold" : "RightHold"));
            var data = VisiblePointer(button.gameObject, isLeft ? 71 : 72, false);
            if (down) ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerDownHandler);
            else ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerUpHandler);
            if (isLeft) leftDown = down; else rightDown = down;
            inputs.Add((isLeft ? "left:" : "right:") + (down ? "down" : "up"));
        }

        private static PointerEventData VisiblePointer(GameObject targetObject, int pointerId, bool allowScroll)
        {
            Canvas.ForceUpdateCanvases();
            var rect = targetObject.GetComponent<RectTransform>();
            var scroll = targetObject.GetComponentInParent<ScrollRect>();
            if (scroll != null)
            {
                for (int step = 0; step < 80; step++)
                {
                    var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, rect);
                    var viewport = scroll.viewport.rect;
                    if (bounds.min.y >= viewport.yMin - .1f && bounds.max.y <= viewport.yMax + .1f) break;
                    if (!allowScroll || step == 79) throw new InvalidOperationException("Clipped control: " + targetObject.name);
                    var wheel = new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0, bounds.min.y < viewport.yMin ? -1 : 1) };
                    scroll.scrollSensitivity = 24;
                    ExecuteEvents.Execute(scroll.gameObject, wheel, ExecuteEvents.scrollHandler);
                    Canvas.ForceUpdateCanvases();
                    inputs.Add("scroll:" + scroll.name + ":" + wheel.scrollDelta.y);
                }
            }
            Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
            var data = new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left, pointerId = pointerId };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(data, hits);
            if (hits.Count == 0 || (hits[0].gameObject != targetObject && !hits[0].gameObject.transform.IsChildOf(targetObject.transform)))
                throw new InvalidOperationException("Control is covered or cannot receive pointer: " + targetObject.name);
            return data;
        }

        private static void Capture(string name, FrontendView view)
        {
            if (!captured.Add(name)) return;
            Canvas.ForceUpdateCanvases(); camera.Render();
            var previous = RenderTexture.active; var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
                var pixels = texture.GetPixels32();
                int darkest = 255, brightest = 0;
                for (int i = 0; i < pixels.Length; i += 7)
                {
                    int value = Math.Max(pixels[i].r, Math.Max(pixels[i].g, pixels[i].b));
                    darkest = Math.Min(darkest, value); brightest = Math.Max(brightest, value);
                }
                if (brightest - darkest < 30) throw new InvalidOperationException("Capture is uniform/blank: " + name);
                byte[] png = texture.EncodeToPNG(); string path = Path.Combine(output, name + ".png"); File.WriteAllBytes(path, png);
                string hash; using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(png)).Replace("-", "");
                var canvasRect = host.GetComponentInChildren<Canvas>().GetComponent<RectTransform>().rect;
                captures.Add(new CaptureEvidence { name = name, path = path, sha256 = hash, width = target.width, height = target.height, revision = view.Revision,
                    canvasWidth = canvasRect.width, canvasHeight = canvasRect.height,
                    renderedFontSizes = host.GetComponentsInChildren<Text>(false).Select(x => x.fontSize).Distinct().OrderBy(x => x).ToArray() });
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.Destroy(texture); }
        }

        private static void Finish(bool success, string error)
        {
            if (finishing) return;
            finishing = true; EditorApplication.update -= Tick;
            UnityEditor.SessionState.SetBool(PendingKey, false);
            var required = saveFeedbackSmoke ? new[] { "save-feedback-retained-business" } : configurationLayout != null ? ConfigurationCaptures : relationshipLayout != null ? RelationshipCaptures : RequiredCaptures;
            bool complete = required.All(captured.Contains);
            complete &= captures.Count == required.Length;
            success &= complete;
            if (evidence == null) evidence = new Evidence();
            evidence.status = success ? "Passed" : "Failed"; evidence.error = error ?? (complete ? null : "Required captures missing.");
            evidence.finishedUtc = DateTime.UtcNow.ToString("O"); evidence.captures = captures.ToArray(); evidence.inputEvents = inputs.ToArray();
            if (!string.IsNullOrEmpty(output)) File.WriteAllText(Path.Combine(output, "ui-evidence.json"), JsonUtility.ToJson(evidence, true));
            session?.Dispose(); session = null;
            if (target) { target.Release(); UnityEngine.Object.Destroy(target); }
            if (host) UnityEngine.Object.Destroy(host);
            if (cameraObject) UnityEngine.Object.Destroy(cameraObject);
            Debug.Log((success ? "FITNESS_UI_SMOKE_OK " : "FITNESS_UI_SMOKE_FAILED ") + output + " " + error);
            EditorApplication.ExitPlaymode();
            if (Environment.GetCommandLineArgs().Contains("-fitnessUiSmokeExit")) EditorApplication.Exit(success ? 0 : 1);
            finishing = false;
        }
    }
}
