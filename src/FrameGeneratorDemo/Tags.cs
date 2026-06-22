namespace FrameGeneratorDemo
{
    // ─────────────────────────────────────────────────────────────────────────
    //  DATA ONLY. No Inventor types here on purpose: these are the plain shapes
    //  we persist on model objects, and a consumer of this example can reuse them
    //  (or model their own attributes on them) without referencing Inventor. The
    //  read/write logic lives separately in AttributeStore.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Which origin work plane a constraint was built against. The value is the
    /// 1-based index into a part's WorkPlanes collection: YZ=1, XZ=2, XY=3 — which
    /// is exactly why recovery can rebuild by index across a profile change.
    /// </summary>
    public enum PlaneAxis { YZ = 1, XZ = 2, XY = 3 }

    /// <summary>
    /// How a constraint is rebuilt after the beam it referenced is replaced. Every
    /// constraint is built against an origin work plane, so recovery recreates it
    /// against the new beam's origin work plane <see cref="ConstraintTag.Axis"/> with
    /// the same type — a pure index rebuild, no geometry re-derivation.
    /// </summary>
    public enum ConstraintKind { PlaneFlush = 0, PlaneMate = 1 }

    /// <summary>
    /// Identity + recovery data stored on a placed endplate occurrence.
    /// </summary>
    public sealed class EndplateTag
    {
        /// <summary>GUID shared by this endplate and every constraint we created for it.</summary>
        public string DependentId { get; set; }

        /// <summary>Stringified Inventor reference key of the parent beam ("context|key").</summary>
        public string BeamReferenceKey { get; set; }

        /// <summary>The parent beam's Frame Generator section family at placement, rolled
        /// forward on each recovery — so we can report the old section the plate was on
        /// versus the new one it was re-resolved onto.</summary>
        public string BeamSection { get; set; }
    }

    /// <summary>
    /// Recovery data stored on each constraint we create — enough to rebuild it from scratch.
    /// </summary>
    public sealed class ConstraintTag
    {
        /// <summary>Matches the owning <see cref="EndplateTag.DependentId"/>.</summary>
        public string DependentId { get; set; }

        /// <summary>Origin work plane the constraint used (for the plane kinds).</summary>
        public PlaneAxis Axis { get; set; }

        /// <summary>How to rebuild this constraint.</summary>
        public ConstraintKind Kind { get; set; }
    }
}
