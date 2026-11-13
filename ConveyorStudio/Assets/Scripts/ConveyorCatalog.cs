using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Conveyor Studio/Module catalogue")]
public sealed class ConveyorCatalog : ScriptableObject
{
    [Serializable]
    public sealed class Module
    {
        public ConveyorKind kind;
        public string title;
        public string description;
        public GameObject prefab;
        public Sprite thumbnail;
    }
    public Module[] modules;
    public GameObject productPrefab;
    public GameObject canPrefab;
    public Material validMaterial, invalidMaterial, lineMaterial;

    public Module Get(ConveyorKind kind)
    {
        foreach (var item in modules) if (item.kind == kind) return item;
        throw new ArgumentException("No module registered for " + kind);
    }
}
