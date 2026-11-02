# Normality tests: the StatsDirect help example (30 penicillin yields) in R
yield <- c(0.0987, 0.0000, 0.0533, -0.0026, 0.0293, -0.0036, 0.0246, -0.0042, 0.0200,
           -0.0114, 0.0194, -0.0139, 0.0191, -0.0222, 0.0180, -0.0333, 0.0172, -0.0348,
           0.0132, -0.0363, 0.0102, -0.0363, 0.0084, -0.0402, 0.0077, -0.0583, 0.0058,
           -0.1184, 0.0016, -0.1420)
n <- length(yield)

# R's standard test gives the Shapiro-Wilk W and its P (Royston 1995), as
# StatsDirect reports them.
sw <- shapiro.test(yield)
print(sw)

six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}
cat("Sample size", n, "  Mean", six(mean(yield)), "  Standard deviation",
    six(sd(yield)), "\n")

# Moments about the mean give the skewness (sqrt b1) and kurtosis (b2)
m2 <- mean((yield - mean(yield))^2)
m3 <- mean((yield - mean(yield))^3)
m4 <- mean((yield - mean(yield))^4)
skew <- m3 / m2^1.5
kurt <- m4 / m2^2

# Test of skewness (D'Agostino 1970), needing at least 8 observations
stopifnot(n >= 8)
y <- skew * sqrt((n + 1) * (n + 3) / (6 * (n - 2)))
beta2 <- 3 * (n^2 + 27 * n - 70) * (n + 1) * (n + 3) /
         ((n - 2) * (n + 5) * (n + 7) * (n + 9))
w2 <- -1 + sqrt(2 * (beta2 - 1))
z1 <- asinh(y / sqrt(2 / (w2 - 1))) / sqrt(log(sqrt(w2)))
cat("Skewness", paste0(six(skew), ","), pv(2 * pnorm(-abs(z1))), "\n")

# Test of kurtosis (Anscombe and Glynn 1983)
xx <- (kurt - 3 * (n - 1) / (n + 1)) /
      sqrt(24 * n * (n - 2) * (n - 3) / ((n + 1)^2 * (n + 3) * (n + 5)))
mo <- 6 * (n^2 - 5 * n + 2) / ((n + 7) * (n + 9)) *
      sqrt(6 * (n + 3) * (n + 5) / (n * (n - 2) * (n - 3)))
a <- 6 + 8 / mo * (2 / mo + sqrt(1 + 4 / mo^2))
z2 <- (1 - 2 / (9 * a) - ((1 - 2 / a) / (1 + xx * sqrt(2 / (a - 4))))^(1 / 3)) /
      sqrt(2 / (9 * a))
cat("Kurtosis", paste0(six(kurt), ","), pv(2 * pnorm(-abs(z2))), "\n")

# Omnibus test: K2 = Z1^2 + Z2^2 (D'Agostino, Belanger and D'Agostino 1990) with
# Royston's (1991) correction, which makes it closer to chi-square with 2 df.
# The corrected K2 is -2 log P.
zc <- -qnorm(exp(-(z1^2 + z2^2) / 2))     # K2's P as a normal deviate
ln <- log(n)
cut <- 0.55 * n^0.2 - 0.21
a1 <- (-5 + 3.46 * ln) * exp(-1.37 * ln)
b1 <- 1 + (0.854 - 0.148 * ln) * exp(-0.55 * ln)
b2 <- 2.13 / (1 - 2.37 * ln)
zr <- if (zc < -1) zc else if (zc < cut) a1 + b1 * zc else
      a1 - b2 * cut + (b2 + b1) * zc
pk <- pnorm(-zr)
cat("Royston chi-sq", paste0(six(-2 * log(pk)), ","), pv(pk), "\n")

# StatsDirect adds V = (1 - W) / the median of 1 - W under normality, from the
# approximation for 12 or more observations (it uses another for 4 to 11)
stopifnot(n >= 12)
mu <- -1.5861 + ln * (-0.31082 + ln * (-0.083751 + ln * 0.0038915))
cat("Shapiro-Wilk W", paste0(six(sw$statistic), ","),
    "V =", paste0(six((1 - sw$statistic) / exp(mu)), ","), pv(sw$p.value), "\n")

# Shapiro-Francia W': the squared correlation between the ordered values and
# Blom's normal scores for their positions, with P and V' from Royston's (1983)
# normalising transformation
x <- sort(yield)
m <- qnorm((1:n - 0.375) / (n + 0.25))
Wf <- cor(x, m)^2
h <- ln - 5
l <- -0.0480157 + h * (0.01971964 - 0.0119065 * h^2)
mf <- -exp(1.6930674 + h * (0.1441647 + h * (-0.01849276 + h * (0.031074485 +
                                                                  h * 0.0055717663))))
f <- exp(-0.510725 + h * (-0.1160364 + h * (-0.006702098 + h * (0.054465944 +
                                                                  h * 0.0087397329))))
psf <- pnorm(-(((1 - Wf)^l - 1) / l - mf) / f)
cat("Shapiro-Francia W'", paste0(six(Wf), ","),
    "V' =", paste0(six((1 - Wf) / (l * mf + 1)^(1 / l)), ","), pv(psf), "\n")

# StatsDirect's conclusion, from the smaller of the last two P values
p <- min(sw$p.value, psf)
cat(if (p < 0.05) "Sample unlikely to be from a normal distribution"
    else if (p < 0.1) "Tests not quite significant but do not assume normality"
    else "No non-normality detected by tests: examine plot", "\n")

# The normal plot at the end of the report: each value against its Blom normal
# score put on the scale of the data (mean + z sd, with sd taken over n), and the
# line of equality
z <- qnorm((rank(yield) - 0.375) / (n + 0.25))
expected <- mean(yield) + z * sqrt(m2)
lim <- range(expected, yield)
plot(expected, yield, xlim = lim, ylim = lim, xlab = "Normal (Penicillin)",
     ylab = "Observed (Penicillin)")
abline(0, 1)
