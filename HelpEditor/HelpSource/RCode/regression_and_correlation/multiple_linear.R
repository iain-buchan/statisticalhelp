# Multiple linear regression: the StatsDirect help example (Armitage and Berry 1994,
# recovery time after a hypotensive drug, 53 patients) in R, then the NIST Longley
# validation data
yy <- c(7, 10, 18, 4, 10, 13, 21, 12, 9, 65, 20, 31, 23, 22, 13, 9, 50, 12, 11, 8, 26,
        16, 23, 7, 11, 8, 14, 39, 28, 12, 60, 10, 60, 22, 21, 14, 4, 27, 26, 28, 15, 8,
        46, 24, 12, 25, 45, 72, 25, 28, 10, 25, 44)
x1 <- c(2.26, 1.81, 1.78, 1.54, 2.06, 1.74, 2.56, 2.29, 1.80, 2.32, 2.04, 1.88, 1.18,
        2.08, 1.70, 1.74, 1.90, 1.79, 2.11, 1.72, 1.74, 1.60, 2.15, 2.26, 1.65, 1.63,
        2.40, 2.70, 1.90, 2.78, 2.27, 1.74, 2.62, 1.80, 1.81, 1.58, 2.41, 1.65, 2.24,
        1.70, 2.45, 1.72, 2.37, 2.23, 1.92, 1.99, 1.99, 2.35, 1.80, 2.36, 1.59, 2.10,
        1.80)
x2 <- c(66, 52, 72, 67, 69, 71, 88, 68, 59, 73, 68, 58, 61, 68, 69, 55, 67, 67, 68, 59,
        68, 63, 65, 72, 58, 69, 70, 73, 56, 83, 67, 84, 68, 64, 60, 62, 76, 60, 60, 59,
        84, 66, 68, 65, 69, 72, 63, 56, 70, 69, 60, 51, 61)

# R's standard regression: coefficients, t tests, the F test, R squared and the
# adjusted R squared (Ra squared); its residual standard error is the root MSE
fit <- lm(yy ~ x1 + x2)
print(summary(fit))
print(anova(fit))

# The report's lines to 6 places. The partial correlation r of each predictor is
# its t over root(t squared + residual degrees of freedom); the Durbin-Watson
# statistic is the sum of squared successive residual differences over the
# residual sum of squares
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}
s <- summary(fit)$coefficients
df <- fit$df.residual
for (i in 1:3) {
  cat(rownames(s)[i], " b", i - 1, " = ", six(s[i, 1]),
      if (i > 1) paste("  r =", six(s[i, 3] / sqrt(s[i, 3]^2 + df))) else "",
      "  t = ", six(s[i, 3]), "  ", pv(s[i, 4]), "\n", sep = "")
}
a <- anova(fit)
ssreg <- sum(a$"Sum Sq"[1:2])
ssres <- a$"Sum Sq"[3]
cat("Regression", six(ssreg), 2, six(ssreg / 2), "\n")
cat("Residual", six(ssres), df, six(ssres / df), "\n")
cat("Total (corrected)", six(ssreg + ssres), df + 2, "\n")
cat("yy =", six(s[1, 1]), paste0("+", six(s[2, 1])), "x1", six(s[3, 1]), "x2\n")
cat("Root MSE =", six(sqrt(ssres / df)), "\n")
f <- summary(fit)$fstatistic
cat("F =", six(f[1]), " ", pv(pf(f[1], f[2], f[3], lower.tail = FALSE)), "\n")
cat("Multiple correlation coefficient (R) =", six(sqrt(summary(fit)$r.squared)), "\n")
cat("R2 =", paste0(six(100 * summary(fit)$r.squared), "%"), "\n")
cat("Ra2 =", paste0(six(100 * summary(fit)$adj.r.squared), "%"), "\n")
e <- residuals(fit)
cat("Durbin-Watson test statistic =", six(sum(diff(e)^2) / sum(e^2)), "\n")

# The technical validation: NIST's Longley data, certified to 12 places
y <- c(60323, 61122, 60171, 61187, 63221, 63639, 64989, 63761, 66019, 67857, 68169,
       66513, 68655, 69564, 69331, 70551)
lx1 <- c(83, 88.5, 88.2, 89.5, 96.2, 98.1, 99, 100, 101.2, 104.6, 108.4, 110.8, 112.6,
         114.2, 115.7, 116.9)
lx2 <- c(234289, 259426, 258054, 284599, 328975, 346999, 365385, 363112, 397469, 419180,
         442769, 444546, 482704, 502601, 518173, 554894)
lx3 <- c(2356, 2325, 3682, 3351, 2099, 1932, 1870, 3578, 2904, 2822, 2936, 4681, 3813,
         3931, 4806, 4007)
lx4 <- c(1590, 1456, 1616, 1650, 3099, 3594, 3547, 3350, 3048, 2857, 2798, 2637, 2552,
         2514, 2572, 2827)
lx5 <- c(107608, 108632, 109773, 110929, 112075, 113270, 115094, 116219, 117388, 118734,
         120445, 121950, 123366, 125368, 127852, 130081)
lx6 <- 1947:1962
longley <- lm(y ~ lx1 + lx2 + lx3 + lx4 + lx5 + lx6)
# 12 places, within the 15 significant figures a double holds, as the report prints
# (R and StatsDirect agree to the 11th place of b1 and to 12 places elsewhere; the
# certified value of b1 is 15.0618722713733)
twelve <- function(x) {
  places <- min(12, 15 - ceiling(log10(abs(x))))
  formatC(x, digits = places, format = "f", drop0trailing = TRUE)
}
s <- summary(longley)$coefficients
ldf <- longley$df.residual
for (i in 1:7) {
  partial <- ""
  if (i > 1) partial <- paste0("  r = ", twelve(s[i, 3] / sqrt(s[i, 3]^2 + ldf)))
  cat(c("Intercept", paste0("x", 1:6))[i], " b", i - 1, " = ", twelve(s[i, 1]), partial,
      "  t = ", twelve(s[i, 3]), "  ", pv(s[i, 4]), "\n", sep = "")
}
terms <- paste0(ifelse(s[2:7, 1] >= 0, "+", ""), sapply(s[2:7, 1], twelve), " x", 1:6)
cat("y =", twelve(s[1, 1]), paste(terms, collapse = " "), "\n")
