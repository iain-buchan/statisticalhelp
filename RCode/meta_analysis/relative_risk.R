# Relative risk meta-analysis: the StatsDirect help example (Fleiss and Gross 1991,
# deaths after myocardial infarction in seven placebo-controlled trials of aspirin;
# the test workbook's Meta-analysis worksheet columns A to E) in R
study <- c("MRC-1", "CDP", "MRC-2", "GASP", "PARIS", "AMIS", "ISIS-2")
exposed_total <- c(615, 758, 832, 317, 810, 2267, 8587)
exposed_cases <- c(49, 44, 102, 32, 85, 246, 1570)
control_total <- c(624, 771, 850, 309, 406, 2257, 8600)
control_cases <- c(67, 64, 126, 38, 52, 219, 1720)

# Each study's fourfold table in the report's order: a = exposed cases, b = control
# cases, cx = exposed non-cases and d = control non-cases (cx, because c is R's
# function for making a vector)
a <- exposed_cases
b <- control_cases
cx <- exposed_total - exposed_cases
d <- control_total - control_cases
n <- a + b + cx + d
k <- length(a)
z <- qnorm(0.975)
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
one <- function(x) formatC(x, digits = 1, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}

# Base R has no meta-analysis function, so the report is built from the formulae.
# The relative risk of each study, with Koopman's (1984) likelihood score interval:
# at a trial value of the ratio the two risks are estimated under the constraint
# that they stand in that ratio (the root of a quadratic in the control risk), and
# the limits are the ratios at which the score statistic for the exposed count
# reaches plus and minus the normal deviate. This holds when every cell is filled;
# the program adjusts a total equal to its count by a half before solving.
rr <- (a / (a + cx)) / (b / (b + d))
koopman <- function(x1, n1, x0, n0) {
  score <- function(theta) {
    qa <- (n0 + n1) * theta
    qb <- -((x0 + n1) * theta + x1 + n0)
    qc <- x0 + x1
    p0 <- (-qb - sqrt(qb^2 - 4 * qa * qc)) / (2 * qa)
    p1 <- p0 * theta
    v <- 1 / ((1 - p0) / (n0 * p0) + (1 - p1) / (n1 * p1))
    (x1 - n1 * p1) / (1 - p1) / sqrt(v)
  }
  est <- (x1 / n1) / (x0 / n0)
  c(uniroot(function(t) score(t) - z, c(est / 100, est), tol = 1e-12)$root,
    uniroot(function(t) score(t) + z, c(est, est * 100), tol = 1e-12)$root)
}
ci <- t(mapply(koopman, a, a + cx, b, b + d))
cat("Stratum  Relative risk  95% CI (Koopman)\n")
for (i in 1:k) cat(i, six(rr[i]), six(ci[i, 1]), six(ci[i, 2]), study[i], "\n")

# Fixed effects: the Mantel-Haenszel type pooled risk ratio of Rothman and Boice,
# with the Greenland-Robins variance of its log for the interval and chi-square
w_mh <- b * (a + cx) / n
rr_mh <- sum(a * (b + d) / n) / sum(w_mh)
v_mh <- sum(((a + b) * (a + cx) * (b + d) - a * b * n) / n^2) /
  (sum(a * (b + d) / n) * sum(w_mh))
x2 <- log(rr_mh)^2 / v_mh

# Cochran's Q about the pooled log risk ratio above, each study weighted by the
# inverse of the usual variance of its log relative risk; the DerSimonian-Laird
# moment estimate of the between studies variance then inflates each variance for
# the random effects weights
lrr <- log(rr)
v_i <- 1 / a + 1 / b - 1 / (a + cx) - 1 / (b + d)
w_i <- 1 / v_i
q <- sum(w_i * (lrr - log(rr_mh))^2)
tau2 <- max(0, (q - (k - 1)) / (sum(w_i) - sum(w_i^2) / sum(w_i)))
w_dl <- 1 / (tau2 + v_i)
rr_dl <- exp(sum(w_dl * lrr) / sum(w_dl))
dl_ci <- exp(sum(w_dl * lrr) / sum(w_dl) + c(-1, 1) * z / sqrt(sum(w_dl)))
x2_dl <- sum(w_dl * lrr)^2 / sum(w_dl)

# The report's second table: the log relative risk, its variance recovered from
# the Koopman limits, and each study's share of the fixed and random weights
cat("Stratum  Standardized effect  Variance  % Weights (fixed, random)\n")
v_ci <- ((log(ci[, 2]) - log(ci[, 1])) / (2 * z))^2
for (i in 1:k) {
  cat(i, six(lrr[i]), six(v_ci[i]), six(100 * w_mh[i] / sum(w_mh)),
      six(100 * w_dl[i] / sum(w_dl)), study[i], "\n")
}

cat("Fixed effects (Mantel-Haenszel, Rothman-Boice)\n")
cat("Pooled relative risk = ", six(rr_mh), " (95% CI = ",
    six(exp(log(rr_mh) - z * sqrt(v_mh))), " to ",
    six(exp(log(rr_mh) + z * sqrt(v_mh))), ")\n", sep = "")
cat("Chi2 (test relative risk differs from 1) =", six(x2), " (df = 1) ",
    pv(pchisq(x2, 1, lower.tail = FALSE)), "\n")

