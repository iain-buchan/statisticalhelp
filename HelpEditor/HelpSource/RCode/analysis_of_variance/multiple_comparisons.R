# Multiple comparisons after one way analysis of variance: an illustration with
# the Substance 1 to 4 columns of the StatsDirect test workbook (ANOVA worksheet)
s1 <- c(29, 28, 23, 26, 26, 19, 25, 29, 26, 28)
s2 <- c(17, 25, 24, 19, 28, 21, 20, 25, 19, 24)
s3 <- c(17, 16, 21, 22, 23, 18, 20, 17, 25, 21)
s4 <- c(18, 20, 25, 24, 16, 20, 20, 17, 19, 17)
y <- c(s1, s2, s3, s4)
group <- factor(rep(paste("Substance", 1:4), each = 10))
fit <- aov(y ~ group)
print(summary(fit))

# Tukey: R's TukeyHSD gives every pairwise difference with its simultaneous
# confidence interval and P value, the figures in StatsDirect's table, but with
# the opposite sign (R subtracts the first level from the second, so the limits
# are swapped too) and in a different order
print(TukeyHSD(fit))

# The pieces of the table, calculated. The groups are of equal size here; the
# Tukey-Kramer form for unequal sizes replaces 1 / n by (1 / ni + 1 / nj) / 2
# in the standard error
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}
a <- anova(fit)
mse <- a$"Mean Sq"[2]                      # the pooled variance
nu <- a$Df[2]                              # its degrees of freedom, 36
k <- nlevels(group)
n <- 10
means <- tapply(y, group, mean)
pairs <- combn(k, 2)                       # every pair i < j, 6 of them
delta <- means[pairs[1, ]] - means[pairs[2, ]]
ord <- order(-abs(delta))                  # the report's order
label <- paste(names(means)[pairs[1, ]], "vs.", names(means)[pairs[2, ]])
q <- qtukey(0.95, k, nu)
cat("Tukey: Critical value (Studentized range) =", six(q), " |q*| =", six(q / sqrt(2)),
    "\n")
cat("Pooled standard deviation =", six(sqrt(mse)), "\n")
se <- sqrt(mse / n)                        # the difference divided by this is the
for (i in ord) {                           # Studentized range statistic
  cat(label[i], six(delta[i]), six(delta[i] - q * se), "to", six(delta[i] + q * se),
      six(abs(delta[i]) / se),
      pv(ptukey(abs(delta[i]) / se, k, nu, lower.tail = FALSE)), "\n")
}

# Scheffe: the critical value is the square root of (k - 1) times the F
# quantile, applied to the standard error of a difference between two means
crit <- sqrt((k - 1) * qf(0.95, k - 1, nu))
cat("Scheffe: Critical value =", six(crit), "\n")
sed <- sqrt(mse * (2 / n))
for (i in ord) {
  tstat <- abs(delta[i]) / sed
  cat(label[i], six(delta[i]), six(delta[i] - crit * sed), "to",
      six(delta[i] + crit * sed), six(tstat),
      pv(pf(tstat^2 / (k - 1), k - 1, nu, lower.tail = FALSE)), "\n")
}

# Newman-Keuls: the same statistic as Tukey's, but referred to the Studentized
# range for the number of ordered means that the two groups span (separation)
rnk <- rank(means)
for (i in ord) {
  span <- abs(rnk[pairs[1, i]] - rnk[pairs[2, i]]) + 1
  cat(label[i], six(delta[i]), span, six(abs(delta[i]) / se),
      pv(ptukey(abs(delta[i]) / se, span, nu, lower.tail = FALSE)), "\n")
}

# Bonferroni: an ordinary t test of one planned comparison (Substance 1 with
# Substance 4) using the pooled variance, judged against a critical P of 1 minus
# the confidence level (0.05 here) divided by the number of comparisons; the
# simultaneous interval uses the same division. R's pairwise.t.test gives the P
# values so multiplied instead.
d14 <- means[1] - means[4]
tstat <- d14 / sed
comparisons <- 6
cat("Bonferroni: Mean A - Mean B =", six(d14), " Estimated std. error =", six(sed),
    "\n")
cat("95% confidence interval =", six(d14 - qt(0.975, nu) * sed), "to",
    six(d14 + qt(0.975, nu) * sed), "\n")
alpha <- 0.05 / comparisons
cat("Bonferroni-adjusted (simultaneous) 95% confidence interval for", comparisons,
    "comparisons =", six(d14 - qt(1 - alpha / 2, nu) * sed), "to",
    six(d14 + qt(1 - alpha / 2, nu) * sed),
    paste0("(each comparison at ", formatC(100 * (1 - alpha), digits = 2, format = "f"),
           "%)"), "\n")
cat("t =", six(tstat), " df =", nu, " ", pv(2 * pt(-abs(tstat), nu)), "\n")
cat("Bonferroni critical P for", comparisons, "comparisons =", six(alpha), "\n")
print(pairwise.t.test(y, group, p.adjust.method = "bonferroni"))

# Dunnett (each substance against Substance 1 as the control): base R has no
# Dunnett test (the multcomp package has one). Its critical value is the 95%
# point of the largest absolute value of the three t statistics, which share
# the control mean and so are correlated; with equal group sizes that
# probability is a double integral over the standard normal and the residual
# standard deviation, evaluated here numerically
m <- k - 1
chi <- function(u) exp(nu / 2 * log(nu) + (nu - 1) * log(u) - nu * u^2 / 2 -
                       (nu / 2 - 1) * log(2) - lgamma(nu / 2))   # density of s / sigma
inner <- function(u, d) sapply(u, function(ui) integrate(function(z) {
  dnorm(z) * (pnorm(z + sqrt(2) * d * ui) - pnorm(z - sqrt(2) * d * ui))^m
}, -Inf, Inf, rel.tol = 1e-10)$value)
pmax_abs_t <- function(d) integrate(function(u) chi(u) * inner(u, d), 0, Inf,
                                    rel.tol = 1e-9)$value
d <- uniroot(function(x) pmax_abs_t(x) - 0.95, c(1, 5), tol = 1e-9)$root
cat("Dunnett: Critical value (|d|) =", six(d), "\n")
for (j in c(4, 3, 2)) {
  dj <- means[j] - means[1]
  cat(names(means)[j], six(dj), six(dj - d * sed), "to", six(dj + d * sed),
      pv(1 - pmax_abs_t(abs(dj) / sed)), "\n")
}
