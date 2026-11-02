# Follow-up (actuarial) life table: the StatsDirect help example (Armitage and Berry
# 1994, the survival of 374 patients after surgery for a malignancy; the test workbook's
# Survival worksheet columns Year, Died and Withdrawn) in R
year <- 0:10
died <- c(90, 76, 51, 25, 20, 7, 4, 1, 3, 2, 21)
withdrawn <- c(0, 0, 0, 12, 5, 9, 9, 3, 5, 5, 26)
alive_at_start <- 374         # StatsDirect's default: the deaths plus the withdrawals

# Base R and the survival package have no actuarial life table (survfit() gives the
# Kaplan-Meier estimate, which treats each death time individually), so the Berkson and
# Gage table is computed from its formulae: the number at risk in an interval is reduced
# by half the withdrawals of that interval, and the probability of surviving beyond an
# interval is the product of the interval survival probabilities up to it. The last
# interval is open ended, so its probability of death is not estimated and the table
# stops at the survival to its start (StatsDirect prints * for these cells)
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
star <- function(x) ifelse(is.na(x), "*", six(x))
k <- length(year)
closed <- 1:(k - 1)
at_risk <- alive_at_start - c(0, cumsum(died + withdrawn)[-k])
adj_at_risk <- at_risk - withdrawn / 2
q <- died[closed] / adj_at_risk[closed]
p <- 1 - q
lx <- cumprod(p)
interval <- c(paste(year[closed], "to", year[-1]), paste(year[k], "up"))
cat("Follow-up life table\n")
print(data.frame(Interval = interval, Deaths = died, Withdrawn = withdrawn,
                 "At risk" = at_risk, "Adj. at risk" = star(c(adj_at_risk[closed], NA)),
                 "P(death)" = star(c(q, NA)), check.names = FALSE), row.names = FALSE)

# Greenwood's formula gives the variance of lx, and the report's column "SD of lx%" is
# 100 times its square root. The confidence interval is on the scale of log(-log(lx)),
# the transformation of Kalbfleisch and Prentice (1980), so that its limits stay between
# 0 and 100%. lx and its interval in each row are the survival to the start of the row.
# These lines hold while some die and some survive in every closed interval (0 < q < 1),
# as here; StatsDirect prints * for the cells that are not estimable otherwise
var_lx <- lx^2 * cumsum(q / (adj_at_risk[closed] * p))
s <- sqrt(var_lx) / (-lx * log(lx))
z <- qnorm(0.975)
lower <- 100 * lx^exp(z * s)
upper <- 100 * lx^exp(-z * s)
ci <- c("* to *", paste(six(lower), "to", six(upper)))
print(data.frame(Interval = interval, "P(survival)" = star(c(p, NA)),
                 "Survivors (lx%)" = six(100 * c(1, lx)),
                 "SD of lx%" = star(c(NA, 100 * sqrt(var_lx))),
                 "95% CI for lx%" = ci, check.names = FALSE), row.names = FALSE)
