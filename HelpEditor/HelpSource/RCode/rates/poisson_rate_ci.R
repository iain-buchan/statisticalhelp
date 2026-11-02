# Poisson rate confidence interval: the StatsDirect help example (14 events in 400
# person-years of observation, an incidence rate) in R
events <- 14
time_at_risk <- 400                       # 200 people for 1 year and 100 for 2 years

# poisson.test gives the exact limits for the rate (a whole number of events), the
# same as the chi-square quantile formulae above: its estimate is events / time
print(poisson.test(events, time_at_risk, conf.level = 0.95))

# The report's lines to 6 places. The limits are also the Poisson means whose
# cumulative probabilities reach the tails: qgamma(alpha / 2, events) and
# qgamma(1 - alpha / 2, events + 1), divided by the time at risk. With no events the
# lower limit is 0.
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
alpha <- 0.05
rate <- events / time_at_risk
lower <- if (events == 0) 0 else qgamma(alpha / 2, events) / time_at_risk
upper <- qgamma(1 - alpha / 2, events + 1) / time_at_risk
cat("Events observed =", events, "\n")
cat("Time spent at risk of event =", time_at_risk, "\n")
cat("Poisson (e.g. incidence) rate estimate =", six(rate), "\n")
cat("Exact 95% confidence interval =", six(lower), "to", six(upper), "\n")
