# 2 by 2 chi-square test: the StatsDirect help example (Armitage and Berry 1994, deaths
# among 257 patients given treatment A and 244 given treatment B) in R
counts <- matrix(c(41, 216, 64, 180), 2, byrow = TRUE,
                 dimnames = list(Treatment = c("A", "B"), Outcome = c("Dead", "Alive")))
print(addmargins(counts))
n <- sum(counts)

# R's standard test: chisq.test applies Yates' continuity correction to a 2 by 2
# table by default and leaves it out when correct = FALSE. The expected values are
# the row total times the column total divided by n.
uncorrected <- chisq.test(counts, correct = FALSE)
yates <- chisq.test(counts, correct = TRUE)
print(uncorrected)
print(yates)

six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}
x2 <- unname(uncorrected$statistic)
cat("Expected values:", six(t(uncorrected$expected)), "\n")
cat("Uncorrected Chi-square =", six(x2), " ", pv(uncorrected$p.value), "\n")
cat("Yates-corrected Chi-square =", six(unname(yates$statistic)), " ",
    pv(yates$p.value), "\n")

# Measures of association: Pearson's coefficient of contingency, and Cramer's V
# carrying the sign of ad - bc (negative here: fewer deaths with A than expected)
a <- counts[1, 1]
b <- counts[1, 2]
c <- counts[2, 1]
d <- counts[2, 2]
margins <- prod(rowSums(counts), colSums(counts))
cat("Pearson's contingency =", six(sqrt(x2 / (x2 + n))), "\n")
cat("Cramer's V (signed) =", six((a * d - b * c) / sqrt(margins)), "\n")

# Odds ratio analysis (a case-control study): the sample odds ratio ad/bc with
# Woolf's (logit) interval from the standard error of its logarithm
or <- a * d / (b * c)
z <- qnorm(0.975)
se <- sqrt(1 / a + 1 / b + 1 / c + 1 / d)
cat("Odds Ratio =", six(or), "\n")
cat("Approximate 95% confidence interval =", six(exp(log(or) - z * se)), "to",
    six(exp(log(or) + z * se)), "\n")

# fisher.test: its P value is the two sided P by summation of the tables no more
# probable than the one observed, and alternative = "less" gives the one sided P for
# the lower tail (41 or fewer deaths with A). Its interval is the exact conditional
# one, agreeing with the report to four decimal places, and its estimate is the
# conditional maximum likelihood odds ratio rather than the sample odds ratio above.
print(fisher.test(counts))
cat("Exact Fisher one sided ", pv(fisher.test(counts, alternative = "less")$p.value),
    ", two sided ", pv(fisher.test(counts)$p.value), "\n", sep = "")

# The exact limits to six places, and the mid-P versions: with the margins fixed,
# a has the hypergeometric distribution weighted by psi^a at odds ratio psi. A limit
# is the psi at which one tail is 2.5%, counting the probability of the observed a
# in full (Fisher) or by half (mid-P). The one sided P is the smaller tail at psi = 1
# and the two sided mid-P is twice it.
k <- max(0, a + b - (b + d)):min(a + b, a + c)     # the possible values of a
pr <- function(psi) {
  lw <- dhyper(k, a + c, b + d, a + b, log = TRUE) + k * log(psi)   # on the log scale
  w <- exp(lw - max(lw))
  w / sum(w)
}
lower <- function(psi, w) sum(pr(psi)[k < a]) + w * pr(psi)[k == a]
upper <- function(psi, w) sum(pr(psi)[k > a]) + w * pr(psi)[k == a]
limit <- function(tail, w) {
  uniroot(function(psi) tail(psi, w) - 0.025, c(0.001, 1000), tol = 1e-10)$root
}
cat("Fisher exact 95% confidence interval =", six(limit(upper, 1)), "to",
    six(limit(lower, 1)), "\n")
cat("mid-P exact 95% confidence interval =", six(limit(upper, 0.5)), "to",
    six(limit(lower, 0.5)), "\n")
mid <- min(lower(1, 0.5), upper(1, 0.5))
cat("Exact mid-P one sided ", pv(mid), ", two sided ", pv(min(1, 2 * mid)), "\n",
    sep = "")
