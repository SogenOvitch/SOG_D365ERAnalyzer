# D365 Electronic Reporting — exported configuration XML schema

Reverse-engineered from four configuration sets — a sales invoice (UBL XML), a purchase order
(Excel), a payment (ISO 20022, XML and Excel together) and a fixed asset movement. Thirteen files
in all, covering derived and base configurations, export and import mappings, and configurations
that carry mapping lines inside a format or a model.

The measurements below come from the sales invoice set unless stated otherwise:

| File | Config type | Size | Lines | Distinct elements | Max depth |
|---|---|---|---|---|---|
| `Invoice model.xml` | Data model | 214 KB | 2 011 | 15 | 8 |
| `Invoice model mapping.xml` | Model mapping | 3.4 MB | 48 078 | 197 | 30 |
| `UBL Sales e-invoice.xml` | Format (+ format mapping) | 681 KB | 9 446 | 135 | 32 |

Everything below is observed in these files, not recalled. Items that could not be confirmed are
marked **[UNVERIFIED]**.

**This file is meant to be enough on its own.** It records not only the shape of the XML but the
rules for consuming it: how the trees are rebuilt from flat lists, how a reference is matched to a
row, how a format is joined to the mapping behind it, and the traps that produce plausible but
wrong answers. Someone picking the parser up with no other context should be able to work from
this alone.

---

## 1. Common envelope

All three config types share the same outer shell. **The config type is identified by what appears
under the root `Contents.`**, not by anything in the envelope.

```xml
<ERSolutionVersion DateTime="…" Description="…" Number="13"
                   PublicVersionNumber="322.395.13" VersionStatus="1">
  <Prerequisites>              <!-- optional; absent in the data model sample -->
    <ERPrerequisites>
      <Contents.>
        <ERPrerequisiteGroup Name="Implementations" Type="1">
          <Contents.>
            <ERPrerequisiteComponent Id="{5BF72F8B-…}" IsImplementation="1" Type="4" Version="7" />
          </Contents.>
        </ERPrerequisiteGroup>
      </Contents.>
    </ERPrerequisites>
  </Prerequisites>
  <Solution>
    <ERSolution ID.="{9E3FC4D7-…}" Base="{59EC3DC7-…},395" BaseName.o.="Invoice model"
                CountryRegionCodes="FR" Description="…" Name="Invoice model">
      <Vendor><ERVendor Name="…" Url="…" /></Vendor>
      <Contents.>
        <Ref. ID.="{4B5553D1-…}" />     <!-- one Ref. per contained versioned object -->
      </Contents.>
    </ERSolution>
  </Solution>
  <Contents.>
    <!-- ERDataModelVersion | ERModelMappingVersion (xN) | ERFormatVersion + ERFormatMappingVersion -->
  </Contents.>
</ERSolutionVersion>
```

### The configuration type has to be ranked, not taken from the last marker seen

A file's type is decided by which versioned object it defines, but more than one kind of marker can
appear in a single file: a format carries its format mapping, and either a format or a data model
may also carry `ERModelMappingVersion` entries of its own. Reading the markers in order and letting
the last one win files a model that happens to embed a mapping line as a *model mapping* — which
sends it to the wrong place and leaves its descriptors unparsed.

The reliable rule is precedence, not order:

| If the file contains | It is a |
|---|---|
| `ERDataModel` | Data model, whatever else it carries |
| otherwise `ERTextFormat` / `ERFormatMapping` | Format |
| otherwise `ERModelMapping` only | Model mapping |

### Naming conventions (X++ serialization artifacts)

| Convention | Meaning | Examples |
|---|---|---|
| Trailing `.` | Framework/system member | `ID.`, `Contents.`, `Ref.`, `Delta` |
| `.o.` suffix | Cached display name of the referenced object; informational only | `BaseName.o.` |
| `{GUID},N` | Versioned object reference: object GUID + version number | `ID.="{4B5553D1-…},7"` |

**`Contents.` is a pure collection wrapper.** It carries no data and must be transparent in the UI —
the parser skips it and re-parents its children.

### Version identity

- `ERSolutionVersion/@Number` — this config's version (13, 7, 23 in the samples).
- `@PublicVersionNumber` — full lineage chain: `322.395.13` reads as base 322 → 395 → this 13.
  This is the string to show in the root tree node.
- `@VersionStatus` — **[UNVERIFIED]**; `1` and `2` both occur across the sets, so it is a real
  enum rather than a constant. Draft / Completed / Shared / Discarded is the likely family.

### Derived configurations

Every sample is derived (`ERSolution/@Base` is set) and every versioned object carries a `Delta`:

```xml
<Delta>
  <ERObjectOperationSequence>
    <Contents.>
      <ERObjectOperationInsert Destination="[InvoiceBase]" AppendBefore="{213D75C1-…}">…</ERObjectOperationInsert>
      <ERObjectOperationModify  Object="{92F36C12-…}" ModifiedProperties="parmValue">…</ERObjectOperationModify>
      <ERObjectOperationDelete  Object="FormatComponentFieldBinding:Enabled:{2ae3550b-…}" ObjectContainer=".Binding" />
    </Contents.>
  </ERObjectOperationSequence>
</Delta>
```

**Important:** the `Model` / `Mapping` / `Format` element holds the *fully resolved* tree — base plus
customizations already applied. The `Delta` is a **parallel record of what this config changed
relative to its base**. The tool therefore never needs to replay the delta to render the tree.

