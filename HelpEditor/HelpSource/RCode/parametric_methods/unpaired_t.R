# Unpaired t test: the StatsDirect help example (Armitage and Berry 1994, weight
# gain of 19 rats on high or low protein diets) in R
high <- c(134, 146, 104, 119, 124, 161, 107, 83, 113, 129, 97, 123)
low <- c(70, 118, 101, 85, 107, 132, 94)

# R's standard test, first assuming equal variances and then not (R's default,
# the Welch test, whose t and df are StatsDirect's t(d) and its fractional df).
# Each gives t, df, the two sided P and the confidence interval.
equal <- t.test(high, low, var.equal = TRUE)
print(equal)
unequal <- t.test(high, low)
print(unequal)

# The F test that StatsDirect uses to compare the variances
f <- var.test(high, low)
print(f)

# The report's other lines, to 6 decimal places
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
cat(sprintf("Mean of High Protein = %s  (n = %d)\n", six(mean(high)), length(high)))
cat(sprintf("Mean of Low Protein = %s  (n = %d)\n", six(mean(low)), length(low)))
delta <- mean(high) - mean(low)
report <- function(r, label, t) {
  cat(label, "\n")
  cat("Combined standard error =", six(delta / r$statistic), "  df =",
      six(r$parameter), " ", t, "=", six(r$statistic), "\n")
  cat(sprintf("One sided P = %.4f   Two sided P = %.4f\n", r$p.value / 2, r$p.value))
  cat("95% confidence interval for difference between means =", six(r$conf.int[1]),
      "to", six(r$conf.int[2]), "\n")
  # Power of a two sided test at the 5% level to detect the difference seen, from
  # the noncentral t distribution: the noncentrality parameter is the t observed
  tc <- qt(0.975, r$parameter)
  pw <- pt(-tc, r$parameter, ncp = r$statistic) +
        pt(tc, r$parameter, ncp = r$statistic, lower.tail = FALSE)
  cat(sprintf("Power (for 5%% significance) = %.2f%%\n", 100 * pw))
}
report(equal, "Assuming equal variances", "t")
report(unequal, "Assuming unequal variances", "t(d)")
cat("Two sided F test is", if (f$p.value < 0.05) "significant" else "not significant",
    "\n")
