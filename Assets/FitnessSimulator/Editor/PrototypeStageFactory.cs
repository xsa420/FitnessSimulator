using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FitnessSimulator.Editor
{
    // Original geometric presentation studies, not reference-quality exercise instruction.
    internal static class PrototypeStageFactory
    {
        internal static Material Skin, Shirt, Shorts, Steel, Rubber, Accent, Floor;
        internal static readonly string[] Ids = { "bench-press", "dumbbell-curl", "lat-pulldown", "triceps-pushdown", "back-squat", "hip-thrust", "shoulder-press", "lateral-raise" };
        internal static readonly string[] Names = { "Bench press", "Dumbbell curl", "Lat pulldown", "Triceps pushdown", "Back squat", "Hip thrust", "Shoulder press", "Lateral raise" };

        internal static void PrepareMaterials(string folder)
        {
            Skin = MaterialAsset(folder, "Skin", new Color(.86f, .59f, .39f));
            Shirt = MaterialAsset(folder, "TrainingShirt", new Color(.08f, .66f, .65f));
            Shorts = MaterialAsset(folder, "Shorts", new Color(.07f, .12f, .19f));
            Steel = MaterialAsset(folder, "EquipmentSteel", new Color(.35f, .42f, .49f), .55f);
            Rubber = MaterialAsset(folder, "Rubber", new Color(.04f, .06f, .09f));
            Accent = MaterialAsset(folder, "SafetyAmber", new Color(1f, .55f, .15f));
            Floor = MaterialAsset(folder, "Floor", new Color(.16f, .21f, .27f));
        }

        private static Material MaterialAsset(string folder, string name, Color color, float metallic = 0)
        {
            string path = folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", .3f);
            EditorUtility.SetDirty(material);
            return material;
        }

        internal static Transform Part(Transform parent, string name, PrimitiveType type, Vector3 p, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = p;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go.transform;
        }

        internal static GameObject Stage(int index)
        {
            var root = new GameObject(Ids[index]);
            var props = new GameObject("Equipment").transform;
            props.SetParent(root.transform, false);
            Part(props, "TrainingMat", PrimitiveType.Cube, new Vector3(0, -.035f, 0), new Vector3(2.8f, .06f, 2.8f), Rubber);
            Part(props, "ZoneMarker", PrimitiveType.Cube, new Vector3(0, .002f, 1.34f), new Vector3(2.8f, .012f, .045f), Accent);
            if (index == 0 || index == 5)
            {
                Part(props, "BenchCushion", PrimitiveType.Cube, new Vector3(0, .48f, index == 5 ? -.48f : -.15f), new Vector3(.55f, .16f, index == 5 ? .55f : 1.2f), Shorts);
                foreach (float z in new[] { -.6f, .25f })
                    Part(props, "BenchLeg" + z, PrimitiveType.Cube, new Vector3(0, .23f, z), new Vector3(.44f, .46f, .12f), Steel);
            }
            if (index == 0 || index == 4)
            {
                float height = index == 4 ? 2.3f : 1.4f;
                for (int side = -1; side <= 1; side += 2)
                {
                    Part(props, "RackPost" + side, PrimitiveType.Cube, new Vector3(side * .87f, height / 2, -.55f), new Vector3(.1f, height, .12f), Steel);
                    Part(props, "RackFoot" + side, PrimitiveType.Cube, new Vector3(side * .87f, .05f, -.25f), new Vector3(.3f, .1f, 1.3f), Steel);
                    Part(props, "RackHook" + side, PrimitiveType.Cube, new Vector3(side * .87f, index == 4 ? 1.45f : 1.1f, -.4f), new Vector3(.15f, .08f, .3f), Accent);
                }
                if (index == 4) Part(props, "RackTop", PrimitiveType.Cube, new Vector3(0, height, -.55f), new Vector3(1.85f, .1f, .1f), Steel);
            }
            if (index == 2 || index == 3)
            {
                for (int side = -1; side <= 1; side += 2)
                    Part(props, "CableTower" + side, PrimitiveType.Cube, new Vector3(side * .58f, 1.35f, -.62f), new Vector3(.09f, 2.7f, .12f), Steel);
                Part(props, "CableTop", PrimitiveType.Cube, new Vector3(0, 2.7f, -.3f), new Vector3(1.25f, .12f, .85f), Steel);
                Part(props, "WeightStack", PrimitiveType.Cube, new Vector3(0, .6f, -.62f), new Vector3(.7f, 1.05f, .3f), Shorts);
                for (int i = 0; i < 8; i++) Part(props, "StackLine" + i, PrimitiveType.Cube, new Vector3(0, .15f + i * .12f, -.455f), new Vector3(.62f, .015f, .01f), Steel);
                if (index == 2)
                {
                    Part(props, "Seat", PrimitiveType.Cube, new Vector3(0, .43f, .1f), new Vector3(.52f, .14f, .6f), Shorts);
                    Part(props, "SeatPost", PrimitiveType.Cube, new Vector3(0, .2f, .1f), new Vector3(.12f, .4f, .16f), Steel);
                }
            }
            var figure = new GameObject("Motion").transform;
            figure.SetParent(root.transform, false);
            foreach (string name in new[] { "Torso", "Pelvis", "UpperArmL", "ForearmL", "UpperArmR", "ForearmR", "ThighL", "ShinL", "ThighR", "ShinR" })
                Part(figure, name, PrimitiveType.Capsule, Vector3.zero, Vector3.one, name == "Torso" ? Shirt : name == "Pelvis" || name.StartsWith("Thigh") ? Shorts : Skin);
            Part(figure, "Head", PrimitiveType.Sphere, Vector3.zero, new Vector3(.26f, .31f, .27f), Skin);
            Part(figure, "Hair", PrimitiveType.Sphere, Vector3.zero, new Vector3(.27f, .17f, .28f), Shorts);
            for (int side = -1; side <= 1; side += 2)
            {
                string suffix = side < 0 ? "L" : "R";
                Part(figure, "Shoe" + suffix, PrimitiveType.Cube, Vector3.zero, new Vector3(.16f, .12f, .3f), Rubber);
                Part(figure, "Hand" + suffix, PrimitiveType.Sphere, Vector3.zero, Vector3.one * .12f, Skin);
                if (index == 1 || index == 6 || index == 7)
                {
                    var dumbbell = new GameObject("Dumbbell" + suffix).transform;
                    dumbbell.SetParent(figure, false);
                    Part(dumbbell, "Handle", PrimitiveType.Cylinder, Vector3.zero, new Vector3(.045f, .18f, .045f), Steel).localRotation = Quaternion.Euler(0, 0, 90);
                    for (int end = -1; end <= 1; end += 2)
                        Part(dumbbell, "Weight" + end, PrimitiveType.Cylinder, new Vector3(end * .18f, 0, 0), new Vector3(.2f, .07f, .2f), Rubber).localRotation = Quaternion.Euler(0, 0, 90);
                }
            }
            if (index == 0 || index == 4 || index == 5 || index == 2 || index == 3)
            {
                var bar = new GameObject("Bar").transform;
                bar.SetParent(figure, false);
                Part(bar, "Shaft", PrimitiveType.Cylinder, Vector3.zero, new Vector3(.035f, index == 3 ? .22f : .85f, .035f), Steel).localRotation = Quaternion.Euler(0, 0, 90);
                if (index == 0 || index == 4 || index == 5)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Part(bar, "Plate" + side, PrimitiveType.Cylinder, new Vector3(side * .7f, 0, 0), new Vector3(.39f, .055f, .39f), Rubber).localRotation = Quaternion.Euler(0, 0, 90);
                        Part(bar, "Hub" + side, PrimitiveType.Cylinder, new Vector3(side * .765f, 0, 0), new Vector3(.12f, .015f, .12f), Accent).localRotation = Quaternion.Euler(0, 0, 90);
                    }
                if (index == 2 || index == 3) Part(figure, "Cable", PrimitiveType.Cylinder, Vector3.zero, Vector3.one, Rubber);
            }
            ApplyPose(root.transform, index, 0);
            return root;
        }

        private static void Bone(Transform motion, string name, Vector3 a, Vector3 b, float width, float depth = -1)
        {
            var t = motion.Find(name);
            t.localPosition = (a + b) * .5f;
            t.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
            t.localScale = new Vector3(width, Vector3.Distance(a, b) * .5f, depth < 0 ? width : depth);
        }

        internal static void ApplyPose(Transform root, int index, float phase)
        {
            float t = (1 - Mathf.Cos(phase * Mathf.PI * 2)) * .5f;
            var motion = root.Find("Motion");
            Vector3 hip = new Vector3(0, 1, 0), shoulder = new Vector3(0, 1.55f, 0), head = new Vector3(0, 1.82f, 0);
            Vector3 knee = new Vector3(.18f, .53f, .04f), foot = new Vector3(.18f, .08f, .13f);
            Vector3 elbow = new Vector3(.3f, 1.16f, .04f), hand = new Vector3(.3f, .87f, .06f);
            if (index == 0)
            {
                hip = new Vector3(0, .68f, .28f); shoulder = new Vector3(0, .68f, -.32f); head = new Vector3(0, .7f, -.65f);
                knee = new Vector3(.23f, .43f, .68f); foot = new Vector3(.24f, .08f, .83f);
                elbow = new Vector3(.48f - .14f * t, .74f + .42f * t, -.27f);
                hand = new Vector3(.36f, 1f + .48f * t, -.27f);
            }
            else if (index == 1) hand = new Vector3(.3f, .88f + .55f * t, .08f + .29f * Mathf.Sin(t * Mathf.PI));
            else if (index == 2)
            {
                hip.y = .61f; shoulder.y = 1.16f; head.y = 1.43f;
                knee = new Vector3(.2f, .47f, .43f); foot = new Vector3(.2f, .08f, .48f);
                elbow = new Vector3(.42f, 1.56f - .55f * t, .03f);
                hand = new Vector3(.49f, 1.91f - .66f * t, .12f);
            }
            else if (index == 3)
            {
                elbow = new Vector3(.27f, 1.2f, .12f);
                hand = new Vector3(.2f, 1.35f - .5f * t, .4f - .2f * t);
            }
            else if (index == 4)
            {
                hip += new Vector3(0, -.43f * t, -.22f * t);
                shoulder += new Vector3(0, -.43f * t, .1f * t);
                head += new Vector3(0, -.43f * t, .1f * t);
                knee += new Vector3(.07f, -.13f * t, .28f * t);
                elbow = shoulder + new Vector3(.42f, -.27f, -.12f);
                hand = shoulder + new Vector3(.52f, 0, -.11f);
            }
            else if (index == 5)
            {
                hip = new Vector3(0, .35f + .3f * t, .3f);
                shoulder = new Vector3(0, .67f, -.35f); head = new Vector3(0, .74f, -.65f);
                knee = new Vector3(.22f, .5f, .75f); foot = new Vector3(.22f, .08f, .9f);
                elbow = Vector3.Lerp(shoulder, hip, .5f) + new Vector3(.38f, .08f, 0);
                hand = hip + new Vector3(.4f, .12f, 0);
            }
            else if (index == 6)
            {
                elbow = new Vector3(.59f - .28f * t, 1.35f + .52f * t, .01f);
                hand = new Vector3(.51f - .24f * t, 1.66f + .55f * t, .01f);
            }
            else if (index == 7)
            {
                elbow = new Vector3(.32f + .26f * t, 1.17f + .34f * t, .02f);
                hand = new Vector3(.4f + .49f * t, .9f + .6f * t, .06f);
            }
            Bone(motion, "Torso", hip + (shoulder - hip) * .2f, shoulder, .43f, .26f);
            Bone(motion, "Pelvis", hip + Vector3.left * .16f, hip + Vector3.right * .16f, .29f);
            motion.Find("Head").localPosition = head;
            motion.Find("Hair").localPosition = head + Vector3.up * .1f;
            for (int side = -1; side <= 1; side += 2)
            {
                string suffix = side < 0 ? "L" : "R";
                Vector3 e = elbow, w = hand, k = knee, f = foot;
                e.x *= side; w.x *= side; k.x *= side; f.x *= side;
                Bone(motion, "UpperArm" + suffix, shoulder + Vector3.right * (side * .22f), e, .145f);
                Bone(motion, "Forearm" + suffix, e, w, .12f);
                Bone(motion, "Thigh" + suffix, hip + Vector3.right * (side * .15f), k, .21f);
                Bone(motion, "Shin" + suffix, k, f, .15f);
                motion.Find("Hand" + suffix).localPosition = w;
                motion.Find("Shoe" + suffix).localPosition = f;
                if (motion.Find("Dumbbell" + suffix)) motion.Find("Dumbbell" + suffix).localPosition = w;
            }
            var bar = motion.Find("Bar");
            if (bar)
            {
                Vector3 center = hand; center.x = 0;
                bar.localPosition = center;
                var cable = motion.Find("Cable");
                if (cable) Bone(motion, "Cable", center, new Vector3(0, 2.63f, -.02f), .012f);
            }
        }

        internal static AnimationClip Bake(GameObject root, int index)
        {
            var clip = new AnimationClip { name = Ids[index] + "_Prototype", frameRate = 30, legacy = true, wrapMode = WrapMode.Loop };
            var transforms = root.transform.Find("Motion").GetComponentsInChildren<Transform>();
            var curves = new Dictionary<string, AnimationCurve[]>();
            foreach (var t in transforms)
            {
                string path = AnimationUtility.CalculateTransformPath(t, root.transform);
                var c = new AnimationCurve[10];
                for (int j = 0; j < c.Length; j++) c[j] = new AnimationCurve();
                curves[path] = c;
            }
            const int frames = 60;
            for (int frame = 0; frame <= frames; frame++)
            {
                ApplyPose(root.transform, index, frame / (float)frames);
                foreach (var t in transforms)
                {
                    var c = curves[AnimationUtility.CalculateTransformPath(t, root.transform)];
                    var p = t.localPosition; var q = t.localRotation; var s = t.localScale;
                    float[] values = { p.x, p.y, p.z, q.x, q.y, q.z, q.w, s.x, s.y, s.z };
                    for (int j = 0; j < values.Length; j++) c[j].AddKey(frame / 30f, values[j]);
                }
            }
            string[] properties = { "localPosition.x", "localPosition.y", "localPosition.z", "localRotation.x", "localRotation.y", "localRotation.z", "localRotation.w", "localScale.x", "localScale.y", "localScale.z" };
            foreach (var pair in curves)
                for (int j = 0; j < properties.Length; j++) clip.SetCurve(pair.Key, typeof(Transform), properties[j], pair.Value[j]);
            clip.EnsureQuaternionContinuity();
            ApplyPose(root.transform, index, 0);
            return clip;
        }
    }
}
