# Walkthrough

A 5-minute run to see the constraint survive a profile change.

## Setup

1. Build and install (`README.md` → Build / Install), restart Inventor.
2. New assembly. Insert or build a simple **Frame Generator** frame — a single
   beam is enough. Use a standard profile, e.g. an HEA/IPE section.

## Place an endplate

3. On the **Assemble** tab, find the **Frame Generator Demo** panel → **Place
   Endplate**.
4. When prompted, click the **beam**.
5. A 150 × 150 × 10 mm plate appears at the beam's origin end, centred on its
   cross-section. In the browser you'll see three constraints — a flush and two
   mates, each built against one of the beam's origin work planes.

What happened under the hood:
- `FgBeam.IsFrameGeneratorBeam` confirmed the picked component carries the
  `com.autodesk.FG` attribute set (Part 1).
- `EndplatePlacer` created the plate, constrained it to the beam's three origin
  planes (YZ flush, XZ mate, XY mate), and tagged the plate + each constraint with
  a shared GUID and the beam's stringified reference key (Part 1 + Part 3).

## Break it, watch it heal

6. Edit the beam in Frame Generator and **change its profile** (e.g. HEA200 →
   HEA240). Confirm.
7. The moment you do, Frame Generator destroys the old beam occurrence. Without
   the add-in you'd now see the three constraints flagged with red icons
   (`kDriverLostHealth`), the plate detached.
8. Instead, a confirmation pops up — "Re-resolved N endplate(s)", the section change
   as **old → new**, and the list of rebuilt constraints — and the plate is re-attached
   to the new beam, no further interaction needed.

What happened under the hood:
- `FrameEditWatcher` saw the frame-edit transaction commit and ran recovery (Part 2).
- It resolved the replacement beam (handed back in the frame sub-assembly's context)
  to the top assembly context with `FgBeam.AdjustToContext` — otherwise its proxies
  are rejected by the top-level constraint API.
- With the new beam in hand it read that beam's **section family**
  (`FgBeam.SectionFamily`, the readable **Stock Number** iProperty — e.g. `HE 100 B`).
  The plate records the section it was on, so the confirmation shows the change as
  **old → new**.
  Because one FG change applies a single new profile to every edited member, that target
  section is one value no matter how many beams changed.
- `EndplateRecovery` found the endplate by attribute search, saw its tagged
  constraints had gone `kDriverLostHealth` (the Part 3 signal that the beam was
  replaced), deleted the broken constraints, and rebuilt all three by work-plane
  index against the new beam — then re-captured the reference key forward.

## Things to try

- Change the profile **several times** in a row — recovery re-runs each commit.
- Place endplates on **multiple beams** — each is tracked independently by its
  own GUID.
- **Edit the beam *without* changing its section** — e.g. change only its length.
  No confirmation appears: the constraints are to the beam's origin work planes,
  which survive any edit that keeps the same occurrence. Frame Generator only
  inserts a new beam (breaking them) when the **section family** changes, so that
  is the only edit that triggers recovery; everything else Inventor auto-resolves.
- Open the assembly's iLogic/VBA and inspect the `fgdemo.endplate` /
  `fgdemo.constraint` attribute sets to see the stored tags.
