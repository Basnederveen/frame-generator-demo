using System;
using System.Collections.Generic;
using Inventor;
using Application = Inventor.Application;

namespace FrameGeneratorDemo
{
    /// <summary>
    /// Tracking edits (Part 2) — a direct port of FrameStiffener's FrameEditEvents,
    /// with its StiffenerFinder/Replacer/Updater swapped for the endplate equivalents.
    /// The event/transaction wiring is intentionally identical to the proven add-in:
    ///
    ///   OnActivateCommand("AFG_FrameMember_Edit" | "AFG_CMD_ChangeProfile")
    ///     → subscribe OnOccurrenceChange, OnNewOccurrence, OnDelete.
    ///   OnDelete (kBefore)              → capture our endplates on the doomed beam.
    ///   OnNewOccurrence (kAfter)        → a re-created beam: pair the endplates with it,
    ///                                     subscribe OnCommit(OnCommitNew), replacing = true.
    ///   OnOccurrenceChange (kAfter,"Replace") → a profile swap: record the new beam,
    ///                                     subscribe OnCommit(OnCommitReplace), replacing = true.
    ///   OnCommit "Change Frame" (kAfter, replacing) → rebuild, then clear.
    ///   OnTerminateCommand             → unsubscribe + clear the lists.
    /// </summary>
    internal sealed class FrameEditWatcher
    {
        private readonly Application _inv;

        private AssemblyEvents _assemblyEvents;
        private UserInputEvents _userInputEvents;
        private TransactionEvents _transactionEvents;

        private bool _replacing;
        // Section families the recovered plates were on before / after this commit. Frame
        // Generator applies one new profile across all members edited in a single change,
        // so each set normally holds exactly ONE value — we show "old → new" to make the
        // "we can read information off the replacement beam" point concrete.
        private readonly HashSet<string> _oldSections = new HashSet<string>();
        private readonly HashSet<string> _newSections = new HashSet<string>();
        // Every constraint rebuilt this commit (name + entities), for the popup report.
        private readonly List<string> _resolvedConstraints = new List<string>();
        private readonly Dictionary<string, ComponentOccurrence> _newBeams = new Dictionary<string, ComponentOccurrence>();
        private readonly List<ComponentOccurrence> _toBeReplacedEndplates = new List<ComponentOccurrence>();
        private readonly List<KeyValuePair<ComponentOccurrence, ComponentOccurrence>> _toBeReplacedOccurrences
            = new List<KeyValuePair<ComponentOccurrence, ComponentOccurrence>>();

        public FrameEditWatcher(Application inv) => _inv = inv;

        public void Start()
        {
            _assemblyEvents = _inv.AssemblyEvents;
            _userInputEvents = _inv.CommandManager.UserInputEvents;
            _transactionEvents = _inv.TransactionManager.TransactionEvents;

            _userInputEvents.OnActivateCommand += UserInputEvents_OnActivateCommand;
            _userInputEvents.OnTerminateCommand += UserInputEvents_OnTerminateCommand;
        }

        public void Stop()
        {
            try { _userInputEvents.OnActivateCommand -= UserInputEvents_OnActivateCommand; } catch { }
            try { _userInputEvents.OnTerminateCommand -= UserInputEvents_OnTerminateCommand; } catch { }
            try { _assemblyEvents.OnOccurrenceChange -= AssemblyEvents_OnOccurrenceChange; } catch { }
            try { _assemblyEvents.OnNewOccurrence -= AssemblyEvents_OnNewOccurrence; } catch { }
            try { _assemblyEvents.OnDelete -= AssemblyEvents_OnDelete; } catch { }
            try { _transactionEvents.OnCommit -= TransactionEvents_OnCommitNew; } catch { }
            try { _transactionEvents.OnCommit -= TransactionEvents_OnCommitReplace; } catch { }
            _assemblyEvents = null;
            _userInputEvents = null;
            _transactionEvents = null;
        }

        private void UserInputEvents_OnActivateCommand(string CommandName, NameValueMap Context)
        {
            SetEditingFrameStatus(CommandName, true);
        }

        private void UserInputEvents_OnTerminateCommand(string CommandName, NameValueMap Context)
        {
            SetEditingFrameStatus(CommandName, false);
            _toBeReplacedEndplates.Clear();
            _toBeReplacedOccurrences.Clear();
            _newBeams.Clear();
        }

        private void SetEditingFrameStatus(string CommandName, bool editingFrame)
        {
            if (CommandName == "AFG_FrameMember_Edit" || CommandName == "AFG_CMD_ChangeProfile")
            {
                Diag.Log("watcher: " + (editingFrame ? "editing" : "done editing") + " (" + CommandName + ")");
                if (editingFrame)
                {
                    _assemblyEvents.OnOccurrenceChange += AssemblyEvents_OnOccurrenceChange;
                    _assemblyEvents.OnNewOccurrence += AssemblyEvents_OnNewOccurrence;
                    _assemblyEvents.OnDelete += AssemblyEvents_OnDelete;
                }
                else
                {
                    _assemblyEvents.OnOccurrenceChange -= AssemblyEvents_OnOccurrenceChange;
                    _assemblyEvents.OnNewOccurrence -= AssemblyEvents_OnNewOccurrence;
                    _assemblyEvents.OnDelete -= AssemblyEvents_OnDelete;
                }
            }
        }

