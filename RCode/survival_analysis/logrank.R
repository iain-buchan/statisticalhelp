# Log-rank and Wilcoxon tests: the StatsDirect help example (Armitage and Berry 1994,
# survival in days of lymphoma patients with stage III or stage IV disease; the test
# workbook's Survival worksheet columns Stage group, Time and Censor) in R
library(survival)
stage <- rep(c(1, 2), c(19, 61))
time <- c(6, 19, 32, 42, 42, 43, 94, 126, 169, 207, 211, 227, 253, 255, 270, 310, 316,
          335, 346, 4, 6, 10, 11, 11, 11, 13, 17, 20, 20, 21, 22, 24, 24, 29, 30, 30,
          31, 33, 34, 35, 39, 40, 41, 43, 45, 46, 50, 56, 61, 61, 63, 68, 82, 85, 88,
          89, 90, 93, 104, 110, 134, 137, 160, 169, 171, 173, 175, 184, 201, 222, 235,
          247, 260, 284, 290, 291, 302, 304, 341, 345)
dead <- c(1, 1, 1, 1, 1, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1,
          1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 1, 1, 1, 1, 0, 0, 1, 1,
          1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0,
          0, 0)

# R's standard test: the log-rank test. Its Observed and Expected are the report's
# observed deaths and extents of exposure to risk of death, and its chi-square is the
# report's, both using the hypergeometric variance at each death time
print(survdiff(Surv(time, dead) ~ stage))

six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}

# The report's statistics from the formulae above, for groups numbered 1 to k. At each
# distinct time the numbers at risk and the deaths in each group give the expected
# deaths, the weighted score U and its variance matrix V. The log-rank weight is 1.
# The Peto-Prentice weight is a survivor function estimate: the product over the
# earlier distinct times of (n - d + 1) / (n + 1), multiplied by n / (n + 1) at the
# current time. The chi-square drops the last group's row and column, as V is singular.
rank_test <- function(time, dead, group, wilcoxon = FALSE) {
  k <- max(group)
  O <- tabulate(group[dead == 1], k)
  E <- numeric(k)
  U <- numeric(k)
  V <- matrix(0, k, k)
  surv <- 1
  for (t in sort(unique(time))) {
    atrisk <- time >= t
    n <- sum(atrisk)
    ni <- tabulate(group[atrisk], k)
    d <- sum(dead[time == t])
    di <- tabulate(group[time == t & dead == 1], k)
    w <- if (wilcoxon) surv * n / (n + 1) else 1
    E <- E + d * ni / n
    U <- U + w * (di - d * ni / n)
    if (n > 1) {
      V <- V + w^2 * (diag(ni * n, k) - outer(ni, ni)) * d * (n - d) / (n^2 * (n - 1))
    }
    surv <- surv * (n - d + 1) / (n + 1)
  }
  chi <- as.numeric(t(U[-k]) %*% solve(V[-k, -k]) %*% U[-k])
  p <- pchisq(chi, k - 1, lower.tail = FALSE)
  list(O = O, E = E, U = U, V = V, chi = chi, p = p)
}
show_test <- function(r, groups = TRUE) {
  if (groups) {
    for (i in seq_along(r$O)) {
      cat("For group", i, "\n")
      cat("Observed deaths =", r$O[i], "\n")
      cat("Extent of exposure to risk of death =", six(r$E[i]), "\n")
      cat("Relative rate =", six(r$O[i] / r$E[i]), "\n")
    }
  }
  cat("test statistics:", six(r$U), "\n")
  cat("variance-covariance matrix:\n")
  for (i in seq_along(r$O)) cat(six(r$V[i, ]), "\n")
  cat("Chi-square for equivalence of death rates =", six(r$chi), " ", pv(r$p), "\n")
}
cat("Log-rank (Peto):\n")
lr <- rank_test(time, dead, stage)
show_test(lr)

# Hazard ratio: the ratio of the relative rates, with the approximate interval from the
# standard error of its log given above
hazard_ratio <- function(O, E, i, j, level = 0.95) {
  hr <- (O[i] / E[i]) / (O[j] / E[j])
  half <- qnorm((1 + level) / 2) * sqrt(1 / E[i] + 1 / E[j])
  cat("Group ", i, " vs. Group ", j, " = ", six(hr), " (", six(hr * exp(-half)), " to ",
      six(hr * exp(half)), ")\n", sep = "")
}
cat("Hazard Ratio (approximate 95% confidence interval)\n")
hazard_ratio(lr$O, lr$E, 1, 2)

# Conditional maximum likelihood estimate: a 2 by 2 table at each death time (deaths
# and survivors by group among those at risk) and the exact test of a common odds
# ratio across the tables. R's estimate, interval and two sided P agree with the
# report's Fisher figures to five decimal places
death_tables <- function(time, dead, group) {
  cells <- NULL
  for (t in sort(unique(time[dead == 1]))) {
    atrisk <- time >= t
    ni <- tabulate(group[atrisk], 2)
    di <- tabulate(group[time == t & dead == 1], 2)
    if (all(ni > 0)) cells <- c(cells, di, ni - di)
  }
  array(cells, c(2, 2, length(cells) / 4))
}
tables <- death_tables(time, dead, stage)
print(mantelhaen.test(tables, exact = TRUE))

