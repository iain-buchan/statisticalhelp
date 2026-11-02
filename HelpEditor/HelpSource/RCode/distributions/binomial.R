# Binomial distribution: the StatsDirect help illustration (a fair coin tossed 10 times,
# and a treatment that succeeds in one patient in four given to 20 patients) in R
# dbinom(r, n, p) is the probability of exactly r successes in n trials, pbinom(r, n, p)
# the probability of r or fewer; with lower.tail = FALSE, pbinom gives the probability
# of more than r successes, so r or more is pbinom(r - 1, n, p, lower.tail = FALSE).
dbinom(5, 10, 0.5)                          # exactly 5 heads in 10 tosses of a coin
pbinom(5, 10, 0.5)                          # 5 or fewer heads
pbinom(4, 10, 0.5, lower.tail = FALSE)      # 5 or more heads

# The dialog's report line, its probabilities to 15 decimal places
fifteen <- function(x) formatC(x, digits = 15, format = "f", drop0trailing = TRUE)
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
binomial <- function(p, n, r) {
  cat("P(binomial p ", p, ", ", n, " trials) = ", fifteen(dbinom(r, n, p)), " [", r,
      " successes], ", fifteen(pbinom(r - 1, n, p, lower.tail = FALSE)), " [>=", r,
      " successes], ", fifteen(pbinom(r, n, p)), " [<=", r, " successes]\n", sep = "")
}
binomial(p = 0.5, n = 10, r = 5)

# The same from the formula in the text: choose(n, r) is n! / (r! (n - r)!)
r <- 0:10
exactly <- choose(10, r) * 0.5^r * (1 - 0.5)^(10 - r)
cat("Exactly 5 =", fifteen(exactly[r == 5]),
    "  5 or more =", fifteen(sum(exactly[r >= 5])),
    "  5 or fewer =", fifteen(sum(exactly[r <= 5])), "\n")
cat("Mean np =", 10 * 0.5, "  standard deviation sqrt(np(1-p)) =",
    six(sqrt(10 * 0.5 * (1 - 0.5))), "\n")

# The normal approximation of the text for 5 or more heads, and with the continuity
# correction (r - 0.5 in place of r) that is usual for it: crude with n as small as 10
se <- sqrt(0.5 * (1 - 0.5) / 10)
cat("Normal approximation to P(>=5) =",
    six(pnorm((5 / 10 - 0.5) / se, lower.tail = FALSE)),
    "  with continuity correction =",
    six(pnorm((4.5 / 10 - 0.5) / se, lower.tail = FALSE)), "\n")

# A treatment that succeeds in one patient in four, given to 20 patients: 8 successes
binomial(p = 0.25, n = 20, r = 8)

# The probability of 8 or more successes is the one sided P value of the exact binomial
# test of 8 successes in 20 against a success rate of 0.25
print(binom.test(8, 20, p = 0.25, alternative = "greater"))

# qbinom is the inverse, the smallest r whose cumulative probability P(<= r) reaches the
# probability given; the StatsDirect dialog does not offer it for the binomial
qbinom(0.95, 20, 0.25)                      # 8: P(<=7) is 0.898188 and P(<=8) 0.959075
