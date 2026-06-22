using System.Collections.Generic;
using System.Text.Json;
using Inventor;

namespace FrameGeneratorDemo
{
    /// <summary>
    /// The only place that talks to Inventor attribute sets — all serialization and
    /// FindObjects queries live here, separate from the data shapes in Tags.cs.
    ///
    /// Each tagged object stores its POCO as a single JSON string in one attribute
    /// (set "fgdemo.endplate" / "fgdemo.constraint", attribute "tag"). Attribute sets
    /// ride along with the model object and survive save/load, so the JSON is there
    /// next session — that's what lets recovery find dependents by attribute search
    /// after Frame Generator destroys the beam they pointed at.
    /// </summary>
    public static class AttributeStore
    {
        /// <summary>Frame Generator's own attribute set, present on every frame member (read-only here).</summary>
        public const string FrameGeneratorSet = "com.autodesk.FG";

        private const string EndplateSet = "fgdemo.endplate";
        private const string ConstraintSet = "fgdemo.constraint";
        private const string TagAttribute = "tag";

        // ── write ────────────────────────────────────────────────────────────
        public static void Write(ComponentOccurrence plate, EndplateTag tag)
            => WriteJson(plate.AttributeSets, EndplateSet, tag);

        public static void Write(AssemblyConstraint constraint, ConstraintTag tag)
            => WriteJson(constraint.AttributeSets, ConstraintSet, tag);

        // ── read ─────────────────────────────────────────────────────────────
        public static EndplateTag ReadEndplate(ComponentOccurrence plate)
            => ReadJson<EndplateTag>(plate.AttributeSets, EndplateSet);

        public static ConstraintTag ReadConstraint(AssemblyConstraint constraint)
            => ReadJson<ConstraintTag>(constraint.AttributeSets, ConstraintSet);

        // ── query ────────────────────────────────────────────────────────────
        /// <summary>Every endplate this add-in placed in the assembly.</summary>
        public static IEnumerable<ComponentOccurrence> FindEndplates(AssemblyDocument asm)
        {
            foreach (object o in asm.AttributeManager.FindObjects(EndplateSet, TagAttribute))
                if (o is ComponentOccurrence occ) yield return occ;
        }

        /// <summary>Every constraint tagged for a given dependent GUID.</summary>
        public static IEnumerable<AssemblyConstraint> FindConstraints(AssemblyDocument asm, string dependentId)
        {
            foreach (object o in asm.AttributeManager.FindObjects(ConstraintSet, TagAttribute))
                if (o is AssemblyConstraint c)
                {
                    ConstraintTag tag = ReadConstraint(c);
                    if (tag != null && tag.DependentId == dependentId) yield return c;
                }
        }

        // ── serialization plumbing ─────────────────────────────────────────────
        private static void WriteJson(AttributeSets sets, string setName, object value)
        {
            AttributeSet set = sets.NameIsUsed[setName] ? sets[setName] : sets.Add(setName);
            string json = JsonSerializer.Serialize(value);
            if (set.NameIsUsed[TagAttribute]) set[TagAttribute].Value = json;
            else set.Add(TagAttribute, ValueTypeEnum.kStringType, json);
        }

        private static T ReadJson<T>(AttributeSets sets, string setName) where T : class
        {
            if (!sets.NameIsUsed[setName]) return null;
            AttributeSet set = sets[setName];
            if (!set.NameIsUsed[TagAttribute]) return null;
            return JsonSerializer.Deserialize<T>((string)set[TagAttribute].Value);
        }
    }
}
