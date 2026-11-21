# Worksheet data sent to R: shared rules

**Status: draft for Iain's approval, 21 November 2026.** Nothing here is in either application yet. Once the choices in the first section are made, `examples.json` will encode them, and both applications must pass it before the worksheet R and R+ buttons are released.

## How this is used

- This file states one set of rules for both StatsDirect for Windows and StatsDirect for Mac. Both send worksheet data to R from the worksheet toolbar's **R** (StatsDirect's own R session) and **R+** (external R) buttons.
- `examples.json` (to follow) will hold worksheets, a selection and the prompt's options. Each comes with the exact R object expected.
- Each application's own test builds each worksheet in its own grid and runs its own import code. In R, the result must be `identical()` to the expected object, and the prompt must show the expected labels.
- Both applications reach these files through their pinned copy of this repository, at the next `learning-v*` tag, as with the code-block examples.
- Rules marked **(choice)** await Iain's decision; the recommended answer is given first.
- "Today" describes Windows pull request 24 (branch `preview/windows-analyse-picker`, head `43d35b6d`) and the Mac branch `codex/mac-worksheet-r-parity` (head `5022dd79`). Full detail is in `WORKSHEET-R-RULES-WINDOWS-MAC-2026-11-20.md`, the rule-by-rule comparison.

## Choices for Iain

| | Question | Recommended | Alternatives | Today |
|---|---|---|---|---|
| C1 | Name of the R object | `worksheet_data` | `data` | Windows `worksheet_data`; Mac `data`. `data` also names R's `data()` function. |
| C2 | What R shows after the import | `str(worksheet_data)`: names, types and the first values, compact for lists too | nothing; `head()` | Windows nothing; Mac `head(data)`, which prints whole vectors for a list |
| C3 | Dates and times | A column of dates with no time of day becomes R `Date`. Any other date or time column becomes ISO text: `2026-11-21 14:30:00`, `14:30:00` | serial numbers; ISO text for all | Windows serial numbers; Mac ISO text |
| C4 | A cell showing a spreadsheet error (`#DIV/0!`, `#N/A`) | `NA` (left out of a list), as StatsDirect's analyses treat it | refuse the whole import | Windows `NA`; Mac refuses |
| C5 | Column names when the first row is not used, and for a blank heading | the worksheet column letter: `A`, `B`, ... `AA` | `V1`, `V2`, ... by position; `Column A` | Windows `V1`; Mac `Column A`, or row-1 text in some cases |
| C6 | Whole rows selected | rows exactly as selected; columns from the first to the last non-blank column of those rows | as the Windows branch: columns of the used range | the two differ (rule 2.4) |
| C7 | Several areas selected | several whole columns, or several whole rows, import as one data set in sheet order; any other multiple selection is refused | refuse all multiple selections | Windows refuses all; Mac accepts several columns or rows |
| C8 | Wording of the prompt | as in section 1 | | the two differ throughout |

## 1. The prompt

| | Rule |
|---|---|
| 1.1 | **Title:** the button's tooltip. **R:** "Open in StatsDirect R". **R+:** "Open in external R" on Windows (it starts R's own window, RGui), and "Open in R or RStudio" on the Mac (it opens RStudio when installed). |
| 1.2 | **Text:** "Choose the data from “Sheet1” to copy into the new R session as worksheet_data." With no data in the sheet: "The worksheet “Sheet1” has no data to copy." |
| 1.3 | **Data choices,** in this order, with **No data** selected and focused when the prompt opens: "Entire worksheet — A1:C4 (4 rows × 3 columns)"; "Selected cells — A1:B3 (3 rows × 2 columns)"; "No data". |
| 1.4 | **A choice that is not available** stays in the list, disabled, and says why: "Entire worksheet (no data)"; "Selected cells (no selection)"; "Selected cells (select one rectangle, or whole columns or rows)"; "Selected cells (no data in the selection)". |
| 1.5 | **Then** "Import as:" with "Data frame (keep rows aligned)" (selected) and "List of vectors (independent samples)". When the list is chosen, the hint "Missing values are removed separately from each vector." is shown. |
| 1.6 | **Then two check boxes.** "Use the first row as column names" is enabled only for a data choice with more than one row. It is set to the suggestion (rule 4.1) each time the data choice changes. "Use R-friendly names" is enabled only while the first box is ticked, and is unticked when the prompt opens. While the prompt is open it keeps the user's tick. |
| 1.7 | **Today:** the titles, text, labels, order, hint and check-box behaviour differ in detail on every line (comparison rules 17, 21 and names 3-4). **Change:** both. |

