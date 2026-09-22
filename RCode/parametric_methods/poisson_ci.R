# Poisson confidence interval: the StatsDirect help example (patients arriving at
# a reception desk in 23 five minute intervals) in R
counts <- c(2, 1, 1, 0, 2, 1, 0, 2, 3, 1, 0, 1, 2, 2, 1, 0, 0, 1, 1, 2, 2, 1, 1)
n <- length(counts)

# R's standard function takes the total count as one Poisson observation over a
# time base of n intervals, so its event rate is the mean count per interval and
# its confidence interval, from the chi-square distribution, has the limits
# StatsDirect reports.
r <- poisson.test(sum(counts), n)
print(r)

# The report's lines, to 6 decimal places
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
cat("Size =", n, "  Mean =", six(mean(counts)), "\n")
cat("Approximate two sided 95% CI =", six(r$conf.int[1]), "to", six(r$conf.int[2]),
    "\n")
# StatsDirect also gives the one sided 95% lower and upper limits, which are the
# ends of a two sided 90% interval, under the heading "one sided 90% CI"
ci <- poisson.test(sum(counts), n, conf.level = 0.9)$conf.int
cat("Approximate one sided 90% CI =", six(ci[1]), "to", six(ci[2]), "\n")
