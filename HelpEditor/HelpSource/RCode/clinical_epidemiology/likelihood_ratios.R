# Likelihood ratios for the levels of a test: the StatsDirect help example (Sackett et
# al. 1991, p. 111, initial creatine kinase levels of 230 patients who had an acute
# myocardial infarction and 130 who had not, in four ranges of CK) in R
ck <- c("280 or more", "80 to 279", "40 to 79", "1 to 39")
mi <- c(97, 118, 13, 2)      # + feature: patients with an MI at each CK level
no_mi <- c(1, 15, 26, 88)    # - feature: patients without an MI at each CK level
counts <- cbind(MI = mi, "No MI" = no_mi)
rownames(counts) <- ck
print(addmargins(counts))

# The likelihood ratio of a level is the proportion of the MI patients at that level
# over the proportion of the non-MI patients there; the proportions come from
# prop.table applied down the columns
p <- prop.table(counts, margin = 2)
print(p, digits = 4)
lr <- p[, "MI"] / p[, "No MI"]
print(lr, digits = 7)

# Base R has no function for the confidence interval of a ratio of two binomial
# proportions, so the limits are found as Koopman (1984) defines them: the ratios at
# which the score chi-square, with the two proportions estimated under the constraint
# that they have that ratio, reaches the chi-square critical value for the chosen level
# (95% here). Every count in the example is greater than zero, which this function
# assumes.
koopman <- function(x1, n1, x0, n0, level = 0.95) {
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

# The report's table: one row per level, numbered in the order given
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
cat("Result  + Feature  - Feature  Likelihood Ratio  95% CI (Koopman)\n")
for (i in seq_along(mi)) {
  k <- koopman(mi[i], sum(mi), no_mi[i], sum(no_mi))
  cat(i, " ", mi[i], " ", no_mi[i], " ", six(k[1]), " ", six(k[2]), " to ", six(k[3]),
      "\n", sep = "")
}

# Bayes' theorem in odds form: with a prior probability of MI of 0.5, say, the prior
# odds are 1, and a CK of 280 or more multiplies them by that level's likelihood ratio
prior <- 0.5
posterior_odds <- prior / (1 - prior) * lr[1]
cat("Posterior probability of MI given CK of 280 or more =",
    six(posterior_odds / (posterior_odds + 1)), "\n")
