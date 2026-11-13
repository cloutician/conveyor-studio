using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// The library and editing tools are runtime UI: they are included in the exported app.
public sealed class ConveyorStudioHud : MonoBehaviour
{
    public ConveyorPlacementController placement;
    private Canvas canvas;
    private const float LibraryWidth = 132f;
    private const float ProductPaneHeight = 52f;
    private RectTransform library, libraryItems;
    private Text status, libraryToggle;
    private Button rotateLeft, rotateRight, parcel, can;
    private Image brandGraphic, boxGraphic, canGraphic;
    private Font font;
    private sealed class ModuleTab { public ConveyorKind kind; public GameObject marker; public Text caption; }
    private readonly List<ModuleTab> moduleTabs = new List<ModuleTab>();
    private readonly Color panel = new Color(0.055f, 0.078f, 0.10f);
    private readonly Color card = new Color(0.10f, 0.135f, 0.16f);
    private readonly Color muted = new Color(0.55f, 0.65f, 0.69f);
    private readonly Color accent = new Color(0.29f, 0.94f, 0.70f);

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        // Rebuild only the owned interface, including after a live script reload.
        // Conveyor placement, selection and the camera stay in the running workspace.
        if (placement != null) placement.Changed -= Refresh;
        foreach (Transform child in transform)
            if (child.name == "Studio interface") { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        moduleTabs.Clear();
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var go = new GameObject("Studio interface", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = 0.5f;
        if (EventSystem.current == null)
        {
            var events = new GameObject("Studio input", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(transform, false);
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
        RectTransform root = go.GetComponent<RectTransform>();
        var header = Panel("Header", root, panel); Stretch(header, new Vector2(0, 1), Vector2.one, new Vector2(0, -68), Vector2.zero);
        var brandMark = Panel("Conveyor Studio icon", header, Color.white); Place(brandMark, 20, 12, 44, 44);
        brandGraphic = brandMark.GetComponent<Image>(); brandGraphic.sprite = Resources.Load<Sprite>("Studio/ConveyorStudioIcon");
        brandMark.GetComponent<Image>().preserveAspect = true; brandMark.GetComponent<Image>().raycastTarget = false;
        Label(header, "CONVEYOR STUDIO", 76, 10, 270, 28, 21, Color.white, FontStyle.Bold);
        Label(header, "FOR VACAC", 77, 38, 180, 17, 10, muted);
        var frame = MakeButton(header, "Reframe", 0, 17, 100, 34, () => placement.orbitCamera.Reframe(placement.Machines));
        frame.GetComponent<RectTransform>().anchorMin = frame.GetComponent<RectTransform>().anchorMax = new Vector2(1, 1);
        frame.GetComponent<RectTransform>().anchoredPosition = new Vector2(-215, -17);
        var toggle = MakeButton(header, "Hide library", 0, 17, 100, 34, ToggleLibrary);
        toggle.GetComponent<RectTransform>().anchorMin = toggle.GetComponent<RectTransform>().anchorMax = new Vector2(1, 1);
        toggle.GetComponent<RectTransform>().anchoredPosition = new Vector2(-110, -17);
        libraryToggle = toggle.GetComponentInChildren<Text>();

        library = Panel("Module library", root, panel);
        Stretch(library, Vector2.zero, new Vector2(0, 1), new Vector2(0, 52), new Vector2(LibraryWidth, -68));
        var items = new GameObject("Equipment", typeof(RectTransform)); items.transform.SetParent(library, false);
        libraryItems = items.GetComponent<RectTransform>();
        libraryItems.anchorMin = new Vector2(0f, 0.5f); libraryItems.anchorMax = new Vector2(1f, 0.5f);
        libraryItems.pivot = new Vector2(0.5f, 0.5f); libraryItems.anchoredPosition = Vector2.zero;
        int i = 0;
        foreach (var item in placement.catalog.modules)
        {
            var module = item;
            var button = MakeButton(libraryItems, "", 12, 16 + i * 118, 108, 108, () => placement.BeginAdd(module.kind));
            button.name = module.title + " tile";
            var rect = button.GetComponent<RectTransform>();
            if (module.thumbnail != null)
            {
                var viewport = new GameObject("Image viewport", typeof(RectTransform), typeof(RectMask2D));
                viewport.transform.SetParent(rect, false); Place(viewport.GetComponent<RectTransform>(), 5, 5, 98, 78);
                var picture = new GameObject("Module preview", typeof(RectTransform), typeof(Image)); picture.transform.SetParent(viewport.transform, false);
                var image = picture.GetComponent<Image>(); image.sprite = module.thumbnail; image.preserveAspect = true; image.raycastTarget = false;
                // Trim the renders' transparent framing, leaving room above the incline.
                // The Short render uses a tighter camera: compensate so it reads smaller.
                if (module.kind == ConveyorKind.Incline) Place(picture.GetComponent<RectTransform>(), -17, 0, 132, 80);
                else if (module.kind == ConveyorKind.Short) Place(picture.GetComponent<RectTransform>(), -21, 5, 140, 84);
                else Place(picture.GetComponent<RectTransform>(), -39, -13, 176, 140);
            }
            var caption = Label(rect, (i + 1) + "  " + module.title.ToUpperInvariant(), 4, 83, 100, 20, 10, Color.white, FontStyle.Bold);
            caption.alignment = TextAnchor.MiddleCenter;
            var marker = Panel("Active module", rect, accent); Place(marker, 8, 105, 92, 2);
            moduleTabs.Add(new ModuleTab { kind = module.kind, marker = marker.gameObject, caption = caption });
            i++;
        }
        float controlsY = 16 + i * 118 + 18;
        parcel = ProductButton("Box", "Studio/BoxIcon", 12, controlsY, 68, placement.AddParcel);
        can = ProductButton("Can", "Studio/CanIcon", 88, controlsY, 32, placement.AddCan);
        rotateLeft = MakeButton(libraryItems, "−90°", 12, controlsY + ProductPaneHeight + 10, 50, 44, () => placement.Rotate(-1));
        rotateRight = MakeButton(libraryItems, "+90°", 70, controlsY + ProductPaneHeight + 10, 50, 44, () => placement.Rotate(1));
        libraryItems.sizeDelta = new Vector2(0f, controlsY + ProductPaneHeight + 70f);
        library.gameObject.SetActive(placement.LibraryOpen);
        libraryToggle.text = placement.LibraryOpen ? "Hide library" : "Show library";

        var footer = Panel("Status bar", root, panel);
        Stretch(footer, Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 52));
        status = Label(footer, "", 20, 7, 1220, 20, 12, accent);
        Label(footer, "1 / 2 / 3 add conveyor   CTRL-CLICK group   DRAG move   DOUBLE TAP controls   Q / E rotate   DEL delete   ESC cancel   RMB orbit   MMB pan   SCROLL zoom   HOME reframe", 20, 30, 1230, 16, 10, muted);
        placement.Changed += Refresh;
        Refresh();
        ConveyorControlBox.Ensure(placement);
    }

    private void ToggleLibrary()
    {
        placement.LibraryOpen = !placement.LibraryOpen;
        library.gameObject.SetActive(placement.LibraryOpen);
        libraryToggle.text = placement.LibraryOpen ? "Hide library" : "Show library";
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }
    private void LateUpdate()
    {
        if (canvas == null) return;
        float scale = canvas.scaleFactor;
        float left = placement.LibraryOpen ? LibraryWidth * scale : 0f;
        float bottom = 52f * scale, top = 68f * scale;
        var rect = new Rect(left, bottom, Mathf.Max(1f, Screen.width - left), Mathf.Max(1f, Screen.height - bottom - top));
        placement.WorldScreenRect = rect;
        placement.sceneCamera.rect = new Rect(rect.x / Screen.width, rect.y / Screen.height, rect.width / Screen.width, rect.height / Screen.height);
        status.text = placement.Status;
        RefreshControls();
    }
    private void Refresh()
    {
        RefreshControls();
    }
    private void RefreshControls()
    {
        var selected = placement.Selected;
        var target = placement.PreviewMachine != null ? placement.PreviewMachine : selected;
        bool canRotate = target != null && !target.IsReturning && !target.IsSettling;
        rotateLeft.interactable = rotateRight.interactable = canRotate;
        parcel.interactable = can.interactable = placement.CanSpawnProducts;
        foreach (var tab in moduleTabs)
        {
            bool active = target != null && target.Definition.kind == tab.kind;
            if (tab.marker.activeSelf != active) tab.marker.SetActive(active);
            tab.caption.color = active ? accent : Color.white;
        }
    }
    private RectTransform Panel(string name, Transform parent, Color colour)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = colour;
        return go.GetComponent<RectTransform>();
    }
    private Button ProductButton(string title, string resource, float x, float y, float width, UnityEngine.Events.UnityAction action)
    {
        var button = MakeButton(libraryItems, "", x, y, width, ProductPaneHeight, action); button.name = "Add " + title.ToLowerInvariant();
        var picture = Panel(title + " icon", button.transform, Color.white);
        bool isBox = title == "Box";
        float imageSize = isBox ? 56f : 42f;
        Place(picture, (width - imageSize) * 0.5f, (ProductPaneHeight - imageSize) * 0.5f, imageSize, imageSize);
        var graphic = picture.GetComponent<Image>(); graphic.sprite = Resources.Load<Sprite>(resource);
        if (isBox) boxGraphic = graphic; else canGraphic = graphic;
        graphic.preserveAspect = true; graphic.raycastTarget = false;
        return button;
    }
    public void ReloadArtwork()
    {
        if (brandGraphic != null) brandGraphic.sprite = Resources.Load<Sprite>("Studio/ConveyorStudioIcon");
        if (boxGraphic != null) boxGraphic.sprite = Resources.Load<Sprite>("Studio/BoxIcon");
        if (canGraphic != null) canGraphic.sprite = Resources.Load<Sprite>("Studio/CanIcon");
    }
    private Text Label(Transform parent, string value, float x, float y, float w, float h, int size, Color colour, FontStyle style = FontStyle.Normal)
    {
        var go = new GameObject(value.Length > 0 ? value : "Label", typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
        var text = go.GetComponent<Text>(); text.font = font; text.text = value; text.fontSize = size; text.fontStyle = style;
        text.color = colour; text.alignment = TextAnchor.MiddleLeft; text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Truncate;
        Place(go.GetComponent<RectTransform>(), x, y, w, h); return text;
    }
    private Button MakeButton(Transform parent, string title, float x, float y, float w, float h, UnityEngine.Events.UnityAction action)
    {
        var rect = Panel(title.Length == 0 ? "Module card" : title, parent, card); Place(rect, x, y, w, h);
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>();
        var colours = button.colors; colours.normalColor = Color.white; colours.highlightedColor = new Color(1.25f, 1.35f, 1.3f);
        colours.pressedColor = new Color(0.65f, 0.9f, 0.8f); colours.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.5f);
        button.colors = colours; button.onClick.AddListener(action);
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        var label = Label(rect, title, 12, 0, w - 24, h, 12, Color.white); label.alignment = TextAnchor.MiddleCenter;
        return button;
    }
    private static void Place(RectTransform rect, float x, float y, float w, float h)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h);
    }
    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax; }
    private void OnDisable()
    {
        if (placement != null) placement.Changed -= Refresh;
        if (canvas != null) canvas.gameObject.SetActive(false);
        if (placement != null && placement.controlBox != null) placement.controlBox.enabled = false;
        var line = GetComponent<ConveyorLineController>(); if (line != null) line.enabled = false;
    }
    private void OnDestroy() { if (placement != null) placement.Changed -= Refresh; }
}
