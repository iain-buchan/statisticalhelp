# Risk (prospective): the StatsDirect help example (Sahai and Khurshid 1996, coronary
# heart disease after six years in 1329 Framingham men aged 40 to 59, by serum
# cholesterol at the start) in R
counts <- matrix(c(72, 20,
                   684, 553), 2, byrow = TRUE,
                 dimnames = list(CHD = c("Yes", "No"),
                                 Cholesterol = c("220 or more", "Under 220")))
print(addmargins(counts))
a <- counts[1, 1]   # exposed with the outcome
b <- counts[1, 2]   # unexposed with the outcome
c <- counts[2, 1]   # exposed without it
d <- counts[2, 2]   # unexposed without it
n1 <- a + c         # exposed
n2 <- b + d         # unexposed
m1 <- a + b         # with the outcome
n <- n1 + n2

# R's standard test compares the two outcome rates, 72 of 756 and 20 of 573, with
# a chi-square test (uncorrected here). The two rates it prints are the risks in the
# exposed and the unexposed, but its interval for their difference is the simple
# normal (Wald) one, not the Miettinen and Nurminen interval of the report
print(prop.test(t(counts), correct = FALSE))

six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
p1 <- a / n1
p0 <- b / n2
rr <- p1 / p0
cat("Risk ratio (relative risk in incidence study) =", six(rr), "\n")

# Base R has no function for the confidence interval of a ratio of two binomial
# proportions, so the limits are found as Koopman (1984) defines them: the ratios at
# which the score chi-square, with the two rates estimated under the constraint that
# they have that ratio, reaches the 95% critical value. Every count in the example is
# greater than zero, which this function assumes.
koopman <- function(x1, n1, x0, n0, level = 0.95) {
  chi2 <- function(theta) {
    A <- (n0 + n1) * theta
    B <- -((x0 + n1) * theta + x1 + n0)
    q0 <- (-B - sqrt(B^2 - 4 * A * (x0 + x1))) / (2 * A)  # constrained estimate
    q1 <- theta * q0
    (x1 - n1 * q1)^2 / (n1 * q1 * (1 - q1)) *
      (1 + n1 * (theta - q1) / (n0 * (1 - q1)))
  }
  est <- (x1 / n1) / (x0 / n0)
  f <- function(lt) chi2(exp(lt)) - qchisq(level, 1)
  lower <- exp(uniroot(f, c(log(est) - 20, log(est)), tol = 1e-12)$root)
  upper <- exp(uniroot(f, c(log(est), log(est) + 20), tol = 1e-12)$root)
  c(lower, upper)
}
k <- koopman(a, n1, b, n2)
cat("Approximate (Koopman) 95% confidence interval =", six(k[1]), "to", six(k[2]),
    "\n")

# Approximate power: the power with which Fisher's exact test on groups of this size
# (756 exposed, 573 unexposed) would detect the observed difference between the two
# rates at the two sided 5% level. It is the power at which the continuity corrected
# sample size formula of Casagrande, Pike and Smith (1978), in the form given by
# Fleiss, returns the size of the exposed group.
alpha <- 0.05
m <- n2 / n1                              # unexposed per exposed subject
z <- qnorm(1 - alpha / 2)
pbar <- (p1 + m * p0) / (m + 1)
size_for_power <- function(power) {
  zb <- qnorm(power)
  np <- (z * sqrt((1 + 1 / m) * pbar * (1 - pbar)) +
           zb * sqrt(p0 * (1 - p0) / m + p1 * (1 - p1)))^2 / (p0 - p1)^2
  np * (1 + sqrt(1 + 2 * (m + 1) / (np * m * abs(p0 - p1))))^2 / 4
}
power <- uniroot(function(pw) size_for_power(pw) - n1, c(alpha, 1 - 1e-12),
                 tol = 1e-10)$root         # the size falls as the power asked for falls
cat("Approximate power (for 5% significance) = ",
    formatC(100 * power, digits = 2, format = "f"), "%\n", sep = "")

# The risk difference, with the score interval of Miettinen and Nurminen (1985): the
# differences at which the chi-square, with the two rates estimated by maximum
# likelihood under the constraint that they differ by that amount, reaches the 95%
# critical value. The variance carries their factor of N / (N - 1).
cat("Risk difference =", six(p1 - p0), "\n")
miettinen <- function(x1, n1, x0, n0, level = 0.95) {
  dhat <- x1 / n1 - x0 / n0
  chi2 <- function(delta) {
    loglik <- function(q0) {
      q1 <- q0 + delta
      x1 * log(q1) + (n1 - x1) * log(1 - q1) + x0 * log(q0) + (n0 - x0) * log(1 - q0)
    }
    q0 <- optimize(loglik, c(max(0, -delta), min(1, 1 - delta)), maximum = TRUE,
                   tol = 1e-12)$maximum
    q1 <- q0 + delta
    (dhat - delta)^2 /
      ((q1 * (1 - q1) / n1 + q0 * (1 - q0) / n0) * (n1 + n0) / (n1 + n0 - 1))
  }
  f <- function(delta) chi2(delta) - qchisq(level, 1)
  lower <- uniroot(f, c(-1 + 1e-9, dhat), tol = 1e-12)$root
  upper <- uniroot(f, c(dhat, 1 - 1e-9), tol = 1e-12)$root
  c(lower, upper)
}
mn <- miettinen(a, n1, b, n2)
cat("Approximate (Miettinen) 95% confidence interval =", six(mn[1]), "to", six(mn[2]),
    "\n")

# Population attributable risk, reported when the relative risk exceeds 1. With no
# population figure entered, the proportion exposed is estimated from the cohort as
# (a + c) / n, and the attributable fraction Px (RR - 1) / (1 + Px (RR - 1)) is then
# the same as 1 - (b / (b + d)) / ((a + b) / n): the share of the overall incidence
# that would go if the whole cohort had the rate of the unexposed. Its large sample
# variance is Walter's (1978). All three are printed as percentages.
px <- n1 / n
par <- px * (rr - 1) / (1 + px * (rr - 1))
var_par <- b * n * (a * d * (n - b) + b^2 * c) / (m1^3 * n2^3)
cat("Population exposure % =", six(100 * px), "\n")
cat("Population attributable risk % =", six(100 * par), "\n")
cat("Approximate (Walter) 95% confidence interval =",
    six(100 * (par - z * sqrt(var_par))), "to", six(100 * (par + z * sqrt(var_par))),
    "\n")
