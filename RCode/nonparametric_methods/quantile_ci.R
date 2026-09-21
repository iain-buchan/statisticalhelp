# Quantile confidence interval: the StatsDirect help example (Conover 1999,
# p. 145) in R
tubes <- c(46.9, 47.2, 49.1, 56.5, 56.8, 59.2, 59.9, 63.2, 63.3, 63.4, 63.7, 64.1,
           67.1, 67.7, 73.3, 78.5)
p <- 0.75                                 # the upper quartile
conf <- 0.90
x <- sort(tubes)
n <- length(x)

# The quantile. StatsDirect takes the p(n + 1)th ordered value, interpolating
# between neighbours. That is type 6 in R, whose default is type 7 (64.85 here).
cat("Quantile (", p, ") = ", quantile(tubes, p, type = 6), "\n", sep = "")

# R has no standard function for this interval. The limits are ordered values.
# The number of observations below the population quantile is binomial(n, p), so
# cum[q + 1] is the probability that at most q of them are below it, and the
# interval from the (lo + 1)th to the (hi + 1)th ordered value covers the
# quantile with probability cum[hi + 1] - cum[lo + 1].
cum <- pbinom(0:n, n, p)
tail <- (1 - conf) / 2
show <- function(lo, hi, label) {
  cat(sprintf("Approximate %g%% CI (%s) = %g to %g\n", 100 * conf, label,
              x[lo + 1], x[hi + 1]))
  cat("Exact confidence level = ",
      format(round(100 * (cum[hi + 1] - cum[lo + 1]), 6), digits = 10), "%\n",
      sep = "")
}

# Non-conservative: the probability in each tail is as close as it can be to
# (1 - conf) / 2
show(which.min(abs(cum - tail)) - 1, which.min(abs(cum - (1 - tail))) - 1,
     "non-conservative")

# Conservative: no more than (1 - conf) / 2 in each tail. With a small sample, or
# a quantile near 0 or 1, no ordered value may be far enough out: StatsDirect
# then uses the smallest or largest observation and marks the limit.
stopifnot(any(cum <= tail), any(cum >= 1 - tail))
show(max(which(cum <= tail)) - 1, min(which(cum >= 1 - tail)) - 1, "conservative")
