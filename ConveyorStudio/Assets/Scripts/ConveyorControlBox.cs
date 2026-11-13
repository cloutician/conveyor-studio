using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// A separate lit 3D model is rendered onto a screen-edge surface. World navigation
// never moves its camera; all input still hits the real projecting model controls.
[DisallowMultipleComponent]
public sealed class ConveyorControlBox : MonoBehaviour
{
    public ConveyorPlacementController placement;
    public ConveyorLineController line;
    public Vector3 renderingOrigin = new Vector3(1000f, 1000f, 1000f);
    private const int PanelLayer = 30;
    private const float PresentationScale = 0.8f;
    private const float OpenEdgeInset = 8f;
    private const float DialDragGain = 0.6375f, DetentBreakaway = 22.25f;
    [SerializeField] private bool hidden = true;
    private Canvas canvas;
    private RawImage image;
    private RectTransform imageRect;
    private Button hideButton;
    private GameObject studio;
    private Camera panelCamera;
    private RenderTexture texture;
    private Transform model, knob, lever, startCap, stopCap;
    private Vector3 front, right, up, startRest, stopRest;
    private Quaternion knobRest, leverRest;
    private float knobSign, leverSign, frontSign;
    private float dock, dockVelocity, shownAngle = 15f, angleVelocity, leverOffset, leverVelocity, startTravel, stopTravel;
    private float rawAngle, lastAngle, flex, startReleaseAt, nextLoad;
    private float promptRemaining, syncReboundRemaining;
    private string promptTop, promptBottom;
    private int displayedCount = -1, displayedLevel = -1;
    private PlacedConveyor displayedMachine;
    private bool displayedRunning, displayedEmergency, displayedSync;
    private List<string> selectionPages = new List<string>();
    private string displayedSelection;
    private int selectionPage;
    private float selectionPageTime;
    private Vector2 hoverTarget, tilt, tiltVelocity;
    private bool hovering;
    private bool peekHover;
    private float peekBlend, peekVelocity;
    private Vector3 viewCentre, restViewOffset;
    private List<Vector3> housingCorners = new List<Vector3>();
    private List<Vector3> fittingEnds = new List<Vector3>();
    private int pointer = int.MinValue;
    private ConveyorPanelControl grabbed;
    private PlacedConveyor grabbedMachine;
    private Material green, lamp, displayMaterial;
    private ConveyorDotMatrix display;
    [SerializeField, HideInInspector] private List<Material> ownedMaterials = new List<Material>();
    private float modelLeft = 0.06f;
    private float housingRight = 0.90f;
    private float restHousingRight = 0.90f;
    public bool Hidden => hidden;
    public bool Ready => image != null;
    public bool CapturingPointer => grabbed != ConveyorPanelControl.None;
    public float OpenWidthPixels
    {
        get
        {
            if (imageRect == null) return 0f;
            float width = imageRect.rect.width;
            float tabOffset = hideButton != null ? ((RectTransform)hideButton.transform).anchoredPosition.x : 0f;
            float contentLeft = Mathf.Min(modelLeft * width, 0.08f * width - 13f + tabOffset);
            return Mathf.Max(0f, width - OpenOffset(width) - contentLeft) * canvas.scaleFactor;
        }
    }
    public Camera PanelCamera => panelCamera;
    public RectTransform Surface => imageRect;
    public Transform SpeedKnob => knob;
    public Canvas ControlCanvas => canvas;
    public string DisplayText { get; private set; }