## 2. Which cells are sent

| | Rule | Today |
|---|---|---|
| 2.1 | **Entire worksheet:** the smallest rectangle on the sheet in view that holds every non-blank cell. A constant or a formula counts, and so does text of spaces only. Cells with formatting only do not count. Hidden rows and columns are included. | same on both |
| 2.2 | **A rectangle** keeps its explicit bounds. Cells inside it beyond the data are blank. A selection with no non-blank cell at all is not available (rule 1.4). | same, except that the Mac offers a rectangle of blank cells |
| 2.3 | **Whole columns:** the selected columns, from the first to the last non-blank row of those columns. | same, except several columns (C7) |
| 2.4 | **Whole rows (C6):** the selected rows exactly, from the first to the last non-blank column of those rows. | the two differ |
| 2.5 | **Several areas (C7):** several whole columns, or several whole rows, import as one data set in sheet order. Any other combination is refused. | Windows refuses all; Mac accepts columns or rows |
| 2.6 | **Select all** (the corner, or Ctrl+A or Command-A) is the same as Entire worksheet. | Windows; the Mac gives a rectangle |
| 2.7 | **A merged area** sends its value in its top-left cell; the other cells are blank. | same |
| 2.8 | **One cell** is a valid selection. The first-row check box is then disabled. | same |

## 3. What values arrive in R

**3.1 Missing values** (both formats; `NA` in a data frame, left out of a list):

- a blank cell;
- text that is empty or only spaces;
- these markers, ignoring case and surrounding spaces: `*` `.` `MISSING` `NA` `N/A` `NaN` `NULL`;
- a spreadsheet error (C4).

This is the rule StatsDirect's own analyses use, so an import and an analysis treat the same cells as missing. **Today:** Windows makes all of these text, so a single `*` turns a numeric column into text. The Mac does so for all but `*` and blanks. **Change:** both.

**3.2 Numbers stored as text:** after trimming, text that reads as a finite number in the invariant culture counts as that number: digits with an optional sign, point and exponent, and no thousands separators. **Today:** text on both. **Change:** both.

**3.3 The type of each column,** judged on its non-missing cells:

