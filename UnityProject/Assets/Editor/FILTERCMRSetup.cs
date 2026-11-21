// FILTER CMR — Editor setup tool.
//
//   FILTER CMR > Setup Main Scene       create/repair MainScene (idempotent)
//   FILTER CMR > Setup Visual Effects   add/repair the Milestone 3/4 mask + halftone systems
//                                       in the EXISTING MainScene (idempotent; keeps your objects)
//   FILTER CMR > Validate Scene         report missing objects/references
//   FILTER CMR > Open Main Scene
//
// Re-running Setup never duplicates objects: everything is found by name/type
// and only missing pieces are added. Existing settings values are kept.

using System.Collections.Generic;
using System.IO;
using FilterCMR;
using FilterCMR.CameraInput;
using FilterCMR.Core;
using FilterCMR.Gestures;
using FilterCMR.Mask;
using FilterCMR.Networking;
using FilterCMR.Rendering;
using FilterCMR.Tracking;
using FilterCMR.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FilterCMR.EditorTools
{
    [InitializeOnLoad]
    public static class FILTERCMRSetup
    {
        public const string Root = "Assets/FILTERCMR";
        public const string ScenePath = Root + "/Scenes/MainScene.unity";
        public const string SettingsPath = Root + "/Settings/FilterCMRSettings.asset";
        public const string ShaderPath = Root + "/Shaders/FILTERCMR_WebcamComposite.shader";
        public const string MaterialPath = Root + "/Materials/FILTERCMR_WebcamComposite.mat";
        const string FirstRunKey = "FilterCMR.SetupPromptShown";

        static readonly string[] Folders =
        {
            Root, Root + "/Scenes", Root + "/Scripts", Root + "/Materials", Root + "/Prefabs",
            Root + "/Textures", Root + "/Settings", Root + "/Shaders",
        };

        // Offer to build the scene the first time the project is opened.
        static FILTERCMRSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (File.Exists(ScenePath) || SessionState.GetBool(FirstRunKey, false)) return;
                SessionState.SetBool(FirstRunKey, true);
                if (EditorUtility.DisplayDialog("FILTER CMR",
                        "MainScene does not exist yet.\n\nCreate it now? (You can also run FILTER CMR > Setup Main Scene at any time.)",
                        "Create Main Scene", "Later"))
                    SetupMainScene();
            };
        }

        [MenuItem("FILTER CMR/Setup Main Scene", priority = 0)]
        public static void SetupMainScene() => RunSetup("Main scene");

        /// <summary>
        /// Milestone 3/4 migration. Opens the existing MainScene (or creates it), keeps every existing
        /// object and component, and adds only what is missing: Interactive Mask + Visual Effects
        /// systems, the composite material on the webcam RawImage, mask debug overlay, settings
        /// panel and presentation-exit button; then wires all references and saves.
        /// </summary>
        [MenuItem("FILTER CMR/Setup Visual Effects", priority = 1)]
        public static void SetupVisualEffects() => RunSetup("Visual effects");

        static void RunSetup(string what)
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("FILTER CMR", "Exit Play Mode before running the setup.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureFolders();
            ApplicationSettings settings = EnsureSettings();
            Scene scene = OpenOrCreateScene();
            var log = new List<string>();

            // ---- camera --------------------------------------------------------------------
            GameObject camGo = FindOrCreateRoot("Main Camera", log);
            camGo.tag = "MainCamera";
            var cam = GetOrAdd<Camera>(camGo, log);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.Black;
            cam.orthographic = true;
            GetOrAdd<AudioListener>(camGo, log);

            // ---- systems -------------------------------------------------------------------
            GameObject root = FindOrCreateRoot("FILTER CMR", log);
            var manager = GetOrAdd<FilterCMRManager>(root, log);
            Transform systems = FindOrCreateChild(root.transform, "Systems", log, rect: false);
            var receiver = GetOrAdd<UDPReceiver>(FindOrCreateChild(systems, "UDP Receiver", log, false).gameObject, log);
            var video = GetOrAdd<VideoStreamReceiver>(FindOrCreateChild(systems, "Video Stream Receiver", log, false).gameObject, log);
            var webcam = GetOrAdd<WebcamController>(FindOrCreateChild(systems, "Webcam", log, false).gameObject, log);
            var tracking = GetOrAdd<HandTrackingManager>(FindOrCreateChild(systems, "Hand Tracking", log, false).gameObject, log);
            var gestures = GetOrAdd<GestureManager>(FindOrCreateChild(systems, "Gestures", log, false).gameObject, log);

            // ---- event system (for future UI controls) -------------------------------------
            if (Object.FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                Undo.RegisterCreatedObjectUndo(es, "FILTER CMR setup");
                log.Add("+ EventSystem");
            }

            // ---- display canvas: webcam + landmarks ---------------------------------------
            Canvas displayCanvas = EnsureCanvas("Display Canvas", 0, log);
            Image bg = GetOrAdd<Image>(FindOrCreateChild(displayCanvas.transform, "Background", log).gameObject, log);
            bg.color = Palette.Black;
            bg.raycastTarget = false;
            UIFactory.Stretch(bg.rectTransform);
            bg.transform.SetAsFirstSibling();

            RectTransform area = (RectTransform)FindOrCreateChild(displayCanvas.transform, "Webcam Area", log);
            UIFactory.Stretch(area);
            RectTransform displayRt = (RectTransform)FindOrCreateChild(area, "Webcam Display", log);
            var raw = GetOrAdd<RawImage>(displayRt.gameObject, log);
            raw.raycastTarget = false;
            raw.color = Color.clear;
            var fitter = GetOrAdd<AspectRatioFitter>(displayRt.gameObject, log);
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 16f / 9f;
            displayRt.anchorMin = Vector2.zero;
            displayRt.anchorMax = Vector2.one;
            displayRt.pivot = new Vector2(0.5f, 0.5f);
            displayRt.anchoredPosition = Vector2.zero;
            var display = GetOrAdd<WebcamDisplay>(displayRt.gameObject, log);

            RectTransform overlay = (RectTransform)FindOrCreateChild(displayRt, "Hand Overlay", log);
            UIFactory.Stretch(overlay);
            var connections = GetOrAdd<HandConnectionVisualizer>(FindOrCreateChild(overlay, "Hand Connections", log).gameObject, log);
            UIFactory.Stretch(connections.rectTransform);
            connections.raycastTarget = false;
            var landmarks = GetOrAdd<HandLandmarkVisualizer>(FindOrCreateChild(overlay, "Hand Landmarks", log).gameObject, log);
            UIFactory.Stretch(landmarks.rectTransform);
            landmarks.raycastTarget = false;
            landmarks.transform.SetAsLastSibling(); // dots above lines

            // ---- UI canvas: status + debug (separate so it can be hidden independently) -----
            Canvas uiCanvas = EnsureCanvas("UI Canvas", 10, log);
            var status = GetOrAdd<TrackingStatusUI>(FindOrCreateChild(uiCanvas.transform, "Status UI", log).gameObject, log);
            var debug = GetOrAdd<DebugPanel>(FindOrCreateChild(uiCanvas.transform, "Debug Panel", log).gameObject, log);
            debug.transform.SetAsLastSibling();

            // ---- Milestone 3/4: interactive masks + visual effects -----------------------------
            var maskManager = GetOrAdd<InteractiveMaskManager>(FindOrCreateChild(systems, "Interactive Mask", log, false).gameObject, log);
            var effects = GetOrAdd<VisualEffectController>(FindOrCreateChild(systems, "Visual Effects", log, false).gameObject, log);
            var composite = GetOrAdd<WebcamCompositeController>(displayRt.gameObject, log);
            var maskDebug = GetOrAdd<MaskDebugVisualizer>(FindOrCreateChild(overlay, "Mask Debug", log).gameObject, log);
            UIFactory.Stretch(maskDebug.rectTransform);
            maskDebug.raycastTarget = false;
            maskDebug.transform.SetAsLastSibling();
            var settingsPanel = GetOrAdd<VisualSettingsPanel>(FindOrCreateChild(uiCanvas.transform, "Settings Panel", log).gameObject, log);
            Button presentationExit = EnsurePresentationExit(uiCanvas.transform, log);
            Shader compositeShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (compositeShader == null) compositeShader = Shader.Find(WebcamCompositeController.ShaderName);
            Material compositeMaterial = EnsureMaterial(compositeShader, log);

            Wire(maskManager, "tracking", tracking);
            Wire(maskManager, "gestures", gestures);
            Wire(maskManager, "display", display);
            Wire(maskManager, "effects", effects);
            Wire(composite, "compositeShader", compositeShader);
            Wire(composite, "compositeMaterial", compositeMaterial);
            Wire(composite, "masks", maskManager);
            Wire(composite, "effects", effects);
            Wire(composite, "display", display);
            Wire(maskDebug, "settings", settings);
            Wire(maskDebug, "tracking", tracking);
            Wire(maskDebug, "masks", maskManager);
            Wire(settingsPanel, "manager", manager);
            Wire(manager, "masks", maskManager);
            Wire(manager, "effects", effects);
            Wire(manager, "composite", composite);
            Wire(manager, "maskDebug", maskDebug);
            Wire(manager, "settingsPanel", settingsPanel);
            Wire(manager, "presentationExit", presentationExit);

            // ---- wire references -------------------------------------------------------------
            Wire(receiver, "settings", settings);
            Wire(video, "settings", settings);
            Wire(webcam, "settings", settings);
            Wire(tracking, "settings", settings);
            Wire(tracking, "receiver", receiver);
            Wire(gestures, "settings", settings);
            Wire(gestures, "tracking", tracking);
            Wire(display, "settings", settings);
            Wire(display, "webcam", webcam);
            Wire(display, "videoStream", video);
            Wire(display, "tracking", tracking);
            Wire(display, "overlay", overlay);
            Wire(connections, "settings", settings);
            Wire(connections, "tracking", tracking);
            Wire(landmarks, "settings", settings);
            Wire(landmarks, "tracking", tracking);
            Wire(landmarks, "gestures", gestures);
            Wire(status, "manager", manager);
            Wire(debug, "manager", manager);
            Wire(manager, "settings", settings);
            Wire(manager, "receiver", receiver);
            Wire(manager, "videoStream", video);
            Wire(manager, "webcam", webcam);
            Wire(manager, "tracking", tracking);
            Wire(manager, "gestures", gestures);
            Wire(manager, "display", display);
            Wire(manager, "landmarkVisualizer", landmarks);
            Wire(manager, "connectionVisualizer", connections);
            Wire(manager, "debugPanel", debug);
            Wire(manager, "statusUI", status);

            // Build the UI children now so the layout is visible in the Scene view.
            status.Build();
            debug.Build();
            settingsPanel.Build();

            // ---- project settings ------------------------------------------------------------
            PlayerSettings.runInBackground = true;
            if (string.IsNullOrEmpty(PlayerSettings.macOS.cameraUsageDescription))
                PlayerSettings.macOS.cameraUsageDescription = "FILTER CMR shows your camera image and tracks your hands.";
            if (PlayerSettings.productName == "UnityProject" || string.IsNullOrEmpty(PlayerSettings.productName))
                PlayerSettings.productName = "FILTER CMR";
            AddSceneToBuildSettings();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            int problems = Validate(verbose: false);
            string summary = log.Count == 0 ? "Scene was already complete; references refreshed." : "Created:\n  " + string.Join("\n  ", log);
            Debug.Log($"[FILTER CMR] {what} setup complete ({ScenePath}). {summary}\nValidation problems: {problems}");
            Selection.activeGameObject = root;
        }

        [MenuItem("FILTER CMR/Validate Scene", priority = 20)]
        public static void ValidateMenu()
        {
            int n = Validate(verbose: true);
            EditorUtility.DisplayDialog("FILTER CMR", n == 0 ? "Scene OK: all systems and references are present."
                : n + " problem(s) found. See the Console. Run FILTER CMR > Setup Main Scene to fix them.", "OK");
        }

        [MenuItem("FILTER CMR/Open Main Scene", priority = 21)]
        public static void OpenMainScene()
        {
            if (!File.Exists(ScenePath)) { SetupMainScene(); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        // =================================================================================
        static int Validate(bool verbose)
        {
            int problems = 0;
            void Problem(string msg) { problems++; Debug.LogWarning("[FILTER CMR] validation: " + msg); }

            if (AssetDatabase.LoadAssetAtPath<ApplicationSettings>(SettingsPath) == null) Problem("settings asset missing: " + SettingsPath);
            if (UIFactory.DefaultFont == null) Problem("built-in font LegacyRuntime.ttf not found");
            Shader sh = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (sh == null) Problem("composite shader missing: " + ShaderPath);
            else if (!sh.isSupported) Problem("composite shader does not compile / is not supported: check the shader's Inspector for errors");
            if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) == null) Problem("composite material missing: " + MaterialPath);

            var manager = Object.FindObjectOfType<FilterCMRManager>();
            if (manager == null) { Problem("no FilterCMRManager in the open scene"); return problems; }

            CheckRefs(new SerializedObject(manager), Problem);
            foreach (MonoBehaviour mb in new MonoBehaviour[]
                     {
                         Object.FindObjectOfType<UDPReceiver>(), Object.FindObjectOfType<VideoStreamReceiver>(),
                         Object.FindObjectOfType<WebcamController>(), Object.FindObjectOfType<HandTrackingManager>(),
                         Object.FindObjectOfType<GestureManager>(), Object.FindObjectOfType<WebcamDisplay>(),
                         Object.FindObjectOfType<HandLandmarkVisualizer>(), Object.FindObjectOfType<HandConnectionVisualizer>(),
                         Object.FindObjectOfType<DebugPanel>(true), Object.FindObjectOfType<TrackingStatusUI>(),
                         Object.FindObjectOfType<InteractiveMaskManager>(), Object.FindObjectOfType<VisualEffectController>(),
                         Object.FindObjectOfType<WebcamCompositeController>(), Object.FindObjectOfType<MaskDebugVisualizer>(),
                         Object.FindObjectOfType<VisualSettingsPanel>(true),
                     })
            {
                if (mb == null) { Problem("a FILTER CMR component is missing from the scene"); continue; }
                CheckRefs(new SerializedObject(mb), Problem);
            }
            if (Object.FindObjectsOfType<FilterCMRManager>().Length > 1) Problem("more than one FilterCMRManager");
            if (verbose && problems == 0) Debug.Log("[FILTER CMR] validation passed");
            return problems;
        }

        static readonly HashSet<string> OptionalRefs = new HashSet<string> { "m_Material", "m_Texture" };

        static void CheckRefs(SerializedObject so, System.Action<string> problem)
        {
            SerializedProperty p = so.GetIterator();
            bool enter = true;
            while (p.NextVisible(enter))
            {
                enter = false;
                if (p.propertyType != SerializedPropertyType.ObjectReference || p.name == "m_Script") continue;
                if (OptionalRefs.Contains(p.name)) continue;
                if (p.objectReferenceValue == null)
                    problem($"{so.targetObject.GetType().Name}.{p.name} on \"{((Component)so.targetObject).gameObject.name}\" is not assigned");
            }
        }

        static void EnsureFolders()
        {
            foreach (string f in Folders)
            {
                if (AssetDatabase.IsValidFolder(f)) continue;
                string parent = Path.GetDirectoryName(f).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(f));
            }
        }

        static ApplicationSettings EnsureSettings()
        {
            var s = AssetDatabase.LoadAssetAtPath<ApplicationSettings>(SettingsPath);
            if (s != null) return s;
            s = ScriptableObject.CreateInstance<ApplicationSettings>();
            AssetDatabase.CreateAsset(s, SettingsPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[FILTER CMR] created " + SettingsPath);
            return s;
        }

        static Scene OpenOrCreateScene()
        {
            Scene active = SceneManager.GetActiveScene();
            if (active.path == ScenePath) return active;
            if (File.Exists(ScenePath)) return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            return scene;
        }

        static Material EnsureMaterial(Shader shader, List<string> log)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (shader == null)
            {
                Debug.LogError("[FILTER CMR] composite shader not found at " + ShaderPath + ". Effects stay off; the webcam still works.");
                return mat;
            }
            if (mat == null)
            {
                mat = new Material(shader) { name = "FILTERCMR_WebcamComposite" };
                AssetDatabase.CreateAsset(mat, MaterialPath);
                log.Add("+ " + MaterialPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
                EditorUtility.SetDirty(mat);
            }
            return mat;
        }

        static Button EnsurePresentationExit(Transform uiCanvas, List<string> log)
        {
            bool existed = uiCanvas.Find("Presentation Exit") != null;
            Button b = UIFactory.CreateButton(uiCanvas, "Presentation Exit", "•", 22, out Text t);
            var rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 1);
            rt.anchoredPosition = new Vector2(-8, -8);
            rt.sizeDelta = new Vector2(34, 34);
            b.GetComponent<Image>().color = new Color(0.03f, 0.08f, 0.18f, 0.35f);
            t.color = new Color(0.21f, 0.87f, 0.95f, 0.6f);
            b.transform.SetAsLastSibling();
            b.gameObject.SetActive(false);   // FilterCMRManager shows it only in presentation mode
            if (!existed) log.Add("+ UI Canvas/Presentation Exit");
            return b;
        }

        static void AddSceneToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(x => x.path == ScenePath)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static Canvas EnsureCanvas(string name, int order, List<string> log)
        {
            GameObject go = FindOrCreateRoot(name, log, rect: true);
            var canvas = GetOrAdd<Canvas>(go, log);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            canvas.pixelPerfect = false;
            var scaler = GetOrAdd<CanvasScaler>(go, log);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            GetOrAdd<GraphicRaycaster>(go, log);
            return canvas;
        }

        static GameObject FindOrCreateRoot(string name, List<string> log, bool rect = false)
        {
            foreach (GameObject g in SceneManager.GetActiveScene().GetRootGameObjects())
                if (g.name == name) return g;
            var go = rect ? new GameObject(name, typeof(RectTransform)) : new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "FILTER CMR setup");
            log.Add("+ " + name);
            return go;
        }

        static Transform FindOrCreateChild(Transform parent, string name, List<string> log, bool rect = true)
        {
            Transform t = parent.Find(name);
            if (t != null) return t;
            var go = rect ? new GameObject(name, typeof(RectTransform)) : new GameObject(name);
            go.layer = parent.gameObject.layer;
            Undo.RegisterCreatedObjectUndo(go, "FILTER CMR setup");
            go.transform.SetParent(parent, false);
            log.Add("+ " + parent.name + "/" + name);
            return go.transform;
        }

        static T GetOrAdd<T>(GameObject go, List<string> log) where T : Component
        {
            T c = go.GetComponent<T>();
            if (c != null) return c;
            c = Undo.AddComponent<T>(go);
            log.Add("+ " + typeof(T).Name + " on " + go.name);
            return c;
        }

        /// <summary>Assigns a serialized private field by name; fails loudly if the field was renamed.</summary>
        static void Wire(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
            {
                Debug.LogError($"[FILTER CMR] setup: {target.GetType().Name} has no serialized field '{field}'");
                return;
            }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
