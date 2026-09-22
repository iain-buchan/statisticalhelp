# Reference range: the StatsDirect help example (Altman 1991, serum IgM in 298
# children) in R
# The data are the IgM column of the Parametric worksheet of the StatsDirect test
# workbook. Save that column, with its heading, as igm.csv in R's working
# directory first.
igm <- read.csv("igm.csv")$IgM
n <- length(igm)
z <- qnorm(0.975)                   # for a 95% range and 95% confidence intervals
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
cat("Sample mean =", six(mean(igm)), "  Sample size n =", n,
    "  Sample sd =", six(sd(igm)), "\n")

# R has no standard function for a reference range. For data from a normal
# distribution the range is the mean plus or minus z standard deviations, and
# each limit has standard error s * sqrt(1/n + z^2/(2n)) (Altman 1991).
normal_range <- function(x, back = identity) {
  m <- mean(x)
  s <- sd(x)
  limits <- m + c(-1, 1) * z * s
  se <- s * sqrt(1 / n + z^2 / (2 * n))
  cat("95% reference interval =", six(back(limits[1])), "to", six(back(limits[2])),
      "\n")
  cat("95% confidence interval for lower range limit =", six(back(limits[1] - z * se)),
      "to", six(back(limits[1] + z * se)), "\n")
  cat("95% confidence interval for upper range limit =", six(back(limits[2] - z * se)),
      "to", six(back(limits[2] + z * se)), "\n")
}
cat("For normal data\n")
normal_range(igm)
# For log-normal data, the same on the logarithms with the results taken back to
# the original scale (StatsDirect gives this only when no value is 0 or less)
cat("For log-normal data\n")
normal_range(log(igm), exp)

# For any data: the 2.5th and 97.5th percentiles (type 6 in R, as in the quantile
# confidence interval topic) with confidence intervals whose limits are ordered
# values. The number of observations below a population quantile is binomial
# (n, p). With more than 200 observations StatsDirect takes the limits from the
# normal approximation to that distribution, at np plus or minus z sqrt(np(1-p)),
# and then gives the exact probability that they cover the quantile; with fewer
# it searches the binomial distribution itself, as in the quantile topic.
stopifnot(n > 200)
cat("For any data\n")
x <- sort(igm)
for (p in c(0.025, 0.975)) {
  lo <- floor(n * p - z * sqrt(n * p * (1 - p)))   # numbers of values below
  hi <- floor(n * p + z * sqrt(n * p * (1 - p)))
  cat("Quantile", p, "=", quantile(igm, p, type = 6), "\n")
  cat("Approximate 95% confidence interval (non-conservative) =", x[lo + 1], "to",
      x[hi + 1], "\n")
  cat(sprintf("Exact confidence level = %s%%\n",
              six(100 * (pbinom(hi, n, p) - pbinom(lo, n, p)))))
}
