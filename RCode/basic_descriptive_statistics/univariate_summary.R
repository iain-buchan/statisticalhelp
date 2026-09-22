# Univariate summary: the StatsDirect help example (Michelson's 100 measurements
# of the speed of light in air, millions of metres per second) in R
speed <- c(299.85, 299.74, 299.90, 300.07, 299.93, 299.85, 299.95, 299.98, 299.98,
           299.88, 300.00, 299.98, 299.93, 299.65, 299.76, 299.81, 300.00, 300.00,
           299.96, 299.96, 299.96, 299.94, 299.96, 299.94, 299.88, 299.80, 299.85,
           299.88, 299.90, 299.84, 299.83, 299.79, 299.81, 299.88, 299.88, 299.83,
           299.80, 299.79, 299.76, 299.80, 299.88, 299.88, 299.88, 299.86, 299.72,
           299.72, 299.62, 299.86, 299.97, 299.95, 299.88, 299.91, 299.85, 299.87,
           299.84, 299.84, 299.85, 299.84, 299.84, 299.84, 299.89, 299.81, 299.81,
           299.82, 299.80, 299.77, 299.76, 299.74, 299.75, 299.76, 299.91, 299.92,
           299.89, 299.86, 299.88, 299.72, 299.84, 299.85, 299.85, 299.78, 299.89,
           299.84, 299.78, 299.81, 299.76, 299.81, 299.79, 299.81, 299.82, 299.85,
           299.87, 299.87, 299.81, 299.74, 299.81, 299.94, 299.95, 299.80, 299.81,
           299.87)
n <- length(speed)

# R's standard summary, then each statistic of the report, printed to 12 places
# as the example shows them (decimal places set to 12 in Analysis_Options)
print(summary(speed))
twelve <- function(x) {
  formatC(round(x, 12), digits = 12, format = "f", drop0trailing = TRUE)
}
cat("Valid data", n, "  Missing data", sum(is.na(speed)), "\n")
cat("Sum", twelve(sum(speed)), "\n")
cat("Mean", twelve(mean(speed)), "\n")
cat("Variance", twelve(var(speed)), "\n")
cat("Standard deviation", twelve(sd(speed)), "\n")
cat("Variance coefficient", twelve(sd(speed) / mean(speed)), "\n")
se <- sd(speed) / sqrt(n)
cat("Standard error of mean", twelve(se), "\n")
cat("Upper 95% CL of mean", twelve(mean(speed) + qt(0.975, n - 1) * se), "\n")
cat("Lower 95% CL of mean", twelve(mean(speed) - qt(0.975, n - 1) * se), "\n")
cat("Geometric mean", twelve(exp(mean(log(speed)))), "\n")

# Skewness and kurtosis are sqrt(b1) and b2, from the moments about the mean
# with n as the divisor, as in the normality tests
m <- function(k) mean((speed - mean(speed))^k)
cat("Skewness", twelve(m(3) / m(2)^1.5), "\n")
cat("Kurtosis", twelve(m(4) / m(2)^2), "\n")

# Centile type 2 interpolates between the ordered values at p(n + 1): type 6 in
# R, whose default, type 7, interpolates at 1 + p(n - 1) instead
q <- quantile(speed, c(0.05, 0.25, 0.5, 0.75, 0.95), type = 6)
cat("Maximum", max(speed), "\n")
cat("Upper quartile", twelve(q[4]), "\n")
cat("Median", twelve(q[3]), "\n")
cat("Lower quartile", twelve(q[2]), "\n")
cat("Interquartile range", twelve(q[4] - q[2]), "\n")
cat("Minimum", min(speed), "\n")
cat("Range", twelve(max(speed) - min(speed)), "\n")
cat("Centile 95", twelve(q[5]), "\n")
cat("Centile 5", twelve(q[1]), "\n")

# Weighted univariate summary: five values with weights, as a second example.
# StatsDirect first scales the weights to sum to the number of values n, then
# uses n - 1 for the variance and n for the moments and the standard error, so a
# weighted summary is not the summary of the values repeated by their weights.
value <- 1:5
weight <- c(2, 1, 3, 1, 1)
n <- length(value)
w <- weight * n / sum(weight)
m <- weighted.mean(value, w)
v <- sum(w * (value - m)^2) / (n - 1)
six <- function(x) formatC(round(x, 6), digits = 6, format = "f", drop0trailing = TRUE)
cat("Valid data", n, "  Sum of weights", sum(weight), "\n")
cat("Mean", six(m), "  Variance", six(v), "  Standard deviation", six(sqrt(v)), "\n")
cat("Variance coefficient", six(sqrt(v) / m), "  Standard error of mean",
    six(sqrt(v / n)), "\n")
cat("Upper 95% CL of mean", six(m + qt(0.975, n - 1) * sqrt(v / n)),
    "  Lower 95% CL of mean", six(m - qt(0.975, n - 1) * sqrt(v / n)), "\n")
cat("Geometric mean", six(exp(sum(w * log(value)) / n)), "\n")
mom <- function(k) sum(w * (value - m)^k) / n
cat("Skewness", six(mom(3) / mom(2)^1.5), "  Kurtosis", six(mom(4) / mom(2)^2), "\n")

# Centile type 1 with weights: the ordered value at which the cumulative scaled
# weight first exceeds pn, or the average of it and the value before when the
# cumulative weight reaches pn exactly
centile <- function(p) {
  cum <- cumsum(w)
  i <- which(cum > p * n)[1]
  if (i > 1 && isTRUE(all.equal(cum[i - 1], p * n))) (value[i - 1] + value[i]) / 2
  else value[i]
}
cat("Upper quartile", centile(0.75), "  Median", centile(0.5), "  Lower quartile",
    centile(0.25), "  Centile 95", centile(0.95), "  Centile 5", centile(0.05), "\n")
