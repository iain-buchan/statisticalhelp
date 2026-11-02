# Sample size for an independent case-control study: the StatsDirect help example
# (invented figures: 20% of the controls are exposed and an odds ratio of 2 is to be
# detected, one control per case, 80% power, 5% two sided alpha) in R
p0 <- 0.2                                 # probability of exposure in the controls
or <- 2                                   # the odds ratio to detect
p1 <- p0 * or / (1 + p0 * (or - 1))       # the probability of exposure in the cases
m <- 1                                    # controls per case
power <- 0.8
alpha <- 0.05
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
cat("Probability of exposure in controls =", six(p0), "\n")
cat("Probability of exposure in cases =", six(p1), "\n")
cat("Controls per case subject =", m, "\n")
cat("Alpha =", alpha, "\n")
cat("Power =", power, "\n")

# power.prop.test (two sided, its default strict = FALSE) gives the same n as the
# formula below when m = 1: it takes one size per group, so it applies only to one
# control per case
print(power.prop.test(p1 = p0, p2 = p1, power = power, sig.level = alpha))

# The report's figures, from the formula in the topic, for any number m of controls per
# case: n is rounded up to a whole number, and the number of controls is m times that,
# rounded down
z_alpha <- qnorm(1 - alpha / 2)
z_beta <- qnorm(power)
pbar <- (p1 + m * p0) / (1 + m)
n <- (z_alpha * sqrt((1 + 1 / m) * pbar * (1 - pbar)) +
      z_beta * sqrt(p1 * (1 - p1) + p0 * (1 - p0) / m))^2 / (p1 - p0)^2
cat("For uncorrected chi-square test:\n")
cat("N =", ceiling(n), "case subjects and", floor(m * ceiling(n)), "controls\n")

# The continuity-corrected size for the corrected chi-square and Fisher's exact tests
# (Casagrande et al. 1978), calculated from the unrounded n
nc <- n / 4 * (1 + sqrt(1 + 2 * (m + 1) / (n * m * abs(p0 - p1))))^2
cat("For corrected chi-square and Fisher's exact tests:\n")
cat("N =", ceiling(nc), "case subjects and", floor(m * ceiling(nc)), "controls\n")
