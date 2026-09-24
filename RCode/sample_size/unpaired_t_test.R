# Sample size for an unpaired t test: the StatsDirect help example (a planned trial
# of a treatment expected to lower systolic blood pressure by 5 mmHg, SD 12 mmHg,
# made up for the help) in R
delta <- 5          # difference in population means worth detecting
sd <- 12            # estimated standard deviation within each group
power <- 0.9
alpha <- 0.05       # two sided
m <- 1              # controls per experimental subject

# power.t.test solves the same two sided non-central t power equation as the report,
# but only for two groups of the same size (m = 1). Its n is per group and is not a
# whole number: the report gives the next whole number, the smallest n whose power
# reaches the target. strict = TRUE counts a rejection in either tail as power, as
# the report does; here the lower tail adds almost nothing.
print(power.t.test(delta = delta, sd = sd, sig.level = alpha, power = power,
                   type = "two.sample", alternative = "two.sided", strict = TRUE))

# For m controls per experimental subject the power of the unpaired t test with n
# experimental subjects (n(m + 1) - 2 degrees of freedom) comes from the non-central
# t distribution, and the report gives the smallest whole n whose power reaches the
# target. The search starts from n = 2, or from the smallest n giving at least one
# degree of freedom when m is below 0.5.
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
size_unpaired <- function(delta, sd, m, power, alpha) {
  stopifnot(delta != 0, m > 0)  # a zero difference or ratio has no finite answer
  power_of <- function(n) {
    df <- n * (m + 1) - 2
    ncp <- abs(delta) / (sd * sqrt((1 + 1 / m) / n))
    tcrit <- qt(1 - alpha / 2, df)
    pt(tcrit, df, ncp, lower.tail = FALSE) + pt(-tcrit, df, ncp)
  }
  n <- max(2, ceiling(3 / (m + 1)))
  while (power_of(n) < power) n <- n + 1
  cat("Alpha =", six(alpha), "\n")
  cat("Power =", six(power), "\n")
  cat("Difference between means =", six(delta), "\n")
  cat("Standard deviation =", six(sd), "\n")
  cat("Controls per experimental subject", six(m), "\n")
  # the report rounds the control count down; the power uses m n controls exactly
  cat("Estimated minimum sample size =", n, "experimental subjects and",
      floor(m * n), "controls.\n")
  cat("Degrees of freedom =", six(n * (m + 1) - 2), "\n")
  cat("Power at this sample size =", six(power_of(n)), "\n\n")
}
size_unpaired(delta, sd, m, power, alpha)

# The same trial with two controls for each experimental subject: fewer experimental
# subjects are needed, but more subjects in all
size_unpaired(delta, sd, m = 2, power, alpha)
