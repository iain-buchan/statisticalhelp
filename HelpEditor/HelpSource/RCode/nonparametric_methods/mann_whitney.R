# Mann-Whitney U test: the StatsDirect help example (Conover 1999, p. 218) in R
farm <- c(14.8, 7.3, 5.6, 6.3, 9.0, 4.2, 10.6, 12.5, 12.9, 16.1, 11.4, 2.7)
town <- c(12.7, 14.2, 12.6, 2.1, 17.7, 11.8, 16.9, 7.9, 16.0, 10.6, 5.6, 5.6,
          7.6, 11.3, 8.3, 6.7, 3.6, 1.0, 2.4, 6.4, 9.1, 6.7, 18.6, 3.2,
          6.2, 6.1, 15.3, 10.6, 1.8, 5.9, 9.9, 10.6, 14.8, 5.0, 2.6, 4.0)
n1 <- length(farm)
n2 <- length(town)

# R's standard test. Its W is the U for x (243). These data have ties. R 4.6.0
# or later gives exact results that allow for the ties. Older versions warn and
# use a normal approximation with continuity correction, as exact = FALSE does
# (two sided P = 0.5279).
# Use alternative = "less" for the lower side P and "greater" for the upper.
print(wilcox.test(farm, town, conf.int = TRUE))

# Medians, rank sum, U, U' and theta
r <- rank(c(farm, town))                  # tied values share a mid-rank
ranksum <- sum(r[1:n1])
U <- ranksum - n1 * (n1 + 1) / 2
Uprime <- n1 * n2 - U
theta <- Uprime / (n1 * n2)
cat("x: n =", n1, "  median =", median(farm), "  rank sum =", ranksum, "\n")
cat("y: n =", n2, "  median =", median(town), "\n")
cat("U =", U, "  U' =", Uprime, "\n")

# Theta: 95% confidence interval by method 5 of Newcombe (2006b). Each search
# stops just short of the end that would solve the equation if theta is 0 or 1.
z <- qnorm(0.975)
h <- (n1 + n2) / 2 - 1
e <- 1e-9
f <- function(y, s) y + s * z * sqrt(y * (1 - y) *
       (1 + h * ((1 - y) / (2 - y) + y / (1 + y))) / (n1 * n2)) - theta
ci <- c(uniroot(f, c(0, 1 - e), s = 1, tol = 1e-10)$root,     # lower limit
        uniroot(f, c(e, 1), s = -1, tol = 1e-10)$root)        # upper limit
cat(sprintf("Theta = %.6f (95%% CI: %.6f to %.6f)\n", theta, ci[1], ci[2]))

# Exact P given the ties, in any version of R: count the ways of choosing n1
# of the ranks, by their sum. Ranks are doubled to make mid-ranks whole numbers.
r2 <- 2 * r
S <- sum(r2)
ways <- matrix(0, n1 + 1, S + 1)          # rows: how many ranks, columns: sum
ways[1, 1] <- 1
for (v in r2) ways[-1, (v + 1):(S + 1)] <-
  ways[-1, (v + 1):(S + 1)] + ways[-(n1 + 1), 1:(S + 1 - v)]
p <- ways[n1 + 1, ] / choose(n1 + n2, n1) # P(doubled rank sum = 0, 1, ... S)
w <- 2 * ranksum + 1                      # position of the observed value
lower <- sum(p[1:w])                      # each side includes the observed U
upper <- sum(p[w:(S + 1)])
cat("Lower side P =", round(lower, 4), "  Upper side P =", round(upper, 4),
    "  Two sided P =", round(min(1, 2 * min(lower, upper)), 4), "\n")

# Confidence interval for the difference: K from the exact distribution of U
# without ties, then the Kth smallest and Kth largest of the n1 * n2 differences.
# For these data R 4.6.0 or later gives the same limits but labels them 95.2
# percent, as it allows for the ties. For other tied data its limits can differ.
K <- qwilcox(0.025, n1, n2)
d <- sort(outer(farm, town, "-"))
cat(sprintf("%.2f%% confidence interval for the difference: K = %d\n",
            100 * (1 - 2 * pwilcox(K - 1, n1, n2)), K))
cat(sprintf("median difference = %g   CI = %g to %g\n",
            median(d), d[K], rev(d)[K]))
