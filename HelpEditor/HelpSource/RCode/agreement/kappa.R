# Agreement of categorical measurements: the StatsDirect help example (Altman 1991,
# the RAST and MAST tests of 363 sera graded in five categories) in R
grades <- c("negative", "weak", "moderate", "high", "very high")
counts <- matrix(c(86, 3, 14, 0, 2,
                   26, 0, 10, 4, 0,
                   20, 2, 22, 4, 1,
                   11, 1, 37, 16, 14,
                   3, 0, 15, 24, 48), 5, 5, byrow = TRUE,
                 dimnames = list(MAST = grades, RAST = grades))
print(counts)

# Base R has no kappa function, so the statistics are computed from the table as the
# formulae above give them: the proportions in the cells, the row and column margins
# and the proportion of sera the two tests graded alike
six <- function(x) if (is.na(x)) "*" else formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (is.na(p)) return("P = *")
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}
pc2 <- function(x) paste0(formatC(100 * x, digits = 2, format = "f"), "%")
g <- nrow(counts)
n <- sum(counts)
p <- counts / n
row_p <- rowSums(p)
col_p <- colSums(p)
po <- sum(diag(p))

# Cohen's kappa, with the standard error for testing kappa = 0 (se_0) and the
# standard error of Fleiss, Cohen and Everitt (1969) for the confidence interval (se)
pe <- sum(row_p * col_p)
kappa <- (po - pe) / (1 - pe)
se0 <- sqrt(pe + pe^2 - sum(row_p * col_p * (row_p + col_p))) / ((1 - pe) * sqrt(n))
a <- sum(diag(p) * (1 - pe - (row_p + col_p) * (1 - po))^2)
b <- 0
for (i in 1:g) for (j in 1:g) if (i != j) b <- b + p[i, j] * (col_p[i] + row_p[j])^2
se <- sqrt((a + (1 - po)^2 * b - (po * pe - 2 * pe + po)^2) / (n * (1 - pe)^4))
z <- qnorm(0.975)
cat("Cohen's kappa (unweighted)\n")
cat("Observed agreement =", pc2(po), "\n")
cat("Expected agreement =", pc2(pe), "\n")
cat("Kappa = ", six(kappa), " (se_0 = ", six(se0), ", se = ", six(se), ")\n", sep = "")
cat("95% confidence interval =", six(max(-1, kappa - z * se)), "to", six(min(1, kappa + z * se)), "\n")
cat("z (for k = 0) =", six(kappa / se0), " ",
    pv(pnorm(kappa / se0, lower.tail = FALSE)), "\n")

# Weighted kappa with the default linear weights: a disagreement of one grade counts
# three quarters of full agreement, and so on down to zero across the whole scale
w <- 1 - abs(outer(1:g, 1:g, "-")) / (g - 1)
print(w)
pow <- sum(w * p)
pew <- sum(w * outer(row_p, col_p))
kappaw <- (pow - pew) / (1 - pew)
wi <- as.vector(w %*% col_p)     # the mean weight of each row grade
wj <- as.vector(t(w) %*% row_p)  # and of each column grade
se0w <- sqrt(sum(outer(row_p, col_p) * (w - outer(wi, wj, "+"))^2) - pew^2) /
  ((1 - pew) * sqrt(n))
s <- 0
for (i in 1:g) for (j in 1:g) {
  s <- s + p[i, j] * (w[i, j] * (1 - pew) - (wi[i] + wj[j]) * (1 - pow))^2
}
sew <- sqrt((s - (pow * pew - 2 * pew + pow)^2) / (n * (1 - pew)^4))
cat("Cohen's kappa (weighted by 1-abs(i-j)/(k-1))\n")
cat("Observed agreement =", pc2(pow), "\n")
cat("Expected agreement =", pc2(pew), "\n")
cat("Kappa = ", six(kappaw), " (se_0 = ", six(se0w), ", se = ", six(sew), ")\n",
    sep = "")
cat("95% confidence interval for kappa =", six(kappaw - z * sew), "to",
    six(kappaw + z * sew), "\n")
cat("z (for kw = 0) =", six(kappaw / se0w), " ",
    pv(pnorm(kappaw / se0w, lower.tail = FALSE)), "\n")

