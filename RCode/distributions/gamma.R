# Gamma distribution: the StatsDirect help illustration (100 random values with a
# shaping parameter A = 2 and a scaling parameter B = 3, from Data_Generating_Random
# Numbers_Gamma) in R
A <- 2                                    # shaping parameter: R's shape argument
B <- 3                                    # scaling parameter: R's scale argument, the
                                          # reciprocal of its rate argument
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)

# The distribution: its mean is AB and its variance A times B squared; dgamma, pgamma
# and qgamma give the density, the cumulative probability and the quantiles
cat("Mean =", six(A * B), "  Variance =", six(A * B^2),
    "  Standard deviation =", six(sqrt(A) * B), "\n")
cat("Density at t = 4:", six(dgamma(4, shape = A, scale = B)), "\n")
cat("P(t <= 4) =", six(pgamma(4, shape = A, scale = B)), "\n")
cat("Median =", six(qgamma(0.5, shape = A, scale = B)), "\n")
cat("95th centile =", six(qgamma(0.95, shape = A, scale = B)), "\n")

# 100 random values: R and StatsDirect both use the Mersenne Twister generator but each
# seeds it in its own way, so the values differ from the column StatsDirect fills for
# any seed, while each seed repeats its own series in both programs. The set.seed call
# makes this run repeatable.
set.seed(2024)
x <- rgamma(100, shape = A, scale = B)
print(summary(x))
cat("Sample mean =", six(mean(x)), "  Sample sd =", six(sd(x)), "\n")

# Two special cases the topic mentions: A = 1 is the exponential distribution with
# mean B, and with B = 1, A = 0.5 is half a squared standard normal deviate (half
# a chi-square deviate with 1 degree of freedom), so the cumulative probabilities
# agree
cat("A = 1: P(t <= 4) =", six(pgamma(4, shape = 1, scale = B)),
    " exponential:", six(pexp(4, rate = 1 / B)), "\n")
cat("A = 0.5, B = 1: P(t <= 1.5) =", six(pgamma(1.5, shape = 0.5)),
    " chi-square:", six(pchisq(2 * 1.5, df = 1)), "\n")
