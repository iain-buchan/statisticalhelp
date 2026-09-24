# Fisher's exact test (expanded): the StatsDirect help example (Armitage and Berry 1994,
# p. 138, malocclusion of the teeth by the type of feeding received by infants) in R
teeth <- matrix(c(4, 16, 1, 21), 2, byrow = TRUE,
                dimnames = list(fed = c("breast", "bottle"),
                                teeth = c("normal", "malocclusion")))
print(addmargins(teeth))

# R's standard test. Its two sided P sums the probabilities of every table with the
# observed margins that is no more probable than the observed table (0.1745), the
# definition of Bailey (1977) that the report uses. With alternative = "greater" the P
# value is the probability of the observed count of breast-fed infants with normal
# teeth (the top left cell, A) or of a larger one: the upper tail of A (0.1435).
# fisher.test also gives a conditional odds ratio and its confidence interval, which
# the expanded report leaves out.
print(fisher.test(teeth))
print(fisher.test(teeth, alternative = "greater"))

six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}

# Given the margins, A is hypergeometric: the k = 20 breast-fed infants are a sample of
# the 42, of whom m = 5 have normal teeth and n = 37 malocclusion, and A counts the
# normal teeth in the sample
a <- teeth[1, 1]
m <- sum(teeth[, 1])
n <- sum(teeth[, 2])
k <- sum(teeth[1, ])
cat("Expectation of A =", six(k * m / (m + n)), "\n")

# The whole conditional distribution to 14 places, as the report tabulates it: for each
# possible A the probability of that value (individual P, dhyper), of that value or a
# smaller one (lower tail, phyper) and of that value or a larger one (upper tail: the
# probability above A - 1, which phyper gives with lower.tail = FALSE). Each individual
# probability is a ratio of counts of tables, choose(m, A) * choose(n, k - A) divided
# by choose(m + n, k).
A <- max(0, k - n):min(k, m)
prob <- dhyper(A, m, n, k)
lower <- phyper(A, m, n, k)
upper <- phyper(A - 1, m, n, k, lower.tail = FALSE)
fourteen <- function(x) formatC(x, digits = 14, format = "f")
cat("A  Lower Tail  Individual P  Upper Tail\n")
for (i in seq_along(A)) {
  cat(A[i], fourteen(lower[i]), fourteen(prob[i]), fourteen(upper[i]), "\n")
}

# One sided P: the tail that holds the observed A, the upper tail here as A = 4 is
# above its expectation; doubling it is the other common two sided P. The two sided P
# by summation adds up every value of A that is no more probable than the observed one
# (a tolerance of one part in ten million allows for rounding in the probabilities).
p_obs <- prob[A == a]
if (a > k * m / (m + n)) {
  tail <- "(upper tail)"
  p1 <- upper[A == a]
} else {
  tail <- "(lower tail)"
  p1 <- lower[A == a]
}
p2 <- sum(prob[prob <= p_obs * (1 + 1e-7)])
cat("One sided ", tail, " ", pv(p1), "   (doubled: ", pv(min(1, 2 * p1)), ")\n",
    sep = "")
cat("Two sided (by summation)", pv(p2), "\n")

# Mid-P: the one sided P less half the probability of the observed table (Armitage and
# Berry 1994), and twice that, at most 1, for the two sided mid-P
mid_p <- p1 - p_obs / 2
cat("One sided mid-", pv(mid_p), "\n", sep = "")
cat("Two sided mid-", pv(min(1, 2 * mid_p)), "\n", sep = "")
