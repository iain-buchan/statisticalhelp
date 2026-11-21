"""Generate WorksheetR/examples.json from case definitions, writing each expected R object in the shared number form.

Each case: the cells (by A1 reference, with stored type), the selection, the prompt's options, and what is expected: the
labels of the data choices, whether the first row is suggested as headings, and the R expression whose value the import
must be identical() to (or a refusal).
"""
import json, math, sys

# ---------- R text ----------

def rnum(v):
    """A double in the shared form (RULES.md 3.8)."""
    if v is None:
        return 'NA'
    if math.isinf(v):
        return 'Inf' if v > 0 else '-Inf'
    if v == 0:
        return '0'
    r = repr(v)
    if 'e' not in r:
        if r.endswith('.0'):
            r = r[:-2]
        intpart, _, frac = r.lstrip('-').partition('.')
        digits = (intpart + frac).lstrip('0')
        if len(digits) <= 15 and len(frac) <= 4:
            return r
    return v.hex()


def rstr(s):
    """An R string literal: ASCII only, with escapes for everything else."""
    if s is None:
        return 'NA'
    out = ['"']
    for ch in s:
        c = ord(ch)
        if ch == '\\': out.append('\\\\')
        elif ch == '"': out.append('\\"')
        elif ch == '\n': out.append('\\n')
        elif ch == '\r': out.append('\\r')
        elif ch == '\t': out.append('\\t')
        elif 0x20 <= c < 0x7f: out.append(ch)
        elif c <= 0xffff: out.append('\\u%04x' % c)
        else: out.append('\\U{%x}' % c)
    out.append('"')
    return ''.join(out)


def num(*vs): return 'c(' + ', '.join(rnum(v) for v in vs) + ')'
def chr_(*vs): return 'c(' + ', '.join(rstr(v) for v in vs) + ')'
def lgl(*vs): return 'c(' + ', '.join('NA' if v is None else ('TRUE' if v else 'FALSE') for v in vs) + ')'
def date(*days): return 'structure(' + num(*days) + ', class = "Date")'

def frame(names, cols, n):
    return ('structure(list(' + ', '.join(cols) + '), names = ' + chr_(*names) +
            ', row.names = c(NA_integer_, -%dL), class = "data.frame")' % n)

def vectors(names, cols):
    return 'structure(list(' + ', '.join(cols) + '), names = ' + chr_(*names) + ')'

# ---------- cells ----------

def N(v): return {'number': v}            # v: decimal text, parsed exactly
def T(s): return {'text': s}
def B(b): return {'boolean': b}
def E(e): return {'error': e}
def D(kind, serial, fmt): return {kind: serial, 'format': fmt}   # kind: date, datetime, time, duration
def F(formula, value): return {'formula': formula, 'value': value}

def col(letter, *values, start=1):
    """Cells down one column from row `start`; None leaves a cell blank."""
    return {'%s%d' % (letter, start + i): v for i, v in enumerate(values) if v is not None}

def cells(*parts):
    out = {}
    for p in parts:
        out.update(p)
    return out

cases = []
def case(name, rules, cells_, select, expect, options=None, **extra):
    c = {'name': name, 'rules': rules, 'cells': cells_, 'select': select,
         'options': options or {'format': 'data frame', 'headings': None, 'friendly': False}, 'expect': expect}
    c.update(extra)
    cases.append(c)

DF = {'format': 'data frame', 'headings': None, 'friendly': False}
LIST = {'format': 'list', 'headings': None, 'friendly': False}
def opts(fmt='data frame', headings=None, friendly=False): return {'format': fmt, 'headings': headings, 'friendly': friendly}

# ---------- 2. Which cells ----------

