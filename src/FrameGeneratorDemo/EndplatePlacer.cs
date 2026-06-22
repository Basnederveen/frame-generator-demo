using System;
using System.Collections.Generic;
using Inventor;

namespace FrameGeneratorDemo
{
    /// <summary>
    /// Places a demo endplate on a beam end and constrains it the same way a real
    /// frame add-in (FrameStiffener) does: purely against the beam's three origin
    /// work planes. Two planes centre the plate on the beam's cross-section; the third
    /// (XY, perpendicular to the beam axis) seats it at the beam's origin end. Every
    /// object we create is tagged (see <see cref="AttributeStore"/>) so
    /// <see cref="EndplateRecovery"/> can find and rebuild it after a profile change.
    ///
    /// Using origin planes — not the picked end face — for every constraint is the whole
    /// point: each one rebuilds by work-plane index after the beam is replaced, with no
    /// geometry heuristic. The trade-off is that the plate lands at the beam's origin end
    /// rather than the exact face clicked; orientation/size aren't the demo's point, the
    /// constraint lifecycle is. The plate is a fixed 150 × 150 × 10 mm slab built in code.
    /// </summary>
    internal sealed class EndplatePlacer
    {
        // Inventor works in centimetres internally, regardless of document units.
        private const double PlateWidthCm = 15.0;   // 150 mm
        private const double PlateHeightCm = 15.0;   // 150 mm
        private const double PlateThicknessCm = 1.0; // 10 mm

        private readonly Application _inv;
        private readonly AssemblyDocument _asm;

        public EndplatePlacer(Application inv, AssemblyDocument asm)
        {
            _inv = inv;
            _asm = asm;
        }

        // The caller's face pick only identifies the beam (beamOcc); positioning is
        // entirely origin-plane based, so the face geometry is never used here.
        public ComponentOccurrence PlaceOn(ComponentOccurrence beamOcc)
        {
            string dependentId = Guid.NewGuid().ToString();
            AssemblyComponentDefinition asmDef = _asm.ComponentDefinition;
            Diag.Log("PlaceOn start, dependentId=" + dependentId);

            // 1) Build the plate as a saved .ipt and add it by filename. Saving (vs
            //    AddByComponentDefinition on an unsaved doc that's then closed) keeps
            //    the occurrence backed by real geometry — an orphaned unsaved part is
            //    the "empty Part4 with no extrusion" trap.
            _inv.StatusBarText = "Frame Generator Demo: building plate…";
            string platePath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "fgdemo-endplate-" + dependentId + ".ipt");
            CreatePlatePart(platePath);
            Diag.Log("built plate -> " + platePath);

            _inv.StatusBarText = "Frame Generator Demo: adding plate to the assembly…";
            Matrix identity = _inv.TransientGeometry.CreateMatrix();
            ComponentOccurrence plateOcc = asmDef.Occurrences.Add(platePath, identity);
            Diag.Log("added occurrence: " + plateOcc.Name);

            // 2) Tag the plate up front (beam reference key + current section) so
            //    recovery can find it even if a constraint below fails to add.
            _inv.StatusBarText = "Frame Generator Demo: tagging plate…";
            AttributeStore.Write(plateOcc, new EndplateTag
            {
                DependentId = dependentId,
                BeamReferenceKey = ReferenceKeys.Capture((Document)_asm, beamOcc),
                BeamSection = FgBeam.SectionFamily(beamOcc),
            });

