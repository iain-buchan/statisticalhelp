# Abridged life table: the StatsDirect help example (Chiang 1984, p. 141, the total
# population of California in 1970; the test workbook's Survival worksheet columns
# Interval, Population, Deaths and Fraction a) in R
n <- c(1, 4, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5)  # lengths, last is open
P <- c(340483, 1302198, 1918117, 1963681, 1817379, 1740966, 1457614, 1219389,
       1149999, 1208550, 1245903, 1083852, 933244, 770770, 620805, 484431, 342097,
       210953, 142691)
D <- c(6234, 1049, 723, 735, 2054, 2702, 2071, 1964, 2588, 4114, 6722, 8948, 11942,
       14309, 17088, 19149, 21325, 20129, 22483)
a <- c(0.09, 0.41, 0.44, 0.54, 0.59, 0.49, 0.51, 0.52, 0.53, 0.54, 0.53, 0.53, 0.52,
       0.52, 0.51, 0.52, 0.51, 0.5)
w <- length(P)                          # the number of intervals, the last open
x <- c(0, cumsum(n))                    # the age at the start of each interval
label <- c(paste(x[1], "to", x[2]), paste(x[2:(w - 1)], "to", x[3:w] - 1),
           paste(x[w], "up"))

# Base R has no current life table function, so the table is built by the formulae
# above: the death rate M, the probability of dying q in each interval (1 in the
# open last interval), then a cohort of 100000 followed through the intervals
M <- D / P
q <- c(n * M[-w] / (1 + (1 - a) * n * M[-w]), 1)
l <- 100000 * cumprod(c(1, 1 - q[-w]))  # living at the start of each interval
d <- l * q                              # dying in each interval
L <- c(n * (l[-w] - d[-w]) + a * n * d[-w], l[w] / M[w])  # years lived in interval
Tx <- rev(cumsum(rev(L)))               # years lived beyond the start of interval
e <- Tx / l                             # expectation of life at the start

# Chiang's variances: of q from the deaths, of e by accumulating the variances of
# the survival proportions from the last closed interval back to the first
z <- qnorm(0.975)
vq <- q^2 * (1 - q) / D
b <- (1 - a) * n + e[-1]
ve <- rev(cumsum(rev(l[-w]^2 * b^2 * vq[-w]))) / l[-w]^2

six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
whole <- function(x) formatC(round(x), format = "d")  # the cohort counts, as integers
ci <- function(est, se) paste(six(est - z * se), "to", six(est + z * se))
cat("Interval  Population  Deaths  Death rate\n")
for (i in 1:w) cat(label[i], P[i], D[i], six(M[i]), "\n")
cat("Interval  Probability of dying [qx]  SE of qx  95% CI for qx\n")
for (i in 1:(w - 1)) cat(label[i], six(q[i]), six(sqrt(vq[i])), ci(q[i], sqrt(vq[i])),
                         "\n")
cat(label[w], q[w], "* * to *\n")
cat("Interval  Living at start [lx]  Dying [dx]  Fraction of last interval of life",
    "[ax]\n")
for (i in 1:(w - 1)) cat(label[i], whole(l[i]), whole(d[i]), six(a[i]), "\n")
cat(label[w], whole(l[w]), whole(d[w]), "*\n")
cat("Interval  Years in interval [Lx]  Years beyond start of interval [Tx]\n")
for (i in 1:w) cat(label[i], whole(L[i]), whole(Tx[i]), "\n")
cat("Interval  Expectation of life [ex]  SE of ex  95% CI for ex\n")
for (i in 1:(w - 1)) cat(label[i], six(e[i]), six(sqrt(ve[i])), ci(e[i], sqrt(ve[i])),
                         "\n")
cat(label[w], six(e[w]), "* * to *\n")

# The median: the age at which the cohort has fallen to 50000, by linear
# interpolation within the interval where that happens (75 to 79 here; if the
# cohort never falls that far the median lies beyond the start of the last interval)
i <- which(l <= 50000)[1]
med <- x[i - 1] + n[i - 1] * (l[i - 1] - 50000) / (l[i - 1] - l[i])

# Monte Carlo limits for the median and the expectation of life at birth: the
# deaths in each interval are drawn from a Poisson distribution with the observed
# count as its mean and the table is rebuilt (the same steps again, compactly)
# 20000 times; the limits are the 2.5th and 97.5th percentiles of the simulated
# values, so they vary a little between runs
lifetable <- function(D) {
  M <- D / P
  q <- c(n * M[-w] / (1 + (1 - a) * n * M[-w]), 1)
  l <- 100000 * cumprod(c(1, 1 - q[-w]))
  L <- c(n * (l[-w] - l[-w] * q[-w]) + a * n * l[-w] * q[-w], l[w] / M[w])
  i <- which(l <= 50000)[1]
  c(e0 = sum(L) / l[1], median = x[i - 1] + n[i - 1] * (l[i - 1] - 50000) /
      (l[i - 1] - l[i]))
}
set.seed(1)
sim <- replicate(20000, lifetable(rpois(w, D)))
mc <- function(v) paste(six(quantile(v, 0.025)), "to", six(quantile(v, 0.975)))
cat("Median expectation of life (age at which half of original cohort survives) =",
    six(med), "\n")
cat("95% CI by Monte Carlo (20000) simulation =", mc(sim["median", ]), "\n")
cat("Expectation of life at birth =", six(e[1]), "\n")
cat("95% CI by Chiang variance estimate =", ci(e[1], sqrt(ve[1])), "\n")
cat("95% CI by Monte Carlo (20000) simulation =", mc(sim["e0", ]), "\n")
