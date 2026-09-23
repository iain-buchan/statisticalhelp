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
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
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
cat("95% confidence interval =", six(kappa - z * se), "to", six(kappa + z * se), "\n")
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

# Scott's pi: chance agreement from the margins averaged over the two tests
margin <- (row_p + col_p) / 2
spe <- sum(margin^2)
cat("Scott's pi\n")
cat("Observed agreement =", pc2(po), "\n")
cat("Expected agreement =", pc2(spe), "\n")
cat("Pi =", six((po - spe) / (1 - spe)), "\n")

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
cat("95% confidence interval =", six(ac1 - z * sqrt(v)), "to", six(ac1 + z * sqrt(v)),
    "\n")

# Maxwell's test of marginal homogeneity: the differences between the row and column
# totals of the first g - 1 grades against their covariance matrix
d <- (rowSums(counts) - colSums(counts))[-g]
V <- -(counts + t(counts))
diag(V) <- rowSums(counts) + colSums(counts) - 2 * diag(counts)
V <- V[-g, -g]
x2 <- as.numeric(t(d) %*% solve(V) %*% d)
cat("Marginal homogeneity (Maxwell) chi-square =", six(x2), " df =", g - 1, " ",
    pv(pchisq(x2, g - 1, lower.tail = FALSE)), "\n")

# The generalised McNemar (Bowker) test of symmetry, as mcnemar.test(counts) computes
# it, except that the pair of cells that are both empty (weak MAST, very high RAST and
# the reverse) is left out rather than making the statistic undefined
above <- counts[upper.tri(counts)]
below <- t(counts)[upper.tri(counts)]
filled <- above + below > 0
x2m <- sum((above - below)[filled]^2 / (above + below)[filled])
dfm <- g * (g - 1) / 2
cat("Symmetry (generalised McNemar) chi-square =", six(x2m), " df =", dfm, " ",
    pv(pchisq(x2m, dfm, lower.tail = FALSE)), "\n")
