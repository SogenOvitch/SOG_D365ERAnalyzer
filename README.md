# D365ERAnalyzer

A desktop tool for reading exported **Dynamics 365 Finance & Operations Electronic Reporting (ER)**
configurations side by side, and following a value from a format element back to the table field it
ultimately comes from.

D365 shows a data model, a model mapping and a format one at a time, in separate screens. Answering
"where does this XML element actually get its value?" means holding three trees in your head at once.
This tool puts them in three panes and links them.

---

## What it does

**Three panes, five trees.** Data model on the left, model mapping in the middle (split into *Data
sources* over *Bindings*), format on the right (split into *Format* over *Format mapping*). Every
split is independently scrollable and every divider is draggable.

**Follows the resolution chain, both ways.** Right-click a format row for *Find binding in model
mapping*; right-click a binding or a model field for *Find format rows using this*. The trace is
real, not textual — it reads the serialized expression trees and matches on the join key that ER
itself uses.

**Coloured dots instead of guesswork.** Each of the five trees owns a colour. Select any row and
every row it relates to, in any tree, gets that colour's dot:

- **bright** — the selected row reads this one
- **darkened** — this row reads the selected row

Dots from different trees accumulate, so a row referenced from two directions carries two dots.
Each tree has ▲ ▼ to walk its dotted rows (scroll only — the selection stays put, so the dot set
does not shift under you), ◎ to return to the selection, and 👁 to hide everything except dotted
rows and their context.

**Search across all three configurations at once**, with per-tree hit counts, a red border when
nothing matches, and an *exact match* toggle so `$CustTrans` stops dragging in `$CustTrans_OrderBy`.
The data model is searched through an index over the underlying graph, so hits are found in
branches that were never expanded and the tree opens itself to reach them.

**Details panel** per pane showing the selected row's name, type, path, formula and enable
condition — read-only but selectable, with the formula's real line breaks preserved.

---

## Getting started

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download) on Windows.

```bash
dotnet run --project src/D365ERAnalyzer
```

Then **Open folder…** and point it at a folder of exported configurations. It reads `*.xml` at the
top level only, identifies each by type, and loads it into the matching pane. If a format and a
model mapping are both present, the mapping line the format actually consumes is selected
automatically. Panes can also be loaded one at a time with **Open…**.

> A folder holding several files of one type loads them all in turn and the last one wins. Point it
> at a folder holding one data model, one model mapping and one format.

---

## How to read an ER configuration

The three file types are not independent, and the links between them are the whole point of this
tool. Briefly:

- A **data model** is a flat graph of descriptors, not a tree. The hierarchy exists only by
  resolving each field's type back to another descriptor, and it can be recursive.
- A **model mapping** file holds *several* independent mapping lines. Only one of them is the line a
  given format consumes; the others are for different documents. The line is identified by the
  triple (model GUID, model version, root descriptor).
- A **format** file holds *two* versioned objects: the component tree, and the format mapping that
  binds it. The component tree carries no bindings itself — they are joined in by component GUID.

The full reverse-engineered file structure, including several traps that are easy to get wrong, is
in **[docs/er-xml-schema.md](docs/er-xml-schema.md)**. Worth reading before changing the parser.

---

## Project layout

```
D365ERAnalyzer.sln
docs/er-xml-schema.md          the XML format, reverse-engineered from real exports
samples/                       exported configurations used during development
src/D365ERAnalyzer/
  Model/                       domain types: descriptors, mappings, bindings, format components
  Parsing/                     XML readers, expression path extraction, the model search index
  ViewModels/                  panes, tree sections, rows, markers
  ViewModels/Panes/            one view model per configuration type
  Views/                       the window, a pane, a tree section
  Converters/                  small value converters
  Themes/Dark.xaml             the shared dark theme, used verbatim
  Themes/Shell.xaml            only what Dark.xaml does not cover
```

Roughly 3 900 lines of C# and 1 000 of XAML. WPF on `net8.0-windows`, MVVM, no third-party
dependencies.

### Notes for anyone changing the parser

- **Resolve from the AST, never from `@ExpressionAsString`.** Every formula is stored twice, and the
  two can disagree — there is a binding in the samples whose display string names `$Amount` while
  its AST names the underlying `SourceTaxAmountCur`. The AST is what runs.
- **`@RelativePath` must be rejoined to its container.** A path interrupted by a function call is
  split in two, and treating the second half as merely relative silently drops every segment after
  the call.
- **Trees are not virtualised.** They measure their full content so the scrollbar is exact and
  scroll-to-row lands precisely; the cost is a real container per expanded row, which is why
  "expand all" stops at 6 000 nodes.

---

## Status

Working tool, developed against one real trio of configurations. Several details of the file format
are decoded empirically rather than from documentation and are marked as unverified in the schema
notes — notably the `@SelectionField` value for a *min* aggregation, which occurs exactly once in
the samples with no name to corroborate it.

Read-only by design: nothing is ever written back to a configuration file.

---

## License

[MIT](LICENSE).
