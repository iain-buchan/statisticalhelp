# Poisson distribution: the StatsDirect help illustration (probabilities for a count of
# events with a given mean, and the mean that gives a chosen tail probability) in R

# StatsDirect shows the probabilities of exactly n, of n or more and of n or fewer
# events to 15 decimal places, and a mean to 15 significant figures; these helpers
# print R's values the same way.
p15 <- function(x) formatC(x, digits = 15, format = "f", drop0trailing = TRUE)
s15 <- function(x) trimws(formatC(x, digits = 15, format = "fg", drop0trailing = TRUE))

# 14 events when the mean is 8. dpois(n, mean) is the probability of exactly n events
# and ppois(n, mean) that of n or fewer; with lower.tail = FALSE ppois gives the
# probability of MORE than n events, so the probability of n or more events is
# ppois(n - 1, mean, lower.tail = FALSE)
n <- 14
mu <- 8
cat("Probability of", n, "events =", p15(dpois(n, mu)), "\n")
cat("Probability of", n, "or more events =", p15(ppois(n - 1, mu, lower.tail = FALSE)),
    "\n")
cat("Probability of", n, "or fewer events =", p15(ppois(n, mu)), "\n")

# The inverse: the mean at which n or more events has probability 0.025 and the mean at
# which n or fewer events has probability 0.025 (StatsDirect's Lower CL and Upper CL
# buttons with 95 in the confidence box, found by bisection on the cumulative
# probability). In R they come from the gamma distribution: n or more events with mean
# m has the same probability as the n-th event of a unit-rate process occurring by time
# m, so P(n or more) = pgamma(m, n) and P(n or fewer) = 1 - pgamma(m, n + 1).
lower <- qgamma(0.025, n)
upper <- qgamma(0.975, n + 1)
cat("Mean at which", n, "or more events has probability 0.025 =", s15(lower), "\n")
cat("Mean at which", n, "or fewer events has probability 0.025 =", s15(upper), "\n")
# a check against ppois; these are also the exact 95% limits for a Poisson count of n
cat("  check:", p15(ppois(n - 1, lower, lower.tail = FALSE)), p15(ppois(n, upper)),
    "\n")
cat("  exact 95% confidence interval for a Poisson count of", n, "=", s15(lower), "to",
    s15(upper), "\n")

# No events when the mean is 3: P(0) = exp(-mean)
cat("Probability of 0 events with mean 3 =", p15(dpois(0, 3)), "\n")
cat("  which is exp(-3) =", p15(exp(-3)), "\n")

# R's qpois inverts the other way, for the count: the smallest number of events whose
# probability of that many or fewer is at least the probability given
cat("Smallest count with probability at least 0.95 of that many or fewer, mean 8 =",
    qpois(0.95, mu), "\n")