        private void AssemblyEvents_OnDelete(_Document DocumentObject, object Entity, EventTimingEnum BeforeOrAfter,
            NameValueMap Context, out HandlingCodeEnum HandlingCode)
        {
            if (BeforeOrAfter == EventTimingEnum.kBefore
                && Entity is ComponentOccurrence
                && _inv.ActiveDocument is AssemblyDocument asm)
            {
                foreach (ComponentOccurrence plate in AttributeStore.FindEndplates(asm))
                    if (!_toBeReplacedEndplates.Contains(plate)) _toBeReplacedEndplates.Add(plate);
                Diag.Log("watcher: OnDelete captured " + _toBeReplacedEndplates.Count + " endplate(s)");
            }
            HandlingCode = HandlingCodeEnum.kEventHandled;
        }

        private void AssemblyEvents_OnNewOccurrence(_AssemblyDocument DocumentObject, ComponentOccurrence Occurrence,
            EventTimingEnum BeforeOrAfter, NameValueMap Context, out HandlingCodeEnum HandlingCode)
        {
            if (Occurrence != null && !IsOurEndplate(Occurrence) && BeforeOrAfter == EventTimingEnum.kAfter)
            {
                _assemblyEvents.OnOccurrenceChange -= AssemblyEvents_OnOccurrenceChange;
                _transactionEvents.OnCommit += TransactionEvents_OnCommitNew;
                _replacing = true;

                _newBeams[Occurrence.Name] = Occurrence;
                foreach (ComponentOccurrence plate in _toBeReplacedEndplates)
                    _toBeReplacedOccurrences.Add(new KeyValuePair<ComponentOccurrence, ComponentOccurrence>(plate, Occurrence));
                _toBeReplacedEndplates.Clear();
                Diag.Log("watcher: OnNewOccurrence beam=" + Occurrence.Name + " pairs=" + _toBeReplacedOccurrences.Count);
            }
            HandlingCode = HandlingCodeEnum.kEventHandled;
        }

        private void AssemblyEvents_OnOccurrenceChange(_AssemblyDocument DocumentObject, ComponentOccurrence Occurrence,
            EventTimingEnum BeforeOrAfter, NameValueMap Context, out HandlingCodeEnum HandlingCode)
        {
            if (BeforeOrAfter == EventTimingEnum.kAfter && Occurrence != null && ContextHas(Context, "Replace"))
            {
                _assemblyEvents.OnDelete -= AssemblyEvents_OnDelete;
                _assemblyEvents.OnNewOccurrence -= AssemblyEvents_OnNewOccurrence;
                _transactionEvents.OnCommit += TransactionEvents_OnCommitReplace;
                _replacing = true;

                _newBeams[Occurrence.Name] = Occurrence;
                Diag.Log("watcher: OnOccurrenceChange Replace beam=" + Occurrence.Name);
            }
            HandlingCode = HandlingCodeEnum.kEventHandled;
        }

        private void TransactionEvents_OnCommitNew(Transaction TransactionObject, NameValueMap Context,
            EventTimingEnum BeforeOrAfter, out HandlingCodeEnum HandlingCode)
        {
            if (TransactionObject.DisplayName == "Change Frame" && BeforeOrAfter == EventTimingEnum.kAfter && _replacing)
            {
                _transactionEvents.OnCommit -= TransactionEvents_OnCommitNew;
                _assemblyEvents.OnDelete -= AssemblyEvents_OnDelete;
                _assemblyEvents.OnNewOccurrence -= AssemblyEvents_OnNewOccurrence;

                try
                {
                    int n = RebuildPairs();
                    Diag.Log("watcher: OnCommitNew rebuilt " + n + " endplate(s)");
                    Popup(n);
                }
                catch (Exception ex) { Diag.Log("watcher: OnCommitNew FAILED: " + ex); }

                _newBeams.Clear();
                _toBeReplacedOccurrences.Clear();
                _replacing = false;
            }
            HandlingCode = HandlingCodeEnum.kEventHandled;
        }

