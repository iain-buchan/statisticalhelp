# Spearman's rho distribution: the StatsDirect help illustration (T = 52 for the 10
# pairs of rankings of the Spearman rank correlation example, Armitage and Berry 1994,
# p. 466, the test workbook's Nonparametric worksheet columns Career and Psychology;
# the T that cuts off an upper tail of 0.025; and rho = 0.4 for 20 pairs) in R
career <- c(4, 10, 3, 1, 9, 2, 6, 7, 8, 5)
psychology <- c(5, 8, 6, 2, 10, 3, 9, 4, 7, 1)
n <- length(career)                       # 10 pairs

fifteen <- function(x) formatC(x, digits = 15, format = "f", drop0trailing = TRUE)
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else paste("P =", formatC(p, digits = 4, format = "f"))
}

# The Hotelling-Pabst statistic T is the sum of the squared differences between the
# paired ranks (Spearman's score in the StatsDirect rank correlation report), and rho
# follows from it: rho = 1 - 6T/(n^3 - n)
score <- sum((career - psychology)^2)
rho <- 1 - 6 * score / (n^3 - n)
cat("T =", score, " Spearman's rho =", fifteen(rho), "\n")

# The distribution of T for n pairs ranked without ties when the two rankings are
# independent: each of the n! orderings of one ranking against the other is equally
# likely. For n = 5 all 120 orderings can be listed and T found for each
perms <- function(m) {
  if (m == 1) return(matrix(1))
  p <- perms(m - 1)
  do.call(rbind, lapply(1:m, function(i) cbind(i, p + (p >= i))))
}
t5 <- rowSums((perms(5) - rep(1:5, each = 120))^2)
print(table(t5))                          # T is even, from 0 to 40

# Listing the orderings is impractical for larger n (10! is 3,628,800), but the counts
# of orderings by T can be built up one position at a time: for each set of ranks
# already placed in the first positions, keep the counts of the partial orderings by
# their sum of squared differences so far; placing rank r at position i adds (i - r)^2
counts_by_T <- function(n) {
  top <- n * (n^2 - 1) / 3                # the largest T, for the reversed ranking
  counts <- vector("list", 2^n)           # one entry per set of ranks placed
  counts[[1]] <- c(1, numeric(top))       # nothing placed: one ordering with T = 0
  bit <- 2^(0:(n - 1))
  for (set in 0:(2^n - 2)) {
    so_far <- counts[[set + 1]]
    if (is.null(so_far)) next
    i <- sum(bitwAnd(set, bit) > 0) + 1   # the next position to fill
    for (r in (1:n)[bitwAnd(set, bit) == 0]) {
      added <- c(numeric((i - r)^2), so_far)[seq_len(top + 1)]
      to <- set + bit[r] + 1
      counts[[to]] <- if (is.null(counts[[to]])) added else counts[[to]] + added
    }
  }
  counts[[2^n]]                           # counts for T = 0, 1, ..., top
}
stopifnot(all(tabulate(t5 + 1, nbins = 41) == counts_by_T(5)))
upper <- function(t, n) {                 # P(T <= t): the upper tail probability of rho
  counts <- counts_by_T(n)
  sum(counts[seq_len(t + 1)]) / sum(counts)
}
counts <- counts_by_T(n)
at_most_T <- sum(counts[seq_len(score + 1)])
cat(format(at_most_T, big.mark = ","), "of the", format(sum(counts), big.mark = ","),
    "orderings give T of", score, "or less\n")

# The line the StatsDirect dialog reports for T = 52 with 10 pairs, to 15 decimal
# places: the probability of a rank correlation at least as large as 0.684848, which
# is a T no larger than 52
cat("P(Hotelling T ", score, ", n ", n, ") = ", fifteen(upper(score, n)),
    " upper tail\n", sep = "")
# This is the upper side P of the exact test in the Spearman rank correlation report;
# the distribution of rho is symmetrical about 0, so the two sided P is twice it
cat("Upper side", pv(upper(score, n)), "\n")
cat("Two sided", pv(2 * upper(score, n)), "\n")
# cor.test(career, psychology, method = "spearman", alternative = "greater") gives the
# same S = 52 and rho, but its P here is 0.0175, from a series approximation: its P for
# Spearman's rho is exact only with fewer than 10 pairs (see the 20 pairs below)

# The inverse: the largest T whose upper tail probability does not exceed the P
# entered, here 0.025 for 10 pairs. StatsDirect shows that T and its rho, and reports
# the probability it actually cuts off
critical <- function(P, n) {
  counts <- counts_by_T(n)
  tail <- cumsum(counts) / sum(counts)    # P(T <= t) for t = 0, 1, ..., top
  t <- seq(0, length(counts) - 1, by = 2) # the possible values of T are even
  max(t[tail[t + 1] <= P])
}
k <- critical(0.025, n)
rho_k <- 1 - 6 * k / (n^3 - n)
cat("T =", k, "  Spearman's rho =", fifteen(rho_k), "\n")
cat("Hotelling T (upper tail P 0.025, n ", n, ") = ", fifteen(upper(k, n)), "\n",
    sep = "")
cat("So a rho of", six(rho_k), "or more (a T of", k, "or less) is significant at the",
    "one sided 2.5% level\n")
# The next possible T cuts off more than 0.025, so its rho is not significant
cat("T = ", k + 2, " (rho = ", six(1 - 6 * (k + 2) / (n^3 - n)), "): P(Hotelling T ",
    k + 2, ", n ", n, ") = ", fifteen(upper(k + 2, n)), " upper tail\n", sep = "")

# For more than 10 pairs StatsDirect uses an Edgeworth series (Best and Roberts 1975),
# as cor.test does for 10 or more pairs, so for 20 pairs the two agree. Entering rho =
# 0.4 in StatsDirect gives T = (1 - 0.4) * 20 * (20^2 - 1) / 6 = 798; in R the series
# is reached through cor.test on two rankings with that T, here 1 to 20 against the
# same ranks with four pairs of positions swapped (swapping positions i and j adds
# 2 * (i - j)^2 to T: 2 * (361 + 36 + 1 + 1) = 798)
x <- 1:20
y <- x
y[c(1, 20)] <- y[c(20, 1)]
y[c(2, 8)] <- y[c(8, 2)]
y[c(3, 4)] <- y[c(4, 3)]
y[c(5, 6)] <- y[c(6, 5)]
score20 <- sum((x - y)^2)
cat("T =", score20, " Spearman's rho =", fifteen(1 - 6 * score20 / (20^3 - 20)), "\n")
print(cor.test(x, y, method = "spearman", alternative = "greater"))
p20 <- cor.test(x, y, method = "spearman", alternative = "greater")$p.value
cat("P(Hotelling T ", score20, ", n 20) = ", fifteen(p20), " upper tail\n", sep = "")
