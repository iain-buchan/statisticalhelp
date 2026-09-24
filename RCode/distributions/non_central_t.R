# Non-central t distribution: the illustration in the StatsDirect help (a tail
# probability, its inverse, and the power of a t test, as the Distributions dialog
# computes them) in R
# StatsDirect shows these to up to 15 decimal places. R's pt evaluates the non-central t
# distribution by the series of Lenth (1989) and qt inverts it (see the help for pt);
# here they agree with StatsDirect's values to 12 decimal places, so 12 are printed.
twelve <- function(x) formatC(x, digits = 12, format = "f", drop0trailing = TRUE)
seven <- function(x) formatC(x, digits = 7, format = "f", drop0trailing = TRUE)

# Tail probabilities of t = 2.5 on 10 degrees of freedom with non-centrality 1:
# pt(q, df, ncp) is the lower tail (P for t <= t); lower.tail = FALSE gives the upper
cat("P(non-central t < 2.5, df 10, delta 1) =",
    twelve(pt(2.5, 10, ncp = 1, lower.tail = FALSE)), "upper,",
    twelve(pt(2.5, 10, ncp = 1)), "lower\n")

# The inverse: the value of t below which the distribution has probability 0.9 (Invert
# with 0.9 as P for t <= t; the report labels the line with the upper tail, 0.1)
cat("non-central t(P 0.1, df 10, delta 1) =", twelve(qt(0.9, 10, ncp = 1)), "\n")

# Power of a paired or single sample t test on n = 20 observations at the two sided 5%
# level when the true mean difference is d = 0.5 standard deviations: under that
# alternative t has 19 degrees of freedom and non-centrality d * sqrt(n). The critical
# value and the non-centrality are rounded to the 6 places typed into the dialog.
n <- 20
d <- 0.5
delta <- round(d * sqrt(n), 6)              # 2.236068
crit <- round(qt(0.975, n - 1), 6)          # 2.093024
upper <- pt(crit, n - 1, ncp = delta, lower.tail = FALSE)
cat(paste0("P(non-central t < ", crit, ", df ", n - 1, ", delta ", delta, ") ="),
    twelve(upper), "upper,", twelve(pt(crit, n - 1, ncp = delta)), "lower\n")
lower <- pt(-crit, n - 1, ncp = delta)
cat(paste0("P(non-central t < ", -crit, ", df ", n - 1, ", delta ", delta, ") ="),
    twelve(pt(-crit, n - 1, ncp = delta, lower.tail = FALSE)), "upper,",
    twelve(lower), "lower\n")
# The test rejects when |t| exceeds the critical value, so its power is the upper tail
# beyond crit plus the lower tail below -crit
cat("Power =", seven(upper + lower), "=", seven(upper), "+", seven(lower), "\n")

# power.t.test does the same with the unrounded critical value; strict = TRUE counts
# rejections in the other tail too, as StatsDirect's sample size calculations for t
# tests do (without it only the tail on the side of the difference is counted)
print(power.t.test(n = n, delta = d, sd = 1, sig.level = 0.05, type = "one.sample",
                   strict = TRUE))