`@Object` / `@Destination` use at least three addressing syntaxes, so treat the Delta as
display-only for now:
- `[InvoiceBase]` — bracketed descriptor name
- `{GUID}` — direct object id
- `.Datasource[Enums/PrePrintLevel].ValueDefinition.ValueSource` — property path with indexer
- `FormatComponentFieldBinding:Enabled:{guid}` — typed reference

---

## 2. Data model file

```
ERSolutionVersion / Contents. / ERDataModelVersion (@ID.="{guid},7" @Number="7")
  ├─ Model / ERDataModel (@ID. @Base @Name @Description @Root)
  │    └─ Contents. / ERDataContainerDescriptor  (×153, FLAT LIST)
  │         └─ Contents. / ERDataContainerDescriptorItem  (×1363)
  └─ Delta / …
```

### The model is a flat graph, not a tree

This is the single most important structural fact. All 153 `ERDataContainerDescriptor` elements are
**siblings**. The tree D365 shows is built by resolving `ERDataContainerDescriptorItem/@TypeDescriptor`
against `ERDataContainerDescriptor/@Name`, starting from a root descriptor.

- 72 of the 153 descriptors have `IsRoot="1"` — these are the selectable model roots.
- 34 have `IsEnum="1"` — enum descriptors, whose items are the enum values.
- `ERDataModel/@Root="AdditionalDocumentReference"` is just the designer's last-selected root.
  It is **not** the model's only root, and the tool should not treat it as such.

**Some descriptors are reachable from nowhere.** A descriptor that is neither flagged `IsRoot` nor
named by any field's `@TypeDescriptor` cannot be reached by walking the graph at all. It happens:
one payment model has two such descriptors — and they are precisely the ones every mapping line and
format in that set binds to, holding 12 and 10 fields between them. An invoice model has one.

They are not errors in the file; D365 evidently reaches them by another route. But any tool that
renders the model by walking outward from `IsRoot` will show a tree in which those subtrees do not
exist, and every path into them will fail to match for reasons that look like a resolution bug and
are not.

**Consequence: the graph can be recursive.** The parser must build the tree lazily (expand on demand)
with a cycle guard, or it can infinite-loop on a self-referencing descriptor.

### `ERDataContainerDescriptor`

| Attr | Notes |
|---|---|
| `ID.` | Not a GUID here — the descriptor **name** (`InvoiceBase`, `SalesInvoice`) |
| `Name` | Same as `ID.` in every observed case |
| `Label`, `Description` | May be a label id: `@GER_LABEL:CustomerInvoice`, `@SYS28013` |
| `IsRoot` | `"1"` when selectable as a mapping root |
| `IsEnum` | `"1"` when this descriptor is an enum |

### `ERDataContainerDescriptorItem`

| Attr | Notes |
|---|---|
| `Name` | Field name — the path segment |
| `Type` | Numeric data type, decoded below |
| `TypeDescriptor` | Name of the referenced descriptor — **this is the edge that builds the tree** |
| `Label`, `Description` | May be a label id |
| `IsTypeDescriptorHost` | **[UNVERIFIED]** marks the item that owns an inline descriptor definition |

### `@Type` decode (empirically derived, 1 363 items)

| Type | Count | Always has `TypeDescriptor`? | Inferred | Evidence |
|---|---|---|---|---|
| 1 | 95 | no | Boolean | `CopyIndicator`, `IsCreditNote` |
| 3 | 10 | no | Int64 | `RecId`, `SourceRecId` |
| 4 | 12 | no | Integer | `CopyNumber`, `DuplicateNumber` |
| 5 | 187 | no | Real | `Amount`, `AmountMST` |
| 6 | 551 | no | String | `AmountInWords`, `AttachmentHash` |
| 7 | 45 | no | Date | `Date`, `DueDate` |
| 8 | 1 | no | Time | `LoadingTime` |
| 9 | 214 | 42 of 214 — see below | **Enum** (field *or* value) | `GSTReference`, `InvoiceType` |
| 10 | 155 | **yes (155/155)** | Record (single) | `Buyer`, `CompanyContact` |
| 11 | 77 | **yes (77/77)** | List (collection) | `AlternativeSchemes`, `BackOrders` |
| 13 | 5 | 2 of 5 | Container / binary | `Logo`, `Content` |
| 14 | 11 | no | DateTime | `DocumentDateTime`, `LoadingDateTime` |

Types 2 and 12 occur in none of the three models — 2 182 fields in total — so the gaps are
probably real rather than an artefact of one sample.

**Type 9 is Enum, and it plays two roles** — which is why only 42 of 214 carry a `TypeDescriptor`.
The split is exact, with zero exceptions across all 214 items:

| Role | `TypeDescriptor` | Where it sits | Count |
|---|---|---|---|
| Enum-typed **field** | present — names an `IsEnum="1"` descriptor | inside a normal descriptor | 42 |
| Enum **value** (member) | absent | inside an `IsEnum="1"` descriptor | 172 |

An enum field's `TypeDescriptor` resolves like any other: to an `ERDataContainerDescriptor`, but one
marked `IsEnum="1"`, whose `ERDataContainerDescriptorItem` children **are the enumeration values**:

