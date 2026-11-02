# Friedman test: the StatsDirect help example (Conover 1999, p. 372) in R
grass <- matrix(c(4,   3,   2,   1,
                  4,   2,   3,   1,
                  3,   1.5, 1.5, 4,
                  3,   1,   2,   4,
                  4,   2,   1,   3,
                  2,   2,   2,   4,
                  1,   3,   2,   4,
                  2,   4,   1,   3,
                  3.5, 1,   2,   3.5,
                  4,   1,   3,   2,
                  4,   2,   3,   1,
                  3.5, 1,   2,   3.5), ncol = 4, byrow = TRUE,
                dimnames = list(NULL, paste("Grass", 1:4)))
b <- nrow(grass)                          # blocks (home owners)
k <- ncol(grass)                          # treatments (grasses)

# R's standard test. Its chi-squared is StatsDirect's T1, and its P (0.044) is
# from the chi-square distribution. StatsDirect gives P from T2, the F statistic
# of Iman and Davenport (1980), which is the better approximation.
print(friedman.test(grass))

# Ranks within blocks, T1 allowing for ties, and T2 with its P
r <- t(apply(grass, 1, rank))
R <- colSums(r)                           # rank sum for each treatment
A1 <- sum(r^2)
T1 <- (k - 1) * sum((R - b * (k + 1) / 2)^2) / (A1 - b * k * (k + 1)^2 / 4)
T2 <- (b - 1) * T1 / (b * (k - 1) - T1)
cat("Mean rank:", round(R / b, 2), "\n")
cat("Sum of squares of ranks =", A1, "\n")
cat(sprintf("T1 (chi-square) = %.6f   T2 (F) = %.6f   P = %.4f\n", T1, T2,
            pf(T2, k - 1, (b - 1) * (k - 1), lower.tail = FALSE)))

# All pairwise comparisons (Conover): differences between rank sums against t
# with (b - 1)(k - 1) degrees of freedom
df <- (b - 1) * (k - 1)
se <- sqrt(2 * (b * A1 - sum(R^2)) / df)
cat(sprintf("Critical t (%d df) = %.6f   critical difference = %.6f\n", df,
            qt(0.975, df), qt(0.975, df) * se))
pairs <- combn(k, 2)
for (j in seq_len(ncol(pairs))) {
  d <- R[pairs[1, j]] - R[pairs[2, j]]
  cat(sprintf("%s vs %s:  difference = %g   P = %.4f\n", colnames(grass)[pairs[1, j]],
              colnames(grass)[pairs[2, j]], d, 2 * pt(-abs(d) / se, df)))
}

# Cochran's Q: the same menu function with data coded 0 and 1 (Conover 1999:
# whether each of three sportsmen predicted the results of 12 games)
games <- matrix(c(1, 1, 1,  1, 1, 1,  0, 1, 0,  1, 1, 0,  0, 0, 0,  1, 1, 1,
                  1, 1, 1,  1, 1, 0,  0, 0, 1,  0, 1, 0,  1, 1, 1,  1, 1, 1),
                ncol = 3, byrow = TRUE)
kq <- ncol(games)
Q <- kq * (kq - 1) * sum((colSums(games) - sum(games) / kq)^2) /
  sum(rowSums(games) * (kq - rowSums(games)))
cat(sprintf("Cochran Q = %g   df = %d   P = %.4f\n", Q, kq - 1,
            pchisq(Q, kq - 1, lower.tail = FALSE)))
