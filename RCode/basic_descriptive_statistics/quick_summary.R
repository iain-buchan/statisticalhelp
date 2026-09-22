# Quick univariate summary: the StatsDirect help example (resting systolic blood
# pressure of 20 first year resident doctors, from the single sample t test) in R
bp <- c(128, 127, 118, 115, 144, 142, 133, 140, 132, 131, 111, 132, 149, 122, 139,
        119, 136, 129, 126, 128)
n <- length(bp)

# R's standard summary, then each line of the quick summary to 6 places
print(summary(bp))
six <- function(x) formatC(round(x, 6), digits = 6, format = "f", drop0trailing = TRUE)
cat("Valid data", n, "  Missing", sum(is.na(bp)), "\n")
cat("Sum", six(sum(bp)), "\n")
cat("Mean", six(mean(bp)), "\n")
cat("Variance", six(var(bp)), "\n")
cat("Standard deviation", six(sd(bp)), "\n")
cat("Variation coefficient", six(sd(bp) / mean(bp)), "\n")
se <- sd(bp) / sqrt(n)
cat("Standard error of mean", six(se), "\n")
cat("95% Upper CL of mean", six(mean(bp) + qt(0.975, n - 1) * se), "\n")
cat("95% Lower CL of mean", six(mean(bp) - qt(0.975, n - 1) * se), "\n")
cat("Geometric mean", six(exp(mean(log(bp)))), "\n")

# Skewness and kurtosis are sqrt(b1) and b2, from the moments about the mean
# with n as the divisor
m <- function(k) mean((bp - mean(bp))^k)
cat("Skewness", six(m(3) / m(2)^1.5), "\n")
cat("Kurtosis", six(m(4) / m(2)^2), "\n")

# The quick summary uses centile type 1: the ordered value whose cumulative
# count first exceeds pn, or the average of two values when pn is a whole
# number, which is type 2 in R
q <- quantile(bp, c(0.05, 0.25, 0.5, 0.75, 0.95), type = 2)
cat("Maximum", six(max(bp)), "\n")
cat("95th percentile", six(q[5]), "\n")
cat("Upper quartile", six(q[4]), "\n")
cat("Median", six(q[3]), "\n")
cat("Lower quartile", six(q[2]), "\n")
cat("Interquartile range", six(q[4] - q[2]), "\n")
cat("5th percentile", six(q[1]), "\n")
cat("Minimum", six(min(bp)), "\n")
cat("Range", six(max(bp) - min(bp)), "\n")