case('Entire worksheet: margins and formatting-only cells are left out', ['2.1', '4.1', '1.3', '5.5'],
     cells(col('A', T('Before visit'), N('15')), col('B', T('Group / label'), T('café, "quoted"%'), T('%NA')),
           col('C', T('Outside selection'), N('30'), None, N('999'))),
     {'entire': True},
     {'entire': 'Entire worksheet — A1:C4 (4 rows × 3 columns)', 'headingsSuggested': True,
      'comments': ['# Data copied from the worksheet “Sheet1”, A1:C4.',
                   '# Blank cells, missing-value markers and spreadsheet errors are NA.'],
      'r': frame(['Before visit', 'Group / label', 'Outside selection'],
                 [num(15, None, None), chr_('café, "quoted"%', '%NA', None), num(30, None, 999)], 3)},
     formatOnly=['H20', 'A6'])

case('A rectangle keeps its bounds beyond the data', ['2.2', 'C5', '3.6'],
     cells({'B2': N('1'), 'C2': T('x')}), {'range': 'B2:D3'},
     {'selected': 'Selected cells — B2:D3 (2 rows × 3 columns)', 'headingsSuggested': False,
      'r': frame(['B', 'C', 'D'], [num(1, None), chr_('x', None), lgl(None, None)], 2)})

case('Several whole columns, in sheet order', ['2.3', 'C7', '1.4'],
     cells(col('A', T('a'), N('1'), N('2')), {'B1': T('skip')}, col('C', T('c'), N('3'), None, None, N('4'))),
     {'columns': ['A', 'C']},
     {'selected': 'Selected cells — A1:A5,C1:C5 (5 rows × 2 columns)', 'headingsSuggested': True,
      'r': frame(['a', 'c'], [num(1, 2, None, None), num(3, None, None, 4)], 4)})

case('A selected column with no data stays, as NA', ['2.3', '4.3', 'C5'],
     cells(col('A', T('a'), N('1'))), {'columns': ['A', 'B']},
     {'selected': 'Selected cells — A1:B2 (2 rows × 2 columns)', 'headingsSuggested': True,
      'r': frame(['a', 'B'], [num(1), lgl(None)], 1)})

case('Whole rows: cut to the rows and columns that hold data', ['2.4', 'C6', 'C5'],
     cells({'B3': T('x'), 'C3': N('1'), 'D4': N('2')}), {'rows': ['2:6']},
     {'selected': 'Selected cells — B3:D4 (2 rows × 3 columns)', 'headingsSuggested': False,
      'r': frame(['B', 'C', 'D'], [chr_('x', None), num(1, None), num(None, 2)], 2)})

case('Whole rows: a blank row between rows with data stays', ['2.4', 'C6'],
     cells({'A1': N('1'), 'A3': N('3')}), {'rows': ['1:4']},
     {'selected': 'Selected cells — A1:A3 (3 rows × 1 column)', 'headingsSuggested': False,
      'r': frame(['A'], [num(1, None, 3)], 3)})

case('Select all is the entire worksheet', ['2.6'],
     cells({'B2': N('1'), 'C3': N('2')}), {'all': True},
     {'selected': 'Selected cells — B2:C3 (2 rows × 2 columns)', 'entire': 'Entire worksheet — B2:C3 (2 rows × 2 columns)',
      'headingsSuggested': False, 'r': frame(['B', 'C'], [num(1, None), num(None, 2)], 2)})

case('A merged area sends its value in its top-left cell', ['2.7', '4.3'],
     cells({'B1': T('Group'), 'B2': N('1'), 'C2': N('2')}), {'range': 'B1:C2'},
     {'selected': 'Selected cells — B1:C2 (2 rows × 2 columns)', 'headingsSuggested': True,
      'r': frame(['Group', 'C'], [num(1), num(2)], 1)}, merged=['B1:C1'])

case('One cell', ['2.8', '1.4'],
     cells({'A1': N('5')}), {'range': 'A1'},
     {'selected': 'Selected cells — A1 (1 row × 1 column)', 'headingsSuggested': False, 'headingsEnabled': False,
      'r': frame(['A'], [num(5)], 1)})

