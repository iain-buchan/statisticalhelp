# Correlation meta-analysis: the StatsDirect help example (eight studies of the
# relationship between drug misuse and delinquency; the test workbook's meta worksheet
# columns Correlation and Sample size) in R
r <- c(0.51, 0.48, 0.30, 0.21, 0.60, 0.46, 0.22, 0.25)
n <- c(131, 129, 155, 121, 111, 119, 112, 145)

# Base R has no meta-analysis function, so the Hedges-Olkin method is computed as the
# report describes it: each correlation becomes Fisher's z (atanh), whose variance is
# 1 / (n - 3); the studies are pooled on the z scale with weights n - 3, and the pooled
# z and its limits are transformed back to correlations (tanh)
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}
k <- length(r)
z <- atanh(r)
v <- 1 / (n - 3)
cit <- qnorm(0.975)
lower <- tanh(z - cit * sqrt(v))
upper <- tanh(z + cit * sqrt(v))
w <- 1 / v
z_fixed <- sum(w * z) / sum(w)
se_fixed <- 1 / sqrt(sum(w))
fixed <- tanh(z_fixed + c(0, -1, 1) * cit * se_fixed)

# Cochran's Q on the z scale, and the DerSimonian-Laird moment estimate of the
# between-studies variance that the random effects weights add to each variance
Q <- sum(w * (z - z_fixed)^2)
df <- k - 1
tau2 <- max(0, (Q - df) / (sum(w) - sum(w^2) / sum(w)))
w_random <- 1 / (v + tau2)
z_random <- sum(w_random * z) / sum(w_random)
se_random <- 1 / sqrt(sum(w_random))
random <- tanh(z_random + c(0, -1, 1) * cit * se_random)

# The report's two tables: each correlation with its approximate 95% interval, then
# its Fisher z, variance and percentage weights (fixed effects, random effects, and by
# sample size as the Schmidt-Hunter method below weights)
print(data.frame(Study = 1:k, Size = n, Correlation = r, Lower = six(lower),
                 Upper = six(upper)), row.names = FALSE)
# (study 1's variance is exactly 1/128, printed 0.007812 here and 0.007813 by the
# program, which squares the standard error)
print(data.frame(Study = 1:k, Fisher_z = six(z), Variance = six(v),
                 Fixed = six(100 * w / sum(w)),
                 Random = six(100 * w_random / sum(w_random)),
                 Size = six(100 * n / sum(n))), row.names = FALSE)

cat("Hedges-Olkin fixed effects\n")
cat("Pooled correlation = ", six(fixed[1]), " (95% CI = ", six(fixed[2]), " to ",
    six(fixed[3]), ")\n", sep = "")
cat("Z (test correlation differs from 0) =", six(z_fixed / se_fixed), " ",
    pv(2 * pnorm(-abs(z_fixed / se_fixed))), "\n")

# I-squared is the percentage of Q beyond its degrees of freedom. Its confidence
# interval is the non-central chi-square method of Hedges and Pigott (2001) that the
# report uses: Q is treated as a non-central chi-square variable on df degrees of
# freedom, and each limit is the non-centrality parameter at which the observed Q sits
# at the 97.5th (lower limit) or the 2.5th (upper limit) percentile, found with uniroot
# and pchisq; a limit is 0 when the central distribution already puts Q below that
# percentile. A non-centrality lambda gives I-squared = lambda / (df + lambda)
cat("Non-combinability of studies\n")
cat("Cochran Q = ", six(Q), "  (df = ", df, ")  ",
    pv(pchisq(Q, df, lower.tail = FALSE)), "\n", sep = "")
cat("Moment-based estimate of between studies variance =", six(tau2), "\n")
one <- function(x) formatC(x, digits = 1, format = "f", drop0trailing = TRUE)
ncp_limit <- function(p) {
  if (pchisq(Q, df) <= p) return(0)
  # the bracket Q + 1000 is ample: the distribution function there is near 0
  uniroot(function(l) pchisq(Q, df, ncp = l) - p, c(0, Q + 1000), tol = 1e-10)$root
}
lambda <- c(ncp_limit(0.975), ncp_limit(0.025))
i2_ci <- 100 * lambda / (df + lambda)
cat("I-squared (inconsistency) = ", one(max(0, 100 * (Q - df) / Q)), "% (95% CI = ",
    one(i2_ci[1]), "% to ", one(i2_ci[2]), "%)\n", sep = "")

cat("Hedges-Olkin random effects\n")
cat("Pooled correlation = ", six(random[1]), " (95% CI = ", six(random[2]), " to ",
    six(random[3]), ")\n", sep = "")
cat("Z (test correlation differs from 0) =", six(z_random / se_random), " ",
    pv(2 * pnorm(-abs(z_random / se_random))), "\n")

