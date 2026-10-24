using System;
using System.IO;
using FitnessSimulator.Presentation;
using FitnessSimulator.Training;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FitnessSimulator.Editor
{
    public static class ProjectSetup
    {
        private const string Root = "Assets/FitnessSimulator";
        private const string Generated = Root + "/Generated";
        private const string Bootstrap = Root + "/Scenes/Bootstrap.unity";

        [MenuItem("Fitness Simulator/Setup project and regenerate prototype assets")]
        public static void Configure()
        {
            foreach (string directory in new[] { Generated + "/Materials", Generated + "/Prefabs", Generated + "/Animations", Generated + "/Exercises", Root + "/Resources", Root + "/Scenes/Exercises" })
                Directory.CreateDirectory(directory);
            AssetDatabase.Refresh();
            PlayerSettings.companyName = "FitnessSimulator";
            PlayerSettings.productName = "Fitness Simulator";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.fitnesssimulator.prototype");
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.forceInternetPermission = false;
            PlayerSettings.Android.forceSDCardPermission = false;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.colorSpace = ColorSpace.Linear;
            EditorSettings.serializationMode = SerializationMode.ForceText;

            PrototypeStageFactory.PrepareMaterials(Generated + "/Materials");
            var catalog = LoadOrCreate<ExerciseCatalog>(Root + "/Resources/ExerciseCatalog.asset");
            catalog.exercises = new ExercisePresentation[8];
            string[][] tags = { new[] { "chest", "triceps" }, new[] { "biceps" }, new[] { "back", "biceps" }, new[] { "triceps" }, new[] { "legs", "glutes" }, new[] { "glutes" }, new[] { "shoulders", "triceps" }, new[] { "shoulders" } };
            for (int i = 0; i < catalog.exercises.Length; i++)
            {
                string id = PrototypeStageFactory.Ids[i];
                var stage = PrototypeStageFactory.Stage(i);
                var baked = PrototypeStageFactory.Bake(stage, i);
                string clipPath = Generated + "/Animations/" + id + ".anim";
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (existing) { EditorUtility.CopySerialized(baked, existing); UnityEngine.Object.DestroyImmediate(baked); baked = existing; }
                else AssetDatabase.CreateAsset(baked, clipPath);
                var animation = stage.AddComponent<Animation>();
                animation.AddClip(baked, baked.name);
                animation.clip = baked;
                animation.playAutomatically = true;
                animation.wrapMode = WrapMode.Loop;
                var prefab = PrefabUtility.SaveAsPrefabAsset(stage, Generated + "/Prefabs/" + id + ".prefab");
                UnityEngine.Object.DestroyImmediate(stage);
                var entry = LoadOrCreate<ExercisePresentation>(Generated + "/Exercises/" + id + ".asset");
                entry.exerciseId = id;
                entry.displayName = PrototypeStageFactory.Names[i];
                entry.targetMuscleTags = tags[i];
                entry.stagePrefab = prefab;
                entry.demonstrationClip = baked;
                entry.demonstrationOnly = true;
                entry.presentationNotes = "Original motion blockout. Replaceable assets; no final minigame scoring or training instruction.";
                entry.referenceUrls = new[] { "https://quaternius.com/packs/universalbasecharacters", "https://vnb3d.com/product/gym-set/" };
                EditorUtility.SetDirty(entry);
                catalog.exercises[i] = entry;
            }
            EditorUtility.SetDirty(catalog);
            for (int i = 0; i < catalog.exercises.Length; i++)
                CreateScene(Root + "/Scenes/Exercises/" + PrototypeStageFactory.Ids[i] + ".unity", i);
            CreateScene(Bootstrap, 0);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Bootstrap, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Validate();
            Debug.Log("FITNESS_SETUP_OK: 8 prefabs, 8 animation clips, 8 exercise scenes, Bootstrap scene.");
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var value = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!value) { value = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(value, path); }
            return value;
        }

        private static void CreateScene(string path, int index)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camera.tag = "MainCamera";
            var cam = camera.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.045f, .07f, .105f);
            cam.orthographic = true;
            cam.orthographicSize = 2.3f;
            camera.transform.position = new Vector3(3, 3.2f, 4.5f);
            camera.transform.LookAt(new Vector3(0, 1.2f, 0));
            var light = new GameObject("Key Light", typeof(Light));
            var key = light.GetComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(1f, .91f, .8f);
            key.intensity = 1.8f;
            key.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(45, -30, 0);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.42f, .5f, .6f);
            var room = new GameObject("Gym Environment").transform;
            PrototypeStageFactory.Part(room, "Floor", PrimitiveType.Cube, new Vector3(0, -.11f, 0), new Vector3(7, .1f, 7), PrototypeStageFactory.Floor);
            PrototypeStageFactory.Part(room, "BackWall", PrimitiveType.Cube, new Vector3(0, 1.65f, -2.3f), new Vector3(7, 3.4f, .15f), PrototypeStageFactory.Floor);
            for (int x = -3; x <= 3; x++)
                PrototypeStageFactory.Part(room, "FloorSeam" + x, PrimitiveType.Cube, new Vector3(x, -.052f, 0), new Vector3(.012f, .006f, 7), PrototypeStageFactory.Rubber);
            PrototypeStageFactory.Part(room, "WallAccent", PrimitiveType.Cube, new Vector3(0, 2.5f, -2.21f), new Vector3(7, .07f, .02f), PrototypeStageFactory.Accent);
            new GameObject("Resource Lab", typeof(ResourceLab)).GetComponent<ResourceLab>().initialExercise = index;
            EditorSceneManager.SaveScene(scene, path);
        }

        [MenuItem("Fitness Simulator/Validate project")]
        public static void Validate()
        {
            var catalog = Resources.Load<ExerciseCatalog>("ExerciseCatalog");
            if (!catalog || catalog.exercises.Length != 8) throw new InvalidOperationException("Expected 8 candidate presentations.");
            foreach (var entry in catalog.exercises)
                if (!entry || !entry.stagePrefab || !entry.demonstrationClip) throw new InvalidOperationException("Incomplete presentation.");
            if (PlayerSettings.defaultInterfaceOrientation != UIOrientation.LandscapeLeft) throw new InvalidOperationException("Landscape required.");
            if (PlayerSettings.Android.forceInternetPermission) throw new InvalidOperationException("Offline project must not force Internet permission.");
            if (!File.Exists(Bootstrap)) throw new InvalidOperationException("Bootstrap scene missing.");
            Debug.Log("FITNESS_PROJECT_VALIDATION_OK");
        }

        public static void BuildWindows()
        {
            Validate();
            Directory.CreateDirectory("Builds/Windows");
            Build("Builds/Windows/FitnessSimulator.exe", BuildTarget.StandaloneWindows64);
        }

        public static void BuildAndroid()
        {
            Validate();
            Directory.CreateDirectory("Builds/Android");
            EditorUserBuildSettings.buildAppBundle = false;
            Build("Builds/Android/FitnessSimulator.apk", BuildTarget.Android);
        }

        private static void Build(string output, BuildTarget target)
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { Bootstrap }, locationPathName = output,
                target = target, options = BuildOptions.None });
            Directory.CreateDirectory("TestResults");
            File.WriteAllText("TestResults/build-" + target + ".json", "{\"target\":\"" + target + "\",\"result\":\"" + report.summary.result + "\",\"errors\":" + report.summary.totalErrors + ",\"bytes\":" + report.summary.totalSize + "}");
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Build failed: " + report.summary.result);
            Debug.Log("FITNESS_BUILD_OK " + output);
        }
    }
}