case('An empty sheet offers no data', ['1.5', '7'],
     {}, {'none': True},
     {'entire': 'Entire worksheet (no data)', 'entireEnabled': False,
      'selected': {'windows': 'Selected cells (no data in the selection)', 'mac': 'Selected cells (no selection)'},
      'selectedEnabled': False, 'r': None},
     note='Windows always has an active cell (here A1), so its selection exists but holds no data; the Mac grid has no selection.')

case('Several rectangles are refused', ['2.5', 'C7', '1.5'],
     cells({'A1': N('1'), 'C1': N('2')}), {'areas': ['A1:A2', 'C1:C2']},
     {'selected': 'Selected cells (select one rectangle, or whole columns or rows)', 'selectedEnabled': False, 'r': None})

case('More than three areas: the label stops at three', ['1.4', 'C7', '5.5'],
     cells({'A1': N('1'), 'C1': N('2'), 'E1': N('3'), 'G1': N('4')}), {'columns': ['A', 'C', 'E', 'G']},
     {'selected': 'Selected cells — A1,C1,E1,… (1 row × 4 columns)', 'headingsSuggested': False, 'headingsEnabled': False,
      'comments': ['# Data copied from the worksheet “Sheet1”, A1,C1,E1,G1.'],
      'r': frame(['A', 'C', 'E', 'G'], [num(1), num(2), num(3), num(4)], 1)})

case('A selection with no data is not available', ['1.5', '2.2'],
     cells({'A1': N('1')}), {'range': 'C3:D4'},
     {'selected': 'Selected cells (no data in the selection)', 'selectedEnabled': False, 'r': None})

case('Counts are written with commas', ['1.4'],
     cells({'A1': N('1'), 'A1048576': N('2')}), {'entire': True},
     {'entire': 'Entire worksheet — A1:A1048576 (1,048,576 rows × 1 column)', 'labelsOnly': True})

# ---------- 3. Values ----------

case('Missing values in a text column', ['3.2', '3.1'],
     cells(col('A', T('Status'), T('Alive'), T('Missing'), T('NA'), T(' '), T('*'), T('* (missing)'), T('.'), T(' '))),
     {'entire': True},
     {'headingsSuggested': True, 'r': frame(['Status'], [chr_('Alive', 'Missing', None, None, None, None, None, None)], 8)})

case('Missing words in any case among numbers', ['3.2'],
     cells(col('A', T('x'), N('1'), T('na'), N('2'), T('N/A'), T('nan'), T('Null'), T('MISSING'), T('*'), T('missing'))),
     {'entire': True},
     {'headingsSuggested': True, 'r': frame(['x'], [num(1, None, 2, None, None, None, None, None, None)], 9)})

case('Real words survive in a text column', ['3.2'],
     cells(col('A', T('Element'), T('Na'), T('K'), T('Cl'))), {'entire': True},
     {'headingsSuggested': True, 'r': frame(['Element'], [chr_('Na', 'K', 'Cl')], 3)})

case('NA written exactly so is missing in a text column', ['3.2'],
     cells(col('A', T('Name'), T('Smith'), T('NA'))), {'entire': True},
     {'headingsSuggested': True, 'r': frame(['Name'], [chr_('Smith', None)], 2)})

case('A Boolean column with na', ['3.2', '3.5'],
     cells(col('A', T('Alive'), B(True), T('na'), B(False))), {'entire': True},
     {'headingsSuggested': True, 'r': frame(['Alive'], [lgl(True, None, False)], 3)})

case('Words and Inf need an anchor value', ['3.2', '3.3'],
     cells(col('A', T('First'), T('Nan'), T('Nan')), col('B', T('Second'), T('Inf')),
           col('C', T('Third'), T('Inf'), N('1')), col('D', T('Fourth'), T('na'), N('1'))),
     {'entire': True},
     {'headingsSuggested': True,
      'r': frame(['First', 'Second', 'Third', 'Fourth'],
                 [chr_('Nan', 'Nan'), chr_('Inf', None), num(math.inf, 1), num(None, 1)], 2)})

