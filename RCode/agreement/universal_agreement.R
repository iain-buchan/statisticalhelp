# Universal agreement R: the StatsDirect help example (Mielke and Berry 2007, five
# objects measured by three observers in three dimensions; the test workbook's
# Agreement worksheet columns Measurement, Object, Observer and Dimension) in R
measurement <- c(8, 10.5, 17.6, 9, 14.6, 9.2, 2.5, 4.5, 12, 6, 6, 11, 13, 14.2, 7.5,
                 8.2, 11.2, 20, 9, 14.2, 9, 3, 4.5, 12.5, 6, 6.5, 11.5, 15, 14, 8,
                 8.2, 9.5, 21.4, 9.5, 14.5, 9, 2.8, 4.5, 13.5, 5.5, 6.5, 12.5, 17, 14.4,
                 9.2)
object <- rep(1:5, 9)
observer <- rep(1:3, each = 15)
dimension <- rep(rep(c("h", "w", "d"), each = 5), 3)
n <- 5
b <- 3

# Each observer's measurements of each object as a point in three dimensions, and the
# distance between every two such points
x <- array(NA, c(n, b, 3))
x[cbind(object, observer, match(dimension, c("h", "w", "d")))] <- measurement
D <- array(0, c(n, b, n, b))
for (i in 1:n) for (r in 1:b) for (j in 1:n) for (s in 1:b) {
  D[i, r, j, s] <- sqrt(sum((x[i, r, ] - x[j, s, ])^2))
}

# Delta is the mean distance between two observers' measurements of the same object,
# over the objects and the pairs of observers. R is 1 minus delta over its expected
# value when each observer's measurements are shuffled among the objects at random.
# Base R has no permutation function; this one lists the n! orderings of the objects.
# Observer 1's labels can stay as they are, so the shuffles of the other observers'
# labels give every distinct rearrangement (5!^2 = 14400 here) and the mean, variance
# and skewness of delta over them are exact
perms <- function(n) {
  if (n == 1) return(matrix(1))
  p <- perms(n - 1)
  do.call(rbind, lapply(1:n, function(k) cbind(k, p + (p >= k))))
}
P <- perms(n)
shuffles <- as.matrix(expand.grid(rep(list(1:nrow(P)), b - 1)))
delta <- function(shuffle, pairs, average) {
  labels <- rbind(1:n, P[shuffle, , drop = FALSE])
  total <- 0
  for (q in 1:ncol(pairs)) {
    r <- pairs[1, q]
    s <- pairs[2, q]
    total <- total + mean(D[cbind(labels[r, ], r, labels[s, ], s)])
  }
  if (average) total / ncol(pairs) else total
}

# The P value refers delta, standardised by its mean and variance, to the Pearson type
# III distribution with the same skewness, which is a gamma distribution
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}
p_type3 <- function(t, skew) {
  if (abs(skew) < 0.01) return(pnorm(t))
  r <- 2 / abs(skew)
  if (skew > 0) pgamma(t + r, shape = r^2, rate = r) else
    pgamma(r - t, shape = r^2, rate = r, lower.tail = FALSE)
}
report <- function(pairs, average, reference) {
  observed <- delta(rep(1, b - 1), pairs, average)   # P's first row is 1:n
  all <- apply(shuffles, 1, delta, pairs = pairs, average = average)
  mu <- mean(all)
  v <- mean((all - mu)^2)
  skew <- mean((all - mu)^3) / v^1.5
  cat("Reference standard:", reference, "\n")
  cat("Observed (realised) delta:", six(observed), "\n")
  cat("Expected (mean) delta:", six(mu), "\n")
  cat("Variance of delta:", six(v), "\n")
  cat("Skewness of delta:", six(skew), "\n")
  cat("Agreement coefficient R:", six(1 - observed / mu), "\n")
  cat("Significance:", pv(p_type3((observed - mu) / sqrt(v), skew)), "\n")
}
cat("Number of measurements:", length(measurement), " observers:", b, " objects:", n,
    " dimensions: 3\n")
report(combn(b, 2), TRUE, "None")

# With observer 1 as the reference standard, delta is the sum over the other observers
# of their mean distance from the reference
report(rbind(1, 2:b), FALSE, "Observer 1")

# Comparison example: R for two independent groups of observers, with the mean,
# variance and skewness of delta that each group's report gives. The difference is
# tested on the delta scale (Berry and Mielke 1997a): its variance and skewness come
# from the two groups' moments, and the P value for the difference is two sided
r1 <- 0.11578
r2 <- 0.19780
mu1 <- 1.27050
mu2 <- 1.60240
var1 <- 0.4678E-03
var2 <- 0.1010E-02
skew1 <- -0.34145
skew2 <- -0.28425
vard <- (mu1^2 * var2 + mu2^2 * var1) / (mu1^2 * mu2^2)
skewd <- (mu1^3 * var2^1.5 * skew2 - mu2^3 * var1^1.5 * skew1) /
  (mu1^3 * mu2^3 * vard^1.5)
cat("R:", six(r1), six(r2), six(r1 - r2), "\n")
cat("Delta:", six(mu1), six(mu2), six(mu1 - mu2), "\n")
cat("Variance:", six(var1), six(var2), six(vard), "\n")
cat("Skewness:", six(skew1), six(skew2), six(skewd), "\n")
cat("Significance:", pv(p_type3((mu1 - mu1 / (1 - r1)) / sqrt(var1), skew1)),
    pv(p_type3((mu2 - mu2 / (1 - r2)) / sqrt(var2), skew2)),
    pv(2 * p_type3((r1 - r2) / sqrt(vard), skewd)), "\n")
