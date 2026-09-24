# Peto odds ratio meta-analysis: the StatsDirect help example (Fleiss 1993, deaths after
# heart attack in seven trials of aspirin; the test workbook's Meta-analysis worksheet
# columns Exposed total, Exposed cases, Non-exposed total, Non-exposed cases and Study)
# in R
study <- c("MRC-1", "CDP", "MRC-2", "GASP", "PARIS", "AMIS", "ISIS-2")
aspirin <- c(615, 758, 832, 317, 810, 2267, 8587)
aspirin_deaths <- c(49, 44, 102, 32, 85, 246, 1570)
control <- c(624, 771, 850, 309, 406, 2257, 8600)
control_deaths <- c(67, 64, 126, 38, 52, 219, 1720)

# Base R has no meta-analysis function, so each study's fourfold table is worked as the
# formulae above give it: a and b are the deaths on aspirin and on control, cc and dd
# the survivors (not c and d, which would hide two of R's own functions), and n is the
# size of the study
a <- aspirin_deaths
b <- control_deaths
cc <- aspirin - aspirin_deaths
dd <- control - control_deaths
n <- a + b + cc + dd
k <- length(a)
print(data.frame(study, a, b, cc, dd))
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}

# O - E is the number of deaths on aspirin less the number expected if aspirin had no
# effect; V is the hypergeometric variance of that number, and it is also the weight of
# the study. The Peto odds ratio is exp((O - E) / V), with its interval from the normal
# distribution.
E <- (a + b) * (a + cc) / n
oe <- a - E
V <- (a + b) * (cc + dd) * (a + cc) * (b + dd) / (n^2 * (n - 1))
z <- qnorm(0.975)
or <- exp(oe / V)
lower <- exp((oe - z * sqrt(V)) / V)
upper <- exp((oe + z * sqrt(V)) / V)
cat("Stratum, O-E, odds ratio, 95% CI:\n")
for (i in 1:k) {
  cat(i, six(oe[i]), six(or[i]), six(lower[i]), six(upper[i]), study[i], "\n")
}

# The standardised effect is the log odds ratio, (O - E) / V, whose variance is 1 / V;
# the weights are the V as percentages of their total
cat("Stratum, standardised effect, variance, % weight:\n")
for (i in 1:k) {
  cat(i, six(log(or[i])), six(1 / V[i]), six(100 * V[i] / sum(V)), study[i], "\n")
}

# Each study's test of no effect: z = (O - E) / sqrt(V) against the normal distribution
zi <- oe / sqrt(V)
cat("Stratum, V, z, P (two sided):\n")
for (i in 1:k) {
  cat(i, six(V[i]), six(zi[i]), pv(2 * pnorm(-abs(zi[i]))), study[i], "\n")
}

# The pooled odds ratio (fixed effects): the sums of O - E and of V take the place of
# the single study's in the same formulae
sum_oe <- sum(oe)
sum_V <- sum(V)
pooled <- exp(sum_oe / sum_V)
pooled_lower <- exp((sum_oe - z * sqrt(sum_V)) / sum_V)
pooled_upper <- exp((sum_oe + z * sqrt(sum_V)) / sum_V)
z_pooled <- sum_oe / sqrt(sum_V)
cat("Pooled odds ratio = ", six(pooled), " (95% CI = ", six(pooled_lower), " to ",
    six(pooled_upper), ")\n", sep = "")
cat("Z (test of odds ratio differs from 1) =", six(z_pooled), " ",
    pv(2 * pnorm(-abs(z_pooled))), "\n")

# Non-combinability: Cochran's Q is the weighted sum of squared differences between the
# studies' log odds ratios and the pooled one, with the V as weights, referred to the
# chi-square distribution with k - 1 degrees of freedom. I-squared is 100 (Q - df) / Q,
# not below zero. Its interval is the non-central chi-square method of Hedges and
# Pigott (2001) that the report uses: Q is treated as a non-central chi-square variable
# on df degrees of freedom, and each limit is the non-centrality parameter at which the
# observed Q sits at the 97.5th (lower limit) or the 2.5th (upper limit) percentile,
# found with uniroot and pchisq; a limit is 0 when the central distribution already
# puts Q below that percentile. A non-centrality lambda gives
# I-squared = lambda / (df + lambda)
Q <- sum(V * (log(or) - log(pooled))^2)
dfq <- k - 1
cat("Cochran Q = ", six(Q), "  (df = ", dfq, ")  ",
    pv(pchisq(Q, dfq, lower.tail = FALSE)), "\n", sep = "")
one <- function(x) formatC(x, digits = 1, format = "f", drop0trailing = TRUE)
i2 <- max(0, 100 * (Q - dfq) / Q)
ncp_limit <- function(p) {
  if (pchisq(Q, dfq) <= p) return(0)
  # the bracket Q + 1000 is ample: the distribution function there is near 0
  uniroot(function(l) pchisq(Q, dfq, ncp = l) - p, c(0, Q + 1000), tol = 1e-10)$root
}
lambda <- c(ncp_limit(0.975), ncp_limit(0.025))
i2_ci <- 100 * lambda / (dfq + lambda)
cat("I2 (inconsistency) = ", one(i2), "% (95% CI = ", one(i2_ci[1]), "% to ",
    one(i2_ci[2]), "%)\n", sep = "")

