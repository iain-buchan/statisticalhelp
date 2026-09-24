# Proportion meta-analysis: the StatsDirect help example (adherence with medication in
# 22 fictitious trials; the test workbook's Meta-analysis worksheet columns Trial,
# Adherent and Total) in R
trial <- c("Brown", "Lamont", "Lally", "Orwell", "Wagner", "Werner", "Venner", "Adams",
           "Brenner", "Borrowdale", "Byers", "Daniels", "Darling", "Ehert", "Fern",
           "Mullen", "Orton", "Jones", "Ning", "Sherraton", "Zu", "Tarone")
adherent <- c(214, 58, 59, 182, 65, 66, 99, 600, 45, 165, 32, 49, 175, 155, 64, 526,
              104, 97, 2310, 72, 37, 31)
total <- c(311, 65, 67, 285, 73, 116, 183, 696, 57, 277, 35, 60, 199, 311, 81, 537,
           107, 102, 4612, 91, 37, 87)

# Base R has no meta-analysis function, so the pooled proportions are computed as the
# formulae above give them: each proportion is transformed by the Freeman-Tukey double
# arcsine, pooled with inverse variance weights (n + 0.5), and the pooled transform is
# taken back to a proportion by the Stuart-Ord inverse, sin(x/2)^2
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}
k <- length(total)
z <- qnorm(0.975)
p <- adherent / total

# Each study's proportion with its exact (Clopper-Pearson) 95% confidence interval, as
# binom.test() gives it; when every subject responds, as in the Zu trial, the interval
# is one sided and its lower limit is the 97.5% limit
ci <- t(sapply(seq_len(k), function(i) binom.test(adherent[i], total[i])$conf.int))
one_sided <- ifelse(adherent == 0 | adherent == total, "[97.5% one-sided CI]", "")
cat("Method: Stuart-Ord (inverse double arcsine square root)\n")
print(data.frame(Study = seq_len(k), Proportion = six(p), Lower = six(ci[, 1]),
                 Upper = six(ci[, 2]), Trial = paste(trial, one_sided)),
      row.names = FALSE, right = FALSE)

# The transformed proportions, their variances and the fixed and random effects weights
x <- asin(sqrt(adherent / (total + 1))) + asin(sqrt((adherent + 1) / (total + 1)))
v <- 1 / (total + 0.5)
w <- 1 / v
xf <- sum(w * x) / sum(w)
se_f <- 1 / sqrt(sum(w))
q <- sum(w * (x - xf)^2)
tau2 <- max(0, (q - (k - 1)) / (sum(w) - sum(w^2) / sum(w)))
wr <- 1 / (tau2 + v)
xr <- sum(wr * x) / sum(wr)
se_r <- 1 / sqrt(sum(wr))
print(data.frame(Study = seq_len(k), Transform = six(x), Variance = six(v),
                 Fixed = six(100 * w / sum(w)), Random = six(100 * wr / sum(wr)),
                 Trial = paste(trial, one_sided)), row.names = FALSE, right = FALSE)

inv <- function(t) sin(t / 2)^2
cat("Fixed effects (inverse variance)\n")
cat("Pooled proportion = ", six(inv(xf)), " (95% CI = ", six(inv(xf - z * se_f)),
    " to ", six(inv(xf + z * se_f)), ")\n", sep = "")

# Cochran's Q against chi-square on k - 1 df, the DerSimonian-Laird estimate of the
# between-studies variance, and I-squared, which is lambda / (df + lambda) for the
# noncentrality parameter lambda = Q - df, with the confidence interval that StatsDirect
# gives by default (the noncentral chi-square method of Hedges and Pigott): the lambda
# at which the observed Q is the 97.5% point of the noncentral chi-square distribution
# on df degrees of freedom, and the lambda at which it is the 2.5% point, each turned
# into an I-squared; a limit is 0 when the central distribution already puts Q below
# that point
cat("Non-combinability of studies\n")
cat("Cochran Q = ", six(q), "  (df = ", k - 1, ")  ",
    pv(pchisq(q, k - 1, lower.tail = FALSE)), "\n", sep = "")
cat("Moment-based estimate of between studies variance =", six(tau2), "\n")
df <- k - 1
i2 <- function(lambda) 100 * lambda / (df + lambda)
lambda_at <- function(prob) {
  if (pchisq(q, df) < prob) return(0)
  uniroot(function(l) pchisq(q, df, ncp = l) - prob, c(0, 10 * (q + df)),
          tol = 1e-10)$root
}
one <- function(x) formatC(x, digits = 1, format = "f")
cat("I2 (inconsistency) = ", one(i2(max(0, q - df))), "% (95% CI = ",
    one(i2(lambda_at(0.975))), "% to ", one(i2(lambda_at(0.025))), "%)\n", sep = "")

cat("Random effects (DerSimonian-Laird)\n")
cat("Pooled proportion = ", six(inv(xr)), " (95% CI = ", six(inv(xr - z * se_r)),
    " to ", six(inv(xr + z * se_r)), ")\n", sep = "")

