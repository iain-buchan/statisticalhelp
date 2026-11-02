# Number needed to treat: the StatsDirect help example (Haynes and Sackett 1993, a trial
# of an ACE inhibitor in severe congestive heart failure: 123 of 607 controls and 94 of
# 607 treated patients died within six months) in R
nc <- 607   # controls
xc <- 123   # controls who died (had the event)
nt <- 607   # treated
xt <- 94    # treated who died
level <- 0.95

# Base R has no number needed to treat function. Each risk is a binomial proportion,
# and its exact (Clopper-Pearson) confidence interval is the one binom.test gives:
# the risk of death among the controls, for instance
print(binom.test(xc, nc, conf.level = level))

six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
risk <- function(x, n) {
  ci <- binom.test(x, n, conf.level = level)$conf.int
  paste0(x, "/", n, " = ", six(x / n), " (", six(ci[1]), " to ", six(ci[2]), ")")
}

# A relative risk is a ratio of two binomial proportions. Its interval is Koopman's
# (1984) score interval: the ratios at which the score chi-square, with the two
# proportions estimated under the constraint that they have that ratio, reaches the
# critical value. All four cells of this table are non-zero, as the search assumes.
koopman <- function(x1, n1, x0, n0) {   # the ratio (x1 / n1) / (x0 / n0)
  chi2 <- function(theta) {
    A <- (n0 + n1) * theta
    B <- -((x0 + n1) * theta + x1 + n0)
    p0 <- (-B - sqrt(B^2 - 4 * A * (x0 + x1))) / (2 * A)  # constrained estimate
    p1 <- theta * p0
    (x1 - n1 * p1)^2 / (n1 * p1 * (1 - p1)) *
      (1 + n1 * (theta - p1) / (n0 * (1 - p1)))
  }
  est <- (x1 / n1) / (x0 / n0)
  f <- function(lt) chi2(exp(lt)) - qchisq(level, 1)
  lower <- exp(uniroot(f, c(log(est) - 20, log(est)), tol = 1e-12)$root)
  upper <- exp(uniroot(f, c(log(est), log(est) + 20), tol = 1e-12)$root)
  c(est, lower, upper)
}
interval <- function(v) paste0(six(v[1]), " (", six(v[2]), " to ", six(v[3]), ")")
cat("Estimates with 95% confidence intervals:\n")
cat("Risk of event in controls =", risk(xc, nc), "\n")
cat("Risk of event in treated =", risk(xt, nt), "\n")
rre <- koopman(xt, nt, xc, nc)
cat("Relative risk of event =", interval(rre), "\n")
cat("Risk of no event in controls =", risk(nc - xc, nc), "\n")
cat("Risk of no event in treated =", risk(nt - xt, nt), "\n")
rrne <- koopman(nt - xt, nt, nc - xc, nc)
cat("Relative risk of no event =", interval(rrne), "\n")

# The odds ratio of death in the treated compared with the controls, with exact
# (Fisher) limits. fisher.test gives them to about four significant figures, around
# its conditional maximum likelihood estimate rather than the observed odds ratio
counts <- matrix(c(xt, nt - xt, xc, nc - xc), 2, byrow = TRUE,
                 dimnames = list(Group = c("Treated", "Controls"),
                                 Outcome = c("Died", "Survived")))
print(fisher.test(counts, conf.level = level))

# Solving the same equations reproduces the report's limits: given the margins of the
# table, the number of treated deaths follows the non-central hypergeometric
# distribution with the odds ratio as its parameter, and each limit puts a tail
# probability of 2.5% at the observed count
m1 <- xt + xc                            # deaths in all
x <- max(0, m1 - nc):min(m1, nt)         # the possible numbers of treated deaths
logd <- dhyper(x, nt, nc, m1, log = TRUE)
dens <- function(log_or) {
  w <- exp(logd + x * log_or - max(logd + x * log_or))
  w / sum(w)
}
solve_or <- function(f) exp(uniroot(f, c(-40, 40), tol = 1e-13)$root)
oor <- c(xt * (nc - xc) / (xc * (nt - xt)),
         solve_or(function(lo) sum(dens(lo)[x >= xt]) - (1 - level) / 2),
         solve_or(function(lo) sum(dens(lo)[x <= xt]) - (1 - level) / 2))
cat("Odds ratio of event in treated cf. controls =", interval(oor), "\n")

# The relative risk reduction is 1 - the relative risk of event, its limits likewise
rrr <- 1 - rre[c(1, 3, 2)]
cat("Relative risk reduction (controls-treated) =", interval(rrr), "\n")

# The risk difference, with the score interval of Miettinen and Nurminen (1985): the
# differences at which the score chi-square, with the two risks estimated by maximum
# likelihood under the constraint that they differ by that amount, and the variance
# multiplied by N / (N - 1), reaches the critical value. The constrained estimate of
# the treated risk is the root of the derivative of the log likelihood in it.
z <- qnorm(1 - (1 - level) / 2)
score <- function(d) {
  dl <- function(p) {
    xc / (p + d) - (nc - xc) / (1 - p - d) + xt / p - (nt - xt) / (1 - p)
  }
  p <- uniroot(dl, c(max(0, -d) + 1e-12, min(1, 1 - d) - 1e-12), tol = 1e-15)$root
  v <- ((p + d) * (1 - p - d) / nc + p * (1 - p) / nt) * (nc + nt) / (nc + nt - 1)
  (xc / nc - xt / nt - d)^2 / v - z^2
}
rd <- c(xc / nc - xt / nt, NA, NA)
rd[2] <- uniroot(score, c(-1 + 1e-9, rd[1]), tol = 1e-14)$root
rd[3] <- uniroot(score, c(rd[1], 1 - 1e-9), tol = 1e-14)$root
cat("Risk difference (controls-treated) =", interval(rd), "\n")

# The number needed to treat is 1 / the risk difference, and its limits are the
# reciprocals of the difference's limits. Altman (1998) quotes each as a number
# needed to treat for one patient to benefit (a positive difference) or to be harmed
# (a negative one), the harm limit first when the interval spans both. The rounded
# up figures are the whole numbers at or above them
nnt_text <- function(x, roundup = FALSE) {
  paste0(if (roundup) ceiling(abs(x)) else six(abs(x)),
         if (x < 0) "_harm" else "_benefit")
}
nnt <- function(label, v) {
  l <- v[2:3]
  l <- if (all(l < 0)) l[order(abs(l))] else sort(l)
  cat("NNT [", label, "] = ", nnt_text(v[1]), " (", nnt_text(l[1]), " to ",
      nnt_text(l[2]), ")\n", sep = "")
  cat("NNT [", label, "] (rounded up) = ", nnt_text(v[1], TRUE), " (",
      nnt_text(l[1], TRUE), " to ", nnt_text(l[2], TRUE), ")\n", sep = "")
}
nnt("risk difference", 1 / rd)

# Given an expected risk of the event without treatment at the optional prompt, the
# report adds the NNTs that assume the relative risk of event, the relative risk of no
# event or the odds ratio holds at that risk, by the formulae above: here 30%
er <- 0.3
cat("Expected risk of event without treatment = 30%\n")
nnt("risk difference", 1 / rd)
nnt("relative risk of event", 1 / (er * rrr))
nnt("relative risk of no event", 1 / ((1 - er) * (rrne - 1)))
nnt("odds ratio", (1 - er * (1 - oor)) / ((1 - er) * er * (1 - oor)))
