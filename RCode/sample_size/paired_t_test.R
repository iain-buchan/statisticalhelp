# Sample size for a paired t test: the StatsDirect help example (an invented plan for
# a study of peak expiratory flow rate before and after a cold walk, like the paired t
# test example: a fall of 20 L/min to detect, sd of paired differences 35 L/min) in R
delta <- 20                               # difference in population means to detect
sdiff <- 35                               # sd of the paired response differences
target <- 0.8                             # power, the probability of detecting delta
alpha <- 0.05                             # two sided type I error probability

# power.t.test solves for the number of pairs at which the power of the two sided
# paired t test, from the non-central t distribution, equals the target. strict = TRUE
# counts rejections in both tails as the report does; the default counts only the tail
# on the side of delta, which seldom changes the whole number of pairs.
r <- power.t.test(delta = delta, sd = sdiff, sig.level = alpha, power = target,
                  type = "paired", strict = TRUE)
print(r)

# A whole number of pairs: the smallest whose power reaches the target (checked below)
n <- ceiling(r$n)
cat("Alpha =", alpha, "\n")
cat("Power =", target, "\n")
cat("Difference of mean from zero =", delta, "\n")
cat("Standard deviation =", sdiff, "\n")
cat("Estimated minimum sample size =", n, "pairs\n")
cat("Degrees of freedom =", n - 1, "\n")

# The report also checks that n is the smallest number of pairs whose power reaches the
# target: the two sided power with n pairs, from the non-central t distribution with
# n - 1 degrees of freedom and non-centrality delta / (sd / sqrt(n)), and with n - 1
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pw <- function(n) {
  tcrit <- qt(1 - alpha / 2, n - 1)
  ncp <- delta / (sdiff / sqrt(n))
  pt(tcrit, n - 1, ncp, lower.tail = FALSE) + pt(-tcrit, n - 1, ncp)
}
cat("Power with", n, "pairs =", six(pw(n)), "and with", n - 1, "pairs =",
    six(pw(n - 1)), "\n")

# The same plan with 90% power
r90 <- power.t.test(delta = delta, sd = sdiff, sig.level = alpha, power = 0.9,
                    type = "paired", strict = TRUE)
n90 <- ceiling(r90$n)
cat("Power = 0.9\n")
cat("Estimated minimum sample size =", n90, "pairs\n")
cat("Degrees of freedom =", n90 - 1, "\n")
