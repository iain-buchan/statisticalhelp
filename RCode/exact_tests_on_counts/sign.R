# Sign test: the StatsDirect help example (Altman 1991, p. 186: 9 of 11 women with a
# food energy intake below the daily average) in R
n <- 11
r <- 9

# R's standard test. With p = 0.5 the binomial distribution is symmetric, so R's
# two sided P (0.06543) is twice the one sided tail (0.032715), the report's two P
# values. Its confidence interval is the exact (Clopper-Pearson) interval.
print(binom.test(r, n, p = 0.5, conf.level = 0.95))

six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}

# Exact cumulative probabilities: the one sided P is the probability of r or fewer
# on the less frequent side (2 or fewer of 11 here); the two sided P is twice that,
# capped at 1.
p1 <- pbinom(min(r, n - r), n, 0.5)
cat("Cumulative probability: two sided", pv(min(1, 2 * p1)), " one sided", pv(p1), "\n")

# Normal approximation with a continuity correction of a half, as the report prints
# it: |n/2 - r| - 1/2 over the standard deviation sqrt(n/4); z is 0 when r is within
# half an observation of n/2.
z <- max(0, abs(n / 2 - r) - 0.5) / sqrt(n / 4)
cat("Normal approximate z =", six(z), "\n")
cat("Two sided", pv(2 * pnorm(-z)), "  One sided", pv(pnorm(-z)), "\n")

# Exact (Clopper-Pearson) confidence limits, as binom.test gives them, from the beta
# distribution: 0 or 1 when r is 0 or n
ci <- binom.test(r, n, conf.level = 0.95)$conf.int
cat("Lower Limit =", six(ci[1]), "\n")
cat("Proportion =", six(r / n), "\n")
cat("Upper Limit =", six(ci[2]), "\n")
