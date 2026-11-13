using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ConveyorControlBoxVerification
{
    private const string Key = "ConveyorStudio.ControlBoxProbe";
    private static Scene scene;
    private static ConveyorPlacementController app;
    private static ConveyorControlBox box;
    private static ConveyorLineController line;
    private static PlacedConveyor a, b, c;
    private static PlacedConveyor observer;
    private static Vector3 groupStartA, groupStartB, groupStartC;
    private static readonly List<string> checks = new List<string>();
    private static int stage;
    private static double next;
    [Serializable] private class Report { public bool success; public string error; public string[] checks; }
    static ConveyorControlBoxVerification()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingPlayMode) Cleanup();
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            { SessionState.SetBool(Key, false); Begin(); }
        };
    }
    [MenuItem("Tools/Conveyor Studio/Verify Integrated Control Box %#&F6")]
    public static void Verify()
    {
        if (EditorApplication.isPlaying) Begin();
        else { SessionState.SetBool(Key, true); Debug.Log("Control-box integration verification armed for the next Play session."); }
    }
    private static void Begin()
    {
        Cleanup(); checks.Clear(); stage = 0;
        try
        {
            Check(AssetDatabase.LoadAssetAtPath<GameObject>(ConveyorControlBoxSetup.PrefabPath) != null, "The imported control box exists as a separate Unity prefab");
            scene = SceneManager.CreateScene("Control box verification", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ConveyorStudioBuilder.AppPath));
            SceneManager.MoveGameObjectToScene(instance, scene); instance.transform.position = new Vector3(1200, 0, 0);
            app = instance.GetComponent<ConveyorPlacementController>(); app.enabled = false;
            instance.GetComponent<ConveyorStudioHud>().enabled = false;
            foreach (var camera in instance.GetComponentsInChildren<Camera>()) camera.enabled = false;
            foreach (var listener in instance.GetComponentsInChildren<AudioListener>()) listener.enabled = false;
            foreach (var light in instance.GetComponentsInChildren<Light>()) light.enabled = false;
            app.orbitCamera.enabled = false; app.sceneCamera.pixelRect = new Rect(0, 0, 1200, 700); app.sceneCamera.aspect = 1200f / 700f;
            app.WorldScreenRect = new Rect(0, 0, 1200, 700);
            ConveyorControlBox.Ensure(app); box = app.controlBox; line = app.GetComponent<ConveyorLineController>();
            box.renderingOrigin = new Vector3(1200, 1000, 1000);
            next = EditorApplication.timeSinceStartup + 0.25;
            EditorApplication.update += Tick;
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }
    private static void Check(bool pass, string text) { if (!pass) throw new Exception(text); checks.Add(text); }
    private static PlacedConveyor Add(ConveyorKind kind, Vector3 position, PlacedConveyor after = null)
    {
        var machine = app.BeginAdd(kind);
        if (after != null) position = after.Section.Exit.position - machine.Section.Entry.localPosition;
        app.MovePreview(new Pose(position, Quaternion.identity), after != null, true);
        Check(app.PreviewIsValid && app.TryCommit(), kind + " can be added while the live line controller is attached");
        return machine;
    }
    private static void Settle()
    { for (int i = 0; i < 90; i++) { box.AdvancePresentation(1f / 60f); app.orbitCamera.AdvanceFocus(1f / 60f); } Physics.SyncTransforms(); }
    private static Vector2 ScreenPoint(Vector3 world)
    {
        Vector3 viewport = box.PanelCamera.WorldToViewportPoint(world);
        Rect rect = box.Surface.rect;
        return RectTransformUtility.WorldToScreenPoint(null, box.Surface.TransformPoint(
            new Vector3(rect.xMin + viewport.x * rect.width, rect.yMin + viewport.y * rect.height, 0f)));
    }
    private static void CheckFittings()
    {
        bool endsOffscreen = true, shaftsVisible = true; int outlets = 0, shafts = 0;
        foreach (var part in box.SpeedKnob.parent.GetComponentsInChildren<Transform>())
        {
            bool outlet = part.name.StartsWith("GlandOutlet"), shaft = part.name.StartsWith("GlandBody");
            if (!outlet && !shaft) continue;
            var filter = part.GetComponent<MeshFilter>(); if (filter == null) continue;
            if (outlet) outlets++; else shafts++;
            float left = float.PositiveInfinity;
            foreach (var vertex in filter.sharedMesh.vertices)
            {
                float screenX = ScreenPoint(filter.transform.TransformPoint(vertex)).x;
                left = Mathf.Min(left, screenX);
                if (outlet) endsOffscreen &= screenX >= Screen.width;
            }
            if (shaft) shaftsVisible &= left < Screen.width - 2f;
        }
        Check(outlets == 2 && endsOffscreen, "Both side-fitting terminal faces remain completely beyond the screen edge");
        Check(shafts == 2 && shaftsVisible, "Both projecting side fittings retain visible shafts before their cropped ends");
    }
    private static void Click(ConveyorPanelControl control)
    {
        Vector2 point = box.ControlScreenPoint(control);
        Check(box.ControlAt(point) == control, "The actual " + control + " control has a correctly aligned 3D hit area");
        box.Press(point, 123); box.Release(point, 123); Settle();
    }
    private static bool Stopped(PlacedConveyor machine) => !machine.Section.BeltSurface.IsRunning && machine.Section.BeltSurface.EffectiveSpeed == 0;
    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        try
        {
            if (stage == 0)
            {
                if (!box.Ready) { next = EditorApplication.timeSinceStartup + 0.25; return; }
                box.ControlCanvas.GetComponent<UnityEngine.UI.GraphicRaycaster>().enabled = false;
                foreach (var graphic in box.ControlCanvas.GetComponentsInChildren<UnityEngine.UI.Graphic>()) graphic.canvasRenderer.SetAlpha(0f);
                foreach (var light in box.PanelCamera.transform.parent.GetComponentsInChildren<Light>()) light.enabled = false;
                Check(box.Hidden && !line.Running && !line.Emergency, "The line starts safely stopped, with the real housing peeking from the edge");
                Check(line.MachineCount == 0 && box.DisplayText == "NO\nMACHINES", "An empty workspace reads NO MACHINES on the dotted display");
                box.SetHidden(false); Settle(); Click(ConveyorPanelControl.Start);
                Check(!line.Running && !line.Emergency && box.DisplayText == "ADD MACHINES\nTO START", "Start rejects an empty line and explains how to begin");
                Settle(); Check(box.DisplayText == "NO\nMACHINES", "The empty-line Start prompt restores the normal display automatically");
                Check(!line.SetSynchronised(true) && !line.Synchronised, "Sync cannot activate in an empty workspace through the controller API");
                a = Add(ConveyorKind.Generic, new Vector3(1200, 0, 0));
                app.Select(a); Settle();
                Transform syncLever = null;
                foreach (var part in box.SpeedKnob.parent.GetComponentsInChildren<Transform>()) if (part.name == "LinkLever_Pivot") syncLever = part;
                Quaternion offPose = syncLever.localRotation;
                Vector2 syncPoint = box.ControlScreenPoint(ConveyorPanelControl.Sync);
                box.Press(syncPoint, 124); box.Release(syncPoint, 124);
                for (int i = 0; i < 5; i++) box.AdvancePresentation(1f / 60f);
                Check(!line.Synchronised && Quaternion.Angle(offPose, syncLever.localRotation) > 15f && box.DisplayText == "NEED TWO\nTO SYNC",
                    "With one machine the physical sync switch flips briefly, rejects the request and explains why");
                Settle();
                Check(!line.Synchronised && Quaternion.Angle(offPose, syncLever.localRotation) < 0.1f,
                    "The rejected sync lever springs fully back to OFF without synchronising a machine");
                Settle(); Check(box.DisplayText == "GENERIC\nUNIT A", "The rejected sync prompt restores the selected machine identity");
                box.SetHidden(true); box.Leave(); Settle();
                b = Add(ConveyorKind.Short, Vector3.zero, a); c = Add(ConveyorKind.Incline, Vector3.zero, b);
                Check(Stopped(a) && Stopped(b) && Stopped(c), "All newly placed belts wait for the green Start button");
                app.Select(a); app.RegisterTap(a, new Vector2(400, 300), 1f);
                Check(box.Hidden, "A single tap selects a belt without opening its panel");
                app.RegisterTap(a, new Vector2(403, 302), 1.2f);
                Check(!box.Hidden && app.orbitCamera.IsFocusing, "Double-tapping the same belt opens the box and begins smooth camera focus");
                Settle();
                Check(box.Surface.rect.height <= 432.1f, "The control box uses the smaller screen-edge presentation");
                app.orbitCamera.ScrollZoom(-3f, new Vector2(600, 350));
                for (int i = 0; i < 90; i++) app.orbitCamera.AdvanceZoom(1f / 60f);
                float retainedZoom = app.orbitCamera.ViewingDistance;
                Vector3 openFocus = app.sceneCamera.transform.position;
                Quaternion retainedAngle = app.sceneCamera.transform.rotation;
                box.SetHidden(true);
                Check(app.orbitCamera.IsFocusing && Vector3.Distance(openFocus, app.sceneCamera.transform.position) < 0.00001f,
                    "Hiding the box starts a smooth lateral camera adjustment without an instantaneous jump");
                Settle(); Vector3 fullFocus = app.sceneCamera.transform.position;
                Check(Mathf.Abs(app.orbitCamera.ViewingDistance - retainedZoom) < 0.0001f && Vector3.Distance(fullFocus, openFocus) > 0.05f
                    && Mathf.Abs(Vector3.Dot(fullFocus - openFocus, app.sceneCamera.transform.forward)) < 0.0002f
                    && Quaternion.Angle(retainedAngle, app.sceneCamera.transform.rotation) < 0.01f,
                    "Hiding after zooming out shifts sideways into the full viewport while preserving zoom and angle");
                box.SetHidden(true);
                Check(!app.orbitCamera.IsFocusing, "Repeated requests to hide an already hidden box do not restart the camera");
                box.SetHidden(false);
                Check(app.orbitCamera.IsFocusing && Vector3.Distance(fullFocus, app.sceneCamera.transform.position) < 0.00001f,
                    "Reopening the housing smoothly makes room for it again");
                Settle(); Vector3 reopenedFocus = app.sceneCamera.transform.position;
                Check(Vector3.Distance(reopenedFocus, openFocus) < 0.0003f && Mathf.Abs(app.orbitCamera.ViewingDistance - retainedZoom) < 0.0001f,
                    "Reopening reserves panel space again without undoing the user's zoom");
                CheckFittings();
                Quaternion restingView = box.PanelCamera.transform.rotation;
                var surfaceCorners = new Vector3[4]; box.Surface.GetWorldCorners(surfaceCorners);
                Vector2 low = RectTransformUtility.WorldToScreenPoint(null, surfaceCorners[0]);
                Vector2 high = RectTransformUtility.WorldToScreenPoint(null, surfaceCorners[2]); high.x = Mathf.Min(high.x, Screen.width);
                foreach (var corner in new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up })
                {
                    // Dock clipping shifts the surface as the viewpoint changes;
                    // choose each sample from its current visible bounds.
                    box.Surface.GetWorldCorners(surfaceCorners);
                    low = RectTransformUtility.WorldToScreenPoint(null, surfaceCorners[0]);
                    high = RectTransformUtility.WorldToScreenPoint(null, surfaceCorners[2]); high.x = Mathf.Min(high.x, Screen.width);
                    box.Hover(new Vector2(Mathf.Lerp(low.x + 8, high.x - 8, corner.x), Mathf.Lerp(low.y + 8, high.y - 8, corner.y))); Settle();
                    float hoverAngle = Quaternion.Angle(restingView, box.PanelCamera.transform.rotation);
                    Check(hoverAngle > 6f && hoverAngle < 24f, "Cursor movement gives the box an aggressive but bounded three-dimensional counter-tilt (" + hoverAngle.ToString("F1") + " degrees)");
                    CheckFittings();
                    foreach (var control in new[] { ConveyorPanelControl.Start, ConveyorPanelControl.Stop, ConveyorPanelControl.Sync, ConveyorPanelControl.Speed })
                        Check(box.ControlAt(box.ControlScreenPoint(control)) == control, "Tilt preserves the projected " + control + " hit area");
                }
                Vector2 heldDial = box.ControlScreenPoint(ConveyorPanelControl.Speed);
                box.Press(heldDial, 125); Quaternion heldView = box.PanelCamera.transform.rotation;
                box.Hover(new Vector2(high.x - 8, low.y + 8)); Settle();
                Check(Quaternion.Angle(heldView, box.PanelCamera.transform.rotation) < 0.01f, "Hover tilt stays frozen while a physical control is held");
                box.Release(heldDial, 125); box.Leave(); Settle();
                Check(Quaternion.Angle(restingView, box.PanelCamera.transform.rotation) < 0.01f, "Leaving the housing smoothly restores its resting view");
                Check(!app.orbitCamera.IsFocusing && box.DisplayText == "GENERIC\nUNIT A", "Focus settles and the live dotted display identifies Generic A");
                Vector3 cameraPosition = box.PanelCamera.transform.position;
                app.orbitCamera.Reframe(app.Machines, false);
                Check(Vector3.Distance(cameraPosition, box.PanelCamera.transform.position) < 0.00001f, "World camera movement leaves the 3D control box fixed to the screen");
                bool clearOfPanel = true;
                string obstructed = "";
                float available = 1f - Mathf.Min(box.OpenWidthPixels, app.WorldScreenRect.width * 0.48f) / app.WorldScreenRect.width;
                foreach (var machine in app.Machines) foreach (var filter in machine.GetComponentsInChildren<MeshFilter>())
                {
                    if (filter.sharedMesh == null) continue;
                    var vertices = filter.sharedMesh.vertices; int stride = Mathf.Max(1, vertices.Length / 96);
                    for (int i = 0; i < vertices.Length; i += stride)
                    {
                        Vector3 point = app.sceneCamera.WorldToViewportPoint(filter.transform.TransformPoint(vertices[i]));
                        bool clear = point.x > 0.02f && point.x < available - 0.02f && point.y > 0.02f && point.y < 0.98f;
                        if (!clear && obstructed.Length == 0) obstructed = machine.DisplayName + "/" + filter.name + " at " + point.ToString("F3");
                        clearOfPanel &= clear;
                    }
                }
                Check(clearOfPanel, "Reframe keeps the whole line visible beside the open control box (available " + available.ToString("F3")
                    + ", aspect " + app.sceneCamera.aspect.ToString("F3") + "; " + obstructed + ")");
                Vector2 panelPoint = box.ControlScreenPoint(ConveyorPanelControl.Stop);
                Rect navigationRect = app.WorldScreenRect;
                app.WorldScreenRect = new Rect(0, 0, Screen.width, Screen.height);
                Quaternion navigationStart = app.sceneCamera.transform.rotation;
                app.orbitCamera.NavigatePointer(new Vector2(400, 300), Vector2.zero, true, false);
                app.orbitCamera.NavigatePointer(panelPoint, new Vector2(10, 0), true, false);
                Check(app.orbitCamera.IsNavigating && Quaternion.Angle(navigationStart, app.sceneCamera.transform.rotation) > 1f,
                    "A held right-drag continues rotating the world when its pointer crosses the real control box");
                box.Press(panelPoint, 126); box.Release(panelPoint, 126);
                Check(!line.Emergency && !box.CapturingPointer, "Camera navigation cannot accidentally press a panel control");
                app.orbitCamera.NavigatePointer(new Vector2(-10, 300), new Vector2(-10, 0), true, false);
                Check(Quaternion.Angle(navigationStart, app.sceneCamera.transform.rotation) < 0.01f,
                    "The captured navigation gesture continues beyond viewport boundaries");
                app.orbitCamera.NavigatePointer(panelPoint, Vector2.zero, false, false);
                Check(!app.orbitCamera.IsNavigating && box.BlocksPointer(panelPoint), "Release ends navigation and restores normal panel input blocking");
                app.orbitCamera.NavigatePointer(panelPoint, new Vector2(10, 0), true, false);
                Check(app.orbitCamera.IsNavigating, "A right-drag may also begin directly over the control box");
                app.orbitCamera.NavigatePointer(panelPoint, new Vector2(-10, 0), true, false);
                app.orbitCamera.NavigatePointer(panelPoint, Vector2.zero, false, false);
                app.WorldScreenRect = navigationRect;
                Click(ConveyorPanelControl.Start);
                Check(line.Running && !line.Emergency && a.Section.BeltSurface.IsRunning && b.Section.BeltSurface.IsRunning && c.Section.BeltSurface.IsRunning,
                    "Pressing the raised green cap starts every placed machine");
                app.Select(a); line.SetLevel(4); Settle();
                Vector2 dial = box.ControlScreenPoint(ConveyorPanelControl.Speed);
                Vector2 begin = dial + Vector2.up * 12f;
                Check(box.ControlAt(begin) == ConveyorPanelControl.Speed, "The raised dial can be grabbed directly");
                Quaternion dialRest = box.SpeedKnob.localRotation;
                box.Press(begin, 77);
                float preload = 27f * Mathf.Deg2Rad;
                box.Drag(dial + new Vector2(Mathf.Sin(preload), Mathf.Cos(preload)) * 12f, 77);
                for (int i = 0; i < 6; i++) box.AdvancePresentation(1f / 60f);
                Check(a.SpeedLevel == 4 && Quaternion.Angle(dialRest, box.SpeedKnob.localRotation) > 1f,
                    "A moderate dial pull builds visible spring resistance without accidentally changing its speed detent");
                float angle = 70f * Mathf.Deg2Rad;
                box.Drag(dial + new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * 12f, 77);
                box.Release(dial, 77); Settle();
                Check(a.SpeedLevel > 4 && a.SpeedLevel <= 7 && b.SpeedLevel == 4, "Dragging the physical dial crosses detents and adjusts only the selected belt");
                line.SetLevel(7); Click(ConveyorPanelControl.Sync);
                Check(line.Synchronised && a.SpeedLevel == 7 && b.SpeedLevel == 7 && c.SpeedLevel == 7,
                    "The physical sync switch matches all existing machine settings");
                Click(ConveyorPanelControl.Stop);
                Check(line.Emergency && !line.Running && Stopped(a) && Stopped(b) && Stopped(c), "Emergency stop latches and stops the whole line immediately");
                line.SetLevel(9); Settle();
                Check(Stopped(a) && Stopped(b) && Stopped(c) && a.SpeedLevel == 9 && b.SpeedLevel == 9,
                    "The dial remains adjustable while stopped without moving any belt");
                var added = Add(ConveyorKind.Short, new Vector3(1205, 0, 0));
                Check(added.SpeedLevel == 9 && Stopped(added), "A newly added machine inherits the synchronised setting and stopped state");
                app.Select(c); Settle(); Check(box.DisplayText == "INCLINE\nUNIT C", "The live display changes to the selected type and stable unit ID");
                Click(ConveyorPanelControl.Start);
                Check(!line.Emergency && line.Running && Mathf.Abs(c.Section.BeltSurface.EffectiveSpeed - 1.8f) < 0.0001f,
                    "Start releases the emergency latch and resumes at the setting chosen while stopped");
                app.Select(a); app.BeginMove(a); line.EmergencyStop(); app.CancelPreview();
                stage = 1; next = EditorApplication.timeSinceStartup + 0.65;
            }
            else if (stage == 1)
            {
                Check(!a.IsEditing && Stopped(a) && Stopped(b) && Stopped(c), "A dragged or returning conveyor cannot restart itself after emergency stop");
                Click(ConveyorPanelControl.Start); Click(ConveyorPanelControl.Sync);
                app.Select(a); line.SetLevel(0);
                Check(!line.Synchronised && a.Section.BeltSurface.EffectiveSpeed == 0f && c.SpeedLevel == 9,
                    "Unsynchronised OFF stops one belt while other settings remain intact");
                box.enabled = false; box.enabled = true;
                box.ControlCanvas.transform.Find("Interactive 3D controls/Tuck control box").GetComponent<UnityEngine.UI.Button>().onClick.Invoke(); Settle();
                Check(box.Hidden, "The tuck button retains exactly one working action after re-enabling or reloading the panel");
                Vector2[] corners = new Vector2[2]; var worldCorners = new Vector3[4]; box.Surface.GetWorldCorners(worldCorners);
                for (int i = 0; i < 2; i++) corners[i] = RectTransformUtility.WorldToScreenPoint(null, worldCorners[i == 0 ? 0 : 2]);
                Check(corners[0].x < Screen.width && corners[1].x > Screen.width, "Hiding tucks the real model away while retaining a clickable edge");
                Vector3 peekCamera = app.sceneCamera.transform.position;
                box.Hover(new Vector2(Screen.width - 10f, (corners[0].y + corners[1].y) * 0.5f)); Settle();
                box.Surface.GetWorldCorners(worldCorners);
                Check(box.Hidden && RectTransformUtility.WorldToScreenPoint(null, worldCorners[0]).x < corners[0].x - 5f
                    && Vector3.Distance(peekCamera, app.sceneCamera.transform.position) < 0.0001f,
                    "Hovering a tucked panel reveals a little more housing without opening it or moving the world camera");
                box.Leave(); Settle();
                bool wasRunning = line.Running; int level = line.Level;
                box.Press(new Vector2(Screen.width - 10f, (corners[0].y + corners[1].y) * 0.5f), 101); Settle();
                Check(!box.Hidden && line.Running == wasRunning && line.Level == level && !line.Emergency,
                    "Clicking the exposed housing reopens it without activating a machine control");
                app.Select(b); app.RemoveSelected(); Settle();
                Check(a.UnitId == "A" && c.UnitId == "C" && line.Selected == null && box.DisplayText == "SELECT\nA BELT",
                    "Deleting the selected belt clears its controls while preserving surviving unit names");
                Check(box.BlocksPointer(box.ControlScreenPoint(ConveyorPanelControl.Stop)), "Panel input is blocked from leaking through to world placement or zoom");
                while (app.Machines.Count < 8) Add(ConveyorKind.Short, new Vector3(1210 + app.Machines.Count * 4, 0, 10));
                Check(box.DisplayText == "EIGHT BELTS\nVERY SUBTLE", "The eighth machine receives a brief sarcastic display message");
                Settle(); Settle(); Settle();
                Check(box.DisplayText.StartsWith("SHORT\nUNIT "), "The eight-machine message returns to the selected unit automatically");
                line.SetLevel(4);
                Check(box.DisplayText.StartsWith("SHORT\nUNIT "), "Ordinary adjustments at eight machines do not repeat the sarcastic prompt");
                line.SetSynchronised(true);
                while (app.Machines.Count > 1) { app.Select(app.Machines[app.Machines.Count - 1]); app.RemoveSelected(); }
                Check(line.MachineCount == 1 && !line.Synchronised, "Removing machines down to one automatically releases sync");
                app.Select(app.Machines[0]); app.RemoveSelected();
                Check(line.MachineCount == 0 && !line.Running && box.DisplayText == "NO\nMACHINES", "Removing the final machine stops the line and restores NO MACHINES");
                line.EmergencyStop();
                Check(!line.StartLine() && line.Emergency && !line.Running, "An empty Start request cannot release the emergency latch");
                Settle(); Settle();
                var fresh = Add(ConveyorKind.Generic, new Vector3(1200, 0, 0));
                Check(Stopped(fresh), "A new machine added after emptying a running line waits for Start again");
                a = fresh; b = Add(ConveyorKind.Incline, Vector3.zero, a); c = Add(ConveyorKind.Short, Vector3.zero, b);
                observer = Add(ConveyorKind.Short, new Vector3(1215, 0, -5));
                app.Select(a); app.Select(b, true); app.Select(c, true);
                string ids = a.UnitId + ", " + b.UnitId + ", " + c.UnitId;
                Check(app.SelectedMachines.Count == 3 && app.SelectionIds == ids && box.DisplayText == "3 MACHINES\n" + ids,
                    "Additive selection shows every selected unit on the dotted control-box display");
                Check(a.GetComponent<ConveyorSilhouette>().Highlighted && b.GetComponent<ConveyorSilhouette>().Highlighted
                    && c.GetComponent<ConveyorSilhouette>().Highlighted && !observer.GetComponent<ConveyorSilhouette>().Highlighted,
                    "Every selected machine has an outline while unselected machines remain clear");
                app.Select(b, true); Check(app.SelectedMachines.Count == 2 && !app.IsSelected(b), "Ctrl-selection toggles an existing machine out of the group");
                app.Select(b, true); box.OpenFor(a); Settle();
                Check(app.SelectedMachines.Count == 3 && app.SelectionIds == ids && !box.Hidden,
                    "Opening controls for a selected group member preserves the complete selection");
                line.SetLevel(6);
                Check(a.SpeedLevel == 6 && b.SpeedLevel == 6 && c.SpeedLevel == 6 && observer.SpeedLevel == 4,
                    "The unsynchronised dial adjusts the selected group without changing other machines");
                line.StartLine();
                groupStartA = a.transform.position + Vector3.right * 3f;
                groupStartB = b.transform.position + Vector3.right * 3f;
                groupStartC = c.transform.position + Vector3.right * 3f;
                Check(app.BeginMove(c) && app.IsGroupMove, "Dragging an elevated selected machine starts a group move");
                app.MovePreview(new Pose(groupStartC, c.transform.rotation), false, true);
                Check(app.PreviewIsValid && app.TryCommit() && Vector3.Distance(a.transform.position, groupStartA) < 0.0001f
                    && Vector3.Distance(b.transform.position, groupStartB) < 0.0001f && Vector3.Distance(c.transform.position, groupStartC) < 0.0001f,
                    "Group movement preserves all offsets, elevated heights and the chosen anchor");
                Check(a.Section.Next == b.Section && b.Section.Next == c.Section && c.Section.Previous == b.Section,
                    "Internal conveyor connections are restored after the group is placed");
                app.BeginMove(a); app.MovePreview(new Pose(observer.transform.position, a.transform.rotation), false, true);
                Check(!app.PreviewIsValid && !app.TryCommit(), "A group cannot be dropped through an unselected machine");
                app.CancelPreview(); line.EmergencyStop();
                Check(a.IsReturning && b.IsReturning && c.IsReturning && Vector3.Distance(a.transform.position, groupStartA) > 0.1f,
                    "An invalid group drop begins a shared smooth return instead of teleporting");
                stage = 2; next = EditorApplication.timeSinceStartup + 0.65;
            }
            else if (stage == 2)
            {
                Check(!a.IsEditing && !b.IsEditing && !c.IsEditing && Stopped(a) && Stopped(b) && Stopped(c)
                    && Vector3.Distance(a.transform.position, groupStartA) < 0.0001f && Vector3.Distance(c.transform.position, groupStartC) < 0.0001f,
                    "The group returns to all exact original poses without restarting after an emergency stop");
                Vector3 oldOffset = c.transform.position - a.transform.position;
                app.Rotate(1);
                Check(app.PreviewMachine == null && Vector3.Distance(c.transform.position - a.transform.position,
                    Quaternion.Euler(0, 90, 0) * oldOffset) < 0.0002f && a.Section.Next == b.Section && b.Section.Next == c.Section,
                    "Quarter-turn group rotation preserves the layout and its internal connections");
                app.BeginMove(c); Vector3 below = c.transform.position - Vector3.up * 2f;
                app.MovePreview(new Pose(below, c.transform.rotation), false, true);
                Check(!app.PreviewIsValid && a.transform.position.y >= 0f && b.transform.position.y >= 0f && c.transform.position.y >= 0f,
                    "A rejected group floor move keeps every root above the floor");
                Vector3 separation = c.transform.position - a.transform.position;
                Quaternion turn = Quaternion.Euler(0, 90, 0);
                app.MovePreview(new Pose(observer.transform.position + turn * separation, c.transform.rotation * turn), false, true);
                Check(!app.PreviewIsValid, "A rotated group still rejects collisions with unselected machines");
                app.CancelPreview(); stage = 3; next = EditorApplication.timeSinceStartup + 0.65;
                Check(Mathf.Abs(Vector3.Distance(c.transform.position, a.transform.position) - separation.magnitude) < 0.0003f,
                    "A returning rotated group stays rigid instead of compressing the space between its machines");
            }
            else
            {
                string observerId = observer.UnitId; app.RemoveSelected();
                Check(app.Machines.Count == 1 && app.Machines[0] == observer && observer.UnitId == observerId
                    && app.SelectedMachines.Count == 0 && line.Selected == null,
                    "One Delete removes all selected machines while preserving other machines and their stable names");
                var many = new List<PlacedConveyor>();
                for (int i = 0; i < 5; i++) many.Add(Add(ConveyorKind.Short, new Vector3(1200 + i * 3, 0, 15)));
                app.Select(many[0]); for (int i = 1; i < many.Count; i++) app.Select(many[i], true);
                string firstPage = box.DisplayText; Settle(); Settle();
                Check(box.DisplayText != firstPage && box.DisplayText.StartsWith("5 MACHINES\n"),
                    "Long selections page their stable unit letters instead of shrinking them into an unreadable line");
                Finish(true, null);
            }
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }
    private static void Finish(bool success, string error)
    {
        File.WriteAllText("Library/ConveyorControlBoxRuntimeReport.json", JsonUtility.ToJson(new Report { success = success, error = error, checks = checks.ToArray() }, true));
        if (success) Debug.Log("Integrated control box verification passed: " + checks.Count + " checks.");
        else Debug.LogError("Integrated control box verification failed: " + error);
        Cleanup();
    }
    private static void Cleanup()
    {
        EditorApplication.update -= Tick;
        if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
        app = null; box = null; line = null;
    }
    [MenuItem("Tools/Conveyor Studio/Build Six-Belt Control Demo %#&F7")]
    public static void Demo()
    {
        if (!EditorApplication.isPlaying) return;
        foreach (var app in Object.FindObjectsByType<ConveyorPlacementController>())
        {
            if (!app.enabled || app.gameObject.scene != SceneManager.GetActiveScene()) continue;
            if (app.Machines.Count != 0) { Debug.Log("The demo leaves the existing layout in place."); return; }
            PlacedConveyor previous = null, first = null;
            foreach (var kind in new[] { ConveyorKind.Generic, ConveyorKind.Short, ConveyorKind.Incline, ConveyorKind.Generic, ConveyorKind.Short, ConveyorKind.Incline })
            {
                var machine = app.BeginAdd(kind);
                Vector3 point = previous == null ? Vector3.zero : previous.Section.Exit.position - machine.Section.Entry.localPosition;
                app.MovePreview(new Pose(point, Quaternion.identity), previous != null, true);
                if (!app.TryCommit()) { Debug.LogError("The demonstration line could not be placed: " + app.Status); return; }
                if (first == null) first = machine; previous = machine;
            }
            app.orbitCamera.Reframe(app.Machines); app.controlBox.OpenFor(first);
            Debug.Log("Six connected conveyors are ready. Press the physical green Start button to run the line.");
            return;
        }
    }
}
