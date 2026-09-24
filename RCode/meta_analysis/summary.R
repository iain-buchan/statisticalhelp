# Summary data meta-analysis: the StatsDirect help example (the odds ratio of each of
# seven trials of aspirin after heart attack, Fleiss 1993, with its 95% confidence
# limits; the test workbook's Meta-analysis worksheet columns Odds Ratio, LCI, UCI and
# Study) in R
study <- c("MRC-1", "CDP", "MRC-2", "GASP", "PARIS", "AMIS", "ISIS-2")
or <- c(0.719714, 0.68076, 0.80287, 0.800739, 0.798143, 1.132736, 0.894969)
lower <- c(0.47831, 0.446423, 0.599864, 0.46972, 0.545041, 0.930385, 0.828783)
upper <- c(1.077371, 1.030758, 1.072965, 1.358784, 1.177753, 1.37967, 0.966411)
k <- length(or)
z <- qnorm(0.975)
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
one <- function(x) formatC(x, digits = 1, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}

# Base R has no meta-analysis function, so the report is built from the formulae.
# A ratio is analysed on the log scale: each study's standard error of the log odds
# ratio is recovered from its limits as their log difference over twice the normal
# deviate (had standard errors been given instead, the limits would be recovered
# from them the other way round), and the inverse variance weights follow
y <- log(or)
se <- (log(upper) - log(lower)) / (2 * z)
w <- 1 / se^2
cat("Study  Odds ratio  SE  Approximate 95% CI\n")
for (i in 1:k) {
  cat(i, six(or[i]), six(se[i]), six(lower[i]), six(upper[i]), study[i], "\n")
}

# Fixed effects: the weighted mean of the log odds ratios, its standard error the
# root of the reciprocal of the total weight, transformed back to the ratio scale
pooled <- sum(w * y) / sum(w)
se_pooled <- 1 / sqrt(sum(w))
z_fixed <- pooled / se_pooled

# Cochran's Q about the pooled log odds ratio, then the DerSimonian-Laird moment
# estimate of the between studies variance, which inflates each study's variance
# for the random effects weights
q <- sum(w * (y - pooled)^2)
tau2 <- max(0, (q - (k - 1)) / (sum(w) - sum(w^2) / sum(w)))
w_dl <- 1 / (tau2 + se^2)
pooled_dl <- sum(w_dl * y) / sum(w_dl)
se_dl <- 1 / sqrt(sum(w_dl))
z_dl <- pooled_dl / se_dl

# The report's second table: each study's standardised effect, the log odds ratio on
# which the pooling is done, with its standard error on that scale and its share of
# the fixed and random weights
cat("Stratum  Standardized effect  Standard error  % Weights (fixed, random)\n")
for (i in 1:k) {
  cat(i, six(y[i]), six(se[i]), six(100 * w[i] / sum(w)),
      six(100 * w_dl[i] / sum(w_dl)), study[i], "\n")
}

cat("Fixed effects (inverse variance)\n")
cat("Pooled odds ratio = ", six(exp(pooled)), " (95% CI = ",
    six(exp(pooled - z * se_pooled)), " to ", six(exp(pooled + z * se_pooled)), ")\n",
    sep = "")
cat("Z (test Odds Ratio differs from 1) =", six(z_fixed), " ",
    pv(2 * pnorm(-abs(z_fixed))), "\n")

# I-squared = (Q - df) / Q, with the interval of the heterogeneity topic's default
# 'exact' option (the iterative method it credits to Hedges and Pigott, 2001): Q is
# treated as non-central chi-square and its distribution function at the observed Q
# is inverted for the non-centrality parameter lambda, the lower limit where that
# probability is 0.975 (0 when even the central distribution gives less) and the
# upper where it is 0.025; each limit becomes I-squared as lambda / (df + lambda)
cat("Non-combinability of studies\n")
dfree <- k - 1
cat("Cochran Q = ", six(q), "  (df = ", dfree, ")  ",
    pv(pchisq(q, dfree, lower.tail = FALSE)), "\n", sep = "")
