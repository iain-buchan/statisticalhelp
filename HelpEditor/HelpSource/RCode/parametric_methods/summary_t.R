# t tests from summary statistics: the StatsDirect help example (the single sample
# and unpaired t test examples again, from their means and standard deviations) in R
# R's t.test needs the data themselves, so the report's lines are calculated here.
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
power <- function(t, df) {                # two sided test at the 5% level, from the
  tc <- qt(0.975, df)                     # noncentral t distribution with the t
  pt(-tc, df, ncp = t) + pt(tc, df, ncp = t, lower.tail = FALSE)  # observed as ncp
}

# Single sample: 20 systolic blood pressures with mean 130.05 and sd 9.960316,
# against a population mean of 120
n <- 20
m <- 130.05
s <- 9.960316
mu <- 120
se <- s / sqrt(n)
t <- (m - mu) / se
p <- 2 * pt(-abs(t), n - 1)
cat("Single sample: mean =", m, " n =", n, " sd =", s, " population mean =", mu,
    "\n")
ci <- m - mu + c(-1, 1) * qt(0.975, n - 1) * se
cat("95% confidence interval for mean difference =", six(ci[1]), "to", six(ci[2]), "\n")
cat("df =", n - 1, "  t =", six(t), "\n")
cat(sprintf("One sided P = %.4f   Two sided P = %.4f\n", p / 2, p))
cat(sprintf("Power (for 5%% significance) = %.2f%%\n", 100 * power(t, n - 1)))

# Unpaired: 12 rats on a high protein diet, mean 120 and sd 21.38819, and 7 on a
# low protein diet, mean 101 and sd 20.62361
n1 <- 12
m1 <- 120
s1 <- 21.38819
n2 <- 7
m2 <- 101
s2 <- 20.62361
delta <- m1 - m2
report <- function(se, df, label, name) {
  t <- delta / se
  p <- 2 * pt(-abs(t), df)
  cat(label, "\n")
  cat("Combined standard error =", six(se), "  df =", six(df), " ", name, "=", six(t),
      "\n")
  cat(sprintf("One sided P = %.4f   Two sided P = %.4f\n", p / 2, p))
  cat("95% confidence interval for difference between means =",
      six(delta - qt(0.975, df) * se), "to", six(delta + qt(0.975, df) * se), "\n")
  cat(sprintf("Power (for 5%% significance) = %.2f%%\n", 100 * power(t, df)))
}
# Assuming equal variances: the pooled variance
pooled <- ((n1 - 1) * s1^2 + (n2 - 1) * s2^2) / (n1 + n2 - 2)
report(sqrt(pooled * (1 / n1 + 1 / n2)), n1 + n2 - 2, "Assuming equal variances", "t")
# Assuming unequal variances: Welch's standard error and degrees of freedom
se2 <- sqrt(s1^2 / n1 + s2^2 / n2)
df2 <- se2^4 / ((s1^2 / n1)^2 / (n1 - 1) + (s2^2 / n2)^2 / (n2 - 1))
report(se2, df2, "Assuming unequal variances", "t(d)")
# The F test of the variances, larger over smaller, with its upper tail P doubled
f <- max(s1, s2)^2 / min(s1, s2)^2
pf2 <- 2 * pf(f, if (s1 > s2) n1 - 1 else n2 - 1, if (s1 > s2) n2 - 1 else n1 - 1,
              lower.tail = FALSE)
cat("Two sided F test is", if (pf2 < 0.05) "significant" else "not significant", "\n")