| If every non-missing cell is | The column is |
|---|---|
| a number (rule 3.2 included) | numeric |
| `TRUE` or `FALSE` as a spreadsheet Boolean | logical |
| a date (C3) | per C3 |
| missing (every cell) | logical `NA` (R's own default) |
| anything else | character |

**Today:** an all-missing column is numeric on Windows. Booleans mixed with numbers become numeric on the Mac.

**3.4 Values in a character column:**

- text exactly as in the cell;
- a number as the shortest decimal that names that double exactly;
- a Boolean as `TRUE` or `FALSE`;
- a date as ISO text;
- missing markers as `NA`.

**Today:** the Mac's `c()` coercion keeps only 15 significant digits of a number in a mixed column, so the value is lost.

**3.5 Numbers are exact:** every finite number arrives in R `identical()` to the double in the worksheet. Each application proves its own way of writing numbers with its own test, over the example values and a large random sample. As measured on 21 November 2026:

- **Windows** writes 17 significant digits in its data file. Windows R 4.6.2 (x86-64) read back all 680,000 test values exactly, subnormal numbers included. The shortest decimal was misread about once in 2,000 values, at every length.
- **The Mac** writes the shortest decimal only when it has at most 15 digits as written, and the power of ten it needs is within 10^±22. Otherwise it writes a hexadecimal significand, and for a subnormal number a product of two exact hexadecimal values. On Apple Silicon R reads `0x1p-1074` as 0 and misreads decimals outside that band.

A negative zero may arrive as 0.

**3.6 Text** arrives exactly as in the cell: spaces kept, UTF-8, quotes, backslashes, newlines, tabs and other control characters intact. **Today:** on Windows a cell holding Ctrl-Z (U+001A) cuts the import short at that row. **Change:** Windows.

**3.7 Formula cells** send their current value. The Mac shows the value Excel saved in the file, and refuses the import when such a value is missing or out of date; that difference stays (section 7).

## 4. Headings and names

| | Rule | Today |
|---|---|---|
| 4.1 | **The first row is suggested as headings** when the data has at least two rows and every non-blank first-row cell is text with a letter in it, not numeric text (rule 3.2), not TRUE or FALSE in any case, and not a missing marker (rule 3.1). At least one such cell is needed. A formula cell counts by its value. | the details differ (comparison names 0-1) |
| 4.2 | **Without headings,** column names follow C5. The first row stays in the data. | Windows `V1`; Mac `Column A`, or row-1 text when its grid treats row 1 as titles |
| 4.3 | **A blank, spaces-only or error heading cell** takes the C5 name. The import goes on. | the two differ |
| 4.4 | **Headings are kept exactly,** with no trimming and no change by R, even a heading such as `check.names`. | Windows exact; the Mac's `as.data.frame()` alters a few |
| 4.5 | **A heading from a number, Boolean or date** (when the box is ticked by hand) is its rule 3.4 text. | the two differ |
| 4.6 | **"Use R-friendly names"** is `make.unique(make.names(gsub("[[:space:]]+", "_", trimws(names))), sep = "_")`. | same on both |
| 4.7 | **List elements** take the same names as the data frame's columns. | same |

## 5. The R object

| | Rule |
|---|---|
| 5.1 | **One object,** named per C1, in R's global environment of the new session. |
| 5.2 | **Data frame:** columns in sheet order, row names `1:n`, character columns never factors. |
| 5.3 | **List of vectors:** a named list, each element its column with missing values removed. |
| 5.4 | **After the import** R shows what C2 decides. |
| 5.5 | **The script's comments say,** on both platforms: `# Data copied from the worksheet “Sheet1”, A1:C4.` and `# Blank cells, missing-value markers and spreadsheet errors are NA.` For a list, also `# Missing values are removed separately from each vector.` Plus one line on dates, per C3. |

## 6. Delivery and files

| | Rule | Change |
|---|---|---|
| 6.1 | **R+ keeps the user's own R start-up file.** `.Rprofile` still runs, and nothing StatsDirect sets is inherited by R processes started from that session (`parallel`, `Rscript`, `callr`). | both |
| 6.2 | **Data is copied only where R needs it.** Temporary copies are removed when R has read them or the session ends, and when StatsDirect itself closes. A copy is kept only in a file the user saves. | Windows: the integrated session's copy stays after exit. Mac: every R+ import keeps a permanent copy. |
| 6.3 | **A saved worksheet script carries its data,** named after the script, so that saving again replaces it. The user is told the data file's name. Saving still works, with a message, if the temporary copy has gone. | Windows |
| 6.4 | **"No data"** opens the session with no import. | the session windows differ by platform; that stays |

## 7. Differences that stay

- **Formulas:** the Mac grid holds the values Excel saved in the file and cannot recalculate. So the Mac refuses an import whose formula values are missing or out of date ("Recalculate and save this workbook in Excel, then reopen it before using formula cells in R."). Windows recalculates and sends current values.
- **Size:** Windows sends the data in a file and has no limit of its own. The Mac writes the values into the R script, which has its 8 MiB limit.
- **The grid's size:** the Mac grid ends at its own size, so a selection cannot reach beyond it.
- **No selection:** Windows always has an active cell, so "Selected cells" is offered as that cell. The Mac's grid can have no selection, so the choice is disabled ("no selection").
- **Where R runs:** each platform's own session windows and external R (RGui on Windows; R.app or RStudio on the Mac).
