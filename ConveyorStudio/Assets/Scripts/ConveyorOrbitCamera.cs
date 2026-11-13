using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public sealed class ConveyorOrbitCamera : MonoBehaviour
{
    public ConveyorPlacementController placement;
    private Vector3 focus = new Vector3(0f, 0.3f, 0f);
    private float yaw = -35f, pitch = 42f, distance = 10f;
    private float minimumDistance = 0.45f, maximumDistance = 18f;
    private float targetLogDistance, lastScrollDirection;
    private bool zoomReady, hasZoomAnchor;
    private Vector3 zoomAnchor, zoomFocusPerMetre;
    private float lastZoomInputTime;
    private float nextBoundsUpdate;
    private Camera viewCamera;
    private Camera ViewCamera => viewCamera != null ? viewCamera : viewCamera = GetComponent<Camera>();
    private struct ModelGeometry { public int start, count; public Bounds bounds; }
    private readonly List<Vector3> framingPoints = new List<Vector3>();
    private readonly List<MeshFilter> meshFilters = new List<MeshFilter>();
    private readonly List<ModelGeometry> modelGeometry = new List<ModelGeometry>();
    private readonly Plane[] visibilityPlanes = new Plane[6];
    private const float ZoomPerStep = 0.14f, ZoomResponse = 0.055f, MaximumZoomRate = 5f;
    private bool focusing;
    private Vector3 focusStart, focusGoal;
    private float focusDistanceStart, focusDistanceGoal, focusElapsed, focusDuration = 0.36f;
    private bool orbitDragging, panDragging;
    public bool IsFocusing => focusing;
    public float ViewingDistance => distance;
    public bool IsNavigating => orbitDragging || panDragging;
    public void Focus(Vector3 position) { focusing = false; focus = position + Vector3.up * 0.3f; CancelZoom(); Apply(); }
    public void FocusMachine(PlacedConveyor machine, float reservedRightPixels = 0f, bool animate = true)
    {
        if (machine == null) return;
        FocusMachines(new[] { machine }, reservedRightPixels, animate);
    }
    public void FocusMachines(IReadOnlyList<PlacedConveyor> machines, float reservedRightPixels = 0f, bool animate = true)
    {
        CollectGeometry(machines); if (framingPoints.Count == 0) return;
        FitWithPanel(reservedRightPixels, out var goal, out float goalDistance, out _);
        maximumDistance = Mathf.Max(maximumDistance, goalDistance * 2.4f);
        MoveFocus(goal, goalDistance, 0.36f, animate);
    }
    private void MoveFocus(Vector3 goal, float goalDistance, float duration, bool animate)
    {
        focusStart = focus; focusDistanceStart = distance; focusElapsed = 0f; focusDuration = duration;
        focusGoal = goal; focusDistanceGoal = goalDistance; CancelZoom(); focusing = animate;
        if (!animate) { focus = focusGoal; distance = focusDistanceGoal; CancelZoom(); Apply(); }
    }
    public void AdvanceFocus(float deltaTime)
    {
        if (!focusing) return;
        focusElapsed += Mathf.Min(Mathf.Max(0f, deltaTime), 0.05f);
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(focusElapsed / focusDuration));
        focus = Vector3.Lerp(focusStart, focusGoal, t);
        distance = focusDistanceStart == focusDistanceGoal ? focusDistanceStart
            : Mathf.Exp(Mathf.Lerp(Mathf.Log(focusDistanceStart), Mathf.Log(focusDistanceGoal), t));
        Apply();
        if (focusElapsed >= focusDuration) { focusing = false; CancelZoom(); }
    }
    public void FrameAll(IReadOnlyList<PlacedConveyor> machines) => Reframe(machines);
    public void AdjustPanelSpace(float oldReservedPixels, float newReservedPixels)
    {
        if (placement == null || placement.WorldScreenRect.width <= 1f) return;
        float width = placement.WorldScreenRect.width;
        float aspect = width / Mathf.Max(1f, placement.WorldScreenRect.height);
        float delta = (Mathf.Clamp(newReservedPixels, 0f, width * 0.48f) - Mathf.Clamp(oldReservedPixels, 0f, width * 0.48f)) / width;
        float halfWidth = ViewCamera.orthographic ? ViewCamera.orthographicSize * aspect
            : distance * Mathf.Tan(ViewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * aspect;
        Vector3 shift = Quaternion.Euler(pitch, yaw, 0f) * Vector3.right * (delta * halfWidth);
        MoveFocus(focus + shift, distance, 0.3f, true);
    }
    public void Reframe(IReadOnlyList<PlacedConveyor> machines, bool animate = true)
    {
        if (machines.Count == 0)
        { RefreshZoomLimits(machines); MoveFocus(new Vector3(0f, 0.3f, 0f), 10f, 0.3f, animate); return; }
        CollectGeometry(machines);
        if (framingPoints.Count == 0) return;
        float reserved = placement != null && placement.controlBox != null && placement.controlBox.enabled && !placement.controlBox.Hidden
            ? placement.controlBox.OpenWidthPixels : 0f;
        FitWithPanel(reserved, out var goal, out float goalDistance, out float farthest);
        UpdateLimits(goalDistance);
        var camera = ViewCamera;
        camera.farClipPlane = Mathf.Max(camera.farClipPlane, goalDistance + farthest + 20f);
        MoveFocus(goal, goalDistance, 0.3f, animate);
    }
    private void CollectGeometry(IReadOnlyList<PlacedConveyor> machines)
    {
        framingPoints.Clear();
        modelGeometry.Clear();
        float narrowest = float.PositiveInfinity;
        foreach (var machine in machines)
        {
            if (machine == null) continue;
            int start = framingPoints.Count;
            meshFilters.Clear(); machine.GetComponentsInChildren(false, meshFilters);
            bool found = false; Bounds machineBounds = default;
            foreach (var filter in meshFilters)
            {
                var renderer = filter.GetComponent<Renderer>();
                if (filter.sharedMesh == null || renderer == null || !renderer.enabled) continue;
                Bounds local = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 point = filter.transform.TransformPoint(corner);
                    framingPoints.Add(point);
                    if (!found) { machineBounds = new Bounds(point, Vector3.zero); found = true; }
                    else machineBounds.Encapsulate(point);
                }
            }
            if (found)
            {
                narrowest = Mathf.Min(narrowest, machineBounds.size.x, machineBounds.size.z);
                modelGeometry.Add(new ModelGeometry { start = start, count = framingPoints.Count - start, bounds = machineBounds });
            }
        }
        minimumDistance = float.IsPositiveInfinity(narrowest) ? 0.45f : Mathf.Clamp(narrowest * 0.6f, 0.35f, 0.8f);
    }
    private void FitWithPanel(float reservedRightPixels, out Vector3 centre, out float required, out float farthest)
    {
        float width = placement != null ? placement.WorldScreenRect.width : ViewCamera.pixelWidth;
        float height = placement != null ? placement.WorldScreenRect.height : ViewCamera.pixelHeight;
        reservedRightPixels = Mathf.Clamp(reservedRightPixels, 0f, width * 0.48f);
        FitGeometry(out centre, out required, out farthest, width / Mathf.Max(1f, height), reservedRightPixels / Mathf.Max(1f, width));
    }
    private void FitGeometry(out Vector3 centre, out float required, out float farthest, float aspectOverride = 0f, float reservedRightFraction = 0f)
    {
        Bounds bounds = new Bounds(framingPoints[0], Vector3.zero);
        foreach (var point in framingPoints) bounds.Encapsulate(point);
        centre = bounds.center;
        var camera = ViewCamera;
        float aspect = camera.aspect;
        if (placement != null && placement.WorldScreenRect.width > 1f && placement.WorldScreenRect.height > 1f)
            aspect = placement.WorldScreenRect.width / placement.WorldScreenRect.height;
        if (aspectOverride > 0f) aspect = aspectOverride;
        float tanVertical = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float tanHorizontal = tanVertical * aspect;
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Quaternion inverse = Quaternion.Inverse(rotation);
        required = minimumDistance; farthest = 0f;
        float nearest = float.PositiveInfinity;
        const float padding = 1.15f;
        float horizontal = tanHorizontal * (1f - reservedRightFraction) / padding, vertical = tanVertical / padding;
        float horizontalCentre = -tanHorizontal * reservedRightFraction;
        float leftSlope = horizontalCentre - horizontal, rightSlope = horizontalCentre + horizontal;
        float left = float.NegativeInfinity, right = float.PositiveInfinity;
        float bottom = float.NegativeInfinity, top = float.PositiveInfinity;
        foreach (var point in framingPoints)
        {
            Vector3 local = inverse * (point - centre);
            left = Mathf.Max(left, local.x - rightSlope * local.z);
            right = Mathf.Min(right, local.x - leftSlope * local.z);
            bottom = Mathf.Max(bottom, local.y - vertical * local.z);
            top = Mathf.Min(top, local.y + vertical * local.z);
            required = Mathf.Max(required, camera.nearClipPlane * 2f - local.z);
            nearest = Mathf.Min(nearest, local.z);
            farthest = Mathf.Max(farthest, local.z);
        }
        // Balance both sides of the perspective frustum, rather than aiming at
        // an arbitrary world-bounds centre and leaving empty space on one side.
        required = Mathf.Max(required, (left - right) / (2f * horizontal), (bottom - top) / (2f * vertical));
        // A tight silhouette fit can put the lens almost against the front roller
        // when looking down a line at a low angle. Keep a comfortable stand-off
        // and limit the apparent size difference between the near and far ends.
        float angleBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(8f, 35f, pitch));
        float maximumPerspectiveScale = Mathf.Lerp(2.25f, 2.5f, angleBlend);
        float standOff = Mathf.Max(minimumDistance * 2.5f, (farthest - nearest) / (maximumPerspectiveScale - 1f));
        required = Mathf.Max(required, standOff - nearest);
        // Solve the side offset at the final distance. A later sideways shift
        // would move near geometry farther on screen than distant geometry.
        centre += rotation * new Vector3((left + right - (leftSlope + rightSlope) * required) * 0.5f,
            (bottom + top) * 0.5f, 0f);
    }
    private void UpdateLimits(float framedDistance)
    {
        maximumDistance = Mathf.Max(6f, framedDistance * 2.4f);
        nextBoundsUpdate = Time.unscaledTime + 0.2f;
    }
    private void RefreshZoomLimits(IReadOnlyList<PlacedConveyor> machines)
    {
        CollectGeometry(machines);
        if (framingPoints.Count == 0) { maximumDistance = 18f; nextBoundsUpdate = Time.unscaledTime + 0.2f; return; }
        FitGeometry(out _, out float framedDistance, out float farthest);
        UpdateLimits(framedDistance);
        var camera = ViewCamera;
        camera.farClipPlane = Mathf.Max(camera.farClipPlane, maximumDistance + farthest + 20f);
    }
    private Vector3 VisibleModelsCentre()
    {
        var camera = ViewCamera;
        GeometryUtility.CalculateFrustumPlanes(camera, visibilityPlanes);
        Vector2 low = Vector2.one, high = Vector2.zero;
        float nearest = float.PositiveInfinity, farthest = 0f;
        bool visible = false;
        foreach (var model in modelGeometry)
        {
            if (!GeometryUtility.TestPlanesAABB(visibilityPlanes, model.bounds)) continue;
            Vector2 modelLow = Vector2.one * float.PositiveInfinity, modelHigh = Vector2.one * float.NegativeInfinity;
            float near = float.PositiveInfinity, far = 0f;
            bool crossesNear = false;
            for (int i = model.start; i < model.start + model.count; i++)
            {
                Vector3 point = camera.WorldToViewportPoint(framingPoints[i]);
                if (point.z <= camera.nearClipPlane) { crossesNear = true; continue; }
                modelLow = Vector2.Min(modelLow, point); modelHigh = Vector2.Max(modelHigh, point);
                near = Mathf.Min(near, point.z); far = Mathf.Max(far, point.z);
            }
            if (far == 0f) continue;
            // At extreme close range the near plane can cut the model's bounds.
            // Use the viewport centre rather than projecting behind the camera.
            if (crossesNear) { modelLow = Vector2.zero; modelHigh = Vector2.one; near = camera.nearClipPlane; }
            if (modelHigh.x < 0f || modelLow.x > 1f || modelHigh.y < 0f || modelLow.y > 1f) continue;
            low = Vector2.Min(low, new Vector2(Mathf.Clamp01(modelLow.x), Mathf.Clamp01(modelLow.y)));
            high = Vector2.Max(high, new Vector2(Mathf.Clamp01(modelHigh.x), Mathf.Clamp01(modelHigh.y)));
            nearest = Mathf.Min(nearest, near); farthest = Mathf.Max(farthest, far); visible = true;
        }
        if (!visible) return focus;
        Vector2 centre = (low + high) * 0.5f;
        return camera.ViewportToWorldPoint(new Vector3(centre.x, centre.y, (nearest + farthest) * 0.5f));
    }
    private void OnEnable() { orbitDragging = panDragging = false; CancelZoom(); nextBoundsUpdate = 0f; Apply(); }
    private void OnDisable() { orbitDragging = panDragging = false; }
    private void OnApplicationFocus(bool active) { if (!active) { orbitDragging = panDragging = false; CancelZoom(); } }
    private void CancelZoom()
    {
        targetLogDistance = Mathf.Log(Mathf.Max(0.01f, distance));
        zoomReady = true; hasZoomAnchor = false; lastScrollDirection = 0f;
    }
    private static float NormalizedScroll(float value, InputSettings.ScrollDeltaBehavior behavior, RuntimePlatform platform)
    {
        // Input System 1.20 defaults to wheel steps, including fractional trackpad
        // steps. Only legacy native-range Windows input needs the 120 conversion.
        bool nativeWindows = behavior == InputSettings.ScrollDeltaBehavior.KeepPlatformSpecificInputRange
            && (platform == RuntimePlatform.WindowsEditor || platform == RuntimePlatform.WindowsPlayer);
        return nativeWindows ? value / 120f : value;
    }
    /// <summary>Queue normalized wheel steps; fractional two-finger movement is retained.</summary>
    public void ScrollZoom(float steps, Vector2 screenPosition)
    {
        if (Mathf.Abs(steps) < 0.000001f || (placement != null && !placement.WorldScreenRect.Contains(screenPosition))) return;
        focusing = false;
        if (!zoomReady) CancelZoom();
        bool newGesture = !hasZoomAnchor || Time.unscaledTime - lastZoomInputTime > 0.25f;
        if (placement != null && (newGesture || Time.unscaledTime >= nextBoundsUpdate)) RefreshZoomLimits(placement.Machines);
        float direction = Mathf.Sign(steps);
        // A reversal responds immediately instead of first paying off queued zoom.
        if (lastScrollDirection != 0f && direction != lastScrollDirection) targetLogDistance = Mathf.Log(distance);
        lastScrollDirection = direction;
        float current = Mathf.Log(distance);
        targetLogDistance = Mathf.Clamp(targetLogDistance - steps * ZoomPerStep,
            Mathf.Min(current, Mathf.Log(minimumDistance)), Mathf.Max(current, Mathf.Log(maximumDistance)));
        if (newGesture)
        {
            zoomAnchor = VisibleModelsCentre();
            // Keep one absolute reference throughout a gesture. Repeatedly scaling
            // the rounded world position introduces visible drift far from origin.
            zoomFocusPerMetre = (focus - zoomAnchor) / distance;
        }
        lastZoomInputTime = Time.unscaledTime;
        hasZoomAnchor = true;
    }
    /// <summary>Advance the short settling response independently of render frame rate.</summary>
    public void AdvanceZoom(float deltaTime)
    {
        if (!zoomReady || deltaTime <= 0f) return;
        float dt = Mathf.Min(deltaTime, 0.05f), current = Mathf.Log(distance);
        float change = (targetLogDistance - current) * (1f - Mathf.Exp(-dt / ZoomResponse));
        change = Mathf.Clamp(change, -MaximumZoomRate * dt, MaximumZoomRate * dt);
        if (Mathf.Abs(targetLogDistance - current) < 0.00001f) return;
        float nextDistance = Mathf.Exp(current + change);
        Vector3 nextFocus = hasZoomAnchor ? zoomAnchor + zoomFocusPerMetre * nextDistance : focus;
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 from = focus - rotation * Vector3.forward * distance;
        Vector3 to = nextFocus - rotation * Vector3.forward * nextDistance;
        float allowed = 1f;
        float floorLimit = MinimumCameraHeight(rotation);
        if (to.y < floorLimit && to.y < from.y) allowed = Mathf.Clamp01((from.y - floorLimit) / (from.y - to.y));
        Vector3 travel = to - from;
        float length = travel.magnitude;
        float clearance = Mathf.Max(0.06f, ViewCamera.nearClipPlane * 1.25f);
        if (length > 0.00001f && gameObject.scene.GetPhysicsScene().SphereCast(from, clearance, travel / length,
            out var hit, length, ~0, QueryTriggerInteraction.Ignore))
            allowed = Mathf.Min(allowed, Mathf.Clamp01((hit.distance - 0.01f) / length));
        focus = Vector3.Lerp(focus, nextFocus, allowed);
        distance = Mathf.Lerp(distance, nextDistance, allowed);
        if (allowed < 1f) CancelZoom();
        Apply();
    }
    private void Start() => Apply();
    // Navigation owns a held right/middle gesture until release, even when the
    // cursor crosses the panel, other UI, or the edge of the world viewport.
    public bool NavigatePointer(Vector2 screen, Vector2 delta, bool orbitHeld, bool panHeld)
    {
        if (!orbitHeld) orbitDragging = false;
        if (!panHeld) panDragging = false;
        if (placement == null) return false;
        bool canBegin = placement.WorldScreenRect.Contains(screen)
            && (placement.controlBox == null || !placement.controlBox.CapturingPointer);
        if (canBegin) { orbitDragging |= orbitHeld; panDragging |= panHeld; }
        if (!IsNavigating) return false;
        focusing = false; CancelZoom();
        if (delta.sqrMagnitude == 0f) return true;
        if (orbitDragging)
        { yaw += delta.x * 0.22f; pitch = Mathf.Clamp(pitch - delta.y * 0.18f, 5f, 75f); }
        if (panDragging)
        {
            Vector3 right = Quaternion.Euler(0, yaw, 0) * Vector3.right;
            Vector3 forward = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            focus -= (right * delta.x + forward * delta.y) * distance * 0.0012f;
        }
        nextBoundsUpdate = 0f; Apply(); return true;
    }
    private void LateUpdate()
    {
        AdvanceFocus(Time.unscaledDeltaTime);
        var mouse = Mouse.current;
        if (mouse == null || placement == null || !Application.isFocused) { orbitDragging = panDragging = false; CancelZoom(); return; }
        if (NavigatePointer(mouse.position.ReadValue(), mouse.delta.ReadValue(), mouse.rightButton.isPressed, mouse.middleButton.isPressed)) return;
        if (!placement.WorldScreenRect.Contains(mouse.position.ReadValue())
            || (placement.controlBox != null && placement.controlBox.BlocksPointer(mouse.position.ReadValue()))
            || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())) { CancelZoom(); return; }
        float scroll = NormalizedScroll(mouse.scroll.ReadValue().y, InputSystem.settings.scrollDeltaBehavior, Application.platform);
        ScrollZoom(scroll, mouse.position.ReadValue());
        if (!focusing) AdvanceZoom(Time.unscaledDeltaTime);
    }
    private void Apply()
    {
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 position = focus - rotation * Vector3.forward * distance;
        float floorLimit = MinimumCameraHeight(rotation);
        if (position.y < floorLimit)
        {
            // A distant fitted pivot can lie below the ground. Lowering the
            // orbit angle must not take the eye through the floor with it.
            Vector3 lift = Vector3.up * (floorLimit - position.y);
            focus += lift; position += lift;
            if (hasZoomAnchor) zoomFocusPerMetre = (focus - zoomAnchor) / Mathf.Max(0.01f, distance);
        }
        transform.SetPositionAndRotation(position, rotation);
    }
    private float MinimumCameraHeight(Quaternion rotation)
    {
        var camera = ViewCamera;
        float near = camera.nearClipPlane;
        float halfHeight = camera.orthographic ? camera.orthographicSize : near * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float lowestNearCorner = (rotation * Vector3.forward).y * near
            - Mathf.Abs((rotation * Vector3.up).y) * halfHeight
            - Mathf.Abs((rotation * Vector3.right).y) * halfHeight * camera.aspect;
        float floorY = placement != null ? placement.transform.position.y : 0f;
        // Protect the whole near clipping plane, not just the camera's origin.
        return floorY + Mathf.Max(0.12f, 0.02f - lowestNearCorner);
    }
}
