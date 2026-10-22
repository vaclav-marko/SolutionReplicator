# Slide-deck generator prompt — SolutionReplicator

> Paste everything in the fenced block below into your slide-deck generator (Gamma, Copilot/
> PowerPoint, Beautiful.ai, Google Slides "help me create", etc.). It is written as a self-contained
> brief: audience, narrative arc, per-slide content, and the technical facts to render. Adjust the
> `[AUDIENCE]` / `[PRESENTER]` / `[DATE]` placeholders before sending.

---

```
You are an expert presentation designer. Create a professional, visually clean slide deck
(16:9, corporate, minimal text per slide, one idea per slide, speaker notes under each slide).

TITLE: SolutionReplicator — Replicate SAP configuration across systems, from your own content

AUDIENCE: [AUDIENCE — e.g. company leadership + SAP delivery leads, mixed technical/non-technical]
PRESENTER: [PRESENTER]    DATE: [DATE]
GOAL OF THE TALK: show (1) how SAP Best Practices delivers configuration today, (2) what the
client can build and run on their own with this tool, (3) the outlook / roadmap. End with a clear
ask. Keep it credible to SAP architects while understandable to non-technical execs.

TONE: confident, concrete, no hype. Prefer diagrams over bullet walls. Use callouts for the few
numbers that matter. Avoid jargon on headline slides; put the SAP detail in speaker notes.

────────────────────────────────────────────────────────
DECK STRUCTURE (one slide per entry; generate speaker notes for each)
────────────────────────────────────────────────────────

1. TITLE SLIDE
   - Title + one-line subtitle: "Turn manual SAP system setup into a repeatable, client-owned process."
   - Presenter, date.

2. THE PROBLEM (why now)
   - Today, standing up each new SAP system means re-doing configuration BY HAND.
   - Many target systems (e.g. 20) = the same manual customizing, repeated, error-prone, slow.
   - Speaker note: this is the pain the tool removes.

3. HOW SAP BEST PRACTICES WORKS TODAY (set the baseline)
   - SAP distributes setup for public & private cloud as a "solution": Scope Items → Building Blocks.
   - The configuration VALUES live separately in installation-data files (tab-delimited .TXT,
     UTF-16LE). The solution XML references them.
   - Simple diagram: [Solution XML] --references--> [thousands of .TXT installation-data files].
   - Speaker note: this is the structure we reuse as a template.

4. THE IDEA (the reframe — this is the pivot slide)
   - Flip the master: instead of SAP's export being the source of truth, the CLIENT owns the content.
   - Two client-owned assets:
       • a REPOSITORY of their own .TXT files written with PLACEHOLDERS (e.g. &PLN for "plant")
         instead of hardcoded values;
       • a NOMENCLATURE TABLE (a simple CSV) saying, per target system, what each placeholder becomes.
   - Callout: "&PLN → HU01 in HU-PROD, → CZ01 in CZ-PROD". One content set, many systems.
   - Speaker note: placeholders are stable across SAP revisions and countries; a plain value map is not.

5. THE NOMENCLATURE TABLE (make it concrete — show the CSV)
   - Render this as a small table on the slide:
       variable | field   | value | sysid
       &PLN     | I_WERKS | HU01  | HU-PROD
       &PLN     | I_WERKS | CZ01  | CZ-PROD
       &PLN     | I_WERKS | DE01  | DE-TEST
   - One line: "Add a new target system = add rows. Nothing else changes." (additive, 20+ systems)
   - Speaker note: Semicolon CSV: variable;field;value;sysid. Maintained by the client.

6. THE TOOL — END-TO-END IN FIVE STEPS (the demo spine)
   - Horizontal 5-step flow diagram:
       1 Load template XML  →  2 Pick scope (Scope Items / Building Blocks)  →
       3 Point at repository + nomenclature CSV (placeholders auto-discovered)  →
       4 Pick target system & fill values  →  5 Compose (optionally rename solution id)
   - Output callout: "a re-importable SAP package: solution XML + populated .TXT files".
   - Speaker note: steps 1–2 reuse the SAP structure; 3–5 are the client-owned compose.

7. LIVE DEMO (placeholder slide)
   - Big "DEMO" title + the 5 steps as a checklist to tick through live.
   - Demo data: template ZVMA_DEMO_TEMPLATE.xml, repo folder of tokenized files, HU-PROD system,
     &PLN → HU01. Show the before (&PLN in a .TXT) and after (HU01 in the output).
   - Speaker note: emphasise that only the targeted column changes; file fidelity is preserved.

8. WHAT HAPPENS UNDER THE HOOD (technical credibility — for the architects)
   - Three facts, as a compact diagram or 3 tiles:
       • SCOPE → CONSUMPTION: from the ticked scope, the tool computes exactly which installation-data
         files are needed (not the full ~14,000-file catalog) by walking the solution XML joins.
       • MATCH: those consumed file names are matched one-to-one to the client's repository files.
       • SUBSTITUTE: each placeholder cell is replaced with the chosen system's value — column-scoped,
         preserving UTF-16LE+BOM, tabs, CRLF, and all non-data lines verbatim.
   - Callout: "The value-substitution engine is reused unchanged — a placeholder is just a cell value."
   - Speaker note: substitution matches on (column header, exact cell value); unmapped placeholders are
     left visible rather than silently blanked.

9. ARCHITECTURE (one diagram)
   - A thin WPF/.NET 8 "shell" wizard reusing two existing proven tools as libraries:
       [SolutionReplicator shell]
          ├── SolutionBuilder  (parse/filter the solution XML, country/id rules)
          └── SapVariantReplacer (parse/extract/substitute the .TXT files)
   - New pieces: nomenclature CSV store, token scanner, repo-file matcher, compose step.
   - Speaker note: minimal new surface; the hard, risky logic was reused, not rewritten.

10. WHY THIS APPROACH (benefits — tie back to the problem)
    - Repeatable: one content set + one CSV → any number of systems.
    - Owned by the client: their repository, their values, maintainable in Excel.
    - Safe: only targeted values change; file format fidelity preserved; re-importable into SAP.
    - Fast: new system = pick a column, compose, import.

11. SCOPE & BOUNDARIES (be honest — builds trust)
    - IN scope: produce a valid, re-importable package (solution XML + populated .TXT).
    - OUT of scope (by design, done manually in SAP): importing the package into SAP Solution Builder
      and activating it.
    - Known check: the first real re-import validates the package; flag it as the proof point.

12. OUTLOOK / ROADMAP (the "what they can achieve" close)
    - Near term: broaden the placeholder nomenclature table; more systems; validate a full real re-import.
    - Medium: richer value rules (dependent values), repository governance, bulk edits.
    - Vision: a client-run configuration-replication platform — new systems stood up in minutes, not weeks.
    - Speaker note: frame as a credible roadmap, not a promise.

13. THE ASK / NEXT STEPS
    - One clear ask for [AUDIENCE] (e.g. green-light a pilot on N real systems; assign a content owner).
    - 2–3 concrete next steps with owners.

14. CLOSING / Q&A
    - Restate the one-liner from slide 1. Contact. Thank you.

────────────────────────────────────────────────────────
DESIGN DIRECTION
────────────────────────────────────────────────────────
- Palette: one primary accent (SAP blue #2196F3 or the company brand), neutral grays, white space.
- Use the 5-step flow (slide 6) as a recurring visual motif; highlight the current step on demo slides.
- Monospace font for the CSV/table and any file-name or token examples (&PLN, I_WERKS, HU01).
- Keep headline slides to ≤15 words; push detail into speaker notes.
- Generate speaker notes for every slide.
```

---

## Notes for the presenter (not part of the prompt)

- The deck's slides 3–6 map exactly to the live tool flow, so you can demo straight off slide 6/7.
- Demo assets that exist and work today: `C:\_testing\ZVMA_DEMO_TEMPLATE.xml`,
  `C:\_testing\_CUST1\` (tokenized repo), `C:\_testing\sample-nomenclature.csv` (HU01/CZ01/DE01).
- The one claim to NOT overstate on stage: "it re-imports into SAP cleanly" — that's the manual
  step you verify, not something the tool self-proves. Slide 11 says so honestly.
- If the audience is mostly non-technical, you can hide slides 8–9 (under-the-hood/architecture) and
  keep them as backup for Q&A.
