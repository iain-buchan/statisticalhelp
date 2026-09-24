# Sample size for a matched case-control study: the StatsDirect help example (an
# invented study with three controls matched to each case, exposure in 30% of the
# controls, an odds ratio of 2 to detect and a correlation of 0.2 for exposure
# between a case and its controls) in R
phi <- 0.2                                # correlation of exposure within a matched set
p0 <- 0.3                                 # probability of exposure in the controls
psi <- 2                                  # odds ratio to be detected
m <- 3                                    # controls matched to each case
power <- 0.8
alpha <- 0.05                             # two sided

# Base R has no function for this design, so the formulae in the topic (Dupont 1988)
# are used. First the probability of exposure in the cases, p1: the odds ratio is
# that of the discordant case-control pairs, p10 / p01, where p01 is as the topic
# gives it and p10 = p1 q0 - phi sqrt(p1 q1 p0 q0), so p1 is the value that makes
# this ratio equal to psi (found numerically here; the program solves the same
# equation)
exposure_odds <- function(p1) {
  s <- phi * sqrt(p1 * (1 - p1) * p0 * (1 - p0))
  (p1 * (1 - p0) - s) - psi * ((1 - p1) * p0 - s)
}
p1 <- uniroot(exposure_odds, c(1e-8, 1 - 1e-8), tol = 1e-12)$root
q0 <- 1 - p0
q1 <- 1 - p1
s <- phi * sqrt(p1 * q1 * p0 * q0)
# every cell of the case-control exposure table must be a probability
stopifnot(p1 * p0 + s >= 0, p1 * q0 - s >= 0, q1 * p0 - s >= 0, q1 * q0 + s >= 0)
p0_plus <- (p1 * p0 + s) / p1             # exposure in a control of an exposed case
p0_minus <- (q1 * p0 - s) / q1            # exposure in a control of an unexposed case

# The cases needed with m controls per case: t_k is the probability that k of the m + 1
# members of a matched set are exposed (sets with none or all exposed carry no
# information); the score statistic has mean E and variance V per matched set, at the
# odds ratio to detect and, for the null hypothesis, at an odds ratio of 1
cases_needed <- function(m) {
  k <- 1:m
  t_k <- p1 * choose(m, k - 1) * p0_plus^(k - 1) * (1 - p0_plus)^(m - k + 1) +
    q1 * choose(m, k) * p0_minus^k * (1 - p0_minus)^(m - k)
  E <- function(psi) sum(k * t_k * psi / (k * psi + m - k + 1))
  V <- function(psi) sum(k * t_k * psi * (m - k + 1) / (k * psi + m - k + 1)^2)
  z_alpha <- qnorm(1 - alpha / 2)
  z_beta <- qnorm(power)
  (z_alpha * sqrt(V(1)) + z_beta * sqrt(V(psi)))^2 / (E(psi) - E(1))^2
}
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
n_m <- ceiling(cases_needed(m))           # rounded up to a whole number of cases
n_1 <- ceiling(cases_needed(1))           # the paired design, one control per case
cat("case-control correlation =", phi, "\n")
cat("probability of exposure in controls =", p0, "\n")
cat("odds ratio =", psi, "\n")
cat("controls per case subject =", m, "\n")
cat("alpha =", alpha, "\n")
cat("power =", power, "\n")
cat("Estimated minimum sample size (cases required) =", n_m, "\n")
cat("Reduction in sample size, relative to paired design,\n")
cat("when using", m, "controls per case =", six(n_m / n_1), "\n")

# The paired design itself, for comparison
cat("controls per case subject = 1\n")
cat("Estimated minimum sample size (cases required) =", n_1, "\n")
