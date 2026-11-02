# Box and whisker plot: the StatsDirect help illustration (Cuzick 1985, lung
# metastases in mice by cell line; the test workbook's Nonparametric worksheet
# columns CMT 64 to CMT 181) in R
mets <- list("CMT 64"  = c(0, 0, 1, 1, 2, 2, 4, 9),
             "CMT 167" = c(0, 0, 5, 7, 8, 11, 13, 23, 25, 97),
             "CMT 170" = c(2, 3, 6, 9, 10, 11, 11, 12, 21),
             "CMT 175" = c(0, 3, 5, 6, 10, 19, 56, 100, 132),
             "CMT 181" = c(2, 4, 6, 6, 6, 7, 18, 39, 60))

# R's standard plot: boxplot() draws the box between Tukey's hinges (the medians
# of the lower and upper halves of the data), the whiskers to the last values
# within 1.5 times the box length of the box, and the values beyond as circles.
op <- par(mar = c(5, 6, 4, 2))              # room for the column names on the left
boxplot(mets, horizontal = TRUE, las = 1, main = "boxplot() of the same columns")
par(op)

# StatsDirect's box runs from the lower to the upper quartile, the (n+1)/4th and
# 3(n+1)/4th ordered values with interpolation between neighbours, which is type
# 6 of quantile(); the hinges differ from these quartiles for most sample sizes.
# Its fences are the quartiles minus and plus 1.5 (inner) and 3 (outer) times the
# interquartile range.
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
summarise <- function(x) {
  x <- sort(x[!is.na(x)])                 # blank cells are skipped, as in StatsDirect
  q <- quantile(x, c(0.25, 0.5, 0.75), type = 6, names = FALSE)
  iqr <- q[3] - q[1]
  list(x = x, n = length(x), lq = q[1], median = q[2], uq = q[3],
       inner = c(q[1] - 1.5 * iqr, q[3] + 1.5 * iqr),
       outer = c(q[1] - 3 * iqr, q[3] + 3 * iqr))
}
for (name in names(mets)) {
  s <- summarise(mets[[name]])
  cat(paste0(name, ":"), "n =", s$n, " min =", six(s$x[1]), " LQ =", six(s$lq),
      " median =", six(s$median), " UQ =", six(s$uq), " max =", six(s$x[s$n]), "\n")
  beyond <- s$x[s$x < s$inner[1] | s$x > s$inner[2]]
  if (length(beyond) > 0) {
    cat("   inner fences", six(s$inner[1]), "to", six(s$inner[2]),
        " outer fences", six(s$outer[1]), "to", six(s$outer[2]),
        " beyond the inner fence:", six(beyond), "\n")
  }
}

# The StatsDirect plot: one box per column, the first at the top, the median
# marked with a filled diamond. With both fence options ticked (the default) a
# whisker stops at the last value inside the inner fence, values beyond it are
# drawn as hollow circles and values beyond the outer fence as filled circles; a
# whisker that reaches the extreme value ends in a bracket. With the fences
# unticked (fences = FALSE) the whiskers run to the minimum and the maximum, as
# in the plot in the help topic.
sd_boxplot <- function(data, fences = TRUE, title = "Box & whisker plot") {
  k <- length(data)
  xlab <- "min < LQ < median > UQ > max"
  if (fences) xlab <- paste0(xlab, ", fences (1.5 & 3.0 IQR)")
  old <- par(mar = c(5, 6, 4, 2))          # room for the column names on the left
  on.exit(par(old))
  plot(range(unlist(data), na.rm = TRUE), c(0, k), type = "n", yaxt = "n",
       ylab = "", xlab = xlab, main = title)
  axis(2, at = k - seq_len(k) + 0.5, labels = names(data), las = 1)
  tip <- 0.01 * diff(range(unlist(data), na.rm = TRUE))   # the bracket's length
  for (i in seq_len(k)) {
    s <- summarise(data[[i]])
    y <- k - i + 0.5
    rect(s$lq, y - 0.333, s$uq, y + 0.333)
    segments(s$median, y - 0.333, s$median, y + 0.333)
    points(s$median, y, pch = 18, cex = 1.5)
    inner <- if (fences) s$inner else range(s$x)
    outer <- if (fences) s$outer else range(s$x)
    ends <- c(min(s$x[s$x >= inner[1]]), max(s$x[s$x <= inner[2]]))
    segments(ends, y, c(s$lq, s$uq), y)
    segments(ends, y - 0.111, ends, y + 0.111)
    if (ends[1] == s$x[1]) segments(ends[1], y + c(-0.111, 0.111), ends[1] + tip)
    if (ends[2] == s$x[s$n]) segments(ends[2], y + c(-0.111, 0.111), ends[2] - tip)
    beyond <- s$x[s$x < inner[1] | s$x > inner[2]]
    filled <- beyond < outer[1] | beyond > outer[2]
    points(beyond, rep(y, length(beyond)), pch = ifelse(filled, 16, 1))
  }
}
sd_boxplot(mets, title = "Box & whisker plot from test")
sd_boxplot(mets, fences = FALSE, title = "Number of metastases in different cell lines")
