# Sample size for survival analysis: the StatsDirect help example (an invented trial
# of a new treatment against the usual one: median survival 12 months on the usual
# treatment, 18 months hoped for on the new one, two years of recruitment and one
# more year of follow-up) in R
ct <- 12                                  # median survival time in the control group
et <- 18                                  # median survival time, experimental group
at <- 24                                  # accrual time, when subjects are recruited
fut <- 12                                 # further follow-up time after recruitment
m <- 1                                    # controls per experimental subject
power <- 0.8
alpha <- 0.05                             # two sided

# Neither base R nor the survival package has a function for this sample size, so the
# topic's formula (Schoenfeld and Richter 1982) is written out here. Survival is taken
# to be exponential in both groups, so the hazard ratio is the inverse ratio of the
# median survival times. A subject recruited at a uniformly distributed time during
# accrual is followed for less than the whole study, and the power of the log-rank test
# depends on how many deaths are seen before the study ends.
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
size_survival <- function(ct, et, at, fut, m, power, alpha) {
  hr <- ct / et                           # experimental hazard relative to controls
  tbar <- (ct + et) / 2                   # the average of the two median survival times
  k <- log(2) * at / tbar
  pa <- (1 - exp(-k)) / k                 # probability of surviving the accrual period,
                                          # averaged over the recruitment times
  p <- 1 - pa * exp(-log(2) * fut / tbar) # probability of dying before the study ends
  n <- (qnorm(1 - alpha / 2) + qnorm(power))^2 * ((1 + 1 / m) / p) / log(hr)^2
  n <- floor(n) + 1                       # next whole number (one above an exact n)
  cat("median survival time for controls =", six(ct), "\n")
  cat("median survival time for experimental subjects =", six(et), "\n")
  cat("hazard ratio (experimental vs. control) =", six(hr), "\n")
  cat("accrual time for recruitment =", six(at), "\n")
  cat("additional follow-up time after recruitment =", six(fut), "\n")
  cat("alpha =", six(alpha), "\n")
  cat("power =", six(power), "\n")
  cat("Estimated minimum sample size =", n, "experimental subjects and",
      ceiling(n * m), "controls\n\n")
}
size_survival(ct, et, at, fut, m, power, alpha)

# The same trial recruiting for three years and closing as soon as recruitment ends:
# fewer of the deaths are seen by then, so more subjects are needed for the same power
size_survival(ct, et, at = 36, fut = 0, m, power, alpha)