```xml
<ERDataContainerDescriptor ID.="CompanyType_MX" IsEnum="1" IsRoot="1" Label="@GER_LABEL:CompanyType">
  <Contents.>
    <ERDataContainerDescriptorItem Label="@GER_LABEL:Blank"          Name="Blank"          Type="9" />
    <ERDataContainerDescriptorItem Label="@GER_LABEL:ForeignCompany" Name="ForeignCompany" Type="9" />
    <ERDataContainerDescriptorItem Label="@GER_LABEL:LegalEntity"    Name="LegalEntity"    Type="9" />
  </Contents.>
</ERDataContainerDescriptor>
```

So an enum field **does** create child nodes (its values) — types 10, 11 and enum-9 are the
node-producing types. Enum values are leaves and should carry a distinct icon from records.

Types 10 and 11 are the only ones that create child nodes; 11 is the repeating one (list icon).

---

## 3. Model mapping file

```
ERSolutionVersion / Contents. / ERModelMappingVersion  ×7   ← one per mapping line
  ├─ Mapping / ERModelMapping
  │    ├─ Datasource / ERModelDefinition / Contents. / ERModelItemDefinition ×N   ← datasource tree
  │    ├─ Binding    / ERDataContainerBinding / Contents. / ERDataContainerPathBinding ×N  ← THE BINDINGS
  │    ├─ PathsToCache / ERPathsToCache / Contents. / ERPathToCache
  │    ├─ Validations, Format, TableName
  └─ Delta / …
```

This file confirms your point about multiple mapping lines — it holds **7 independent
`ERModelMappingVersion` blocks**, each with its own datasource tree and binding set:

| Mapping name | `@DataContainerDescriptor` | `@ModelVersion` | Line |
|---|---|---|---|
| Sales invoice | `SalesInvoice` | `{4B5553D1-…},7` | 43 |
| Vendor invoice | `InvoiceVendor` | `{4B5553D1-…},7` | 3 288 |
| Project invoice | `InvoiceProject` | `{4B5553D1-…},7` | 6 198 |
| Customer prepayments | `CustomerPrepayment` | `{4B5553D1-…},7` | 20 495 |
| Commercial invoice | `TMSCommercialInvoice` | `{4B5553D1-…},7` | 21 289 |
| **Customer invoice** | **`InvoiceCustomer`** | `{4B5553D1-…},7` | **21 505** |
| Customer debit/credit note | `CustDebitCreditNote` | `{4B5553D1-…},7` | 47 775 |

### `ERModelMapping` — the join key

| Attr | Example | Role |
|---|---|---|
| `ID.` | `{B5AFF97C-…}` | Mapping identity |
| `Name` | `Customer invoice` | Display name (the "mapping line") |
| `Model` | `{4B5553D1-…}` | Data model GUID → matches `ERDataModel/@ID.` |
| `ModelName` | `Invoice model` | Cached name |
| `ModelVersion` | `{4B5553D1-…},7` | **Model version this line targets** |
| `DataContainerDescriptor` | `InvoiceCustomer` | **Which model root this line maps** |
| `Base` | `{3ABB9EF5-…},263` | Derivation parent |

The triple (`Model`, `ModelVersion`, `DataContainerDescriptor`) is what the format matches against.

### Mapping lines are not confined to model mapping files

`ERModelMappingVersion` / `ERModelMapping` also appear inside **format** files — one payment format
embeds a line of its own — and inside **data models**: the fixed asset model carries the only
mapping line that set has, so the middle pane has nothing to show unless models are read for them
too. A reader that only looks when the envelope says "model mapping" will miss both.

The reliable approach is to read `ERModelMappingVersion/Mapping/ERModelMapping` from every file
regardless of its declared type, and to keep track of which file each line came from: a line living
inside a format is not one of the mapping file's own and should not be presented as such.

### Not every line declares a root, and versions drift

Two assumptions that the first sample set quietly supported turn out to be false:

- **`@DataContainerDescriptor` can be absent.** All four mapping lines in the payment set omit it.
  A line without one agrees with a format on the model alone, which is far too weak to call it
  *the* line that format uses.
- **The versions need not agree.** In the payment set the format asks for model **v40**, the mapping
  lines target **v76**, and the model file itself is **v99**. A format built against an older model
  version runs perfectly well against a newer mapping, so requiring version equality when matching
  rejects real pairings.

`ERModelMapping/@Direction` distinguishes the two kinds of line: absent on export mappings, `1` on
import ones — in the samples, exactly the lines named "Import mapping for …". An export format will
not find its counterpart among import lines, and saying so is more useful than silently selecting
one of them.

### `ERDataContainerPathBinding` — model field → datasource expression

2 676 of these across the file. This is the artifact the cross-config lookup resolves to.

```xml
<ERDataContainerPathBinding
    ExpressionAsString="IF(&#xA;	Parameters.'$IsQRBill',&#xA;	Parameters.'$SourceJournal'.'$InvoiceId',&#xA;	CustInvoiceJour.InvoiceId&#xA;)"
    Path="InvoiceBase/Id" SyntaxVersion="1">
  <Expression> … typed AST … </Expression>
</ERDataContainerPathBinding>
```

- `@Path` — model path **relative to the mapping's `@DataContainerDescriptor`**, slash-separated.
- `@ExpressionAsString` — the datasource-side formula exactly as shown in the D365 designer,
  with real newlines encoded as `&#xA;` / `&#xD;`. **Render these as line breaks.**