        private void TransactionEvents_OnCommitReplace(Transaction TransactionObject, NameValueMap Context,
            EventTimingEnum BeforeOrAfter, out HandlingCodeEnum HandlingCode)
        {
            if (TransactionObject.DisplayName == "Change Frame" && BeforeOrAfter == EventTimingEnum.kAfter && _replacing)
            {
                _transactionEvents.OnCommit -= TransactionEvents_OnCommitReplace;
                _assemblyEvents.OnOccurrenceChange -= AssemblyEvents_OnOccurrenceChange;

                try
                {
                    int n = UpdateChangedBeams();
                    Diag.Log("watcher: OnCommitReplace rebuilt " + n + " endplate(s)");
                    Popup(n);
                }
                catch (Exception ex) { Diag.Log("watcher: OnCommitReplace FAILED: " + ex); }

                _newBeams.Clear();
                _replacing = false;
            }
            HandlingCode = HandlingCodeEnum.kEventHandled;
        }

        // Replacer: rebuild each captured (endplate, new beam) pair. The beam captured
        // from the assembly event is in the frame sub-assembly's context; adjust it to
        // the top assembly context (now that the commit has settled the assembly) before
        // rebuilding, exactly as FrameStiffener does — otherwise its proxies E_FAIL.
        private int RebuildPairs()
        {
            if (!(_inv.ActiveDocument is AssemblyDocument asm)) return 0;
            ClearReport();
            return InTransaction(asm, recovery =>
            {
                int n = 0;
                foreach (KeyValuePair<ComponentOccurrence, ComponentOccurrence> pair in _toBeReplacedOccurrences)
                {
                    ComponentOccurrence top = FgBeam.AdjustToContext(pair.Value, asm);
                    Diag.Log("watcher: adjust beam " + pair.Value.Name
                        + (top == null ? " -> NO top-context match, using event beam" : " -> " + top.Name));
                    if (recovery.Recover(pair.Key, top ?? pair.Value)) n++;
                }
                CollectReport(recovery);
                return n;
            });
        }

        // Updater: for each changed beam, rebuild every endplate whose constraints broke.
        private int UpdateChangedBeams()
        {
            if (!(_inv.ActiveDocument is AssemblyDocument asm)) return 0;
            ClearReport();
            return InTransaction(asm, recovery =>
            {
                int n = 0;
                foreach (ComponentOccurrence beam in _newBeams.Values)
                {
                    ComponentOccurrence ctxBeam = FgBeam.AdjustToContext(beam, asm) ?? beam;
                    foreach (ComponentOccurrence plate in new List<ComponentOccurrence>(AttributeStore.FindEndplates(asm)))
                        if (recovery.Recover(plate, ctxBeam)) n++;
                }
                CollectReport(recovery);
                return n;
            });
        }

        private void ClearReport()
        {
            _oldSections.Clear();
            _newSections.Clear();
            _resolvedConstraints.Clear();
        }

        private void CollectReport(EndplateRecovery recovery)
        {
            _oldSections.UnionWith(recovery.OldSections);
            _newSections.UnionWith(recovery.NewSections);
            _resolvedConstraints.AddRange(recovery.ResolvedConstraints);
        }

        // Deleting + recreating constraints from inside a transaction-commit event must
        // run in its OWN transaction, or Delete() throws E_FAIL — exactly as
        // FrameStiffener's Replacer wraps its work in StartTransaction/EndTransaction.
        private int InTransaction(AssemblyDocument asm, Func<EndplateRecovery, int> work)
        {
            Transaction tx = _inv.TransactionManager.StartTransaction((_Document)asm, "Re-resolve Endplates");
            try
            {
                int n = work(new EndplateRecovery(_inv, asm));
                tx.End();
                return n;
            }
            catch
            {
                tx.Abort();
                throw;
            }
        }

        private void Popup(int n)
        {
            // Nothing rebuilt means no constraint broke. Our constraints are to the beam's
            // origin work planes, which survive any edit that keeps the same occurrence —
            // Frame Generator only inserts a NEW beam (breaking them) when the section
            // family changes. Length/position edits auto-resolve, so there's nothing to
            // report; stay silent rather than pop a misleading "Re-resolved 0".
            if (n <= 0)
            {
                Diag.Log("watcher: nothing to re-resolve — constraints survived (no new beam inserted)");
                return;
            }

            string oldSection = _oldSections.Count == 0 ? "unknown" : string.Join(", ", _oldSections);
            string newSection = _newSections.Count == 0 ? "unknown" : string.Join(", ", _newSections);

            string message = "Re-resolved " + n + " endplate(s).\n\n"
                + "Beam section: " + oldSection + " → " + newSection + ".";
            if (_resolvedConstraints.Count > 0)
                message += "\n\nConstraints rebuilt:\n  • " + string.Join("\n  • ", _resolvedConstraints);

            System.Windows.Forms.MessageBox.Show(message, "Frame Generator Demo");
        }

        private static bool IsOurEndplate(ComponentOccurrence occ)
        {
            try { return AttributeStore.ReadEndplate(occ) != null; }
            catch { return false; }
        }

        private static bool ContextHas(NameValueMap context, string name)
        {
            try { for (int i = 1; i <= context.Count; i++) if (context.Name[i] == name) return true; }
            catch { }
            return false;
        }
    }
}
