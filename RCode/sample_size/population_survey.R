# Sample size for a population survey: the StatsDirect help example (an invented
# survey of smoking among the 20,000 adults of a town, expected 25% give or take 3%,
# with 95% confidence) in R
N <- 20000                                # size of the population to be sampled
p <- 25 / 100                             # expected proportion with the characteristic
d <- 3 / 100                              # acceptable absolute deviation, plus or minus
conf <- 0.95                              # confidence level

# Base R has no function for this survey sample size, so the topic's formula is used:
# the size for an unlimited population from the normal approximation to the binomial,
# then the finite population correction, then rounding up to a whole number of subjects.
z <- qnorm(1 - (1 - conf) / 2)            # two sided: 1.959964 for 95% confidence
sn <- z^2 * p * (1 - p) / d^2             # for an unlimited population
n <- sn / (1 + sn / N)                    # with the finite population correction
# StatsDirect adds one to the whole part of n instead, so a rate of exactly 0% or 100%
# (n = 0) gives 1 there and 0 here; they differ only when n is a whole number
cat("Population estimate =", format(N, scientific = FALSE), "\n")
cat("Population rate =", paste0(100 * p, "%"), "\n")
cat("Maximum deviation = +/-", paste0(100 * d, "%"), "\n")
cat("Confidence level =", 100 * conf, "\n")
cat("Estimated minimum sample size =", ceiling(n), "\n")

# The same survey of a population of unlimited size, for comparison
cat("Sample size for an unlimited population =", ceiling(sn), "\n")