case('Spreadsheet errors are missing; text that looks like one is text', ['3.2', 'C4'],
     cells(col('A', T('Value'), N('1'), E('#DIV/0!'), N('3')), col('B', T('Label'), T('x'), T('#DIV/0!'), T('y'))),
     {'entire': True},
     {'headingsSuggested': True,
      'r': frame(['Value', 'Label'], [num(1, None, 3), chr_('x', '#DIV/0!', 'y')], 3)})

case('Spreadsheet errors are left out of a list', ['3.2', 'C4', '5.3'],
     cells(col('A', T('Value'), N('1'), E('#N/A'), N('3'))), {'entire': True},
     {'headingsSuggested': True, 'r': vectors(['Value'], [num(1, 3)])}, options=opts('list'))

case('A formula showing empty text is missing', ['3.2', '3.11', '7'],
     cells(col('A', T('Dose'), N('2'), F('=IF(A2>5,A2,"")', T('')), N('7'), F('=IF(A4>5,A4,"")', N('7')))),
     {'entire': True},
     {'headingsSuggested': True, 'r': frame(['Dose'], [num(2, None, 7, 7)], 4)},
     note='Windows calculates the formulas; the Mac uses the values given here as those Excel saved.')

case('Numbers stored as text', ['3.4', '3.1'],
     cells(col('A', T('n'), T('12.5'), T(' 13'), T('13\u0085'), T('1e3'), T('-0.25'), T('.5'), T('5.'))),
     {'entire': True},
     {'headingsSuggested': True, 'r': frame(['n'], [num(12.5, 13, 13, 1000, -0.25, 0.5, 5)], 7)})

case('Codes with a leading zero stay text', ['3.4', 'C9'],
     cells(col('A', T('Code'), T('0012'), T('-007'), T('00.5')), col('B', T('Value'), T('0'), T('0.5'), T('-0.25'), T('0e5'))),
     {'entire': True},
     {'headingsSuggested': True,
      'r': frame(['Code', 'Value'], [chr_('0012', '-007', '00.5', None), num(0, 0.5, -0.25, 0)], 4)})

case('White space is Unicode White_Space; U+FEFF is not', ['3.1', '3.2'],
     cells(col('A', T('w'), T('　'), T('﻿13'), N('5'))), {'entire': True},
     {'headingsSuggested': True, 'r': frame(['w'], [chr_(None, '﻿13', '5')], 3)})

case('Infinity among numbers; NaN is missing', ['3.3'],
     cells(col('A', T('x'), N('1'), T('Inf'), T('-infinity'), T('NaN'), T('INFINITY'))), {'entire': True},
     {'headingsSuggested': True, 'r': frame(['x'], [num(1, math.inf, -math.inf, None, math.inf)], 5)})

case('Text that is not a number makes the column character', ['3.4', '3.7'],
     cells(col('A', T('x'), N('1'), T('1e999'), T('0x1A'))), {'entire': True},
     {'headingsSuggested': True, 'r': frame(['x'], [chr_('1', '1e999', '0x1A')], 3)})

case('Booleans, as values and as text', ['3.5'],
     cells(col('A', T('b'), B(True), B(False), T('true'), T(' False '))), {'entire': True},
     {'headingsSuggested': True, 'r': frame(['b'], [lgl(True, False, True, False)], 4)})

case('Boolean text keeps its text in a character column', ['3.7'],
     cells(col('A', T('c'), B(True), T('true'), T('x'))), {'entire': True},
     {'headingsSuggested': True, 'r': frame(['c'], [chr_('TRUE', 'true', 'x')], 3)})

case('Booleans mixed with numbers are character', ['3.6', '3.7'],
     cells(col('A', T('m'), B(True), N('2'))), {'entire': True},
     {'headingsSuggested': True, 'r': frame(['m'], [chr_('TRUE', '2')], 2)})