# Bias indicators, all on the proportion scale with each study's standard error taken
# from its exact confidence interval. Begg-Mazumdar: Kendall's tau between the
# standardised deviates from the pooled proportion and the variances, with the exact P
# that cor.test() gives when there are no ties. Egger: the intercept of the regression
# of the standardised proportion on precision, with a 90% interval and its t test
cat("Bias indicators\n")
se <- (ci[, 2] - ci[, 1]) / 2 / z
wx <- 1 / se^2
ts <- (p - sum(p * wx) / sum(wx)) / sqrt(se^2 - 1 / sum(wx))
begg <- cor.test(ts, se^2, method = "kendall")
cat("Begg-Mazumdar: Kendall's tau =", six(begg$estimate), " ", pv(begg$p.value), "\n")
egger <- lm(I(p / se) ~ I(1 / se))
cat("Egger: bias = ", six(coef(egger)[1]), " (90% CI = ",
    six(confint(egger, level = 0.9)[1, 1]), " to ",
    six(confint(egger, level = 0.9)[1, 2]), ")  ",
    pv(summary(egger)$coefficients[1, 4]), "\n", sep = "")

# Harbord's test as StatsDirect adapts it to proportions: each study's score is the
# excess of responders over the number expected at the pooled fixed effects proportion,
# with the binomial variance, and the bias is the intercept of the regression of
# score / sqrt(variance) on sqrt(variance)
pf <- inv(xf)
score <- adherent - total * pf
vs <- total * pf * (1 - pf)
harbord <- lm(I(score / sqrt(vs)) ~ sqrt(vs))
cat("Harbord: bias = ", six(coef(harbord)[1]), " (90% CI = ",
    six(confint(harbord, level = 0.9)[1, 1]), " to ",
    six(confint(harbord, level = 0.9)[1, 2]), ")  ",
    pv(summary(harbord)$coefficients[1, 4]), "\n", sep = "")

# The report's charts: a funnel plot of each proportion against its standard error,
# with the pooled fixed effects proportion and its 95% limits, then a forest plot for
# each model with the studies in order from the top and the pooled estimate at the foot
plot(p, se, ylim = rev(c(0, max(se))), xlim = c(0, 1), xlab = "Proportion",
     ylab = "Standard error", main = "Bias assessment plot")
abline(v = pf)
s <- seq(0, max(se), length.out = 50)
lines(pf - z * s, s, lty = 2)
lines(pf + z * s, s, lty = 2)
forest <- function(est, lower, upper, weight, title) {
  rows <- c(trial, "combined")
  y <- rev(seq_along(rows))
  old <- par(mar = c(5, 7, 4, 10))
  plot(est, y, type = "n", xlim = c(0, 1), yaxt = "n", ylab = "",
       xlab = "proportion (95% confidence interval)", main = title)
  segments(lower, y, upper, y)
  points(est[1:k], y[1:k], pch = 15, col = "grey", cex = 3 * sqrt(weight / max(weight)))
  points(est[1:k], y[1:k], pch = 20)
  points(est[k + 1], y[k + 1], pch = 18, cex = 2)
  abline(v = est[k + 1], lty = 3)
  axis(2, at = y, labels = rows, las = 1, tick = FALSE, cex.axis = 0.7)
  axis(4, at = y, tick = FALSE, las = 1, cex.axis = 0.7,
       labels = sprintf("%.2f (%.2f, %.2f)", est, lower, upper))
  par(old)
}
forest(c(p, inv(xf)), c(ci[, 1], inv(xf - z * se_f)), c(ci[, 2], inv(xf + z * se_f)), w,
       "Proportion meta-analysis plot [fixed effects]")
forest(c(p, inv(xr)), c(ci[, 1], inv(xr - z * se_r)), c(ci[, 2], inv(xr + z * se_r)),
       wr, "Proportion meta-analysis plot [random effects]")

# The Miller inverse, the other choice of back-transform in StatsDirect: the exact
# inverse of the double arcsine at n = the harmonic mean of the study sizes
hm <- k / sum(1 / total)
ft <- function(r, n) asin(sqrt(r / (n + 1))) + asin(sqrt((r + 1) / (n + 1)))
miller <- function(t) {
  # beyond the range of the transform at n = hm the exact inverse folds back, so
  # StatsDirect returns 0 or 1 there
  if (t > ft(hm, hm)) return(1)
  if (t < ft(0, hm)) return(0)
  0.5 * (1 - sign(cos(t)) * sqrt(1 - (sin(t) + (sin(t) - 1 / sin(t)) / hm)^2))
}
cat("Method: Miller (exact inverse Freeman-Tukey double arcsine)\n")
cat("Fixed effects pooled proportion = ", six(miller(xf)), " (95% CI = ",
    six(miller(xf - z * se_f)), " to ", six(miller(xf + z * se_f)), ")\n", sep = "")
cat("Random effects pooled proportion = ", six(miller(xr)), " (95% CI = ",
    six(miller(xr - z * se_r)), " to ", six(miller(xr + z * se_r)), ")\n", sep = "")
# Harbord's score is taken from the Miller pooled proportion here, so the test moves
pf <- miller(xf)
score <- adherent - total * pf
vs <- total * pf * (1 - pf)
harbord <- lm(I(score / sqrt(vs)) ~ sqrt(vs))
cat("Harbord: bias = ", six(coef(harbord)[1]), " (90% CI = ",
    six(confint(harbord, level = 0.9)[1, 1]), " to ",
    six(confint(harbord, level = 0.9)[1, 2]), ")  ",
    pv(summary(harbord)$coefficients[1, 4]), "\n", sep = "")