            // 3) Constrain the plate against the beam's three origin work planes — the
            //    same recipe FrameStiffener uses, so every constraint rebuilds by index.
            //    Each is independent — one failure is reported but never aborts the others.
            //      YZ flush : same-facing origin planes (centre)
            //      XZ mate  : opposing origin planes (centre)
            //      XY mate  : planes perpendicular to the beam axis — seats the plate at
            //                 the beam's origin end (replaces the old end-face mate)
            _inv.StatusBarText = "Frame Generator Demo: constraining…";
            var failures = new List<string>();
            AddConstraint(failures, dependentId, "YZ flush", ConstraintKind.PlaneFlush, PlaneAxis.YZ,
                () => asmDef.Constraints.AddFlushConstraint(FgBeam.PlaneProxy(plateOcc, PlaneAxis.YZ), FgBeam.PlaneProxy(beamOcc, PlaneAxis.YZ), 0.0));
            AddConstraint(failures, dependentId, "XZ mate", ConstraintKind.PlaneMate, PlaneAxis.XZ,
                () => asmDef.Constraints.AddMateConstraint(FgBeam.PlaneProxy(plateOcc, PlaneAxis.XZ), FgBeam.PlaneProxy(beamOcc, PlaneAxis.XZ), 0.0));
            AddConstraint(failures, dependentId, "XY mate", ConstraintKind.PlaneMate, PlaneAxis.XY,
                () => asmDef.Constraints.AddMateConstraint(FgBeam.PlaneProxy(plateOcc, PlaneAxis.XY), FgBeam.PlaneProxy(beamOcc, PlaneAxis.XY), 0.0));

            _inv.StatusBarText = "Frame Generator Demo: endplate placed.";
            Diag.Log(failures.Count == 0
                ? "endplate placed, all constraints OK"
                : "endplate placed, " + failures.Count + " constraint(s) failed (see above)");
            return plateOcc;
        }

        // Create one tagged constraint; a failure is recorded, not thrown, so the rest
        // (and the plate's own tags) survive.
        private void AddConstraint(List<string> failures, string dependentId, string label,
            ConstraintKind kind, PlaneAxis axis, Func<object> create)
        {
            try
            {
                var c = (AssemblyConstraint)create();
                AttributeStore.Write(c, new ConstraintTag { DependentId = dependentId, Axis = axis, Kind = kind });
                Diag.Log("constraint OK: " + label);
            }
            catch (Exception ex)
            {
                Diag.Log("constraint FAILED: " + label + " -> " + ex);
                failures.Add(label + ": " + ex.GetType().Name + " — " + ex.Message);
            }
        }

        // Builds the demo plate (a sketched rectangle extruded to a slab) in a new
        // part document, saves it to savePath, and closes it. Returns nothing — the
        // caller adds the saved file to the assembly.
        private void CreatePlatePart(string savePath)
        {
            string template = _inv.FileManager.GetTemplateFile(
                DocumentTypeEnum.kPartDocumentObject,
                SystemOfMeasureEnum.kMetricSystemOfMeasure);
            var doc = (PartDocument)_inv.Documents.Add(DocumentTypeEnum.kPartDocumentObject, template, false);
            PartComponentDefinition def = doc.ComponentDefinition;
            TransientGeometry tg = _inv.TransientGeometry;

            // Rectangle centred on the origin, sketched on the XY origin plane.
            PlanarSketch sketch = def.Sketches.Add(def.WorkPlanes[3]); // [3] = XY
            sketch.SketchLines.AddAsTwoPointRectangle(
                tg.CreatePoint2d(-PlateWidthCm / 2, -PlateHeightCm / 2),
                tg.CreatePoint2d(PlateWidthCm / 2, PlateHeightCm / 2));

            Profile profile = sketch.Profiles.AddForSolid();
            // kJoinOperation is the canonical base-feature op (Inventor's own extrude
            // sample uses it for the first solid).
            ExtrudeDefinition ed = def.Features.ExtrudeFeatures.CreateExtrudeDefinition(
                profile, PartFeatureOperationEnum.kJoinOperation);
            ed.SetDistanceExtent(PlateThicknessCm, PartFeatureExtentDirectionEnum.kPositiveExtentDirection);
            def.Features.ExtrudeFeatures.Add(ed);
            doc.Update();

            doc.SaveAs(savePath, false); // real file → the occurrence won't orphan
            doc.Close(true);
        }
    }
}