case('An all-missing column is logical', ['3.6', '5.2'],
     cells(col('A', T('a'), N('1'), N('2')), {'B1': T('b')}), {'entire': True},
     {'headingsSuggested': True, 'r': frame(['a', 'b'], [num(1, 2), lgl(None, None)], 2)})

case('An all-missing column is logical(0) in a list', ['3.6', '5.3'],
     cells(col('A', T('a'), N('1'), N('2')), {'B1': T('b')}), {'entire': True},
     {'headingsSuggested': True, 'r': vectors(['a', 'b'], [num(1, 2), 'logical(0)'])}, options=opts('list'))

EXACT = ['0.30000000000000004', '0.3333333333333333', '5.499065962e-14', '84814136847853600000', '1e22', '1e23',
         '9007199254740993', '1.7976931348623157e308', '2.2250738585072014e-308', '-0', '0.883020325331',
         '12', '13.5', '0.1', '98.6', '-0.25']
case('Numbers arrive exactly', ['3.8'],
     cells(col('A', T('x'), *[N(v) for v in EXACT])), {'entire': True},
     {'headingsSuggested': True, 'r': frame(['x'], [num(*[float(v) for v in EXACT])], len(EXACT))})

SUBNORMAL = ['5e-324', '1.2e-310', '-2.2250738585072009e-308']
case('Subnormal numbers arrive exactly', ['3.8'],
     cells(col('A', T('x'), *[N(v) for v in SUBNORMAL])), {'entire': True},
     {'headingsSuggested': True, 'r': frame(['x'], [num(*[float(v) for v in SUBNORMAL])], len(SUBNORMAL))},
     skip={'windows': 'a SpreadsheetGear worksheet stores a subnormal number as 0, as Excel does, so it never reaches the import'})

case('Number cells in a character column', ['3.7'],
     cells(col('A', T('c'), T('x'), N('100000'), N('0.00001'), N('1e-7'), N('1.2e-7'), N('1e21'), N('-0'),
               N('0.30000000000000004'), N('123456789012345680000'))),
     {'entire': True},
     {'headingsSuggested': True,
      'r': frame(['c'], [chr_('x', '100000', '0.00001', '1e-7', '1.2e-7', '1e+21', '0', '0.30000000000000004',
                              '123456789012345680000')], 9)})

case('Text arrives exactly', ['3.10'],
     cells(col('A', T('café, "quoted"%'), T('back\\slash'), T('line1\nline2'), T('tab\tx'), T('ctrl\u001az'),
               T('a⁨b⁩‮c'), T('  spaces  '), T('\U0001F600 emoji'))),
     {'entire': True},
     {'headingsSuggested': True,
      'r': frame(['A'], [chr_('café, "quoted"%', 'back\\slash', 'line1\nline2', 'tab\tx', 'ctrl\u001az',
                              'a⁨b⁩‮c', '  spaces  ', '\U0001F600 emoji')], 8)},
     options=opts(headings=False))

case('NUL is refused, naming the cell', ['3.10'],
     cells({'A1': T('a\u0000b'), 'A2': T('x')}), {'entire': True},
     {'refusedNaming': 'A1', 'refusedMessage': 'Cell A1 contains a null character, which R cannot hold. Remove it, then open the R import again.',
      'r': None}, options=opts(headings=False))

case('A lone surrogate arrives as U+FFFD', ['3.10'],
     cells({'A1': {'textUnits': [97, 0xD800, 98]}}), {'entire': True},
     {'headingsSuggested': False, 'headingsEnabled': False, 'r': frame(['A'], [chr_('a�b')], 1)})

# ---------- 3.9 Dates and times (1900 date system) ----------

