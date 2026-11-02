# 2 by k chi-square test with a test for linear trend: the StatsDirect help example
# (Armitage and Berry 1994, palatine tonsil size and carrier status for Strep.
# pyogenes in 1398 children) in R
tonsils <- c("not enlarged", "enlarged", "enlarged greatly")
carriers <- c(19, 29, 24)
non_carriers <- c(497, 560, 269)
counts <- rbind(Carriers = carriers, "Non-carriers" = non_carriers)
colnames(counts) <- tonsils
print(counts)

# R's standard test of independence. chisq.test applies its continuity correction to
# 2 by 2 tables only; it is switched off here because StatsDirect does not use it.
fit <- chisq.test(counts, correct = FALSE)
print(fit)

# The table as the report lays it out: each group's observed counts, total and
# percentage of successes, and the expected frequencies from chisq.test (row total
# times column total divided by the grand total)
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}
pc <- function(x) formatC(100 * x, digits = 2, format = "f", drop0trailing = TRUE)
total <- carriers + non_carriers
k <- length(carriers)
for (i in 1:k) {
  cat("Observed:", carriers[i], non_carriers[i], total[i], pc(carriers[i] / total[i]),
      "\n")
  cat("Expected:", six(fit$expected[1, i]), six(fit$expected[2, i]), "\n")
}
cat("Total:", sum(carriers), sum(non_carriers), sum(total),
    pc(sum(carriers) / sum(total)), "\n")
x2 <- unname(fit$statistic)
cat("Total chi-square =", six(x2), " |Chi| =", six(sqrt(x2)),
    paste0("(", k - 1, " DF) "), pv(fit$p.value), "\n")

# The chi-square for linear trend. prop.trend.test gives the statistic of the formula
# above; its scores are 1 to k unless others are given, as score = c(1, 2, 5) would.
trend <- prop.trend.test(carriers, total, score = 1:k)
print(trend)
x2_lin <- unname(trend$statistic)
# Its square root is a normal deviate; the report prints its absolute value under
# the |Chi| label, so this script does too.
cat("Chi-square for linear trend =", six(x2_lin), " |Chi| =", six(sqrt(x2_lin)),
    "(1 DF) ", pv(trend$p.value), "\n")

# What the trend leaves of the total chi-square tests departure from a linear trend
# on k - 2 degrees of freedom (Armitage and Berry 1994), so it needs k of 3 or more
x2_non <- x2 - x2_lin
cat("Remaining chi-square (non-linearity) =", six(x2_non), paste0("(", k - 2, " DF) "),
    pv(pchisq(x2_non, k - 2, lower.tail = FALSE)), "\n")
