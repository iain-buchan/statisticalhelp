# Risk (retrospective): the StatsDirect help example (Sahai and Khurshid 1996, heavy
# smoking of 16 or more cigarettes a day among 304 oral cancer cases and 139 controls)
# in R
counts <- matrix(c(255, 49,
                   93, 46), 2, byrow = TRUE,
                 dimnames = list(Outcome = c("Positive", "Negative"),
                                 Factor = c("Present", "Absent")))
print(counts)
a <- counts[1, 1]   # cases with the factor
b <- counts[1, 2]   # cases without it
c <- counts[2, 1]   # controls with the factor
d <- counts[2, 2]   # controls without it

# R's standard test conditions on both margins of the table. Its odds ratio is the
# conditional maximum likelihood estimate and its interval is the exact Fisher
# interval: each limit leaves 2.5% in one tail of the conditional distribution. Its
# P value is the two sided Fisher P. All three are found to about four significant
# figures, so the last digits differ from the report, which is exact to 6 places.
print(fisher.test(counts))

six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}
odds <- a * d / (b * c)
cat("Observed odds ratio =", six(odds), "\n")

# Approximate power: the power with which Fisher's exact test on samples of this size
# (348 with the factor, 95 without) would detect the observed difference between the
# proportions of cases, a / (a + c) and b / (b + d), at the two sided 5% level. It is
# the power at which the continuity corrected sample size formula of Casagrande, Pike
# and Smith (1978), in the form given by Fleiss, returns the size of the exposed group.
alpha <- 0.05
p1 <- a / (a + c)
p0 <- b / (b + d)
m <- (b + d) / (a + c)                    # unexposed per exposed subject
z <- qnorm(1 - alpha / 2)
pbar <- (p1 + m * p0) / (m + 1)
size_for_power <- function(power) {
  zb <- qnorm(power)
  n <- (z * sqrt((1 + 1 / m) * pbar * (1 - pbar)) +
          zb * sqrt(p0 * (1 - p0) / m + p1 * (1 - p1)))^2 / (p0 - p1)^2
  n * (1 + sqrt(1 + 2 * (m + 1) / (n * m * abs(p0 - p1))))^2 / 4
}
power <- uniroot(function(pw) size_for_power(pw) - (a + c), c(alpha, 1 - 1e-12),
                 tol = 1e-10)$root         # the size falls as the power asked for falls
cat("Approximate power (for 5% significance) = ",
    formatC(100 * power, digits = 2, format = "f", drop0trailing = TRUE), "%\n",
    sep = "")

# Woolf's (logit) interval from the standard error of the log odds ratio
se <- sqrt(1 / a + 1 / b + 1 / c + 1 / d)
cat("Approximate (Woolf, logit) 95% confidence interval =",
    six(exp(log(odds) - z * se)), "to", six(exp(log(odds) + z * se)), "\n")

# The conditional estimate and exact limits to full precision. Given the margins, the
# top left cell a can take the values lo to hi, with probabilities proportional to the
# central hypergeometric probabilities times (odds ratio)^a
m1 <- a + b                    # cases
n1 <- a + c                    # factor present
n0 <- b + d                    # factor absent
lo <- max(0, m1 - n0)
hi <- min(m1, n1)
x <- lo:hi
prob <- function(psi) {        # the distribution of a when the odds ratio is psi
  lw <- dhyper(x, n1, n0, m1, log = TRUE) + (x - lo) * log(psi)
  w <- exp(lw - max(lw))
  w / sum(w)
}
cat("Conditional maximum likelihood estimates:\n")
# The estimate makes the expected value of a equal to the observed a
cmle <- exp(uniroot(function(l) sum(x * prob(exp(l))) - a, c(-30, 30),
                    tol = 1e-12)$root)
cat("Conditional estimate of odds ratio =", six(cmle), "\n")

# Exact Fisher limits: the lower limit is the odds ratio under which a or more is
# a 2.5% upper tail, the upper limit the one under which a or fewer is a 2.5% lower
# tail. The mid-P limits count half of the probability of a itself in each tail.
limit <- function(upper, half) {
  tail <- function(l) {
    p <- prob(exp(l))
    if (upper) sum(p[x < a]) + half * p[x == a] else sum(p[x > a]) + half * p[x == a]
  }
  exp(uniroot(function(l) tail(l) - alpha / 2, c(-30, 30), tol = 1e-12)$root)
}
cat("Exact Fisher 95% confidence interval =", six(limit(FALSE, 1)), "to",
    six(limit(TRUE, 1)), "\n")

# P values at an odds ratio of 1. The one sided Fisher P is the smaller tail; the two
# sided Fisher P adds up the tables no more probable than the observed one, as
# fisher.test does, allowing a tiny tolerance for probabilities that are equal in
# theory but differ by rounding.
p_null <- prob(1)
up <- sum(p_null[x >= a])
down <- sum(p_null[x <= a])
cat("Exact Fisher one sided ", pv(min(up, down)), ", two sided ",
    pv(sum(p_null[p_null <= p_null[x == a] * (1 + 1e-7)])), "\n", sep = "")

# The mid-P versions count half of the probability of the observed table, and the
# two sided mid-P is twice the one sided value
cat("Exact mid-P 95% confidence interval =", six(limit(FALSE, 0.5)), "to",
    six(limit(TRUE, 0.5)), "\n")
mid <- min(up, down) - 0.5 * p_null[x == a]
cat("Exact mid-P one sided ", pv(mid), ", two sided ", pv(min(2 * mid, 1)), "\n",
    sep = "")

# Population attributable risk, reported when the odds ratio exceeds 1. With no
# population figure entered, the exposure is estimated from the controls, c / (c + d),
# and the attributable fraction Px (OR - 1) / (1 + Px (OR - 1)) is then the same as
# 1 - (b / (a + b)) / (d / (c + d)), whose large sample variance is Walter's (1978).
# All three are printed as percentages.
px <- c / (c + d)
par <- px * (odds - 1) / (1 + px * (odds - 1))
var_par <- (b * (c + d) / (d * (a + b)))^2 * (a / (b * (a + b)) + c / (d * (c + d)))
cat("Population exposure % =", six(100 * px), "(exposure among the controls)\n")
cat("Population attributable risk % =", six(100 * par), "\n")
cat("Approximate 95% confidence interval =", six(100 * (par - z * sqrt(var_par))),
    "to", six(100 * (par + z * sqrt(var_par))), "\n")

# If the population exposure is entered instead (say 50%), it carries no sampling
# error, so the limits are the Woolf limits of the odds ratio put through the same
# formula
px <- 0.5
attributable <- function(or) 100 * px * (or - 1) / (1 + px * (or - 1))
cat("Population exposure % =", six(100 * px), "(as entered)\n")
cat("Population attributable risk % =", six(attributable(odds)), "\n")
cat("Approximate 95% confidence interval =", six(attributable(exp(log(odds) - z * se))),
    "to", six(attributable(exp(log(odds) + z * se))), "\n")