- `@SyntaxVersion` — often absent. Parser must tolerate it missing.
- Quoting in expressions: identifiers with special chars are single-quoted
  (`'$IsQRBill'`, `'>Relations'`, `'creditNote()'`).

### Datasource handlers (`ValueSource` child)

| Element | Key attributes | Meaning |
|---|---|---|
| `ERTableDataSourceHandler` | `Table`, `Path`, `AskForQuery`, `IsCrossCompany`, `IsIntegrationPoint` | Table record |
| `ERTableDataSource` | `Table`, `Path` | Table reference |
| `ERClassDataSourceHandler` | `ClassName` | Application class |
| `ERObjectDataSourceHandler` | `ClassName`, `SRSAttributeSupport` | Report data provider |
| `EREnumDataSourceHandler` | `EnumName` | D365 enum |
| `ERModelEnumDataSourceHandler` | `ModelEnumName`, `ModelGUID`, `RevisionNumber` | Model enum |
| `ERUserParameterDataSourceHandler` | `ExtendedDataTypeName` | User input parameter |
| `EREmptyContainerDataSourceHandler` | — | Empty grouping container |
| `ERModelExpressionItem` | `ExpressionAsString`, `SyntaxVersion` | Calculated field |
| `ERModelGroupByFunction` | `ListToGroup`, `SourceListIsAlreadySorted` | Group-by datasource |
| `ERJoinedList` / `ERListJoinDatasource` | `Name`, `Path` | Joined list |
| `ERExportFormatDatasource` | `FormatGUID` | **The format itself** — see below |
| `ERDataCollectionDatasource` | `CollectDuplicates`, `ItemType` | Collected values |

---

## 4. Format file

A format export contains **two** versioned objects (hence two `Ref.` entries in `ERSolution`):

```
ERSolutionVersion / Contents.
  ├─ ERFormatVersion (@ID.="{173108B0-…},11")
  │    ├─ Format / ERTextFormat (@ID. @Base @Name @Description)
  │    │    ├─ Root / ERTextFormatFileComponent (@Name="XMLHeader" @Encoding="UTF-8")
  │    │    │    └─ Contents. / ERTextFormatXMLElement …          ← the format tree
  │    │    ├─ TransformationRepository / ERNamedTransformationsRepository
  │    │    └─ EnumList / EREnumDefinitionList
  │    └─ Delta
  └─ ERFormatMappingVersion (@ID.="{0BBDF9B1-…},22")
       ├─ Mapping / ERFormatMapping (@Format @FormatVersion @Name @Base)
       │    ├─ Datasource / ERModelDefinition / Contents. / ERModelItemDefinition ×52
       │    └─ Binding / ERFormatBinding / Contents. / ERFormatComponentPropertyBinding ×788
       └─ Delta
```

### Format component types

| Element | Key attributes | Count |
|---|---|---|
| `ERTextFormatFileComponent` | `Name`, `Encoding` | 1 (root) |
| `ERTextFormatXMLElement` | `ID.` (GUID), `Name`, `Multiplicity` | 343 |
| `ERTextFormatXMLAttribute` | `ID.`, `Name`, `Value` | 416 |
| `ERTextFormatString` | `ID.`, `Value`, `Transformation` → transformation GUID | 249 |
| `ERTextFormatDate` | `ID.`, `DateFormat` (`yyyy-MM-dd`) | 7 |
| `ERTextFormatBase64Component` | `ID.` | 1 |

Only `ERTextFormatXMLElement` and `ERTextFormatFileComponent` have `Contents.` children — the format
tree nests through those two. `@Multiplicity` — **[UNVERIFIED]** numeric occurrence enum; `1`,
`20` and `200` all occur.

### `ERFormatComponentPropertyBinding` — the format↔mapping join

**The format tree carries no bindings inline.** Bindings live in the format mapping and point back at
format components by GUID:

```xml
<!-- a value binding: no @PropertyName -->
<ERFormatComponentPropertyBinding Component="{6B2895AA-…}"
                                  ExpressionAsString="Invoice.InvoiceBase.Id" SyntaxVersion="1">
  <Expression><ERExpressionStringItemValue ItemPath="Invoice/InvoiceBase/Id" /></Expression>
</ERFormatComponentPropertyBinding>

<!-- an Enabled binding: switches the component off -->
<ERFormatComponentPropertyBinding Component="{0167D8F4-…}" PropertyName="Enabled"
                                  ExpressionAsString="false" SyntaxVersion="1" />
```

`@Component` matches an `ERTextFormat*/@ID.`. **`@PropertyName` is absent on the binding that
supplies the component's own value** — those are the ones that carry the model paths. The named
properties observed across all three sets are:

| `@PropertyName` | Count | Meaning |
|---|---|---|
| *(absent)* | 170 | The component's **value** binding |
| `Enabled` | 620 | Conditional enable; 579 of them are the literal `false` |
| `FileName` | 2 | File name expression on the file component |
| `Validation` | 8 | Validation rule on a component *(other sets)* |
| `FileLanguage` | 1 | Output language of a file component *(other sets)* |

792 bindings in total: 773 under `Binding/ERFormatBinding`, the remaining 19 inside the `Delta`.

**Implication for the UI:** 579 of the 989 components are hard-disabled (`Enabled = "false"`).
That is normal for a configuration derived from a broad base format — the derivation switches off
the elements it does not emit — and it is *not* noise to filter away. A reader needs to see which
components are off, so render them dimmed rather than hiding them.

