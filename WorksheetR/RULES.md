# Worksheet data sent to R: shared rules

**Status: approved by Iain on 21 November 2026** ("agree", to the recommended answer of every choice below), and tagged `learning-v1.1.7`.

**Amendment 1** (`learning-v1.1.8`), agreed by the Windows and Mac sessions while building to these rules, 21 November 2026:

- dates and date-times anchor the missing-value words; times of day and durations do not (rule 3.2);
- a date-time column is defined, and a column that also holds a time of day or a duration is an ordinary character column (rules 3.6 and 3.9);
- formats such as `mm:ss` give times of day (rule 3.9);
- a script the user has saved in R.app's editor is kept (rule 6.2);
- a value beyond 9999-12-31, or a date before its date system, is a number, and colour codes in a format do not make a duration (rule 3.9);
- every control character in the sheet name is a space in the script's comment (rule 5.5);
- the user's own `R_PROFILE_USER` is honoured, and the user's start-up file cannot change the imported values (rule 6.1);
- five examples pin these.

- This version takes in the Mac session's review of the first draft: 54 findings, each checked by a second reader, numbered M1 to M29 below where they changed a rule.
- `examples.json` encodes these rules, and both applications must pass it before the worksheet R and R+ buttons are released.

## How this is used

- **One set of rules** for StatsDirect for Windows and for Mac. Both send worksheet data to R from the worksheet toolbar's **R** button (StatsDirect's own R session) and **R+** button (external R).
- **`examples.json`** holds worksheets, a selection and the prompt's options. Each example gives every cell's stored type, not keystrokes, and the exact R object expected.
- **Each application's own test** builds each worksheet in its own grid and runs its own import code. In R, the result must be `identical()` to the expected object, and the prompt must show the expected labels.
- **Expected data frames** are written as `structure(list(...), names = c(...), row.names = c(NA_integer_, -nL), class = "data.frame")`. This is `identical()` to `data.frame(..., check.names = FALSE)`, and it can also express names that `data.frame()` cannot build.
- **Both applications reach these files** through their pinned copy of this repository, at the next `learning-v*` tag.
- **Choices:** the nine in the first section were decided on 21 November 2026: the recommended answer in every case.
- **"Today"** describes Windows pull request 24 (`preview/windows-analyse-picker`, `43d35b6d`) and Mac pull request 23 (`codex/mac-worksheet-r-parity`, `5022dd79`). The rule-by-rule comparison is in `WORKSHEET-R-RULES-WINDOWS-MAC-2026-11-20.md`.

## Choices (decided: the recommended answer in every case)

| | Question | Recommended | Alternatives | Today |
|---|---|---|---|---|
| C1 | Name of the R object | `worksheet_data` | `data` | Windows `worksheet_data`; Mac `data`. `data` is also the name of R's `data()` function. |
| C2 | What R shows after the import | `str(worksheet_data)`: names, types and the first values, compact for lists too | nothing; `head()` | Windows nothing; Mac `head(data)`, which prints whole vectors for a list |
| C3 | Dates and times | A column of dates with no time of day becomes R `Date`. Date-times, times of day and durations become text (rule 3.9). | serial numbers; text for all | Windows serial numbers; Mac text |
| C4 | A cell holding a spreadsheet error (`#DIV/0!`, `#N/A`) | `NA` (left out of a list). This matches Windows analyses. On the Mac, analyses refuse error cells, so the import and the analyses would disagree there unless the Mac's analyses change too (M15). | refuse the whole import | Windows `NA`; Mac refuses |
| C5 | Column names when the first row is not used, and for a blank heading | the worksheet column letter: `A`, `B`, ... `AA`. Column 365 is then named `NA`, reachable as ``d$`NA` `` or `d[["NA"]]` (M20). | `V1`, `V2`, ... by position, as R's own `read.csv` | Windows `V1`; Mac `Column A`, or row-1 text in some cases |
| C6 | Whole rows selected | from the first to the last selected row that holds a non-blank cell (blank selected rows between them stay, as `NA` rows), and across from the first to the last non-blank column of those rows (M22) | every selected row | the two differ (rule 2.4) |
| C7 | Several areas selected | several whole columns, or several whole rows, import as one data set in sheet order; any other multiple selection is refused | refuse all multiple selections | Windows refuses all; Mac accepts several columns or rows |
| C8 | Wording of the prompt | as in section 1 | | the two differ throughout |
| C9 | Numbers stored as text | Such text counts as the number (rule 3.4), except text with a leading zero, which stays text: after white space, an optional sign, then `0` followed by another digit (`0012`, `-007`, `00.5`). `0`, `0.5`, `-0.25` and `0e5` are numbers. Codes such as IDs and postcodes therefore stay text (M3). | every numeric text counts as a number; none does | text on both |