# The same from the conditional distribution of the total deaths in group 1 given the
# margins of every table: the product of the hypergeometric polynomials of the tables,
# scaled as it grows to stay within range. Each side's P counts the observed value in
# full (Fisher) or by half (mid-P); the two sided Fisher P sums the probabilities no
# larger than the observed value's, and the two sided mid-P is twice the smaller side.
# A limit is the ratio at which a tail probability equals half of one minus the level
poly <- 1
low <- 0
for (i in seq_len(dim(tables)[3])) {
  n1 <- sum(tables[1, , i])
  n0 <- sum(tables[2, , i])
  m1 <- sum(tables[, 1, i])
  a <- max(0, m1 - n0):min(m1, n1)
  coef <- choose(n1, a) * choose(n0, m1 - a)
  product <- numeric(length(poly) + length(coef) - 1)
  for (j in seq_along(coef)) {
    at <- j - 1 + seq_along(poly)
    product[at] <- product[at] + poly * coef[j]
  }
  poly <- product / max(product)
  low <- low + a[1]
}
support <- low + seq_along(poly) - 1
A <- sum(tables[1, 1, ])
observed <- which(support == A)
prob <- function(ratio) {
  lp <- log(poly) + (support - low) * log(ratio)
  p <- exp(lp - max(lp))
  p / sum(p)
}
p <- prob(1)
upper <- sum(p[observed:length(p)])
lower <- sum(p[1:observed])
upper_mid <- upper - p[observed] / 2
tail <- function(log_ratio, target, half) {
  q <- prob(exp(log_ratio))
  sum(q[observed:length(q)]) - half * q[observed] - target
}
limit <- function(target, half) {
  exp(uniroot(tail, c(-30, 30), target = target, half = half, tol = 1e-12)$root)
}
mean_deaths <- function(log_ratio) sum(prob(exp(log_ratio)) * support) - A
cmle <- exp(uniroot(mean_deaths, c(-30, 30), tol = 1e-12)$root)
cat("Conditional maximum likelihood estimates:\n")
cat("Hazard Ratio =", six(cmle), "\n")
cat("Exact Fisher 95% confidence interval =", six(limit(0.025, 0)), "to",
    six(limit(0.975, 1)), "\n")
cat("Exact Fisher one sided ", pv(min(upper, lower)), ", two sided ",
    pv(sum(p[p <= p[observed]])), "\n", sep = "")
cat("Exact mid-P 95% confidence interval =", six(limit(0.025, 0.5)), "to",
    six(limit(0.975, 0.5)), "\n")
cat("Exact mid-P one sided ", pv(min(upper_mid, 1 - upper_mid)), ", two sided ",
    pv(min(1, 2 * min(upper_mid, 1 - upper_mid))), "\n", sep = "")

# Generalised Wilcoxon test. R's nearest is the Peto and Peto modification, with the
# Kaplan-Meier estimate as the weight (rho = 1): chi-square 5.45, against the report's
# 5.445 with the Peto-Prentice weight described above
print(survdiff(Surv(time, dead) ~ stage, rho = 1))
cat("Generalised Wilcoxon (Peto-Prentice):\n")
show_test(rank_test(time, dead, stage, wilcoxon = TRUE), groups = FALSE)

# The stratified example (Peto et al. 1977; the test workbook's Survival worksheet
# columns Group, Trial Time, Censorship and Strat): the test within each stratum, then
# the scores and variance matrices summed over the strata for the combined test
trial <- c(8, 8, 13, 18, 23, 52, 63, 63, 70, 70, 180, 195, 210, 220, 365, 632, 700,
           852, 1296, 1296, 1328, 1460, 1976, 1990, 2240)
event <- c(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 0, 1, 0, 0, 0, 0, 0, 0)
arm <- c(1, 1, 2, 2, 2, 1, 1, 1, 2, 2, 2, 2, 2, 1, 1, 2, 2, 1, 2, 1, 1, 1, 1, 2, 2)
stratum <- c(1, 2, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2)
print(survdiff(Surv(trial, event) ~ arm + strata(stratum)))
combined <- function(wilcoxon) {
  total <- NULL
  for (s in 1:2) {
    r <- rank_test(trial[stratum == s], event[stratum == s], arm[stratum == s],
                   wilcoxon)
    cat("STRATUM", s, "of 2\n")
    show_test(r, groups = !wilcoxon)
    total <- if (is.null(total)) r else Map("+", total, r)
  }
  cat("COMBINED STRATA: group, deaths, extent of exposure to risk of death,",
      "relative rate\n")
  for (i in 1:2) cat(i, total$O[i], six(total$E[i]), six(total$O[i] / total$E[i]), "\n")
  chi <- total$U[1]^2 / total$V[1, 1]
  cat("overall chi-square =", six(chi), " ", pv(pchisq(chi, 1, lower.tail = FALSE)),
      "\n")
  total
}
cat("Log-rank (Peto), stratified:\n")
total <- combined(wilcoxon = FALSE)
cat("Hazard Ratio (approximate 95% confidence interval)\n")
hazard_ratio(total$O, total$E, 1, 2)
cat("Generalised Wilcoxon (Peto-Prentice), stratified:\n")
total <- combined(wilcoxon = TRUE)
