using System.Collections.Generic;
using UnityEngine;

// A machine contributes all its parts to one screen-space silhouette. The source
// renderers keep their original materials, lighting and moving geometry.
[DisallowMultipleComponent]
public sealed class ConveyorSilhouette : MonoBehaviour
{
    private static readonly List<ConveyorSilhouette> active = new List<ConveyorSilhouette>();
    public static IReadOnlyList<ConveyorSilhouette> Active => active;
    public MeshRenderer[] Renderers { get; private set; }
    public bool Highlighted { get; private set; }
    public Color Colour { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => active.Clear();
    private void Awake() => Renderers = GetComponentsInChildren<MeshRenderer>(true);
    public void RefreshRenderers() => Renderers = GetComponentsInChildren<MeshRenderer>(true);
    private void OnEnable() { if (!active.Contains(this)) active.Add(this); }
    private void OnDisable() => active.Remove(this);
    public void SetHighlight(bool show, Color colour) { Highlighted = show; Colour = colour; }
}