# I-squared, with the interval that the heterogeneity topic attributes to Hedges and
# Pigott (2001), from the non-central chi-square distribution of Q: Q - df estimates the
# non-centrality, and the quantiles of that distribution give the limits for H-squared
# and so for I-squared
cat("Non-combinability of studies\n")
cat("Cochran Q = ", six(q), "  (df = ", k - 1, ")  ",
    pv(pchisq(q, k - 1, lower.tail = FALSE)), "\n", sep = "")
cat("Moment-based estimate of between studies variance =", six(tau2), "\n")
i2 <- max(0, 100 * (q - (k - 1)) / q)
nc <- max(0, q - (k - 1))
h2_lower <- max(1, qchisq(0.025, k - 1, ncp = nc) / (k - 1))
h2_upper <- qchisq(0.975, k - 1, ncp = nc) / (k - 1)
cat("I2 (inconsistency) = ", one(i2), "% (95% CI = ",
    one(100 * (h2_lower - 1) / h2_lower), "% to ",
    one(100 * (h2_upper - 1) / h2_upper), "%)\n", sep = "")

cat("Random effects (DerSimonian-Laird)\n")
cat("Pooled relative risk = ", six(rr_dl), " (95% CI = ", six(dl_ci[1]), " to ",
    six(dl_ci[2]), ")\n", sep = "")
cat("Chi2 (test relative risk differs from 1) =", six(x2_dl), " (df = 1) ",
    pv(pchisq(x2_dl, 1, lower.tail = FALSE)), "\n")

# Bias indicators. Begg and Mazumdar's rank correlation is Kendall's tau between
# each study's deviation from the inverse variance pooled log relative risk,
# standardised by its variance less the pooled variance, and that variance; with
# no ties among seven studies cor.test gives the exact two sided P, and fewer than
# eleven studies give the test little power
se_i <- sqrt(v_i)
dev <- (lrr - sum(w_i * lrr) / sum(w_i)) / sqrt(v_i - 1 / sum(w_i))
kt <- cor.test(dev, v_i, method = "kendall")
cat("Bias indicators\n")
cat("Begg-Mazumdar: Kendall's tau =", six(kt$estimate), "", pv(kt$p.value),
    "(low power)\n")

# Egger's test regresses each study's standardised effect (log relative risk over
# its standard error) on its precision (one over that standard error); the bias
# is the intercept, judged by a t test on k - 2 degrees of freedom with a 90%
# interval, the level the test is conventionally reported at
egger <- function(y, x, label) {
  fit <- lm(y ~ x)
  bias <- coef(fit)[1]
  se <- sqrt(vcov(fit)[1, 1])
  t <- bias / se
  cat(label, ": bias = ", six(bias), " (90% CI = ", six(bias - qt(0.95, k - 2) * se),
      " to ", six(bias + qt(0.95, k - 2) * se), ") ",
      pv(2 * pt(-abs(t), k - 2)), "\n", sep = "")
}
egger(lrr / se_i, 1 / se_i, "Egger")

# Harbord's modification (Harbord, Egger and Sterne 2006) uses the efficient score
# for the log relative risk and its Fisher information in place of the estimate and
# its standard error, which are far less correlated than the estimate and its standard
# error when effects are large or events few
score <- (a * n - (a + b) * (a + cx)) / (cx + d)
info <- (b + d) * (a + cx) * (a + b) / (n * (cx + d))
egger(score / sqrt(info), sqrt(info), "Harbord-Egger")

# The report's L'Abbe plot: the risk in the exposed group of each study against
# the risk in its control group, the symbol growing with the study size, with the
# line of equal risks and the line of the pooled relative risk
plot(100 * b / (b + d), 100 * a / (a + cx), cex = 0.5 + 2 * sqrt(n / max(n)),
     xlim = c(0, 25), ylim = c(0, 25), xlab = "control percent",
     ylab = "experimental percent",
     main = "L'Abbe plot (symbol size represents sample size)")
abline(0, 1)
abline(0, rr_mh, lty = 3)

# The two forest plots: each study's relative risk (the symbol growing with its
# weight) with its interval on a log scale, the line of no effect at 1 and the
# pooled estimate as a diamond at the foot
forest <- function(weights, pooled, lower, upper, title, label) {
  y <- rev(seq_len(k)) + 1
  par(mar = c(5, 8, 4, 8))
  plot(rr, y, log = "x", xlim = range(ci, lower, upper), ylim = c(0.5, k + 1.5),
       pch = 15, cex = 0.5 + 2 * sqrt(weights / max(weights)), yaxt = "n", ylab = "",
       xlab = "relative risk (95% confidence interval)", main = title)
  segments(ci[, 1], y, ci[, 2], y)
  polygon(c(lower, pooled, upper, pooled), c(1, 1.3, 1, 0.7), col = "grey")
  segments(pooled, 1, pooled, k + 1, lty = 3)
  abline(v = 1)
  axis(2, at = c(y, 1), labels = c(study, label), las = 1, tick = FALSE)
  axis(4, at = c(y, 1), las = 1, tick = FALSE,
       labels = sprintf("%.2f (%.2f, %.2f)", c(rr, pooled), c(ci[, 1], lower),
                        c(ci[, 2], upper)))
}
forest(w_mh, rr_mh, exp(log(rr_mh) - z * sqrt(v_mh)), exp(log(rr_mh) + z * sqrt(v_mh)),
       "Relative risk meta-analysis plot (fixed effects)", "combined [fixed]")
forest(w_dl, rr_dl, dl_ci[1], dl_ci[2],
       "Relative risk meta-analysis plot (random effects)", "combined [random]")
