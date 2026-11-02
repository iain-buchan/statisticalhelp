# Single sample t test: the StatsDirect help example (resting systolic blood
# pressure of 20 first year resident doctors) in R
bp <- c(128, 127, 118, 115, 144, 142, 133, 140, 132, 131, 111, 132, 149, 122, 139,
        119, 136, 129, 126, 128)
mu <- 120                                 # the population mean to compare with
n <- length(bp)

# R's standard test gives t, df, the two sided P and a 95% confidence interval for
# the mean. StatsDirect's interval is for the difference between the sample and
# population means, so its limits are these less 120.
r <- t.test(bp, mu = mu)
print(r)

# The report's other lines, to 6 decimal places
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
cat("Sample mean =", six(mean(bp)), "  Sample size n =", n,
    "  Sample sd =", six(sd(bp)), "\n")
cat("95% confidence interval for mean difference =", six(r$conf.int[1] - mu), "to",
    six(r$conf.int[2] - mu), "\n")
cat("df =", r$parameter, "  t =", six(r$statistic), "\n")
cat(sprintf("One sided P = %.4f   Two sided P = %.4f\n", r$p.value / 2, r$p.value))

# Power of a two sided test at the 5% level to detect the difference and sd seen,
# from the noncentral t distribution; strict = TRUE counts rejections in both
# tails, as StatsDirect does.
pw <- power.t.test(n = n, delta = mean(bp) - mu, sd = sd(bp), sig.level = 0.05,
                   type = "one.sample", strict = TRUE)
cat(sprintf("Power (for 5%% significance) = %.2f%%\n", 100 * pw$power))
