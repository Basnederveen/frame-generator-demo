# Frame Generator Demo

A minimal, standalone Autodesk **Inventor 2026** add-in that accompanies the
*Inside Frame Generator* blog series. It places an **endplate** on a Frame
Generator beam end, and — when the user later changes that beam's profile
(e.g. HEA200 → HEA240) — automatically **re-resolves the endplate's constraints**
that the profile swap broke.

It's a teaching reference with no external dependencies.

| Part | Topic | Demonstrated in |
|------|-------|-----------------|
| [Part 1 — The Data Model](https://basautomationservices.com/blog/inside-frame-generator-1-data-model) | `com.autodesk.FG` attribute set, native occurrence, reference keys | `FgBeam.cs`, `ReferenceKeys.cs` |
| [Part 2 — Tracking Edits](https://basautomationservices.com/blog/inside-frame-generator-2-tracking-edits) | reconstructing a "frame edited" signal from events | `FrameEditWatcher.cs` |
| [Part 3 — Surviving Replacement](https://basautomationservices.com/blog/inside-frame-generator-3-surviving-replacement) | GUID-tag dependents, rebind keys forward, rebuild `kDriverLostHealth` constraints | `Tags.cs`, `EndplateRecovery.cs` |

## What it does

1. **Place Endplate** (ribbon button, assembly *Assemble* tab) — you pick a Frame
   Generator beam; the add-in builds a 150 × 150 × 10 mm plate and constrains it to
   the beam's three origin work planes (a flush + two mates), then **tags** the
   plate and every constraint with a shared GUID + the beam's reference key.
2. **Change the beam profile** in Frame Generator. FG tears down the old beam
   occurrence, so the endplate's constraints go to `kDriverLostHealth`.
3. The add-in catches the frame edit, finds the orphaned endplate by attribute
   search, resolves the replacement beam to the top assembly context, **rebuilds the
   constraints** against it by work-plane index, and re-captures the reference key
   forward — automatically, with no further interaction.

## Try it in Inventor

The point is to watch a dependent survive a profile change. Three steps:

1. **Make a beam.** New assembly → *Design* tab → **Frame Generator ▸ Insert Frame**.
   Place a single member along a sketch line or model edge (any standard profile,
   e.g. an HEA section) and finish Frame Generator. You now have a frame member in
   the assembly.
2. **Place an endplate.** *Assemble* tab → **Frame Generator Demo ▸ Place Endplate**.
   When prompted, click the **beam**. A 150 × 150 × 10 mm plate appears at the beam's
   origin end, constrained to its origin planes — you'll see its three constraints in
   the browser.
3. **Update the beam.** Edit that member in Frame Generator and **change its
   profile** (e.g. HEA200 → HEA240), then finish. Frame Generator replaces the beam,
   which breaks the endplate's constraints (`kDriverLostHealth`).
   → The add-in re-resolves them automatically and pops up a confirmation; the plate
   re-attaches to the new, larger beam.

`docs/walkthrough.md` runs the same steps with the under-the-hood detail.

## Build

Inventor 2026 runs on **.NET 8**, so the add-in targets `net8.0-windows` and is
loaded via .NET COM hosting (`EnableComHosting` → `FrameGeneratorDemo.comhost.dll`).

- Inventor 2026 installed (provides `Autodesk.Inventor.Interop.dll`).
- .NET 8 SDK (Visual Studio 2022 or `dotnet`).

```
dotnet build FrameGeneratorDemo.sln -c Release
```

## Install (development)

**Building deploys it** — a post-build step in the `.csproj` lays out an
**ApplicationPlugins bundle** at
`%ProgramData%\Autodesk\ApplicationPlugins\FrameGeneratorDemo.bundle\` (a
`PackageContents.xml` plus `Contents\2026\` holding the `.addin`, the managed DLL,
and the comhost). Inventor discovers the bundle on startup — no COM registration.
Just build, then (re)start Inventor. (Writing to `%ProgramData%`
may require an elevated build.)

To uninstall, delete that `FrameGeneratorDemo.bundle` folder.

> Rebuilding while Inventor is open can't overwrite the loaded DLL, so the
> post-build copy warns and skips. Close Inventor and rebuild to refresh.

## Debugging

The project ships an **Inventor 2026** launch profile
(`src/FrameGeneratorDemo/Properties/launchSettings.json`), so pressing **F5** in
Visual Studio builds (which deploys the bundle) and starts Inventor with the
(.NET 8) debugger attached; breakpoints bind once the add-in loads. Edit
`executablePath` in the launch profile if Inventor is installed elsewhere.

## Scope & honesty

This is a demo, not a product. To keep it readable it makes a few simplifying
choices that you'd harden for real use, all flagged in code comments:

- The plate is a fixed-size slab; orientation isn't profile-aware.
- The plate seats at the beam's **origin end** (where its origin planes sit), not
  at a face you choose — you pick the beam, not an end.

Constraining only to the beam's three **origin work planes** is the deliberate core
of the technique: each constraint rebuilds purely by **work-plane index**, which is
exactly stable across a profile change, so recovery needs no geometry heuristic — it
just re-resolves the replacement beam to the top assembly context
(`FgBeam.AdjustToContext`) and rebuilds by index. The origin-plane indices are always
consistent in a part: **`WorkPlanes[1]` = YZ, `[2]` = XZ, `[3]` = XY** (1-based) — they
never reorder, which is precisely what makes the index a safe recovery key.

Once recovery has resolved the new beam, it also reads that beam's **section family**
straight off its part document (`FgBeam.SectionFamily`, the readable **Stock Number**
iProperty Frame Generator fills with the profile name, e.g. `HE 100 B`). The plate
stores the section it was on, so the confirmation reports the change as **old → new**
along with each rebuilt constraint (name + entities). A single FG change applies one new profile to every member edited, so there
is exactly **one target section** regardless of how many beams (and endplates) were
touched. See `docs/walkthrough.md` for a step-by-step run.

## License

MIT — see `LICENSE`.
