# Wilcoxon signed ranks test: the StatsDirect help example (Conover 1999) in R
first  <- c(86, 71, 77, 68, 91, 72, 77, 91, 70, 71, 88, 87)
second <- c(88, 77, 76, 64, 96, 72, 65, 90, 65, 80, 81, 72)
d <- first - second                       # one difference is 0 and some are tied
n <- length(d)

# R's standard test. Its V is the sum of ranks for positive differences. R 4.6.0
# or later gives exact results that allow for the zero and the ties, but it ranks
# the zero with the other differences before setting it aside (the method of
# Pratt, 1959): V = 48.5 and two sided P = 0.458. StatsDirect, like Conover, sets
# zeros aside before ranking: the sum is 41.5 and two sided P = 0.4756. Each P is
# exact for its own method. Older versions of R warn and use a normal
# approximation with continuity correction, as exact = FALSE does (V = 41.5,
# two sided P = 0.4765).
print(wilcox.test(first, second, paired = TRUE, conf.int = TRUE))

# With zeros set aside first, R 4.6.0 or later gives StatsDirect's two sided P.
# Use alternative = "less" for the lower side P and "greater" for the upper.
# Keep the confidence interval from the first call: it needs every difference.
nz <- d[d != 0]
print(wilcox.test(nz))

# Number of non-zero differences and the sum of ranks for positive differences
r <- rank(abs(nz))                        # tied values share a mid-rank
Tplus <- sum(r[nz > 0])
cat("Number of non-zero differences ranked =", length(nz), "\n")
cat("Sum of ranks for positive differences =", Tplus, "\n")

# Exact P given the ties, in any version of R: each rank is as likely to belong
# to a positive as to a negative difference, so build the distribution of the
# sum one rank at a time. Ranks are doubled to make mid-ranks whole numbers.
p <- 1                                    # P(doubled sum = 0), before any rank
for (v in 2 * r) p <- (c(p, numeric(v)) + c(numeric(v), p)) / 2
w <- 2 * Tplus + 1                        # position of the observed value
lower <- sum(p[1:w])                      # each side includes the observed sum
upper <- sum(p[w:length(p)])
cat("Lower side P =", round(lower, 4), "  Upper side P =", round(upper, 4),
    "  Two sided P =", round(min(1, 2 * min(lower, upper)), 4), "\n")

# Confidence interval, from all n differences: K from the exact distribution of
# the statistic without ties, then the Kth smallest and Kth largest of the
# n(n + 1)/2 averages of two differences (each is also averaged with itself).
# With fewer than 6 differences K is 0 and there is no 95% interval.
# StatsDirect's median difference is the median of these averages, which R calls
# the (pseudo)median; it is not median(d), which is 1 here.
# R 4.6.0 or later gives the same limits for these data but works out the level
# given the ties (96.2 percent in R 4.6.1). For other tied data its limits can
# differ.
K <- qsignrank(0.025, n)
a <- outer(d, d, "+") / 2
a <- sort(a[upper.tri(a, diag = TRUE)])
cat(sprintf("%.1f%% confidence interval for difference between population medians:\n",
            100 * (1 - 2 * psignrank(K - 1, n))))
cat(sprintf("K = %g   Median difference = %g (%g to %g)\n",
            K, median(a), a[K], rev(a)[K]))