# Bias indicators. Each study's standard error of the log odds ratio is 1 / sqrt(V).
# Begg and Mazumdar's test is Kendall's tau between the standardised deviates of the
# studies from the pooled log odds ratio and their variances; the exact P holds when
# there are no ties among either, as here. The report notes the test's low power with
# fewer than eleven studies.
se <- 1 / sqrt(V)
deviate <- (log(or) - log(pooled)) / sqrt(se^2 - 1 / sum_V)
begg <- cor.test(deviate, se^2, method = "kendall", exact = TRUE)
cat("Begg-Mazumdar: Kendall's tau =", six(begg$estimate), " ", pv(begg$p.value),
    "(low power)\n")

# Egger's test regresses each log odds ratio divided by its standard error on the
# precision, 1 / standard error: the bias is the intercept, tested with Student's t on
# k - 2 degrees of freedom and given a 90% interval, twice the alpha of the analysis
egger <- lm(I(log(or) / se) ~ I(1 / se))
egger_ci <- confint(egger, level = 0.9)
cat("Egger: bias = ", six(coef(egger)[1]), " (90% CI = ", six(egger_ci[1, 1]), " to ",
    six(egger_ci[1, 2]), ")  ", pv(summary(egger)$coefficients[1, 4]), "\n", sep = "")

# Harbord's modification regresses the score O - E divided by sqrt(V) on sqrt(V). For
# the Peto odds ratio that is the same regression as Egger's, since the log odds ratio
# over its standard error is (O - E) / sqrt(V) and the precision is sqrt(V), so the two
# tests agree unless a table with an empty cell needs a continuity correction
harbord <- lm(I(oe / sqrt(V)) ~ sqrt(V))
harbord_ci <- confint(harbord, level = 0.9)
cat("Harbord-Egger: bias = ", six(coef(harbord)[1]), " (90% CI = ",
    six(harbord_ci[1, 1]), " to ", six(harbord_ci[1, 2]), ")  ",
    pv(summary(harbord)$coefficients[1, 4]), "\n", sep = "")

# The report's charts. First the bias assessment (funnel) plot: each log odds ratio
# against its standard error, the axis reversed so that the 95% cone about the pooled
# log odds ratio opens downwards
se_top <- max(se) * 1.05
plot(log(or), se, ylim = c(se_top, 0), xlab = "Log(Peto odds ratio)",
     ylab = "Standard error", main = "Bias assessment plot",
     xlim = range(log(or), log(pooled) + c(-1, 1) * z * se_top))
abline(v = log(pooled))
lines(log(pooled) + c(-1, 0, 1) * z * se_top, c(se_top, 0, se_top))

# The L'Abbe plot: the percentage of deaths on aspirin against that on control, each
# study drawn with a symbol whose size grows with its sample size, the line of
# equality, and the dashed curve on which a study with the pooled odds ratio lies
# whatever its control rate
plot(100 * b / (b + dd), 100 * a / (a + cc), xlim = c(0, 100), ylim = c(0, 100),
     cex = 3 * sqrt(n / max(n)), xlab = "control percent",
     ylab = "experimental percent",
     main = "L'Abbe plot (symbol size represents sample size)")
abline(0, 1)
pc <- 0:100
lines(pc, 100 * pooled * pc / (100 - pc + pooled * pc), lty = 2)

# The Peto odds ratio (forest) plot on a log scale: each study's odds ratio with its 95%
# interval and a square whose size grows with its weight, the line of no effect, and
# the pooled odds ratio and interval at the foot
par(mar = c(5, 6, 4, 8))
plot(c(lower, pooled_lower), k:0, type = "n", log = "x", yaxt = "n",
     xlim = range(lower, upper), ylim = c(-0.5, k + 0.5), ylab = "",
     xlab = "Peto odds ratio (95% confidence interval)", main = "Peto odds ratio plot")
segments(lower, k:1, upper, k:1)
points(or, k:1, pch = 15, cex = 3 * sqrt(V / max(V)))
segments(pooled_lower, 0, pooled_upper, 0)
points(pooled, 0, pch = 18, cex = 2)
abline(v = 1)
axis(2, at = k:0, labels = c(study, "combined"), las = 1)
label <- sprintf("%.2f (%.2f, %.2f)", c(or, pooled), c(lower, pooled_lower),
                 c(upper, pooled_upper))
mtext(label, side = 4, at = k:0, las = 1, line = 1, cex = 0.8)
par(mar = c(5, 4, 4, 2) + 0.1)

# And the plot of O - E against V, both axes including the origin: the studies fall
# along the line through the origin with the slope of the pooled log odds ratio when
# all of them share one odds ratio
plot(V, oe, xlim = c(0, max(V)), ylim = range(0, oe), xlab = "Peto weights",
     ylab = "Observed-Expected", main = "Peto O-E vs. V plot")
abline(0, log(pooled))
