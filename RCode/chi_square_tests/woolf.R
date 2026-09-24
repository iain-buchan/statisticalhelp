# Woolf chi-square analysis of 2 by 2 series: the StatsDirect help example (Armitage
# and Berry 1994, p. 516, smoking among lung cancer patients and controls in ten
# studies; the test workbook's Meta worksheet columns Smokers total, Smokers cancer,
# Control total and Control cancer) in R
# a = Smokers cancer, b = Control cancer, c = Smokers total - a, d = Control total - b
a <- c(83, 90, 129, 412, 1350, 60, 459, 499, 451, 260)     # lung cancer, smoker
b <- c(3, 3, 7, 32, 7, 3, 18, 19, 39, 5)                   # lung cancer, non-smoker
c <- c(72, 227, 81, 299, 1296, 106, 534, 462, 1729, 259)   # control, smoker
d <- c(14, 43, 19, 131, 61, 27, 81, 56, 636, 28)           # control, non-smoker
k <- length(a)

# Base R has no Woolf function, so the statistics are computed by Woolf's formulae: each
# table's log odds ratio is weighted by the reciprocal of its variance, the weighted
# mean is the pooled log odds ratio, and the weighted sum of squares about that mean
# is the chi-square for heterogeneity between the tables, as Woolf proposed
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}
z <- qnorm(0.975)
woolf <- function(lor, v) {
  w <- 1 / v
  mean_lor <- sum(w * lor) / sum(w)
  se <- sqrt(1 / sum(w))
  ci <- mean_lor + c(-1, 1) * z * se
  chi2 <- (mean_lor / se)^2
  het <- sum(w * lor^2) - sum(w * lor)^2 / sum(w)
  cat("Number of tables =", k, "\n")
  cat("Mean log odds ratio =", six(mean_lor), "giving odds ratio =",
      six(exp(mean_lor)), "\n")
  cat("Variance of mean log odds ratio =", six(se^2), " standard error =", six(se),
      "\n")
  cat("Approximate 95% CI for mean log odds ratio =", six(ci[1]), "to", six(ci[2]),
      "\n")
  cat("Giving odds ratio of", six(exp(ci[1])), "to", six(exp(ci[2])), "\n")
  cat("Chi-square for expected log odds ratio = 0 is", six(chi2), "Chi =",
      six(mean_lor / se), pv(pchisq(chi2, 1, lower.tail = FALSE)), "\n")
  cat("Chi-square for heterogeneity =", six(het), "DF =", k - 1,
      pv(pchisq(het, k - 1, lower.tail = FALSE)), "\n")
}

# Without the Haldane correction: log(ad/bc) with variance 1/a + 1/b + 1/c + 1/d,
# which needs every cell of every table to be greater than zero
cat("For combined tables without Haldane correction:\n")
woolf(log(a * d / (b * c)), 1 / a + 1 / b + 1 / c + 1 / d)

# With the Haldane correction: 0.5 is added to each cell for the log odds ratio, and
# its variance is 1/(a+1) + 1/(b+1) + 1/(c+1) + 1/(d+1)
cat("For combined tables with Haldane correction:\n")
woolf(log((a + 0.5) * (d + 0.5) / ((b + 0.5) * (c + 0.5))),
      1 / (a + 1) + 1 / (b + 1) + 1 / (c + 1) + 1 / (d + 1))

# The Mantel-Haenszel pooled odds ratio that the text compares with, from R's standard
# test, without its continuity correction
tables <- array(rbind(a, c, b, d), c(2, 2, k))   # each table filled column by column
print(mantelhaen.test(tables, correct = FALSE))
