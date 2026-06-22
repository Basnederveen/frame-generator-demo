using Inventor;

namespace FrameGeneratorDemo
{
    /// <summary>
    /// Frame Generator beam identification (Part 1: The Data Model).
    ///
    /// Frame Generator stamps every frame member with the "com.autodesk.FG"
    /// attribute set. We use its presence to tell "this occurrence is a beam" from
    /// "this is some other component". The one gotcha from Part 1: in event
    /// handlers Inventor hands you an occurrence *proxy*, and attribute lookups must
    /// go through the native occurrence — so always normalise with Native() first.
    /// </summary>
    internal static class FgBeam
    {
        /// <summary>
        /// Resolve a possibly-proxy occurrence (as delivered by assembly events) to
        /// the native occurrence its attribute sets actually live on.
        /// </summary>
        public static ComponentOccurrence Native(ComponentOccurrence occ)
        {
            return occ is ComponentOccurrenceProxy proxy
                ? (ComponentOccurrence)proxy.NativeObject
                : occ;
        }

        /// <summary>
        /// Express an event-delivered occurrence in the TOP assembly's context. Frame
        /// edit events hand the beam back in the frame sub-assembly's context; a work
        /// plane / face proxy built from that is rejected by a constraint added at the
        /// top level (E_FAIL). Match it to the top-level leaf occurrence with the same
        /// native object — a direct port of FrameStiffener's AdjustOccurrenceToContext.
        /// </summary>
        public static ComponentOccurrence AdjustToContext(ComponentOccurrence occ, AssemblyDocument context)
        {
            ComponentOccurrence native = Native(occ);
            foreach (ComponentOccurrence leaf in context.ComponentDefinition.Occurrences.AllLeafOccurrences)
                if (native == Native(leaf)) return leaf;
            return null;
        }

        public static bool IsFrameGeneratorBeam(ComponentOccurrence occ)
        {
            ComponentOccurrence native = Native(occ);
            AttributeSets sets = native.AttributeSets;
            if (!sets.NameIsUsed[AttributeStore.FrameGeneratorSet]) return false;

            // FG uses the same set on a few member kinds; "FrameMemberType" is the beam.
            AttributeSet fg = sets[AttributeStore.FrameGeneratorSet];
            return !fg.NameIsUsed["Type"] || (string)fg["Type"].Value == "FrameMemberType";
        }

        /// <summary>
        /// The beam's section family as a human-readable designation, e.g. "HE 100 B".
        /// Read from the <b>Stock Number</b> iProperty (the standard "Design Tracking
        /// Properties" set), which Frame Generator fills with the profile name — readable,
        /// unlike FG's internal FamilyId GUID, and shared by members of the same section
        /// regardless of length, so it answers "what is the target section?". This is the
        /// teaching point of Part 3's tail: once recovery has resolved the *new* beam, you
        /// can read its properties. Returns "" if the property isn't present.
        /// </summary>
        public static string SectionFamily(ComponentOccurrence occ)
        {
            try
            {
                var def = (PartComponentDefinition)Native(occ).Definition;
                PropertySet ps = ((Document)def.Document).PropertySets["Design Tracking Properties"];
                return ps["Stock Number"].Value.ToString();
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// The first three work planes of a part definition are its origin planes,
        /// 1-based: WorkPlanes[1]=YZ, [2]=XZ, [3]=XY. We constrain endplates against
        /// these (not user geometry) precisely because their index is stable across
        /// a profile change — which is what lets recovery rebuild by index alone.
        /// </summary>
        public static WorkPlane OriginPlane(ComponentOccurrence occ, PlaneAxis axis)
        {
            var def = (PartComponentDefinition)Native(occ).Definition;
            return def.WorkPlanes[(int)axis];
        }

        public static object PlaneProxy(ComponentOccurrence occ, PlaneAxis axis)
        {
            occ.CreateGeometryProxy(OriginPlane(occ, axis), out object proxy);
            return proxy;
        }
    }
}
