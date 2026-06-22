using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Inventor;
using Application = Inventor.Application;

namespace FrameGeneratorDemo
{
    /// <summary>
    /// Add-in entry point. Inventor CoCreateInstance's this class (matched to the
    /// ClassId in FrameGeneratorDemo.addin) on startup, calls Activate once, and
    /// Deactivate on unload.
    ///
    /// Responsibilities, kept deliberately small for a teaching demo:
    ///   - add one ribbon button ("Place Endplate") to the assembly Assemble tab;
    ///   - start a <see cref="FrameEditWatcher"/> that listens for Frame Generator
    ///     profile changes and re-resolves any endplate constraints they break.
    /// </summary>
    [ComVisible(true)]
    [Guid("6F9A1C2E-7B3D-4E5A-8C1F-2A3B4C5D6E7F")]
    [ProgId("FrameGeneratorDemo.StandardAddInServer")]
    public class StandardAddInServer : ApplicationAddInServer
    {
        // The ClientId string Inventor uses to scope our UI; must match the .addin ClassId.
        private const string ClientId = "{6F9A1C2E-7B3D-4E5A-8C1F-2A3B4C5D6E7F}";

        private Application _inv;
        private ButtonDefinition _placeEndplateButton;
        private FrameEditWatcher _watcher;

        public void Activate(ApplicationAddInSite addInSiteObject, bool firstTime)
        {
            _inv = addInSiteObject.Application;

            // --- Ribbon button ---------------------------------------------------
            ControlDefinitions controlDefs = _inv.CommandManager.ControlDefinitions;
            _placeEndplateButton = controlDefs.AddButtonDefinition(
                "Place\nEndplate",
                "FGDemo:PlaceEndplate",
                CommandTypesEnum.kShapeEditCmdType,
                ClientId,
                "Place a demo endplate on a Frame Generator beam end.",
                "Places an endplate, constrains it to the beam, and tags it so the " +
                "constraint is rebuilt automatically when the beam profile changes.");
            _placeEndplateButton.OnExecute += PlaceEndplateButton_OnExecute;

            if (firstTime)
            {
                AddButtonToAssemblyRibbon();
            }

            // --- Frame Generator edit watcher (Part 2 + Part 3) ------------------
            // Always listening: when the user changes a beam profile anywhere, the
            // watcher re-resolves endplate constraints with no further interaction.
            _watcher = new FrameEditWatcher(_inv);
            _watcher.Start();
        }

        public void Deactivate()
        {
            _watcher?.Stop();
            _watcher = null;

            if (_placeEndplateButton != null)
            {
                _placeEndplateButton.OnExecute -= PlaceEndplateButton_OnExecute;
                _placeEndplateButton = null;
            }

            Marshal.ReleaseComObject(_inv);
            _inv = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        // Obsolete but part of the interface contract.
        public void ExecuteCommand(int commandID) { }

        public object Automation => null;

        private void AddButtonToAssemblyRibbon()
        {
            try
            {
                Ribbon assemblyRibbon = _inv.UserInterfaceManager.Ribbons["Assembly"];
                RibbonTab assembleTab = assemblyRibbon.RibbonTabs["id_TabAssemble"];
                RibbonPanel panel = assembleTab.RibbonPanels.Add(
                    "Frame Generator Demo", "FGDemo:Panel", ClientId);
                panel.CommandControls.AddButton(_placeEndplateButton, true);
            }
            catch (Exception ex)
            {
                // Non-fatal: the command still works from Tools > Macros / the command list.
                System.Diagnostics.Debug.WriteLine("FGDemo: ribbon setup failed: " + ex.Message);
            }
        }

        private void PlaceEndplateButton_OnExecute(NameValueMap context)
        {
            try
            {
                if (!(_inv.ActiveDocument is AssemblyDocument asm))
                {
                    MessageBox.Show("Open an assembly that contains a Frame Generator frame first.",
                        "Frame Generator Demo");
                    return;
                }

                // Let the user pick the Frame Generator beam to place the endplate on.
                // Placement constrains to the beam's origin work planes, so we only need
                // to know which beam — not a face. A leaf-occurrence filter selects the
                // beam itself even though Frame Generator nests it in a frame sub-assembly.
                object picked = _inv.CommandManager.Pick(
                    SelectionFilterEnum.kAssemblyLeafOccurrenceFilter,
                    "Select a Frame Generator beam");
                if (picked == null) return; // Escape

                if (!(picked is ComponentOccurrence beamOcc) || !FgBeam.IsFrameGeneratorBeam(beamOcc))
                {
                    MessageBox.Show("The selected component isn't a Frame Generator beam " +
                        "(no com.autodesk.FG attribute set).", "Frame Generator Demo");
                    return;
                }

                new EndplatePlacer(_inv, asm).PlaceOn(beamOcc);
            }
            catch (Exception ex)
            {
                // Full type + message + stack (Debug PDB ships in the bundle, so the
                // stack has line numbers) → VS Output (Debug) window, copy-pasteable.
                Diag.Log("PLACE FAILED: " + ex);
            }
        }
    }
}
