# Paired proportions: the StatsDirect help example (Armitage and Berry 1994, p. 138,
# growth of tubercle bacilli from 50 sputum specimens on two culture media) in R
n <- 50   # total pairs
r <- 20   # growth on both media
s <- 12   # growth on medium A only
t <- 2    # growth on medium B only
p1 <- (r + s) / n
p2 <- (r + t) / n
cat("Total = ", n, ",  both = ", r, ",  first only = ", s, ",  second only = ", t,
    "\n", sep = "")
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}
cat("Proportion 1 =", six(p1), "\n")
cat("Proportion 2 =", six(p2), "\n")
cat("Proportion difference =", six((s - t) / n), "\n")

# The exact test is a binomial test on the discordant pairs: under the null
# hypothesis each of the s + t pairs that differ is equally likely to favour
# either medium, so s ~ Binomial(s + t, 1/2). With probability 1/2 the binomial is
# symmetric, so the outcomes at least as far from 7 as s = 12 are 0 to 2 and 12 to 14,
# and binom.test's two sided P (0.0129) is twice the one sided P. mcnemar.test() on the
# 2 by 2 table gives the continuity corrected chi-square approximation instead.
print(binom.test(s, s + t, p = 0.5))

# One sided and mid P values. The one sided P is the lower tail of the smaller of
# s and t; mid P counts the observed value with half weight. Two sided P is twice
# the one sided value, capped at 1.
m <- min(s, t)
p_one <- pbinom(m, s + t, 0.5)
p_mid <- p_one - dbinom(m, s + t, 0.5) / 2
cat("Exact two sided", pv(min(1, 2 * p_one)), "\n")
cat("Exact one sided", pv(p_one), "\n")
cat("Exact two sided mid", pv(min(1, 2 * p_mid)), "\n")
cat("Exact one sided mid", pv(p_mid), "\n")

# Confidence interval for the difference: Newcombe's (1998) method 10. The Wilson
# score interval of each proportion is found first, then the two are combined using
# an estimate of the correlation (phi) between the paired outcomes, with Newcombe's
# continuity correction of the numerator of phi when it is positive.
z <- qnorm(0.975)
a <- r
b <- s
c <- t
d <- n - r - s - t
wilson <- function(x) {
  den <- n + z^2
  half <- 0.5 * z * sqrt(z^2 + 4 * x * (n - x) / n)
  c((x + 0.5 * z^2 - half) / den, (x + 0.5 * z^2 + half) / den)
}
w1 <- wilson(a + b)   # limits for proportion 1
w2 <- wilson(a + c)   # limits for proportion 2
if ((a + b) * (c + d) * (a + c) * (b + d) == 0) {
  phi <- 0
} else {
  num <- a * d - b * c
  if (num > 0) num <- max(num - n / 2, 0)
  phi <- num / sqrt((a + b) * (c + d) * (a + c) * (b + d))
}
dl1 <- p1 - w1[1]
du1 <- w1[2] - p1
dl2 <- p2 - w2[1]
du2 <- w2[2] - p2
lower <- (p1 - p2) - sqrt(dl1^2 - 2 * phi * dl1 * du2 + du2^2)
upper <- (p1 - p2) + sqrt(du1^2 - 2 * phi * du1 * dl2 + dl2^2)
cat("Score based (Newcombe) 95% confidence interval for the proportion difference:\n")
cat(six(lower), "to", six(upper), "\n")