case('Dates and times', ['3.9', 'C3', '5.5'],
     cells(col('A', T('Date'), D('date', '46347', 'yyyy-mm-dd'), D('datetime', '46348', 'yyyy-mm-dd hh:mm')),
           col('B', T('When'), D('datetime', '46347', 'yyyy-mm-dd hh:mm:ss'), D('datetime', '46347.604166666664', 'yyyy-mm-dd hh:mm:ss')),
           col('C', T('Time'), D('time', '0.6041666666666666', 'hh:mm:ss'), D('time', '0.6041724537037036', 'hh:mm:ss.0')),
           col('D', T('Duration'), D('duration', '1.0833333333333333', '[h]:mm:ss'), D('duration', '-0.041666666666666664', '[h]:mm:ss'))),
     {'entire': True},
     {'headingsSuggested': True,
      'comments': ['# Dates without a time of day are R Date values; other dates and times are text.'],
      'r': frame(['Date', 'When', 'Time', 'Duration'],
                 [date(20778, 20779), chr_('2026-11-21 00:00:00', '2026-11-21 14:30:00'), chr_('14:30:00', '14:30:00.5'),
                  chr_('26:00:00', '-01:00:00')], 2)})

case('Serial 60 (1900-02-29) is missing', ['3.9'],
     cells(col('A', T('Date'), D('date', '60', 'yyyy-mm-dd'), D('date', '46347', 'yyyy-mm-dd'))), {'entire': True},
     {'headingsSuggested': True, 'r': frame(['Date'], [date(None, 20778)], 2)})

# Amendment 1 (learning-v1.1.8)
case('Date-times anchor the missing words; times of day and durations do not', ['3.2', '3.9'],
     cells(col('A', T('When'), D('datetime', '46347.416666666664', 'yyyy-mm-dd hh:mm'), T('na')),
           col('B', T('Time'), D('time', '0.6041666666666666', 'hh:mm:ss'), T('na')),
           col('C', T('Duration'), D('duration', '1.0833333333333333', '[h]:mm:ss'), T('na'))),
     {'entire': True},
     {'headingsSuggested': True,
      'r': frame(['When', 'Time', 'Duration'], [chr_('2026-11-21 10:00:00', None), chr_('14:30:00', 'na'), chr_('26:00:00', 'na')], 2)})

case('A date-time column writes midnight; a time of day makes the column character', ['3.6', '3.9'],
     cells(col('A', T('When'), D('date', '46347', 'yyyy-mm-dd'), D('datetime', '46347.416666666664', 'yyyy-mm-dd hh:mm')),
           col('B', T('Mixed'), D('date', '46347', 'yyyy-mm-dd'), D('time', '0.6041666666666666', 'hh:mm:ss'))),
     {'entire': True},
     {'headingsSuggested': True,
      'r': frame(['When', 'Mixed'], [chr_('2026-11-21 00:00:00', '2026-11-21 10:00:00'), chr_('2026-11-21', '14:30:00')], 2)})

case('mm:ss formats give times of day', ['3.9'],
     cells(col('A', T('Lap'), D('time', '0.0010416666666666667', 'mm:ss'), D('time', '0.0010474537037037037', 'mm:ss.0'))),
     {'entire': True},
     {'headingsSuggested': True, 'r': frame(['Lap'], [chr_('00:01:30', '00:01:30.5')], 2)})

case('Colour codes in a format do not make a duration', ['3.9'],
     cells(col('A', T('Date'), D('date', '46347', '[Magenta]yyyy-mm-dd'), D('date', '46348', '[magenta]dd/mm/yyyy')),
           col('B', T('When'), D('datetime', '46347.5', '[Magenta]yyyy-mm-dd hh:mm'), D('datetime', '46347.25', '[Red]yyyy-mm-dd hh:mm')),
           col('C', T('Time'), D('time', '0.25', '[Red]hh:mm'), D('time', '0.5', '[Magenta]hh:mm')),
           col('D', T('Elapsed'), D('duration', '1.25', '[Red][h]:mm'), D('duration', '0.5', '[mm]:ss'))),
     {'entire': True},
     {'headingsSuggested': True,
      'r': frame(['Date', 'When', 'Time', 'Elapsed'],
                 [date(20778, 20779), chr_('2026-11-21 12:00:00', '2026-11-21 06:00:00'), chr_('06:00:00', '12:00:00'),
                  chr_('30:00:00', '12:00:00')], 2)})

