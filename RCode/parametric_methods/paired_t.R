# Paired t test: the StatsDirect help example (peak expiratory flow rate before
# and after a walk on a cold day in 9 asthmatics) in R
before <- c(312, 242, 340, 388, 296, 254, 391, 402, 290)
after <- c(300, 201, 232, 312, 220, 256, 328, 330, 231)

# R's standard test gives t, df, the two sided P and the confidence interval for
# the mean difference, as StatsDirect reports them.
r <- t.test(before, after, paired = TRUE)
print(r)

# The report's other lines, to 6 decimal places
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
d <- before - after
n <- length(d)
cat(sprintf("Mean of differences = %s  (n = %d)\n", six(mean(d)), n))
cat("Standard deviation =", six(sd(d)), "  Standard error =", six(sd(d) / sqrt(n)),
    "\n")
cat("95% CI =", six(r$conf.int[1]), "to", six(r$conf.int[2]), "\n")
cat("df =", r$parameter, "  t =", six(r$statistic), "\n")
cat(sprintf("One sided P = %.4f   Two sided P = %.4f\n", r$p.value / 2, r$p.value))

# Power of a two sided test at the 5% level to detect the mean difference and sd
# seen, from the noncentral t distribution; strict = TRUE counts rejections in
# both tails, as StatsDirect does.
pw <- power.t.test(n = n, delta = mean(d), sd = sd(d), sig.level = 0.05,
                   type = "paired", strict = TRUE)
cat(sprintf("Power (for 5%% significance) = %.2f%%\n", 100 * pw$power))

# StatsDirect's agreement analysis option adds the 95% limits of agreement of
# Bland and Altman: the mean difference plus or minus 1.96 standard deviations.
loa <- mean(d) + c(-1, 1) * qnorm(0.975) * sd(d)
cat("95% Limits of agreement =", six(loa[1]), "to", six(loa[2]), "\n")

# The agreement plot that follows in the report: each difference against the mean
# of its pair, with the mean difference (black) and the limits of agreement (red)
plot((before + after) / 2, d, xlab = "Mean ((PEFR Before + PEFR After) / 2)",
     ylab = "Difference (PEFR Before - PEFR After)", ylim = range(d, loa))
abline(h = mean(d))
abline(h = loa, col = "red")
legend("topright", "mean difference +/- 95% limits of agreement", bty = "n")
