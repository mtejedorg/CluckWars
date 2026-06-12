using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Swaps this GameObject's <see cref="MeshFilter"/> mesh for a procedural
    /// placeholder model at <c>Awake</c> (see <see cref="PlaceholderMeshFactory"/>).
    /// Purely visual: colliders, CharacterController, tint (MaterialPropertyBlock)
    /// and the food-pile scale feedback all operate on the same renderer/transform
    /// and are unaffected. Remove this component when real artwork lands.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    public sealed class PlaceholderModel : MonoBehaviour
    {
        [Tooltip("Which placeholder shape replaces this mesh.")]
        [SerializeField] private PlaceholderMeshFactory.Kind _kind = PlaceholderMeshFactory.Kind.Chicken;

        private void Awake()
        {
            var mesh = PlaceholderMeshFactory.Get(_kind);
            if (mesh == null) return;
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }
    }
}
