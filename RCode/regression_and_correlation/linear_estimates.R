# Linearized regression estimates: an illustration with the electricity
# consumption and home size data of the polynomial regression topic (McClave and
# Dietrich 1991, the test workbook's Home Size and KW Hrs/Mnth) in R
size <- c(1290, 1350, 1470, 1600, 1710, 1840, 1980, 2230, 2400, 2930)
kwh <- c(1182, 1172, 1264, 1493, 1571, 1711, 1804, 1840, 1956, 1954)

# Each model is a straight line after a transformation, fitted by ordinary least
# squares on the transformed scale; R's standard regression gives the line, its
# correlation coefficient and the standard error of the estimate (the residual
# standard error on the transformed scale)
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
report <- function(model, fit, a, b) {
  cat(model, "\n")
  cat("A =", six(a), "  B =", six(b), "\n")
  r <- sqrt(summary(fit)$r.squared)
  cat("Correlation coefficient (r) =", six(r), "  (r2 =", six(r^2), ")\n")
  cat("Standard error of estimate =", six(summary(fit)$sigma), "\n")
}

# Exponential, Y = a exp(b x): log Y against x, so A is exp of the intercept
fit <- lm(log(kwh) ~ size)
report("(Exponential)  Y = a * exp(b * x)", fit, exp(coef(fit)[1]), coef(fit)[2])

# Geometric (power), Y = a x^b: log Y against log x
fit <- lm(log(kwh) ~ log(size))
report("(Geometric / Power)  Y = a * x^b", fit, exp(coef(fit)[1]), coef(fit)[2])

# Hyperbolic, Y = x / (a + b x): 1/Y against 1/x, so a is the slope and b the
# intercept
fit <- lm(I(1 / kwh) ~ I(1 / size))
report("(Hyperbolic)  Y = x / (a + b * x)", fit, coef(fit)[2], coef(fit)[1])