## 1. The prompt

| | Rule |
|---|---|
| 1.1 | **Title:** the button's tooltip. **R:** "Open in StatsDirect R". **R+:** "Open in external R" on Windows, which starts R's own window, RGui. On the Mac, "Open in R or RStudio", which opens RStudio when it is installed. |
| 1.2 | **Text:** "Choose the data from “Sheet1” to copy into the new R session as worksheet_data." For Mac R+: "Choose the data from “Sheet1” to copy into R as worksheet_data.", since RStudio runs it in the user's own session (M24). With no data in the sheet: "The worksheet “Sheet1” has no data to copy." |
| 1.3 | **Data choices,** in this order. **No data** is selected when the prompt opens, and focused where the platform gives that control focus (M23). The choices: "Entire worksheet — A1:C4 (4 rows × 3 columns)"; "Selected cells — A1:B3 (3 rows × 2 columns)"; "No data". |
| 1.4 | **Counts:** written in digits with commas, whatever the locale: "1,048,576 rows", "1 row", "1 column". **Cells:** written in A1 notation (M21). One area is `B5:D9`, also for whole columns or rows after trimming. A single cell is written alone, `A1`, not `A1:A1`: as one area, within a union, and in the script's comment. Several areas are written as Excel writes a union, in sheet order, with adjacent lines merged: `C3:C4,E3:E4`. The prompt lists at most three areas, then `,…` (U+2026, as with `—` and `×` in the same labels); the script's comment lists them all. |
| 1.5 | **A choice that is not available** stays in the list, disabled, and says why: "Entire worksheet (no data)"; "Selected cells (no selection)"; "Selected cells (select one rectangle, or whole columns or rows)"; "Selected cells (no data in the selection)". |
| 1.6 | **Then** "Import as:", with "Data frame (keep rows aligned)" selected and "List of vectors (independent samples)". It is disabled while No data is chosen. When the list is chosen, the hint "Missing values are removed separately from each vector." is shown. |
| 1.7 | **Then two check boxes.** "Use the first row as column names" is enabled only for a data choice with more than one row. It is set to the suggestion (rule 4.1) each time the data choice changes. "Use R-friendly names" is enabled only while the first box is ticked, and is unticked when the prompt opens. While the prompt is open it keeps the user's tick. |
| 1.8 | **Buttons:** "Open" (the default) and "Cancel". |
| 1.9 | **Today:** the titles, text, labels, order, hint, counts and check-box behaviour differ in detail on every line. **Change:** both. |

## 2. Which cells are sent

| | Rule | Today |
|---|---|---|
| 2.1 | **Entire worksheet:** the smallest rectangle on the sheet in view that holds every non-blank cell. A constant or a formula counts, and so does text of white space only. Cells with formatting only do not count. Hidden rows and columns are included. | same on both |
| 2.2 | **A rectangle** keeps its explicit bounds. Cells inside it beyond the data are blank. A selection with no non-blank cell at all is not available (rule 1.5). | same, except that the Mac offers a rectangle of blank cells |
| 2.3 | **Whole columns:** the selected columns, from the first to the last non-blank row of those columns. A selected column with no data stays, as a column of `NA`. | same, except several columns (C7) |
| 2.4 | **Whole rows:** per C6. | the two differ |
| 2.5 | **Several areas:** per C7. | Windows refuses all; Mac accepts columns or rows |
| 2.6 | **Select all** (the corner, Ctrl+A or Command-A) is the same as Entire worksheet. | Windows. On the Mac, Command-A gives a rectangle from A1 across every grid column, and the corner selects every grid row, sent as whole rows from A1 (M29). |
| 2.7 | **A merged area** sends its value in its top-left cell; the other cells are blank. | same |
| 2.8 | **One cell** is a valid selection. The first-row check box is then disabled. | same |

