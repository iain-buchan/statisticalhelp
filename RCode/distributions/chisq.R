# Chi-square distribution: the example in the StatsDirect help (the upper tail
# probabilities of two chi-square statistics and the 1% critical value on 3 degrees of
# freedom, as the Distributions dialog computes them) in R
fifteen <- function(x) formatC(x, digits = 15, format = "f", drop0trailing = TRUE)

# The upper tail probability of a chi-square statistic, the P value of a chi-square
# test: pchisq with lower.tail = FALSE (pchisq(x, df) gives the lower tail; subtracting
# that from 1 would lose accuracy far into the upper tail)
cat("P(chi-sq 3.84, df 1) =", fifteen(pchisq(3.84, 1, lower.tail = FALSE)),
    "upper tail\n")
cat("P(chi-sq 15.2, df 6) =", fifteen(pchisq(15.2, 6, lower.tail = FALSE)),
    "upper tail\n")

# With one degree of freedom chi-square is the square of a standard normal deviate, so
# its upper tail is the two sided normal tail beyond the square root of the statistic
cat("Two sided normal P beyond sqrt(3.84) =", fifteen(2 * pnorm(-sqrt(3.84))), "\n")

# The chi-square value that cuts off a given upper tail (the critical value for a P):
# qchisq. StatsDirect shows it to at most 15 decimal places and 15 significant figures
# (13 decimal places for a value above 10), so it is printed the same way here
x15 <- function(x) formatC(x, digits = 15, format = "g", drop0trailing = TRUE)
cat("chi-sq(upper P 0.01, df 3) =", x15(qchisq(0.01, 3, lower.tail = FALSE)), "\n")
