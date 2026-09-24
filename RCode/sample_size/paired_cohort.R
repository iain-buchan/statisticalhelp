# Sample size for a paired cohort study: the StatsDirect help example (an invented study
# of exposed workers each matched with an unexposed worker, 10% of controls with the
# event, a relative risk of 2 to be detected) in R
p0 <- 0.1                                 # event rate in the control group
rr <- 2                                   # relative risk to be detected
p1 <- rr * p0                             # event rate in the experimental group, 0.2
r <- 0.2                                  # correlation for failure within a pair
power <- 0.9
alpha <- 0.05                             # two sided

# Base R's power.prop.test() is for two independent groups and takes no account of the
# pairing, so the topic's formula (Dupont 1990, from Breslow and Day 1980) is used: the
# probabilities of the two kinds of discordant pair, the proportion of discordant pairs
# in which the experimental subject is the one who fails, then the number of pairs,
# rounded up. It needs p1 to differ from p0 and both discordant probabilities to be
# positive (r not too large), which is why StatsDirect refuses some inputs.
z_alpha <- qnorm(1 - alpha / 2)           # two sided: 1.959964 for alpha 0.05
z_beta <- qnorm(power)                    # 1.281552 for 90% power
s <- r * sqrt(p1 * (1 - p1) * p0 * (1 - p0))
py <- p1 * (1 - p0) - s                   # experimental subject fails, control does not
px <- p0 * (1 - p1) - s                   # control fails, experimental subject does not
stopifnot(p1 != p0, px > 0, py > 0)
pa <- py / (px + py)
n <- (z_alpha / 2 + z_beta * sqrt(pa * (1 - pa)))^2 / ((pa - 0.5)^2 * (px + py))
cat("Event rate in control group =", p0, "\n")
cat("Event rate in experimental group =", p1, "\n")
cat("Correlation for failure between experimental and control subjects =", r, "\n")
cat("Alpha =", alpha, "\n")
cat("Power =", power, "\n")
cat("Estimated minimum sample size =", ceiling(n), "pairs\n")
