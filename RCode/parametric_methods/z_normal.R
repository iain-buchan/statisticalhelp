# Normal distribution (z) tests: the StatsDirect help example (Michelson's 100
# measurements of the speed of light in air, millions of metres per second) in R
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
mu <- 299.792458                          # the accepted value, in a vacuum

# R has no z test among its standard functions (with a sample this large t.test
# gives almost the same answers), so the report's lines are calculated here.
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else paste("P =", formatC(p, digits = 4, format = "f"))
}
z <- qnorm(0.975)
cat("Sample mean =", six(mean(speed)), "  Sample size n =", n,
    "  Sample sd =", six(sd(speed)), "\n")

# Single sample test, with the population sd not known: the sample sd is used
se <- sd(speed) / sqrt(n)
cat("95% confidence interval for mean difference =", six(mean(speed) - mu - z * se),
    "to", six(mean(speed) - mu + z * se), "\n")
dev <- (mean(speed) - mu) / se
cat("Standard normal deviate (z) =", six(dev), "\n")
cat("One sided", pv(pnorm(-abs(dev))), "  Two sided", pv(2 * pnorm(-abs(dev))), "\n")

# For lognormal data: the geometric mean, with the reference range exp(mean of
# the logs plus or minus z times their sd)
lg <- log(speed)
cat("Geometric mean =", six(exp(mean(lg))), " (95% reference range =",
    six(exp(mean(lg) - z * sd(lg))), "to", paste0(six(exp(mean(lg) + z * sd(lg))), ")"),
    "\n")

# Two independent samples: the first and last 50 measurements
first <- speed[1:50]
last <- speed[51:100]
cat("First 50:  Mean =", six(mean(first)), "  Variance =", six(var(first)),
    "  Size =", length(first), "\n")
cat("Last 50:  Mean =", six(mean(last)), "  Variance =", six(var(last)),
    "  Size =", length(last), "\n")
se2 <- sqrt(var(first) / length(first) + var(last) / length(last))
cat("Combined standard error =", six(se2), "\n")
d <- mean(first) - mean(last)
cat("95% confidence interval for difference between means =", six(d - z * se2), "to",
    six(d + z * se2), "\n")
dev2 <- d / se2
cat("Standard normal deviate (z) =", six(dev2), "\n")
cat("One sided", pv(pnorm(-abs(dev2))), "  Two sided", pv(2 * pnorm(-abs(dev2))), "\n")
