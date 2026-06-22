using System.Reflection;
using Inventor;

namespace FrameGeneratorDemo
{
    /// <summary>
    /// Reference-key persistence (Part 1) and the forward-rebuild it enables (Part 3).
    ///
    /// A reference key is an opaque, save/load-stable handle to a model object,
    /// resolved through the document's ReferenceKeyManager. We stringify the parent
    /// beam's key and stash it on the endplate, so on a later edit we can ask "which
    /// beam did this plate belong to?". The catch (Part 3): once Frame Generator
    /// replaces the beam occurrence on a profile change, the old key's
    /// BindKeyToObject returns null — the occurrence it pointed at no longer exists.
    /// Recovery therefore rebuilds the key *forward* against the new occurrence; it
    /// never tries to recover the old one backward.
    ///
    /// We pack the key and its context into one string ("context|key") so a single
    /// attribute value round-trips everything BindKeyToObject needs.
    /// </summary>
    internal static class ReferenceKeys
    {
        private const char Sep = '|';

        /// <summary>Capture a stable, stringified reference key for <paramref name="obj"/>.</summary>
        public static string Capture(Document doc, ComponentOccurrence obj)
        {
            ReferenceKeyManager rkm = doc.ReferenceKeyManager;
            int context = rkm.CreateKeyContext();

            byte[] key = GetReferenceKey(obj, context);

            // ref byte[] args must be a real array, not null, or the interop mismarshals
            // (same DISP_E_TYPEMISMATCH class as GetReferenceKey) — match FrameStiffener.
            byte[] contextData = new byte[2];
            rkm.SaveContextToArray(context, ref contextData);

            return rkm.KeyToString(contextData) + Sep + rkm.KeyToString(key);
        }

        /// <summary>
        /// Calls ComponentOccurrence.GetReferenceKey(ByRef Byte(), KeyContext). A direct
        /// interop call throws DISP_E_TYPEMISMATCH because the ByRef byte-array argument
        /// mismarshals, so we invoke it late-bound with a ParameterModifier marking the
        /// first argument as by-ref. This mirrors FrameStiffener's ReferenceKey.cs verbatim.
        /// </summary>
        private static byte[] GetReferenceKey(ComponentOccurrence obj, int context)
        {
            var refkey = new byte[2];
            var args = new object[2] { refkey, context };
            var mods = new ParameterModifier[2] { new ParameterModifier(1), new ParameterModifier(1) };
            mods[0][0] = true;
            obj.GetType().InvokeMember(
                "GetReferenceKey", BindingFlags.InvokeMethod, null, obj, args, mods, null, null);
            return (byte[])args[0];
        }

        /// <summary>
        /// Resolve a captured key back to a live occurrence, or null if it no longer
        /// binds (the Part 3 signal that the beam was replaced).
        /// </summary>
        public static ComponentOccurrence Bind(Document doc, string captured)
        {
            if (string.IsNullOrEmpty(captured)) return null;
            int sep = captured.IndexOf(Sep);
            if (sep < 0) return null;

            ReferenceKeyManager rkm = doc.ReferenceKeyManager;

            byte[] contextData = new byte[2];
            rkm.StringToKey(captured.Substring(0, sep), ref contextData);
            int context = rkm.LoadContextFromArray(contextData);

            byte[] key = new byte[2];
            rkm.StringToKey(captured.Substring(sep + 1), ref key);

            return rkm.BindKeyToObject(ref key, context, out _) as ComponentOccurrence;
        }
    }
}