    public static void Ensure(ConveyorPlacementController placement)
    {
        var line = placement.GetComponent<ConveyorLineController>();
        if (line == null) line = placement.gameObject.AddComponent<ConveyorLineController>();
        line.placement = placement; line.enabled = true;
        var box = placement.GetComponent<ConveyorControlBox>();
        if (box == null) box = placement.gameObject.AddComponent<ConveyorControlBox>();
        box.placement = placement; box.line = line; placement.controlBox = box; box.enabled = true;
    }
    private void OnEnable()
    {
        if (placement == null) placement = GetComponent<ConveyorPlacementController>();
        if (line == null) line = GetComponent<ConveyorLineController>();
        if (line != null)
        {
            line.Changed -= StateChanged; line.Changed += StateChanged;
            line.Feedback -= ShowFeedback; line.Feedback += ShowFeedback;
        }
        if (canvas != null) canvas.gameObject.SetActive(true);
        if (studio != null) studio.SetActive(true);
        if (hideButton != null)
        { hideButton.onClick.RemoveListener(ToggleHidden); hideButton.onClick.AddListener(ToggleHidden); }
        RefreshTuckButton();
        if (model != null && panelCamera != null)
        {
            viewCentre = model.position - front * 0.025f + right * 0.015f;
            restViewOffset = front * 1.2f;
            MeasureRestDockBounds();
            RefreshRendering();
        }
        nextLoad = 0f;
        StateChanged();
    }
    private void OnDisable()
    {
        CancelInteraction();
        Leave();
        if (line != null) { line.Changed -= StateChanged; line.Feedback -= ShowFeedback; }
        if (canvas != null) canvas.gameObject.SetActive(false);
        if (studio != null) studio.SetActive(false);
    }
    private void Update()
    {
        if (image == null)
        {
            if (Time.unscaledTime < nextLoad) return;
            nextLoad = Time.unscaledTime + 0.5f;
            var prefab = Resources.Load<GameObject>("ControlBox/ControlBox");
            if (prefab == null) return;
            Build(prefab);
        }
        AdvancePresentation(Time.unscaledDeltaTime);
    }
    public void AdvancePresentation(float deltaTime)
    {
        if (!Ready) return;
        float dt = Mathf.Clamp(deltaTime, 0.0001f, 0.05f);
        if (selectionPages.Count > 1 && promptRemaining <= 0f)
        {
            selectionPageTime += dt;
            if (selectionPageTime >= 2f) { selectionPageTime = 0f; selectionPage = (selectionPage + 1) % selectionPages.Count; RefreshDisplay(); }
        }
        if (promptRemaining > 0f)
        {
            promptRemaining = Mathf.Max(0f, promptRemaining - dt);
            if (promptRemaining == 0f) RefreshDisplay();
        }
        if (grabbed != ConveyorPanelControl.None && grabbedMachine != line.Selected) CancelInteraction();
        dock = Mathf.SmoothDamp(dock, hidden ? 1f : 0f, ref dockVelocity, 0.15f, Mathf.Infinity, dt);
        AdvanceTilt(dt);
        peekBlend = Mathf.SmoothDamp(peekBlend, hidden && peekHover ? 1f : 0f, ref peekVelocity, 0.12f, Mathf.Infinity, dt);
        var rootRect = canvas.GetComponent<RectTransform>();
        float height = Mathf.Clamp(rootRect.rect.height - 150f, 260f, 540f) * PresentationScale, width = height * 0.75f;
        imageRect.sizeDelta = new Vector2(width, height);
        float tuckedOffset = width * (1f - modelLeft) - 34f - peekBlend * 16f;
        imageRect.anchoredPosition = new Vector2(Mathf.Lerp(OpenOffset(width), tuckedOffset, dock), -8f);
        // Fitting clipping shifts the 3D surface as it tilts. Counter that shift
        // only for the screen-space tab, retaining the drawer's slide motion.
        if (hideButton != null)
            ((RectTransform)hideButton.transform).anchoredPosition = new Vector2(
                (width * (1f - restHousingRight) + OpenEdgeInset - OpenOffset(width)) * (1f - dock), 0f);
        float dialTarget = 135f - line.Level * 30f - (grabbed == ConveyorPanelControl.Speed ? flex : 0f);
        shownAngle = Mathf.SmoothDamp(shownAngle, dialTarget, ref angleVelocity,
            grabbed == ConveyorPanelControl.Speed ? 0.0635f : 0.0465f, Mathf.Infinity, dt);
        knob.localRotation = knobRest * Quaternion.AngleAxis(knobSign * (shownAngle - 15f), Vector3.forward);
        syncReboundRemaining = Mathf.Max(0f, syncReboundRemaining - dt);
        leverOffset = Mathf.SmoothDamp(leverOffset, line.Synchronised || syncReboundRemaining > 0f ? -56f : 0f, ref leverVelocity, 0.075f, Mathf.Infinity, dt);
        lever.localRotation = leverRest * Quaternion.AngleAxis(leverSign * leverOffset, Vector3.right);
        float pressed = grabbed == ConveyorPanelControl.Start || Time.unscaledTime < startReleaseAt ? 0.003f : 0f;
        startTravel = Mathf.Lerp(startTravel, pressed, 1f - Mathf.Exp(-dt / 0.035f));
        stopTravel = Mathf.Lerp(stopTravel, line.Emergency || grabbed == ConveyorPanelControl.Stop ? 0.008f : 0f, 1f - Mathf.Exp(-dt / 0.045f));
        startCap.localPosition = startRest - Vector3.forward * (frontSign * startTravel);
        stopCap.localPosition = stopRest - Vector3.forward * (frontSign * stopTravel);
    }
    private void Build(GameObject prefab)
    {
        studio = new GameObject("Control box rendering studio"); studio.transform.SetParent(transform, false);
        studio.transform.position = renderingOrigin;
        model = Instantiate(prefab, studio.transform).transform;
        knob = Find("SpeedKnob_Pivot"); lever = Find("LinkLever_Pivot");
        startCap = Find("StartCap_Pivot"); stopCap = Find("EmergencyCap_Pivot");
        knobRest = knob.localRotation; leverRest = lever.localRotation;
        knobSign = Mathf.Sign(Mathf.DeltaAngle(0f, knob.localEulerAngles.z));
        leverSign = Mathf.Sign(Mathf.DeltaAngle(0f, lever.localEulerAngles.x));
        startRest = startCap.localPosition; stopRest = stopCap.localPosition;
        right = (Find("EmergencyStop").position - Find("StartButton").position).normalized;
        up = model.up;
        front = (Find("KnobFace").GetComponent<Renderer>().bounds.center - Find("RecessedFaceplate").GetComponent<Renderer>().bounds.center).normalized;
        // Remove the dial's vertical offset before deriving its outward normal.
        front = Vector3.Cross(right, up); front = front.normalized;
        var faceDelta = Find("KnobFace").GetComponent<Renderer>().bounds.center - Find("RecessedFaceplate").GetComponent<Renderer>().bounds.center;
        if (Vector3.Dot(front, faceDelta) < 0) front = -front;
        frontSign = Mathf.Sign(Vector3.Dot(front, startCap.parent.forward));
        foreach (var part in model.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = PanelLayer;
        AddControl(knob, ConveyorPanelControl.Speed); AddControl(Find("LinkSwitch"), ConveyorPanelControl.Sync);
        AddControl(Find("StartButton"), ConveyorPanelControl.Start); AddControl(Find("EmergencyStop"), ConveyorPanelControl.Stop);
        green = CloneMaterial(Find("StartButton").Find("StartCap_Pivot").GetComponentInChildren<Renderer>());
        lamp = CloneMaterial(Find("LinkLampLens").GetComponent<Renderer>());
        var oldDots = Find("FlatDotMatrix"); oldDots.GetComponent<Renderer>().enabled = false;
        displayMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        displayMaterial.SetColor("_BaseColor", new Color(0.50f, 0.84f, 0.70f)); ownedMaterials.Add(displayMaterial);
        var dots = new GameObject("Live dot-matrix display"); dots.layer = PanelLayer; dots.transform.SetParent(oldDots.parent, false);
        display = dots.AddComponent<ConveyorDotMatrix>();
        display.Configure(dots.transform.InverseTransformDirection(right), dots.transform.InverseTransformDirection(up), dots.transform.InverseTransformDirection(front), displayMaterial);
        var cameraObject = new GameObject("Control box camera", typeof(Camera)); cameraObject.transform.SetParent(studio.transform, false);
        panelCamera = cameraObject.GetComponent<Camera>(); panelCamera.cullingMask = 1 << PanelLayer;
        panelCamera.clearFlags = CameraClearFlags.SolidColor; panelCamera.backgroundColor = Color.clear;
        panelCamera.orthographic = true; panelCamera.orthographicSize = 0.305f; panelCamera.nearClipPlane = 0.05f; panelCamera.farClipPlane = 4f;
        panelCamera.allowHDR = false; panelCamera.allowMSAA = true; panelCamera.depth = -5;
        viewCentre = model.position - front * 0.025f + right * 0.015f;
        restViewOffset = front * 1.2f;
        cameraObject.transform.position = viewCentre + restViewOffset;
        cameraObject.transform.LookAt(viewCentre, up);
        texture = new RenderTexture(768, 1024, 24, RenderTextureFormat.ARGB32) { name = "Control box screen", antiAliasing = 4 };
        texture.Create(); panelCamera.targetTexture = texture;
        RefreshRendering();
        MakeLight("Control box soft key", front + right * -0.5f + up * 0.65f, 0.85f);
        MakeLight("Control box fill", front + right * 0.7f, 0.35f);
        placement.sceneCamera.cullingMask &= ~(1 << PanelLayer);
        foreach (var light in placement.GetComponentsInChildren<Light>()) if (!light.transform.IsChildOf(studio.transform)) light.cullingMask &= ~(1 << PanelLayer);
        Physics.SyncTransforms();
        MeasureRestDockBounds();
        BuildSurface(); dock = hidden ? 1f : 0f; shownAngle = 135f - line.Level * 30f; StateChanged();
    }
    // Show the fitting shafts, but crop just before either terminal face.
    private float OpenOffset(float width) => width * (1f - housingRight) + OpenEdgeInset;
    private void MeasureRestDockBounds()
    {
        Vector3 position = panelCamera.transform.position; Quaternion rotation = panelCamera.transform.rotation;
        panelCamera.transform.SetPositionAndRotation(viewCentre + restViewOffset, Quaternion.LookRotation(-restViewOffset, up));
        MeasureDockBounds(); restHousingRight = housingRight;
        panelCamera.transform.SetPositionAndRotation(position, rotation);
        MeasureDockBounds();
    }
    private void MeasureDockBounds()
    {
        if (housingCorners.Count == 0)
        {
            var enclosure = Find("Enclosure").GetComponent<MeshFilter>();
            Bounds bounds = enclosure.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                housingCorners.Add(enclosure.transform.TransformPoint(corner));
            }
            foreach (var part in model.GetComponentsInChildren<Transform>())
            {
                if (!part.name.StartsWith("GlandOutlet")) continue;
                var filter = part.GetComponent<MeshFilter>();
                if (filter != null) foreach (var vertex in filter.sharedMesh.vertices) fittingEnds.Add(filter.transform.TransformPoint(vertex));
            }
        }
        float left = 1f;
        foreach (var point in housingCorners) left = Mathf.Min(left, panelCamera.WorldToViewportPoint(point).x);
        modelLeft = Mathf.Clamp(left, 0f, 0.25f);
        float edge = 1f;
        foreach (var point in fittingEnds) edge = Mathf.Min(edge, panelCamera.WorldToViewportPoint(point).x);
        housingRight = Mathf.Clamp(edge - 0.004f, 0.5f, 1f);
    }
    public void Hover(Vector2 screen)
    {
        if (placement != null && placement.orbitCamera != null && placement.orbitCamera.IsNavigating) { Leave(); return; }
        if (!Ready || CapturingPointer) return;
        if (hidden) { peekHover = RectTransformUtility.RectangleContainsScreenPoint(imageRect, screen); hovering = false; return; }
        hovering = RectTransformUtility.ScreenPointToLocalPointInRectangle(imageRect, screen, null, out var point)
            && imageRect.rect.Contains(point);
        Rect rect = imageRect.rect;
        hoverTarget = new Vector2(Mathf.Clamp((point.x - rect.center.x) / (rect.width * 0.45f), -1f, 1f),
            Mathf.Clamp((point.y - rect.center.y) / (rect.height * 0.45f), -1f, 1f));
    }
    public void Leave() { hovering = false; peekHover = false; }
    private void AdvanceTilt(float dt)
    {
        // Hold the exact projection during a gesture so controls cannot drift
        // beneath the pointer. Moving the viewpoint makes the face counter-tilt.
        if (CapturingPointer) return;
        tilt = Vector2.SmoothDamp(tilt, hovering && !hidden ? hoverTarget : Vector2.zero, ref tiltVelocity, 0.105f, Mathf.Infinity, dt);
        // Enough travel to sweep through face-on and past the resting side angle.
        Vector3 offset = restViewOffset + right * (tilt.x * 0.36f) + up * (tilt.y * 0.24f);
        panelCamera.transform.position = viewCentre + offset;
        panelCamera.transform.LookAt(viewCentre, up);
        MeasureDockBounds();
    }
    private Transform Find(string name)
    { foreach (var part in model.GetComponentsInChildren<Transform>(true)) if (part.name == name) return part; throw new System.InvalidOperationException("Missing control-box part: " + name); }
    private Material CloneMaterial(Renderer renderer)
    { var material = new Material(renderer.sharedMaterial); renderer.sharedMaterial = material; ownedMaterials.Add(material); return material; }
    private void MakeLight(string name, Vector3 direction, float intensity)
    {
        var go = new GameObject(name, typeof(Light)); go.transform.SetParent(studio.transform, false);
        go.transform.rotation = Quaternion.LookRotation(-direction.normalized, up);
        var light = go.GetComponent<Light>(); light.type = LightType.Directional; light.intensity = intensity; light.cullingMask = 1 << PanelLayer; light.shadows = LightShadows.None;
    }
    public void RefreshRendering()
    {
        if (panelCamera == null) return;
        var data = panelCamera.GetUniversalAdditionalCameraData();
        data.renderShadows = false; data.renderPostProcessing = false;
        data.requiresColorOption = CameraOverrideOption.Off; data.requiresDepthOption = CameraOverrideOption.Off;
        data.volumeLayerMask = 0;
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)
            for (int i = 0; i < pipeline.rendererDataList.Length; i++)
                if (pipeline.rendererDataList[i] != null && pipeline.rendererDataList[i].name == "ControlBox_Renderer")
                { data.SetRenderer(i); break; }
    }
    private void AddControl(Transform parent, ConveyorPanelControl control)
    {
        var marker = parent.gameObject.AddComponent<ConveyorControlPart>(); marker.control = control;
        bool first = true; Bounds bounds = default;
        foreach (var filter in parent.GetComponentsInChildren<MeshFilter>())
        {
            Bounds source = filter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = source.center + Vector3.Scale(source.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = parent.InverseTransformPoint(filter.transform.TransformPoint(corner));
                if (first) { bounds = new Bounds(p, Vector3.zero); first = false; } else bounds.Encapsulate(p);
            }
        }
        var collider = parent.gameObject.AddComponent<BoxCollider>(); collider.isTrigger = true; collider.center = bounds.center;
        collider.size = bounds.size + new Vector3(0.006f, 0.006f, 0.006f);
    }
    private void BuildSurface()
    {
        var go = new GameObject("Control box interface", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); go.transform.SetParent(transform, false);
        canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 20;
        var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = 0.5f;
        var panel = new GameObject("Interactive 3D controls", typeof(RectTransform), typeof(RawImage), typeof(ConveyorControlBoxSurface)); panel.transform.SetParent(go.transform, false);
        image = panel.GetComponent<RawImage>(); image.texture = texture;
        imageRect = panel.GetComponent<RectTransform>(); imageRect.anchorMin = imageRect.anchorMax = new Vector2(1f, 0.5f); imageRect.pivot = new Vector2(1f, 0.5f);
        panel.GetComponent<ConveyorControlBoxSurface>().owner = this;
        var tab = new GameObject("Tuck control box", typeof(RectTransform), typeof(Image), typeof(Button)); tab.transform.SetParent(panel.transform, false);
        var rect = tab.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(0.08f, 0.55f); rect.sizeDelta = new Vector2(26f, 54f);
        tab.GetComponent<Image>().color = new Color(0.07f, 0.12f, 0.14f, 0.94f);
        hideButton = tab.GetComponent<Button>(); hideButton.onClick.AddListener(ToggleHidden); hideButton.navigation = new Navigation { mode = Navigation.Mode.None };
        var label = new GameObject("Arrow", typeof(RectTransform), typeof(Text)); label.transform.SetParent(tab.transform, false);
        var labelRect = label.GetComponent<RectTransform>(); labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one; labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
        var text = label.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.text = "›"; text.fontSize = 24; text.alignment = TextAnchor.MiddleCenter; text.color = Color.white; text.raycastTarget = false;
        RefreshTuckButton();
    }
    private void RefreshTuckButton()
    {
        if (hideButton == null) return;
        hideButton.gameObject.SetActive(!hidden);
        hideButton.GetComponentInChildren<Text>(true).text = "›";
    }
    private void StateChanged()
    {
        if (display == null || line == null) return;
        string ids = placement.SelectionIds;
        if (ids != displayedSelection)
        {
            displayedSelection = ids; selectionPages.Clear(); selectionPage = 0; selectionPageTime = 0f; promptRemaining = 0f;
            string page = "";
            foreach (string id in ids.Split(new[] { ", " }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                if (page.Length > 0 && page.Length + id.Length + 2 > 12) { selectionPages.Add(page); page = ""; }
                page += (page.Length == 0 ? "" : ", ") + id;
            }
            if (page.Length > 0) selectionPages.Add(page);
        }
        if (displayedCount != line.MachineCount || displayedMachine != line.Selected || displayedLevel != line.Level
            || displayedRunning != line.Running || displayedEmergency != line.Emergency || displayedSync != line.Synchronised)
        { promptRemaining = 0f; syncReboundRemaining = 0f; }
        displayedCount = line.MachineCount; displayedMachine = line.Selected; displayedLevel = line.Level;
        displayedRunning = line.Running; displayedEmergency = line.Emergency; displayedSync = line.Synchronised;
        RefreshDisplay();
        var selected = line.Selected;
        green.EnableKeyword("_EMISSION"); green.SetColor("_EmissionColor", line.Running ? new Color(0.10f, 0.55f, 0.18f) : Color.black);
        lamp.EnableKeyword("_EMISSION"); lamp.SetColor("_EmissionColor", line.Synchronised ? new Color(0.10f, 0.75f, 0.35f) : Color.black);
        if (grabbed != ConveyorPanelControl.None && grabbedMachine != selected) CancelInteraction();
    }
    private void RefreshDisplay()
    {
        if (display == null || line == null) return;
        var selected = line.Selected;
        string top = line.MachineCount == 0 ? "NO" : selected == null ? "SELECT" : selected.Definition.title.ToUpperInvariant();
        string bottom = line.MachineCount == 0 ? "MACHINES" : selected == null ? "A BELT" : "UNIT " + selected.UnitId;
        if (placement.SelectedMachines.Count > 1)
        {
            top = placement.SelectedMachines.Count + " MACHINES";
            bottom = selectionPages.Count > 0 ? selectionPages[selectionPage % selectionPages.Count] : placement.SelectionIds;
        }
        if (promptRemaining > 0f) { top = promptTop; bottom = promptBottom; }
        DisplayText = top + "\n" + bottom; display.SetText(top, bottom);
    }
    private void ShowFeedback(ConveyorLineFeedback feedback)
    {
        promptRemaining = feedback == ConveyorLineFeedback.EightMachines ? 3f : 2.4f;
        switch (feedback)
        {
            case ConveyorLineFeedback.AddMachinesToStart: promptTop = "ADD MACHINES"; promptBottom = "TO START"; break;
            case ConveyorLineFeedback.NeedTwoMachinesToSync:
                promptTop = "NEED TWO"; promptBottom = "TO SYNC"; syncReboundRemaining = 0.18f; break;
            default: promptTop = "EIGHT BELTS"; promptBottom = "VERY SUBTLE"; break;
        }
        RefreshDisplay();
    }
    public void OpenFor(PlacedConveyor machine)
    {
        if (machine == null) return;
        if (placement.IsSelected(machine)) placement.MakePrimary(machine); else placement.Select(machine);
        SetHidden(false, false); placement.FocusSelection(OpenWidthPixels);
    }
    public void SetHidden(bool value) => SetHidden(value, true);
    private void SetHidden(bool value, bool refocus)
    {
        bool changed = hidden != value;
        float previousSpace = hidden ? 0f : OpenWidthPixels;
        CancelInteraction(); hidden = value; Leave();
        RefreshTuckButton();
        if (changed && refocus && placement != null && placement.Machines.Count > 0 && placement.orbitCamera != null)
            placement.orbitCamera.AdjustPanelSpace(previousSpace, hidden ? 0f : OpenWidthPixels);
    }
    private void ToggleHidden() => SetHidden(!hidden);
    public bool BlocksPointer(Vector2 screen) => enabled && imageRect != null && (CapturingPointer || RectTransformUtility.RectangleContainsScreenPoint(imageRect, screen));
    private Ray ViewRay(Vector2 screen)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(imageRect, screen, null, out Vector2 point);
        var rect = imageRect.rect;
        return panelCamera.ViewportPointToRay(new Vector3((point.x - rect.xMin) / rect.width, (point.y - rect.yMin) / rect.height, 0f));
    }
    public ConveyorPanelControl ControlAt(Vector2 screen)
    {
        if (!Ready || !RectTransformUtility.RectangleContainsScreenPoint(imageRect, screen)) return ConveyorPanelControl.None;
        Ray ray = ViewRay(screen);
        if (!gameObject.scene.GetPhysicsScene().Raycast(ray.origin, ray.direction, out var hit, 4f, 1 << PanelLayer, QueryTriggerInteraction.Collide)) return ConveyorPanelControl.None;
        var part = hit.collider.GetComponentInParent<ConveyorControlPart>(); return part == null ? ConveyorPanelControl.None : part.control;
    }
    public Vector2 ControlScreenPoint(ConveyorPanelControl control)
    {
        if (!Ready) return Vector2.zero;
        foreach (var part in model.GetComponentsInChildren<ConveyorControlPart>())
        {
            if (part.control != control) continue;
            Vector3 viewport = panelCamera.WorldToViewportPoint(part.GetComponent<Collider>().bounds.center);
            Rect rect = imageRect.rect;
            Vector3 local = new Vector3(rect.xMin + viewport.x * rect.width, rect.yMin + viewport.y * rect.height, 0f);
            return RectTransformUtility.WorldToScreenPoint(null, imageRect.TransformPoint(local));
        }
        return Vector2.zero;
    }
    private float DialAngle(Vector2 screen)
    {
        Ray ray = ViewRay(screen); var plane = new Plane(front, knob.position);
        if (!plane.Raycast(ray, out float along)) return lastAngle;
        Vector3 delta = ray.GetPoint(along) - knob.position;
        return Mathf.Atan2(Vector3.Dot(delta, right), Vector3.Dot(delta, up)) * Mathf.Rad2Deg;
    }
    public void Press(Vector2 screen, int pointerId)
    {
        if (!Ready || CapturingPointer) return;
        if (placement != null && placement.orbitCamera != null && placement.orbitCamera.IsNavigating) return;
        if (hidden) { SetHidden(false); return; }
        grabbed = ControlAt(screen); if (grabbed == ConveyorPanelControl.None) return;
        pointer = pointerId; grabbedMachine = line.Selected; lastAngle = DialAngle(screen); rawAngle = -135f + line.Level * 30f;
        if (grabbed == ConveyorPanelControl.Stop) line.EmergencyStop();
    }
    public void Drag(Vector2 screen, int pointerId)
    {
        if (pointer != pointerId || grabbed != ConveyorPanelControl.Speed || grabbedMachine == null) return;
        float angle = DialAngle(screen); rawAngle = Mathf.Clamp(rawAngle + Mathf.DeltaAngle(lastAngle, angle) * DialDragGain, -135f, 135f); lastAngle = angle;
        int next = line.Level;
        while (next < 9 && rawAngle - (-135f + next * 30f) > DetentBreakaway) next++;
        while (next > 0 && rawAngle - (-135f + next * 30f) < -DetentBreakaway) next--;
        if (next != line.Level) line.SetLevel(next);
        flex = (float)System.Math.Tanh((rawAngle - (-135f + line.Level * 30f)) / 11.7f) * 6.55f;
    }
    public void Release(Vector2 screen, int pointerId)
    {
        if (pointer != pointerId) return;
        var control = grabbed; bool activate = ControlAt(screen) == control;
        CancelInteraction();
        if (!activate) return;
        if (control == ConveyorPanelControl.Start) { line.StartLine(); startReleaseAt = Time.unscaledTime + 0.12f; }
        else if (control == ConveyorPanelControl.Sync) line.SetSynchronised(!line.Synchronised);
        Hover(screen);
    }
    public void CancelInteraction() { grabbed = ConveyorPanelControl.None; pointer = int.MinValue; grabbedMachine = null; flex = 0f; }
    public void ScrollDial(Vector2 screen, float amount)
    { if (!hidden && ControlAt(screen) == ConveyorPanelControl.Speed && Mathf.Abs(amount) >= 0.5f) line.SetLevel(line.Level + (amount > 0 ? 1 : -1)); }
    private void OnApplicationFocus(bool active) { if (!active) { CancelInteraction(); Leave(); } }
    private void OnDestroy()
    {
        if (line != null) { line.Changed -= StateChanged; line.Feedback -= ShowFeedback; }
        if (studio != null) Destroy(studio);
        if (canvas != null) Destroy(canvas.gameObject);
        if (texture != null) { texture.Release(); Destroy(texture); }
        foreach (var material in ownedMaterials) if (material != null) Destroy(material);
    }
}