case('A value beyond 9999-12-31, or before the date system, is a number', ['3.9'],
     cells(col('A', T('Big'), D('date', '2958466', 'yyyy-mm-dd')), col('B', T('Before'), D('date', '-1', 'yyyy-mm-dd')),
           col('C', T('Long'), D('duration', '3000000', '[h]:mm:ss')), col('D', T('Last'), D('date', '2958465', 'yyyy-mm-dd')),
           col('E', T('Edge'), D('duration', '2958466', '[h]:mm:ss')), col('F', T('Longest'), D('duration', '2958465', '[h]:mm:ss')),
           col('G', T('Zero'), D('date', '0', 'yyyy-mm-dd')), col('H', T('Half'), D('datetime', '0.5', 'yyyy-mm-dd hh:mm'))),
     {'entire': True},
     {'headingsSuggested': True,
      'r': frame(['Big', 'Before', 'Long', 'Last', 'Edge', 'Longest', 'Zero', 'Half'],
                 [num(2958466), num(-1), num(3000000), date(2932896), num(2958466), chr_('71003160:00:00'), num(0), num(0.5)], 1)})

# ---------- 4. Headings and names ----------

case('Headings: the electrolyte table', ['4.1'],
     cells(col('A', T('Na'), N('140'), N('138')), col('B', T('K'), N('4.1'), N('3.9'))), {'entire': True},
     {'headingsSuggested': True, 'r': frame(['Na', 'K'], [num(140, 138), num(4.1, 3.9)], 2)})

case('No heading suggestion when the first row holds a number', ['4.1', '4.2', 'C5'],
     cells(col('A', T('x'), N('1')), col('B', N('2'), N('3'))), {'entire': True},
     {'headingsSuggested': False, 'r': frame(['A', 'B'], [chr_('x', '1'), num(2, 3)], 2)})

case('Headings from a number and a Boolean, ticked by hand', ['4.5', '3.7'],
     cells(col('A', N('1.5'), N('2')), col('B', B(True), N('3'))), {'entire': True},
     {'headingsSuggested': False, 'r': frame(['1.5', 'TRUE'], [num(2), num(3)], 1)}, options=opts(headings=True))

case('A blank or error heading takes its column letter', ['4.3', 'C5'],
     cells(col('A', T('B'), N('1')), {'B2': N('2')}, col('C', E('#N/A'), N('3'))), {'entire': True},
     {'headingsSuggested': False, 'r': frame(['B', 'B', 'C'], [num(1), num(2), num(3)], 1)}, options=opts(headings=True))

case('R-friendly names make repeated letters unique', ['4.3', '4.6'],
     cells(col('A', T('B'), N('1')), {'B2': N('2')}, col('C', E('#N/A'), N('3'))), {'entire': True},
     {'headingsSuggested': False, 'r': frame(['B', 'B_1', 'C'], [num(1), num(2), num(3)], 1)},
     options=opts(headings=True, friendly=True))

ARGNAMES = ['row.names', 'row.names', 'check.rows', 'check.rows', 'stringsAsFactors', 'stringsAsFactors', '  padded  ']
case('Headings are kept exactly, even names data.frame() uses', ['4.4'],
     cells(*[col(chr(65 + i), T(h), N(str(i + 1))) for i, h in enumerate(ARGNAMES)]), {'entire': True},
     {'headingsSuggested': True, 'r': frame(ARGNAMES, [num(i + 1) for i in range(len(ARGNAMES))], 1)})

