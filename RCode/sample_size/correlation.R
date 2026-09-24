# Sample size for Pearson's correlation: the StatsDirect help example (an invented
# study that should detect a correlation of 0.4 or more, r1 = 0.4 against the null
# value r0 = 0, with 80% power at the two sided 5% level) in R
r0 <- 0
r1 <- 0.4
power <- 0.8
alpha <- 0.05
stopifnot(r0 != r1, abs(r0) < 1, abs(r1) < 1)  # Fisher's z needs |r| < 1, r0 != r1

# Base R has no function for this sample size, so the topic's method is used here.
# Fisher's z transformation, atanh(r) = 0.5 * log((1 + r) / (1 - r)), makes a sample
# correlation from n pairs approximately normal with mean atanh(rho) and variance
# 1 / (n - 3), which gives the two sided power of a test between the two hypotheses.
dif <- abs(atanh(r1) - atanh(r0))
z_alpha <- qnorm(1 - alpha / 2)
z_power <- qnorm(power)
size_power <- function(n) {
  z <- dif * sqrt(n - 3)
  pnorm(z - z_alpha) + pnorm(-z - z_alpha)
}

# The initial estimate ((z_alpha + z_power) / dif)^2 + 3 is rounded up, then moved to
# the smallest whole number of pairs (at least 4) at which the power reaches the target
n0 <- ((z_alpha + z_power) / dif)^2 + 3
n <- max(ceiling(n0), 4)
while (n > 4 && size_power(n - 1) >= power) n <- n - 1
while (size_power(n) < power) n <- n + 1
cat("Alpha =", alpha, "\n")
cat("Power =", power, "\n")
cat("Correlation coefficient under null hypothesis =", r0, "\n")
cat("Correlation coefficient under alternative hypothesis =", r1, "\n")
cat("Estimated minimum sample size =", n, "\n")

# The working: the initial estimate and the power either side of the answer
pct <- function(x) paste0(formatC(100 * x, digits = 1, format = "f"), "%")
cat("Initial estimate of n =", formatC(n0, digits = 2, format = "f"), "\n")
cat("Power with", n - 1, "pairs =", paste0(pct(size_power(n - 1)), ","), "with", n,
    "pairs =", pct(size_power(n)), "\n")