### Format mapping datasources

`ERModelItemDefinition` / `ERModelItemValueDefinition` / `ValueSource` — the **same three-element
shape used by the model mapping**, so one parser serves both.

The root datasource of this format is named `Invoice`:

```xml
<ERModelDataSourceHandler DataContainerDescriptorName="InvoiceCustomer"
                          ModelGuid="{4b5553d1-f92e-407b-b65b-06f4fdbb671a}" RevisionNumber="7" />
```

---

## 5. Expressions are pre-parsed — no tokenizer needed

Every expression is serialized **twice**: as `@ExpressionAsString` (display) and as a fully typed
AST of `ERExpression*` elements (machine-readable). Model paths appear as literal attributes:

| Attribute | On | Example |
|---|---|---|
| `ItemPath` | `ERExpression*ItemValue`, `ERExpressionGenericCall` | `Invoice/InvoiceBase/Id` |
| `FieldPath` | `ERModelGroupByAggregation`, `…FieldReference` | `Invoice/LineItem/DeliveryDate` |
| `ParentPath` | `ERModelItemDefinition` | `Invoice/InvoiceBase` |
| `RelativePath` | `ERExpressionGenericRelativeItemValue` | `$PostalAddress/$Country/ISOcode` |
| `ListToGroup` | `ERModelGroupByFunction` | `Invoice/LineItem` |

Path extraction is therefore an **attribute sweep over the AST**, not parsing. This removes the
expression-tokenizer milestone from the plan entirely.

### ⚠ `RelativePath` must be rejoined to its container

A path interrupted by a function call is split across two attributes. This formula:

```
FIRSTORNULL(Tables.EInvoiceParameters_IT_Current.'>Relations'.DirPartyTable_Signer)
    .'$PrimaryPostalAddress'.'$Country'.ISOcode
```

serializes as:

```xml
<ERExpressionGenericRelativeItemValue RelativePath="$PrimaryPostalAddress/$Country/ISOcode">
  <DataContainer>
    <ERExpressionListFirstOrNull><List>
      <ERExpressionListItemValue ItemPath="Tables/EInvoiceParameters_IT_Current/&gt;Relations/DirPartyTable_Signer" />
    </List></ERExpressionListFirstOrNull>
  </DataContainer>
</ERExpressionGenericRelativeItemValue>
```

The absolute path is **the container's path + `/` + `@RelativePath`**. Treating `@RelativePath` as
merely relative and skipping it silently drops every segment after the function call — and those
trailing segments are precisely the `$`- and `#`-prefixed calculated fields, which appear
single-quoted in the display string. Relative items nest, so a container can itself be one.

274 expressions across the samples use this shape. Six of them carry `$` segments in the relative
part; skipping those loses 8 real references in the customer-invoice mapping line alone.

### ⚠ `@ExpressionAsString` can disagree with the AST

One binding in the samples reads
`CustInvoiceJour.'$CustInvoiceTrans_OrderByLineSeqNum'.'$TaxTrans_All'.'$Amount'` in the display
string, while its AST names `…/$TaxTrans_All/SourceTaxAmountCur`. Both `$Amount` and the underlying
field exist. This is another reason the AST — not the display string — is authoritative.

AST shape: operators use named child wrapper elements rather than positional args —
`ERExpressionGenericIf` → `Condition` / `TrueValue` / `FalseValue`;
`ERExpressionNumericSubtract` → `Minuend` / `Subtraend`;
`ERExpressionStringReplace` → `Input` / `Pattern` / `Replacement` / `IsRegexp`.
~120 distinct `ERExpression*` types across the samples.

### Path grammar — segment kinds and relation navigation

The same path is serialized in **two notations**:

| Notation | Where | Separator | Relation step |
|---|---|---|---|
| **Slash** | `@ItemPath`, `@Path`, `@FieldPath`, `@ParentPath`, `@ListToGroup` | `/` | `Table/>Relations/X` |
| **Dot** | `@ExpressionAsString` | `.` | `Table.'>Relations'.X` |

**Always resolve from the slash notation.** The dot notation quotes any segment containing special
characters, and a quoted segment can itself contain a dot — `'CustVendCreditInvoicingJour.CustInvoiceJourCorrection'`
is a *single* segment — so splitting it on `.` produces garbage. Use `@ExpressionAsString` for
display only.

#### Relation navigation (`>Relations` / `<Relations`)

Table datasources expose their foreign-key graph through two pseudo-segments. In this file:
1 755 outgoing steps and 3 018 incoming ones.

| Marker | Direction | Cardinality | The segment *after* it names |
|---|---|---|---|
| `>Relations` | **Outgoing** — this table points at another | to-one | the **relation / FK on this table** |
| `<Relations` | **Incoming** — another table points at this one | to-many | the **foreign table**, optionally qualified `Table.RelationName` |

```
CustInvoiceJour />Relations/ CustTable              -- FK on CustInvoiceJour  → one customer
CustInvoiceJour />Relations/ InvoicePostalAddress_FK
CompanyInfo     /<Relations/ CompanyImage           -- CompanyImage points at CompanyInfo → many
CustInvoiceJour /<Relations/ CustVendCreditInvoicingJour.CustInvoiceJourCorrection
                                                    -- qualified: which relation, when several exist
```

