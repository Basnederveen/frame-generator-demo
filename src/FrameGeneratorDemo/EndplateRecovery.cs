using System.Collections.Generic;
using Inventor;

namespace FrameGeneratorDemo
{
    /// <summary>
    /// Surviving Replacement (Part 3). After Frame Generator swaps a beam's profile,
    /// the old occurrence is gone: the endplate's stored reference key no longer
    /// binds and its constraints sit at kDriverLostHealth. For each orphaned plate
    /// we find the replacement beam, then rebuild its constraints against the new
    /// occurrence and re-capture the reference key forward.
    ///
    /// Every constraint was built against an origin work plane, so each rebuilds by
    /// work-plane index against the new occurrence — stable across a profile change,
    /// with no geometry re-derivation (mirrors FrameStiffener's Replacer.FixBrokenConstraints).
    /// </summary>
    internal sealed class EndplateRecovery
    {
        private readonly Application _inv;
        private readonly AssemblyDocument _asm;

        /// <summary>
        /// A human-readable line per rebuilt constraint, accumulated across every plate
        /// this instance recovers — name, kind, and the two entities it now joins. The
        /// watcher reads it to report exactly what was re-resolved.
        /// </summary>
        public List<string> ResolvedConstraints { get; } = new List<string>();

        /// <summary>Section families the recovered plates were on before this change, and
        /// the ones they were re-resolved onto. Normally one each (one FG change = one new
        /// profile); the watcher shows "old → new".</summary>
        public HashSet<string> OldSections { get; } = new HashSet<string>();
        public HashSet<string> NewSections { get; } = new HashSet<string>();

        public EndplateRecovery(Application inv, AssemblyDocument asm)
        {
            _inv = inv;
            _asm = asm;
        }

        /// <summary>
        /// Rebuild one endplate's broken constraints against its replacement beam.
        /// The watcher resolves <paramref name="newBeam"/> to the top assembly context
        /// (FgBeam.AdjustToContext) before calling — its proxies must be top-level.
        /// </summary>
        public bool Recover(ComponentOccurrence plate, ComponentOccurrence newBeam)
        {
            EndplateTag tag = AttributeStore.ReadEndplate(plate);
            if (tag == null) return false;

            var broken = new List<AssemblyConstraint>();
            foreach (AssemblyConstraint c in AttributeStore.FindConstraints(_asm, tag.DependentId))
                if (c.HealthStatus == HealthStatusEnum.kDriverLostHealth) broken.Add(c);
            Diag.Log("recover(" + plate.Name + "): " + broken.Count + " broken constraint(s) vs " + newBeam.Name);
            if (broken.Count == 0) return false;

            if (RebuildConstraints(plate, newBeam, broken))
            {
                // Report old (stored) vs new section, then roll the tag forward (key +
                // section) so the next change reads this section as its "old".
                string oldSection = tag.BeamSection;
                string newSection = FgBeam.SectionFamily(newBeam);
                if (!string.IsNullOrEmpty(oldSection)) OldSections.Add(oldSection);
                if (!string.IsNullOrEmpty(newSection)) NewSections.Add(newSection);
                Diag.Log("recover(" + plate.Name + "): section "
                    + (string.IsNullOrEmpty(oldSection) ? "(unknown)" : oldSection) + " -> "
                    + (string.IsNullOrEmpty(newSection) ? "(unknown)" : newSection));

                tag.BeamReferenceKey = ReferenceKeys.Capture((Document)_asm, newBeam);
                tag.BeamSection = newSection;
                AttributeStore.Write(plate, tag);
                return true;
            }
            return false;
        }

        private bool RebuildConstraints(ComponentOccurrence plate, ComponentOccurrence newBeam,
            List<AssemblyConstraint> broken)
        {
            AssemblyConstraints constraints = _asm.ComponentDefinition.Constraints;
            bool any = false;

            foreach (AssemblyConstraint c in broken)
            {
                ConstraintTag tag = AttributeStore.ReadConstraint(c);
                if (tag == null) continue;
                c.Delete();

                AssemblyConstraint rebuilt = tag.Kind switch
                {
                    ConstraintKind.PlaneFlush => (AssemblyConstraint)constraints.AddFlushConstraint(
                        FgBeam.PlaneProxy(plate, tag.Axis), FgBeam.PlaneProxy(newBeam, tag.Axis), 0.0),
                    ConstraintKind.PlaneMate => (AssemblyConstraint)constraints.AddMateConstraint(
                        FgBeam.PlaneProxy(plate, tag.Axis), FgBeam.PlaneProxy(newBeam, tag.Axis), 0.0),
                    _ => null
                };

                if (rebuilt != null)
                {
                    AttributeStore.Write(rebuilt, tag);
                    ResolvedConstraints.Add(Describe(rebuilt, tag, plate, newBeam));
                    any = true;
                }
            }
            return any;
        }

        // e.g. "Mate:7 — XY mate: <plate> XY plane ↔ <beam> XY plane". Name is the
        // browser name of the new constraint; the entities are the two origin planes
        // it now joins, so a reader can find both in the model.
        private static string Describe(AssemblyConstraint rebuilt, ConstraintTag tag,
            ComponentOccurrence plate, ComponentOccurrence newBeam)
        {
            string kind = tag.Kind == ConstraintKind.PlaneFlush ? "flush" : "mate";
            return rebuilt.Name + " — " + tag.Axis + " " + kind + ": "
                + FgBeam.Native(plate).Name + " " + tag.Axis + " plane ↔ "
                + FgBeam.Native(newBeam).Name + " " + tag.Axis + " plane";
        }
    }
}