cat("Moment-based estimate of between studies variance =", six(tau2), "\n")
i2 <- max(0, 100 * (q - dfree) / q)
lambda <- function(p) {
  if (pchisq(q, dfree) < p) 0 else
    uniroot(function(l) pchisq(q, dfree, ncp = l) - p, c(0, 10 * q + 100),
            tol = 1e-10)$root
}
lim <- c(lambda(0.975), lambda(0.025))
cat("I2 (inconsistency) = ", one(i2), "% (95% CI = ",
    one(100 * lim[1] / (dfree + lim[1])), "% to ",
    one(100 * lim[2] / (dfree + lim[2])), "%)\n", sep = "")

cat("Random effects (DerSimonian-Laird)\n")
cat("Pooled odds ratio = ", six(exp(pooled_dl)), " (95% CI = ",
    six(exp(pooled_dl - z * se_dl)), " to ", six(exp(pooled_dl + z * se_dl)), ")\n",
    sep = "")
cat("Z (test Odds Ratio) =", six(z_dl), " ", pv(2 * pnorm(-abs(z_dl))), "\n")

# Bias indicators. Begg and Mazumdar's test is Kendall's tau between each study's
# deviation from the pooled log odds ratio, standardised by its variance less the
# pooled variance, and that variance; with no ties among seven studies cor.test
# gives the exact two sided P (with tied standard errors it would give tau-b and a
# normal approximation, which the report corrects for continuity); the report marks
# the test as low in power when there are fewer than eleven studies
dev <- (y - pooled) / sqrt(se^2 - se_pooled^2)
kt <- cor.test(dev, se^2, method = "kendall")
cat("Bias indicators\n")
cat("Begg-Mazumdar: Kendall's tau =", six(kt$estimate), " ", pv(kt$p.value),
    "(low power)\n")

# Egger's test regresses each study's standardised effect (log odds ratio over its
# standard error) on its precision (one over that standard error); the bias is the
# intercept, judged by a t test on k - 2 degrees of freedom; the interval is at
# twice the analysis's alpha, 90% for this 95% analysis
fit <- lm(I(y / se) ~ I(1 / se))
bias <- coef(fit)[1]
se_bias <- sqrt(vcov(fit)[1, 1])
cat("Egger: bias = ", six(bias), " (90% CI = ", six(bias - qt(0.95, k - 2) * se_bias),
    " to ", six(bias + qt(0.95, k - 2) * se_bias), ")  ",
    pv(2 * pt(-abs(bias / se_bias), k - 2)), "\n", sep = "")

# The report's bias assessment (funnel) plot: each log odds ratio against its
# standard error, the axis reversed so that the 95% cone about the pooled log odds
# ratio opens downwards
se_top <- max(se) * 1.05
plot(y, se, ylim = c(se_top, 0), xlim = range(y, pooled + c(-1, 1) * z * se_top),
     xlab = "Log(odds ratio)", ylab = "Standard error", main = "Bias assessment plot")
abline(v = pooled)
lines(pooled + c(-1, 0, 1) * z * se_top, c(se_top, 0, se_top))

# The two forest plots: each study's odds ratio (the symbol size growing with its
# weight) with its interval on a log scale, the line of no effect at 1 and the
# pooled estimate as a diamond at the foot
forest <- function(weights, est, se_est, title) {
  yy <- rev(seq_len(k)) + 1
  limits <- exp(est + c(-1, 1) * z * se_est)
  par(mar = c(5, 8, 4, 8))
  plot(or, yy, log = "x", xlim = range(lower, upper, limits), ylim = c(0.5, k + 1.5),
       pch = 15, cex = 0.5 + 2 * sqrt(weights / max(weights)), yaxt = "n", ylab = "",
       xlab = "odds ratio (95% confidence interval)", main = title)
  segments(lower, yy, upper, yy)
  polygon(c(limits[1], exp(est), limits[2], exp(est)), c(1, 1.3, 1, 0.7), col = "grey")
  segments(exp(est), 1, exp(est), k + 1, lty = 3)
  abline(v = 1)
  axis(2, at = c(yy, 1), labels = c(study, "combined"), las = 1, tick = FALSE)
  axis(4, at = c(yy, 1), las = 1, tick = FALSE,
       labels = sprintf("%.2f (%.2f, %.2f)", c(or, exp(est)), c(lower, limits[1]),
                        c(upper, limits[2])))
}
forest(w, pooled, se_pooled, "Summary meta-analysis plot [fixed effects]")
forest(w_dl, pooled_dl, se_dl, "Summary meta-analysis plot [random effects]")