The dotted `Table.RelationName` qualifier appears **only on the incoming side**, which is what you
would expect: several relations can run from the same foreign table back to this one, so the
relation name disambiguates. Outgoing steps are already unique by FK name.

Both directions yield a *list* at the relation target (`ERExpressionListItemValue`), even the to-one
outgoing case — ER models to-one as a one-element list that the expression then unwraps, typically
with `FIRSTORNULL(...)`:

```
FIRSTORNULL(CustInvoiceJour.'>Relations'.InvoicePostalAddress_FK).County
```

Relation steps chain freely and mix directions:

```
CompanyInfo/postalAddress()/>Relations/Location/<Relations/DirPartyLocation.LogisticsLocation_FK/<Relations/TaxRegistration
```

#### Segment kinds

| Form | Kind | Example |
|---|---|---|
| `Name` | Table datasource, table field, or model field | `CustInvoiceJour`, `InvoiceId` |
| `name()` | Table/class **method call** | `postalAddress()`, `find()`, `creditNote()`, `getMarkupTransactions()` |
| `>Relations` / `<Relations` | Relation navigation (above) | |
| `<Documents` | Attachments pseudo-relation, reached via `<Relations` | `CustInvoiceJour/<Relations/<Documents` |
| `$Name`, `#Name`, `_Name` | Calculated field / derived datasource | `$CustInvoiceTable`, `#DirectDebitMandate`, `_ExtCodeTable` |

**The `$` / `#` / `_` prefixes are author naming conventions, not syntax.** All of them resolve to an
`ERModelItemValueDefinition` whose `ValueSource` is an `ERModelExpressionItem` (a calculated field):

```xml
<ERModelItemValueDefinition Name="#DirectDebitMandate">
  <ValueSource>
    <ERModelExpressionItem ExpressionAsString="FIRSTORNULL(CustInvoiceJour.'&gt;Relations'.CustDirectDebitMandate)" />
```

So the resolver must look every segment up **by name in the datasource tree**, never branch on a
prefix character. Calculated fields nest — `$CustInvoiceTable` is defined as
`FIRSTORNULL(CustInvoiceJour.'#01CustInvTable')`, which is itself another calculated field — so
resolution recurses and needs the same cycle guard as the model graph.

Well-known root datasource names seen in expressions: `Tables`, `Enums`, `Parameters`, `Functions`,
`CalcFunctions`, `PrintMgmt`, `ReportDataProvider`, `ReportDataContract`.


---

## 6. Embedded label translations

`@Label` and `@Description` hold references such as `@GER_LABEL:Accepted` or `@SYS28013` rather than
text. Those are often resolvable **from the file itself**:

```xml
<Labels>
  <ERClassList>
    <Contents.>
      <ERLabel LabelId="Accepted" LabelValue="Accepté(e)" LanguageId="fr" />
```

The id is the part after the colon, or after the `@` when there is no colon. A payment model carries
**607 ids across 65 languages — 38 830 entries, which is what makes that file 5 MB**, not the model
itself (96 descriptors, 617 fields).

Two things to watch:

- **Not every export carries labels.** One whole set has none at all, so resolution must fall back
  to showing the raw id.
- **Labels do not have to ship with the thing they name.** A model can reference ids whose
  translations arrive with its mapping, so resolution should pool every loaded file.
- **Language ids are inconsistently cased** — `en-us`, not `en-US`. Compare case-insensitively, and
  treat `fr` and `fr-BE` as fallbacks for one another.

---

## 7. Output kinds beyond XML

A format is not necessarily XML. The component element names carry the output kind, and the Excel
family is named quite differently from the XML one:

| Element | Identified by | Notes |
|---|---|---|
| `ERTextFormatExcelFileComponent` | `@Name`, `@AttachmentGuid` | the workbook, bound to a template attachment |
| `ERTextFormatExcelSheet` | `@ExcelSheetName` | **no `@Name`** |
| `ERTextFormatExcelRange` | `@ExcelRange`, `@ReplicationDirection` | a named range, replicated down or across |
| `ERTextFormatExcelCell` | `@ExcelRange` (e.g. `A1`) | **`@Name` absent on 255 of 315** |
| `ERTextFormatExcelHeader` / `…Footer` | — / `@Name` | page header and footer |

Three container components appear regardless of output kind: `ERTextFormatFolderComponent`
(`@Name`), `ERTextFormatSequence` (`@Delimiter`, `@MaximalLength`) and `ERTextFormatDataItem`
(`@Type`).

**A single format can emit several files of different kinds.** One payment format has a folder
component at its root holding both an XML file (177 elements) and a workbook (101 cells).

Consequence for any UI: `@Name` is the wrong thing to key a caption on. Falling back through
`@ExcelSheetName`, `@ExcelRange`, `@Value` and finally the bound formula is what keeps a sheet of
cells from rendering as 255 rows all reading "ExcelCell".

**`@Value` is a literal typed into the component**, the only non-expression `Value` attribute in
the samples: 213 on `ERTextFormatXMLAttribute` (namespaces, fixed scheme codes) and 65 on
`ERTextFormatString`. It is normally present exactly when the component has no value binding (only
7 of the 278 also carry one, so show both rather than choosing), and then it is the only place the
emitted text exists — a search over formulas alone never finds it.
The reverse is not true: a fixed code can just as well be a string constant inside a formula, as
with the `"0225"` fallback of `cbc:EndpointID/schemeID`, so a search has to read both.

---

## 8. Rebuilding the trees from what is on disk