## 3. What values arrive in R

**3.1 Words used below.**

- **White space** is Unicode White_Space: U+0009 to U+000D, U+0020, U+0085, U+00A0, U+1680, U+2000 to U+200A, U+2028, U+2029, U+202F, U+205F and U+3000. This is what .NET's `String.Trim()` removes. U+200B and U+FEFF are not white space (M8).
- **Trimmed** means with white space removed from both ends.
- **A letter** is a code point of Unicode category L: JavaScript `/\p{L}/u`, .NET `Rune.IsLetter`. It is judged on code points, not on UTF-16 units (M17).

**3.2 Missing values:** `NA` in a data frame, left out of a list (M1, M12, M15).

- **In any column:**
  - a blank cell;
  - empty or white-space text;
  - the text `*`, `* (missing)` or `.` (`* (missing)` is what StatsDirect writes for a missing category);
  - the words `NA`, `N/A`, `NaN`, `NULL` and `MISSING`, written exactly so;
  - a formula whose value is empty text;
  - a spreadsheet error (C4): a cell holding an error value (`#DIV/0!`, `#N/A`, `#NAME?`, `#NULL!`, `#NUM!`, `#REF!`, `#VALUE!`, `#SPILL!`, `#CALC!` and the like). Text that only looks like an error stays text.
- **Also in any case** (`na`, `n/a`, `nan`, `Null`): the same words count, in a column whose other non-missing cells are all numbers, all Booleans, or all dates and date-times, and that has at least one such number, Boolean, date or date-time. Times of day and durations do not count, so {`14:30:00`, `na`} stays character, `c("14:30:00", "na")`, while {`2026-11-21 10:00`, `na`} gives `c("2026-11-21 10:00:00", NA)`. All such words in a column are set aside together when its other cells are judged, so {1, `na`, `Null`, 2} is numeric. So {`Nan`, `Nan`} (two people called Nan) stays character, and {`na`, 1} is numeric.
- **Surrounding white space** is ignored.
- **So real words survive** in text columns: `Na`, `Nan`, `Null`, `Missing`. Examples: {`Na`, `K`, `Cl`} is character; {`1`, `na`, `2`} is numeric; {`Alive`, `Missing`} is character; {`Smith`, `NA`} gives `c("Smith", NA)`; {TRUE, `na`} is logical.
- **How this compares with the analyses** (M2): these are the markers StatsDirect treats as missing when it reads row-aligned columns. Ordinary numeric analyses differ by platform. The Mac takes only blank and `*`, and stops on other text. Windows takes any non-numeric cell as missing.
- **Today:** Windows makes all of these text, so a single `*` turns a numeric column into text. The Mac makes blank cells `NA`, and `*` only in an otherwise numeric column; other markers, `*` elsewhere and white-space text stay text. **Change:** both.

**3.3 Numbers.** A number cell, or text that reads as a number (rule 3.4), counts as a number. These are also numbers, in a column whose other non-missing cells are numbers and that has at least one number, so that {`Inf`} alone stays text and {`Inf`, 1} is numeric (M10):

- `Inf`, `-Inf`, `Infinity` and `-Infinity` in any case, as plus or minus `Inf`;
- `NaN` counts as missing (rule 3.2).

**3.4 Numbers stored as text** (C9, M9). Text counts as a number when, after trimming:

- it matches `^[+-]?([0-9]+(\.[0-9]*)?|\.[0-9]+)([eE][+-]?[0-9]+)?$` (ASCII digits only);
- its value is finite;
- it has no leading zero (C9).

`1e-400` is 0. `1e999`, hexadecimal text, `Infinity` and `NaN` are not numbers by this rule; rule 3.3 covers the last two, so `1e999` and hexadecimal text stay text. The same test applies to number cells that keep their typed text, as the Mac grid's do.

