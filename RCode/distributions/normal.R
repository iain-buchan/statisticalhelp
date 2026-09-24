# Normal distribution: the StatsDirect help illustration (tail areas of the standard
# normal deviates 3 and 0.67, and the deviates that cut off upper tails of 0.025 and
# 0.25, as the Distributions calculator reports them) in R

# StatsDirect shows each figure to 15 significant figures and at most 15 decimal places
s15 <- function(x) {
  formatC(signif(x, 15), digits = 15, format = "f", drop0trailing = TRUE)
}

# Tail areas of a normal deviate z: pnorm(z) is the lower tail P(Z <= z), and with
# lower.tail = FALSE it is the upper tail P(Z > z). The two sided P is twice the
# smaller tail.
tails <- function(z) {
  lower <- pnorm(z)
  upper <- pnorm(z, lower.tail = FALSE)
  cat("P(z ", z, ") = ", s15(upper), " upper, ", s15(lower), " lower, ",
      s15(2 * min(lower, upper)), " two sided\n", sep = "")
}
tails(3)                  # three standard deviations above the mean
tails(0.67)               # close to the upper quartile of the distribution

# Deviates for a tail area: qnorm(p) is the deviate whose lower tail area is p, so
# qnorm(p, lower.tail = FALSE) is the deviate whose upper tail area is p. StatsDirect
# names each inverse by its upper tail P, whichever of its P boxes the P was typed in.
deviate <- function(upper) {
  z <- qnorm(upper, lower.tail = FALSE)
  cat("z(upper P ", s15(upper), ") = ", s15(z), "\n", sep = "")
}
deviate(0.025)            # also from a two tailed P of 0.05: 0.025 in each tail
deviate(0.25)             # the upper quartile

# The technical validation figures above, to the 15 significant figures the help shows
z1 <- qnorm(0.001)
cat("z0.001 = ", s15(z1), "\n", sep = "")
cat("Lower tail P(z= ", s15(z1), ") = ", s15(pnorm(z1)), "\n", sep = "")
z2 <- qnorm(0.25)
cat("z0.25 = ", s15(z2), "\n", sep = "")
cat("Lower tail P(z= ", s15(z2), ") = ", s15(pnorm(z2)), "\n", sep = "")