Three of the structures are stored flat and have to be reassembled. Getting these wrong produces a
tree that looks plausible and is subtly incorrect.

### Data model: resolve `@TypeDescriptor`, guard the cycle

Descriptors are siblings; the hierarchy is the `@TypeDescriptor` → `@Name` edge, walked from a root.
The graph *can* be recursive, so every branch must carry the set of descriptors already open above
it and stop when it meets one again. None of the three models here actually contains a cycle, so
this guard is defensive — and therefore easy to get wrong without noticing. It is worth testing
against a synthetic recursive model rather than trusting the samples.

### Data source tree: `@ParentPath` plus synthesised ancestors

**Read every declaration before attaching any of them.** A child may name a parent that appears
later in the file; attaching as you read forces that parent to be synthesised, and the real
declaration then becomes a *second* node at the same path. One mapping declares
`$notSentTransactions` as a root while 56 children name it as their parent, and the children written
before the declaration end up under the synthetic copy while the rest go under the real one —
splitting the branch in two and leaving the declared node looking like it feeds nothing.

Note that a genuine double declaration does occur (one mapping declares the same
`…/ProjInvoiceRevenue/$TaxTrans` path twice), so two nodes at one path is not by itself proof of a
parsing fault — but two nodes where one is synthesised and one is declared always is.


Data sources are a flat list. Each `ERModelItemDefinition` names its parent by path and itself by
`ERModelItemValueDefinition/@Name`; the full path is the two joined by `/`.

The parent is frequently **not** itself a declared data source. A calculated field can hang off a
model path such as `Invoice/InvoiceBase`, which exists in the model rather than in this mapping. The
missing ancestors have to be **synthesised** so the hierarchy reads the way the designer shows it —
and those synthetic rows must stay flagged as such, because the difference matters:

> **A path that is declared in the mapping is a calculated field. A path that is only synthesised is
> a model path.** The resolver relies on exactly this to know when to follow a formula onward and
> when it has reached a real model field.

### Group-by results are addressed by name, not by the field they aggregate

An `ERModelGroupByFunction` holds its grouped fields and its aggregations in wrapper elements, and
neither appears as a data source child — so a reader that only walks `ERModelItemDefinition` will
not show them at all.

More importantly, an aggregation is **read under a synthesised address**, not under the field it
computes from:

```
<group-by data source path>/aggregated/<aggregation name>

$PaymentByCreditor/aggregated/Amount
$FirstPO/OrderLine/$LineSum_GroupBy/aggregated/TotalAmount
```

130 paths across the samples are written that way. An aggregation with no `@Name` of its own — one
occurs — is addressed by the last segment of its `@FieldPath`. Index aggregations under that
address or nothing will ever be found to read them.

### A data source can be the format itself

`ERExportFormatDatasource` names a format by GUID, and paths beneath it walk **format component
names** rather than model fields:

```
format/Document/CstmrCdtTrfInitn/PmtInf/CdtTrfTxInf
```

This is how a mapping embedded in a format reads what that format produced. Segments match
components by `@Name`, skipping the file and folder components that wrap the tree.

### Reading a path *through* a data source needs its result, not its references

Two different questions get asked of a formula, and they have different answers:

- **What does this read?** Every path it mentions — the list a `FILTER` walks *and* the operands of
  the condition it tests. This is the answer for showing dependencies.
- **What does this evaluate to?** Only the value-producing branch: the list for `FILTER`, `WHERE`,
  `FIRSTORNULL` and `ORDERBY`; both branches of an `IF`; the operand of an adapter.

The distinction matters when a path continues *through* a calculated field —
`$FirstPO/ID`, where `$FirstPO` is `FIRSTORNULL(model.'$PurchPurchase')`. Appending `/ID` to what
the field evaluates to is correct; appending it to a condition operand produces a path that means
nothing.

### Matching a referenced path to a row: longest declared prefix

Referenced paths reach into fields that are not rows. `CustInvoiceJour/InvoiceId` names a table
field; only `CustInvoiceJour` is a declared data source. So a reference is attributed to the
**longest declared prefix** of its path, not to an exact match — otherwise most references resolve
to nothing at all.

### Matching a format to its mapping line

The format mapping's model data source carries a model GUID, a revision and a root descriptor. Of
those:

- the **GUID must agree** (compare parsed, not textually — the casing differs between files);
- the **root descriptor must agree when the line declares one**, and some lines declare none;
- the **version need not agree**, and requiring it rejects real pairings — one set has a format on
  v40, its mapping lines on v76 and the model itself on v99. Use version to rank candidates, not to
  filter them.

A line that declares no root descriptor agrees on the model alone. That is too weak to call it *the*
line a format uses; saying so is more useful than picking one.

---

## 9. The resolution chain (verified end to end)

Traced for real on `Invoice/InvoiceBase/Id`:

```
① Format component        ERTextFormatXMLElement @ID.="{6B2895AA-…}"  (cbc:ID)
                                    │  matched by GUID
② Format binding          ERFormatComponentPropertyBinding @Component="{6B2895AA-…}"
                          @ExpressionAsString="Invoice.InvoiceBase.Id"
                                    │  attribute sweep of the AST
③ Model path              ERExpressionStringItemValue @ItemPath="Invoice/InvoiceBase/Id"
                                    │  split first segment
④ Format datasource       ERModelItemValueDefinition @Name="Invoice"
                          → ERModelDataSourceHandler ModelGuid={4b5553d1…}
                                                     RevisionNumber=7
                                                     DataContainerDescriptorName="InvoiceCustomer"
                                    │  match on the triple
⑤ Mapping line            ERModelMapping @Model={4B5553D1…} @ModelVersion={…},7
                                         @DataContainerDescriptor="InvoiceCustomer"
                          → "Customer invoice" {B5AFF97C-…}   (line 21 505 of 7 candidates)
                                    │  lookup remainder "InvoiceBase/Id"
⑥ Model binding           ERDataContainerPathBinding @Path="InvoiceBase/Id"
                          @ExpressionAsString="IF(Parameters.'$IsQRBill',
                                                  Parameters.'$SourceJournal'.'$InvoiceId',
                                                  CustInvoiceJour.InvoiceId)"
                                    │  attribute sweep again
⑦ Datasources             Parameters/$IsQRBill, CustInvoiceJour.InvoiceId
                          → ERTableDataSourceHandler @Table="CustInvoiceJour"
```

Every hop is a **literal attribute match**. Step ⑤ is where your point about multiple mapping lines
bites: 6 of the 7 candidates are wrong, and only the `DataContainerDescriptor` + `ModelVersion` pair
picks the right one.

---

## 10. Parser traps

Each of these produces a plausible but wrong result rather than an error, which is what makes them
worth writing down.

1. **`ERExpressionSTringLen`** — capital `T`. Microsoft typo in the element name; match literally.
2. **`ModelGuid` vs `ModelGUID`** — `ERModelDataSourceHandler` uses `ModelGuid`;
   `ERModelEnumDataSourceHandler` uses `ModelGUID`. Both occur in the same file.
3. **GUID casing is inconsistent** — `{4B5553D1-…}` in one file, `{4b5553d1-…}` in another. Parse to
   a GUID and compare that; never compare the strings.
4. **Language ids are inconsistently cased too** — `en-us`, not `en-US`. Compare case-insensitively.
5. **`@SyntaxVersion`, `@Description`, `@Name`, `@DataContainerDescriptor` are all frequently
   absent.** Every attribute read must be null-tolerant.
6. **Label ids in `@Label` / `@Description`** — `@GER_LABEL:CustomerInvoice`, `@SYS28013`. Often
   resolvable from the file itself (§6), but not always; fall back to showing the raw id.
7. **`&#xA;` in `@ExpressionAsString`** — real newlines. Render multi-line.
8. **`Contents.` must be skipped**, never shown as a node.
9. **The model graph can cycle** — expand lazily with a visited-set guard. No sample actually
   cycles, so this is defensive code that will not be exercised by the files you have; test it
   against a synthetic recursive model.
10. **`ID.` is a GUID on format components but a plain name on model descriptors.** Do not type it
    as a GUID globally.
11. **BOM** — every file starts with a UTF-8 BOM.
12. **`<` and `>` in paths are XML-escaped** as `&lt;` / `&gt;` — inside `@ItemPath` and `@Path` too,
    not only in `@ExpressionAsString`. An XML parser decodes them for free, but any regex run over
    the raw file text must match the entity form.
13. **Never split `@ExpressionAsString` on `.`** — a single-quoted segment can itself contain a dot
    (`'CustVendCreditInvoicingJour.CustInvoiceJourCorrection'` is one segment). Resolve from the
    slash-notation `@ItemPath` instead; treat `@ExpressionAsString` as display-only. The two can
    also simply disagree — see §5.
14. **`$`, `#`, `_` prefixes carry no meaning** — resolve every segment by name lookup against the
    data source tree, never by prefix heuristics.
15. **`@Name` is not a reliable caption.** Excel cells and sheets carry none; 255 of 315 cells in
    the samples have no `@Name` at all. Fall back through `@ExcelSheetName`, `@ExcelRange`,
    `@Value` and finally the bound formula.
16. **Navigate to bindings, do not search for them.** A `Delta` holds its own copies of
    `ERDataContainerBinding` and `ERModelMapping`. Reading them by descendant search picks up
    customisation records as if they were live definitions; walk
    `ERModelMappingVersion/Mapping/ERModelMapping/Binding` explicitly instead.
17. **A format or a model can hold mapping lines.** Do not read `ERModelMapping` only from files
    whose envelope says "model mapping" (§3), and do not let finding one change what the file is
    taken to be (§1).
18. **Group-by aggregations are not addressed by the field they aggregate** but by
    `<group-by>/aggregated/<name>` (§8). Indexing them by field path leaves them unreachable.

## 11. Open questions

1. **`@Multiplicity`** — `1`, `20`, `200` observed. What is the enum?
2. **`@VersionStatus`** — `1` and `2` observed. Draft / Completed / Shared / Discarded mapping?
3. **`@Direction`** — `1` means import on the evidence of the line names, but the value for an
   explicit export is never written out, so only the absence is confirmed.
4. **`@IsTypeDescriptorHost`** — inline descriptor ownership?
5. **`ERModelMapping/@Format` and `@TableName`** — present but unexamined; relevant to display?
6. **`@SelectionField=2`** — read as *min*. Still exactly one occurrence across all three sets, and
   still unnamed, so still uncorroborated.

*Resolved: `@Type=9` (enum field vs enum value); the `@PropertyName` set; label resolution, which an
earlier revision of this document wrongly called impossible offline.*