**3.5 Booleans** (M11). A spreadsheet Boolean, or text that is `TRUE` or `FALSE` in any case after trimming, counts as a Boolean when the column's type is decided. Excel makes a typed TRUE a Boolean; the Mac grid stores it as text.

**3.6 The type of each column,** judged on its non-missing cells:

| If every non-missing cell is | The column is |
|---|---|
| a number (rules 3.3 and 3.4) | numeric: double, never integer, even when every value is whole (M19) |
| a Boolean (rule 3.5) | logical |
| a date with no time of day (C3, rule 3.9) | `Date`, double storage: `structure(20778, class = "Date")` |
| a date or a date-time, at least one with a time of day | character: a date-time column (rule 3.9) |
| missing (every cell) | logical `NA`, R's own default; `logical(0)` in a list |
| anything else | character |

**Today:** an all-missing column is numeric on Windows, and Booleans mixed with numbers become numeric on the Mac.

**3.7 Values in a character column** (M4, M7):

- **A text cell** keeps its exact text, even text that reads as a number or a Boolean: `1.50` stays `1.50`, and `true` stays `true`.
- **A number cell** is written as ECMAScript's `Number::toString`, which is JavaScript's `String(x)`: the shortest digits that name the double, e-notation below 1e-6 and from 1e21, and -0 written as `0`. Examples: `100000`, `0.00001`, `1e-7`, `1.2e-7`, `0.30000000000000004`, `123456789012345680000`, `1e+21`, `5e-324`, `1.7976931348623157e+308`. .NET's own text differs (`1E-07`, `-0`). The Mac host has a C# port, `JsNumber` in `FullEngine/WorkbookIO.cs`, that matched `String(x)` on 1,200,000 values.
- **A spreadsheet Boolean** is `TRUE` or `FALSE`.
- **A date** is its rule 3.9 text.
- **Missing markers** are `NA`.
- **Today:** the Mac's `c()` coercion keeps only 15 significant digits of a number in a mixed column, and writes 100000 as `1e+05`.

**3.8 Numbers are exact** (M5, M6). Every finite number arrives in R `identical()` to the double in the worksheet; a negative zero may arrive as 0. Plus and minus `Inf` (rule 3.3) are written `Inf` and `-Inf`. Both applications write a finite number, in R code or in a data file, in one form:

- **the shortest decimal,** when it has at most 15 digits, no exponent and at most four digits after the point;
- **otherwise R's hexadecimal form,** `0x1.<13 hex digits>p<exp>`;
- **a subnormal** (below 2.2250738585072014e-308) as `0x0.<13 hex digits>p-1022`.

Why this form:

- **Every R reads it exactly.** With an 8-byte long double (Apple Silicon), R does one correctly rounded operation on exact values. With an 80-bit long double (Windows and Intel R), a wrong second rounding would need a longer run of equal bits than the four permitted decimal places can produce.
- **The C `%a` form must not be used for subnormals:** every R reads `0x1p-1074` as 0.
- **It stays readable:** `c(12, 13.5, 0.1, 98.6, -0.25, 0x1.5bf0a8b145769p+1)`.

Measured on 21 November 2026:

- **Windows R 4.6.2 (x86-64):** 0 misreads of 679,125 values, 23,957 of them subnormal, both through `read.csv` and as R code.
- **Mac R 4.6.1 (Apple Silicon), and arm64 and x86-64 builds of R's own reader:** 0 misreads of 1,638,463 values (Mac session).
- **Other forms fail:** 17 significant digits is exact only with an 80-bit long double. Apple Silicon R misread 16,362 of 100,000 values, so a saved Windows script and its data opened on a Mac would not be exact. The shortest decimal alone was misread about once in 2,000 values on Windows.

Each application's own test proves the form over the example values and a large random sample.

**3.9 Dates and times** (C3, M16):

