# Histogram: the StatsDirect help illustration (serum IgM in g/l of 298 children aged
# 6 months to 6 years, Altman 1991; the test workbook's Other worksheet columns IgM
# and Log(base 10): IgM) in R
# The data are the IgM column of the test workbook. Save that column, with its
# heading, as igm.csv in R's working directory first.
igm <- read.csv("igm.csv")$IgM

# StatsDirect's default number of bins is Doane's rule: 1 + log2(n) + log2(1 + |g1| /
# sigma), rounded, where g1 is the skewness (the third central moment divided by the
# second to the power 1.5) and sigma = sqrt(6 (n - 2) / ((n + 1) (n + 3))) its standard
# error under normality. The bins are equal intervals from the smallest value to the
# largest; a value on a bin's upper edge belongs to that bin and the smallest value to
# the first bin, as hist() counts with right = TRUE and include.lowest = TRUE.
doane <- function(x) {
  # StatsDirect takes the skewness as 0 when it cannot compute it (n <= 3 or all
  # values equal); without this R gives a different count for n = 3 and NaN below
  if (length(x) <= 3 || var(x) == 0) return(round(1 + log2(length(x))))
  m2 <- mean((x - mean(x))^2)
  m3 <- mean((x - mean(x))^3)
  g1 <- m3 / m2^1.5
  sigma <- sqrt(6 * (length(x) - 2) / ((length(x) + 1) * (length(x) + 3)))
  round(1 + log2(length(x)) + log2(1 + abs(g1) / sigma))
}
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)

# The chart labels the x axis at the bin mid-points: exactly when they are short (up
# to 4 decimals), otherwise with the fewest decimals (up to 6) that put every label
# within a fortieth of a bin of its mid-point (-1 here means neither, when the chart
# falls back to three significant figures).
decimals_within <- function(mids, error, most) {
  for (d in 0:most) if (max(abs(round(mids, d) - mids)) <= error) return(d)
  -1
}
label_decimals <- function(mids, width) {
  d <- decimals_within(mids, width * 1e-9, 4)
  if (d < 0) d <- decimals_within(mids, width / 40, 6)
  d
}
mid_labels <- function(mids, width) {
  d <- label_decimals(mids, width)
  if (d < 0) return(formatC(mids, digits = 3, format = "g"))
  formatC(mids, digits = d, format = "f")
}

# One histogram in StatsDirect's layout: bars, the mid-point labels and, when asked
# for, the normal curve of the data's mean and standard deviation scaled to the counts
# (n times the bin width times the normal density), as the chart option draws it.
sd_histogram <- function(x, name, normal_curve = FALSE) {
  k <- doane(x)
  breaks <- seq(min(x), max(x), length.out = k + 1)
  h <- hist(x, breaks = breaks, right = TRUE, include.lowest = TRUE, plot = FALSE)
  width <- breaks[2] - breaks[1]
  labels <- mid_labels(h$mids, width)
  cat("Distribution of", name, "\n")
  cat("Bins =", k, "(Doane's rule), width =", six(width), "\n")
  cat("Mid-points:", labels, "\n")
  cat("Counts:", h$counts, "\n\n")
  plot(h, main = paste("Distribution of", name), xlab = paste("Mid-points for", name),
       ylab = "Counts", xaxt = "n", col = NA, border = "steelblue4")
  axis(1, at = h$mids, labels = labels)
  if (normal_curve) {
    scale <- length(x) * width
    m <- mean(x)
    s <- sd(x)
    curve(scale * dnorm(x, m, s), add = TRUE, col = "steelblue4")
  }
  invisible(h)
}

# The IgM concentrations, then their logarithms to base 10 with the normal curve
sd_histogram(igm, "IgM")
sd_histogram(log10(igm), "Log(base 10): IgM", normal_curve = TRUE)
