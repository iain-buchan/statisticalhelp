# Gini coefficient of inequality: the StatsDirect help example (Pan American
# Health Organisation 2001, infant mortality in five countries) in R
imr <- c(59, 43, 39, 24, 22)              # infant deaths per 1000 live births
n <- length(imr)

# The Gini coefficient from the values in ascending order (the argument i lets
# boot() below pass in a re-sample)
gini <- function(x, i = seq_along(x)) {
  x <- sort(x[i])
  n <- length(x)
  sum((2 * seq_len(n) - n - 1) * x) / (n * sum(x))
}
G <- gini(imr)
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
cat("Positive non-zero observations =", n, "\n")
cat("Variance coefficient =", six(sd(imr) / mean(imr)), "\n")
cat("Gini coefficient =", six(G), "\n")
cat("Unbiased estimator of population Gini coefficient =", six(G * n / (n - 1)), "\n")

# Bootstrap confidence intervals with the boot package, which comes with R: 2000
# re-samples as in the example (StatsDirect's default is 100,000). The limits
# depend on the random re-samples, so they differ a little from run to run and
# from StatsDirect's. R's bias is the mean re-sampled coefficient less G, the
# opposite way round to StatsDirect's. The BCa interval is given the jackknife
# influence values, so its acceleration is the one StatsDirect uses (Efron and
# Tibshirani 1993). With so few values R warns that the BCa limits are unstable.
library(boot)
set.seed(2001)
b <- boot(imr, gini, R = 2000)
cat("Re-samples =", b$R, "\n")
print(b)
ci <- boot.ci(b, type = c("perc", "bca"), L = empinf(b, type = "jack"))
print(ci)

# StatsDirect multiplies the limits, like the coefficient, by n / (n - 1) for the
# unbiased estimator
cat("Unbiased percentile 95% CI =", six(ci$percent[4] * n / (n - 1)), "to",
    six(ci$percent[5] * n / (n - 1)), "\n")
cat("Unbiased BCa 95% CI =", six(ci$bca[4] * n / (n - 1)), "to",
    six(ci$bca[5] * n / (n - 1)), "\n")

# Brown's (1994) index from the two components, live births and infant deaths,
# with the countries in ascending order of the rate
births <- c(250, 621, 308, 889, 568)
deaths <- c(14750, 26703, 12012, 21336, 12496)
o <- order(imr)
X <- c(0, cumsum(births[o]) / sum(births))
Y <- c(0, cumsum(deaths[o]) / sum(deaths))
gb <- 1 - sum((Y[-1] + Y[-(n + 1)]) * (X[-1] - X[-(n + 1)]))
cat("Brown's G b =", formatC(gb, digits = 4, format = "f"), "\n")

# The Lorenz plot at the end of the report: the cumulative share of the variable
# against the cumulative share of the sample (green), with the line of equality
# (red)
x <- sort(imr)
plot(c(0, seq_len(n) / n), c(0, cumsum(x) / sum(x)), type = "b", col = "green",
     xlim = c(0, 1), ylim = c(0, 1), xlab = "Proportion of sample",
     ylab = "Proportion of variable")
abline(0, 1, col = "red")
