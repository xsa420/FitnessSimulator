using System;
using System.Collections;
using System.IO;
using FitnessSimulator.Persistence;
using FitnessSimulator.Training;
using UnityEngine;

namespace FitnessSimulator.Presentation
{
    public sealed class ResourceLab : MonoBehaviour
    {
        public int initialExercise;
        private ExerciseCatalog catalog;
        private GameObject stage;
        private Animation animationPlayer;
        private int selected;
        private bool paused;
        private float speed = 1;
        private float cameraAngle = 35;
        private string storageStatus = "Local storage: not checked";
        private GUIStyle title, section, body, small, button;
        private bool smokeMode;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void ConfigureVerificationExecution()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fitnessSmoke") >= 0)
                Application.runInBackground = true;
        }

        private void Start()
        {
            Application.targetFrameRate = 60;
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            catalog = Resources.Load<ExerciseCatalog>("ExerciseCatalog");
            if (!catalog || catalog.exercises.Length == 0)
            {
                Debug.LogError("ExerciseCatalog is missing. Run Fitness Simulator > Setup project.");
                enabled = false;
                return;
            }
            Select(Mathf.Clamp(initialExercise, 0, catalog.exercises.Length - 1));
            string[] args = Environment.GetCommandLineArgs();
            smokeMode = Array.IndexOf(args, "-fitnessSmoke") >= 0;
            if (smokeMode) StartCoroutine(SmokeRun(args));
        }

        private void Select(int index)
        {
            if (stage) Destroy(stage);
            selected = index;
            var entry = catalog.exercises[index];
            stage = Instantiate(entry.stagePrefab);
            animationPlayer = stage.GetComponent<Animation>();
            animationPlayer.Play(entry.demonstrationClip.name);
            animationPlayer[entry.demonstrationClip.name].speed = paused ? 0 : speed;
            PositionCamera();
        }

        private void PositionCamera()
        {
            var cam = Camera.main;
            if (!cam) return;
            float rad = cameraAngle * Mathf.Deg2Rad;
            cam.transform.position = new Vector3(Mathf.Sin(rad) * 5.3f, 3.2f, Mathf.Cos(rad) * 5.3f);
            cam.transform.LookAt(new Vector3(0, 1.2f, 0));
            cam.rect = new Rect(.28f, 0, .72f, 1);
        }

        private bool CheckStorage()
        {
            try
            {
                var local = new LocalJsonStore(Path.Combine(Application.persistentDataPath, "DevelopmentProbe"));
                string nonce = Guid.NewGuid().ToString("N");
                local.Save("probe", new RunSaveEnvelope { runId = nonce, gameDay = 1, randomState = 42 });
                bool ok = local.TryLoad<RunSaveEnvelope>("probe", out var data) && data.runId == nonce && data.randomState == 42;
                storageStatus = ok ? "Local storage: WRITE / READ OK" : "Local storage: verification failed";
                return ok;
            }
            catch (Exception e)
            {
                storageStatus = "Local storage: " + e.GetType().Name;
                Debug.LogException(e);
                return false;
            }
        }

