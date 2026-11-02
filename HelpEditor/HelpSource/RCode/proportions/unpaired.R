# Two independent proportions: the StatsDirect help example (Armitage and Berry 1994,
# deaths under two treatments: 41 of 257 patients given A and 64 of 244 given B) in R
n1 <- 257
r1 <- 41
n2 <- 244
r2 <- 64

# R's standard test. Without the continuity correction its chi-square is the square of
# the report's z (7.9789 = 2.824689^2) and its P is the approximate two sided P. Its
# confidence interval is the simple normal one, not the interval the report gives.
print(prop.test(c(r1, r2), c(n1, n2), correct = FALSE))

six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}
p1 <- r1 / n1
p2 <- r2 / n2
N <- n1 + n2
cat("Total 1 = ", n1, ", response 1 = ", r1, "\n", sep = "")
cat("Proportion 1 =", six(p1), "\n")
cat("Total 2 = ", n2, ", response 2 = ", r2, "\n", sep = "")
cat("Proportion 2 =", six(p2), "\n")
cat("Proportion difference =", six(p1 - p2), "\n")

# The confidence interval of Miettinen and Nurminen (1985): the two values of the
# difference (theta) at which the score statistic equals plus or minus z, the two
# proportions being estimated with the constraint p1 - p2 = theta and the variance
# scaled by N / (N - 1). The constrained estimate of p2 is the root of a cubic, in the
# closed form given by Farrington and Manning (1990). The closed form breaks down only
# where a limit is -1 or 1 itself (all responses in one sample and none in the other);
# the program then prints that limit.
if (r1 == 0 && r2 == n2 || r1 == n1 && r2 == 0) {
  stop("a limit of the interval is -1 or 1: not handled here")
}
p2_given <- function(theta) {
  L3 <- N
  L2 <- (n1 + 2 * n2) * theta - N - (r1 + r2)
  L1 <- (n2 * theta - N - 2 * r2) * theta + r1 + r2
  L0 <- r2 * theta * (1 - theta)
  q <- L2^3 / (3 * L3)^3 - L1 * L2 / (6 * L3^2) + L0 / (2 * L3)
  p <- sqrt(L2^2 / (3 * L3)^2 - L1 / (3 * L3))
  if (q < 0) p <- -p
  2 * p * cos((pi + acos(q / p^3)) / 3) - L2 / (3 * L3)
}
score <- function(theta) {
  q2 <- p2_given(theta)
  q1 <- q2 + theta
  (p1 - p2 - theta) / sqrt((q1 * (1 - q1) / n1 + q2 * (1 - q2) / n2) * N / (N - 1))
}
z <- qnorm(0.975)
lower <- uniroot(function(t) score(t) - z, c(-1 + 1e-9, p1 - p2), tol = 1e-12)$root
upper <- uniroot(function(t) score(t) + z, c(p1 - p2, 1 - 1e-9), tol = 1e-12)$root
cat("Approximate (Miettinen) 95% confidence interval =", six(lower), "to", six(upper),
    "\n")

# Exact two sided mid-P. With all four margins fixed, the number of deaths under A has
# a hypergeometric distribution: 105 deaths among the 501 patients, 257 of them on A.
# Each tail's mid-P is the probability of the tables up to and including the observed
# one, the observed table counting half (0.0024 for the lower tail here, the one sided
# mid-P of the program's Fisher's exact test); the two sided mid-P is twice the smaller
# tail, the central convention, as the Fisher's exact test report also prints it. The
# other common convention, all the less probable tables plus half the observed one,
# gives 0.0051 here
x <- 0:min(n1, r1 + r2)
prob <- dhyper(x, r1 + r2, N - r1 - r2, n1)
obs <- which(x == r1)
lower_tail <- sum(prob[1:obs]) - prob[obs] / 2
upper_tail <- sum(prob[obs:length(x)]) - prob[obs] / 2
cat("Exact two sided (mid)", pv(min(1, 2 * min(lower_tail, upper_tail))), "\n")

# The normal approximation, with the pooled proportion in the standard error
p <- (r1 + r2) / N
se <- sqrt(p * (1 - p) * (1 / n1 + 1 / n2))
deviate <- (p1 - p2) / se
cat("Standard error of proportion difference =", six(se), "\n")
cat("Standard normal deviate (z) =", six(deviate), "\n")
cat("Approximate two sided", pv(2 * pnorm(-abs(deviate))), "\n")
cat("Approximate one sided", pv(pnorm(-abs(deviate))), "\n")