# Invert the Pearson goodness-of-fit statistic for the three cells in
# Donner and Eliasziw's common correlation model, pooling disagreements.
kappa_donner_interval <- function(counts, confidence) {
  if (nrow(counts) != 2 || ncol(counts) != 2) return(NULL)
  observed <- round(c(counts[1, 1], counts[1, 2] + counts[2, 1], counts[2, 2]))
  if (observed[1] + observed[2] == 0 || observed[2] + observed[3] == 0) return(NULL)
  n <- sum(observed)
  p <- (observed[1] + observed[2] / 2) / n
  q <- 1 - p
  pq <- p * q
  minimum <- -min(p / q, q / p)
  estimate <- max(minimum, 1 - observed[2] / (2 * n * pq))
  critical <- qchisq(confidence, 1)
  nonzero <- observed > 0
  lack_of_fit <- function(pi) {
    expected <- n * pmax(0, c(p^2 + pi * pq, 2 * pq * (1 - pi), q^2 + pi * pq))
    sum((observed[nonzero] - expected[nonzero])^2 / expected[nonzero]) +
      sum(expected[!nonzero]) - critical
  }
  lower <- if (minimum < estimate && lack_of_fit(minimum) > 0)
    uniroot(lack_of_fit, c(minimum, estimate), tol = 1e-12)$root else minimum
  upper <- if (observed[2] > 0)
    uniroot(lack_of_fit, c(estimate, 1), tol = 1e-12)$root else 1
  c(lower, upper)
}

# Scott's pi: chance agreement from the margins averaged over the two tests
margin <- (row_p + col_p) / 2
spe <- sum(margin^2)
cat("Scott's pi\n")
cat("Observed agreement =", pc2(po), "\n")
cat("Expected agreement =", pc2(spe), "\n")
cat("Pi =", six((po - spe) / (1 - spe)), "\n")
donner <- kappa_donner_interval(counts, 0.95)
if (!is.null(donner))
  cat("95% Donner-Eliasziw confidence interval =", six(donner[1]), "to", six(donner[2]), "\n")

# Gwet's AC1, with its variance (Gwet 2008)
pe1 <- sum(margin * (1 - margin)) / (g - 1)
ac1 <- (po - pe1) / (1 - pe1)
pog <- sum(diag(p) * (1 - margin))
soma <- sum(p * (1 - outer(margin, margin, "+") / 2)^2)
v <- (po * (1 - po) - 4 * (1 - ac1) * (pog / (g - 1) - po * pe1) +
        4 * (1 - ac1)^2 * (soma / (g - 1)^2 - pe1^2)) / (n * (1 - pe1)^2)
cat("Gwet's AC1\n")
cat("Observed agreement =", pc2(po), "\n")
cat("Chance-independent agreement =", pc2(pe1), "\n")
cat("AC1 = ", six(ac1), " (se = ", six(sqrt(v)), ")\n", sep = "")
cat("95% confidence interval =", six(max(-1, ac1 - z * sqrt(v))), "to", six(min(1, ac1 + z * sqrt(v))),
    "\n")

# Maxwell's test of marginal homogeneity: the differences between the row and column
# totals against their covariance matrix. Two grades are in the same group when a subject
# was given one of them by one rater and the other by the other rater, or when they are
# joined in that way through other grades. The differences of a group add up to nothing,
# so its last grade is left out; a grade that was never taken for another adds nothing
d <- rowSums(counts) - colSums(counts)
V <- -(counts + t(counts))
diag(V) <- rowSums(counts) + colSums(counts) - 2 * diag(counts)
group <- seq_len(g)
repeat {
  joined <- FALSE
  for (i in 1:g) for (j in 1:g) if (counts[i, j] + counts[j, i] > 0 && group[i] != group[j]) {
    from <- max(group[i], group[j])
    to <- min(group[i], group[j])
    group[group == from] <- to
    joined <- TRUE
  }
  if (!joined) break
}
x2 <- 0
df <- 0
for (first in unique(group)) {
  member <- which(group == first)
  keep <- member[-length(member)]
  if (length(keep) == 0) next
  x2 <- x2 + as.numeric(t(d[keep]) %*% solve(V[keep, keep, drop = FALSE]) %*% d[keep])
  df <- df + length(keep)
}
if (df > 0) {
  cat("Marginal homogeneity (Maxwell) chi-square =", six(x2), " df =", df, " ",
      pv(pchisq(x2, df, lower.tail = FALSE)), "\n")
} else {
  cat("Marginal homogeneity (Maxwell) chi-square not calculated: the raters never disagree\n")
}

# The generalised McNemar (Bowker) test of symmetry, as mcnemar.test(counts) computes
# it, except that the pair of cells that are both empty (weak MAST, very high RAST and
# the reverse) is left out rather than making the statistic undefined: it adds nothing
# to the statistic, and is not counted in the degrees of freedom
above <- counts[upper.tri(counts)]
below <- t(counts)[upper.tri(counts)]
filled <- above + below > 0
x2m <- sum((above - below)[filled]^2 / (above + below)[filled])
dfm <- sum(filled)
if (dfm > 0) {
  cat("Symmetry (generalised McNemar) chi-square =", six(x2m), " df =", dfm, " ",
      pv(pchisq(x2m, dfm, lower.tail = FALSE)), "\n")
} else {
  cat("Symmetry (generalised McNemar) chi-square = [not calculated - zero cells]  df = 0\n")
}
