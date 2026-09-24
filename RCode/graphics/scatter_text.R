# Scatter plot (text-based): the StatsDirect help illustration (Armitage and Berry
# 1994, birth weight in oz and percentage weight gain between 70 and 100 days of 32
# babies; the test workbook's Regression worksheet columns "Birth Weight" and
# "% Increase") in R
weight <- c(72, 112, 111, 107, 119, 92, 126, 80, 81, 84, 115, 118, 128, 128, 123, 116,
            125, 126, 122, 126, 127, 86, 142, 132, 87, 123, 133, 106, 103, 118, 114, 94)
gain <- c(68, 63, 66, 72, 52, 75, 76, 118, 120, 114, 29, 42, 48, 50, 69, 59, 27, 60, 71,
          88, 63, 88, 53, 50, 111, 59, 76, 72, 90, 68, 93, 91)

# StatsDirect draws the plot as text on a grid of characters 85 columns wide: the y
# axis stands in column 15 and the x axis runs 60 columns to its right; there are 5
# lines for every tic of the y axis and 5 more for the titles and the x axis. Each
# point goes to the nearest character cell (a half is rounded to the even neighbour, as
# round() does); a second point in the same cell turns its * into 2, a third into 3
# and so on, and ten or more are marked X. A pair with a missing value is left out.
# The axis scales are chosen automatically; for these data StatsDirect offers 70 to
# 140 in tens and 25 to 125 in 25s (labelled with one decimal place). The scale runs
# from the first to the last tic, stretched to the data where a point lies beyond.
text_scatter <- function(x, y, xtics, ytics, xlabs, ylabs, xtitle, ytitle,
                         title = "Scatter plot") {
  keep <- !is.na(x) & !is.na(y)
  x <- x[keep]
  y <- y[keep]
  rows <- 5 * length(ytics) + 5
  grid <- matrix(" ", nrow = rows, ncol = 85)
  put <- function(row, col, text) {   # row 1 is the bottom line, column 1 the left
    chars <- strsplit(text, "")[[1]]
    grid[row, col + seq_along(chars) - 1] <<- chars
  }
  xr <- range(c(xtics, x))
  yr <- range(c(ytics, y))
  ext <- rows - 5
  col_of <- function(v) round(-(xr[1] / diff(xr) * 60) + 15 + v / diff(xr) * 60) + 1
  row_of <- function(v) round(-(yr[1] / diff(yr) * ext) + 2 + v / diff(yr) * ext) + 1
  put(3, 15, paste0("/", strrep("-", 60)))
  for (r in 4:(3 + ext)) put(r, 15, "|")
  for (i in seq_along(xtics)) {
    put(3, col_of(xtics[i]), "+")
    put(2, col_of(xtics[i]) - startsWith(xlabs[i], "-"), xlabs[i])
  }
  for (i in seq_along(ytics)) {
    put(row_of(ytics[i]), 15, "+")
    put(row_of(ytics[i]), 15 - nchar(ylabs[i]), ylabs[i])
  }
  put(rows, (85 - nchar(title)) %/% 2 + 1, title)
  put(rows - 1, if (nchar(ytitle) < 14) 15 - nchar(ytitle) else 3, ytitle)
  put(1, 77 - nchar(xtitle), xtitle)
  # A point that falls on the axis line is drawn over it, so that no point is lost
  for (i in seq_along(x)) {
    r <- row_of(y[i])
    k <- col_of(x[i])
    cell <- grid[r, k]
    grid[r, k] <- if (cell %in% c(" ", "/", "-", "|", "+")) "*" else
      if (cell == "*") "2" else if (cell %in% c("9", "X")) "X" else
      as.character(as.numeric(cell) + 1)
  }
  cat(apply(grid[rows:1, ], 1, paste, collapse = ""), sep = "\n")
}

xtics <- seq(70, 140, by = 10)
ytics <- seq(25, 125, by = 25)
text_scatter(weight, gain, xtics, ytics, formatC(xtics, format = "f", digits = 0),
             formatC(ytics, format = "f", digits = 1), "Birth Weight", "% Increase")

# What the plot shows, in words
cat("Scatter plot of % Increase against Birth Weight:", length(weight), "points\n")
cat("Birth weight from", min(weight), "to", max(weight), "oz; gain from", min(gain),
    "to", max(gain), "%\n")