# Begg-Mazumdar: Kendall's rank correlation between each study's standardised
# deviation from the fixed effects pooled z and its variance. With no tied variances
# cor.test gives the exact two sided P, as the report does; with tied sample sizes the
# report instead uses a continuity corrected normal approximation, which cor.test
# does not. The test has low power with fewer than 11 studies, as the report notes.
cat("Bias indicators\n")
deviation <- (z - z_fixed) / sqrt(v - 1 / sum(w))
bm <- cor.test(deviation, v, method = "kendall")
cat("Begg-Mazumdar: Kendall's tau =", six(bm$estimate), " ", pv(bm$p.value),
    if (k < 11) "(low power)\n" else "\n")

# Egger: each standardised effect (z / se) regressed on its precision (1 / se); the
# bias is the intercept, with a t test on k - 2 degrees of freedom and a 90% interval
egger <- lm(I(z / sqrt(v)) ~ I(1 / sqrt(v)))
egger_ci <- confint(egger, level = 0.9)[1, ]
cat("Egger: bias = ", six(coef(egger)[1]), " (90% CI = ", six(egger_ci[1]), " to ",
    six(egger_ci[2]), ")  ", pv(summary(egger)$coefficients[1, 4]), "\n", sep = "")

# Schmidt-Hunter: the mean correlation weighted by sample size, the variance of the
# correlations about it, the part of that variance expected from sampling error alone
# and the remainder, attributed to real differences between the populations studied
wmc <- sum(n * r) / sum(n)
var_r <- sum(n * (r - wmc)^2) / sum(n)
var_e <- (1 - wmc^2)^2 / (mean(n) - 1)
var_p <- max(0, var_r - var_e)
se_wmc <- sqrt(var_r / k)
sh <- wmc + c(0, -1, 1) * cit * se_wmc
cat("Schmidt-Hunter\n")
cat("Weighted mean correlation (95% CI): ", six(sh[1]), " (", six(sh[2]), " to ",
    six(sh[3]), ")\n", sep = "")
cat("Z (test correlation differs from 0) =", six(wmc / se_wmc),
    pv(2 * pnorm(-abs(wmc / se_wmc))), "\n")
cat("Observed variance across studies:", six(var_r), "\n")
cat("Variance due to sampling error:", six(var_e), "\n")
cat("Variance in the population correlations:", six(var_p), "\n")
# the credibility interval spreads the population correlations about the mean by
# their standard deviation, the square root of var_p (the report leaves it out when
# that is zero)
cat("95% Credibility interval for weighted mean correlation:",
    six(wmc - cit * sqrt(var_p)), "to", six(wmc + cit * sqrt(var_p)), "\n")
cat("Indicators of homogeneity/heterogeneity:\n")
cat("1. Residual standard deviation (should be smaller than 1/4 WMC: ", six(wmc / 4),
    "): ", six(sqrt(var_p)), "\n", sep = "")
cat("2. Percent of observed variance accounted for by sampling error",
    "(should be at least 75%):", six(100 * var_e / var_r), "\n")
het <- k * var_r / var_e
cat("3. Chi-square test of heterogeneity:", six(het),
    pv(pchisq(het, df, lower.tail = FALSE)), "\n")

# The report's charts: the bias assessment (funnel) plot of each study's Fisher z
# against its standard error, the most precise studies at the top, with the fixed
# effects pooled z and the limits within which 95% of studies of each precision would
# fall; then a forest plot for each of the three pooled correlations, the studies'
# squares scaled by the square root of their fixed effects weights
se <- sqrt(v)
top <- max(se) * 1.05
plot(z, se, ylim = c(top, 0), xlim = z_fixed + c(-1, 1) * cit * top,
     xlab = "Fisher Z(Correlation)", ylab = "Standard error",
     main = "Bias assessment plot", pch = 16)
abline(v = z_fixed)
lines(z_fixed + cit * c(0, top), c(0, top))
lines(z_fixed - cit * c(0, top), c(0, top))
forest <- function(pooled, title) {
  y <- rev(seq_len(k + 1))   # study 1 at the top, the pooled correlation at the foot
  par(mar = c(5, 8, 4, 2))
  # the axis runs from -1 when a study or limit is negative, as the program draws it
  left <- if (min(lower, pooled[2]) < 0) -1 else 0
  right <- if (max(upper, pooled[3]) <= 0) 0 else 1
  plot(c(r, pooled[1]), y, xlim = c(left, right), yaxt = "n", ylab = "",
       xlab = "Correlation (95% confidence interval)", main = title,
       pch = c(rep(15, k), 18), cex = c(2 * sqrt(w / max(w)), 3))
  segments(c(lower, pooled[2]), y, c(upper, pooled[3]), y)
  abline(v = pooled[1], lty = 2)
  axis(2, at = y, labels = c(paste("study", 1:k), "combined"), las = 1, tick = FALSE)
}
forest(fixed, "Correlation (Hedges-Olkin fixed effects) meta-analysis plot")
forest(random, "Correlation (Hedges-Olkin random effects) meta-analysis plot")
forest(sh, "Correlation (Schmidt-Hunter) meta-analysis plot")
