namespace CluckWars.Gameplay
{
    /// <summary>
    /// One-shot marker for a <see cref="ChickenCargo.Cargo"/> write that the local cargo pop
    /// (<c>+5 🌽</c> / <c>-5 🌽</c>) must not announce. Lab tooling only: the Ability Lab
    /// dummy refills its cargo after a steal, and without this the refill painted a green
    /// <c>+5</c> over the robbed victim in every steal frame.
    /// </summary>
    /// <remarks>
    /// Purely local and presentation-side: the cargo write itself replicates exactly as any
    /// other. The marker covers at most the first <see cref="ChickenCargo.Render"/> pass that
    /// runs on a later frame than the write, so a real change after that is never swallowed
    /// even when the silent write was never observed (for example, overwritten in the same frame).
    /// </remarks>
    public struct SilentCargoWrite
    {
        private bool _armed;
        private int _armedFrame;

        public bool IsArmed => _armed;

        /// <summary>Marks the write made on <paramref name="frame"/> as silent.</summary>
        public void Arm(int frame)
        {
            _armed = true;
            _armedFrame = frame;
        }

        /// <summary>
        /// Called for a Cargo change seen by the change detector. True when that change is the
        /// silent write (the pop and the loss beat are skipped); disarms either way.
        /// </summary>
        public bool ConsumeChange()
        {
            if (!_armed) return false;
            _armed = false;
            return true;
        }

        /// <summary>
        /// Called at the end of every Render pass. A pass on a later frame than the write has
        /// already seen it (or never will), so the marker expires there.
        /// </summary>
        public void EndRender(int frame)
        {
            if (_armed && frame > _armedFrame) _armed = false;
        }
    }
}
