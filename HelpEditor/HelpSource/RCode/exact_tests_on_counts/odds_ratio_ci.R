# Exact confidence interval for a 2 by 2 odds ratio: the StatsDirect help example
# (Thomas 1971, criminal convictions of monozygotic and dizygotic twins) in R
twins <- matrix(c(10, 2,
                  3, 15), 2, byrow = TRUE,
                dimnames = list(c("convicted", "not convicted"),
                                c("monozygotic", "dizygotic")))
print(twins)

# R's standard test conditions on both margins of the table. Its odds ratio is the
# conditional maximum likelihood estimate and its interval is the exact Fisher
# interval: each limit leaves 2.5% in one tail of the conditional distribution. Its
# P value is the two sided Fisher P and matches the report exactly. The estimate and
# the limits come from root finding to a modest tolerance, so their last digits differ
# from the report (the upper limit in its third significant figure); the report is
# exact to 6 places.
print(fisher.test(twins))

# The same quantities to full precision. Given the margins, the top left cell a can
# take the values lo to hi, with probabilities proportional to the central
# hypergeometric probabilities times (odds ratio)^a
a <- twins[1, 1]
m1 <- sum(twins[1, ])          # outcome present
n1 <- sum(twins[, 1])          # feature present
n0 <- sum(twins[, 2])          # feature absent
lo <- max(0, m1 - n0)
hi <- min(m1, n1)
x <- lo:hi
prob <- function(psi) {        # the distribution of a when the odds ratio is psi
  lw <- dhyper(x, n1, n0, m1, log = TRUE) + (x - lo) * log(psi)
  w <- exp(lw - max(lw))
  w / sum(w)
}
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}
odds <- twins[1, 1] * twins[2, 2] / (twins[1, 2] * twins[2, 1])
cat("Observed odds ratio =", six(odds), "\n")

# The conditional maximum likelihood estimate makes the expected value of a equal to
# the observed a. The root finding needs lo < a < hi: the estimate is 0 when a is at
# lo and infinite when a is at hi.
cmle <- if (a == lo) 0 else if (a == hi) Inf else
  exp(uniroot(function(l) sum(x * prob(exp(l))) - a, c(-30, 30), tol = 1e-12)$root)
cat("Conditional maximum likelihood estimate of odds ratio =", six(cmle), "\n")

# Exact Fisher limits: the lower limit is the odds ratio under which a or more is
# a 2.5% upper tail, the upper limit the one under which a or fewer is a 2.5% lower
# tail. The mid-P limits count half of the probability of a itself in each tail.
# The lower limit is 0 when a is at lo, the upper limit infinite when a is at hi.
alpha <- 0.05
limit <- function(upper, half) {
  if (!upper && a == lo) return(0)
  if (upper && a == hi) return(Inf)
  tail <- function(l) {
    p <- prob(exp(l))
    if (upper) sum(p[x < a]) + half * p[x == a] else sum(p[x > a]) + half * p[x == a]
  }
  exp(uniroot(function(l) tail(l) - alpha / 2, c(-30, 30), tol = 1e-12)$root)
}
cat("Exact Fisher 95% confidence interval =", six(limit(FALSE, 1)), "to",
    six(limit(TRUE, 1)), "\n")

# P values at an odds ratio of 1. The one sided Fisher P is the smaller tail; the two
# sided Fisher P adds up the tables no more probable than the observed one, as
# fisher.test does (with an allowance for rounding in equal probabilities).
p1 <- prob(1)
up <- sum(p1[x >= a])
down <- sum(p1[x <= a])
cat("Exact Fisher one sided ", pv(min(up, down)), ", two sided ",
    pv(sum(p1[p1 <= p1[x == a] * (1 + 1e-7)])), "\n", sep = "")

# The mid-P versions count half of the probability of the observed table, and the
# two sided mid-P is twice the one sided value
cat("Exact mid-P 95% confidence interval =", six(limit(FALSE, 0.5)), "to",
    six(limit(TRUE, 0.5)), "\n")
mid <- min(up, down) - 0.5 * p1[x == a]
cat("Exact mid-P one sided ", pv(mid), ", two sided ", pv(min(2 * mid, 1)), "\n",
    sep = "")