case('R-friendly names', ['4.6'],
     cells(col('A', T('Weight (kg)'), N('1')), col('B', T('Weight (kg)'), N('2')), col('C', T('2nd'), N('3')),
           col('D', T('a b'), N('4'))), {'entire': True},
     {'headingsSuggested': True, 'r': frame(['Weight_.kg.', 'Weight_.kg._1', 'X2nd', 'a_b'], [num(1), num(2), num(3), num(4)], 1)},
     options=opts(friendly=True))

case('Column 365 is named NA', ['C5', '4.2'],
     cells({'NA1': N('1'), 'NA2': N('2')}), {'columns': ['NA']},
     {'selected': 'Selected cells — NA1:NA2 (2 rows × 1 column)', 'headingsSuggested': False,
      'r': frame(['NA'], [num(1, 2)], 2)})

# ---------- 5. Lists ----------

case('A list of vectors leaves out missing values separately', ['5.3', '3.2', '5.5'],
     cells(col('A', T('a'), N('1'), None, N('3')), col('B', T('b'), T('x'), T('*'), T('y'))), {'entire': True},
     {'headingsSuggested': True,
      'comments': ['# Missing values are removed separately from each vector.'],
      'r': vectors(['a', 'b'], [num(1, 3), chr_('x', 'y')])}, options=opts('list'))

case('The same cells as a data frame keep rows aligned', ['5.2', '3.2'],
     cells(col('A', T('a'), N('1'), None, N('3')), col('B', T('b'), T('x'), T('*'), T('y'))), {'entire': True},
     {'headingsSuggested': True, 'r': frame(['a', 'b'], [num(1, None, 3), chr_('x', None, 'y')], 3)})

about = ('Worksheets with a selection and the prompt\'s options, each with what StatsDirect must show and the exact R object '
         'it must create. Both StatsDirect for Windows and for Mac test their own import against every case: the object '
         'must be identical() in R to "expect.r", evaluated with base R only. See RULES.md, which these encode.')
schema = {
    'cells': 'A1 reference to stored cell. {"number": "<decimal text, parsed to the nearest double>"}; {"text": "<string>"}; '
             '{"textUnits": [<UTF-16 code units, for text JSON cannot carry, e.g. a lone surrogate>]}; {"boolean": true|false}; '
             '{"error": "#DIV/0!"}; {"date"|"datetime"|"time"|"duration": "<1900-system serial, decimal text>", '
             '"format": "<number format>"}; {"formula": "<Excel formula>", "value": <stored cell of its current value>}. '
             'A cell not listed is blank.',
    'formatOnly': 'cells with formatting (a number format) and no value',
    'merged': 'merged areas; only the top-left cell holds a value',
    'select': '{"entire": true} | {"range": "B2:D3"} | {"columns": ["A", "C"]} | {"rows": ["2:6"]} | {"areas": ["A1:A2", "C1:C2"]} | '
              '{"all": true} (select all) | {"none": true} (no selection; on Windows the active cell A1)',
    'options': '"format": "data frame" | "list"; "headings": null (leave the suggestion) | true | false; "friendly": true | false. '
               'The data choice is the one the select names (Entire worksheet for "entire", Selected cells otherwise).',
    'expect': '"entire" and "selected": the two data-choice labels, exactly, where given (an object gives one per platform); '
              '"entireEnabled", "selectedEnabled", "headingsEnabled": where given; "headingsSuggested": the first check box\'s '
              'suggestion; "comments": lines the script must contain; "r": the R expression the object must be identical() '
              'to, or null when nothing is imported; "refusedNaming": the import is refused with a message naming that cell, and "refusedMessage" gives that message exactly; '
              '"labelsOnly": only the labels are checked.',
    'sheet': 'every case uses one worksheet named "Sheet1"',
    'skip': '{"windows"|"mac": "<reason>"}: a case that platform cannot build, with the reason; it runs on the other',
}
json.dump({'about': about, 'version': 1, 'schema': schema, 'cases': cases}, open(sys.argv[1], 'w', encoding='utf-8', newline='\n'),
          ensure_ascii=False, indent=1)
print(len(cases), 'cases')
