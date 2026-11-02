# Polynomial regression: the StatsDirect help example (McClave and Dietrich 1991,
# electricity consumption and home size, degree 2) in R
size <- c(1290, 1350, 1470, 1600, 1710, 1840, 1980, 2230, 2400, 2930)
kwh <- c(1182, 1172, 1264, 1493, 1571, 1711, 1804, 1840, 1956, 1954)

# R's standard regression on the size and its square (poly with raw = TRUE keeps
# the ordinary powers, so the coefficients are the report's b0, b1 and b2)
fit <- lm(kwh ~ poly(size, 2, raw = TRUE))
print(summary(fit))

# The report's lines to 6 places, as for multiple linear regression
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}
s <- summary(fit)$coefficients
df <- fit$df.residual
lab <- c("Intercept", "Home Size", "Home Size^2")
for (i in 1:3) {
  cat(lab[i], " b", i - 1, "= ", six(s[i, 1]),
      if (i > 1) paste("  r =", six(s[i, 3] / sqrt(s[i, 3]^2 + df))) else "",
      "  t = ", six(s[i, 3]), "  ", pv(s[i, 4]), "\n", sep = "")
}
cat("KW Hrs/Mnth =", six(s[1, 1]), paste0("+", six(s[2, 1])), "Home Size", six(s[3, 1]),
    "Home Size^2\n")
a <- anova(fit)
ssreg <- a$"Sum Sq"[1]
ssres <- a$"Sum Sq"[2]
cat("Regression", six(ssreg), 2, six(ssreg / 2), "\n")
cat("Residual", six(ssres), df, six(ssres / df), "\n")
cat("Total (corrected)", six(ssreg + ssres), df + 2, "\n")
cat("Root MSE =", six(sqrt(ssres / df)), "\n")
f <- summary(fit)$fstatistic
cat("F =", six(f[1]), " ", pv(pf(f[1], f[2], f[3], lower.tail = FALSE)), "\n")
cat("Multiple correlation coefficient (R) =", six(sqrt(summary(fit)$r.squared)), "\n")
cat("R2 =", paste0(six(100 * summary(fit)$r.squared), "%"), "\n")
cat("Ra2 =", paste0(six(100 * summary(fit)$adj.r.squared), "%"), "\n")
e <- residuals(fit)
cat("Durbin-Watson test statistic =", six(sum(diff(e)^2) / sum(e^2)), "\n")

# Area under the fitted curve between the smallest and largest home size, by
# integrating the polynomial, and the area under the data by the trapezoidal rule
b <- coef(fit)
curve_area <- integrate(function(x) b[1] + b[2] * x + b[3] * x^2, min(size), max(size))
cat("AUC (polynomial function) =", six(curve_area$value), "\n")
trapezia <- sum(diff(size) * (head(kwh, -1) + tail(kwh, -1)) / 2)
cat("AUC (by trapezoidal rule) =", six(trapezia), "\n")
