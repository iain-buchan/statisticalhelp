# Nonparametric linear regression: the StatsDirect help example (Conover 1999,
# p. 338) in R
gpa  <- c(4.0, 4.0, 3.9, 3.8, 3.7, 3.6, 3.5, 3.5, 3.5, 3.3, 3.2, 3.2)   # outcome
gmat <- c(710, 610, 640, 580, 545, 560, 610, 530, 560, 540, 570, 560)   # predictor
n <- length(gpa)

# R has no standard function for this method (Theil's regression). The slope is
# the median of the slopes between all pairs of points that differ in x, and the
# intercept makes the line pass through the two medians.
s <- outer(gpa, gpa, "-") / outer(gmat, gmat, "-")
s <- sort(s[upper.tri(s) & outer(gmat, gmat, "!=")])
N <- length(s)                            # 62 slopes: 4 of the 66 pairs tie in x
slope <- median(s)
cat("Observations per sample =", n, "\n")
cat(sprintf("Median slope = %.6f   Y-intercept = %.6f\n", slope,
            median(gpa) - slope * median(gmat)))

# 95% confidence interval for the slope (Conover 1999): with w the 0.975 quantile
# of Kendall's statistic (concordant minus discordant pairs) for n pairs, the
# limits are the rth smallest and rth largest slope, r = (N - w) / 2 rounded down.
# The null distribution of the statistic comes from the number of inversions of
# a random ordering: multiply the polynomials 1 + q + ... + q^i for i below n.
ways <- 1
for (i in 1:(n - 1)) ways <- convolve(ways, rep(1, i + 1), type = "open")
S <- n * (n - 1) / 2 - 2 * (seq_along(ways) - 1)      # the statistic, largest first
upper <- cumsum(round(ways)) / factorial(n)           # P(statistic >= S)
w <- max(S[upper > 0.025])                # 28: P(statistic > 28) is 0.0224
r <- floor((N - w) / 2)
cat(sprintf("95%% CI for the slope = %g to %g\n", s[r], s[N + 1 - r]))

# Kendall's rank correlation between outcome and predictor. With ties R gives
# tau b and a normal approximation; continuity = TRUE matches StatsDirect's P.
print(cor.test(gmat, gpa, method = "kendall", exact = FALSE, continuity = TRUE))