- **A date column,** judged by value: every non-missing cell is a date or date-time whose time, rounded to the millisecond, is 00:00:00, whatever its format. The class is `Date`, as in rule 3.6.
- **Precision:** every date-time, time of day and duration is rounded to the millisecond. A point and one to three digits follow the seconds only when there is a fraction: `14:30:00.5`.
- **A time of day:** a value below one day, with a format that has no date. It is written `HH:MM:SS`. Formats such as `mm:ss` and `mm:ss.0` (Excel's built-in formats 45 and 47) have no date, so they give times of day: 90.5 seconds is `00:01:30.5`. A value of one day or more in such a format is a date or a date-time, judged by value as above.
- **A duration** (an elapsed-time format): written `[-]HH:MM:SS`, with hours not wrapped: `26:00:00`, `-01:00:00`. An elapsed-time format has, outside quotes, a bracket holding one of the letters h, m or s alone, repeated: `[h]`, `[hh]`, `[mm]`, `[ss]`. Other brackets are colours, locales and conditions (`[Red]`, `[Magenta]`, `[$-409]`, `[>=100]`): `[Magenta]yyyy-mm-dd` is a date format.
- **Out of range:** 9999-12-31 is the last day of either date system: serial 2,958,465 in the 1900 system and 2,957,003 in the 1904 system. A value in a date, time or duration format beyond it, as a number of days, and a value in a date or time format before the first day of its date system, is a number: 2958466 and -1 in `yyyy-mm-dd` give `2958466` and `-1`.
- **A date-time:** written `YYYY-MM-DD HH:MM:SS`. In a date-time column midnight is written `00:00:00`, because `as.POSIXct()` drops the time from every value if one lacks it.
- **A date-time column:** every non-missing cell is a date or a date-time, and at least one has a time of day (rule 3.6). A column that also holds a time of day or a duration is an ordinary character column, where a date is written `YYYY-MM-DD`: {`2026-11-21`, `14:30:00`} gives `c("2026-11-21", "14:30:00")`.
- **A bare `YYYY-MM-DD`** appears only in a character column or a heading.
- **Serial 60** (the nonexistent 1900-02-29) gives `NA`.
- **The examples** give calendar dates. Each loader's date system (1900 or 1904) is tested separately.

**3.10 Text** arrives exactly as in the cell: white space kept, UTF-8, and quotes, backslashes, newlines, tabs and other control characters intact (M13, M14).

- **NUL (U+0000):** R cannot hold it. The import is refused with the message "Cell A1 contains a null character, which R cannot hold. Remove it, then open the R import again.", naming the first such cell in sheet order (rows, then columns).
- **A lone surrogate** arrives as U+FFFD. Each application tests this in its own tests.
- **Text written inside R code** (a string literal: a heading, a name or a value) uses `\u` escapes for control characters and for the bidi controls U+202A to U+202E and U+2066 to U+2069. A raw bidi control in a string makes R refuse the whole script ("bidi formatting not allowed, use escapes instead"). This was checked on Windows R 4.6.2 and Mac R 4.6.1. Comments may hold them.
- **Today:** on Windows a cell holding Ctrl-Z (U+001A) cuts the import short at that row; Windows already writes every non-ASCII character in its script's literals as a `\U` escape. The Mac writes bidi controls raw. **Change:** both.

**3.11 Formula cells** send their current value. The Mac sends the value Excel saved in the file instead (section 7).

## 4. Headings and names

| | Rule | Today |
|---|---|---|
| 4.1 | **The first row is suggested as headings** when the data has at least two rows and every non-blank first-row cell is text with a letter in it, not numeric text (rule 3.4), not a Boolean (rule 3.5), and not a missing marker written exactly so (rule 3.2's any-column list: `*`, `* (missing)`, `.`, `NA`, `N/A`, `NaN`, `NULL`, `MISSING`). So a heading `Na` over numbers is a heading. At least one such cell is needed. With the box unticked, the data rule stands: a first-row `Na` over numbers is `NA`. Blank and white-space cells are passed over. A formula cell counts by its value. | the details differ |
| 4.2 | **Without headings,** column names follow C5. The first row stays in the data. | Windows `V1`; Mac `Column A`, or row-1 text when its grid treats row 1 as titles |
| 4.3 | **A blank, white-space or error heading cell** takes the C5 name. The import goes on. A heading's letter name can repeat another heading (`B`, `B`); R-friendly names then give `B`, `B_1` (M20). | the two differ |
| 4.4 | **Headings are kept exactly,** with no trimming and no change by R. This holds for repeated names, and for names that `data.frame()` itself uses (`row.names` twice, `check.rows`, `check.names`, `fix.empty.names`, `stringsAsFactors`), names starting `..adfl.`, and very long headings. So the data frame is built with base functions only (`structure()`), so that a user's start-up file cannot change the import (M18). | Windows exact; the Mac's `as.data.frame()` fails on repeated argument names |
| 4.5 | **A heading from a number, Boolean or date,** when the box is ticked by hand, is its rule 3.7 text. | the two differ |
| 4.6 | **"Use R-friendly names"** is `make.unique(make.names(gsub("[[:space:]]+", "_", trimws(names))), sep = "_")`. | same on both |
| 4.7 | **List elements** take the same names as the data frame's columns. | same |

## 5. The R object

| | Rule |
|---|---|
| 5.1 | **One object,** named per C1, in the global environment of the R session that runs the import. With RStudio, that is the user's own session, once they run the script (M24). |
| 5.2 | **Data frame:** columns in sheet order; automatic integer row names (compact `c(NA, -n)` or `1:n`, not character); character columns never factors (M19). |
| 5.3 | **List of vectors:** a named list, each element its column with missing values removed. A column with no non-missing value is `logical(0)` (M19). |
| 5.4 | **After the import** R shows what C2 decides. |
| 5.5 | **The script's comments** are the same on both platforms, with the range text of rule 1.4 listing every area, and each control character in the sheet name written as a space: U+0000 to U+001F, CR and LF among them, and U+007F to U+009F, so CR LF gives two spaces (M25). On Windows R stops reading a script at a raw Ctrl-Z (U+001A). For example: `# Data copied from the worksheet “Sheet1”, A1:C4.` and `# Blank cells, missing-value markers and spreadsheet errors are NA.` For a list, also `# Missing values are removed separately from each vector.` When the copied cells include a date, date-time, time of day or duration, also `# Dates without a time of day are R Date values; other dates and times are text.` |

## 6. Delivery and files

**6.1 R+ keeps the user's own R start-up file** (M26). StatsDirect's own start-up file:

- unsets `R_PROFILE_USER` (never sets it to `""`);
- then runs the user's profile as R would: the file the user's own `R_PROFILE_USER` names, when the user set one before StatsDirect started R, else `./.Rprofile`, else `~/.Rprofile`, in the global environment, printing what it prints; R processes started from the session get the user's own `R_PROFILE_USER` back, or none;
- reads its own files without re-encoding them, so the user's start-up file cannot change the imported values: `options(encoding = "latin1")` in it leaves `café` as `café`;
- leaves the user's `.First` to run.

So nothing StatsDirect sets is inherited by R processes started from that session (`parallel`, `Rscript`, `callr`). **Change:** Windows (child processes fail today, and the user's profile is skipped); Mac (the user's profile is skipped).

**6.2 Data is copied only where R needs it** (M27).

- **A copy that R reads by itself** (the integrated session's input, or the script RGui or R.app is started to run) is removed once R has read it or that R session ends. R.app also shows its script in its editor, so for R.app the copy is removed when the session ends, unless the user has saved changes to it in R.app's editor: then it is the user's file, and it is kept.
- **Any copy left over** is removed when StatsDirect closes or next starts.
- **A script given to RStudio,** which runs it only when the user does, is the user's file. It is one uniquely named file per import in StatsDirect's R Scripts folder, with its full path shown, and no other copy is made.
- **The folder R uses as its working directory is never deleted,** because users save files there.
- **Today:** Windows leaves the integrated session's copy after StatsDirect exits. On the Mac, every R+ import keeps two permanent copies, and the integrated session's private event log in `$TMPDIR` holds each command's full text, data included, until the session ends (after a crash it stays). **Change:** both.

**6.3 A saved worksheet script carries its data.** On Windows that is a companion data file named after the script, so that saving again replaces it, and the user is told its name. Saving still works, with a message, if the temporary copy has gone. **Change:** Windows.

**6.4 "No data"** opens the session with no import. The session windows differ by platform; that stays.

## 7. Differences that stay

- **Formulas:** the Mac grid holds the values Excel saved in the file and cannot recalculate. So the Mac refuses an import whose formula cells have no saved value, or whose workbook has been edited since it was opened (any cell, any sheet). The message is "Recalculate and save this workbook in Excel, then reopen it before using formula cells in R." A saved empty-text result is missing (rule 3.2), not a reason to refuse (M12). Windows recalculates and sends current values.
- **Size:** Windows sends the data in a file and has no limit of its own. The Mac writes the values into the R script, at most 8 MiB for R and R+ alike: about 1.1 million short typed values, or 380,000 full-precision ones.
- **Typed entry:** the two grids store some keystrokes differently. Typed `0012` is text on the Mac but 12 on Windows, as in Excel. Typed `TRUE` is text on the Mac but a Boolean on Windows. For TRUE, rule 3.5 gives the same result on both. For `0012` the result still differs, `"0012"` on the Mac and 12 on Windows, because C9 keeps leading-zero text as text. The examples give stored types (M3, M11).
- **No selection:** Windows always has an active cell, so "Selected cells" is offered as that cell. The Mac grid can have no selection, so the choice is disabled ("no selection").
- **The grid's size:** the Mac grid ends at its own size, so a selection cannot reach beyond it.
- **Where R runs** (M24, M28):
  - **Windows:** R starts StatsDirect's own R session window, and R+ starts RGui.
  - **The Mac's R button** needs Apple Silicon R 4.2 or later, and starts R without the user's start-up files, in a UTF-8 locale.
  - **The Mac's R+** uses whatever R that R.app or RStudio runs, possibly Intel R. RStudio opens the script in the user's current session and runs nothing until the user does; it is not a new session, and it can overwrite an object of the same name. Without R.app or RStudio, R+ saves the script and says so.

## Example values to pin in `examples.json`

- **Numbers:** `0.30000000000000004`, `1/3`, `5.499065962e-14`, `84814136847853600000`, `1e22`, `1e23`, `9007199254740993`, `1.7976931348623157e308`, `2.2250738585072014e-308`, `5e-324`, `-0`, `0.883020325331`, `12`, `13.5`, `0.1`, `98.6`, `-0.25`.
- **Missing and Booleans:** {`Na`, `K`, `Cl`}, {`1`, `na`, `2`}, {`Alive`, `Missing`}, {`Smith`, `NA`}, {TRUE, `na`}.
- **White space:** `" 13"`, `"13\u0085"`, `"　"` (missing), `"﻿13"` (text).
- **Codes and non-finite:** {`0012`, `-007`, `00.5`} (text), {`0`, `0.5`, `-0.25`, `0e5`} (numbers), {`1`, `Inf`, `-infinity`}.
- **No anchor:** {`Nan`, `Nan`} (character), {`Inf`} (text), {`Inf`, 1} (numeric), {`na`, 1} (numeric).
- **Number cells in a character column:** `100000`, `0.00001`, `1e-7`, `1.2e-7`, `1e+21`, and `-0` written `0`.
- **Boolean text:** `true` and `True` in a logical column and in a character column.
- **Headings:** {`Na`, 140, 138} with {`K`, 4.1, 3.9} is suggested as headings.
- **Names:** column 365 (`NA`), a blank heading beside `B`, and repeated `row.names`, `check.rows` and `stringsAsFactors` headings.
- **Text:** a cell `"a⁨b⁩‮c"`, a NUL (refused), a lone surrogate, and a formula with empty text.
- **Dates:** a date column, a midnight date-time beside one with a time, a time of day, a duration over 24 hours, a duration with a fraction (`14:30:00.5`), and serial 60.
- **Amendment 1:** {`2026-11-21 10:00`, `na`}, {`14:30:00`, `na`} and a duration with `na`; {`2026-11-21`, `2026-11-21 10:00`} beside {`2026-11-21`, `14:30:00`}; `mm:ss` and `mm:ss.0` values; `[Magenta]` and `[Red]` in date, date-time and time formats beside `[h]:mm`; 2958466 and -1 in `yyyy-mm-dd` and a duration of 3,000,000 days.
