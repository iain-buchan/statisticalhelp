# Fisher's exact test: the StatsDirect help example (Armitage and Berry 1994, p. 138,
# malocclusion of teeth by method of feeding infants) in R
teeth <- matrix(c(4, 16,
                  1, 21), 2, byrow = TRUE,
                dimnames = list(feeding = c("Breast fed", "Bottle fed"),
                                teeth = c("Normal", "Malocclusion")))
print(addmargins(teeth))

# R's standard test. Its two sided P is the report's "by summation": the probabilities
# of every table with the same margins that is no more likely than the observed one are
# added up. R also estimates the odds ratio conditional on the margins, with a
# confidence interval, which the report does not show. For a one sided P use
# alternative = "greater" (upper tail for the top left count) or "less" (lower tail).
print(fisher.test(teeth))

# The report's other lines come from the hypergeometric distribution of the top left
# count A given the margins: dhyper(x, m, n, k) is the probability that A = x when m
# is the first column total, n the second column total and k the first row total.
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}
counts <- as.vector(t(teeth))        # a, b, c, d row by row
# StatsDirect turns the table so that its A is the smaller of the two diagonal counts
# (for this example it only transposes the table, which changes nothing). The P values
# are the same whichever way the table is turned; the expectation is that of A.
if (counts[1] > counts[4]) counts <- rev(counts)
a <- counts[1]
m <- counts[1] + counts[3]
n <- counts[2] + counts[4]
k <- counts[1] + counts[2]
expected <- k * m / (m + n)
cat("Expectation of A =", six(expected), "\n")
x <- max(0, k - n):min(k, m)         # the values A can take with these margins
pr <- dhyper(x, m, n, k)
observed <- pr[x == a]

# One sided P: the tail in which A lies, upper when A exceeds its expectation. Turning
# the table never changes which side of its expectation A lies (A - E(A) = (ad - bc)/n).
if (a > expected) {
  tail <- "(upper tail)"
  one_sided <- sum(pr[x >= a])
} else {
  tail <- "(lower tail)"
  one_sided <- sum(pr[x <= a])
}
cat("One sided", tail, pv(one_sided), "  (doubled one sided",
    paste0(pv(min(1, 2 * one_sided)), ")"), "\n")

# Two sided P by summation (Bailey 1977), as fisher.test gives it: the tables no more
# probable than the observed one, allowing a little rounding in the comparison
two_sided <- sum(pr[pr <= observed * (1 + 1e-7)])
cat("Two sided (by summation)", pv(min(1, two_sided)), "\n")

# Mid-P: only half the probability of the observed table is counted in its tail
mid_p <- one_sided - observed / 2
cat("One sided mid-", pv(mid_p), "\n", sep = "")
cat("Two sided mid-", pv(min(1, 2 * mid_p)), "\n", sep = "")
