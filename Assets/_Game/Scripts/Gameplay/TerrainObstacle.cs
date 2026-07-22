using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The terrain vocabulary (ADR 0003 Decision 5). Traversal abilities read this
    /// tag to decide what they can cross; the obstacle's height is the physical and
    /// visual expression of the class, never its definition.
    ///
    /// Backing type is <c>byte</c> to match the project's other gameplay enums and to
    /// stay cheap if a future slice ever needs to put it on the wire. Order is stable —
    /// append new classes at the end, do NOT renumber.
    /// </summary>
    public enum ObstacleClass : byte
    {
        /// <summary>Crate / low fence. Blocks running; Vault, Barge and Blink all clear it.</summary>
        Low      = 0,
        /// <summary>Wall segment — the pre-ADR default. Vault and Blink clear it; Barge does not.</summary>
        Standard = 1,
        /// <summary>Rock / silo. Only Blink answers it.</summary>
        Tall     = 2,
    }

    /// <summary>
    /// Tags a piece of generated interior geometry with its <see cref="ObstacleClass"/>.
    /// Slice 2's Vault / Barge / Blink query <see cref="Class"/> — discrimination is by
    /// this tag and never inferred from the collider's height, so re-tuning a class's
    /// height can never silently change what can traverse it.
    /// </summary>
    /// <remarks>
    /// Deliberately a plain <see cref="MonoBehaviour"/>, not a <c>NetworkBehaviour</c>:
    /// interior obstacles are LOCAL geometry built identically on every peer from a
    /// session-seeded RNG (see <see cref="MapGenerator"/>), so there is no state to
    /// replicate.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TerrainObstacle : MonoBehaviour
    {
        [Tooltip("Which terrain class this obstacle belongs to. Traversal abilities query this tag, not the object's height.")]
        [SerializeField] private ObstacleClass _class = ObstacleClass.Standard;

        /// <summary>The terrain class traversal abilities test against.</summary>
        public ObstacleClass Class => _class;

        /// <summary>
        /// Set by <see cref="MapGenerator"/> immediately after the primitive is created.
        /// Hand-placed obstacles can author <c>_class</c> in the inspector instead.
        /// </summary>
        public void SetClass(ObstacleClass value) => _class = value;
    }
}