        private void PrepareStyles()
        {
            if (title != null) return;
            title = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, wordWrap = true };
            section = new GUIStyle(GUI.skin.label) { fontSize = 21, fontStyle = FontStyle.Bold, wordWrap = true };
            body = new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true };
            small = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            button = new GUIStyle(GUI.skin.button) { fontSize = 19, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(18, 8, 8, 8) };
        }

        private void OnGUI()
        {
            if (!catalog) return;
            PrepareStyles();
            float scale = Mathf.Min(Screen.width / 1600f, Screen.height / 900f);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1600 * scale) * .5f, (Screen.height - 900 * scale) * .5f), Quaternion.identity, Vector3.one * scale);
            Color old = GUI.color;
            GUI.color = new Color(.035f, .06f, .09f, .98f);
            GUI.DrawTexture(new Rect(0, 0, 440, 900), Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(28, 30, 390, 76), "FITNESS SIMULATOR\nRESOURCE LAB", title);
            GUI.color = new Color(.3f, .9f, .85f);
            GUI.Label(new Rect(28, 112, 390, 30), "3D PRESENTATION STUDIES", small);
            GUI.color = old;
            for (int i = 0; i < catalog.exercises.Length; i++)
            {
                GUI.backgroundColor = i == selected ? new Color(.18f, .65f, .61f) : new Color(.2f, .27f, .34f);
                if (GUI.Button(new Rect(24, 166 + i * 57, 390, 48), $"{i + 1:00}   {catalog.exercises[i].displayName}", button)) Select(i);
            }
            GUI.backgroundColor = Color.white;
            GUI.Label(new Rect(28, 650, 385, 84), "Original blockout models + loop clips.\nCandidate exercises; gameplay and scoring are not final.", small);
            if (GUI.Button(new Rect(24, 755, 390, 45), "Check offline storage", button)) CheckStorage();
            GUI.Label(new Rect(28, 813, 385, 60), storageStatus, small);

            var exercise = catalog.exercises[selected];
            GUI.Label(new Rect(480, 30, 1050, 44), exercise.displayName.ToUpperInvariant(), title);
            GUI.Label(new Rect(480, 81, 1000, 36), "SCENE / EQUIPMENT / CHARACTER / LOOP ANIMATION", small);
            GUI.color = new Color(.035f, .06f, .09f, .95f);
            GUI.DrawTexture(new Rect(480, 728, 1070, 143), Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(500, 742, 1020, 42), exercise.presentationNotes, body);
            if (GUI.Button(new Rect(500, 807, 135, 44), paused ? "Play" : "Pause", button)) paused = !paused;
            GUI.Label(new Rect(663, 811, 145, 30), $"Speed {speed:0.0}x", body);
            speed = GUI.HorizontalSlider(new Rect(800, 825, 185, 24), speed, .25f, 1.5f);
            if (GUI.Button(new Rect(1020, 807, 220, 44), "Rotate camera", button)) { cameraAngle += 45; PositionCamera(); }
            if (GUI.Button(new Rect(1270, 807, 250, 44), "Reset view", button)) { cameraAngle = 35; PositionCamera(); }
            if (animationPlayer) animationPlayer[exercise.demonstrationClip.name].speed = paused ? 0 : speed;
            GUI.matrix = Matrix4x4.identity;
        }

        [Serializable]
        private sealed class RuntimeEvidence
        {
            public string engineVersion;
            public string platform;
            public bool landscape;
            public bool localStorageRoundTrip;
            public int presentationsChecked;
            public string[] animatedExerciseIds;
            public bool allAnimationsMoved;
        }

        private IEnumerator SmokeRun(string[] args)
        {
            string directory = Path.Combine(Application.persistentDataPath, "Verification");
            int reportArg = Array.IndexOf(args, "-fitnessReportDir");
            if (reportArg >= 0 && reportArg + 1 < args.Length) directory = Path.GetFullPath(args[reportArg + 1]);
            Directory.CreateDirectory(directory);
            bool storage = CheckStorage();
            bool allMoved = true;
            var ids = new string[catalog.exercises.Length];
            yield return null;
            for (int i = 0; i < catalog.exercises.Length; i++)
            {
                Select(i);
                yield return null;
                Transform hand = stage.transform.Find("Motion/HandR");
                Vector3 before = hand.localPosition;
                yield return new WaitForSeconds(.55f);
                allMoved &= Vector3.Distance(before, hand.localPosition) > .02f;
                ids[i] = catalog.exercises[i].exerciseId;
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(directory, ids[i] + ".png"));
                yield return new WaitForSeconds(.25f);
            }
            var evidence = new RuntimeEvidence { engineVersion = Application.unityVersion, platform = Application.platform.ToString(),
                landscape = Screen.width > Screen.height, localStorageRoundTrip = storage,
                presentationsChecked = ids.Length, animatedExerciseIds = ids, allAnimationsMoved = allMoved };
            File.WriteAllText(Path.Combine(directory, "runtime-evidence.json"), JsonUtility.ToJson(evidence, true));
            Debug.Log("FITNESS_RUNTIME_SMOKE " + JsonUtility.ToJson(evidence));
            if (Application.platform != RuntimePlatform.Android) Application.Quit(storage && allMoved ? 0 : 1);
        }
    }
}
