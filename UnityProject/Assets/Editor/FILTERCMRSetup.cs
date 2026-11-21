// FILTER CMR — Editor setup tool.
//
//   FILTER CMR > Setup Main Scene     create/repair MainScene (idempotent)
//   FILTER CMR > Validate Scene       report missing objects/references
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
using FilterCMR.Networking;
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
        const string FirstRunKey = "FilterCMR.SetupPromptShown";

        static readonly string[] Folders =
        {
            Root, Root + "/Scenes", Root + "/Scripts", Root + "/Materials", Root + "/Prefabs",
            Root + "/Textures", Root + "/Settings",
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
        public static void SetupMainScene()
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
            Debug.Log($"[FILTER CMR] Setup complete ({ScenePath}). {summary}\nValidation problems: {problems}");
            Selection.activeGameObject = root;
        }

        [MenuItem("FILTER CMR/Validate Scene", priority = 1)]
        public static void ValidateMenu()
        {
            int n = Validate(verbose: true);
            EditorUtility.DisplayDialog("FILTER CMR", n == 0 ? "Scene OK: all systems and references are present."
                : n + " problem(s) found. See the Console. Run FILTER CMR > Setup Main Scene to fix them.", "OK");
        }

        [MenuItem("FILTER CMR/Open Main Scene", priority = 2)]
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
